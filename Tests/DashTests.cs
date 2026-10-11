using System;
using System.Collections.Generic;
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

    private static bool Equal(List<List<Vector2>> actual, List<List<Vector2>> expected)
    {
        if (actual.Count != expected.Count)
            return false;
        for (var i = 0; i < actual.Count; i++)
        {
            if (actual[i].Count != expected[i].Count)
                return false;
            for (var j = 0; j < actual[i].Count; j++)
                if (Vector2.Distance(actual[i][j], expected[i][j]) > 1e-4f)
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
        while (skip >= lengths[index])
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
