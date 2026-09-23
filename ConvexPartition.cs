using System.Collections.Generic;
using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>
/// Splits a simple polygon into convex pieces, so a concave clip can be served by the convex
/// clipper one piece at a time.
/// </summary>
/// <remarks>
/// Hertel–Mehlhorn: ear-clip into triangles, then merge neighbours across shared diagonals
/// while the merged polygon stays convex. At most four times the minimum piece count, and a
/// clip outline is tens of points, so the quadratic merge loop is not worth improving.
///
/// The pieces tile the polygon exactly -- every diagonal is shared by the two pieces it
/// separates -- so geometry clipped to each and drawn together is geometry clipped to the
/// whole, with no gap and no double coverage.
/// </remarks>
internal static class ConvexPartition
{
    internal static List<List<Vector2>> Split(List<Vector2> polygon)
    {
        var result = new List<List<Vector2>>();
        if (polygon.Count < 3)
            return result;

        if (Tessellator.IsConvex(polygon))
        {
            result.Add(new List<Vector2>(polygon));
            return result;
        }

        if (!Triangulator.Triangulate(polygon, null, out var vertices, out var indices) || indices.Count < 3)
            return result;

        // Pieces as vertex-index loops, all wound the same way as the triangles.
        var pieces = new List<List<int>>();
        for (var t = 0; t + 2 < indices.Count; t += 3)
        {
            var a = indices[t];
            var b = indices[t + 1];
            var c = indices[t + 2];
            var area = Cross(vertices[a], vertices[b], vertices[c]);
            if (Mathf.Abs(area) < 1e-8f)
                continue;

            pieces.Add(area > 0f ? new List<int> { a, b, c } : new List<int> { a, c, b });
        }

        var merged = true;
        while (merged)
        {
            merged = false;

            for (var p = 0; p < pieces.Count && !merged; p++)
            {
                for (var q = p + 1; q < pieces.Count && !merged; q++)
                {
                    var joined = Join(pieces[p], pieces[q]);
                    if (joined == null || !Convex(joined, vertices))
                        continue;

                    pieces[p] = joined;
                    pieces.RemoveAt(q);
                    merged = true;
                }
            }
        }

        foreach (var piece in pieces)
        {
            var points = new List<Vector2>(piece.Count);
            foreach (var index in piece)
                points.Add(vertices[index]);

            result.Add(points);
        }

        return result;
    }

    /// <summary>Two loops sharing an edge (u,v in one, v,u in the other), spliced; null otherwise.</summary>
    private static List<int>? Join(List<int> a, List<int> b)
    {
        for (var i = 0; i < a.Count; i++)
        {
            var u = a[i];
            var v = a[(i + 1) % a.Count];

            for (var j = 0; j < b.Count; j++)
            {
                if (b[j] != v || b[(j + 1) % b.Count] != u)
                    continue;

                // a from v round to u, then b's vertices strictly between u and v.
                var joined = new List<int>(a.Count + b.Count - 2);
                for (var k = 0; k < a.Count; k++)
                    joined.Add(a[(i + 1 + k) % a.Count]);

                for (var k = 2; k < b.Count; k++)
                    joined.Add(b[(j + k) % b.Count]);

                return joined;
            }
        }

        return null;
    }

    private static bool Convex(List<int> loop, List<Vector2> vertices)
    {
        for (var i = 0; i < loop.Count; i++)
        {
            var cross = Cross(vertices[loop[i]], vertices[loop[(i + 1) % loop.Count]], vertices[loop[(i + 2) % loop.Count]]);
            if (cross < -1e-6f)
                return false;
        }

        return true;
    }

    private static float Cross(Vector2 a, Vector2 b, Vector2 c)
    {
        return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
    }
}
