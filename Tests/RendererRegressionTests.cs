using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using SS = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem;

namespace ScriptedScreensVector.Tests;

internal static class RendererRegressionTests
{
    internal static void Run(TestRun run)
    {
        GradientBounds(run);
        CollapsedDashes(run);
        Payloads(run);
        StatsDispatch(run);
        FixRound(run);
    }

    private static void GradientBounds(TestRun run)
    {
        var gradient = new Gradient { BoundingBox = true, Start = Vector2.zero, End = Vector2.right };
        gradient.Positions.Add(0);
        gradient.Positions.Add(1);
        gradient.Colours.Add(new Color(0, 0, 0, 1));
        gradient.Colours.Add(new Color(1, 1, 1, 1));
        var scene = new VecScene { DebugNoFeather = true };
        scene.Gradients["ramp"] = gradient;
        var node = new VecNode { Op = VecOp.Polyline, HasStroke = true, StrokeGradient = "ramp",
            Points = new[] { 0f, 50f, 100f, 50f }, DashPattern = new[] { 10f, 10f } };
        scene.Root.Add(node);
        foreach (var (label, window) in new[] { ("whole", (Rect?)null),
            ("window-cut", (Rect?)new Rect(25, 45, 20, 10)),
            ("clipped", (Rect?)null), ("undashed-clipped", (Rect?)null) })
        {
            if (label == "clipped")
            {
                var group = new VecNode { Op = VecOp.Group, ClipRef = "crop" };
                group.Children.Add(node);
                scene.Root.Clear();
                scene.Root.Add(group);
                scene.Clips["crop"] = new System.Collections.Generic.List<Vector2>
                    { new(25, 0), new(75, 0), new(75, 100), new(25, 100) };
            }
            if (label == "undashed-clipped")
                node.DashPattern = Array.Empty<float>();
            var builder = new MeshBuilder();
            Tessellator.Emit(builder, scene, new EvalContext(), new Rect(0, 0, 100, 100), 1, true,
                new TessellationStats(), window);
            var correct = builder.currentVertCount > 0;
            for (var i = 0; i < builder.currentVertCount; i++)
                correct &= Math.Abs(builder.Colour(i).r - builder.Vertex(i).Position.x / 100f) < 1e-4f;
            run.Check("renderer bbox: " + label + " dashes share the path ramp", correct,
                "every emitted vertex uses x / whole-path width");
        }
    }

    private static void CollapsedDashes(TestRun run)
    {
        var invalid = new Rect(float.NaN, 0, 100, 100);
        var finite = new Rect(0, 0, 100, 100);
        run.Check("renderer window: collapsed state survives expansion and rebuilds on recovery",
            DashWindow.Expand(invalid) is Rect expanded && !DashWindow.Finite(expanded)
            && DashWindow.NeedsRebuild(expanded, finite) && DashWindow.NeedsRebuild(expanded, null)
            && !DashWindow.NeedsRebuild(expanded, invalid) && DashWindow.Expand(null) == null,
            "non-finite is distinct from no canvas; repeated collapsed frames stay quiet");
        var singular = Matrix4x4.identity;
        singular.m00 = 0;
        var nearlySingular = Matrix4x4.identity;
        nearlySingular.m00 = 1e-13f;
        var nonFinite = Matrix4x4.identity;
        nonFinite.m13 = float.PositiveInfinity;
        var reflected = Matrix4x4.identity;
        reflected.m00 = -1;
        run.Check("renderer window: screen and matrix collapse cull all",
            DashWindow.CullAll(invalid, Matrix4x4.identity)
            && DashWindow.CullAll(null, singular) && DashWindow.CullAll(finite, nearlySingular)
            && DashWindow.CullAll(null, nonFinite) && !DashWindow.CullAll(null, Matrix4x4.identity)
            && !DashWindow.CullAll(finite, reflected), "singular threshold, translation infinity, valid and reflected matrices");
        foreach (var collapsedScreen in new[] { true, false })
        {
            var scene = new VecScene();
            var group = new VecNode { Op = VecOp.Group, Sx = Expression.Constant(collapsedScreen ? 1 : 0) };
            group.Children.Add(new VecNode { Op = VecOp.Polyline, HasStroke = true,
                Points = new[] { 0f, 50f, 100f, 50f }, DashPattern = new[] { 1f, 1f } });
            scene.Root.Add(group);
            var builder = new MeshBuilder();
            var stats = new TessellationStats();
            Task.Run(() => Tessellator.Emit(builder, scene, new EvalContext(), finite, 1, true,
                stats, collapsedScreen ? DashWindow.Expand(invalid) : null)).GetAwaiter().GetResult();
            run.Check("renderer window: worker skips " + (collapsedScreen ? "collapsed screen" : "singular node"),
                stats.CulledDashes && builder.currentVertCount == 0, "no stroke emission; culling flag harvested");
        }
    }

