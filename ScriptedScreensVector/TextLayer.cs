using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ScriptedScreensVector;

/// <summary>
/// Turns the text placements a rebuild found into TMP children, and keeps them.
/// </summary>
/// <remarks>
/// Main thread only, driven when a tessellation job lands.
///
/// **Objects are pooled by index, not rebuilt.** The nth placement reuses the nth object.
/// Destroying and recreating them would give up TMP's own caching and put a per-frame
/// allocation where the point of the exercise was removing one.
///
/// Pooling by index is safe because <see cref="Show"/> reassigns **every** property of the
/// label it takes, so an object that served a different placement last rebuild carries
/// nothing forward. That is load bearing: without it, a node that stops being emitted would
/// shift every later placement onto an object still configured for something else. The font
/// was the one property that leaked, and it is reset explicitly below.
///
/// It is also why this is worth having at all: a ScriptedScreens `label` element costs about
/// 300 Lua instructions to declare and has to be re-declared to change its text, against a
/// string in a data payload here.
/// </remarks>
internal sealed class TextLayer
{
    private readonly List<TextMeshProUGUI> _pool = new();

    /// <summary>The face a fresh label starts with, restored when a placement names none.</summary>
    private TMP_FontAsset? _defaultFont;

    /// <summary>Labels currently carrying an instanced material because of a shadow.</summary>
    /// <remarks>
    /// Reading `fontMaterial` INSTANTIATES a material, which costs an extra draw call and
    /// breaks batching, so it is only touched for a label that actually has a shadow. This
    /// set is what lets one be turned off again: the pool reassigns every property, and a
    /// label reused for a placement with no shadow must not keep the previous occupant's.
    /// </remarks>
    private readonly HashSet<int> _shadowed = new();
    private readonly List<RectTransform> _masks = new();

    /// <summary>
    /// Offset copies that draw a text shadow too large for the label's own quad, by index.
    /// </summary>
    /// <remarks>
    /// Created only for a placement whose shadow needs one, inside the same clip object as
    /// its label and before it, so it clips and orders exactly as the label does. See
    /// <see cref="TextShadow.ShouldCast"/>.
    /// </remarks>
    private readonly List<TextMeshProUGUI?> _casters = new();

    /// <summary>Per placement: the enclosing groups' filters and mask, applied per glyph vertex.</summary>
    private readonly List<VertexTint?> _tints = new();
    private readonly Transform _parent;

    internal TextLayer(Transform parent)
    {
        _parent = parent;
    }

    /// <summary>Applies a rebuild's placements, reusing objects and hiding the surplus.</summary>
    internal void Apply(List<TextPlacement> placements)
    {
        for (var i = 0; i < placements.Count; i++)
            Show(i, placements[i]);

        // Surplus is disabled rather than destroyed: a scene that alternates between two
        // pages would otherwise churn objects every time it switched.
        for (var i = placements.Count; i < _pool.Count; i++)
        {
            if (_pool[i] != null)
                _pool[i].gameObject.SetActive(false);

            if (i < _casters.Count && _casters[i] != null)
                _casters[i]!.gameObject.SetActive(false);

            HideCopies(i, 0);
            if (i < _insets.Count && _insets[i] != null)
                HideCaster(_insets[i]!.Mask);

            // A hidden label keeps its material, and the pool may hand it to a placement with
            // no shadow later. Forget it now so that placement clears it.
            _shadowed.Remove(i);
        }
    }

