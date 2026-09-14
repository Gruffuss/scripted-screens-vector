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
}
