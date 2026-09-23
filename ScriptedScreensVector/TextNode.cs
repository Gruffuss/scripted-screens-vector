using System.Collections.Generic;
using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>Where a `T` node ended up, in canvas space, after the tree walk.</summary>
/// <remarks>
/// Text cannot be part of the mesh. TMP builds its own geometry on its own GameObject and is
/// main-thread only, while tessellation runs on a worker — so the walk **collects** these and
/// <see cref="VectorGraphic"/> turns them into TMP children when the job lands.
///
/// That split is the whole design of text here, and it has two consequences worth knowing:
/// text updates at the rebuild rate rather than instantly, and a text node cannot be clipped
/// by the geometric clipper, which reshapes contours before triangulation and has nothing to
/// say about a child object.
/// </remarks>
internal struct TextPlacement
{
    internal string Text;
    internal Rect Rect;          // canvas space, already transformed
    internal float Size;
    internal Color Colour;
    internal int Align;          // 0 left, 1 centre, 2 right
    internal int VAlign;         // 0 top, 1 middle, 2 bottom
    internal string? Font;
    internal bool Bold;
    internal float CharSpacing;
    internal int Fit;            // 0 none, 1 ellipsis, 2 shrink
    internal float MinSize;
    internal float Rotation;     // degrees, from the group transform
    internal Rect? ClipRect;     // canvas-space bounds of the enclosing clip, if any
    internal bool Wrap;          // may run to more than one line
    internal float LineHeight;   // multiple of the font size; 0 means the font's own
    internal VecShadow? Shadow;  // first outset `sh` entry, in canvas units; see TextLayer
    internal float OutlineWidth; // `ow` in canvas units, centred on the glyph edge; 0 for none
    internal Color OutlineColour;

    /// <summary>Outset shadows after the first, each drawn by a copy of the label.</summary>
    internal VecShadow[]? ExtraShadows;

    /// <summary>An inset shadow, drawn inside the glyphs by a copy over the label.</summary>
    internal VecShadow? InsetShadow;

    /// <summary>
    /// The clip's true outline in canvas space when it is not an axis-aligned rectangle: a
    /// rounded or rotated box, an ellipse, a concave polygon. Null when <see cref="ClipRect"/>
    /// says everything. TextLayer masks with a stencil for these.
    /// </summary>
    internal List<Vector2>? ClipPolygon;

    /// <summary>Filters and mask of the enclosing groups, applied per glyph vertex.</summary>
    internal VertexTint? Tint;

    /// <summary>
    /// What the group's transform does to the text beyond rotating and uniformly scaling it --
    /// a skew, or a stretch along one axis -- as a 2x2 matrix (m00, m01, m10, m11) in the label's
    /// own space, +Y up. Applied to the glyph vertices. Null when there is none.
    /// </summary>
    internal Vector4? Shear;

    /// <summary>`f=@gradient`: sampled at every glyph vertex. Null for a flat colour.</summary>
    internal TextGradient? Gradient;

    /// <summary>`fl`: first-line overrides, with size already in canvas units.</summary>
    internal FirstLineStyle? FirstLine;

    /// <summary>How many shapes had been emitted when this label was collected.</summary>
    /// <remarks>Where the label sits in draw order; see <see cref="TextOrder"/>.</remarks>
    internal int ShapeIndex;

    /// <summary>Vertex count this label must be drawn before, when a shape covers it.</summary>
    internal int CutVertex;

    /// <summary>How many meshes draw before this label. Set by <see cref="TextOrder"/>.</summary>
    internal int SliceDepth;