    private void Show(int index, TextPlacement placement)
    {
        while (_pool.Count <= index)
            Create();

        var label = _pool[index];
        var mask = _masks[index];

        if (label == null || mask == null)
            return;

        // The mask carries the clip; the label sits inside it at the placement's rect. With
        // no clip the mask is made to match the label exactly, so it never cuts anything.
        var clip = placement.ClipRect ?? placement.Rect;

        Place(mask, clip);
        Place(label.rectTransform, placement.Rect);

        // The label's rect is expressed relative to the mask, since it is its child.
        label.rectTransform.anchoredPosition =
            new Vector2(placement.Rect.center.x - clip.center.x,
                        placement.Rect.center.y - clip.center.y);

        // A rectangle is RectMask2D's job. Any other outline goes to the stencil: a ClipShape
        // drawing it, under a Mask that hides the shape and keeps the cut. Added on first need,
        // since most labels never have one.
        var polygon = placement.ClipPolygon;
        var masking = mask.GetComponent<RectMask2D>();
        if (masking != null)
            masking.enabled = placement.ClipRect.HasValue && polygon == null;

        var shape = mask.GetComponent<ClipShape>();
        if (polygon != null && shape == null)
        {
            shape = mask.gameObject.AddComponent<ClipShape>();
            shape.raycastTarget = false;
            mask.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
        }

        if (shape != null)
        {
            if (polygon != null)
                shape.SetOutline(polygon, clip.center);

            shape.enabled = polygon != null;
            var stencil = mask.GetComponent<UnityEngine.UI.Mask>();
            if (stencil != null)
                stencil.enabled = polygon != null;
        }

        label.gameObject.SetActive(true);
        label.text = placement.Text;
        label.color = placement.Colour;
        label.fontSize = Mathf.Max(1f, placement.Size);
        label.characterSpacing = placement.CharSpacing;
        label.fontStyle = placement.Bold ? FontStyles.Bold : FontStyles.Normal;
        label.alignment = Alignment(placement);

        // Justified lines take their extra width in the gaps between words, as CSS does. TMP's
        // default (0.4) gives 40% of it to the letters, which spaced a short bold line out
        // letter by letter. Only a line with no gap at all still spreads its letters.
        label.wordWrappingRatios = 0f;

        // Single line unless the node asked otherwise. TMP wraps by default, and a label
        // that silently becomes two lines moves its own text off the baseline the scene
        // placed it on -- which looks like a bug in the console, not a long string.
        label.enableWordWrapping = placement.Wrap;

        // TMP's lineSpacing is a PERCENTAGE OFFSET from the font's natural line height, not
        // a multiplier, so `lh = 1` is 0 and `lh = 1.4` is +40. Written as a CSS-style
        // multiple because that is what an author porting a page already has in hand.
        label.lineSpacing = placement.LineHeight > 0.01f
            ? (placement.LineHeight - 1f) * 100f
            : 0f;

        // `ellipsis` and `shrink` are TMP's own overflow modes, so the fitting is done by the
        // text engine that knows the glyph metrics rather than guessed at here.
        switch (placement.Fit)
        {
            case TextFit.Ellipsis:
                label.enableAutoSizing = false;
                label.overflowMode = TextOverflowModes.Ellipsis;
                break;

            case TextFit.Shrink:
                label.enableAutoSizing = true;
                label.fontSizeMin = Mathf.Max(1f, placement.MinSize);
                label.fontSizeMax = Mathf.Max(1f, placement.Size);
                label.overflowMode = TextOverflowModes.Overflow;
                break;

            default:
                label.enableAutoSizing = false;
                label.overflowMode = TextOverflowModes.Overflow;
                break;
        }

        label.rectTransform.localRotation = Quaternion.Euler(0f, 0f, placement.Rotation);

        // Reset rather than "leave it alone", because the pool is by index: a placement with
        // no `font` must not inherit the face of whatever used this object last rebuild. This
        // was a real leak whenever placement ORDER changed -- a repeat whose count grew, a
        // scroll container gaining a row -- not only when a node disappeared.
        if (!string.IsNullOrEmpty(placement.Font))
            ApplyFont(label, placement.Font!);
        else if (_defaultFont != null && label.font != _defaultFont)
            label.font = _defaultFont;

        // Build the glyph geometry now if it has none. TMP normally marks itself dirty and
        // lets its own update manager build it on the canvas callback -- but a screen capture
        // runs us from INSIDE that callback, so a label touched there marks itself for a pass
        // already in progress and never gets built. Every later rebuild assigns the same text,
        // TMP sees no change, and it stays empty for good, which is why only a power cycle
        // healed it.
        //
        // NOT DONE HERE, and worth saying why: pushing the material and texture to the
        // CanvasRenderer by hand as well. That rendered every glyph as a solid block, and
        // because this condition is also true for any FRESHLY created label it broke ordinary
        // consoles that had never been captured at all. TMP owns its material; only the mesh
        // is ours to force.
        if (label.mesh != null && label.mesh.vertexCount == 0 && !string.IsNullOrEmpty(placement.Text))
            label.ForceMeshUpdate();

        // A graphic needs a mesh AND a material, and both are normally pushed to the
        // CanvasRenderer by a deferred rebuild -- the same one a screen capture is already
        // inside. Measured after a capture: "mesh=32 mats=0 mat=NONE", geometry present and
        // nothing to draw it with, unchanged across every later rebuild.
        //
        // These two lines are TextMeshPro's own UpdateMaterial, verbatim. It is protected, so
        // it cannot be called directly; copying its body is safer than improvising, and the
        // first attempt proved why. That version added SetTexture(mainTexture) as well, by
        // analogy with UGUI's base implementation -- but TMP deliberately does NOT, because
        // for distance-field text the atlas comes from the material. Overriding the renderer's
        // texture rendered every glyph as a solid block.
        var renderer = label.canvasRenderer;
        if (renderer != null && renderer.materialCount == 0 && label.fontSharedMaterial != null)
        {
            renderer.materialCount = 1;
            renderer.SetMaterial(label.materialForRendering, 0);
        }

        // Filters and masks reach glyphs through TMP's own vertex colours, rewritten in its
        // pre-render hook each time it builds the mesh. A tint that changed with unchanged text
        // does not make TMP rebuild by itself, so it is asked to.
        while (_tints.Count <= index)
            _tints.Add(null);

        while (_gradients.Count <= index)
            _gradients.Add(null);

        while (_shears.Count <= index)
            _shears.Add(null);

        var hadTint = _tints[index] != null || _gradients[index] != null || _shears[index] != null;
        _tints[index] = placement.Tint;
        _gradients[index] = placement.Gradient;
        _shears[index] = placement.Shear;
        ApplyFirstLine(label, index, placement);

        // The recolour happens in TMP's pre-render hook, so the mesh has to be regenerated for
        // it to run. Asking TMP to (havePropertiesChanged) relies on its deferred rebuild, which a
        // capture's rebuild loop swallows -- the colours were lost for good. Rebuilt here instead.
        if (placement.Tint != null || placement.Gradient != null || placement.Shear != null || hadTint)
            label.ForceMeshUpdate();

        ApplyShadow(label, index, placement);
    }

