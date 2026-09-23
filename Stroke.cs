using System.Collections.Generic;
using UnityEngine;

namespace ScriptedScreensVector;

internal enum CapStyle
{
    Butt,
    Square,
    Round,
}

internal enum JoinStyle
{
    Miter,
    Bevel,
    Round,
}

/// <summary>
/// Turns a point list into a filled ribbon of a given width: the one piece of machinery
/// behind every stroked thing in the format.
/// </summary>
/// <remarks>
/// Strategy is offset polylines. For each input point a single left and a single right
/// offset point is produced, so the two sides always have matching counts and the body is a
/// plain triangle strip. That matters for feathering: a per-segment approach would need a
/// separate feather per quad and would show seams at every joint.
///
/// The cost of keeping counts matched is that joins are handled by a **clamped miter**
/// rather than by inserting extra geometry. A miter longer than the limit is shortened to
/// the limit instead of being replaced by a true bevel or round wedge. At UI stroke widths
/// the difference is not visible; at very wide strokes with very sharp corners it is. Noted
/// rather than hidden — revisit if a design needs heavy strokes on spiky paths.
/// </remarks>
internal static class Stroke
{
    private const int RoundCapSegments = 8;
    private const float Epsilon = 0.0001f;

    internal static void Emit(
        MeshBuilder vh,
        IReadOnlyList<Vector2> points,
        bool closed,
        float width,
        Paint paint,
        CapStyle cap,
        JoinStyle join,
        float miterLimit,
        float feather,
        Matrix4x4 matrix,
        int maxVertices)
    {
        var path = Clean(points, closed);
        if (path.Count < 2 || width <= 0f)
            return;

        var half = width * 0.5f;

        // Square caps are just a longer path; doing it here keeps the offset maths uniform.
        if (!closed && cap == CapStyle.Square)
            Extend(path, half);

        var count = path.Count;

        // Reused, not allocated: a stroked shape is emitted on every rebuild, and three
        // buffers per shape per rebuild was measured at 717 B a shape -- 86 KB for a page of
        // 120 bordered boxes, every frame it animates, against 5 B a shape unstroked.
        var left = Buffer(ref _left, count);
        var right = Buffer(ref _right, count);

        for (var i = 0; i < count; i++)
        {
            var normal = VertexNormal(path, i, closed, join, miterLimit, out var scale);
            left[i] = path[i] + normal * half * scale;
            right[i] = path[i] - normal * half * scale;
        }

        var segments = closed ? count : count - 1;
        if (vh.currentVertCount + count * 2 > maxVertices)
            return;

        var origin = vh.currentVertCount;
        for (var i = 0; i < count; i++)
        {
            // Gradient strokes sample per vertex, same as fills.
            vh.AddVert(matrix.MultiplyPoint3x4(left[i]), paint.At(left[i]), Vector2.zero);
            vh.AddVert(matrix.MultiplyPoint3x4(right[i]), paint.At(right[i]), Vector2.zero);
        }

        for (var i = 0; i < segments; i++)
        {
            var a = origin + i * 2;
            var b = origin + ((i + 1) % count) * 2;
            vh.AddTriangle(a, a + 1, b + 1);
            vh.AddTriangle(a, b + 1, b);
        }

        if (feather > 0f)
        {
            FeatherSide(vh, left, count, path, closed, feather, paint, matrix, maxVertices, outward: true);
            FeatherSide(vh, right, count, path, closed, feather, paint, matrix, maxVertices, outward: false);
        }

        if (!closed && cap == CapStyle.Round)
        {
            RoundCap(vh, path[0], path[1], half, paint, matrix, maxVertices);
            RoundCap(vh, path[^1], path[^2], half, paint, matrix, maxVertices);
        }
    }

