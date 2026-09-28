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
    /// <summary>
    /// Raising this to 64 was tried for the stair-stepping seen on a console 3000 pixels tall
    /// and measured as no change at all -- same slope breaks, same spacing, 2.5x the vertices --
    /// once the probe was built at the same scale it was rasterised at. The bands are not ours.
    /// </summary>
    /// <summary>
    /// Rings across the ramp, capped. The cap is what a console you are standing at needs.
    /// </summary>
    /// <remarks>
    /// A ring boundary is a break in the SLOPE of the fade, and the eye sees slope breaks as
    /// bands long before the numbers look wrong -- which is why every measurement of this said
    /// "no difference" while a player could see it at a glance. The proof came from a gradient
    /// drawn as one quad beside a shadow: the gradient is a straight ramp with no breaks at all
    /// and was perfectly smooth on the same screen, on black, with the same 8-bit colours.
    /// </remarks>
    private const int MaxRings = 96;

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

    /// <summary>Most points one corner's run may take, whatever the size on screen.</summary>
    // Both this and MaxRings are ceilings on a density that is already driven by real screen
    // pixels (ScreenPixelsPerCanvasUnit projects through the camera, so walking up to a console
    // raises it). They were tuned when banding hid everything underneath them. With the banding
    // gone the ceilings are what is visible: standing at a console, a glow's tail is a few
    // hundred screen pixels wide, so 24 rings is a band every ten pixels and a corner is a
    // twelve-segment polygon. A scene seen from across the room never reaches these numbers.
    private const int MaxCornerSamples = 32;

    /// <summary>
    /// Where to put points along the run out of a corner, as fractions of the three-sigma reach.
    /// </summary>
    /// <remarks>
    /// The count follows the SIZE ON SCREEN, which is the whole lesson of this one. Fixed
    /// fractions of sigma (0.5, 1.1, 1.9, 3) look fine on a console across the room and become
    /// facets 40 to 90 pixels wide on one you are standing at, because sigma is scene units and
    /// the eye measures pixels. Reported as stair-stepping, and invisible to every measurement
    /// taken at three pixels per unit.
    ///
    /// Spaced by a power so they crowd near the corner, where the coverage actually bends.
    /// </remarks>
    private static int CornerSampleCount(float sigma, float screenScale)
    {
        var reachPixels = 3f * sigma * Mathf.Max(0.0001f, screenScale);
        return Mathf.Clamp(Mathf.CeilToInt(reachPixels / 10f), 2, MaxCornerSamples);
    }

    private static float CornerSampleAt(int index, int count, float sigma)
    {
        var t = (index + 1) / (float)count;
        return 3f * sigma * t * t;
    }

    /// <summary>
    /// A rectangle with extra points near its corners, so a ring can carry a corner's coverage
    /// without dragging the whole edge down to it.
    /// </summary>
    private static List<Vector2> Densify(List<Vector2> rect, float sigma, float screenScale, List<Vector2> into)
    {
        into.Clear();
        var samples = CornerSampleCount(sigma, screenScale);

        for (var i = 0; i < rect.Count; i++)
        {
            var a = rect[i];
            var b = rect[(i + 1) % rect.Count];
            var along = b - a;
            var length = along.magnitude;
            if (length <= 0.0001f)
                continue;

            var unit = along / length;
            into.Add(a);

            // Out from this corner, then in toward the next, never past the middle: the two
            // corners' samples must not cross or the contour would fold.
            var half = length * 0.5f;
            for (var k = 0; k < samples; k++)
            {
                var d = CornerSampleAt(k, samples, sigma);
                if (d < half)
                    into.Add(a + unit * d);
            }

            for (var k = samples - 1; k >= 0; k--)
            {
                var d = CornerSampleAt(k, samples, sigma);
                if (d < half)
                    into.Add(b - unit * d);
            }
        }

        return into;
    }

    [System.ThreadStatic] private static List<Vector2>? _dense;

    private static System.DateTime _lastReport = System.DateTime.MinValue;

    // Vertex dither does NOT work here, do not try it again. 0.11.48 added a 4x4 Bayer shift of
    // +-0.5/255 at each emitted vertex; in game it made the glow visibly worse (blocky, jittered
    // ring edges, screenshot on the 0.11.48 round). Dither has to happen per PIXEL. A vertex is
    // tens of pixels from its neighbour, so a per-vertex offset does not scatter the rounding of
    // the pixels between them -- it moves the whole interpolated ramp, which just jitters where
    // each band starts. Per-pixel dither needs a fragment shader, i.e. leaving CanvasRenderer.

    [System.ThreadStatic] private static List<Vector2>? _inner;
    [System.ThreadStatic] private static List<Vector2>? _outer;

    /// <summary>
    /// Emits one shadow beneath a shape. Call before the shape itself is filled.
    /// </summary>
    /// <summary>
    /// The exact coverage of a blurred axis-aligned rectangle, which is separable: the product
    /// of the two one-dimensional profiles.
    /// </summary>
    /// <remarks>
    /// This is what a ring cannot express. A ring carries one alpha for a whole contour, taken
    /// from its distance to the outline, which is right along a straight edge -- there the blur
    /// is a one-dimensional problem -- and wrong at a corner, where two edges act at once and
    /// the true coverage is their PRODUCT. At the corner point itself that is 0.5 x 0.5 = 0.25
    /// against the ring's 0.5: measured 0.512 drawn where 0.250 was due, a quarter of full
    /// brightness too much, and visible on a console as a bright four-pointed star.
    ///
    /// Giving each ring VERTEX its own alpha from this product fixes the corners exactly and
    /// costs nothing: the same rings, the same vertices. Only the interpolation between
    /// vertices is approximate, and the rings are already spaced to a few screen pixels.
    /// </remarks>
    private readonly struct RectCoverage
    {
        private readonly float _x0, _x1, _y0, _y1, _sigma;

        internal readonly bool Valid;

        internal RectCoverage(List<Vector2> outline, VecShadow shadow, float sigma)
        {
            _x0 = _x1 = _y0 = _y1 = 0f;
            _sigma = sigma;
            Valid = false;

            // Only a four-point axis-aligned rectangle: a rounded one has arcs, and anything
            // else has corners this product does not describe. Both keep the old ring alpha.
            if (outline.Count != 4 || sigma <= 0.0001f)
                return;

            var a = outline[0];
            var b = outline[1];
            var c = outline[2];
            var d = outline[3];
            const float E = 0.0001f;
            var axis = (Mathf.Abs(a.y - b.y) < E && Mathf.Abs(c.y - d.y) < E && Mathf.Abs(a.x - d.x) < E && Mathf.Abs(b.x - c.x) < E)
                       || (Mathf.Abs(a.x - b.x) < E && Mathf.Abs(c.x - d.x) < E && Mathf.Abs(a.y - d.y) < E && Mathf.Abs(b.y - c.y) < E);
            if (!axis)
                return;

            var minX = Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x));
            var maxX = Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x));
            var minY = Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y));
            var maxY = Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y));

            // The shape the shadow is cast from: moved by the offset and grown by the spread.
            _x0 = minX + shadow.Dx - shadow.Spread;
            _x1 = maxX + shadow.Dx + shadow.Spread;
            _y0 = minY + shadow.Dy - shadow.Spread;
            _y1 = maxY + shadow.Dy + shadow.Spread;
            Valid = _x1 > _x0 && _y1 > _y0;
        }

        /// <summary>
        /// Holds a contour to the rectangle it is meant to be: the one at distance
        /// <paramref name="d"/> inside the shape spans <c>[x0 + d, x1 - d]</c> either way.
        /// </summary>
        /// <remarks>
        /// The corner points added for the profile are what make this necessary. A mitred
        /// offset moves each point along its own bisector, so a point near a corner keeps its
        /// distance along the edge it sits on while the contour shrinks past it, and the
        /// contour crosses itself. The fill over that fold came out solid: measured 0.92 drawn
        /// four units inside a corner where 0.56 was due, a bright patch in exactly the place
        /// this work set out to fix.
        /// </remarks>
        internal void ClampContour(List<Vector2> points, float d)
        {
            if (!Valid)
                return;

            var lowX = _x0 + d;
            var highX = _x1 - d;
            var lowY = _y0 + d;
            var highY = _y1 - d;

            // Past the middle the rectangle has closed: everything meets at the centre line.
            if (lowX > highX)
                lowX = highX = (_x0 + _x1) * 0.5f;
            if (lowY > highY)
                lowY = highY = (_y0 + _y1) * 0.5f;

            for (var i = 0; i < points.Count; i++)
            {
                var p = points[i];
                points[i] = new Vector2(Mathf.Clamp(p.x, lowX, highX), Mathf.Clamp(p.y, lowY, highY));
            }
        }

        internal float At(Vector2 p)
        {
            // Coverage(d) is the profile at a signed distance INSIDE an edge, so the span
            // between two edges is the near one's profile minus the far one's.
            var x = Coverage(p.x - _x0, _sigma) - Coverage(p.x - _x1, _sigma);
            var y = Coverage(p.y - _y0, _sigma) - Coverage(p.y - _y1, _sigma);
            return Mathf.Clamp01(x) * Mathf.Clamp01(y);
        }
    }

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
        // Denser rings were tried for a bright glow and measured: no change to the error, 70%
        // more vertices. The biggest jump between neighbouring pixels sits where the Gaussian
        // is steepest -- at the shape's edge, where the true image steps by as much -- so that
        // is not banding and rings are not what is coarse.
        var rings = sigma <= 0.0001f
            ? 1
            : Mathf.Clamp(Mathf.CeilToInt(reach * Mathf.Max(0.0001f, screenScale) / 2f), MinRings, MaxRings);

        // Rings inside the outline stop where shrinking it further would turn a corner inside
        // out. Offsetting a rounded corner inward by more than its radius folds its points over
        // each other, and the fan over that fold covered parts of the core twice: visible as
        // light wedges under a translucent card (`G o=0.5` over a blur 12 glow, 2026-09-15). Past
        // the limit the core simply stays at the deepest valid contour, at the coverage there.
        // Mitred, as the inset path always was: a vertex moved `d` along its bisector moves the
        // edges beside it by only d*cos(half the corner), so on a plain rectangle every ring sat
        // 1/sqrt2 as far out as its coverage said and the blur came out sqrt2 too sharp.
        var start = Mathf.Min(reach, shadow.Spread + InwardLimit(outline, winding, miter: true));
        start = Mathf.Max(start, -reach);

        var exact = new RectCoverage(outline, shadow, sigma);

        // A rectangle's contour has vertices ONLY at its corners, so a corner's true (lower)
        // coverage would be dragged along the whole edge by interpolation -- measured, the edge
        // came out at the corner's value. Points near each corner give the profile somewhere to
        // live; along the straight run between them it is flat and needs none. Skipped when the
        // blur is too small on screen for the difference to show.
        var dense = exact.Valid && sigma * Mathf.Max(0.0001f, screenScale) >= 3f;
        if (dense)
        {
            // Ring count is NOT reduced to pay for the corner points. Halving it was tried and
            // measured: the corners stayed right but the straight edge went from 0.01 to 0.033
            // off the exact profile, which is the part that was already correct. Rings resolve
            // the ramp across an edge; corner points resolve it around a corner; neither buys
            // the other.

            outline = Densify(outline, sigma, screenScale, _dense ??= new List<Vector2>(64));
            count = outline.Count;
        }

        // One line per big blur, at most every five seconds: what the renderer actually decided
        // in game, because every theory about this so far has been argued from offline numbers.
        if (VectorConfig.Diagnostics && sigma * Mathf.Max(0.0001f, screenScale) >= 30f)
        {
            var now = System.DateTime.UtcNow;
            if ((now - _lastReport).TotalSeconds > 5d)
            {
                _lastReport = now;
                ScriptedScreensVectorPlugin.Log?.LogInfo(
                    $"blur: sigma {sigma:0.0} units, {screenScale:0.0} px/unit, reach {reach * screenScale:0} px, "
                    + $"{rings} rings ({reach * screenScale / Mathf.Max(1, rings):0.0} px each), dense {dense}, "
                    + $"{count} points/contour, alpha {shadow.Colour.a:0.000}..{Coverage(-reach, sigma):0.0000}");
            }
        }

        // Innermost contour: covered to `start`, so it is filled solid rather than ramped.
        MiterOffset(outline, inner, shift, shadow.Spread - start, winding);
        if (dense)
            exact.ClampContour(inner, start);

        var core = shadow.Colour;
        core.a *= Coverage(start, sigma);

        // The core is everything inside the innermost ring, and filling it with ONE alpha is
        // only right when the blur is small against the shape. Blur a 30x24 box by 9 and the
        // true coverage at its centre is 0.74 while `Coverage(start)` says 0.91: it drew as a
        // flat bright rectangle sitting inside the glow, reported from a console as "a bright
        // square in the middle". With the exact product to hand, every core vertex can carry
        // its own value instead, and a fan through the centre gives the middle one too.
        if (dense)
        {
            FillCore(vh, inner, shadow.Colour, exact, matrix, clip);
        }
        else
        {
            var paint = new Paint(core, null, 1f);
            FillConvexOrEar(vh, inner, paint, matrix, clip);
        }

        if (rings <= 1 || reach <= 0.0001f)
            return;

        // Then a strip per ring outward, alpha falling along the Gaussian. Each ring reports
        // where its outer edge landed so the next one can index it instead of repeating it.
        var previous = -1;

        for (var r = 0; r < rings; r++)
        {
            var dInner = start - r * ((start + reach) / rings);
            var dOuter = start - (r + 1) * ((start + reach) / rings);

            MiterOffset(outline, outer, shift, shadow.Spread - dOuter, winding);
            if (dense)
                exact.ClampContour(outer, dOuter);

            var aInner = shadow.Colour.a * Coverage(dInner, sigma);
            var aOuter = shadow.Colour.a * Coverage(dOuter, sigma);

            previous = EmitRing(vh, inner, outer, shadow.Colour, aInner, aOuter, matrix, clip, previous, exact);

            // The outer contour becomes the next ring's inner one.
            (inner, outer) = (outer, inner);
        }

        _inner = inner;
        _outer = outer;
    }

    /// <summary>
    /// The region inside the innermost ring, as a fan through its centre, every vertex carrying
    /// the coverage that belongs to it.
    /// </summary>
    private static void FillCore(MeshBuilder vh, List<Vector2> contour, Color colour, RectCoverage exact, Matrix4x4 matrix, ClipRegion? clip)
    {
        var count = contour.Count;
        if (count < 3 || Tessellator.Starved(vh, count + 1, "a shadow core"))
            return;

        var centre = Vector2.zero;
        for (var i = 0; i < count; i++)
            centre += contour[i];

        centre /= count;

        var middle = colour;
        middle.a = colour.a * exact.At(centre);

        var origin = vh.currentVertCount;
        vh.AddVert(matrix.MultiplyPoint3x4(centre), middle, Vector2.zero);

        for (var i = 0; i < count; i++)
        {
            var p = contour[i];
            if (clip != null)
                p = clip.ClampInside(centre, p);

            var tint = colour;
            tint.a = colour.a * exact.At(p);
            var where = matrix.MultiplyPoint3x4(p);
            vh.AddVert(where, tint, Vector2.zero);
        }

        for (var i = 0; i < count; i++)
            vh.AddTriangle(origin, origin + 1 + i, origin + 1 + (i + 1) % count);
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
    private static int EmitRing(MeshBuilder vh, List<Vector2> inner, List<Vector2> outer, Color colour, float aInner, float aOuter, Matrix4x4 matrix, ClipRegion? clip, int previous, RectCoverage exact)
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

        var inner32 = ci;
        var outer32 = co;
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

                var at = matrix.MultiplyPoint3x4(a);
                var tint = Tint(colour, inner32, exact, a, aInner);
                vh.AddVert(at, tint, Vector2.zero);
            }
        }

        var outerBase = vh.currentVertCount;

        for (var i = 0; i < count; i++)
        {
            var b = outer[i];
            if (clip != null)
                b = clip.ClampInside(inner[i], b);

            var to = matrix.MultiplyPoint3x4(b);
            var outerTint = Tint(colour, outer32, exact, b, aOuter);
            vh.AddVert(to, outerTint, Vector2.zero);
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
    /// A ring vertex's colour: the exact product where the shape is a rectangle, the ring's own
    /// alpha otherwise. <paramref name="ring"/> is that fallback, already built.
    /// </summary>
    private static Color Tint(Color colour, Color ring, RectCoverage exact, Vector2 at, float ringAlpha)
    {
        if (!exact.Valid)
            return ring;

        var tinted = colour;
        tinted.a = colour.a * exact.At(at);

        // Never brighter than the ring it belongs to: a shared contour is emitted once, and a
        // vertex is read by the ring inside it as well as the one outside.
        tinted.a = Mathf.Min(tinted.a, Mathf.Max(ringAlpha, 0f));
        return tinted;
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

        internal Color Colour(float alpha)
        {
            var c = _colour;
            c.a = alpha;
            return c;
        }

        internal Color Colour(Vector2 p)
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