    /// <summary>Per placement: a skew or stretch applied to the glyph vertices.</summary>
    private readonly List<Vector4?> _shears = new();

    /// <summary>Per placement: a gradient fill, looked up per glyph vertex.</summary>
    private readonly List<TextGradient?> _gradients = new();

    /// <summary>Per placement: the text and style the first-line wrap was built for, and its result.</summary>
    private readonly List<(string Key, string Text)?> _firstLines = new();

    /// <summary>
    /// `::first-line`: rich tags around what TMP laid out as the first line.
    /// </summary>
    /// <remarks>
    /// Only the text engine knows where a line breaks, so the label is laid out once as written,
    /// the break read from `lineInfo[0]`, and the text set again with tags around that span.
    /// The tags can move the break themselves (a larger size wraps sooner), so the break is
    /// read once more and the wrap redone if it moved. Cached on the inputs that decide the
    /// layout, so an unchanged label costs nothing per rebuild.
    /// </remarks>
    private void ApplyFirstLine(TextMeshProUGUI label, int index, TextPlacement placement)
    {
        while (_firstLines.Count <= index)
            _firstLines.Add(null);

        var style = placement.FirstLine;
        if (style == null || string.IsNullOrEmpty(placement.Text))
        {
            _firstLines[index] = null;
            return;
        }

        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var key = string.Join("|", placement.Text, style.Colour?.ToString() ?? "", style.Size?.ToString(invariant) ?? "",
            style.Bold?.ToString() ?? "", style.Font ?? "", placement.Rect.width.ToString(invariant), placement.Size.ToString(invariant),
            placement.Wrap.ToString(), label.font != null ? label.font.name : "", placement.Bold.ToString(), placement.CharSpacing.ToString(invariant));

        if (_firstLines[index] is { } cached && cached.Key == key)
        {
            label.text = cached.Text;
            return;
        }

        var cut = FirstLineEnd(label, 0);
        var wrapped = style.Wrap(placement.Text, cut, out var prefix);
        label.text = wrapped;

        var moved = FirstLineEnd(label, prefix);
        if (moved >= 0 && moved != cut)
        {
            wrapped = style.Wrap(placement.Text, moved, out _);
            label.text = wrapped;
        }

        _firstLines[index] = (key, wrapped);
    }

    /// <summary>Lays the label out and returns where its first line ends in the unwrapped text.</summary>
    private static int FirstLineEnd(TextMeshProUGUI label, int prefix)
    {
        label.ForceMeshUpdate();
        var info = label.textInfo;
        if (info == null || info.lineCount == 0 || info.characterCount == 0)
            return -1;

        var last = Mathf.Clamp(info.lineInfo[0].lastCharacterIndex, 0, info.characterCount - 1);
        var character = info.characterInfo[last];
        return character.index + character.stringLength - prefix;
    }

