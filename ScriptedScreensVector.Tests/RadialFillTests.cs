using System.Collections.Generic;

namespace ScriptedScreensVector.Tests;

/// <summary>
/// Ring sizing and the stitch between rings of different point counts.
/// </summary>
/// <remarks>
/// Both halves fail in ways nobody would report as this bug. Oversized ring counts blow the
/// 60,000-vertex ceiling, which drops geometry silently — artwork simply missing, with no
/// warning. A stitch that skips an edge leaves a crack between two colour bands, which reads
/// as a gradient artefact rather than as a triangulation fault.
/// </remarks>
internal static class RadialFillTests
{
    private static void Eq(TestRun run, string name, double actual, double expected)
    {
        run.Check(name, actual, expected, 0.001d, 0, 0);
    }

    internal static void RingsFollowSize(TestRun run)
    {
        // A large shape wants a band every couple of pixels, up to the hard cap.
        Eq(run, "radial: 100px radius", RadialFill.Rings(100f, 2), 40d);
        Eq(run, "radial: 400px radius caps at 96", RadialFill.Rings(400f, 2), 96d);

        // A tiny one gets the floor, not the cap.
        Eq(run, "radial: 5px radius", RadialFill.Rings(5f, 2), 5d);
    }

    internal static void StopsCannotOutvoteTheScreen(TestRun run)
    {
        // THE BUG. A many-stop ramp used to force its ring count with no regard to how large
        // the shape actually is, so a six-pixel badge with a thirteen-stop gradient drew 96
        // rings -- about eight thousand vertices of detail finer than a pixel.
        Eq(run, "radial: 13 stops on a 6px shape", RadialFill.Rings(6f, 13), 6d);

        // Stops still win where there is room to show them: a 40px radius wants 16 by size,
        // and five stops ask for 32, which fits well inside what 40 pixels can resolve.
        Eq(run, "radial: 5 stops on a 40px shape", RadialFill.Rings(40f, 5), 32d);

        // And a two-stop ramp on a big shape is unaffected by any of this.
        Eq(run, "radial: 2 stops on a 200px shape", RadialFill.Rings(200f, 2), 80d);
    }

    internal static void InnerRingsAreCoarser(TestRun run)
    {
        // The outermost ring is the full outline; nothing is lost at the silhouette.
        Eq(run, "radial: outer ring keeps every point", RadialFill.Points(48, 96, 96), 48d);

        // Halfway out, half the circumference, half the points.
        Eq(run, "radial: half radius, half the points", RadialFill.Points(48, 48, 96), 24d);

        // Close to the focus it floors rather than collapsing to a triangle.
        Eq(run, "radial: innermost ring floors at 16", RadialFill.Points(48, 1, 96), 16d);

        // A shape whose outline is already coarse is left alone entirely.
        Eq(run, "radial: a coarse outline is untouched", RadialFill.Points(12, 1, 96), 12d);
    }

    internal static void TheSavingIsReal(TestRun run)
    {
        // The orb in GradientDemo: a 48-point circle at 96 rings. Flat rings cost 48*96+1.
        const int flat = 48 * 96 + 1;
        var now = RadialFill.VertexCount(48, 96);

        run.Check("radial: orb vertices (was 4609)", now, 2577d, 0.05d, 0, 0);

        run.Check("radial: orb is at least a third cheaper",
            now < flat * 0.67 ? 1d : 0d, 1d, 0.001d, 0, 0);
    }

    /// <summary>
    /// Every edge of both rings used exactly once, which is what "no cracks" means.
    /// </summary>
    private static void CheckStitch(TestRun run, int inner, int outer)
    {
        const int innerBase = 1000;
        const int outerBase = 2000;

        var tris = new List<int>();
        RadialFill.Stitch(innerBase, inner, outerBase, outer, tris);

        var triangles = tris.Count / 3;

        // One triangle per edge consumed. A one-point inner ring has no edges of its own, so
        // the annulus is a fan and the count is the outer ring's alone.
        var innerEdgeCount = inner < 2 ? 0 : inner;

        if (triangles != innerEdgeCount + outer)
        {
            run.Fail($"radial: stitch {inner}/{outer} made {triangles} triangles, expected {innerEdgeCount + outer}");
            return;
        }

        // Walk the triangles and tick off the edges they close. A gap shows as an edge that
        // no triangle covered; an overlap as one covered twice. Either is a rendering fault.
        var innerEdges = new int[innerEdgeCount];
        var outerEdges = new int[outer];
        var degenerate = 0;

        for (var k = 0; k < tris.Count; k += 3)
        {
            var a = tris[k];
            var b = tris[k + 1];
            var c = tris[k + 2];

            if (a == b || b == c || a == c)
                degenerate++;

            // An inner-advancing triangle is (inner i, outer j, inner i+1): two inner points.
            var innerPoints = (a >= innerBase && a < innerBase + inner ? 1 : 0)
                              + (b >= innerBase && b < innerBase + inner ? 1 : 0)
                              + (c >= innerBase && c < innerBase + inner ? 1 : 0);

            if (innerPoints == 2 && innerEdgeCount > 0)
                innerEdges[a - innerBase]++;
            else
                outerEdges[b - outerBase]++;
        }

        var missing = 0;
        var doubled = 0;

        foreach (var used in innerEdges)
        {
            if (used == 0) missing++;
            else if (used > 1) doubled++;
        }

        foreach (var used in outerEdges)
        {
            if (used == 0) missing++;
            else if (used > 1) doubled++;
        }

        if (missing == 0 && doubled == 0 && degenerate == 0)
            run.Pass($"radial: stitch {inner}/{outer} covers every edge once ({triangles} triangles)");
        else
            run.Fail($"radial: stitch {inner}/{outer} -- {missing} edges uncovered, {doubled} doubled, {degenerate} degenerate");
    }

