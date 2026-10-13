using System;
using System.Diagnostics;
using UnityEngine;

namespace ScriptedScreensVector.Tests;

internal static class ScaleCost
{
    private const int Samples = 15;
    internal static void Run()
    {
        var scene = SceneParser.Parse(SceneText.ToProps(@"SCENE w=684.3 h=400 fit=stretch
RP n=208 {
 R x==5+mod(i,17)*39 y==8+floor(i/17)*29 w=31 h=22 rx=5 f=#2A3C50 s=#5FD9A8 sw=0.8
}
RP n=13 {
 R x==5+i*39 y=356 w=31 h=22 rx=5 f=#2A3C50 s=#5FD9A8 sw=0.8 sh=[[0,1,5,0,#00000080]]
}", "scale cost")!)!;
        if (scene.Problems.Count != 0) throw new InvalidOperationException(string.Join("; ", scene.Problems));
        var savedBlur = VectorConfig.BlurLod; var savedCurve = VectorConfig.CurveLodEnabled;
        var savedCount = VectorConfig.CountLodEnabled;
        var savedUv = Tessellator.CoverageInUv; var savedPixel = Tessellator.PixelEdgesAvailable;
        VectorConfig.BlurLod = VectorConfig.CurveLodEnabled = VectorConfig.CountLodEnabled = false;
        Tessellator.CoverageInUv = Tessellator.PixelEdgesAvailable = true;
        try
        {
            Console.WriteLine("Scale cost: 221 rounded stroked cards, 13 sigma-5 shadows; LOD off, PixelEdges on; no culling");
            Console.WriteLine($"{Samples} alternating paired samples after 6 paired warmups; median ms; parsing excluded");
            foreach (var (name, projection, rect) in new[] {
                ("side", VisibleScaleTests.Side(), VisibleScaleTests.SideRect),
                ("close in front", VisibleScaleTests.Close(false), VisibleScaleTests.CloseRect),
                ("close crossing near", VisibleScaleTests.Close(true), VisibleScaleTests.CloseRect) })
            {
                var state = new ScaleMeasurement();
                var raw = state.Measure(VisibleScaleTests.Screen(projection, SurfaceProjection.Corner(rect, 0)),
                    VisibleScaleTests.Screen(projection, SurfaceProjection.Corner(rect, 1)), projection.Near, rect.width);
                var legacyKnown = raw > 0f;
                var legacyScale = ScaleChoice.Choose(legacyKnown, raw, 2f);
                var fixedScale = VisibleScaleTests.Measure(ref state, projection, rect, 2f);
                var mesh = new MeshBuilder(); var context = new EvalContext(); var stats = new TessellationStats();
                int Emit(float scale, bool known)
                {
                    var shapes = Tessellator.Emit(mesh, scene, context, rect, scale, known, stats, projection: projection, pixelEdges: true);
                    if (shapes != 221 || stats.Starved != null)
                        throw new InvalidOperationException($"Incomplete benchmark: {shapes} shapes, {stats.Starved}");
                    return mesh.currentVertCount;
                }
                for (var i = 0; i < 6; i++) { Emit(legacyScale, legacyKnown); Emit(fixedScale, true); }
                var oldMs = new double[Samples]; var newMs = new double[Samples];
                var oldVerts = 0; var newVerts = 0; var timer = new Stopwatch();
                double Time(float scale, bool known, out int vertices)
                {
                    timer.Restart(); vertices = Emit(scale, known); timer.Stop(); return timer.Elapsed.TotalMilliseconds;
                }
                for (var i = 0; i < Samples; i++)
                {
                    if ((i & 1) == 0) {
                        oldMs[i] = Time(legacyScale, legacyKnown, out oldVerts);
                        newMs[i] = Time(fixedScale, true, out newVerts);
                    } else {
                        newMs[i] = Time(fixedScale, true, out newVerts);
                        oldMs[i] = Time(legacyScale, legacyKnown, out oldVerts);
                    }
                }
                Array.Sort(oldMs); Array.Sort(newMs);
                Console.WriteLine($"{name}: legacy raw={raw:F6}, chosen={legacyScale:F6} px/unit, {oldMs[Samples/2]:F3} ms, {oldVerts} vertices; "
                    + $"visible={fixedScale:F6} px/unit, {newMs[Samples/2]:F3} ms, {newVerts} vertices");
            }
        }
        finally
        {
            VectorConfig.BlurLod = savedBlur; VectorConfig.CurveLodEnabled = savedCurve; VectorConfig.CountLodEnabled = savedCount;
            Tessellator.CoverageInUv = savedUv; Tessellator.PixelEdgesAvailable = savedPixel;
        }
    }
}