    /// <summary>
    /// Splits a path into dashes before stroking. Spec §5.2 <c>dash</c> / <c>dofs</c>.
    /// </summary>
    internal static List<List<Vector2>> Dash(IReadOnlyList<Vector2> points, float[] pattern, float offset)
    {
        var runs = new List<List<Vector2>>();
        if (points.Count < 2 || pattern == null || pattern.Length == 0)
        {
            runs.Add(new List<Vector2>(points));
            return runs;
        }

        var total = 0f;
        foreach (var length in pattern)
            total += Mathf.Max(0f, length);

        if (total <= Epsilon)
        {
            runs.Add(new List<Vector2>(points));
            return runs;
        }

        var index = 0;
        var remaining = pattern[0];
        var on = true;

        // Consume the dash offset before drawing anything.
        var skip = Mathf.Repeat(offset, total);
        while (skip > 0f)
        {
            var step = Mathf.Min(skip, remaining);
            remaining -= step;
            skip -= step;
            if (remaining <= Epsilon)
            {
                index = (index + 1) % pattern.Length;
                remaining = pattern[index];
                on = !on;
            }
        }

        var current = on ? new List<Vector2> { points[0] } : null;

        for (var i = 0; i < points.Count - 1; i++)
        {
            var from = points[i];
            var to = points[i + 1];
            var segment = Vector2.Distance(from, to);
            var travelled = 0f;

            while (segment - travelled > Epsilon)
            {
                var step = Mathf.Min(segment - travelled, remaining);
                travelled += step;
                remaining -= step;

                var at = Vector2.Lerp(from, to, travelled / segment);
                current?.Add(at);

                if (remaining > Epsilon)
                    continue;

                // Pattern boundary: close the current run or start a new one.
                if (on && current is { Count: >= 2 })
                    runs.Add(current);

                on = !on;
                current = on ? new List<Vector2> { at } : null;

                index = (index + 1) % pattern.Length;
                remaining = pattern[index];
            }
        }

        if (on && current is { Count: >= 2 })
            runs.Add(current);

        return runs;
    }