    /// <summary>TMP's pre-render hook: tints this label's glyph vertices before upload.</summary>
    /// <remarks>
    /// Shadow copies pass <paramref name="fill"/> false: a gradient fill is the face's colour
    /// and has no business in a shadow, which filters and masks do reach.
    /// </remarks>
    private void TintGlyphs(int slot, TextMeshProUGUI label, TMP_TextInfo info, bool fill = true)
    {
        var tint = slot < _tints.Count ? _tints[slot] : null;
        var gradient = fill && slot < _gradients.Count ? _gradients[slot] : null;
        var shear = slot < _shears.Count ? _shears[slot] : null;
        if ((tint == null && gradient == null && shear == null) || info == null)
            return;

        var positioned = gradient != null || (tint != null && tint.NeedsPosition);

        for (var m = 0; m < info.materialCount && m < info.meshInfo.Length; m++)
        {
            var mesh = info.meshInfo[m];
            if (mesh.colors32 == null || mesh.vertices == null)
                continue;

            var count = Mathf.Min(mesh.vertexCount, mesh.colors32.Length);
            for (var i = 0; i < count; i++)
            {
                // A skew or stretch first, about the label's pivot: it is where the glyph really
                // is, and a gradient or mask must be sampled there.
                if (shear is { } k && i < mesh.vertices.Length)
                {
                    var p = mesh.vertices[i];
                    mesh.vertices[i] = new Vector3(k.x * p.x + k.y * p.y, k.z * p.x + k.w * p.y, p.z);
                }

                // Glyph vertices are in the label's space; a mask is written in the surface's.
                var at = positioned
                    ? _parent.InverseTransformPoint(label.transform.TransformPoint(mesh.vertices[i]))
                    : Vector3.zero;

                if (gradient == null && tint == null)
                    continue;

                Color colour = mesh.colors32[i];
                if (gradient != null)
                    colour *= gradient.At(at);

                mesh.colors32[i] = tint != null ? tint.Apply(colour, at) : colour;
            }

            // Kept on the label, so the copy a screen capture takes shows these colours too.
            if (m == 0)
            {
                var keeper = label.GetComponent<GlyphColours>();
                if (keeper == null)
                {
                    GlyphColours.Creating = true;
                    keeper = label.gameObject.AddComponent<GlyphColours>();
                    GlyphColours.Creating = false;
                }

                keeper.Store(mesh.colors32, mesh.vertices, count);
            }
        }
    }

    /// <summary>Drives the text engine's underlay from a `sh` entry.</summary>
    /// <remarks>
    /// **Text cannot take the geometric shadow every other shape gets.** That one stacks
    /// contours carrying a blurred edge's coverage, and a glyph has no contour here -- the
    /// text engine builds its own mesh on its own object, above ours. The underlay is the
    /// same idea done inside the SDF shader, which is the only place the glyph's silhouette
    /// is known.
    ///
    /// **Units.** Underlay offset, dilate and softness are normalised against the glyph's SDF
    /// padding, not measured in pixels: 1.0 is the whole padding, which is `gradientScale`
    /// atlas texels, and one texel covers `fontSize / samplingPointSize` rendered units. So
    /// the conversion out of canvas units is
    /// `canvas * samplingPointSize / (gradientScale * fontSize)`. Read out of the decompiled
    /// `ShaderUtilities.GetPadding` and `UpdateShaderRatios` rather than guessed.
    ///
    /// **The budget is real and it bites.** The shader renormalises whenever
    /// `max(|dx|,|dy|) + dilate + softness` exceeds 1, so an over-large shadow is not clipped,
    /// it is silently SHRUNK -- and everything else shrinks with it. Rather than let that
    /// happen invisibly, the whole request is scaled down together here and the fact is
    /// reported, so the shape stays right and the cause is named.
    /// </remarks>
    private void ApplyShadow(TextMeshProUGUI label, int index, TextPlacement placement)
    {
        // First shadow before the rest, so the ordering below sees every copy that exists.
        ApplyFirstShadow(label, index, placement);
        ApplyExtraShadows(label, index, placement);
    }

    private void ApplyFirstShadow(TextMeshProUGUI label, int index, TextPlacement placement)
    {
        var caster = index < _casters.Count ? _casters[index] : null;

        if (placement.Shadow == null)
        {
            ClearUnderlay(label, index);
            HideCaster(caster);
            return;
        }

        // The ID_* fields are lazily filled and are ZERO until this runs. Writing to shader
        // property 0 is a silent no-op, which would have made every text shadow simply not
        // appear with nothing in the log -- the same class of failure as the native ECalls
        // that bit this project twice. It self-guards, so calling it every time is free.
        ShaderUtilities.GetShaderPropertyIDs();

        var shadow = placement.Shadow.Value;
        var font = label.font;

        // Read from the SHARED material: reading `fontMaterial` instances one, and a label whose
        // shadow ends up on its caster should not pay for an instance it never uses.
        var shared = label.fontSharedMaterial;

        if (shared == null || font == null || !shared.HasProperty(ShaderUtilities.ID_UnderlayOffsetX))
        {
            // The face has no underlay in its shader. Silence here would be the whole bug
            // again, so it is said once per surface rather than per label per frame.
            Warn($"font \"{font?.name}\" has no underlay in its shader; text shadow not drawn");
            return;
        }

        var gradient = shared.HasProperty(ShaderUtilities.ID_GradientScale)
            ? shared.GetFloat(ShaderUtilities.ID_GradientScale)
            : 0f;

        var sampling = font.faceInfo.pointSize;
        var size = Mathf.Max(1f, placement.Size);

        if (gradient <= 0.001f || sampling <= 0f)
        {
            Warn($"font \"{font.name}\" reports no usable SDF scale; text shadow not drawn");
            return;
        }

        var cast = TextShadow.ShouldCast(shadow, gradient, sampling, size, out var fit);

        if (fit.Scale < 0.999f)
        {
            Warn($"text shadow is larger than the font's SDF padding allows; reduced to {fit.Scale * 100f:F0}% "
                 + "(use a smaller blur, or a font atlas with more padding)");
        }

        if (cast)
        {
            // The shadow moves to an offset copy; the label itself draws none.
            ClearUnderlay(label, index);
            caster = EnsureCaster(index);
            Mirror(caster, label, shadow);
            WriteUnderlay(caster, fit, shadow.Colour, hideFace: true);
            return;
        }

        HideCaster(caster);
        WriteUnderlay(label, fit, shadow.Colour, hideFace: false);
        _shadowed.Add(index);
    }

