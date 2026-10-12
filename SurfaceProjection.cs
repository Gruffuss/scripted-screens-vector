using System;
using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>Canvas-to-screen homography copied on the main thread; only managed maths here.</summary>
internal readonly struct SurfaceProjection
{
    internal readonly bool Valid;
    internal readonly Vector3 A, B, C;
    internal readonly float Near;

    internal SurfaceProjection(Vector3 a, Vector3 b, Vector3 c, float near)
    {
        A = a; B = b; C = c; Near = near;
        Valid = Finite(a.x) && Finite(a.y) && Finite(a.z) && Finite(b.x) && Finite(b.y)
            && Finite(b.z) && Finite(c.x) && Finite(c.y) && Finite(c.z);
    }

    private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
    internal float Depth(Vector2 p) => C.x * p.x + C.y * p.y + C.z;
    internal Vector2 Project(Vector2 p)
    {
        var w = Depth(p);
        return new Vector2((A.x * p.x + A.y * p.y + A.z) / w,
            (B.x * p.x + B.y * p.y + B.z) / w);
    }

    internal void Jacobian(Vector2 p, out float j00, out float j01, out float j10, out float j11, out bool inFront)
    {
        j00 = j01 = j10 = j11 = 0f;
        var w = Depth(p);
        inFront = Valid && w > Near && Finite(w);
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

    internal static bool Drifted(float[] built, float[] now, float q)
    {
        for (var i = 0; i < built.Length; i++)
            if (built[i] > 0f && now[i] < built[i] / q) return true;
        return false;
    }
}
