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
/// - `inset` is drawn by <see cref="EmitInset"/>, for convex outlines.
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
        //
        // One ring per FOUR screen pixels of reach, not two. The density was inherited from the
        // colour-gradient rule, where banding across a saturated ramp is the visible failure; a
        // shadow is a dark translucent ramp and a step of the same size is far harder to see.
        // Measured offline against a 400-ring reference, the page's `4 6 10` shadow at the size
        // it is viewed: max error 5.3/255 at two pixels, 12.0/255 at four, mean 0.011 and 0.016
        // -- isolated edge pixels, invisible even amplified sixteen times. Up to half the
        // vertices below the 24-ring cap; ~25% on that shadow, which sits at the cap.
        var rings = sigma <= 0.0001f
            ? 1
            : Mathf.Clamp(Mathf.CeilToInt(reach * Mathf.Max(0.0001f, screenScale) / 4f), MinRings, MaxRings);

        // Rings inside the outline stop where shrinking it further would turn a corner inside
        // out. Offsetting a rounded corner inward by more than its radius folds its points over
        // each other, and the fan over that fold covered parts of the core twice: visible as
        // light wedges under a translucent card (`G o=0.5` over a blur 12 glow, 2026-09-15). Past
        // the limit the core simply stays at the deepest valid contour, at the coverage there.
        var start = Mathf.Min(reach, shadow.Spread + InwardLimit(outline, winding, miter: false));
        start = Mathf.Max(start, -reach);

        // Innermost contour: covered to `start`, so it is filled solid rather than ramped.
        Offset(outline, inner, shift, shadow.Spread - start, winding);

        var core = shadow.Colour;
        core.a *= Coverage(start, sigma);

        var paint = new Paint(core, null, 1f);
        FillConvexOrEar(vh, inner, paint, matrix, clip);

        if (rings <= 1 || reach <= 0.0001f)
            return;

        // Then a strip per ring outward, alpha falling along the Gaussian. Each ring reports
        // where its outer edge landed so the next one can index it instead of repeating it.
        var previous = -1;

        for (var r = 0; r < rings; r++)
        {
            var dInner = start - r * ((start + reach) / rings);
            var dOuter = start - (r + 1) * ((start + reach) / rings);

            Offset(outline, outer, shift, shadow.Spread - dOuter, winding);

            var aInner = shadow.Colour.a * Coverage(dInner, sigma);
            var aOuter = shadow.Colour.a * Coverage(dOuter, sigma);

            previous = EmitRing(vh, inner, outer, shadow.Colour, aInner, aOuter, matrix, clip, previous);

            // The outer contour becomes the next ring's inner one.
            (inner, outer) = (outer, inner);
        }

        _inner = inner;
        _outer = outer;
    }

    /// <summary>
    /// One ring of the blur, from <paramref name="inner"/> out to <paramref name="outer"/>.
    /// </summary>
    /// <remarks>
    /// **Consecutive rings share a contour.** Ring r's outer edge and ring r+1's inner edge
    /// are the same points at the same alpha -- the distance steps are laid out so that
    /// <c>dOuter(r) == dInner(r+1)</c>, and the contour list is literally handed forward by the
    /// caller. Emitting both meant a shadow paid <c>2N</c> rings' worth of vertices where
    /// <c>N+1</c> would do. An eight-ring shadow on a rounded card was 640 vertices; it is 360
    /// now, and it looks the same.
    ///
    /// <paramref name="previous"/> is where the last ring put its outer vertices, or -1 for
    /// the first ring. **A clip forces the long way round**: the two clamps take different
    /// reference points -- ring r clamps its outer against its own inner, ring r+1 clamps that
    /// same contour against itself -- so the results are not guaranteed to coincide and
    /// sharing them would tear the ring where the clip bites.
    /// </remarks>
    private static int EmitRing(MeshBuilder vh, List<Vector2> inner, List<Vector2> outer, Color colour, float aInner, float aOuter, Matrix4x4 matrix, ClipRegion? clip, int previous)
    {
        var count = inner.Count;
        var share = previous >= 0 && clip == null;

        if (count < 3 || outer.Count != count
            || Tessellator.Starved(vh, share ? count : count * 2, "a shadow"))
        {
            return -1;
        }

        var ci = colour; ci.a = aInner;
        var co = colour; co.a = aOuter;

        var inner32 = (Color32)ci;
        var outer32 = (Color32)co;
        var origin = vh.currentVertCount;

        // Inner edge first, then outer, rather than interleaved pairs. That keeps the OUTER
        // vertices contiguous, which is the whole point: the next ring indexes them as
        // `base + i`, and a strided layout could not be indexed that way at all.
        if (!share)
        {
            for (var i = 0; i < count; i++)
            {
                var a = inner[i];
                if (clip != null)
                    a = clip.ClampInside(a, a);

                vh.AddVert(matrix.MultiplyPoint3x4(a), inner32, Vector2.zero);
            }
        }

        var outerBase = vh.currentVertCount;

        for (var i = 0; i < count; i++)
        {
            var b = outer[i];
            if (clip != null)
                b = clip.ClampInside(inner[i], b);

            vh.AddVert(matrix.MultiplyPoint3x4(b), outer32, Vector2.zero);
        }

        var innerBase = share ? previous : origin;

        for (var i = 0; i < count; i++)
        {
            var next = (i + 1) % count;

            vh.AddTriangle(innerBase + i, outerBase + i, outerBase + next);
            vh.AddTriangle(innerBase + i, outerBase + next, innerBase + next);
        }

        // Where this ring's OUTER vertices start, so the next can index them as its inner.
        return outerBase;
    }

    /// <summary>
    /// Emits one inset shadow inside a convex outline. Call after the fill, before the stroke.
    /// </summary>
    /// <remarks>
    /// CSS draws an inset shadow as the blurred inverse of the shape moved by the offset and
    /// shrunk by the spread, clipped to the shape. Here: at a point whose inward distance into
    /// the moved outline is <c>u</c>, alpha is <c>1 - Coverage(u - spread)</c>. Exact for a
    /// straight edge, the same corner approximation as the outset shadow.
    ///
    /// Geometry is rings along the moved outline, offset from `spread - 3 sigma` to
    /// `spread + 3 sigma`, plus a solid band out to past the shape and the core inside. Each
    /// ring triangle is cut to the shape with the convex clipper and every vertex gets its
    /// alpha from the distance, so a vertex a clip created is coloured correctly rather than
    /// interpolated from ring corners it no longer has. A ring deeper than the shape is thick
    /// would turn inside out, so rings stop at the outline's inradius and the core takes the
    /// rest.
    ///
    /// ponytail: one unshared vertex set per clipped triangle, roughly 3x an outset shadow's
    /// vertices; share vertices across unclipped triangles if inset shadows get common.
    /// </remarks>
    internal static void EmitInset(MeshBuilder vh, List<Vector2> outline, VecShadow shadow, Matrix4x4 matrix, float screenScale, ClipRegion? clip)
    {
        var count = outline.Count;
        if (count < 3 || shadow.Colour.a <= 0.002f)
            return;

        var shape = new List<Vector2>(clip != null ? clip.ClipPolygon(outline) : outline);
        var region = shape.Count >= 3 ? ClipRegion.FromPolygon(shape) : null;
        if (region == null)
            return;

        var winding = Mathf.Sign(Triangulator.SignedArea(outline));
        var shift = new Vector2(shadow.Dx, shadow.Dy);

        // A blur of 0 is floored to a fraction of a unit. The alpha is evaluated at vertices,
        // and a true step there flips on rounding: a ring laid exactly on the edge read as
        // inside or outside at random, and the whole shape went dark.
        var sigma = Mathf.Max(0.05f, Mathf.Max(0f, shadow.Blur) * 0.5f);
        var reach = sigma * Support;
        var spread = shadow.Spread;

        var moved = new List<Vector2>(count);
        foreach (var point in outline)
            moved.Add(point + shift);

        var inradius = Inradius(moved);
        var uLo = spread - reach;

        // The same fold as the outset shadow's, for miter offsets: a rounded corner cannot be
        // shrunk past its radius without folding, whatever the shape's inradius allows.
        var uHi = Mathf.Min(spread + reach, Mathf.Min(inradius * 0.999f, InwardLimit(outline, winding, miter: true)));
        var uFar = Mathf.Min(uLo, -shift.magnitude) - 1f;

        var field = new InsetField(moved, winding, spread, sigma, shadow.Colour);

        // Levels from outside the shape inward. Consecutive levels bound one ring.
        var levels = new List<float> { uFar };
        if (uHi > uLo)
        {
            // A ramp under a pixel wide -- a zero blur, floored above -- is an edge, not a
            // gradient: one ring draws it. Four were 510 vertices a shape for a hard 2-unit ring.
            var ramp = (uHi - uLo) * Mathf.Max(0.0001f, screenScale);
            var rings = ramp < 1f ? 1 : Mathf.Clamp(Mathf.CeilToInt(ramp / 4f), MinRings, MaxRings);

            for (var r = 0; r <= rings; r++)
                levels.Add(Mathf.Lerp(uLo, uHi, r / (float)rings));
        }
        else
        {
            levels.Add(uHi);
        }

        var outer = new List<Vector2>(count);
        var inner = new List<Vector2>(count);
        var triangle = new List<Vector2>(3);
        var piece = new List<Vector2>(8);

        MiterOffset(outline, outer, shift, -levels[0], winding);

        for (var l = 1; l < levels.Count; l++)
        {
            MiterOffset(outline, inner, shift, -levels[l], winding);

            for (var i = 0; i < count; i++)
            {
                var next = (i + 1) % count;
                EmitPiece(vh, region, field, matrix, triangle, piece, outer[i], outer[next], inner[next], levels[l - 1], levels[l - 1], levels[l]);
                EmitPiece(vh, region, field, matrix, triangle, piece, outer[i], inner[next], inner[i], levels[l - 1], levels[l], levels[l]);
            }

            (outer, inner) = (inner, outer);
        }

        // The core, inside the deepest ring. Skipped when it is fully clear of shadow.
        if (field.Alpha(Centroid(outer)) <= 0.002f && field.Alpha(outer[0]) <= 0.002f)
            return;

        var core = region.ClipPolygon(outer, piece);
        if (core.Count < 3 || Tessellator.Starved(vh, core.Count, "an inset shadow"))
            return;

        var origin = vh.currentVertCount;
        foreach (var point in core)
            vh.AddVert(matrix.MultiplyPoint3x4(point), field.Colour(point), Vector2.zero);

        for (var i = 1; i < core.Count - 1; i++)
            vh.AddTriangle(origin, origin + i, origin + i + 1);
    }

    /// <summary>
    /// Offsets a closed contour so that every EDGE moves by <paramref name="amount"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="Offset"/> moves each vertex that far along its normal, which moves a square's
    /// edges only 0.71 of it. The outset shadow lives with that; the inset one cannot, since its
    /// alpha comes from true distance and a ring laid at 0.71 of its depth takes the wrong one:
    /// a hard inset on a square went black to the middle.
    /// </remarks>
    private static void MiterOffset(List<Vector2> source, List<Vector2> into, Vector2 shift, float amount, float winding)
    {
        into.Clear();
        var count = source.Count;

        for (var i = 0; i < count; i++)
        {
            var incoming = (source[i] - source[(i - 1 + count) % count]).normalized;
            var outgoing = (source[(i + 1) % count] - source[i]).normalized;

            var a = new Vector2(incoming.y, -incoming.x) * winding;
            var b = new Vector2(outgoing.y, -outgoing.x) * winding;

            // Scaled so both adjacent edges move the full amount; capped for needle corners.
            var miter = (a + b) / Mathf.Max(0.25f, 1f + Vector2.Dot(a, b));
            into.Add(source[i] + shift + miter * amount);
        }
    }

    /// <summary>
    /// How far a closed contour can be offset inward before any of its edges reverses.
    /// </summary>
    /// <remarks>
    /// Moving each point inward along its offset direction <c>v</c> changes an edge <c>e</c> to
    /// <c>e - d (v_b - v_a)</c>, which reverses at <c>d = |e|^2 / (e . (v_b - v_a))</c>. On a
    /// corner arc that is the arc's radius; on a sharp box it is its half-width. Nine tenths of
    /// the smallest, so the deepest contour stays clear of the fold.
    /// </remarks>
    internal static float InwardLimit(List<Vector2> source, float winding, bool miter)
    {
        var count = source.Count;
        if (count < 3)
            return 0f;

        var directions = new Vector2[count];
        for (var i = 0; i < count; i++)
        {
            var incoming = (source[i] - source[(i - 1 + count) % count]).normalized;
            var outgoing = (source[(i + 1) % count] - source[i]).normalized;

            if (miter)
            {
                var a = new Vector2(incoming.y, -incoming.x) * winding;
                var b = new Vector2(outgoing.y, -outgoing.x) * winding;
                directions[i] = (a + b) / Mathf.Max(0.25f, 1f + Vector2.Dot(a, b));
            }
            else
            {
                var normal = new Vector2(incoming.y + outgoing.y, -(incoming.x + outgoing.x)).normalized * winding;
                directions[i] = normal.sqrMagnitude < 0.0001f ? Vector2.up : normal;
            }
        }

        var limit = float.MaxValue;
        for (var i = 0; i < count; i++)
        {
            var next = (i + 1) % count;
            var edge = source[next] - source[i];
            var closing = Vector2.Dot(edge, directions[next] - directions[i]);
            if (closing > 1e-8f)
                limit = Mathf.Min(limit, edge.sqrMagnitude / closing);
        }

        return limit == float.MaxValue ? float.MaxValue : limit * 0.9f;
    }

    /// <remarks>
    /// Corners sit on ring contours whose depth is known, so their alpha comes straight from
    /// that depth. Measuring depth against every edge of the outline, for every vertex, made two
    /// inset shadows cost 25 ms of a 36 ms rebuild. Only a vertex the clip created -- one that is
    /// on no ring -- is measured.
    /// </remarks>
    private static void EmitPiece(MeshBuilder vh, ClipRegion region, InsetField field, Matrix4x4 matrix,
        List<Vector2> triangle, List<Vector2> piece, Vector2 a, Vector2 b, Vector2 c, float ua, float ub, float uc)
    {
        // Alpha falls with depth, so a triangle between two clear rings is clear throughout.
        var aa = field.AlphaAtDepth(ua);
        var ab = field.AlphaAtDepth(ub);
        var ac = field.AlphaAtDepth(uc);
        if (aa <= 0.002f && ab <= 0.002f && ac <= 0.002f)
            return;

        triangle.Clear();
        triangle.Add(a);
        triangle.Add(b);
        triangle.Add(c);

        var cut = region.ClipPolygon(triangle, piece);
        if (cut.Count < 3 || Tessellator.Starved(vh, cut.Count, "an inset shadow"))
            return;

        var origin = vh.currentVertCount;
        if (ReferenceEquals(cut, triangle))
        {
            vh.AddVert(matrix.MultiplyPoint3x4(a), field.Colour(aa), Vector2.zero);
            vh.AddVert(matrix.MultiplyPoint3x4(b), field.Colour(ab), Vector2.zero);
            vh.AddVert(matrix.MultiplyPoint3x4(c), field.Colour(ac), Vector2.zero);
            vh.AddTriangle(origin, origin + 1, origin + 2);
            return;
        }

        foreach (var point in cut)
            vh.AddVert(matrix.MultiplyPoint3x4(point), field.Colour(point), Vector2.zero);

        for (var i = 1; i < cut.Count - 1; i++)
            vh.AddTriangle(origin, origin + i, origin + i + 1);
    }

    private static Vector2 Centroid(List<Vector2> points)
    {
        var sum = Vector2.zero;
        foreach (var point in points)
            sum += point;

        return sum / Mathf.Max(1, points.Count);
    }

    /// <summary>Distance from the centroid to the nearest edge line: a safe inradius for a convex outline.</summary>
    internal static float Inradius(List<Vector2> polygon)
    {
        var centre = Centroid(polygon);
        var nearest = float.MaxValue;

        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var edge = polygon[(i + 1) % polygon.Count] - a;
            if (edge.sqrMagnitude < 1e-10f)
                continue;

            nearest = Mathf.Min(nearest, Mathf.Abs(edge.x * (centre.y - a.y) - edge.y * (centre.x - a.x)) / edge.magnitude);
        }

        return nearest == float.MaxValue ? 0f : nearest;
    }

    /// <summary>An inset shadow's alpha as a function of position.</summary>
    internal readonly struct InsetField
    {
        private readonly List<Vector2> _moved;
        private readonly float _winding;
        private readonly float _spread;
        private readonly float _sigma;
        private readonly Color _colour;

        internal InsetField(List<Vector2> moved, float winding, float spread, float sigma, Color colour)
        {
            _moved = moved;
            _winding = winding;
            _spread = spread;
            _sigma = sigma;
            _colour = colour;
        }

        /// <summary>Signed distance into the moved outline: positive inside.</summary>
        internal float Depth(Vector2 p)
        {
            var inside = true;
            var edgeDistance = float.MaxValue;
            var segmentDistance = float.MaxValue;

            for (var i = 0; i < _moved.Count; i++)
            {
                var a = _moved[i];
                var b = _moved[(i + 1) % _moved.Count];
                var edge = b - a;
                var length = edge.magnitude;
                if (length < 1e-6f)
                    continue;

                // Signed so that inside is positive whichever way the outline winds.
                var side = (edge.x * (p.y - a.y) - edge.y * (p.x - a.x)) / length * _winding;
                if (side < 0f)
                    inside = false;

                edgeDistance = Mathf.Min(edgeDistance, Mathf.Abs(side));

                var t = Mathf.Clamp01(Vector2.Dot(p - a, edge) / (length * length));
                segmentDistance = Mathf.Min(segmentDistance, (a + edge * t - p).magnitude);
            }

            return inside ? edgeDistance : -segmentDistance;
        }

        internal float Alpha(Vector2 p)
        {
            return AlphaAtDepth(Depth(p));
        }

        internal float AlphaAtDepth(float depth)
        {
            return _colour.a * (1f - Coverage(depth - _spread, _sigma));
        }

        internal Color32 Colour(float alpha)
        {
            var c = _colour;
            c.a = alpha;
            return c;
        }

        internal Color32 Colour(Vector2 p)
        {
            var c = _colour;
            c.a = Alpha(p);
            return c;
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