    /// <summary>Copies of each label drawing its second and later outset shadows, by index.</summary>
    private readonly List<List<TextMeshProUGUI>?> _copies = new();

    /// <summary>
    /// Second and later outset shadows, each on a copy of the label; then the inset one.
    /// </summary>
    /// <remarks>
    /// The label's own underlay is one layer and already serves the first shadow, so every
    /// further one needs another TMP object. Outset copies go under the label, the last written
    /// lowest as CSS stacks them. The inset shadow goes over the label; see <see cref="ApplyInset"/>.
    /// </remarks>
    private void ApplyExtraShadows(TextMeshProUGUI label, int index, TextPlacement placement)
    {
        var extras = placement.ExtraShadows;
        var used = 0;

        if (extras != null && ShadowMetrics(label, out var gradient, out var sampling))
        {
            while (_copies.Count <= index)
                _copies.Add(null);

            var list = _copies[index] ??= new List<TextMeshProUGUI>();

            foreach (var shadow in extras)
            {
                var fit = TextShadow.Fit(new VecShadow(0f, 0f, shadow.Blur, shadow.Spread, shadow.Colour), gradient, sampling, Mathf.Max(1f, placement.Size));
                ReportShrink(fit);

                if (list.Count <= used)
                    list.Add(NewCopy(index, "VecTextShadowExtra"));

                var copy = list[used++];
                copy.gameObject.SetActive(true);
                Mirror(copy, label, shadow);
                WriteUnderlay(copy, fit, shadow.Colour, hideFace: true);

                if (NeedsHook(index))
                    copy.ForceMeshUpdate();
            }

            // CSS paints the first shadow on top: the last copy goes lowest.
            for (var k = 0; k < used; k++)
                list[k].rectTransform.SetSiblingIndex(0);
        }

        HideCopies(index, used);

        ApplyInset(label, index, placement);
    }

    /// <summary>The three objects drawing one label's inset shadow.</summary>
    private sealed class InsetParts
    {
        /// <summary>A copy of the label that is a stencil Mask: its glyphs are the only place the other two draw.</summary>
        internal TextMeshProUGUI Mask = null!;

        /// <summary>The glyphs again, in the shadow colour.</summary>
        internal TextMeshProUGUI Shade = null!;

        /// <summary>The face moved by the offset, softened and shrunk by blur and spread, over the shade.</summary>
        internal TextMeshProUGUI Face = null!;
    }

    /// <summary>Per placement: its inset shadow's objects, created on first need.</summary>
    private readonly List<InsetParts?> _insets = new();

