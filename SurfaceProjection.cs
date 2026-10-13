using System;
using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>Canvas-to-screen homography copied on the main thread; only managed maths here.</summary>
internal readonly struct SurfaceProjection
{
    internal readonly bool Valid;
    internal readonly Vector3 A, B, C, CameraDepth;
    internal readonly float Near, Width, Height;

    internal SurfaceProjection(Vector3 a, Vector3 b, Vector3 c, float near, float width = 0f, float height = 0f, Vector3? cameraDepth = null)
    {
        A = a; B = b; C = c; Near = near; Width = width; Height = height; CameraDepth = cameraDepth ?? c;
        Valid = Finite(a.x) && Finite(a.y) && Finite(a.z) && Finite(b.x) && Finite(b.y)
            && Finite(b.z) && Finite(c.x) && Finite(c.y) && Finite(c.z)
            && Finite(CameraDepth.x) && Finite(CameraDepth.y) && Finite(CameraDepth.z);
    }

    internal static Vector2 Corner(Rect rect, int i) => new Vector2((i & 1) == 0 ? rect.xMin : rect.xMax,
        (i & 2) == 0 ? rect.yMin : rect.yMax);

    internal static SurfaceProjection FromMatrix(Matrix4x4 m, float width, float height, float near, Vector3? cameraDepth = null)
    {
        var hw = 0.5f * width; var hh = 0.5f * height;
        return new SurfaceProjection(
            new Vector3(hw * (m.m00 + m.m30), hw * (m.m01 + m.m31), hw * (m.m03 + m.m33)),
            new Vector3(hh * (m.m10 + m.m30), hh * (m.m11 + m.m31), hh * (m.m13 + m.m33)),
            new Vector3(m.m30, m.m31, m.m33), near, width, height, cameraDepth);
    }

    private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
    internal float Depth(Vector2 p) => CameraDepth.x * p.x + CameraDepth.y * p.y + CameraDepth.z;
    private float Weight(Vector2 p) => C.x * p.x + C.y * p.y + C.z;
    internal Vector2 Project(Vector2 p)
    {
        var w = Weight(p);
        return new Vector2((A.x * p.x + A.y * p.y + A.z) / w,
            (B.x * p.x + B.y * p.y + B.z) / w);
    }

    internal void Jacobian(Vector2 p, out float j00, out float j01, out float j10, out float j11, out bool inFront)
    {
        j00 = j01 = j10 = j11 = 0f;
        var w = Weight(p);
        inFront = Valid && Depth(p) > Near && w > 0f && Finite(w);
        if (!inFront) return;
        var screen = Project(p);
        j00 = (A.x - screen.x * C.x) / w;
        j01 = (A.y - screen.x * C.y) / w;
        j10 = (B.x - screen.y * C.x) / w;
        j11 = (B.y - screen.y * C.y) / w;
        inFront = Finite(j00) && Finite(j01) && Finite(j10) && Finite(j11);
    }

    internal float InverseTransposeLength(Vector2 p, Vector2 u, float uniform)
    {
        Jacobian(p, out var a, out var b, out var c, out var d, out var front);
        var det = (double)a * d - (double)b * c;
        if (!front || Math.Abs(det) < 1e-20)
            return u.magnitude / Mathf.Max(0.0001f, uniform);
        var x = (d * (double)u.x - c * (double)u.y) / det;
        var y = (a * (double)u.y - b * (double)u.x) / det;
        return (float)Math.Sqrt(x * x + y * y);
    }

    internal float SigmaMin(Vector2 p)
    {
        Jacobian(p, out var a, out var b, out var c, out var d, out var front);
        return front ? SigmaMin(a, b, c, d) : 0f;
    }

    internal static float SigmaMin(float a, float b, float c, float d)
    {
        // det / sigmaMax avoids subtracting nearly equal eigenvalues at grazing angles.
        var t = (double)a * a + (double)b * b + (double)c * c + (double)d * d;
        var det = (double)a * d - (double)b * c;
        var largest = Math.Sqrt((t + Math.Sqrt(Math.Max(0d, t * t - 4d * det * det))) * 0.5d);
        return largest > 0d ? (float)(Math.Abs(det) / largest) : 0f;
    }

    // Homogeneous side planes avoid dividing points behind the camera. Orthographic
    // projections carry camera depth separately from the perspective divisor.
    private double Plane(Vector2 p, int plane)
    {
        double a = (double)A.x * p.x + (double)A.y * p.y + A.z;
        double b = (double)B.x * p.x + (double)B.y * p.y + B.z;
        double w = (double)C.x * p.x + (double)C.y * p.y + C.z;
        return plane switch {
            0 => a, 1 => Width * w - a, 2 => b, 3 => Height * w - b,
            _ => (double)CameraDepth.x * p.x + (double)CameraDepth.y * p.y + CameraDepth.z - Near
        };
    }

    internal bool Contains(Vector2 p)
    {
        if (!Valid) return false;
        for (var i = 0; i < 5; i++) if (Plane(p, i) < 0d) return false;
        return true;
    }

    internal bool ClipChord(Rect rect, float y, out Vector2 left, out Vector2 right)
    {
        var a = new Vector2(rect.xMin, y); var b = new Vector2(rect.xMax, y);
        double lo = 0d, hi = 1d;
        for (var plane = 0; plane < 5; plane++)
        {
            var da = Plane(a, plane); var db = Plane(b, plane);
            if (da < 0d && db < 0d) { left = right = default; return false; }
            if (da < 0d) lo = Math.Max(lo, da / (da - db));
            if (db < 0d) hi = Math.Min(hi, da / (da - db));
        }
        left = new Vector2((float)(a.x + (b.x - (double)a.x) * lo), y);
        right = new Vector2((float)(a.x + (b.x - (double)a.x) * hi), y);
        return hi > lo && right.x > left.x;
    }

    // A rectangle clipped by five planes has at most nine vertices. Scratch storage
    // is on the stack: visibility polling does not allocate per surface per frame.
    internal int VisiblePolygon(Rect rect, Span<Vector2> result)
    {
        if (!Valid) return 0;
        Span<Vector2> input = stackalloc Vector2[16];
        Span<Vector2> output = stackalloc Vector2[16];
        input[0] = Corner(rect, 0); input[1] = Corner(rect, 1);
        input[2] = Corner(rect, 3); input[3] = Corner(rect, 2);
        var count = 4;
        // Projections supplied without a viewport use the whole rect (uniform/headless callers).
        var planes = Width > 0f && Height > 0f ? 5 : 0;
        for (var plane = 0; plane < planes && count > 0; plane++)
        {
            var nextCount = 0;
            var previous = input[count - 1]; var dp = Plane(previous, plane);
            for (var i = 0; i < count; i++)
            {
                var point = input[i]; var d = Plane(point, plane);
                if ((dp < 0d) != (d < 0d))
                {
                    var t = dp / (dp - d);
                    output[nextCount++] = new Vector2((float)(previous.x + (point.x - (double)previous.x) * t),
                        (float)(previous.y + (point.y - (double)previous.y) * t));
                }
                if (d >= 0d) output[nextCount++] = point;
                previous = point; dp = d;
            }
            var swap = input; input = output; output = swap; count = nextCount;
        }
        input.Slice(0, count).CopyTo(result);
        return count;
    }

    internal static bool Drifted(SurfaceProjection built, SurfaceProjection now, Rect rect, float q)
    {
        Span<Vector2> visible = stackalloc Vector2[16];
        var count = now.Valid ? now.VisiblePolygon(rect, visible) : 4;
        // No current projection retains the uniform fallback's existing recovery behavior.
        if (!now.Valid) for (var i = 0; i < count; i++) visible[i] = Corner(rect, i);
        for (var i = 0; i < count; i++)
        {
            var p = visible[i];
            built.Jacobian(p, out var a, out var b, out var c, out var d, out var wasFront);
            now.Jacobian(p, out var e, out var f, out var g, out var h, out var isFront);
            if (wasFront != isFront) return true;
            if (!isFront) continue;
            var det = (double)a * d - (double)b * c;
            var nextDet = (double)e * h - (double)f * g;
            if (Math.Abs(det) < 1e-20)
            {
                if (Math.Abs(nextDet) >= 1e-20) return true;
                continue;
            }
            // The relative Jacobian bounds contraction along every edge normal, including turns.
            var relative = SigmaMin((float)((e * (double)d - f * (double)c) / det),
                (float)((f * (double)a - e * (double)b) / det),
                (float)((g * (double)d - h * (double)c) / det),
                (float)((h * (double)a - g * (double)b) / det));
            if (!Finite(relative) || relative < 1f / q) return true;
        }
        return false;
    }
}
