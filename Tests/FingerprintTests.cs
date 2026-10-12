using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace ScriptedScreensVector.Tests;

internal static class FingerprintTests
{
    internal static void Run(TestRun run, bool rebaseline)
    {
        var folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../..", "gate"));
        var baseline = Path.Combine(folder, "fingerprint.txt");
        var scenes = Path.Combine(folder, "scenes");
        run.Check("fingerprint: nofill control differs only by the flag",
            File.ReadAllText(Path.Combine(scenes, "zzz-nofill.txt")).Replace(" nofill=1", "")
                == File.ReadAllText(Path.Combine(scenes, "zzz-nofill-control.txt")),
            "same geometry and paints");
        var actual = Fingerprint(scenes, run);
        if (rebaseline)
        {
            if (run.HasFailures)
            {
                run.Fail("fingerprint: baseline unchanged because a corpus check failed");
                return;
            }
            File.WriteAllText(baseline, string.Join("\n", actual) + "\n", new UTF8Encoding(false));
            run.Pass($"fingerprint: rewrote {actual.Count} rows");
            return;
        }

        var expected = File.ReadAllLines(baseline);
        for (var i = 0; i < Math.Max(expected.Length, actual.Count); i++)
        {
            var want = i < expected.Length ? expected[i] : "<missing>";
            var got = i < actual.Count ? actual[i] : "<missing>";
            run.Check($"fingerprint line {i + 1}", got == want,
                got == want ? got : $"expected: {want}\n        actual: {got}");
        }
    }

    private static List<string> Fingerprint(string folder, TestRun run)
    {
        var rows = new List<string>();
        var files = new List<string>(Directory.GetFiles(folder, "*.txt"));
        files.Sort(StringComparer.Ordinal);
        foreach (var path in files)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var props = SceneText.ToProps(File.ReadAllText(path), name);
            if (props == null)
            {
                rows.Add($"{name}|REJECTED|{SceneText.Rejected}");
                run.Fail($"fingerprint: {rows[rows.Count - 1]}");
                continue;
            }

            var scene = SceneParser.Parse(props);
            if (scene == null)
            {
                rows.Add($"{name}|PARSE-NULL");
                run.Fail($"fingerprint: {rows[rows.Count - 1]}");
                continue;
            }

            if (name.StartsWith("zzz-", StringComparison.Ordinal))
                run.Check($"fingerprint: {name} parses", scene.Problems.Count == 0,
                    string.Join(" | ", scene.Problems));
            if (name == "zzz-font")
                run.Check("fingerprint: FONT retained without downloading",
                    scene.FontLinks.Count == 1 && scene.FontLinks[0] == "https://fonts.googleapis.com/css2?family=Inter",
                    string.Join(" | ", scene.FontLinks));

            var target = new Rect(0f, 0f, 400f, 400f);
            var windows = new List<(string Name, Rect? Visible)>();
            // The billion-unit fixture only runs windowed: an unculled pass would exhaust memory.
            if (name != "zzz-dash-window")
                windows.Add((name, null));
            if (name == "zzz-dash" || name == "zzz-dash-window")
                windows.Add((name + "-visible", target));

            foreach (var window in windows)
            foreach (var time in new[] { 0f, 1.5f, 7.25f })
            {
                var mesh = new MeshBuilder();
                var context = new EvalContext { Time = time };
                var stats = new TessellationStats();
                var hits = new List<HitRegion>();
                Tessellator.HitsFound = hits;
                int shapes;
                try
                {
                    shapes = Tessellator.Emit(mesh, scene, context, target,
                        2f, true, stats, window.Visible);
                }
                catch (Exception e)
                {
                    rows.Add(FormattableString.Invariant($"{window.Name}|t={time}|THREW|{e.GetType().Name}: {e.Message}"));
                    run.Fail($"fingerprint: {rows[rows.Count - 1]}");
                    continue;
                }
                finally
                {
                    Tessellator.HitsFound = null;
                }

                rows.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0}|t={1}|verts={2}|tris={3}|shapes={4}|hits={5}|labels={6}|bounds={7}|problems={8}",
                    window.Name, time, mesh.currentVertCount, mesh.TriangleCount, shapes,
                    hits.Count, stats.Text.Count, Bounds(mesh), scene.Problems.Count));

                if (window.Visible != null)
                    run.Check($"fingerprint: {window.Name} bounded at t={time}",
                        mesh.currentVertCount > 0 && mesh.currentVertCount <= 1024 && stats.CulledDashes,
                        $"{mesh.currentVertCount} vertices; culled={stats.CulledDashes}");
                if (name == "zzz-nofill")
                    run.Check($"fingerprint: nofill removes all three fills at t={time}",
                        mesh.ShapeCount == 2 && mesh.ShapeStart(1) == 0 && mesh.currentVertCount == 8,
                        $"first fill boundary={mesh.ShapeStart(1)}; total={mesh.currentVertCount} vertices (stroke only)");
                if (name == "zzz-nofill-control")
                    run.Check($"fingerprint: fills draw without nofill at t={time}",
                        mesh.ShapeCount == 3 && mesh.ShapeStart(1) > 0
                        && mesh.ShapeStart(2) > mesh.ShapeStart(1)
                        && mesh.currentVertCount - mesh.ShapeStart(2) > 8,
                        $"radial={mesh.ShapeStart(1)}, blurred={mesh.ShapeStart(2) - mesh.ShapeStart(1)}, "
                        + $"flat with stroke={mesh.currentVertCount - mesh.ShapeStart(2)} vertices");
            }
        }
        return rows;
    }

    private static string Bounds(MeshBuilder mesh)
    {
        if (mesh.currentVertCount == 0)
            return "empty";
        var min = mesh.Vertex(0).Position;
        var max = min;
        for (var i = 0; i < mesh.currentVertCount; i++)
        {
            var p = mesh.Vertex(i).Position;
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        return string.Format(CultureInfo.InvariantCulture, "{0:0.00},{1:0.00}..{2:0.00},{3:0.00}",
            min.x, min.y, max.x, max.y);
    }
}
