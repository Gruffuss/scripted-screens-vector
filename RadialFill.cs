using System.Collections.Generic;
using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>
/// How a radial gradient's concentric bands are sized and stitched.
/// </summary>
/// <remarks>
/// Separated from the tessellator so it can be checked without Unity. The arithmetic decides
/// how much mesh a single gradient costs, and this renderer has a hard 60,000-vertex ceiling
/// per surface that fails by silently dropping geometry — so being wrong here is not slow, it
/// is invisible missing artwork.
/// </remarks>
internal static class RadialFill
{
    private const int MaxRings = 96;
    private const int MinRings = 4;

    /// <summary>Screen pixels between bands, and the most any outline edge may span.</summary>
    internal const float PixelsPerBand = 2.5f;

    /// <summary>Most outline points a ring may carry after densifying.</summary>
    /// <remarks>
    /// Rings cost points x rings / 2 vertices, so 384 at the 96-ring cap is ~18,500 -- well
    /// inside one mesh. A shape large enough to hit this at close range gets edges a little
    /// longer than a band is wide, which is a soft loss; running past the mesh budget would
    /// refuse the fill and draw nothing, which is not.
    /// </remarks>
    internal const int MaxOutlinePoints = 384;

    /// <summary>Fewest points any ring keeps, however small it is.</summary>
    /// <remarks>
    /// A ring is a scaled copy of the outline, so cutting its points makes it a coarser
    /// polygon — and the deviation from the true curve is what shows, as a wobble along the
    /// boundary between two colour bands. At 16 points the error is under a pixel for any
    /// ring big enough to reach that floor, because a ring small enough to need fewer is also
    /// small enough that the deviation shrinks with it.
    /// </remarks>
    private const int MinRingPoints = 16;

    /// <summary>
    /// How many bands to draw, from the shape's on-screen radius and its stop count.
    /// </summary>
    /// <remarks>
    /// Three things want a say and the third was missing:
    ///
    /// - **Size.** Bands read as steps once they are more than a couple of pixels apart.
    /// - **Stops.** A ramp needs enough bands to resolve each of its stops at all.
    /// - **What the screen can show.** More bands than pixels of radius cannot be seen, and
    ///   this is the one the stop rule ignored. A thirteen-stop ramp on a six-pixel shape was
    ///   drawing ninety-six rings — eight thousand vertices of a gradient nobody can resolve.
    ///   Capping by radius is not a quality reduction; there is no band to lose.
    /// </remarks>
    internal static int Rings(float screenRadius, int stopCount)
    {
        var bySize = Mathf.CeilToInt(screenRadius / PixelsPerBand);
        var byStops = 8 * Mathf.Max(1, stopCount - 1);

        // One band per screen pixel of radius is already past the point of visibility, so it
        // is a ceiling rather than a target -- it only ever removes work nobody can see.
        var visible = Mathf.Max(MinRings, Mathf.FloorToInt(screenRadius));

        var wanted = Mathf.Max(bySize, byStops);
        return Mathf.Clamp(Mathf.Min(wanted, visible), MinRings, MaxRings);
    }

