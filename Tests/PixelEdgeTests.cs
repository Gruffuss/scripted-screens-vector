using System;
using UnityEngine;

namespace ScriptedScreensVector.Tests;

internal static class PixelEdgeTests
{
    internal static SurfaceProjection Rig(float near, float far)
    {
        const float focal = 1000f;
        var slope = (far - near) / 400f;
        var cosine = (float)Math.Sqrt(1d - (far - near) * (far - near));
        // Manually filled perspective * Y rotation * canvas scale (no Unity native factories).
        return new SurfaceProjection(new Vector3(focal * cosine / 400f + 960f * slope, 0f, 960f * near),
            new Vector3(540f * slope, focal / 400f, 540f * near), new Vector3(slope, 0f, near), 0.01f);
    }

    internal static void Run(TestRun run)
    {
        foreach (var (name, projection) in new[] { ("frontal", Rig(1f, 1f)), ("grazing", Rig(0.151f, 0.515f)), ("near", Rig(0.013f, 0.549f)) })
        {
            var error = 0f;
            foreach (var p in new[] { new Vector2(30f, 25f), new Vector2(200f, 130f), new Vector2(380f, 290f) })
            {
                projection.Jacobian(p, out var a, out var b, out var c, out var d, out var front);
                // Double finite differences avoid float cancellation with the prescribed h=1e-3.
                const double h = 1e-3;
                (double X, double Y) Project(double x, double y)
                {
                    double w = projection.C.x * x + projection.C.y * y + projection.C.z;
                    return ((projection.A.x * x + projection.A.y * y + projection.A.z) / w,
                        (projection.B.x * x + projection.B.y * y + projection.B.z) / w);
                }
                var xp = Project(p.x + h, p.y); var xm = Project(p.x - h, p.y);
                var yp = Project(p.x, p.y + h); var ym = Project(p.x, p.y - h);
                var expected = new[] { (xp.X - xm.X) / (2 * h), (yp.X - ym.X) / (2 * h),
                    (xp.Y - xm.Y) / (2 * h), (yp.Y - ym.Y) / (2 * h) };
                var actual = new[] { a, b, c, d };
                for (var i = 0; i < 4; i++)
                    error = Math.Max(error, (float)(Math.Abs(expected[i] - actual[i]) / Math.Max(1d, Math.Abs(expected[i]))));
                run.Check($"P1 {name}: in front", front, "valid Jacobian");
                double xx = (double)a * a + (double)c * c, yy = (double)b * b + (double)d * d, xy = (double)a * b + (double)c * d;
                var eigen = (xx + yy - Math.Sqrt((xx - yy) * (xx - yy) + 4d * xy * xy)) / 2d;
                var sigma = SurfaceProjection.SigmaMin(a, b, c, d);
                run.Check($"P1 {name}: singular values", Math.Abs(sigma * sigma - eigen) <= 1e-3 * eigen,
                    "independent J transpose J eigenvalue");
            }
            run.Check($"P1 {name}: central differences", error < 1e-3f, $"relative error {error}");
        }
        run.Check("P3: drift below limit", !SurfaceProjection.Drifted(new[] { 3f, 6f }, new[] { 3f / 1.49f, 6f / 1.49f }, 1.5f), "1.49x");
        run.Check("P3: drift above limit", SurfaceProjection.Drifted(new[] { 3f, 6f }, new[] { 3f / 1.51f, 6f }, 1.5f), "1.51x");
        run.Check("P3: lost projection rebuilds", SurfaceProjection.Drifted(new[] { 3f }, new[] { 0f }, 1.5f), "near plane");
        foreach (var (feature, effective) in new[] { (1.3f, 0f), (2.6f, 0.65f), (3.9f, 1.3f) })
        {
            var value = EdgeRamp.Coverage(0.325f / feature, 1f / feature, 1f, 1.3f);
            var expected = effective == 0f ? 0f : Math.Max(0f, 1f - 0.325f / effective);
            run.Check("P5: fill falloff " + feature, Math.Abs(value - expected) < 1e-5f, $"coverage {value}");
        }
        run.Check("P5: stroke width clamp", Math.Abs(EdgeRamp.Coverage(-0.24f / 0.48f, 1f / 0.48f, 1f, 1.3f) - 0.5f) < 1e-5f, "0.48px stroke");
        run.Check("P5: linear 1.3px ramp", Math.Abs(EdgeRamp.Coverage(0.65f / 100f, 1f / 100f, 1f, 1.3f) - 0.5f) < 1e-5f, "halfway");
        run.Check("P5: narrow stale band", EdgeRamp.Coverage(0.1f, 1f / 100f, 0.2f, 1.3f) == 0f
            && EdgeRamp.Coverage(0.001f, 1f / 100f, 0.2f, 1.3f) == 0.2f, "min with x");
        run.Check("P5: lost uv y fallback", EdgeRamp.Coverage(0f, 0f, 0.4f, 1.3f) == 0.4f, "geometric coverage");
        var behind = Rig(-0.02f, 0.515f);
        behind.Jacobian(Vector2.zero, out _, out _, out _, out _, out var visible);
        run.Check("P8: behind near plane uniform fallback", !visible
            && Math.Abs(behind.InverseTransposeLength(Vector2.zero, new Vector2(3f, 4f), 2f) - 2.5f) < 1e-6f, "5/2");
        var mesh = new MeshBuilder();
        mesh.TrackBounds(true);
        mesh.AddVert(Vector3.zero, Color.white, Vector2.zero, 1f, 0f);
        mesh.AddVert(new Vector3(100f, 100f), Color.white, Vector2.zero, 0f, 1f);
        mesh.MarkShape();
        run.Check("P7: edge band excluded from shape bounds", mesh.ShapeBounds(0).width == 0f, "solid outer colour does not cover labels");
    }
}