    /// <summary>
    /// Flattens a Catmull-Rom spline through the given points.
    /// </summary>
    /// <remarks>
    /// Catmull-Rom rather than bezier because it passes <em>through</em> its control points,
    /// which is what a chart or a gauge needle wants: the author supplies samples, not
    /// handles. Endpoints are duplicated so the curve starts and ends where it should.
    /// </remarks>
    internal static List<Vector2> Spline(IReadOnlyList<Vector2> points, int segmentsPerSpan)
    {
        var output = new List<Vector2>();
        if (points.Count < 2)
        {
            output.AddRange(points);
            return output;
        }

        var steps = Mathf.Clamp(segmentsPerSpan, 1, 32);

        for (var i = 0; i < points.Count - 1; i++)
        {
            var p0 = points[Mathf.Max(i - 1, 0)];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = points[Mathf.Min(i + 2, points.Count - 1)];

            for (var s = 0; s < steps; s++)
            {
                var t = s / (float)steps;
                output.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }

        output.Add(points[^1]);
        return output;
    }

    private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        var t2 = t * t;
        var t3 = t2 * t;

        return 0.5f * (
            2f * p1 +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    /// <summary>Averaged edge normal, with the miter scale needed to keep width constant.</summary>
    private static Vector2 VertexNormal(List<Vector2> path, int index, bool closed, JoinStyle join, float miterLimit, out float scale)
    {
        var count = path.Count;
        var hasPrevious = index > 0 || closed;
        var hasNext = index < count - 1 || closed;

        var previous = path[(index - 1 + count) % count];
        var next = path[(index + 1) % count];

        var incoming = hasPrevious ? (path[index] - previous).normalized : Vector2.zero;
        var outgoing = hasNext ? (next - path[index]).normalized : Vector2.zero;

        if (incoming.sqrMagnitude < Epsilon) incoming = outgoing;
        if (outgoing.sqrMagnitude < Epsilon) outgoing = incoming;

        var na = new Vector2(-incoming.y, incoming.x);
        var nb = new Vector2(-outgoing.y, outgoing.x);

        var normal = (na + nb).normalized;
        if (normal.sqrMagnitude < Epsilon)
        {
            scale = 1f;
            return na;
        }

        // Lengthen the offset so the ribbon keeps its width through the turn.
        var cosine = Vector2.Dot(normal, nb);
        scale = Mathf.Abs(cosine) < Epsilon ? 1f : 1f / cosine;

        var limit = join == JoinStyle.Miter ? Mathf.Max(1f, miterLimit) : 1.4f;
        scale = Mathf.Clamp(scale, -limit, limit);

        return normal;
    }

    /// <summary>
    /// Feathers one side of a stroke. <paramref name="count"/> is the number of points in
    /// use, NOT <c>side.Length</c>: the buffers are pooled and a reused one is as long as the
    /// longest stroke this thread has drawn.
    /// </summary>
    private static void FeatherSide(MeshBuilder vh, Vector2[] side, int count, List<Vector2> centre, bool closed, float width, Paint paint, Matrix4x4 matrix, int maxVertices, bool outward)
    {
        if (vh.currentVertCount + count * 2 > maxVertices)
            return;

        var origin = vh.currentVertCount;

        for (var i = 0; i < count; i++)
        {
            var direction = (side[i] - centre[i]).normalized;
            if (direction.sqrMagnitude < Epsilon)
                direction = outward ? Vector2.up : Vector2.down;

            var solid = paint.At(side[i]);
            var faded = solid;
            faded.a = 0;

            vh.AddVert(matrix.MultiplyPoint3x4(side[i]), solid, Vector2.zero);
            vh.AddVert(matrix.MultiplyPoint3x4(side[i] + direction * width), faded, Vector2.zero);
        }

        var segments = closed ? count : count - 1;
        for (var i = 0; i < segments; i++)
        {
            var a = origin + i * 2;
            var b = origin + ((i + 1) % count) * 2;
            vh.AddTriangle(a, a + 1, b + 1);
            vh.AddTriangle(a, b + 1, b);
        }
    }

    private static void RoundCap(MeshBuilder vh, Vector2 end, Vector2 inward, float radius, Paint paint, Matrix4x4 matrix, int maxVertices)
    {
        if (vh.currentVertCount + RoundCapSegments + 2 > maxVertices)
            return;

        var direction = (end - inward).normalized;
        if (direction.sqrMagnitude < Epsilon)
            return;

        var start = Mathf.Atan2(direction.y, direction.x) - Mathf.PI * 0.5f;
        var origin = vh.currentVertCount;

        vh.AddVert(matrix.MultiplyPoint3x4(end), paint.At(end), Vector2.zero);
        for (var i = 0; i <= RoundCapSegments; i++)
        {
            var angle = start + Mathf.PI * (i / (float)RoundCapSegments);
            var at = end + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            vh.AddVert(matrix.MultiplyPoint3x4(at), paint.At(at), Vector2.zero);
        }

        for (var i = 0; i < RoundCapSegments; i++)
            vh.AddTriangle(origin, origin + 1 + i, origin + 2 + i);
    }

    /// <summary>
    /// Scratch for one stroke, per thread because tessellation runs on workers.
    /// </summary>
    /// <remarks>
    /// [ThreadStatic] fields cannot carry an initialiser -- only the thread that runs the
    /// static constructor would get one -- so each is filled on first use, as the
    /// tessellator's own scratch buffers are.
    /// </remarks>
    [System.ThreadStatic] private static List<Vector2>? _cleaned;

    [System.ThreadStatic] private static Vector2[]? _left;

    [System.ThreadStatic] private static Vector2[]? _right;

    private static Vector2[] Buffer(ref Vector2[]? slot, int count)
    {
        // Managed arithmetic, deliberately: `Mathf.NextPowerOfTwo` is a native ECall, and a
        // native call on the tessellation path is exactly what makes it un-threadable. The
        // headless test harness threw SecurityException on it the moment it was written,
        // which is the third time that trap has been caught by running the renderer outside
        // the player.
        if (slot == null || slot.Length < count)
        {
            var size = 64;
            while (size < count)
                size *= 2;

            slot = new Vector2[size];
        }

        return slot;
    }

    /// <summary>Drops duplicate points, which would otherwise produce zero-length normals.</summary>
    /// <remarks>
    /// The list is this thread's scratch and is valid until the next stroke on it. Every
    /// caller consumes it within the same emit, and a dashed stroke emits its runs one after
    /// another rather than holding them.
    /// </remarks>
    private static List<Vector2> Clean(IReadOnlyList<Vector2> points, bool closed)
    {
        var cleaned = _cleaned ??= new List<Vector2>(64);
        cleaned.Clear();

        foreach (var point in points)
        {
            if (cleaned.Count == 0 || (point - cleaned[^1]).sqrMagnitude > Epsilon)
                cleaned.Add(point);
        }

        if (closed && cleaned.Count > 1 && (cleaned[0] - cleaned[^1]).sqrMagnitude <= Epsilon)
            cleaned.RemoveAt(cleaned.Count - 1);

        return cleaned;
    }

    private static void Extend(List<Vector2> path, float by)
    {
        if (path.Count < 2)
            return;

        var head = (path[0] - path[1]).normalized;
        var tail = (path[^1] - path[^2]).normalized;

        path[0] += head * by;
        path[^1] += tail * by;
    }
}