    /// <summary>
    /// Copies an outline with extra points along any edge longer than <paramref name="maxEdge"/>.
    /// </summary>
    /// <remarks>
    /// **Rings are scaled copies of the outline, and only their vertices carry colour.** For a
    /// circle centred on the focus that is exact: every ring point sits at one gradient value.
    /// For a rounded rect it is not -- the outline has points only in its corner arcs, so along
    /// each long straight edge the colour was a straight blend between two corners. The bands
    /// came out as rounded rectangles with a seam running from the focus to each corner,
    /// reported in game as "a point with corners" where it had been a smooth gradient.
    ///
    /// Only the tangential direction needs this. Along a ray from the focus the gradient
    /// parameter is linear in the ring's scale -- the bounding-box map is affine and keeps
    /// ratios along a line -- so ring spacing already resolves it.
    /// </remarks>
    internal static void Densify(List<Vector2> outline, float maxEdge, List<Vector2> into)
    {
        into.Clear();

        var count = outline.Count;
        if (count < 2 || maxEdge <= 0f)
        {
            into.AddRange(outline);
            return;
        }

        // Size the step so the whole outline fits the point budget, never finer than asked.
        var perimeter = 0f;
        for (var i = 0; i < count; i++)
            perimeter += (outline[(i + 1) % count] - outline[i]).magnitude;

        var step = Mathf.Max(maxEdge, perimeter / MaxOutlinePoints);

        for (var i = 0; i < count; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % count];
            var pieces = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude / step));

            for (var k = 0; k < pieces; k++)
                into.Add(Vector2.Lerp(a, b, k / (float)pieces));
        }
    }

    /// <summary>How many outline points a ring keeps, given how far out it sits.</summary>
    /// <remarks>
    /// A ring at a tenth of the radius has a tenth of the circumference, so spending the full
    /// outline on it oversamples by ten. Scaling the count with the radius keeps the chord
    /// length roughly constant, which is the same rule the outline itself was built by.
    /// </remarks>
    internal static int Points(int outlineCount, int ring, int rings)
    {
        if (outlineCount <= MinRingPoints || rings <= 0)
            return outlineCount;

        var ratio = ring / (float)rings;
        var wanted = Mathf.RoundToInt(outlineCount * ratio);

        return Mathf.Clamp(wanted, MinRingPoints, outlineCount);
    }

    /// <summary>Total vertices a fill will cost, so a caller can check it against the cap.</summary>
    internal static int VertexCount(int outlineCount, int rings)
    {
        var total = 1;
        for (var ring = 1; ring <= rings; ring++)
            total += Points(outlineCount, ring, rings);

        return total;
    }

    /// <summary>
    /// Triangulates the annulus between two rings that need not have the same point count.
    /// </summary>
    /// <remarks>
    /// Both rings run the same way round the same outline, so a point's position round the
    /// ring is its index over its count. Walking the two together and always advancing
    /// whichever is behind emits one triangle per step and uses every edge of both rings
    /// exactly once — <c>inner + outer</c> triangles, no gaps and no overlaps.
    ///
    /// Absolute vertex indices in, so there is nothing to decode: the test calls it with made
    /// up bases and reads the triples straight out.
    /// </remarks>
    internal static void Stitch(int innerBase, int innerCount, int outerBase, int outerCount, List<int> into)
    {
        if (innerCount <= 0 || outerCount <= 0)
            return;

        // A one-point ring has no edges of its own -- its only "edge" runs from a point back
        // to itself -- so the annulus is a plain fan from it. Handled here rather than inside
        // the walk: an earlier attempt merely stopped advancing the inner pointer, which left
        // the loop waiting forever for it to finish. On a worker thread that is a hung game
        // with nothing in the log, and the test found it in one run.
        if (innerCount < 2)
        {
            for (var k = 0; k < outerCount; k++)
            {
                into.Add(innerBase);
                into.Add(outerBase + k);
                into.Add(outerBase + (k + 1) % outerCount);
            }

            return;
        }

        var i = 0;
        var j = 0;

        while (i < innerCount || j < outerCount)
        {
            bool advanceInner;

            if (i >= innerCount)
                advanceInner = false;
            else if (j >= outerCount)
                advanceInner = true;
            else
                advanceInner = (i + 1) * (long)outerCount <= (j + 1) * (long)innerCount;

            if (advanceInner)
            {
                // The inner edge i -> i+1, closed off against where the outer ring has got to.
                into.Add(innerBase + i);
                into.Add(outerBase + j % outerCount);
                into.Add(innerBase + (i + 1) % innerCount);
                i++;
            }
            else
            {
                // The outer edge j -> j+1, against the inner ring's current point.
                into.Add(innerBase + i % innerCount);
                into.Add(outerBase + j);
                into.Add(outerBase + (j + 1) % outerCount);
                j++;
            }
        }
    }
}