    internal static void StitchCoversTheAnnulus(TestRun run)
    {
        CheckStitch(run, 16, 16);   // equal counts, the old behaviour
        CheckStitch(run, 16, 32);   // the doubling that happens as rings grow
        CheckStitch(run, 16, 17);   // one apart, where an off-by-one would hide
        CheckStitch(run, 16, 48);   // a three-fold jump
        CheckStitch(run, 7, 23);    // coprime, so no step ever lines up
        CheckStitch(run, 1, 16);    // a degenerate inner ring
    }

    internal static void OneFillCannotExhaustASurface(TestRun run)
    {
        // The mesh holds 250,000 vertices. A single radial fill has to stay well inside that
        // on its own, or one gradient could starve everything drawn after it -- which is the
        // failure this whole pass exists to make visible rather than to allow.
        //
        // Worst case: the hard 96-ring cap against the coarsest outline a shape can produce.
        // An ellipse tops out at 48 points and a rounded rect at 64, but a flattened path is
        // not bounded the same way, so 256 is a deliberately pessimistic stand-in.
        var ellipse = RadialFill.VertexCount(48, 96);
        var roundedRect = RadialFill.VertexCount(64, 96);
        var fatPath = RadialFill.VertexCount(256, 96);

        run.Check("radial: worst ellipse fill", ellipse, 2577d, 0.05d, 0, 0);
        run.Check("radial: worst rounded-rect fill", roundedRect, 3289d, 0.0001d, 0, 0);

        // Even the pessimistic one is a twentieth of the surface, so a page can hold plenty.
        run.Check("radial: even a 256-point outline stays under a tenth of the mesh",
            fatPath < 25000 ? 1d : 0d, 1d, 0.001d, 0, 0);

        // And the prediction the budget check relies on must never UNDER-count, or the check
        // passes and the emit overruns. It is the sum of the same Points() the emit loop uses,
        // so this pins that they stay in step.
        var summed = 1;
        for (var ring = 1; ring <= 96; ring++)
            summed += RadialFill.Points(64, ring, 96);

        run.Check("radial: the budget prediction matches the emit", roundedRect, summed, 0.0001d, 0, 0);
    }

    /// <summary>
    /// A bounding-box radial must take the band fill, whatever the shape's position.
    /// </summary>
    /// <remarks>
    /// The gate used to test the gradient's raw focus, which in `units = "bbox"` is a 0..1
    /// fraction, against the outline in shape space -- so a shape anywhere but the origin never
    /// qualified and fell into ear clipping plus subdivision. Reported at ~49,000 vertices for
    /// one 90x50 rounded rect. The test uses that exact box and focus, at its real position.
    /// </remarks>
    internal static void BoundingBoxRadialTakesTheBands(TestRun run)
    {
        var box = new List<UnityEngine.Vector2>
        {
            new(10f, 243f), new(100f, 243f), new(100f, 293f), new(10f, 293f),
        };

        var gradient = new ScriptedScreensVector.Gradient
        {
            Radial = true,
            BoundingBox = true,
            Focus = new UnityEngine.Vector2(0.3f, 0.3f),
        };

        var paint = new ScriptedScreensVector.Paint(UnityEngine.Color.white, gradient, 1f)
            .WithBounds(new UnityEngine.Vector2(10f, 243f), new UnityEngine.Vector2(90f, 50f));

        run.Check("radial: a bbox radial away from the origin takes the band fill",
            ScriptedScreensVector.Tessellator.RadialBandsFit(box, paint),
            "focus (0.3,0.3) of a box at x 10..100, y 243..293");

        // The other direction: a focus genuinely outside the shape must still be refused, or
        // the rings would be built around a point they cannot radiate from.
        var outside = new ScriptedScreensVector.Gradient
        {
            Radial = true,
            BoundingBox = true,
            Focus = new UnityEngine.Vector2(1.5f, 0.5f),
        };

        var outsidePaint = new ScriptedScreensVector.Paint(UnityEngine.Color.white, outside, 1f)
            .WithBounds(new UnityEngine.Vector2(10f, 243f), new UnityEngine.Vector2(90f, 50f));

        run.Check("radial: a focus outside the shape still falls back",
            !ScriptedScreensVector.Tessellator.RadialBandsFit(box, outsidePaint),
            "focus (1.5,0.5) is past the right edge");
    }

