using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
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

    private static MeshBuilder Emit(string body, bool pixel, SurfaceProjection projection = default, float scale = 2f, Rect? target = null, TessellationStats? stats = null, bool available = true, bool uv = true)
    {
        var scene = SceneParser.Parse(SceneText.ToProps("SCENE w=400 h=400 fit=stretch ztext=1\n" + body, "pixel tests")!)!;
        if (scene.Problems.Count != 0) throw new InvalidOperationException(string.Join("; ", scene.Problems));
        var mesh = new MeshBuilder();
        var oldCoverage = Tessellator.CoverageInUv; var oldAvailable = Tessellator.PixelEdgesAvailable;
        Tessellator.CoverageInUv = uv; Tessellator.PixelEdgesAvailable = available;
        try
        {
            Tessellator.Emit(mesh, scene, new EvalContext(), target ?? new Rect(0, 0, 400, 400), scale, true,
                stats ?? new TessellationStats(), projection: projection, pixelEdges: pixel);
        }
        finally { Tessellator.CoverageInUv = oldCoverage; Tessellator.PixelEdgesAvailable = oldAvailable; }
        return mesh;
    }

    private static float LineDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var v = b - a; var d = p - a;
        return Math.Abs(v.x * d.y - v.y * d.x) / v.magnitude;
    }

    private static void Band(TestRun run, string name, MeshBuilder mesh, SurfaceProjection projection, float feature, bool stroke)
    {
        var checkedVertices = 0; var minPixels = float.MaxValue; var encoding = true;
        for (var t = 0; t < mesh.TriangleCount; t++)
        {
            var inside = new List<int>(); var outside = new List<int>();
            for (var k = 0; k < 3; k++)
            {
                var v = mesh.Index(t * 3 + k);
                if (mesh.CoverageUv(v).y != 0f || mesh.Colour(v).a == 0f) outside.Add(v);
                else inside.Add(v);
            }
            if (inside.Count != 2 || outside.Count != 1) continue;
            var a = (Vector2)mesh.Vertex(inside[0]).Position; var b = (Vector2)mesh.Vertex(inside[1]).Position;
            if (t > 0)
                for (var k = 0; k < 3; k++)
                {
                    var v = mesh.Index((t - 1) * 3 + k);
                    if ((mesh.CoverageUv(v).y != 0f || mesh.Colour(v).a == 0f) && !outside.Contains(v)) outside.Add(v);
                }
            foreach (var o in outside)
            {
                var p = (Vector2)mesh.Vertex(o).Position;
                var h = LineDistance(p, a, b);
                if (h < 1e-6f) continue;
                minPixels = Math.Min(minPixels, LineDistance(projection.Project(p), projection.Project(a), projection.Project(b)));
                var uv = mesh.CoverageUv(o);
                encoding &= mesh.CoverageUv(inside[0]) == new Vector2(1f, 0f) && uv.x == 0f
                    && (stroke ? uv.y < 0f : uv.y > 0f) && mesh.Colour(o) == mesh.Colour(inside[0]);
                if (feature > 0f) encoding &= Math.Abs(Math.Abs(uv.y) * feature - h) < 1e-4f * Math.Max(h, 1e-3f);
                checkedVertices++;
            }
        }
        run.Check("P2 band contains ramp: " + name, checkedVertices > 0 && minPixels >= 1.3f - 0.01f,
            $"{checkedVertices} vertices; minimum {minPixels:F4}px");
        run.Check("P4 encoding: " + name, checkedVertices > 0 && encoding, "signed perpendicular distance; solid colour; x=0");
    }

    private static void Meshes(TestRun run)
    {
        var pixel = Array.IndexOf(Environment.GetCommandLineArgs(), "--pixel-edges-off") < 0;
        var bodies = new[] {
            ("rect", "R x=30 y=50 w=330 h=220 f=#fff", 220f, false),
            ("rounded", "R x=30 y=50 w=330 h=220 rx=20 f=#fff", 220f, false),
            ("ellipse", "C cx=200 cy=180 rx=170 ry=110 f=#fff", 220f, false),
            ("concave", "P d=\"M30 50 H360 V270 H200 V130 H160 V270 H30 Z\" f=#fff", 220f, false),
            ("stroke1", "L p=[30,120,360,240] sw=1 s=#fff", 1f, true),
            ("stroke03", "L p=[30,120,360,240] sw=0.3 s=#fff", 0.3f, true),
            ("round caps", "L p=[30,120,360,240] sw=10 s=#fff cap=round", 10f, true),
            ("YS", "YS n=8 x=\"=30+i*45\" y=120 y2=240 f=#fff", 0f, false) };
        foreach (var (name, rig) in new[] { ("frontal", Rig(1f, 1f)), ("grazing", Rig(0.151f, 0.515f)), ("near", Rig(0.013f, 0.549f)) })
            foreach (var (shape, body, feature, stroke) in bodies)
                Band(run, name + " " + shape, Emit(body, pixel, rig), rig, feature, stroke);

        var grazing = Rig(0.151f, 0.515f);
        var reduced = new SurfaceProjection(grazing.A / 1.49f, grazing.B / 1.49f, grazing.C, grazing.Near);
        var bar = Emit(bodies[0].Item2, pixel, grazing);
        Band(run, "P3 drift 1.49x", bar, reduced, 220f, false);
        foreach (var x in new[] { 30f, 350f })
        {
            var body = FormattableString.Invariant($"R x={x} y=50 w=20 h=250 f=#fff");
            var mesh = Emit(body, pixel, grazing);
            float rampWidth = 0f;
            for (var t = 0; t < mesh.TriangleCount; t++)
            {
                var a = mesh.Index(t * 3); var b = mesh.Index(t * 3 + 1); var c = mesh.Index(t * 3 + 2);
                if (mesh.CoverageUv(a).y != 0f || mesh.CoverageUv(c).y != 0f
                    || (mesh.CoverageUv(b).y == 0f && mesh.Colour(b).a != 0f)) continue;
                var pa = (Vector2)mesh.Vertex(a).Position; var pc = (Vector2)mesh.Vertex(c).Position;
                if (Math.Abs(pa.x - pc.x) > 1e-5f || Math.Abs(pa.y - pc.y) < 1f) continue;
                var outer = (Vector2)mesh.Vertex(b).Position;
                var h = LineDistance(outer, pa, pc);
                if (h <= 1e-6f) continue;
                var label = Math.Abs(mesh.CoverageUv(b).y);
                if (label == 0f)
                    rampWidth = LineDistance(grazing.Project(outer), grazing.Project(pa), grazing.Project(pc));
                else
                {
                    var gradient = grazing.InverseTransposeLength(pa, new Vector2(label / h, 0f), 2f);
                    // Independently sample the reference ramp every .01 screen pixel.
                    for (var distance = 0.01f; distance <= 2f; distance += 0.01f)
                        if (EdgeRamp.Coverage(distance * gradient, gradient, 1f, 1.3f) <= 1e-5f)
                        { rampWidth = distance; break; }
                }
                break;
            }
            run.Check("P6 grazing ramp at x=" + x, rampWidth >= 1.25f && rampWidth <= 1.35f, $"{rampWidth:F4}px");
        }
        foreach (var body in new[] { "R x=30 y=50 w=200 h=120 f=#fff fea=2", "L p=[30,100,360,100] sw=2 s=#fff fea=2 cap=round",
            "YS n=8 x=\"=30+i*45\" y=120 y2=240 f=#fff fea_edge=3",
            "G blur=5 { R x=80 y=80 w=200 h=120 f=#fff }", "R x=80 y=80 w=200 h=120 f=#fff fea=2 sh=[2,3,4,0,#000]",
            "R x=80 y=80 w=200 h=120 f=#fff fea=2 sh=[2,3,4,0,#000,inset]" })
            run.Check("P7 explicit/blur/shadow bytes: " + body, FingerprintTests.MeshChecksum(Emit(body, true), true)
                == FingerprintTests.MeshChecksum(Emit(body, false), true), "exact float bytes");
        foreach (var body in new[] { bodies[0].Item2, bodies[1].Item2, bodies[2].Item2, bodies[3].Item2, bodies[7].Item2 })
        {
            var on = Emit(body, true); var off = Emit(body, false);
            run.Check("P7 uniform counts: " + body, on.currentVertCount == off.currentVertCount && on.TriangleCount == off.TriangleCount,
                $"{on.currentVertCount}/{off.currentVertCount} vertices, {on.TriangleCount}/{off.TriangleCount} triangles");
        }
        foreach (var (name, body, target) in new[] {
            ("root y fit", "R x=30 y=50 w=200 h=120 f=#fff", new Rect(0,0,400,800)),
            ("group y scale", "G s=[1,3] { R x=30 y=10 w=200 h=120 f=#fff }", new Rect(0,0,400,400)),
            ("group affine", "G m=[1,0.3,0.6,2,0,0] { R x=30 y=10 w=200 h=120 f=#fff }", new Rect(0,0,400,400)) })
            Band(run, name, Emit(body, pixel, Rig(1,1), target: target), Rig(1,1), 0f, false);
        var holes = Emit("P d=\"M30 50 H360 V300 H30 Z M120 130 H260 V220 H120 Z\" fr=evenodd f=#fff", pixel);
        var holeRing = false;
        for (var i = 0; i < holes.currentVertCount; i++)
        {
            var p = holes.Vertex(i).Position;
            if (holes.CoverageUv(i).y > 0f && p.x > 120 && p.x < 260 && p.y > 180 && p.y < 270) holeRing = true;
        }
        run.Check("hole boundaries feather inward", holeRing, "encoded band inside empty hole");
        Band(run, "hole boundary", holes, Rig(1f, 1f), 0f, false);
        foreach (var cap in new[] { "butt", "square", "round" })
        {
            var mesh = Emit("L p=[100,200,300,200] sw=10 s=#fff cap=" + cap, pixel);
            var ends = 0;
            for (var i = 0; i < mesh.currentVertCount; i++)
                if (mesh.CoverageUv(i).y < 0f && (mesh.Vertex(i).Position.x < 100f || mesh.Vertex(i).Position.x > 300f)) ends++;
            run.Check("stroke ends feather: " + cap, ends >= 4, $"{ends} band vertices past endpoints");
        }
        const string ordered = "R x=10 y=10 w=100 h=100 f=#fff id=box click=1\n"
            + "T x=20 y=20 w=50 h=20 text=hello\nR x=25 y=25 w=30 h=30 f=#fff";
        var onStats = new TessellationStats(); var offStats = new TessellationStats();
        var orderedOn = Emit(ordered, true, stats: onStats); var orderedOff = Emit(ordered, false, stats: offStats);
        TextOrder.Assign(onStats.Text, orderedOn); TextOrder.Assign(offStats.Text, orderedOff);
        var onSlices = orderedOn.Slices(); var offSlices = orderedOff.Slices();
        TextOrder.Resolve(onStats.Text, orderedOn, onSlices); TextOrder.Resolve(offStats.Text, orderedOff, offSlices);
        var sameBounds = orderedOn.ShapeCount == orderedOff.ShapeCount;
        for (var i = 0; i < orderedOn.ShapeCount; i++) sameBounds &= orderedOn.ShapeBounds(i) == orderedOff.ShapeBounds(i);
        run.Check("P7: bounds and slices unchanged", sameBounds && onSlices == offSlices, $"{onSlices}/{offSlices} slices");
        run.Check("P7: label placement unchanged", onStats.Text.Count == 1 && offStats.Text.Count == 1
            && onStats.Text[0].Rect == offStats.Text[0].Rect && onStats.Text[0].SliceDepth == offStats.Text[0].SliceDepth,
            "same rectangle and draw order");
        run.Check("P7: hit outline unchanged", onStats.Hits.Count == 1 && offStats.Hits.Count == 1
            && string.Join(";", onStats.Hits[0].Outline) == string.Join(";", offStats.Hits[0].Outline), "painted outline");
        foreach (var (available, uv) in new[] { (false, true), (true, false), (false, false) })
            run.Check($"P7: shader/channel fallback {available}/{uv}",
                FingerprintTests.MeshChecksum(Emit(bodies[0].Item2, true, available: available, uv: uv), true)
                == FingerprintTests.MeshChecksum(Emit(bodies[0].Item2, false, available: available, uv: uv), true), "exact bytes");
        var masked = Emit("DEFS { GL id=m units=bbox x1=0 y1=0 x2=1 y2=0 stops=[[0,#ffffffff],[1,#ffffff00]] }\n"
            + "G mask=@m { R x=100 y=100 w=100 h=100 f=#fff }", true);
        var correctMaskBox = true; var maskCorners = 0;
        for (var i = 0; i < masked.currentVertCount; i++)
        {
            var p = masked.Vertex(i).Position;
            if (masked.CoverageUv(i).y == 0f && (p.x == 100f || p.x == 200f) && (p.y == 200f || p.y == 300f))
            {
                correctMaskBox &= Math.Abs(masked.Colour(i).a - (p.x == 100f ? 1f : 0f)) < 1e-5f;
                maskCorners++;
            }
        }
        run.Check("P7: mask bbox excludes solid edge bands", correctMaskBox && maskCorners >= 4,
            $"{maskCorners} original corners retain bbox endpoints");
        var clippedStroke = Emit("DEFS { CP id=box { R x=100 y=100 w=200 h=200 } }\n"
            + "G clip=box { L p=[0,200,400,200] sw=10 s=#fff }", pixel);
        var clippedEnds = true;
        for (var i = 0; i < clippedStroke.currentVertCount; i++)
            if (clippedStroke.CoverageUv(i).y < 0f)
                clippedEnds &= clippedStroke.Vertex(i).Position.x >= 100f && clippedStroke.Vertex(i).Position.x <= 300f;
        run.Check("stroke end bands stay inside clip", clippedEnds, "no halo at an artificial cut end");
        var near = Emit(bodies[0].Item2, pixel, Rig(-0.1f, 0.549f));
        var finite = true;
        for (var i = 0; i < near.currentVertCount; i++)
        {
            var p = near.Vertex(i).Position;
            finite &= float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(near.CoverageUv(i).y);
        }
        run.Check("P8 near-plane mesh finite", finite && near.currentVertCount > 0, "one corner behind; uniform fallback");
        var uniform = Emit(bodies[0].Item2, pixel);
        var behindProjection = Rig(-0.1f, 0.549f);
        var fallbackVertices = 0; var sameFallback = true;
        // A shared rectangle fan has four inner vertices followed by its four outer vertices.
        for (var i = 4; i < near.currentVertCount; i++)
            if (behindProjection.Depth(near.Vertex(i - 4).Position) <= behindProjection.Near)
            {
                sameFallback &= near.Vertex(i).Position == uniform.Vertex(i).Position;
                fallbackVertices++;
            }
        run.Check("P8 behind vertices use uniform band extent", sameFallback && fallbackVertices == 2,
            $"{fallbackVertices} behind vertices match the uniform mesh exactly");
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
                var sigmaMax = Math.Abs((double)a * d - (double)b * c) / sigma;
                var eigenMax = xx + yy - eigen;
                run.Check($"P1 {name}: maximum singular value", Math.Abs(sigmaMax * sigmaMax - eigenMax) <= 1e-3 * eigenMax,
                    "determinant / sigma_min matches the larger eigenvalue");
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
        var gate = FingerprintTests.FindGateFolder(AppContext.BaseDirectory);
        var page = Path.Combine(Directory.GetParent(Directory.GetParent(gate)!.FullName)!.FullName, "InGameTest-0.11.117.lua");
        var lines = new List<string>();
        foreach (var line in File.ReadAllLines(page))
            if (line.TrimStart().StartsWith("\"", StringComparison.Ordinal))
                lines.Add(JsonSerializer.Deserialize<string>(line.Trim().TrimEnd(','))!);
        foreach (var at in new[] { 50, 350, 650, 950 })
        {
            lines.Add($"R x={at} y=360 w=8 h=480 f=#fff");
            lines.Add($"R x=0 y={at} w=1000 h=8 f=#fff");
        }
        var pageScene = SceneParser.Parse(SceneText.ToProps(string.Join("\n", lines), "measurement")!)!;
        run.Check("measurement page parses", pageScene.Problems.Count == 0 && lines.Count == 21,
            string.Join("; ", pageScene.Problems) + $" ({lines.Count} lines)");
        Meshes(run);
        var mesh = new MeshBuilder();
        mesh.TrackBounds(true);
        mesh.AddVert(Vector3.zero, Color.white, Vector2.zero, 1f, 0f);
        mesh.AddVert(new Vector3(100f, 100f), Color.white, Vector2.zero, 0f, 1f);
        mesh.MarkShape();
        run.Check("P7: edge band excluded from shape bounds", mesh.ShapeBounds(0).width == 0f, "solid outer colour does not cover labels");
    }
}
