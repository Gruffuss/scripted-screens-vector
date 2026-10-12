using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;

namespace ScriptedScreensVector.Tests;

internal static class DashTests
{
    internal static void Run(TestRun run)
    {
        var points = new[] {
            new Vector2(1100.125f, 1000.375f), new Vector2(1285.123f, 1067.456f),
            new Vector2(1406.78f, 985.877f), new Vector2(1560.24f, 1085.93f) };
        var pattern = new[] { 7f, 3f, 2f, 3f };
        var expected = Reference(points, pattern, 4.5);
        var actual = Stroke.Dash(points, pattern, 4.5f, new Rect(1000f, 900f, 700f, 300f));
        run.Check("dash: covering window and no window match the reference",
            Equal(actual, expected) && Equal(Stroke.Dash(points, pattern, 4.5f), expected),
            $"{actual.Count} runs, reference {expected.Count}; points within 1e-4");

        points = new[] { new Vector2(-1000f, 0f), new Vector2(1000f, 0f) };
        actual = Stroke.Dash(points, new[] { 11f, 11f }, 0f, new Rect(0f, -5f, 100f, 10f));
        expected = new List<List<Vector2>>();
        for (var start = -1000; start < 100; start += 22)
            if (start + 11 > 0)
                expected.Add(new List<Vector2> { new(Math.Max(0, start), 0), new(Math.Min(100, start + 11), 0) });
        run.Check("dash: crossing window clips runs without restarting phase", Equal(actual, expected),
            $"{actual.Count} runs, want {expected.Count}, first starts at 0");

        points = new[] { Vector2.zero, new Vector2(30f, 0f) };
        var parity = true;
        foreach (var offset in new[] { 0f, 5f, 15f, 10f, -5f })
        {
            actual = Stroke.Dash(points, new[] { 5f }, offset);
            var start = offset == 0f || offset == 10f ? 0f : 5f;
            parity &= actual.Count > 0 && actual[0][0].x == start;
        }
        run.Check("dash: odd pattern offsets keep on/off parity", parity,
            "offsets 0, 5, 15, 10, -5 start at 0, 5, 5, 0, 5");

        actual = Stroke.Dash(points, new[] { 5f, -3f, 0f, 2f }, 0f);
        expected = Reference(points, new[] { 5f, 0f, 0f, 2f }, 0);
        run.Check("dash: negative entries clamp to zero", Equal(actual, expected),
            $"{actual.Count} runs, want {expected.Count}");

        actual = Stroke.Dash(points, new[] { 0f, 10f }, 0f);
        expected = Reference(points, new[] { 0f, 10f }, 0);
        run.Check("dash: zero first entry keeps the initial dot", Equal(actual, expected)
            && actual[0].Count == 2 && actual[0][0] == Vector2.zero && actual[0][1] == Vector2.zero,
            $"{actual.Count} runs, reference {expected.Count}");

        var closed = new List<Vector2> { Vector2.zero, new(12, 0), new(12, 9), new(0, 9), Vector2.zero };
        expected = Reference(closed, new[] { 5f, 3f }, 2);
        run.Check("dash: closed polyline walks the appended closing edge",
            Equal(Stroke.Dash(closed, new[] { 5f, 3f }, 2f), expected), "all four edges match the reference");

        var bent = new[] { new Vector2(1, 1), new(9, 1), new(15, 1), new(15, 9), new(9, 9), new(1, 9) };
        var clip = ClipRegion.FromPolygon(new List<Vector2> { new(0, 0), new(10, 0), new(10, 10), new(0, 10) })!;
        expected = new List<List<Vector2>>();
        foreach (var dash in Reference(bent, new[] { 5f, 3f }, 1.5))
            expected.AddRange(clip.ClipPolyline(dash));
        actual = Stroke.Dash(bent, new[] { 5f, 3f }, 1.5f, new Rect(0, 0, 10, 10));
        run.Check("dash: outside segment preserves phase between inside segments", Equal(actual, expected),
            "full reference runs independently clipped to the window");

        expected = Reference(bent, new[] { 3f, 2f, 4f }, 11);
        run.Check("dash: odd multi-segment pattern with offset",
            Equal(Stroke.Dash(bent, new[] { 3f, 2f, 4f }, 11f), expected), "physically doubled reference pattern");

        foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            expected = Reference(points, new[] { 5f, 3f }, 0);
            run.Check($"dash: {value} offset behaves as zero",
                Equal(Stroke.Dash(points, new[] { 5f, 3f }, value), expected), "finite runs at offset zero");
            foreach (var window in new[] { new Rect(value, -1, 10, 2), new Rect(-1, value, 10, 2),
                new Rect(10, -1, value, 2), new Rect(-1, 1, 10, value) })
                run.Check($"dash: non-finite window {window} behaves as no window",
                    Equal(Stroke.Dash(points, new[] { 5f, 3f }, 0f, window), expected), "unculled finite runs");

            // Invalid segments may hang; kill an isolated runner rather than leave a spinning task.
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            if (System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet")
                start.ArgumentList.Add(typeof(DashTests).Assembly.Location);
            start.ArgumentList.Add("--dash-invalid-segment");
            start.ArgumentList.Add(float.IsNaN(value) ? "NaN" : value > 0 ? "+Infinity" : "-Infinity");
            using var child = Process.Start(start)!;
            var ended = child.WaitForExit(2000);
            if (!ended)
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
            }
            run.Check($"dash: {value} segments terminate and end the open run",
                ended && child.ExitCode == 0, ended ? $"child exit {child.ExitCode}" : "child killed after 2 s");
        }