    /// <summary>
    /// An inset text shadow, from stencil and copies.
    /// </summary>
    /// <remarks>
    /// TMP's own inner underlay (`UNDERLAY_INNER`) is declared by the game's text shaders but does
    /// not draw: four arrangements of it were put on a console on 2026-09-15 -- a copy with its face
    /// hidden and visible, the label itself, and the desktop shader -- and none showed anything,
    /// with the keyword confirmed on in every one. The variant is not in the build.
    ///
    /// So it is composed from things that do draw. Inside the label's glyphs (a copy used as a
    /// stencil Mask, the same mechanism as a rounded clip), the glyphs are painted in the shadow
    /// colour, and the face is painted back over them moved by the offset. Where the moved face
    /// does not reach -- a band along the edges facing away from the offset -- the shadow shows.
    /// Blur softens the moved face's edge and spread shrinks it, both in the font's own terms, so
    /// they are capped by its padding like every text shadow; the offset is a position and is not.
    /// </remarks>
    private void ApplyInset(TextMeshProUGUI label, int index, TextPlacement placement)
    {
        while (_insets.Count <= index)
            _insets.Add(null);

        var parts = _insets[index];

        if (placement.InsetShadow is not { } inset || !ShadowMetrics(label, out var gradient, out var sampling))
        {
            if (parts != null)
                HideCaster(parts.Mask);

            return;
        }

        var fit = TextShadow.Fit(new VecShadow(0f, 0f, inset.Blur, inset.Spread, inset.Colour), gradient, sampling, Mathf.Max(1f, placement.Size));
        ReportShrink(fit);

        parts ??= _insets[index] = NewInset(index);

        var mask = parts.Mask;
        mask.gameObject.SetActive(true);
        Mirror(mask, label, new VecShadow(0f, 0f, 0f, 0f, Color.clear));
        mask.color = Color.white;
        mask.rectTransform.SetAsLastSibling();

        Inside(parts.Shade, label, 0f, 0f);
        var shade = inset.Colour;
        shade.a *= label.color.a;
        parts.Shade.color = shade;

        Inside(parts.Face, label, inset.Dx, inset.Dy);
        parts.Face.color = label.color;

        var material = parts.Face.fontMaterial;
        if (material != null)
        {
            if (material.HasProperty(ShaderUtilities.ID_FaceDilate))
                material.SetFloat(ShaderUtilities.ID_FaceDilate, Mathf.Clamp(-fit.Dilate, -1f, 1f));

            if (material.HasProperty(ShaderUtilities.ID_OutlineSoftness))
                material.SetFloat(ShaderUtilities.ID_OutlineSoftness, Mathf.Clamp01(fit.Softness));

            ShaderUtilities.UpdateShaderRatios(material);
            parts.Face.UpdateMeshPadding();
        }

        // A colour set after the glyphs were built only recolours them; the pre-render hook does
        // not run, so the colours it keeps for screen captures stayed as they were at creation --
        // a white shade, which is no band at all. Rebuilt here when that hook does something.
        if (NeedsHook(index))
        {
            mask.ForceMeshUpdate();
            parts.Shade.ForceMeshUpdate();
            parts.Face.ForceMeshUpdate();
        }
    }

    /// <summary>True when this placement's glyphs go through the pre-render hook: a tint, a gradient or a skew.</summary>
    private bool NeedsHook(int index)
    {
        return (index < _tints.Count && _tints[index] != null)
               || (index < _gradients.Count && _gradients[index] != null)
               || (index < _shears.Count && _shears[index] != null);
    }

    /// <summary>A copy of the label inside the inset mask, moved by an offset in canvas units.</summary>
    private static void Inside(TextMeshProUGUI copy, TextMeshProUGUI label, float dx, float dy)
    {
        copy.gameObject.SetActive(true);
        Mirror(copy, label, new VecShadow(0f, 0f, 0f, 0f, Color.clear));

        // A child of the mask, which already sits where the label does and turns with it.
        copy.rectTransform.localRotation = Quaternion.identity;
        copy.rectTransform.localScale = Vector3.one;
        copy.rectTransform.anchoredPosition = new Vector2(dx, -dy);
    }

    private InsetParts NewInset(int index)
    {
        var mask = NewCopy(index, "VecTextInsetMask");
        mask.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;

        var shade = NewCopy(index, "VecTextInsetShade");
        shade.rectTransform.SetParent(mask.rectTransform, worldPositionStays: false);

        var face = NewCopy(index, "VecTextInsetFace", fill: true);
        face.rectTransform.SetParent(mask.rectTransform, worldPositionStays: false);

        return new InsetParts { Mask = mask, Shade = shade, Face = face };
    }

    private void HideCopies(int index, int from)
    {
        if (index >= _copies.Count || _copies[index] == null)
            return;

        var list = _copies[index]!;
        for (var k = from; k < list.Count; k++)
            HideCaster(list[k]);
    }

    private TextMeshProUGUI NewCopy(int index, string name, bool fill = false)
    {
        var host = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        var copy = host.GetComponent<TextMeshProUGUI>();
        copy.rectTransform.SetParent(_masks[index], worldPositionStays: false);
        copy.raycastTarget = false;
        copy.richText = true;

        // Group filters and masks reach the shadow the way they reach the label.
        copy.OnPreRenderText += info => TintGlyphs(index, copy, info, fill);
        return copy;
    }

    /// <summary>The font's SDF scale and sampling size, or false (and a warning) when it has none.</summary>
    private bool ShadowMetrics(TextMeshProUGUI label, out float gradient, out float sampling)
    {
        ShaderUtilities.GetShaderPropertyIDs();
        gradient = 0f;
        sampling = 0f;

        var shared = label.fontSharedMaterial;
        var font = label.font;
        if (shared == null || font == null || !shared.HasProperty(ShaderUtilities.ID_UnderlayOffsetX))
        {
            Warn($"font \"{font?.name}\" has no underlay in its shader; text shadow not drawn");
            return false;
        }

        gradient = shared.HasProperty(ShaderUtilities.ID_GradientScale) ? shared.GetFloat(ShaderUtilities.ID_GradientScale) : 0f;
        sampling = font.faceInfo.pointSize;

        if (gradient > 0.001f && sampling > 0f)
            return true;

        Warn($"font \"{font.name}\" reports no usable SDF scale; text shadow not drawn");
        return false;
    }