    /// <summary>
    /// The colour between ring vertices must follow the gradient, not the shape's corners.
    /// </summary>
    /// <remarks>
    /// Shipped broken once: rings were scaled copies of a rounded rect whose straight edges had
    /// no points, so colour along them was a blend between corners and the bands came out as
    /// rounded rectangles -- "a point with corners" in game, where the old subdivided fill had
    /// been smooth. This rebuilds the rings the way FillRadialBands does, through the real
    /// Densify, Points and Stitch, and compares the gradient parameter interpolated at every
    /// triangle's centre with the true value there. The gradient parameter stands in for
    /// colour: colour is a function of it, so an error in one is an error in the other.
    /// </remarks>
    internal static void BandsFollowTheGradientNotTheCorners(TestRun run)
    {
        // The reported case: 90x50 rounded rect, bbox radial, centre (0.3,0.3), r 0.71, at
        // about 1.4 screen pixels per unit.
        var min = new UnityEngine.Vector2(10f, 243f);
        var size = new UnityEngine.Vector2(90f, 50f);
        const float pixelsPerUnit = 1.4f;

        var shape = RoundedRect(min, size, 8f, 6);
        var focus = min + new UnityEngine.Vector2(0.3f * size.x, 0.3f * size.y);

        float T(UnityEngine.Vector2 p)
        {
            var g = new UnityEngine.Vector2((p.x - min.x) / size.x, (p.y - min.y) / size.y);
            return (g - new UnityEngine.Vector2(0.3f, 0.3f)).magnitude / 0.71f;
        }

        var raw = Worst(shape, focus, pixelsPerUnit, T);

        var dense = new List<UnityEngine.Vector2>();
        RadialFill.Densify(shape, RadialFill.PixelsPerBand / pixelsPerUnit, dense);
        var fixedError = Worst(dense, focus, pixelsPerUnit, T);

        run.Pass($"radial: worst colour error corners-only {raw:F3}, densified {fixedError:F3} (of a 0..1 ramp)");
        run.Check("radial: densified bands follow the gradient", fixedError < 0.02f, $"worst {fixedError:F3}");
        run.Check("radial: the corners-only outline really was wrong", raw > fixedError * 3f,
            $"{raw:F3} vs {fixedError:F3}");
    }

    private static float Worst(List<UnityEngine.Vector2> outline, UnityEngine.Vector2 focus, float pixelsPerUnit,
        System.Func<UnityEngine.Vector2, float> t)
    {
        var count = outline.Count;
        var radius = 0f;
        foreach (var point in outline)
            radius = UnityEngine.Mathf.Max(radius, (point - focus).magnitude);

        var rings = RadialFill.Rings(radius * pixelsPerUnit, 2);
        var verts = new List<UnityEngine.Vector2> { focus };
        var tris = new List<int>();
        var previousBase = 0;
        var previousCount = 0;

        for (var ring = 1; ring <= rings; ring++)
        {
            var ratio = ring / (float)rings;
            var points = RadialFill.Points(count, ring, rings);
            var thisBase = verts.Count;

            for (var k = 0; k < points; k++)
                verts.Add(focus + (outline[k * count / points] - focus) * ratio);

            if (ring == 1)
            {
                for (var k = 0; k < points; k++)
                    tris.AddRange(new[] { 0, thisBase + k, thisBase + (k + 1) % points });
            }
            else
            {
                RadialFill.Stitch(previousBase, previousCount, thisBase, points, tris);
            }

            previousBase = thisBase;
            previousCount = points;
        }

        var worst = 0f;
        for (var i = 0; i + 2 < tris.Count; i += 3)
        {
            var a = verts[tris[i]];
            var b = verts[tris[i + 1]];
            var c = verts[tris[i + 2]];
            var centre = (a + b + c) / 3f;
            var blended = (t(a) + t(b) + t(c)) / 3f;

            // The ramp clamps past its last stop at 0.7 of the radius; errors out there are
            // invisible, so only the part of the fill the ramp actually spans is judged.
            if (t(centre) > 0.7f)
                continue;

            worst = UnityEngine.Mathf.Max(worst, UnityEngine.Mathf.Abs(blended - t(centre)));
        }

        return worst;
    }

    private static List<UnityEngine.Vector2> RoundedRect(UnityEngine.Vector2 min, UnityEngine.Vector2 size, float r, int arc)
    {
        var points = new List<UnityEngine.Vector2>();
        var corners = new[]
        {
            (min + new UnityEngine.Vector2(size.x - r, r), -90f),
            (min + new UnityEngine.Vector2(size.x - r, size.y - r), 0f),
            (min + new UnityEngine.Vector2(r, size.y - r), 90f),
            (min + new UnityEngine.Vector2(r, r), 180f),
        };

        foreach (var (centre, start) in corners)
        {
            for (var k = 0; k <= arc; k++)
            {
                var angle = (start + 90f * k / arc) * UnityEngine.Mathf.Deg2Rad;
                points.Add(centre + new UnityEngine.Vector2(UnityEngine.Mathf.Cos(angle), UnityEngine.Mathf.Sin(angle)) * r);
            }
        }

        return points;
    }
}
