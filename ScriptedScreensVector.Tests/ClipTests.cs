using System;
using System.Collections.Generic;
using UnityEngine;

namespace ScriptedScreensVector.Tests;

/// <summary>
/// Regression tests for convex clipping.
/// </summary>
/// <remarks>
/// Both bugs these cover were sign errors — an inverted intersection parameter and a
/// swapped enter/exit bound. Neither could fail to compile, and both produced output that
/// looked like a modelling problem rather than a maths one: the clipped contours came out
/// self-intersecting, ear clipping stalled on them, and the screen filled with garbage.
///
/// Area and length are the assertions because they are checkable against arithmetic done by
/// hand, and because the visible symptom was always "far too much geometry".
/// </remarks>
internal static class ClipTests
{
    private static List<Vector2> Rect(float x, float y, float w, float h)
    {
        return new List<Vector2>
        {
            new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h),
        };
    }

    private static double Area(List<Vector2> points)
    {
        if (points.Count < 3)
            return 0d;

        var total = 0d;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            total += a.x * (double)b.y - b.x * (double)a.y;
        }

        return Math.Abs(total) * 0.5d;
    }

    /// <summary>A subject entirely inside the clip must come back unchanged in area.</summary>
    internal static void SubjectInside(TestRun run)
    {
        var region = ClipRegion.FromPolygon(Rect(0f, 0f, 100f, 100f))!;
        var clipped = region.ClipPolygon(Rect(20f, 20f, 30f, 30f));

        run.Check("clip: subject fully inside", Area(clipped), 30d * 30d, 0.001d, 0, clipped.Count);
    }

    /// <summary>
    /// A diagonal boundary is where the bounds fast path can go wrong: a subject can sit
    /// wholly inside the region's bounding box and still be outside the region itself.
    /// </summary>
    /// <remarks>
    /// The fast path skips clipping when a subject falls inside a box computed to be
    /// interior to the region. Get that box too large and clipping silently stops happening
    /// on some shapes — geometry escapes its window and nothing warns, which is a far worse
    /// failure than clipping too much. A diamond is the sharpest test of it: its bounding
    /// box is twice its area, so half the box must NOT qualify as inside.
    /// </remarks>
    internal static void DiagonalBoundaryStillClips(TestRun run)
    {
        var diamond = new List<Vector2>
        {
            new(50f, 0f), new(100f, 50f), new(50f, 100f), new(0f, 50f),
        };

        var region = ClipRegion.FromPolygon(diamond)!;

        // Sits in the bounding box's top-left corner, entirely outside the diamond.
        var corner = region.ClipPolygon(Rect(2f, 2f, 14f, 14f));
        run.Check("clip: diamond rejects a bbox corner", Area(corner), 0d, 0.001d, 0, corner.Count);

        // Straddles the upper-left edge x+y=50: the (20,20) corner is outside, and the cut
        // takes a right triangle with 10-unit legs off a 400-unit square.
        var straddle = region.ClipPolygon(Rect(20f, 20f, 20f, 20f));
        run.Check("clip: diamond cuts a straddling square", Area(straddle), 350d, 0.001d, 0, straddle.Count);

        // Dead centre, well inside: must survive whole.
        var centre = region.ClipPolygon(Rect(45f, 45f, 10f, 10f));
        run.Check("clip: diamond keeps its centre", Area(centre), 100d, 0.001d, 0, centre.Count);
    }

    /// <summary>
    /// The buffered overload must agree with the allocating one, including when the caller
    /// reuses the same buffer across calls.
    /// </summary>
    internal static void BufferedMatchesAllocating(TestRun run)
    {
        var region = ClipRegion.FromPolygon(Rect(0f, 0f, 100f, 100f))!;
        var buffer = new List<Vector2>(8);

        // A straddling subject, then an outside one, then a straddling one again. A buffer
        // left dirty by the middle call would show up as the third result being wrong.
        region.ClipPolygon(Rect(80f, 80f, 40f, 40f), buffer);
        var first = Area(buffer);

        region.ClipPolygon(Rect(500f, 500f, 10f, 10f), buffer);
        var empty = Area(buffer);

        region.ClipPolygon(Rect(80f, 80f, 40f, 40f), buffer);
        var again = Area(buffer);

        run.Check("clip: buffered overload matches", first, 20d * 20d, 0.001d, 0, buffer.Count);
        run.Check("clip: buffer cleared when empty", empty, 0d, 0.001d, 0, 0);
        run.Check("clip: buffer reusable after empty", again, 20d * 20d, 0.001d, 0, buffer.Count);
    }

    /// <summary>
    /// ClampInside walks from an interior point toward an exterior one and returns the
    /// furthest point still inside. Its fast path skips that walk for points inside the
    /// proven-interior box, so it has the same failure direction as the clip fast path:
    /// too large a box and a feather halo escapes its window silently.
    /// </summary>
    internal static void ClampInsideRespectsBoundary(TestRun run)
    {
        var diamond = new List<Vector2>
        {
            new(50f, 0f), new(100f, 50f), new(50f, 100f), new(0f, 50f),
        };

        var region = ClipRegion.FromPolygon(diamond)!;

        // Straight up from the centre: the boundary is at y=0, so it must stop there.
        var clamped = region.ClampInside(new Vector2(50f, 50f), new Vector2(50f, -20f));
        run.Check("clamp: stops at the boundary", clamped.y, 0d, 0.001d, 0, 0);

        // Toward the diagonal edge x+y=50, well outside it.
        var diagonal = region.ClampInside(new Vector2(50f, 50f), new Vector2(10f, 10f));
        run.Check("clamp: stops on a diagonal edge", diagonal.x + diagonal.y, 50d, 0.01d, 0, 0);

        // A short step that stays inside must come back untouched.
        var interior = region.ClampInside(new Vector2(50f, 50f), new Vector2(52f, 52f));
        run.Check("clamp: interior point untouched", interior.x + interior.y, 104d, 0.001d, 0, 0);
    }

    /// <summary>A subject entirely outside must be removed, not relocated.</summary>
    internal static void SubjectOutside(TestRun run)
    {
        var region = ClipRegion.FromPolygon(Rect(0f, 0f, 50f, 50f))!;
        var clipped = region.ClipPolygon(Rect(200f, 200f, 30f, 30f));

        run.Check("clip: subject fully outside", Area(clipped), 0d, 0.001d, 0, clipped.Count);
    }

    /// <summary>
    /// Partial overlap: the whole point of clipping, and where a wrong intersection sign
    /// shows up as a wildly oversized or inside-out polygon.
    /// </summary>
    internal static void PartialOverlap(TestRun run)
    {
        var region = ClipRegion.FromPolygon(Rect(0f, 0f, 100f, 100f))!;

        // Overlaps the top-right quadrant of the region: 40 x 30 of it lies inside.
        var clipped = region.ClipPolygon(Rect(60f, 70f, 80f, 60f));

        run.Check("clip: partial overlap", Area(clipped), 40d * 30d, 0.001d, 0, clipped.Count);
    }

    /// <summary>A subject larger than the clip must come back as the clip itself.</summary>
    internal static void SubjectSwallowsRegion(TestRun run)
    {
        var region = ClipRegion.FromPolygon(Rect(30f, 30f, 40f, 40f))!;
        var clipped = region.ClipPolygon(Rect(-500f, -500f, 1000f, 1000f));

        run.Check("clip: subject covers region", Area(clipped), 40d * 40d, 0.001d, 0, clipped.Count);
    }

    /// <summary>
    /// A clipped contour must remain a simple polygon: it is fed straight to the ear
    /// clipper, and a self-intersecting result is exactly what stalled it in game.
    /// </summary>
    internal static void ClippedContourTriangulates(TestRun run)
    {
        var region = ClipRegion.FromPolygon(Rect(108f, 108f, 84f, 84f))!;

        // The bar shape from GradientDemo: overhangs the clip window top and bottom.
        var clipped = region.ClipPolygon(Rect(133f, 96f, 7f, 108f));

        var before = ScriptedScreensVectorPlugin.Log!.Warnings.Count;
        Triangulator.Triangulate(clipped, null, out var vertices, out var indices);
        var warnings = ScriptedScreensVectorPlugin.Log.Warnings.Count - before;

        var area = 0d;
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var a = vertices[indices[i]];
            var b = vertices[indices[i + 1]];
            var c = vertices[indices[i + 2]];
            area += Math.Abs((b.x - a.x) * (double)(c.y - a.y) - (b.y - a.y) * (double)(c.x - a.x)) * 0.5d;
        }

        run.Check("clip: result triangulates", area, 7d * 84d, 0.001d, warnings, indices.Count / 3);
    }

    /// <summary>
    /// Clipping an open path keeps the inside runs. A swapped enter/exit bound keeps the
    /// outside instead, which has the same total length in a symmetric case — so the check
    /// is on where the run is, not merely how long it is.
    /// </summary>
    internal static void PolylineCut(TestRun run)
    {
        var region = ClipRegion.FromPolygon(Rect(0f, 0f, 100f, 100f))!;

        // Straight across the region and out the far side: one run, from x=0 to x=100.
        var runs = region.ClipPolyline(new List<Vector2> { new(-50f, 50f), new(150f, 50f) });

        if (runs.Count != 1)
        {
            run.Fail($"clip: polyline cut expected 1 run, got {runs.Count}");
            return;
        }

        var piece = runs[0];
        var length = Vector2.Distance(piece[0], piece[^1]);

        var insideEnds = piece[0].x >= -0.01f && piece[0].x <= 100.01f
                         && piece[^1].x >= -0.01f && piece[^1].x <= 100.01f;

        if (!insideEnds)
        {
            run.Fail($"clip: polyline run lies outside the region ({piece[0]} -> {piece[^1]})");
            return;
        }

        run.Check("clip: polyline cut", length, 100d, 0.001d, 0, runs.Count);
    }

    /// <summary>A path that misses the region entirely yields nothing.</summary>
    internal static void PolylineMisses(TestRun run)
    {
        var region = ClipRegion.FromPolygon(Rect(0f, 0f, 100f, 100f))!;
        var runs = region.ClipPolyline(new List<Vector2> { new(-50f, 500f), new(150f, 500f) });

        if (runs.Count != 0)
        {
            run.Fail($"clip: polyline that misses should yield 0 runs, got {runs.Count}");
            return;
        }

        run.Check("clip: polyline misses", 0d, 0d, 0.001d, 0, 0);
    }
}