    /// <summary>
    /// True when applying <paramref name="other"/> to a label already showing this placement
    /// would change nothing about it.
    /// </summary>
    /// <remarks>
    /// Every field the text layer writes onto the label, and nothing else: `ShapeIndex`,
    /// `CutVertex` and `SliceDepth` decide where the label sits among the mesh slices, which is
    /// ordered separately every rebuild. Text compares by value, so a changed number always
    /// applies. The reference-typed fields compare by reference, which is deliberately
    /// cautious: a clip polygon, tint, gradient or first-line style is built fresh on each
    /// rebuild, so a label carrying one is never skipped rather than risking a stale one.
    /// </remarks>
    internal readonly bool SameAs(in TextPlacement other)
    {
        return string.Equals(Text, other.Text, System.StringComparison.Ordinal)
               && Rect == other.Rect
               && Size == other.Size
               && Colour == other.Colour
               && Align == other.Align
               && VAlign == other.VAlign
               && string.Equals(Font, other.Font, System.StringComparison.Ordinal)
               && Bold == other.Bold
               && CharSpacing == other.CharSpacing
               && Fit == other.Fit
               && MinSize == other.MinSize
               && Rotation == other.Rotation
               && ClipRect == other.ClipRect
               && Wrap == other.Wrap
               && LineHeight == other.LineHeight
               && OutlineWidth == other.OutlineWidth
               && OutlineColour == other.OutlineColour
               && Same(Shadow, other.Shadow)
               && Same(InsetShadow, other.InsetShadow)
               && Shear == other.Shear
               && ReferenceEquals(ExtraShadows, other.ExtraShadows)
               && ReferenceEquals(ClipPolygon, other.ClipPolygon)
               && ReferenceEquals(Tint, other.Tint)
               && ReferenceEquals(Gradient, other.Gradient)
               && ReferenceEquals(FirstLine, other.FirstLine);

        static bool Same(VecShadow? a, VecShadow? b)
        {
            if (a.HasValue != b.HasValue)
                return false;

            if (!a.HasValue)
                return true;

            var x = a.Value;
            var y = b!.Value;
            return x.Dx == y.Dx && x.Dy == y.Dy && x.Blur == y.Blur && x.Spread == y.Spread
                   && x.Colour == y.Colour && x.Inset == y.Inset;
        }
    }
}

/// <summary>Fit modes for <c>T</c>.</summary>
internal static class TextFit
{
    internal const int None = 0;
    internal const int Ellipsis = 1;
    internal const int Shrink = 2;

    internal static int Parse(string? value)
    {
        return value?.ToUpperInvariant() switch
        {
            "ELLIPSIS" => Ellipsis,
            "SHRINK" => Shrink,
            _ => None,
        };
    }
}

internal static class TextAlign
{
    internal static int Horizontal(string? value)
    {
        return value?.ToUpperInvariant() switch
        {
            "CENTER" or "CENTRE" => 1,
            "RIGHT" => 2,
            "JUSTIFIED" or "JUSTIFY" => 3,
            _ => 0,
        };
    }

    internal static int Vertical(string? value)
    {
        return value?.ToUpperInvariant() switch
        {
            "MIDDLE" or "CENTER" or "CENTRE" => 1,
            "BOTTOM" => 2,
            _ => 0,
        };
    }
}


/// <summary>A clickable node's bounds, in canvas space, in draw order.</summary>
/// <remarks>
/// Bounds rather than the tessellated shape. The walk knows each node's outline, but keeping
/// every contour alive past the rebuild to hit-test against later is a lot of memory for a
/// test that is only ever run on a click — and a row, a button and a tile, which is what
/// carries `click`, are rectangles whose bounds are their shape.
/// </remarks>
/// <summary>An `IMG` the walk met: drawn as shape <see cref="ShapeIndex"/>, or -1 when its texture is not here yet.</summary>
internal struct ImagePlacement
{
    internal string Src;
    internal int ShapeIndex;
}

internal struct HitRegion
{
    internal string Id;

    /// <summary>Bounds of <see cref="Outline"/>, for a cheap first test.</summary>
    internal Rect Rect;

    /// <summary>The shape's own outline in canvas space.</summary>
    internal Vector2[] Outline;

    /// <summary>The clip it is drawn through, in canvas space, or null when unclipped.</summary>
    internal Vector2[]? Clip;

    /// <summary>
    /// True when a canvas point is on the drawn shape: inside its outline and inside its clip.
    /// </summary>
    /// <remarks>
    /// This was the bounding rectangle alone, so a circle took clicks in its corners, a turned
    /// shape took them across its upright bounds, and a clipped one took them where the clip had
    /// cut it away -- the empty corners around a star-shaped clip, or a list row scrolled out of
    /// sight above its container. A browser only hits what is drawn.
    /// </remarks>
    internal bool Contains(Vector2 point)
    {
        return Rect.Contains(point)
               && (Outline == null || Outline.Length < 3 || Inside(Outline, point))
               && (Clip == null || Clip.Length < 3 || Inside(Clip, point));
    }