    private static void Payloads(TestRun run)
    {
        EvalContext Values()
        {
            var values = new EvalContext();
            values.Scalars["number"] = 3;
            values.Strings["text"] = "same";
            values.Colours["colour"] = new Color(.1f, .2f, .3f, .4f);
            values.Arrays["numbers"] = new[] { 1f, 2f };
            values.StringArrays["texts"] = new[] { "a", "b" };
            values.ColourArrays["colours"] = new[] { new Color(.2f, .3f, .4f, .5f) };
            return values;
        }
        var current = Values();
        var source = Values();
        run.Check("renderer data: identical full payload preserves blending", current.CompareData(source, false) == (false, false),
            "all six value types; fresh arrays; no timing declarations");
        source = new EvalContext { KeepUnmentioned = true };
        source.Strings["text"] = "same";
        var samePatch = current.CompareData(source, false) == (false, false);
        source.KeepUnmentioned = false;
        var removed = current.CompareData(source, false) == (true, true);
        source.KeepUnmentioned = true;
        source.Strings["number"] = "3";
        run.Check("renderer data: patches retain names; full payload drops names; type changes rebuild",
            samePatch && removed && current.CompareData(source, false) == (true, true), "name sets and typed equality");
        foreach (var kind in new[] { "scalar", "string", "colour", "array", "strings", "colours" })
        {
            source = Values();
            switch (kind)
            {
                case "scalar": source.Scalars["number"] = 4; break;
                case "string": source.Strings["text"] = "different"; break;
                case "colour": source.Colours["colour"] = new Color(.1f, .2f, .3f, .40001f); break;
                case "array": source.Arrays["numbers"][1] = 9; break;
                case "strings": source.StringArrays["texts"][1] = "c"; break;
                case "colours": source.ColourArrays["colours"][0] = new Color(1, 0, 0, 1); break;
            }
            run.Check("renderer data: " + kind + " changes detected exactly",
                current.CompareData(Values(), false) == (false, false) && current.CompareData(source, false) == (true, true),
                "equal baseline skips; changed value rebuilds");
        }
        source = Values();
        source.Arrays["numbers"] = new[] { 1f };
        var lengthChanged = current.CompareData(source, false) == (true, true);
        source = Values();
        source.Scalars.Remove("number");
        source.Scalars["renamed"] = 3;
        run.Check("renderer data: array length and same-count renamed keys change values",
            current.CompareData(Values(), false) == (false, false) && lengthChanged
            && current.CompareData(source, false) == (true, true), "contents and name identity");
        var sinceScene = SceneParser.Parse(SceneText.ToProps("R x==since($number) w=5 h=5", "since")!)!;
        var ordinaryScene = SceneParser.Parse(SceneText.ToProps("R x==t w=5 h=5", "ordinary")!)!;
        var defsScene = SceneParser.Parse(SceneText.ToProps(
            "DEFS { GL id=ramp x1==since($number) }\nR w=5 h=5", "defs")!)!;
        var patchedScene = SceneParser.Parse(SceneText.ToProps("R id=box w=5 h=5", "patch")!)!;
        SS.UiValue Map(params SS.UiProp[] props) => new() { Type = SS.UiValueType.Map, Map = props };
        var patched = SceneParser.PatchNodes(new[] { new SS.UiProp { Key = "nodes", Value = Map(
            new SS.UiProp { Key = "box", Value = Map(new SS.UiProp { Key = "x", Value = new SS.UiValue
                { Type = SS.UiValueType.String, String = "=since($number)" } }) }) } }, patchedScene);
        run.Check("renderer data: since payload rebuilds without restarting blend",
            sinceScene.UsesSince && defsScene.UsesSince && patched && patchedScene.UsesSince && !ordinaryScene.UsesSince
            && current.CompareData(Values(), sinceScene.UsesSince) == (false, true)
            && current.CompareData(Values(), ordinaryScene.UsesSince) == (false, false),
            "nodes, live defs and node patches track arrivals; ordinary time does not");
    }