    private void ReportShrink(TextShadowFit fit)
    {
        if (fit.Scale < 0.999f)
        {
            Warn($"text shadow is larger than the font's SDF padding allows; reduced to {fit.Scale * 100f:F0}% "
                 + "(use a smaller blur, or a font atlas with more padding)");
        }
    }

    /// <summary>Turns a label's underlay off, if this layer is the one that turned it on.</summary>
    private void ClearUnderlay(TextMeshProUGUI label, int index)
    {
        // Only touch the material if we are the ones who instanced it.
        if (!_shadowed.Remove(index))
            return;

        ShaderUtilities.GetShaderPropertyIDs();

        var current = label.fontMaterial;
        if (current == null)
            return;

        current.DisableKeyword(ShaderUtilities.Keyword_Underlay);
        current.SetColor(ShaderUtilities.ID_UnderlayColor, Color.clear);
        label.UpdateMeshPadding();
    }

    /// <summary>Writes a fitted underlay onto a label's own material instance.</summary>
    private static void WriteUnderlay(TextMeshProUGUI target, TextShadowFit fit, Color colour, bool hideFace)
    {
        var material = target.fontMaterial;
        if (material == null)
            return;

        material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
        material.SetColor(ShaderUtilities.ID_UnderlayColor, colour);
        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, fit.OffsetX);
        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, fit.OffsetY);
        material.SetFloat(ShaderUtilities.ID_UnderlayDilate, Mathf.Clamp(fit.Dilate, -1f, 1f));
        material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, Mathf.Clamp01(fit.Softness));

        // A caster shows only its underlay. The face is hidden through the MATERIAL's face
        // colour rather than the label's vertex colour, so nothing that multiplies by vertex
        // alpha can take the underlay down with it.
        if (hideFace && material.HasProperty(ShaderUtilities.ID_FaceColor))
            material.SetColor(ShaderUtilities.ID_FaceColor, new Color(1f, 1f, 1f, 0f));

        // TMP caches each glyph quad's padding and only recomputes it when told. Writing the
        // underlay straight onto the material does not tell it, so every quad kept the tight
        // padding it had before the shadow existed and the glow was cut off exactly at each
        // letter's edge. GetPadding does account for the underlay (read in the decompiled
        // ShaderUtilities); it simply was never being asked again.
        target.UpdateMeshPadding();
    }

    private TextMeshProUGUI EnsureCaster(int index)
    {
        while (_casters.Count <= index)
            _casters.Add(null);

        var caster = _casters[index];
        if (caster != null)
        {
            caster.gameObject.SetActive(true);
            return caster;
        }

        var host = new GameObject("VecTextShadow", typeof(RectTransform), typeof(TextMeshProUGUI));
        caster = host.GetComponent<TextMeshProUGUI>();
        caster.rectTransform.SetParent(_masks[index], worldPositionStays: false);

        // Before the label, so the label draws over its own shadow.
        caster.rectTransform.SetSiblingIndex(0);
        caster.raycastTarget = false;
        caster.richText = true;
        var slot = index;
        caster.OnPreRenderText += info => TintGlyphs(slot, caster, info, fill: false);

        _casters[index] = caster;
        return caster;
    }

    private static void HideCaster(TextMeshProUGUI? caster)
    {
        if (caster != null && caster.gameObject.activeSelf)
            caster.gameObject.SetActive(false);
    }

    /// <summary>Makes a caster an exact copy of its label, moved by the shadow's offset.</summary>
    private static void Mirror(TextMeshProUGUI caster, TextMeshProUGUI label, VecShadow shadow)
    {
        // Font first: assigning one resets the shared material, and the underlay is written
        // onto a fresh instance afterwards.
        if (caster.font != label.font)
            caster.font = label.font;

        caster.text = label.text;
        caster.fontSize = label.fontSize;
        caster.characterSpacing = label.characterSpacing;
        caster.fontStyle = label.fontStyle;
        caster.alignment = label.alignment;
        caster.wordWrappingRatios = label.wordWrappingRatios;
        caster.enableWordWrapping = label.enableWordWrapping;
        caster.lineSpacing = label.lineSpacing;
        caster.overflowMode = label.overflowMode;
        caster.enableAutoSizing = label.enableAutoSizing;
        caster.fontSizeMin = label.fontSizeMin;
        caster.fontSizeMax = label.fontSizeMax;
        caster.color = new Color(1f, 1f, 1f, label.color.a);

        var from = label.rectTransform;
        var to = caster.rectTransform;
        to.anchorMin = from.anchorMin;
        to.anchorMax = from.anchorMax;
        to.pivot = from.pivot;
        to.sizeDelta = from.sizeDelta;
        to.localRotation = from.localRotation;
        to.localScale = from.localScale;

        // Canvas units, and scenes are +Y down while the canvas is +Y up.
        to.anchoredPosition = from.anchoredPosition + new Vector2(shadow.Dx, -shadow.Dy);

        // The same two capture guards the label takes in Show, for the same reason.
        if (caster.mesh != null && caster.mesh.vertexCount == 0 && !string.IsNullOrEmpty(caster.text))
            caster.ForceMeshUpdate();

        var renderer = caster.canvasRenderer;
        if (renderer != null && renderer.materialCount == 0 && caster.fontSharedMaterial != null)
        {
            renderer.materialCount = 1;
            renderer.SetMaterial(caster.materialForRendering, 0);
        }
    }

    /// <summary>One of each distinct complaint, not one per label per frame.</summary>
    private readonly HashSet<string> _warned = new(StringComparer.Ordinal);

    /// <summary>Everything this layer has warned about, for the vector_stats report.</summary>
    /// <remarks>
    /// The warnings used to reach the BepInEx log only. `vector_stats` printed "no problems"
    /// for a whole evening while every text shadow on a page was being shrunk to 43% of what
    /// it asked for -- reported, and read by nobody, which is silent in practice.
    /// </remarks>
    internal IReadOnlyCollection<string> Warnings => _warned;

    private void Warn(string message)
    {
        if (_warned.Add(message))
            ScriptedScreensVectorPlugin.Log?.LogWarning($"vector text: {message}");
    }

    /// <summary>
    /// Resolves a family name through TMP's own registry, which is what the companion fonts
    /// mod populates. An unknown name leaves the current face rather than blanking the label.
    /// </summary>
    private static void ApplyFont(TextMeshProUGUI label, string family)
    {
        if (label.font != null && label.font.name == family)
            return;

        if (MaterialReferenceManager.TryGetFontAsset(TMP_TextUtilities.GetSimpleHashCode(family), out var asset))
            label.font = asset;
    }

    private static void Place(RectTransform rect, Rect where)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(where.width, where.height);
        rect.anchoredPosition = where.center;
    }

    private static TextAlignmentOptions Alignment(TextPlacement placement)
    {
        return (placement.VAlign, placement.Align) switch
        {
            (0, 0) => TextAlignmentOptions.TopLeft,
            (0, 1) => TextAlignmentOptions.Top,
            (0, 2) => TextAlignmentOptions.TopRight,
            (1, 0) => TextAlignmentOptions.Left,
            (1, 1) => TextAlignmentOptions.Center,
            (1, 2) => TextAlignmentOptions.Right,
            (2, 0) => TextAlignmentOptions.BottomLeft,
            (2, 1) => TextAlignmentOptions.Bottom,
            (0, 3) => TextAlignmentOptions.TopJustified,
            (1, 3) => TextAlignmentOptions.Justified,
            (2, 3) => TextAlignmentOptions.BottomJustified,
            _ => TextAlignmentOptions.BottomRight,
        };
    }

    /// <summary>The clip object the nth placement is drawn inside, for ordering siblings.</summary>
    /// <remarks>
    /// The mask is the child of the surface; the label is a child of the mask. So draw order
    /// is decided by the mask's sibling index, not the label's.
    /// </remarks>
    internal Transform? MaskFor(int index)
    {
        if (index < 0 || index >= _masks.Count)
            return null;

        return _masks[index] == null ? null : _masks[index];
    }

    private void Create()
    {
        var maskObject = new GameObject("VecTextClip", typeof(RectTransform), typeof(RectMask2D));
        var mask = maskObject.GetComponent<RectTransform>();
        mask.SetParent(_parent, worldPositionStays: false);
        maskObject.GetComponent<RectMask2D>().enabled = false;

        var labelObject = new GameObject("VecText", typeof(RectTransform), typeof(TextMeshProUGUI));
        var label = labelObject.GetComponent<TextMeshProUGUI>();
        label.rectTransform.SetParent(mask, worldPositionStays: false);
        label.raycastTarget = false;
        label.richText = true;

        var slot = _pool.Count;
        label.OnPreRenderText += info => TintGlyphs(slot, label, info);

        _defaultFont ??= label.font;

        _masks.Add(mask);
        _pool.Add(label);
    }

    internal void Destroy()
    {
        foreach (var mask in _masks)
        {
            if (mask != null)
                UnityEngine.Object.Destroy(mask.gameObject);
        }

        _masks.Clear();
        _pool.Clear();
        _casters.Clear();
        _copies.Clear();
        _insets.Clear();
        _gradients.Clear();
        _shears.Clear();
        _firstLines.Clear();
        _shadowed.Clear();
    }
}