    /// <summary>Even-odd point in polygon; right for any simple outline, convex or not.</summary>
    internal static bool Inside(Vector2[] polygon, Vector2 point)
    {
        var inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            if ((a.y > point.y) != (b.y > point.y)
                && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}

/// <summary>Where an <c>SC</c> container landed, and how much room it has to scroll.</summary>
/// <remarks>
/// The offset itself is **client-side state and never leaves the client**. Scrolling a list
/// is not a decision the chip needs to hear about, and routing it through the tick would make
/// a wheel notch cost half a second and 50,000 instructions' worth of contention. So the
/// tessellator records where each container ended up, the main thread moves the offset when a
/// wheel or drag lands inside one, and the next rebuild reads it back.
///
/// Heights are scene units; <see cref="Rect"/> is canvas units, which is what a pointer
/// position arrives in. <see cref="ToScene"/> converts between them for drags.
/// </remarks>
internal struct ScrollRegion
{
    internal string Id;
    internal Rect Rect;
    internal float View;
    internal float Content;
    internal float ToScene;

    /// <summary>Scene units of content hidden below the viewport.</summary>
    internal float Max => Mathf.Max(0f, Content - View);

    internal float Clamp(float offset) => Mathf.Clamp(offset, 0f, Max);

    /// <summary>A fifth of the viewport per wheel notch, so the step suits the container.</summary>
    internal float WheelStep => Mathf.Max(1f, View * 0.2f);
}


/// <summary>Translates a printf conversion into the .NET format string it corresponds to.</summary>
/// <remarks>
/// A Lua author already knows printf, because that is what <c>string.format</c> takes. Asking
/// them to learn a second dialect for the same job would be gratuitous, so <c>fmt = "%.1f"</c>
/// is what a `T` node accepts and this turns it into <c>{0:F1}</c>.
///
/// Only the numeric conversions are handled, with their precision. Width and flags are not: a
/// console lays text out with <c>align</c> inside a box, not by padding with spaces. A spec
/// this cannot read is returned unchanged, so it reaches <c>string.Format</c> and fails there
/// as a caught FormatException rather than silently formatting the wrong thing.
/// </remarks>
/// <summary>One piece of a `T` text template: a literal, or a bound value with its own format.</summary>
internal readonly struct TextPart
{
    /// <summary>The literal text; null for a binding.</summary>
    internal readonly string? Literal;

    internal readonly string? Name;
    internal readonly Expression? Index;

    /// <summary>The printf format translated for .NET; null prints "0.##".</summary>
    internal readonly string? Net;

    /// <summary>`Net` as one conversion between literals, when it is one.</summary>
    internal readonly bool Split;
    internal readonly string Prefix;
    internal readonly string? Spec;
    internal readonly string Suffix;

    private TextPart(string? literal, string? name, Expression? index, string? format)
    {
        Literal = literal;
        Name = name;
        Index = index;
        Net = format == null ? null : Printf.ToNet(format);
        Prefix = Suffix = string.Empty;
        Spec = "0.##";
        Split = Net == null || Printf.TrySplit(Net, out Prefix, out Spec, out Suffix);
    }

    internal static TextPart Text(string literal) => new(literal, null, null, null);

    internal static TextPart Binding(string name, Expression? index, string? format) => new(null, name, index, format);
}

internal static class Printf
{
    private static readonly System.Collections.Generic.Dictionary<string, string> Cache =
        new(System.StringComparer.Ordinal);

    internal static string ToNet(string spec)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(spec, out var cached))
                return cached;
        }

        var result = Translate(spec);

        lock (Cache)
        {
            // Bounded and cleared wholesale: a scene inventing a new format every tick is not
            // what this is for, and tracking use order would cost more than the translation.
            if (Cache.Count > 64)
                Cache.Clear();

            Cache[spec] = result;
        }

