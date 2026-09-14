using System.Collections.Generic;
using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>One CSS-style drop shadow: offset, blur, spread, colour.</summary>

/// <summary>
/// Draws Gaussian drop shadows as geometry, with no offscreen pass and no shader.
/// </summary>
/// <remarks>
/// The obvious implementation is a fragment shader evaluating the closed form for a
/// blurred rounded box. That is not available here: Unity cannot compile a shader at
/// runtime, so shipping one means an AssetBundle built in the Editor, which this mod has
/// never needed for anything else.
///
/// The same closed form works on the mesh instead. A Gaussian-blurred step edge has
/// coverage <c>0.5 * erfc(d / (sigma * sqrt2))</c> at signed distance <c>d</c> from the
/// edge, so the shadow is a stack of contours offset along their own normals from
/// <c>-3 sigma</c> to <c>+3 sigma</c>, each carrying that alpha, with the interior filled
/// solid. Vertex interpolation between contours makes the ramp piecewise linear, which at
/// this ring count is indistinguishable from the curve — the same trick the radial gradient
/// already uses.
///
/// **Where this differs from CSS**, and it is worth knowing rather than discovering:
///
/// - The formula is exact for a straight edge and approximate at a corner, because it
///   measures distance along the vertex normal rather than solving the 2-D convolution.
///   On a rounded rect at UI radii the difference is well under a pixel; on a sharp corner
///   the shadow is very slightly tight.
/// - The shadow is **not knocked out** under the shape. CSS clips it to outside the border
///   box so a translucent shape does not darken over its own shadow. Doing that here needs
///   a polygon boolean, and every shadow this was built for sits under an opaque control.
///   A translucent shape over its own shadow will read darker than the mockup.
/// - `inset` is not implemented.
///
/// Blending is straight source-over on sRGB bytes, which is the space the colours are
/// written in, so shadows composite at the value the artboard specifies.
/// </remarks>
internal static class Shadow
{
    /// <summary>Contours across the blur. Enough that the ramp reads as smooth.</summary>
    private const int MinRings = 4;
    private const int MaxRings = 24;

    /// <summary>Gaussian support. Past three sigma the contribution is under 0.2%.</summary>
    private const float Support = 3f;

    /// <summary>
    /// Abramowitz and Stegun 7.1.26. Max error 1.5e-7, which is far below a colour byte.
    /// </summary>
    private static float Erf(float x)
    {
        var sign = x < 0f ? -1f : 1f;
        x = Mathf.Abs(x);

        var t = 1f / (1f + 0.3275911f * x);
        var y = 1f - (((((1.061405429f * t - 1.453152027f) * t) + 1.421413741f) * t
                       - 0.284496736f) * t + 0.254829592f) * t * Mathf.Exp(-x * x);

        return sign * y;
    }

    /// <summary>Coverage of a blurred step edge at signed distance d, inside positive.</summary>
    private static float Coverage(float d, float sigma)
    {
        if (sigma <= 0.0001f)
            return d >= 0f ? 1f : 0f;

        return 0.5f * (1f + Erf(d / (sigma * 1.41421356f)));
    }

    /// <summary>
    /// Offsets a closed contour along its own vertex normals. Positive grows it.
    /// </summary>
    private static void Offset(List<Vector2> source, List<Vector2> into, Vector2 shift, float amount, float winding)
    {
        into.Clear();
        var count = source.Count;

        for (var i = 0; i < count; i++)
        {
            var previous = source[(i - 1 + count) % count];
            var next = source[(i + 1) % count];

            var incoming = (source[i] - previous).normalized;
            var outgoing = (next - source[i]).normalized;

            var normal = new Vector2(incoming.y + outgoing.y, -(incoming.x + outgoing.x)).normalized * winding;
            if (normal.sqrMagnitude < 0.0001f)
                normal = Vector2.up;

            into.Add(source[i] + shift + normal * amount);
        }
    }

    [System.ThreadStatic] private static List<Vector2>? _inner;
    [System.ThreadStatic] private static List<Vector2>? _outer;

