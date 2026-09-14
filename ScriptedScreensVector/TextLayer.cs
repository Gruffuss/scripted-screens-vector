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

        var masking = mask.GetComponent<RectMask2D>();
        if (masking != null)
            masking.enabled = placement.ClipRect.HasValue;

        label.gameObject.SetActive(true);
        label.text = placement.Text;
        label.color = placement.Colour;
        label.fontSize = Mathf.Max(1f, placement.Size);
        label.characterSpacing = placement.CharSpacing;
        label.fontStyle = placement.Bold ? FontStyles.Bold : FontStyles.Normal;
        label.alignment = Alignment(placement);

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

        ApplyShadow(label, index, placement);
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
        if (placement.Shadow == null)
        {
            // Only touch the material if we are the ones who instanced it.
            if (_shadowed.Remove(index))
            {
                ShaderUtilities.GetShaderPropertyIDs();

                var current = label.fontMaterial;
                if (current != null)
                {
                    current.DisableKeyword(ShaderUtilities.Keyword_Underlay);
                    current.SetColor(ShaderUtilities.ID_UnderlayColor, Color.clear);
                }
            }

            return;
        }

        // The ID_* fields are lazily filled and are ZERO until this runs. Writing to shader
        // property 0 is a silent no-op, which would have made every text shadow simply not
        // appear with nothing in the log -- the same class of failure as the native ECalls
        // that bit this project twice. It self-guards, so calling it every time is free.
        ShaderUtilities.GetShaderPropertyIDs();

        var shadow = placement.Shadow.Value;
        var material = label.fontMaterial;
        var font = label.font;

        if (material == null || font == null || !material.HasProperty(ShaderUtilities.ID_UnderlayOffsetX))
        {
            // The face has no underlay in its shader. Silence here would be the whole bug
            // again, so it is said once per surface rather than per label per frame.
            Warn($"font \"{font?.name}\" has no underlay in its shader; text shadow not drawn");
            return;
        }

        var gradient = material.HasProperty(ShaderUtilities.ID_GradientScale)
            ? material.GetFloat(ShaderUtilities.ID_GradientScale)
            : 0f;

        var sampling = font.faceInfo.pointSize;
        var size = Mathf.Max(1f, placement.Size);

        if (gradient <= 0.001f || sampling <= 0f)
        {
            Warn($"font \"{font.name}\" reports no usable SDF scale; text shadow not drawn");
            return;
        }

        var fit = TextShadow.Fit(shadow, gradient, sampling, size);

        if (fit.Scale < 0.999f)
        {
            Warn($"text shadow is larger than the font's SDF padding allows; reduced to {fit.Scale * 100f:F0}% "
                 + "(use a smaller offset and blur, or a font atlas with more padding)");
        }

        material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
        material.SetColor(ShaderUtilities.ID_UnderlayColor, shadow.Colour);
        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, fit.OffsetX);
        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, fit.OffsetY);
        material.SetFloat(ShaderUtilities.ID_UnderlayDilate, Mathf.Clamp(fit.Dilate, -1f, 1f));
        material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, Mathf.Clamp01(fit.Softness));

        _shadowed.Add(index);
    }

    /// <summary>One of each distinct complaint, not one per label per frame.</summary>
    private readonly HashSet<string> _warned = new(StringComparer.Ordinal);

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
        _shadowed.Clear();
    }
}