    private static void StatsDispatch(TestRun run)
    {
        var pump = new MainThreadWork();
        var mainId = Thread.CurrentThread.ManagedThreadId;
        var direct = pump.Invoke(() => Thread.CurrentThread.ManagedThreadId.ToString(), 0);
        var workerId = mainId;
        var queued = Task.Run(() =>
        {
            workerId = Thread.CurrentThread.ManagedThreadId;
            return pump.Invoke(() => Thread.CurrentThread.ManagedThreadId.ToString());
        });
        var completed = SpinWait.SpinUntil(() => { pump.Pump(); return queued.IsCompleted; }, 2000);
        run.Check("renderer stats: direct and queued calls both describe on main thread",
            completed && direct == mainId.ToString() && queued.GetAwaiter().GetResult() == direct && workerId != mainId,
            "direct call has a zero timeout; background call completes through the pump");

        var called = false;
        var timedOut = Task.Run(() => pump.Invoke(() => { called = true; return "late"; }, 20)).GetAwaiter().GetResult();
        pump.Pump();
        run.Check("renderer stats: timeout reports busy and expires queued work",
            timedOut.Contains("game busy, try again", StringComparison.Ordinal) && !called,
            "a stalled game returns promptly and does not describe after expiry");

        var failure = Task.Run(() =>
        {
            try { return pump.Invoke(() => throw new InvalidOperationException("expected")); }
            catch (InvalidOperationException ex) { return ex.Message; }
        });
        completed = SpinWait.SpinUntil(() => { pump.Pump(); return failure.IsCompleted; }, 2000);
        run.Check("renderer stats: callback errors reach caller without breaking pump",
            completed && failure.GetAwaiter().GetResult() == "expected" && pump.Invoke(() => "next") == "next",
            "subsequent diagnostics still run");
    }