        WindowChecks(run);

        var longPattern = new float[512];
        Array.Fill(longPattern, 1f);
        var outside = new Rect(100, 100, 10, 10);
        Stroke.Dash(points, longPattern, 1f, outside);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        Stroke.Dash(points, longPattern, 1f, outside);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        run.Check("dash: warmed pattern scratch is reused",
            allocated < longPattern.Length * sizeof(double), $"{allocated} bytes, less than a pattern buffer");
        run.Check("dash: shorter odd pattern ignores unused scratch entries",
            Equal(Stroke.Dash(bent, new[] { 3f, 2f, 4f }, 11f), Reference(bent, new[] { 3f, 2f, 4f }, 11)),
            "512-entry scratch followed by a 3-entry pattern");

        // A stuck worker must fail the check rather than block the runner.
        var task = Task.Run(() => Stroke.Dash(new[] { Vector2.zero, new Vector2(1e9f, 0f) },
            new[] { 11f, 11f }, 0f, new Rect(-10f, -10f, 230f, 230f)));
        var completed = task.Wait(2000);
        var bounded = completed && task.Result.Count == 10 && task.Result[0][0] == Vector2.zero;
        if (completed)
            foreach (var dash in task.Result)
                foreach (var point in dash)
                    bounded &= point.x >= -10f && point.x <= 220f && point.y >= -10f && point.y <= 220f;
        run.Check("dash: billion-unit segment completes within 2 s with bounded runs", bounded,
            completed ? $"{task.Result.Count} runs" : "timed out after 2000 ms");
    }

    private static void WindowChecks(TestRun run)
    {
        var screen = new Rect(10, 20, 100, 50);
        var expanded = DashWindow.Expand(screen);
        run.Check("dash window: expansion covers 3x3 screens",
            expanded == new Rect(-90, -30, 300, 150), "one screen of margin on every side");
        run.Check("dash window: only leaving the build window requests a rebuild",
            !DashWindow.NeedsRebuild(expanded, screen)
            && !DashWindow.NeedsRebuild(expanded, new Rect(-90, -30, 100, 50))
            && !DashWindow.NeedsRebuild(expanded, new Rect(110, 70, 100, 50))
            && DashWindow.NeedsRebuild(expanded, new Rect(-91, 20, 100, 50))
            && DashWindow.NeedsRebuild(expanded, new Rect(10, -31, 100, 50))
            && DashWindow.NeedsRebuild(expanded, new Rect(111, 20, 100, 50))
            && DashWindow.NeedsRebuild(expanded, new Rect(10, 71, 100, 50))
            && DashWindow.NeedsRebuild(expanded, null)
            && DashWindow.NeedsRebuild(expanded, new Rect(float.NaN, 0, 100, 50))
            && !DashWindow.NeedsRebuild(null, screen), "inclusive edges, all four exits, unavailable screen, unculled build");
        run.Check("dash window: non-finite bounds and expansion overflow are rejected",
            !DashWindow.Finite(new Rect(float.NaN, 0, 100, 50))
            && !DashWindow.Finite(new Rect(0, float.PositiveInfinity, 100, 50))
            && !DashWindow.Finite(new Rect(0, 0, float.NegativeInfinity, 50))
            && !DashWindow.Finite(new Rect(0, 0, 100, float.NaN))
            && DashWindow.Expand(new Rect(float.MaxValue / 2, 0, float.MaxValue / 2, 50)) == null,
            "invalid inverse-transform results disable culling");

        var stats = new TessellationStats();
        var node = new VecNode { Op = VecOp.Polyline, HasStroke = true,
            Points = new[] { -200f, 0f, 250f, 0f }, DashPattern = new[] { 5f, 3f } };
        var scene = new VecScene { Fit = FitMode.Stretch };
        scene.Root.Add(node);
        var window = DashWindow.Expand(new Rect(0, 0, 100, 100));
        Task.Run(() => Tessellator.Emit(new MeshBuilder(), scene, new EvalContext(),
            new Rect(0, 0, 100, 100), 1f, true, stats, window)).GetAwaiter().GetResult();
        run.Check("dash window: worker reports an outline beyond the window",
            stats.CulledDashes, "harvested from the producing thread");

        var quiet = true;
        foreach (var (path, pattern, visible) in new[] {
            (new[] { -50f, 0f, 50f, 0f }, new[] { 5f, 3f }, window),
            (new[] { -200f, 0f, 250f, 0f }, Array.Empty<float>(), window),
            (new[] { -200f, 0f, 250f, 0f }, new[] { 5f, 3f }, (Rect?)null) })
        {
            stats.CulledDashes = true;
            node.Points = path;
            node.DashPattern = pattern;
            Tessellator.Emit(new MeshBuilder(), scene, new EvalContext(), new Rect(0, 0, 100, 100), 1f, true, stats, visible);
            quiet &= !stats.CulledDashes;
        }
        run.Check("dash window: fully contained, undashed and unwindowed builds clear the flag",
            quiet, "later builds must not retain the worker flag");
    }

    internal static bool InvalidSegment(string kind)
    {
        var value = kind == "NaN" ? float.NaN : kind == "+Infinity" ? float.PositiveInfinity : float.NegativeInfinity;
        foreach (var invalid in new[] { new Vector2(value, 0), new Vector2(0, value) })
            foreach (Rect? window in new Rect?[] { null, new Rect(-1, -1, 20, 2) })
            {
                var points = new[] { Vector2.zero, new Vector2(2, 0), invalid, new Vector2(7, 0), new Vector2(15, 0) };
                var expected = new List<List<Vector2>> { new() { Vector2.zero, new(2, 0) }, new() { new(7, 0), new(10, 0) } };
                if (!Equal(Stroke.Dash(points, new[] { 5f, 5f }, 0, window), expected))
                    return false;
            }
        return true;
    }

    private static bool Equal(List<List<Vector2>> actual, List<List<Vector2>> expected)
    {
        if (actual.Count != expected.Count)
            return false;
        for (var i = 0; i < actual.Count; i++)
        {
            if (actual[i].Count != expected[i].Count)
                return false;
            for (var j = 0; j < actual[i].Count; j++)
                if (!float.IsFinite(actual[i][j].x) || !float.IsFinite(actual[i][j].y)
                    || Vector2.Distance(actual[i][j], expected[i][j]) > 1e-4f)
                    return false;
        }
        return true;
    }

    // Independent full-line walker: repeat odd patterns physically, then consume the offset.
    private static List<List<Vector2>> Reference(IReadOnlyList<Vector2> points, float[] pattern, double offset)
    {
        var lengths = new List<double>();
        for (var copy = 0; copy < (pattern.Length % 2 == 0 ? 1 : 2); copy++)
            foreach (var entry in pattern)
                lengths.Add(Math.Max(0, entry));
        double total = 0;
        foreach (var entry in lengths)
            total += entry;
        var skip = ((offset % total) + total) % total;
        var index = 0;
        while (skip > 0 && skip >= lengths[index])
        {
            skip -= lengths[index];
            index = (index + 1) % lengths.Count;
        }
        var remaining = lengths[index] - skip;
        var runs = new List<List<Vector2>>();
        List<Vector2>? current = index % 2 == 0 ? new() { points[0] } : null;
        for (var i = 1; i < points.Count; i++)
        {
            var from = points[i - 1];
            var to = points[i];
            var dx = (double)to.x - from.x;
            var dy = (double)to.y - from.y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            double walked = 0;
            while (length - walked > 0.0001)
            {
                var step = Math.Min(length - walked, remaining);
                walked += step;
                remaining -= step;
                var at = new Vector2((float)(from.x + dx * walked / length), (float)(from.y + dy * walked / length));
                current?.Add(at);
                if (remaining > 0.0001)
                    continue;
                if (current is { Count: >= 2 })
                    runs.Add(current);
                index = (index + 1) % lengths.Count;
                remaining = lengths[index];
                current = index % 2 == 0 ? new() { at } : null;
            }
        }
        if (current is { Count: >= 2 })
            runs.Add(current);
        return runs;
    }
}
