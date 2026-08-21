using System;
using System.Collections.Generic;
using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>
/// A convex clip region, held as the half-planes of its boundary.
/// </summary>
/// <remarks>
/// Convex only, as spec §7 states. That restriction is what keeps clipping purely
/// geometric: a convex region is an intersection of half-planes, so clipping is a sequence
/// of independent passes with no stencil buffer and no cost at draw time. A concave clip
/// cannot be expressed that way and needs a different mechanism entirely.
///
/// Regions live in the local coordinate space of the group that declared them. Entering a
/// child group transforms the region by the inverse of that group's local matrix, so a
/// clip keeps clipping the same *scene* area however its descendants are transformed.
/// </remarks>
internal sealed class ClipRegion
{
    private readonly List<Vector2> _boundary;
    private readonly float _winding;

    // Bounds fast paths. Sutherland-Hodgman costs one pass over the subject PER BOUNDARY
    // EDGE, and a rounded-rect clip has around forty of them. A clipped band of 40 samples
    // therefore ran ~1,600 edge tests per quad, and a real console did that 184 times over.
    // Almost every one of those quads is wholly inside the clip and needs no work at all.
    //
    // _outer is the region's bounding box: a subject outside it is gone.
    // _inner is a box guaranteed wholly INSIDE the region: a subject inside it is untouched.
    // For an axis-aligned rectangular clip the two coincide, so clipping becomes free.
    private readonly Vector2 _outerMin;
    private readonly Vector2 _outerMax;
    private readonly Vector2 _innerMin;
    private readonly Vector2 _innerMax;

    private ClipRegion(List<Vector2> boundary, float winding)
    {
        _boundary = boundary;
        _winding = winding;

        var min = boundary[0];
        var max = boundary[0];
        var centroid = Vector2.zero;

        foreach (var point in boundary)
        {
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
            centroid += point;
        }

        centroid /= boundary.Count;
        _outerMin = min;
        _outerMax = max;

        // The largest box centred on the centroid, with the same aspect as the bounding box,
        // that satisfies every half-plane. A box of half-extents (ex, ey) lies inside edge i
        // when ex*|nx| + ey*|ny| <= distance(centroid, edge). Scaling both extents by one
        // factor k reduces that to a minimum over the edges. The centroid of a convex
        // polygon is interior, so every distance is positive and k is well defined.
        var halfX = (max.x - min.x) * 0.5f;
        var halfY = (max.y - min.y) * 0.5f;
        var k = 1f;

        for (var i = 0; i < boundary.Count; i++)
        {
            var a = boundary[i];
            var b = boundary[(i + 1) % boundary.Count];

            var normal = new Vector2(-(b.y - a.y), b.x - a.x) * winding;
            var length = normal.magnitude;
            if (length < 0.000001f)
                continue;

            normal /= length;

            var reach = Mathf.Abs(normal.x) * halfX + Mathf.Abs(normal.y) * halfY;
            if (reach < 0.000001f)
                continue;

            k = Mathf.Min(k, Vector2.Dot(normal, centroid - a) / reach);
        }

        k = Mathf.Clamp01(k);
        _innerMin = new Vector2(centroid.x - halfX * k, centroid.y - halfY * k);
        _innerMax = new Vector2(centroid.x + halfX * k, centroid.y + halfY * k);
    }

    /// <summary>True when every point is proven interior, so clipping would change nothing.</summary>
    /// <remarks>
    /// Lets a caller keep a whole-shape fast path rather than decomposing into pieces that
    /// each get clipped. A band wholly inside its tank can stay one shared-vertex strip.
    /// </remarks>
    internal bool Covers(List<Vector2> a, List<Vector2> b)
    {
        return Covers(a) && Covers(b);
    }

    private bool Covers(List<Vector2> points)
    {
        foreach (var p in points)
        {
            if (p.x < _innerMin.x || p.x > _innerMax.x || p.y < _innerMin.y || p.y > _innerMax.y)
                return false;
        }

        return true;
    }

    private enum Coverage { Inside, Outside, Straddles }

