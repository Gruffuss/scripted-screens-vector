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
        var bySize = Mathf.CeilToInt(screenRadius / 2.5f);
        var byStops = 8 * Mathf.Max(1, stopCount - 1);

        // One band per screen pixel of radius is already past the point of visibility, so it
        // is a ceiling rather than a target -- it only ever removes work nobody can see.
        var visible = Mathf.Max(MinRings, Mathf.FloorToInt(screenRadius));

        var wanted = Mathf.Max(bySize, byStops);
        return Mathf.Clamp(Mathf.Min(wanted, visible), MinRings, MaxRings);
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
