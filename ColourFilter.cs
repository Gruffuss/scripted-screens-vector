using System.Collections.Generic;
using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>
/// CSS <c>filter()</c> colour functions, applied to vertex and label colours at emit.
/// </summary>
/// <remarks>
/// The matrices are the Filter Effects spec's, on the sRGB values colours are written in, and
/// every step clamps to 0..1 as the spec's feColorMatrix does. Applied to vertex colours rather
/// than to rendered pixels: exact for flat colours, and inside a triangle the GPU interpolates
/// between filtered corners, which differs from filtering the interpolated colour only for the
/// non-linear steps (contrast clamping, hue rotation) and only by the curvature across one
/// triangle.
/// </remarks>
internal static class ColourFilter
{
    internal const int Brightness = 0;
    internal const int Contrast = 1;
    internal const int Saturate = 2;
    internal const int HueRotate = 3;
    internal const int Grayscale = 4;
    internal const int Sepia = 5;
    internal const int Invert = 6;

    /// <summary>Scene keys, in op-code order.</summary>
    internal static readonly string[] Keys = { "bri", "con", "sat", "hue", "gray", "sep", "inv" };

    internal static int OpFor(string key)
    {
        for (var i = 0; i < Keys.Length; i++)
        {
            if (string.Equals(Keys[i], key, System.StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    internal static Color Apply(Color c, int op, float amount, bool clamp = true)
    {
        float r = c.r, g = c.g, b = c.b;

        switch (op)
        {
            case Brightness:
                r *= amount; g *= amount; b *= amount;
                break;

            case Contrast:
                r = (r - 0.5f) * amount + 0.5f;
                g = (g - 0.5f) * amount + 0.5f;
                b = (b - 0.5f) * amount + 0.5f;
                break;

            case Saturate:
            {
                var s = Mathf.Max(0f, amount);
                Mix(ref r, ref g, ref b,
                    0.213f + 0.787f * s, 0.715f - 0.715f * s, 0.072f - 0.072f * s,
                    0.213f - 0.213f * s, 0.715f + 0.285f * s, 0.072f - 0.072f * s,
                    0.213f - 0.213f * s, 0.715f - 0.715f * s, 0.072f + 0.928f * s);
                break;
            }

            case HueRotate:
            {
                var rad = amount * Mathf.Deg2Rad;
                var cos = Mathf.Cos(rad);
                var sin = Mathf.Sin(rad);
                Mix(ref r, ref g, ref b,
                    0.213f + cos * 0.787f - sin * 0.213f, 0.715f - cos * 0.715f - sin * 0.715f, 0.072f - cos * 0.072f + sin * 0.928f,
                    0.213f - cos * 0.213f + sin * 0.143f, 0.715f + cos * 0.285f + sin * 0.140f, 0.072f - cos * 0.072f - sin * 0.283f,
                    0.213f - cos * 0.213f - sin * 0.787f, 0.715f - cos * 0.715f + sin * 0.715f, 0.072f + cos * 0.928f + sin * 0.072f);
                break;
            }

            case Grayscale:
            {
                var k = 1f - Mathf.Clamp01(amount);
                Mix(ref r, ref g, ref b,
                    0.2126f + 0.7874f * k, 0.7152f - 0.7152f * k, 0.0722f - 0.0722f * k,
                    0.2126f - 0.2126f * k, 0.7152f + 0.2848f * k, 0.0722f - 0.0722f * k,
                    0.2126f - 0.2126f * k, 0.7152f - 0.7152f * k, 0.0722f + 0.9278f * k);
                break;
            }

            case Sepia:
            {
                var k = 1f - Mathf.Clamp01(amount);
                Mix(ref r, ref g, ref b,
                    0.393f + 0.607f * k, 0.769f - 0.769f * k, 0.189f - 0.189f * k,
                    0.349f - 0.349f * k, 0.686f + 0.314f * k, 0.168f - 0.168f * k,
                    0.272f - 0.272f * k, 0.534f - 0.534f * k, 0.131f + 0.869f * k);
                break;
            }

            case Invert:
            {
                var i = Mathf.Clamp01(amount);
                r = i + r * (1f - 2f * i);
                g = i + g * (1f - 2f * i);
                b = i + b * (1f - 2f * i);
                break;
            }
        }

        return clamp ? new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), c.a) : new Color(r, g, b, c.a);
    }

    private static void Mix(ref float r, ref float g, ref float b,
        float m00, float m01, float m02, float m10, float m11, float m12, float m20, float m21, float m22)
    {
        var nr = m00 * r + m01 * g + m02 * b;
        var ng = m10 * r + m11 * g + m12 * b;
        var nb = m20 * r + m21 * g + m22 * b;
        r = nr; g = ng; b = nb;
    }
}

/// <summary>A gradient mask, resolved for one group at one rebuild.</summary>
internal sealed class MaskInfo
{
    internal Gradient Gradient = null!;

    /// <summary>Canvas space to the masked group's local space.</summary>
    internal Matrix4x4 CanvasToLocal;

    /// <summary>The group's content bounds in local space, for `units = "bbox"`.</summary>
    internal Vector2 Min;
    internal Vector2 Size = Vector2.one;

    /// <summary>Mask alpha at a canvas-space position.</summary>
    internal float AlphaAt(Vector3 canvas)
    {
        var local = (Vector2)CanvasToLocal.MultiplyPoint3x4(canvas);

        // A conic's angle is measured in the group's own space, as a conic fill measures it in
        // the shape's: through bounding-box fractions a wide group would bend every angle.
        if (Gradient is { Conic: true, BoundingBox: true })
            return Gradient.Sample(Gradient.ConicParameter(local - (Min + Vector2.Scale(Gradient.Start, Size)))).a;

        return Gradient.At(InGradientSpace(local)).a;
    }

    private Vector2 InGradientSpace(Vector2 local)
    {
        return Gradient.BoundingBox
            ? new Vector2(
                (local.x - Min.x) / (Mathf.Abs(Size.x) < 0.0001f ? 1f : Size.x),
                (local.y - Min.y) / (Mathf.Abs(Size.y) < 0.0001f ? 1f : Size.y))
            : local;
    }

    /// <summary>
    /// Whether any of these canvas-space vertices lies past either end of a linear ramp.
    /// </summary>
    /// <remarks>
    /// There the mask holds its end alpha flat, and alpha interpolated between corners runs
    /// straight through that flat part: a list longer than its fade faded evenly from end to end
    /// instead of staying opaque until the fade began. Fills have had the same check since
    /// 0.11.31; masks did not.
    /// </remarks>
    internal bool LeavesRamp(System.Collections.Generic.List<Vector3> positions, int from, int to)
    {
        if (Gradient.Radial || Gradient.Conic)
            return false;

        for (var v = from; v < to; v++)
        {
            var t = Gradient.Parameter(InGradientSpace(CanvasToLocal.MultiplyPoint3x4(positions[v])));
            if (t < -0.0001f || t > 1.0001f)
                return true;
        }

        return false;
    }

    /// <summary>
    /// For a conic mask: where its start angle runs, in canvas space. Vertex colours cannot
    /// carry the jump from 1 back to 0 there, so the mesh is cut along it.
    /// </summary>
    internal bool Seam(out Vector2 origin, out Vector2 direction)
    {
        origin = Vector2.zero;
        direction = Vector2.up;
        if (!Gradient.Conic)
            return false;

        var centre = Gradient.BoundingBox ? Min + Vector2.Scale(Gradient.Start, Size) : Gradient.Start;
        var radians = Gradient.Angle * Mathf.Deg2Rad;
        var heading = new Vector2(Mathf.Sin(radians), -Mathf.Cos(radians));

        var toCanvas = Tessellator.AffineInverse(CanvasToLocal);
        origin = toCanvas.MultiplyPoint3x4(centre);
        direction = ((Vector2)toCanvas.MultiplyVector(heading)).normalized;
        return direction.sqrMagnitude > 0.5f;
    }

    /// <summary>Mask alpha range over a triangle: corners, centroid and edge midpoints.</summary>
    internal float SpreadOver(Vector3 a, Vector3 b, Vector3 c)
    {
        var low = float.MaxValue;
        var high = float.MinValue;

        Take(a); Take(b); Take(c);
        Take((a + b + c) / 3f);
        Take((a + b) * 0.5f); Take((b + c) * 0.5f); Take((c + a) * 0.5f);
        return high - low;

        void Take(Vector3 p)
        {
            var alpha = AlphaAt(p);
            low = Mathf.Min(low, alpha);
            high = Mathf.Max(high, alpha);
        }
    }
}

/// <summary>
/// What a group does to every colour under it: filters, then a mask, then its parent's.
/// </summary>
/// <remarks>
/// Applied where every colour meets the mesh -- <see cref="MeshBuilder.AddVert"/> -- so fills,
/// strokes, feathers, shadows and images all take it without each emitter knowing. Labels take
/// the same object per glyph vertex in TextLayer. Innermost first, as CSS composites: a child
/// group's filter acts on its content before the parent's acts on the whole.
/// </remarks>
internal sealed class VertexTint
{
    internal readonly VertexTint? Parent;
    private readonly int[] _ops;
    private readonly float[] _amounts;

    /// <summary>Set after the group's content is known when the mask is `units = "bbox"`.</summary>
    internal MaskInfo? Mask;

    /// <summary>Each filter as a 3x4 matrix (row-major, offset last), built once per group.</summary>
    private readonly float[] _matrices;

    internal VertexTint(VertexTint? parent, List<int> ops, List<float> amounts, MaskInfo? mask)
    {
        Parent = parent;
        _ops = ops.ToArray();
        _amounts = amounts.ToArray();
        Mask = mask;

        // Colour filters were evaluated from scratch for every vertex, cosines and all: 5.7 ms
        // of a 36 ms rebuild on a test console. They are affine maps, so each becomes a matrix
        // here and a vertex costs twelve multiplies and a clamp per filter.
        _matrices = new float[_ops.Length * 12];
        for (var i = 0; i < _ops.Length; i++)
        {
            var r = ColourFilter.Apply(new Color(1f, 0f, 0f, 1f), _ops[i], _amounts[i], clamp: false);
            var g = ColourFilter.Apply(new Color(0f, 1f, 0f, 1f), _ops[i], _amounts[i], clamp: false);
            var b = ColourFilter.Apply(new Color(0f, 0f, 1f, 1f), _ops[i], _amounts[i], clamp: false);
            var o = ColourFilter.Apply(new Color(0f, 0f, 0f, 1f), _ops[i], _amounts[i], clamp: false);

            var m = i * 12;
            _matrices[m] = r.r - o.r; _matrices[m + 1] = g.r - o.r; _matrices[m + 2] = b.r - o.r; _matrices[m + 3] = o.r;
            _matrices[m + 4] = r.g - o.g; _matrices[m + 5] = g.g - o.g; _matrices[m + 6] = b.g - o.g; _matrices[m + 7] = o.g;
            _matrices[m + 8] = r.b - o.b; _matrices[m + 9] = g.b - o.b; _matrices[m + 10] = b.b - o.b; _matrices[m + 11] = o.b;
        }
    }

    private Color Filter(Color colour)
    {
        for (var i = 0; i < _ops.Length; i++)
        {
            var m = i * 12;
            var r = _matrices[m] * colour.r + _matrices[m + 1] * colour.g + _matrices[m + 2] * colour.b + _matrices[m + 3];
            var g = _matrices[m + 4] * colour.r + _matrices[m + 5] * colour.g + _matrices[m + 6] * colour.b + _matrices[m + 7];
            var b = _matrices[m + 8] * colour.r + _matrices[m + 9] * colour.g + _matrices[m + 10] * colour.b + _matrices[m + 11];
            colour = new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), colour.a);
        }

        return colour;
    }

    internal bool NeedsPosition => Mask != null || (Parent?.NeedsPosition ?? false);

    internal bool HasFilters => _ops.Length > 0 || (Parent?.HasFilters ?? false);

    internal Color Apply(Color colour, Vector3 canvas)
    {
        colour = Filter(colour);

        if (Mask != null)
            colour.a *= Mask.AlphaAt(canvas);

        return Parent != null ? Parent.Apply(colour, canvas) : colour;
    }

    /// <summary>Filters only, for colours with no position: shadow and underlay colours.</summary>
    internal Color ApplyFilters(Color colour)
    {
        colour = Filter(colour);

        return Parent != null ? Parent.ApplyFilters(colour) : colour;
    }
}