        return result;
    }

    /// <summary>
    /// Splits a translated format into its literal prefix, its one conversion and its literal
    /// suffix, so the number can be written into a buffer rather than through `string.Format`.
    /// </summary>
    /// <remarks>
    /// Refuses anything that is not exactly one `{0:SPEC}` between braceless literals -- a
    /// literal brace in the author's text, or a format with no conversion -- so those keep the
    /// original formatter and cannot print differently than before.
    /// </remarks>
    internal static bool TrySplit(string composite, out string prefix, out string? spec, out string suffix)
    {
        // Cached like the translation itself: cutting three substrings on every call was 32 bytes
        // a label per rebuild, the whole of what was left after the buffer took the formatting.
        (bool Ok, string Prefix, string? Spec, string Suffix) split;
        lock (Splits)
        {
            if (!Splits.TryGetValue(composite, out split))
            {
                split = Split(composite);
                if (Splits.Count > 64)
                    Splits.Clear();

                Splits[composite] = split;
            }
        }

        prefix = split.Prefix;
        spec = split.Spec;
        suffix = split.Suffix;
        return split.Ok;
    }

    private static readonly System.Collections.Generic.Dictionary<string, (bool, string, string?, string)> Splits =
        new(System.StringComparer.Ordinal);

    private static (bool, string, string?, string) Split(string composite)
    {
        var open = composite.IndexOf("{0:", System.StringComparison.Ordinal);
        var close = open < 0 ? -1 : composite.IndexOf('}', open);
        if (close < 0)
            return (false, string.Empty, null, string.Empty);

        var prefix = composite[..open];
        var spec = composite[(open + 3)..close];
        var suffix = composite[(close + 1)..];

        var ok = prefix.IndexOfAny(Braces) < 0 && suffix.IndexOfAny(Braces) < 0 && spec.Length > 0;
        return (ok, prefix, spec, suffix);
    }

    private static readonly char[] Braces = { '{', '}' };

    private static string Translate(string spec)
    {
        var percent = spec.IndexOf('%', System.StringComparison.Ordinal);
        if (percent < 0 || percent + 1 >= spec.Length)
            return spec;

        // "%%" is a literal percent sign and carries no conversion.
        if (spec[percent + 1] == '%')
            return spec;

        var end = percent + 1;
        while (end < spec.Length && !char.IsLetter(spec[end]))
            end++;

        if (end >= spec.Length)
            return spec;

        var body = spec[(percent + 1)..end];
        var dot = body.IndexOf('.', System.StringComparison.Ordinal);
        var precision = dot >= 0 ? body[(dot + 1)..] : null;

        var net = spec[end] switch
        {
            'f' or 'F' => "F" + (precision ?? "2"),
            'e' or 'E' => "E" + (precision ?? "2"),
            'g' or 'G' => "G" + precision,
            'd' or 'i' => "F0",
            'x' or 'X' => "X",
            _ => null,
        };

        return net == null ? spec : spec[..percent] + "{0:" + net + "}" + spec[(end + 1)..];
    }
}


/// <summary>One drop shadow: CSS `box-shadow` order, in scene units.</summary>
/// <remarks>
/// Lives here rather than beside the emitter that draws it because a text placement carries
/// one too, and the placement types are the half of this renderer the headless test harness
/// can compile -- the emitter needs a mesh and a clip region, this needs neither.
/// </remarks>
internal readonly struct VecShadow
{
    internal readonly float Dx;
    internal readonly float Dy;
    internal readonly float Blur;
    internal readonly float Spread;
    internal readonly Color Colour;

    /// <summary>CSS `inset`: drawn inside the shape, over its fill.</summary>
    internal readonly bool Inset;

    internal VecShadow(float dx, float dy, float blur, float spread, Color colour, bool inset = false)
    {
        Dx = dx;
        Dy = dy;
        Blur = blur;
        Spread = spread;
        Colour = colour;
        Inset = inset;
    }
}


/// <summary>A text shadow converted into the SDF shader's normalised padding space.</summary>
internal struct TextShadowFit
{
    internal float OffsetX;
    internal float OffsetY;
    internal float Dilate;
    internal float Softness;

    /// <summary>1 when the request fit; below 1 by the factor it had to be reduced.</summary>
    internal float Scale;
}