    private Coverage Classify(List<Vector2> subject)
    {
        var min = subject[0];
        var max = subject[0];

        for (var i = 1; i < subject.Count; i++)
        {
            min = Vector2.Min(min, subject[i]);
            max = Vector2.Max(max, subject[i]);
        }

        if (max.x < _outerMin.x || min.x > _outerMax.x || max.y < _outerMin.y || min.y > _outerMax.y)
            return Coverage.Outside;

        if (min.x >= _innerMin.x && max.x <= _innerMax.x && min.y >= _innerMin.y && max.y <= _innerMax.y)
            return Coverage.Inside;

        return Coverage.Straddles;
    }

    internal static ClipRegion? FromPolygon(List<Vector2> polygon)
    {
        if (polygon.Count < 3)
            return null;

        var area = Triangulator.SignedArea(polygon);
        if (Mathf.Abs(area) < 0.000001f)
            return null;

        return new ClipRegion(new List<Vector2>(polygon), Mathf.Sign(area));
    }

    /// <summary>Re-expresses this region in another coordinate space.</summary>
    internal ClipRegion Transform(Matrix4x4 matrix)
    {
        var moved = new List<Vector2>(_boundary.Count);
        foreach (var point in _boundary)
            moved.Add(matrix.MultiplyPoint3x4(point));

        return new ClipRegion(moved, Mathf.Sign(Triangulator.SignedArea(moved)));
    }

