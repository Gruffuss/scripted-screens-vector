using System;
using System.Collections.Generic;
using UnityEngine;

namespace ScriptedScreensVector.Tests;

/// <summary>
/// Regression tests for the fill bugs found by eye in <c>PathDemo.lua</c>.
/// </summary>
/// <remarks>
/// Each of these shipped once, compiled cleanly, and was only caught by looking at a
/// screenshot. They share a failure mode: ear clipping stalls, returns a partial result, and
/// the shape renders with a bite out of it. Area is the assertion that catches all of them,
/// because a stall always loses area and no correct triangulation can.
/// </remarks>
internal static class TriangulatorTests
{
    /// <summary>Sum of triangle areas actually produced.</summary>
    private static double TriangulatedArea(List<Vector2> outer, List<List<Vector2>>? holes, out int leftover, out int triangles)
    {
        var before = ScriptedScreensVectorPlugin.Log!.Warnings.Count;

        if (!Triangulator.Triangulate(outer, holes, out var vertices, out var indices))
        {
            // Total failure to produce any geometry. Counted as a warning so the harness
            // reports it as a failure rather than as a suspiciously tidy zero.
            leftover = 1;
            triangles = 0;
            return 0d;
        }

        var total = 0d;
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var a = vertices[indices[i]];
            var b = vertices[indices[i + 1]];
            var c = vertices[indices[i + 2]];
            total += Math.Abs((b.x - a.x) * (double)(c.y - a.y) - (b.y - a.y) * (double)(c.x - a.x)) * 0.5d;
        }

        triangles = indices.Count / 3;

        // A stall is reported by the triangulator; treat any new warning as failure so the
        // silent-partial-result mode cannot come back.
        leftover = ScriptedScreensVectorPlugin.Log.Warnings.Count - before;
        return total;
    }

    private static List<Vector2> Circle(float cx, float cy, float r, int segments, bool clockwise)
    {
        var points = new List<Vector2>(segments);
        for (var i = 0; i < segments; i++)
        {
            var angle = Mathf.PI * 2f * i / segments * (clockwise ? -1f : 1f);
            points.Add(new Vector2(cx + Mathf.Cos(angle) * r, cy + Mathf.Sin(angle) * r));
        }

        return points;
    }

    private static List<Vector2> Star(float cx, float cy, float outer, float inner, int points)
    {
        var result = new List<Vector2>(points * 2);
        for (var i = 0; i < points * 2; i++)
        {
            var radius = i % 2 == 0 ? outer : inner;
            var angle = Mathf.PI * i / points - Mathf.PI * 0.5f;
            result.Add(new Vector2(cx + Mathf.Cos(angle) * radius, cy + Mathf.Sin(angle) * radius));
        }

        return result;
    }

    /// <summary>
    /// A square hole in a square. Exact arithmetic, so the tolerance can be tight.
    /// </summary>
    /// <remarks>
    /// This is the bridging bug. The seam must run
    /// <c>outer → hole → around → hole start → outer</c>; the original emitted a leading
    /// duplicate and never returned to the outer contour, leaving an edge that cut a chord
    /// across the shape. Area catches it: the chord swallows part of the fill.
    /// </remarks>
    internal static bool SquareWithSquareHole(TestRun run)
    {
        var outer = new List<Vector2>
        {
            new(0f, 0f), new(100f, 0f), new(100f, 100f), new(0f, 100f),
        };

        var hole = new List<Vector2>
        {
            new(40f, 40f), new(40f, 60f), new(60f, 60f), new(60f, 40f),
        };

        var area = TriangulatedArea(outer, new List<List<Vector2>> { hole }, out var warnings, out var triangles);

        return run.Check("square with square hole", area, 100d * 100d - 20d * 20d, 0.001d, warnings, triangles);
    }

    /// <summary>
    /// A ring: the shape that rendered as a circle with a wedge missing.
    /// </summary>
    /// <remarks>
    /// Two bugs had to be fixed before this passed — the seam, and the ear test rejecting
    /// the seam's *intentionally* duplicated vertices because it compared positions rather
    /// than indices. Fixing only the first left the picture unchanged, which is what made
    /// the bug take two attempts.
    /// </remarks>
    internal static bool ConcentricRing(TestRun run)
    {
        const int segments = 64;
        var outer = Circle(110f, 41f, 29f, segments, clockwise: false);
        var hole = Circle(110f, 41f, 15f, segments / 2, clockwise: true);

        var area = TriangulatedArea(outer, new List<List<Vector2>> { hole }, out var warnings, out var triangles);

        // Polygons inscribe their circles, so compare against polygon area, not πr².
        var expected = PolygonArea(outer) - PolygonArea(hole);

        return run.Check("concentric ring (hole)", area, expected, 0.002d, warnings, triangles);
    }

    /// <summary>
    /// A closed contour whose last point repeats its first: the heart's dark seam.
    /// </summary>
    /// <remarks>
    /// Any path ending in <c>Z</c> at its start point produces this. The duplicate gives ear
    /// clipping a zero-length edge and the degenerate triangle shows as a crack.
    /// </remarks>
    internal static bool DuplicatedClosingVertex(TestRun run)
    {
        var square = new List<Vector2>
        {
            new(0f, 0f), new(50f, 0f), new(50f, 50f), new(0f, 50f), new(0f, 0f),
        };

        var area = TriangulatedArea(square, null, out var warnings, out var triangles);

        // Area alone is not enough here. Emitting the duplicate as a zero-area triangle
        // keeps the total area correct while still producing geometry that should not
        // exist, so assert the count too: four distinct corners must give exactly two
        // triangles, not three.
        if (triangles != 2)
        {
            run.Fail($"duplicated closing vertex: expected 2 triangles, got {triangles} (degenerate emitted)");
            return false;
        }

        return run.Check("duplicated closing vertex", area, 50d * 50d, 0.001d, warnings, triangles);
    }

    /// <summary>A five-pointed star: concave, and the case a centre fan cannot draw.</summary>
    internal static bool ConcaveStar(TestRun run)
    {
        var star = Star(0f, 0f, 40f, 16f, 5);
        var area = TriangulatedArea(star, null, out var warnings, out var triangles);

        // A simple polygon of n vertices always yields exactly n-2 triangles.
        if (triangles != star.Count - 2)
        {
            run.Fail($"concave star: expected {star.Count - 2} triangles, got {triangles}");
            return false;
        }

        return run.Check("concave star", area, PolygonArea(star), 0.001d, warnings, triangles);
    }

    /// <summary>Two holes in one contour, to check bridging survives being applied twice.</summary>
    internal static bool TwoHoles(TestRun run)
    {
        var outer = new List<Vector2>
        {
            new(0f, 0f), new(120f, 0f), new(120f, 60f), new(0f, 60f),
        };

        var holes = new List<List<Vector2>>
        {
            new() { new(20f, 20f), new(20f, 40f), new(40f, 40f), new(40f, 20f) },
            new() { new(80f, 20f), new(80f, 40f), new(100f, 40f), new(100f, 20f) },
        };

        var area = TriangulatedArea(outer, holes, out var warnings, out var triangles);

        return run.Check("two holes", area, 120d * 60d - 2 * 20d * 20d, 0.001d, warnings, triangles);
    }

    private static double PolygonArea(List<Vector2> points)
    {
        var total = 0d;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            total += a.x * (double)b.y - b.x * (double)a.y;
        }

        return Math.Abs(total) * 0.5d;
    }
}
