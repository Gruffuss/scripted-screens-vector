using System.Collections.Generic;
using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>
/// Ear-clipping triangulation for arbitrary simple polygons, with holes.
/// </summary>
/// <remarks>
/// Replaces the centre-fan used for convex outlines. A fan is only valid when every vertex
/// can see the centre; on a concave shape it folds triangles over the outside, which is why
/// filled concave polygons were unsupported until now.
///
/// Holes are handled by bridging: the hole's rightmost vertex is joined to a visible vertex
/// on the outer contour by a doubled-back seam, turning "outer plus hole" into one simple
/// polygon the ear clipper can chew. The seam is zero-area so it never shows.
///
/// Not handled: self-intersecting contours. Ear clipping is undefined on those, and the
/// output would be visibly wrong rather than merely imperfect. Detecting them is more
/// expensive than the fill itself, so the contract is "give it simple polygons".
/// </remarks>
internal static class Triangulator
{
    private const float Epsilon = 1e-6f;

    /// <summary>
    /// Triangulates one outer contour with optional holes. Returns index triples into the
    /// returned vertex list, which may differ from the input because bridging duplicates
    /// the seam vertices.
    /// </summary>
    internal static bool Triangulate(List<Vector2> outer, List<List<Vector2>>? holes, out List<Vector2> vertices, out List<int> indices)
    {
        // A closed contour whose last point repeats its first -- which any path ending in
        // Z at its start point produces -- gives ear clipping a zero-length edge, and the
        // degenerate triangle shows as a crack. Strip coincident points first.
        vertices = Dedupe(outer);
        indices = new List<int>();

        if (vertices.Count < 3)
            return false;

        // Ear clipping wants a consistent winding; normalise to counter-clockwise.
        if (SignedArea(vertices) < 0f)
            vertices.Reverse();

        if (holes != null)
        {
            foreach (var hole in holes)
            {
                if (hole.Count >= 3)
                    Bridge(vertices, hole);
            }
        }

        var remaining = new List<int>(vertices.Count);
        for (var i = 0; i < vertices.Count; i++)
            remaining.Add(i);

        // Each successful clip removes one vertex; the guard stops a malformed contour
        // (self-intersecting, or degenerate after bridging) from spinning forever.
        var guard = remaining.Count * remaining.Count;

        while (remaining.Count > 3 && guard-- > 0)
        {
            var clipped = false;

            for (var i = 0; i < remaining.Count; i++)
            {
                var previous = remaining[(i - 1 + remaining.Count) % remaining.Count];
                var current = remaining[i];
                var next = remaining[(i + 1) % remaining.Count];

                if (!IsEar(vertices, remaining, previous, current, next))
                    continue;

                indices.Add(previous);
                indices.Add(current);
                indices.Add(next);
                remaining.RemoveAt(i);
                clipped = true;
                break;
            }

            if (!clipped)
                break;
        }

        if (remaining.Count == 3)
        {
            indices.Add(remaining[0]);
            indices.Add(remaining[1]);
            indices.Add(remaining[2]);
        }
        else if (remaining.Count > 3)
        {
            // Say so. A stall leaves the shape partly filled, which reads as a wedge or a
            // bite taken out of it — easy to mistake for a modelling mistake in the scene
            // rather than a triangulation failure.
            ScriptedScreensVectorPlugin.Log?.LogWarning(
                $"triangulation stalled with {remaining.Count} of {vertices.Count} vertices left; " +
                "contour is likely self-intersecting");
        }

        return indices.Count >= 3;
    }

    private static List<Vector2> Dedupe(List<Vector2> points)
    {
        var cleaned = new List<Vector2>(points.Count);

        foreach (var point in points)
        {
            if (cleaned.Count == 0 || (point - cleaned[^1]).sqrMagnitude > Epsilon)
                cleaned.Add(point);
        }

        while (cleaned.Count > 1 && (cleaned[0] - cleaned[^1]).sqrMagnitude <= Epsilon)
            cleaned.RemoveAt(cleaned.Count - 1);

        return cleaned;
    }

    private static bool IsEar(List<Vector2> vertices, List<int> remaining, int a, int b, int c)
    {
        var pa = vertices[a];
        var pb = vertices[b];
        var pc = vertices[c];

        // Reflex vertices cannot be ear tips under CCW winding.
        if (Cross(pa, pb, pc) <= Epsilon)
            return false;

        foreach (var index in remaining)
        {
            if (index == a || index == b || index == c)
                continue;

            var point = vertices[index];

            // Skip vertices that coincide with a corner rather than merely sharing its
            // index. Bridging a hole intentionally duplicates the seam vertices, and a
            // duplicate sits exactly on its twin: judged by position it is always "inside"
            // the triangle, so every ear touching the seam gets rejected, clipping stalls,
            // and the shape is left partly untriangulated.
            if ((point - pa).sqrMagnitude <= Epsilon ||
                (point - pb).sqrMagnitude <= Epsilon ||
                (point - pc).sqrMagnitude <= Epsilon)
            {
                continue;
            }

            if (InTriangle(point, pa, pb, pc))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Joins a hole into the outer contour with a zero-area seam.
    /// </summary>
    private static void Bridge(List<Vector2> outer, List<Vector2> hole)
    {
        var working = Dedupe(hole);
        if (working.Count < 3)
            return;

        // A hole must wind opposite to the outer contour or the seam turns it inside out.
        if (SignedArea(working) > 0f)
            working.Reverse();

        // Start from the hole's rightmost vertex: the ray cast to find a visible outer
        // vertex is then guaranteed to leave the hole immediately.
        var rightmost = 0;
        for (var i = 1; i < working.Count; i++)
        {
            if (working[i].x > working[rightmost].x)
                rightmost = i;
        }

        var target = working[rightmost];

        var best = -1;
        var bestDistance = float.MaxValue;

        for (var i = 0; i < outer.Count; i++)
        {
            if (outer[i].x < target.x)
                continue;

            var distance = (outer[i] - target).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        // Nothing to the right: fall back to the nearest vertex in any direction.
        if (best < 0)
        {
            for (var i = 0; i < outer.Count; i++)
            {
                var distance = (outer[i] - target).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
        }

        if (best < 0)
            return;

        // The seam runs out along the bridge, all the way around the hole, back to where
        // it entered, and then back to the outer contour. Both duplicated points are
        // required: they are what makes the bridge zero-area so it never shows.
        //
        //   ... outer[best] -> hole[r] -> hole[r+1] -> ... -> hole[r] -> outer[best] ...
        //
        // Omitting the return to outer[best] leaves an edge from the hole straight to
        // outer[best+1], which cuts a visible chord across the shape.
        var seam = new List<Vector2>(working.Count + 2);
        for (var i = 0; i < working.Count; i++)
            seam.Add(working[(rightmost + i) % working.Count]);

        seam.Add(target);
        seam.Add(outer[best]);

        outer.InsertRange(best + 1, seam);
    }

    private static bool InTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        var d1 = Cross(point, a, b);
        var d2 = Cross(point, b, c);
        var d3 = Cross(point, c, a);

        var negative = d1 < -Epsilon || d2 < -Epsilon || d3 < -Epsilon;
        var positive = d1 > Epsilon || d2 > Epsilon || d3 > Epsilon;

        return !(negative && positive);
    }

    private static float Cross(Vector2 a, Vector2 b, Vector2 c)
    {
        return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
    }

    internal static float SignedArea(List<Vector2> points)
    {
        var total = 0f;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            total += a.x * b.y - b.x * a.y;
        }

        return total * 0.5f;
    }
}