    /// <summary>
    /// Emits one shadow beneath a shape. Call before the shape itself is filled.
    /// </summary>
    internal static void Emit(MeshBuilder vh, List<Vector2> outline, VecShadow shadow, Matrix4x4 matrix, float screenScale, ClipRegion? clip)
    {
        var count = outline.Count;
        if (count < 3 || shadow.Colour.a <= 0.002f)
            return;

        var inner = _inner ??= new List<Vector2>(64);
        var outer = _outer ??= new List<Vector2>(64);

        var winding = Mathf.Sign(Triangulator.SignedArea(outline));
        var shift = new Vector2(shadow.Dx, shadow.Dy);

        // CSS blur radius is twice the standard deviation.
        var sigma = Mathf.Max(0f, shadow.Blur) * 0.5f;
        var reach = sigma * Support;

        // Ring count follows the blur's on-screen size: a two-pixel shadow needs no more
        // resolution than a two-pixel gradient does.
        var rings = sigma <= 0.0001f
            ? 1
            : Mathf.Clamp(Mathf.CeilToInt(reach * Mathf.Max(0.0001f, screenScale) / 2f), MinRings, MaxRings);

        // Innermost contour: fully covered, so it is filled solid rather than ramped.
        Offset(outline, inner, shift, shadow.Spread - reach, winding);

        var core = shadow.Colour;
        core.a *= Coverage(reach, sigma);

        var paint = new Paint(core, null, 1f);
        FillConvexOrEar(vh, inner, paint, matrix, clip);

        if (rings <= 1 || reach <= 0.0001f)
            return;

        // Then a strip per ring outward, alpha falling along the Gaussian.
        for (var r = 0; r < rings; r++)
        {
            var dInner = reach - r * (2f * reach / rings);
            var dOuter = reach - (r + 1) * (2f * reach / rings);

            Offset(outline, outer, shift, shadow.Spread - dOuter, winding);

            var aInner = shadow.Colour.a * Coverage(dInner, sigma);
            var aOuter = shadow.Colour.a * Coverage(dOuter, sigma);

            EmitRing(vh, inner, outer, shadow.Colour, aInner, aOuter, matrix, clip);

            // The outer contour becomes the next ring's inner one.
            (inner, outer) = (outer, inner);
        }

        _inner = inner;
        _outer = outer;
    }

    private static void EmitRing(MeshBuilder vh, List<Vector2> inner, List<Vector2> outer, Color colour, float aInner, float aOuter, Matrix4x4 matrix, ClipRegion? clip)
    {
        var count = inner.Count;
        if (count < 3 || outer.Count != count || vh.currentVertCount + count * 2 > 60000)
            return;

        var ci = colour; ci.a = aInner;
        var co = colour; co.a = aOuter;

        var inner32 = (Color32)ci;
        var outer32 = (Color32)co;
        var origin = vh.currentVertCount;

        for (var i = 0; i < count; i++)
        {
            var a = inner[i];
            var b = outer[i];

            if (clip != null)
            {
                a = clip.ClampInside(a, a);
                b = clip.ClampInside(inner[i], b);
            }

            vh.AddVert(matrix.MultiplyPoint3x4(a), inner32, Vector2.zero);
            vh.AddVert(matrix.MultiplyPoint3x4(b), outer32, Vector2.zero);
        }

        for (var i = 0; i < count; i++)
        {
            var a = origin + i * 2;
            var c = origin + ((i + 1) % count) * 2;

            vh.AddTriangle(a, a + 1, c + 1);
            vh.AddTriangle(a, c + 1, c);
        }
    }

    /// <summary>Fills the solid core. Convex is the common case and fans directly.</summary>
    private static void FillConvexOrEar(MeshBuilder vh, List<Vector2> contour, Paint paint, Matrix4x4 matrix, ClipRegion? clip)
    {
        if (contour.Count < 3)
            return;

        var shape = clip != null ? clip.ClipPolygon(contour) : contour;
        if (shape.Count < 3)
            return;

        if (vh.currentVertCount + shape.Count > 60000)
            return;

        var origin = vh.currentVertCount;
        var colour = paint.At(shape[0]);

        foreach (var point in shape)
            vh.AddVert(matrix.MultiplyPoint3x4(point), colour, Vector2.zero);

        for (var i = 1; i < shape.Count - 1; i++)
            vh.AddTriangle(origin, origin + i, origin + i + 1);
    }
}