    private float Side(Vector2 a, Vector2 b, Vector2 p)
    {
        return ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x)) * _winding;
    }

    internal bool Contains(Vector2 point)
    {
        for (var i = 0; i < _boundary.Count; i++)
        {
            if (Side(_boundary[i], _boundary[(i + 1) % _boundary.Count], point) < -0.0001f)
                return false;
        }

        return true;
    }

    // Ping-pong buffers for clipping. Sutherland-Hodgman is a pass per boundary edge, and
    // allocating a fresh list for each of them is ruinous: a rounded-rect clip has around
    // forty boundary points, so clipping ONE quad allocated forty lists. A clipped band of
    // 40 samples did that per quad. Measured in game as 73 us per band, 13.4 ms across 184
    // of them — the single largest cost in a real console.
    // [ThreadStatic]: tessellation runs on worker threads, and two surfaces clipping at the
    // same time through one shared buffer would silently interleave their geometry.
    [ThreadStatic] private static List<Vector2>? _clipScratch;

    private static List<Vector2> ClipScratch => _clipScratch ??= new List<Vector2>(64);

    /// <summary>
    /// Sutherland–Hodgman into a caller-owned buffer, allocating nothing.
    /// </summary>
    internal List<Vector2> ClipPolygon(List<Vector2> subject, List<Vector2> output)
    {
        switch (Classify(subject))
        {
            // Returned unchanged, not copied. Both call sites only read the result.
            case Coverage.Inside:
                return subject;

            case Coverage.Outside:
                output.Clear();
                return output;
        }

        var front = output;
        var back = ClipScratch;

        front.Clear();
        front.AddRange(subject);

        for (var edge = 0; edge < _boundary.Count && front.Count > 0; edge++)
        {
            var a = _boundary[edge];
            var b = _boundary[(edge + 1) % _boundary.Count];

            back.Clear();

            for (var i = 0; i < front.Count; i++)
            {
                var current = front[i];
                var next = front[(i + 1) % front.Count];

                var currentIn = Side(a, b, current) >= 0f;
                var nextIn = Side(a, b, next) >= 0f;

                if (currentIn)
                    back.Add(current);

                if (currentIn != nextIn)
                    back.Add(Intersect(a, b, current, next));
            }

            (front, back) = (back, front);
        }

        // An odd number of edges leaves the result in the scratch buffer.
        if (!ReferenceEquals(front, output))
        {
            output.Clear();
            output.AddRange(front);
        }

        return output;
    }

    /// <summary>Allocating overload, for the rare callers that keep the result.</summary>
    internal List<Vector2> ClipPolygon(List<Vector2> subject)
    {
        return ClipPolygon(subject, new List<Vector2>(subject.Count + 8));
    }

    /// <summary>
    /// Clips an open path into the runs of it that fall inside, so a stroke is cut rather
    /// than redirected along the boundary the way a filled contour would be.
    /// </summary>
    internal List<List<Vector2>> ClipPolyline(List<Vector2> points)
    {
        var runs = new List<List<Vector2>>();
        List<Vector2>? current = null;

        for (var i = 0; i < points.Count - 1; i++)
        {
            var from = points[i];
            var to = points[i + 1];

            if (!ClipSegment(ref from, ref to, out var enteredAtStart, out var leftBeforeEnd))
            {
                current = null;
                continue;
            }

            if (current == null || enteredAtStart)
            {
                current = new List<Vector2> { from };
                runs.Add(current);
            }

            current.Add(to);

            if (leftBeforeEnd)
                current = null;
        }

        runs.RemoveAll(run => run.Count < 2);
        return runs;
    }

    /// <summary>
    /// Walks from a point known to be inside toward one that may be outside, and returns the
    /// furthest point still inside the region.
    /// </summary>
    /// <remarks>
    /// Used to keep a feather ring inside its clip. The ring is a strip from the contour
    /// outward along the normal; where the contour lies on the clip boundary the outer point
    /// is pulled back onto it, collapsing the ring to zero width exactly there. So an edge
    /// that the clip cut gets no halo, while an edge the clip never touched keeps its full
    /// feather — which is the behaviour that was wanted and is currently missing entirely.
    /// </remarks>
    internal Vector2 ClampInside(Vector2 inside, Vector2 outside)
    {
        // The inner box is proven interior to the region, so a point inside it needs no
        // clamping. Without this the walk below runs a parametric pass over EVERY boundary
        // edge, per feather vertex — measured at 1.36 us each, 3.13 ms across one console's
        // bands, plus most of the cost of its 616 feathered rects.
        if (outside.x >= _innerMin.x && outside.x <= _innerMax.x
            && outside.y >= _innerMin.y && outside.y <= _innerMax.y)
        {
            return outside;
        }

        var from = inside;
        var to = outside;

        return ClipSegment(ref from, ref to, out _, out _) ? to : inside;
    }

    /// <summary>Liang–Barsky style parametric clip of one segment against all half-planes.</summary>
    private bool ClipSegment(ref Vector2 from, ref Vector2 to, out bool clippedStart, out bool clippedEnd)
    {
        var enter = 0f;
        var exit = 1f;

        var origin = from;
        var direction = to - from;

        for (var i = 0; i < _boundary.Count; i++)
        {
            var a = _boundary[i];
            var b = _boundary[(i + 1) % _boundary.Count];

            var normal = new Vector2(-(b.y - a.y), b.x - a.x) * _winding;
            var denominator = Vector2.Dot(normal, direction);
            var numerator = Vector2.Dot(normal, a - origin);

            if (Mathf.Abs(denominator) < 0.000001f)
            {
                // Parallel to this edge: either wholly inside it or wholly outside.
                if (numerator > 0f)
                {
                    clippedStart = clippedEnd = false;
                    return false;
                }

                continue;
            }

            var t = numerator / denominator;

            // Inside means dot(normal, P - a) >= 0 along P = origin + t*direction, which
            // rearranges to t*denominator >= numerator. A positive denominator therefore
            // gives a lower bound on t (the segment is entering here) and a negative one an
            // upper bound (it is leaving). Swapping these keeps precisely the part of the
            // segment that lies outside.
            if (denominator > 0f)
                enter = Mathf.Max(enter, t);
            else
                exit = Mathf.Min(exit, t);

            if (enter <= exit)
                continue;

            clippedStart = clippedEnd = false;
            return false;
        }

        clippedStart = enter > 0f;
        clippedEnd = exit < 1f;

        to = origin + direction * exit;
        from = origin + direction * enter;
        return true;
    }

    private static Vector2 Intersect(Vector2 a, Vector2 b, Vector2 p, Vector2 q)
    {
        var edge = b - a;
        var segment = q - p;

        var denominator = edge.x * segment.y - edge.y * segment.x;
        if (Mathf.Abs(denominator) < 0.000001f)
            return q;

        // Solve (p + t*segment - a) x edge = 0 for t, where x is the 2D cross product:
        //   (p-a) x edge + t * (segment x edge) = 0
        //   t = -((p-a) x edge) / (segment x edge)
        // and segment x edge == -denominator, so the two negations cancel. Getting this
        // sign wrong puts the intersection on the far side of the clip edge, which turns
        // every clipped contour into a self-intersecting one.
        var t = ((p.x - a.x) * edge.y - (p.y - a.y) * edge.x) / denominator;
        return p + segment * t;
    }
}