/// <summary>
/// Turns a <c>sh</c> entry into the four numbers the font's underlay takes.
/// </summary>
/// <remarks>
/// Separated from the code that writes them so it can be checked without Unity, because this
/// is the part that goes wrong quietly: a factor out by the sampling size draws a shadow at a
/// tenth of its size, which reads as "the blur is subtle" rather than as a bug.
///
/// **The space.** Underlay offset, dilate and softness are normalised against the glyph's SDF
/// padding: 1.0 is the whole padding, which is <c>gradientScale</c> atlas texels, and one
/// texel covers <c>fontSize / samplingPointSize</c> rendered units. So a canvas-unit distance
/// becomes <c>d * samplingPointSize / (gradientScale * fontSize)</c>. Read out of the
/// decompiled <c>ShaderUtilities.GetPadding</c>, not guessed.
///
/// **The budget.** The shader renormalises whenever
/// <c>max(|dx|,|dy|) + dilate + softness</c> exceeds 1 — it does not clip an over-large
/// shadow, it SHRINKS it, and everything else with it. Scaling the whole request here
/// instead keeps the shadow's shape and makes the reduction a number the caller can report.
/// </remarks>
internal static class TextShadow
{
    internal static TextShadowFit Fit(VecShadow shadow, float gradientScale, float samplingPointSize, float fontSize)
    {
        var fit = new TextShadowFit { Scale = 1f };

        if (gradientScale <= 0.0001f || samplingPointSize <= 0f || fontSize <= 0f)
            return fit;

        var perUnit = samplingPointSize / (gradientScale * fontSize);

        fit.OffsetX = shadow.Dx * perUnit;

        // Scenes are +Y down; the shader is +Y up.
        fit.OffsetY = -shadow.Dy * perUnit;

        fit.Dilate = shadow.Spread * perUnit;

        // The geometric shadow reads `blur` as a CSS blur radius and uses sigma = blur/2.
        // Softness spreads across the same space, so it takes the same halving and a text
        // shadow reads at the same strength as the one on the box behind it.
        fit.Softness = Mathf.Max(0f, shadow.Blur * 0.5f * perUnit);

        var budget = Mathf.Max(Mathf.Abs(fit.OffsetX), Mathf.Abs(fit.OffsetY)) + fit.Dilate + fit.Softness;
        if (budget > 1f)
        {
            fit.Scale = 1f / budget;
            fit.OffsetX *= fit.Scale;
            fit.OffsetY *= fit.Scale;
            fit.Dilate *= fit.Scale;
            fit.Softness *= fit.Scale;
        }

        return fit;
    }

    /// <summary>
    /// Whether this shadow comes out bigger drawn as its own offset copy of the text.
    /// </summary>
    /// <remarks>
    /// Inside a glyph's quad, offset, spread and blur all share one SDF padding budget. A
    /// separate copy of the label moved by the offset takes the offset out of that budget
    /// entirely -- position is free -- so only blur and spread have to fit. Measured on the
    /// game's LiberationSans, a `2 2 3` shadow on 13-point headings fit at 43% in the quad and
    /// fits whole as a copy.
    ///
    /// A shadow with no offset gains nothing and keeps the cheaper single-label path, which is
    /// also why a glow (`0 0 6`) stays capped: its blur alone is past the font's padding, and
    /// no arrangement of labels changes what the distance field holds.
    /// </remarks>
    internal static bool ShouldCast(VecShadow shadow, float gradientScale, float samplingPointSize, float fontSize, out TextShadowFit fit)
    {
        fit = Fit(shadow, gradientScale, samplingPointSize, fontSize);

        if (fit.Scale >= 0.999f || (Mathf.Abs(shadow.Dx) < 0.001f && Mathf.Abs(shadow.Dy) < 0.001f))
            return false;

        var still = new VecShadow(0f, 0f, shadow.Blur, shadow.Spread, shadow.Colour);
        var split = Fit(still, gradientScale, samplingPointSize, fontSize);

        if (split.Scale <= fit.Scale + 0.001f)
            return false;

        fit = split;
        return true;
    }
}

