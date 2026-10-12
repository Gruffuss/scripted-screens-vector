using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>Stores frame-time measurements; only diagnostics format their rejection reason.</summary>
internal struct ScaleMeasurement
{
    internal enum Status { NotMeasured, EmptyRect, NoCanvas, NoCamera, None, NearPlane, Span }
    internal Status Reason;
    internal float LeftDepth, RightDepth, Near, Pixels;

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

    internal string Describe() => Reason switch
    {
        Status.EmptyRect => "empty rect",
        Status.NoCanvas => "no canvas",
        Status.NoCamera => "no camera",
        Status.None => string.Empty,
        Status.NearPlane => $"bottom corner at near plane: z {LeftDepth:0.000}/{RightDepth:0.000}, near {Near:0.000}",
        Status.Span => $"projected span {Pixels:0.00}px outside scalar range",
        _ => "not measured"
    };
}
