using System;
using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>Stores frame-time measurements; only diagnostics format their rejection reason.</summary>
internal struct ScaleMeasurement
{
    internal enum Status { NotMeasured, EmptyRect, NoCanvas, NoCamera, None, NearPlane, Span, Outside }
    internal Status Reason;
    internal float LeftDepth, RightDepth, Near, Pixels, CanvasWidth, RawPixels;
    internal enum Segment { Bottom, ClippedBottom, Chord, Retained }
    internal Segment Sample;

    internal float Measure(Vector3 left, Vector3 right, SurfaceProjection projection, Rect rect, float lastKnown)
    {
        CanvasWidth = rect.width; Sample = Segment.Bottom;
        RawPixels = Vector2.Distance(new Vector2(left.x, left.y), new Vector2(right.x, right.y));
        if (!projection.Valid) return Measure(left, right, Mathf.Max(0.01f, projection.Near), rect.width);
        if (projection.Contains(SurfaceProjection.Corner(rect, 0))
            && projection.Contains(SurfaceProjection.Corner(rect, 1))
            && left.z > projection.Near && right.z > projection.Near)
            // Preserve the legacy near threshold as well as the arithmetic on this path.
            return Measure(left, right, Mathf.Max(0.01f, projection.Near), rect.width);

        LeftDepth = left.z; RightDepth = right.z; Near = projection.Near;
        Sample = Segment.ClippedBottom;
        if (!projection.ClipChord(rect, rect.yMin, out var a, out var b))
        {
            Span<Vector2> visible = stackalloc Vector2[16];
            var count = projection.VisiblePolygon(rect, visible);
            if (count == 0) return Retain(lastKnown);
            var minY = visible[0].y; var maxY = minY;
            for (var i = 1; i < count; i++)
            {
                minY = Mathf.Min(minY, visible[i].y); maxY = Mathf.Max(maxY, visible[i].y);
            }
            // Horizontal chord halfway between the visible polygon's lowest and highest y.
            Sample = Segment.Chord;
            if (!projection.ClipChord(rect, minY + (maxY - minY) * 0.5f, out a, out b))
                return Retain(lastKnown);
        }
        CanvasWidth = b.x - a.x;
        Pixels = Vector2.Distance(projection.Project(a), projection.Project(b));
        Reason = Status.None;
        return Pixels / CanvasWidth;
    }

    private float Retain(float lastKnown)
    {
        Reason = Status.Outside; Sample = Segment.Retained; Pixels = CanvasWidth = 0f;
        return lastKnown;
    }

    internal int Bucket(float scale, int built) => Reason == Status.Outside ? built : scale > 0f
        ? Mathf.FloorToInt(Mathf.Log(scale) / Mathf.Log(1.15f)) : int.MinValue + 1;

    internal float Measure(Vector3 left, Vector3 right, float near, float width)
    {
        LeftDepth = left.z; RightDepth = right.z; Near = near;
        if (left.z <= near || right.z <= near)
        {
            Reason = Status.NearPlane;
            return -1f;
        }
        Pixels = Vector2.Distance(new Vector2(left.x, left.y), new Vector2(right.x, right.y));
        if (float.IsNaN(Pixels) || float.IsInfinity(Pixels) || Pixels <= 0.01f || Pixels > 100000f)
        {
            Reason = Status.Span;
            return -1f;
        }
        Reason = Status.None;
        return Pixels / width;
    }

    /// <summary>Which part of the surface the last measurement used, and what it saw there.</summary>
    internal string Where(float rectWidth) => Reason is Status.None or Status.Outside
        ? Sample switch
        {
            Segment.Bottom => $"the whole bottom edge, {RawPixels:0} px across {rectWidth:0.0} units",
            Segment.ClippedBottom => $"the visible part of the bottom edge, {Pixels:0} px across {CanvasWidth:0.0} of {rectWidth:0.0} units",
            Segment.Chord => $"a chord through the visible part, {Pixels:0} px across {CanvasWidth:0.0} of {rectWidth:0.0} units",
            _ => "nothing in view, last size kept",
        }
        : Describe();

    internal string Describe() => Reason switch
    {
        Status.EmptyRect => "empty rect",
        Status.NoCanvas => "no canvas",
        Status.NoCamera => "no camera",
        Status.Outside => "outside view frustum; retaining last scale",
        Status.None => string.Empty,
        Status.NearPlane => $"bottom corner at near plane: z {LeftDepth:0.000}/{RightDepth:0.000}, near {Near:0.000}",
        Status.Span => $"projected span {Pixels:0.00}px outside scalar range",
        _ => "not measured"
    };
}