/// <summary>A scroll offset set by a script, applied once per new version.</summary>
internal static class ForcedScroll
{
    /// <summary>Applies <paramref name="offset"/> when <paramref name="version"/> is new for this id.</summary>
    internal static bool Apply(Dictionary<string, float> offsets, Dictionary<string, float> applied, string id, float version, float offset)
    {
        if (applied.TryGetValue(id, out var last) && Mathf.Approximately(last, version))
            return false;

        applied[id] = version;
        offsets[id] = offset;
        return true;
    }
}

/// <summary>A gradient fill on text, looked up per glyph vertex in the text layer.</summary>
internal sealed class TextGradient
{
    /// <summary>The gradient at full alpha; `units=bbox` is bound to the label's box.</summary>
    internal Paint Paint;

    /// <summary>Canvas space to the text node's own coordinates.</summary>
    internal Matrix4x4 CanvasToLocal;

    internal Color At(Vector3 canvas)
    {
        return Paint.At(CanvasToLocal.MultiplyPoint3x4(canvas));
    }
}

/// <summary>`fl="f=#fff size=12 weight=bold font=Name"`: what `::first-line` changes.</summary>
internal sealed class FirstLineStyle
{
    internal Color? Colour;
    internal float? Size;
    internal bool? Bold;
    internal string? Font;

    internal FirstLineStyle Scaled(float scale)
    {
        return new FirstLineStyle { Colour = Colour, Size = Size * scale, Bold = Bold, Font = Font };
    }

    /// <summary>Space-separated key=value pairs; a value may be quoted with ' or ".</summary>
    internal static FirstLineStyle Parse(string text, VecScene scene)
    {
        var style = new FirstLineStyle();
        var i = 0;

        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;

            var keyStart = i;
            while (i < text.Length && text[i] != '=' && !char.IsWhiteSpace(text[i]))
                i++;

            var key = text.Substring(keyStart, i - keyStart);
            if (key.Length == 0)
                break;

            var value = string.Empty;
            if (i < text.Length && text[i] == '=')
            {
                i++;
                if (i < text.Length && (text[i] == '\'' || text[i] == '"'))
                {
                    var quote = text[i++];
                    var close = text.IndexOf(quote, i);
                    if (close < 0)
                        close = text.Length;

                    value = text.Substring(i, close - i);
                    i = Mathf.Min(text.Length, close + 1);
                }
                else
                {
                    var valueStart = i;
                    while (i < text.Length && !char.IsWhiteSpace(text[i]))
                        i++;

                    value = text.Substring(valueStart, i - valueStart);
                }
            }

            switch (key)
            {
                case "f":
                    if (ColorUtility.TryParseHtmlString(value, out var colour))
                        style.Colour = colour;
                    else
                        scene.Problem($"T fl: \"{value}\" is not a colour");
                    break;

                case "size":
                    if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var size))
                        style.Size = size;
                    else
                        scene.Problem($"T fl: size \"{value}\" is not a number");
                    break;

                case "weight":
                    style.Bold = value.Equals("bold", System.StringComparison.OrdinalIgnoreCase)
                                 || (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var weight) && weight >= 600f);
                    break;

                case "font":
                    style.Font = value;
                    break;

                default:
                    scene.Problem($"T fl: \"{key}\" is not a first-line attribute (f, size, weight, font)");
                    break;
            }
        }

        return style;
    }

    /// <summary>The text with rich tags around its first <paramref name="cut"/> characters.</summary>
    internal string Wrap(string text, int cut, out int prefixLength)
    {
        var open = new System.Text.StringBuilder();
        var close = new System.Text.StringBuilder();

        if (Font != null)
        {
            open.Append("<font=\"").Append(Font).Append("\">");
            close.Insert(0, "</font>");
        }

        if (Size.HasValue)
        {
            open.Append("<size=").Append(Size.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append('>');
            close.Insert(0, "</size>");
        }

        if (Bold == true)
        {
            open.Append("<b>");
            close.Insert(0, "</b>");
        }

        if (Colour.HasValue)
        {
            open.Append("<color=#").Append(ColorUtility.ToHtmlStringRGBA(Colour.Value)).Append('>');
            close.Insert(0, "</color>");
        }

        prefixLength = open.Length;
        cut = Mathf.Clamp(cut, 0, text.Length);
        return open + text.Substring(0, cut) + close + text.Substring(cut);
    }
}