    private static void FixRound(TestRun run)
    {
        var stats = new TessellationStats { DrewTime = false, Starved = "budget" };
        stats.Missing.Add("value");
        var problems = new System.Collections.Generic.List<string> { "landed problem" };
        var warnings = new System.Collections.Generic.HashSet<string> { "landed warning" };
        var snapshot = new BuildDiagnostics();
        snapshot.Capture(stats, problems, "landed drop", warnings);
        Task.Run(() =>
        {
            stats.DrewTime = true;
            stats.Starved = null;
            stats.Missing.Clear();
            problems.Clear();
            problems.Add("next problem");
            warnings.Clear();
        }).GetAwaiter().GetResult();
        run.Check("renderer snapshot: in-flight writes cannot replace landed diagnostics",
            !snapshot.DrewTime && snapshot.Starved == "budget" && snapshot.Dropped == "landed drop"
            && snapshot.Missing.Count == 1 && snapshot.Missing[0] == "value"
            && snapshot.Problems.Count == 1 && snapshot.Problems[0] == "landed problem"
            && snapshot.TextWarnings.Count == 1 && snapshot.TextWarnings[0] == "landed warning",
            "stats, problems, dropped and text warnings retain the last complete build");
        snapshot.Capture(stats, problems, null, warnings);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            if (i % 2 == 0)
                warnings.Add("next warning");
            else
                warnings.Clear();
            snapshot.Capture(stats, problems, null, warnings);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        run.Check("renderer snapshot: landing clears old diagnostics without repeat allocation",
            snapshot.DrewTime && snapshot.Starved == null && snapshot.Dropped == null
            && snapshot.Missing.Count == 0 && snapshot.Problems.Count == 1 && snapshot.Problems[0] == "next problem"
            && snapshot.TextWarnings.Count == 0 && allocated == 0, $"100 captures allocated {allocated} bytes");

        var cadence = new DataCadence();
        var current = new EvalContext();
        current.Scalars["gauge"] = 1;
        var source = new EvalContext();
        source.Scalars["gauge"] = 1;
        cadence.Arrive(1);
        var regular = cadence.Arrive(1.5f);
        var noOps = true;
        for (var now = 2f; now <= 4.5f; now += .5f)
        {
            cadence.Arrive(now);
            noOps &= current.CompareData(source, false) == (false, false);
        }
        source.Scalars["gauge"] = 2;
        run.Check("renderer cadence: idle-value payloads preserve the next half-second glide",
            regular == .5f && noOps && current.CompareData(source, false) == (true, true)
            && cadence.Arrive(5) == .5f && cadence.Arrive(5.01f) == .05f && cadence.Arrive(10) == 1.5f,
            "unchanged arrivals refresh cadence without rebuilding; interval clamps still apply");

        current.Arrays["row"] = new[] { 1f, 2f };
        current.Colours["paint"] = new Color(1, 0, 0, 1);
        source = new EvalContext { KeepUnmentioned = true };
        source.Scalars["gauge"] = 1;
        var unchanged = current.CompareData(source, false) == (false, false);
        source.Snapped.Add("gauge");
        var snap = current.CompareData(source, false) == (true, true);
        source.Snapped.Clear();
        source.Eased["gauge"] = (2, Easing.Linear, .1f);
        var ease = current.CompareData(source, false) == (true, true);
        source.Scalars.Clear();
        source.Eased.Clear();
        source.Arrays["row"] = new[] { 1f, 2f };
        source.Snapped.Add("row");
        var arraySnap = current.CompareData(source, false) == (true, true);
        source.Arrays.Clear();
        source.Snapped.Clear();
        source.Colours["paint"] = new Color(1, 0, 0, 1);
        source.Eased["paint"] = (2, Easing.Linear, 0);
        run.Check("renderer timing: same-value snap and ease payloads still take effect",
            unchanged && snap && ease && arraySnap && current.CompareData(source, false) == (true, true),
            "untimed no-op stays quiet; scalar snap/ease, array snap and colour ease rebuild");

        SS.UiValue Map(params SS.UiProp[] props) => new() { Type = SS.UiValueType.Map, Map = props };
        SS.UiValue Number(float n) => new() { Type = SS.UiValueType.Number, Number = n };
        SS.UiProp Prop(string key, SS.UiValue value) => new() { Key = key, Value = value };
        SS.UiProp[] Patch(float x) => new[] { Prop("nodes", Map(Prop("bar", Map(Prop("x", Number(x)),
            Prop("p", new SS.UiValue { Type = SS.UiValueType.Array, Array = new[] { Number(1), Number(2) } }))))),
            Prop("keep", new SS.UiValue { Type = SS.UiValueType.Bool, Bool = true }),
            Prop("ease", Map(Prop("value", Number(2)))) };
        var previous = Patch(3);
        var repeat = Patch(3);
        Array.Reverse(repeat);
        var changed = Patch(4);
        UiProps.Equal(previous, repeat);
        before = GC.GetAllocatedBytesForCurrentThread();
        var repeats = true;
        for (var i = 0; i < 100; i++)
            repeats &= UiProps.Equal(previous, repeat);
        allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        run.Check("renderer pending patches: identical nested payloads skip merges without allocation",
            repeats && !UiProps.Equal(previous, changed) && allocated == 0,
            $"fresh reordered maps and point arrays; changed patch detected; {allocated} bytes on repeats");
    }
}
