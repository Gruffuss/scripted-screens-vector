using System;
using UnityEngine;
using SS = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem;

namespace ScriptedScreensVector.Tests;

/// <summary>
/// An author-intent check judges what the AUTHOR wrote, on the first parse and on every patch.
/// </summary>
/// <remarks>
/// `ParseNode` takes the merged map for values and a second map, `own`, for "did the author
/// write this key here" -- the question `so`/`sov` and the enum words ask, because a group
/// hands every key in its `style` down to everything below it. The patch path passed no `own`
/// at all and `own ??= map` fell back to the merged map, which by then was `Merge(SourceProps,
/// patch)` and `SourceProps` is itself the merged map -- so after any `nodes` patch an
/// inherited key read as one the author had written on the node.
///
/// Problems are never cleared for the life of a scene, so one such report paints the magenta
/// border for good: these checks are the reason the intent checks may be trusted on the patch
/// path at all.
/// </remarks>
internal static class PatchPathTests
{
    private static SS.UiValue Num(float value) => new() { Type = SS.UiValueType.Number, Number = value };

    private static SS.UiValue Str(string value) => new() { Type = SS.UiValueType.String, String = value };

    private static SS.UiProp P(string key, SS.UiValue value) => new() { Key = key, Value = value };

    private static SS.UiValue Arr(params SS.UiValue[] items) =>
        new() { Type = SS.UiValueType.Array, Array = items };

    private static SS.UiValue Map(params SS.UiProp[] props) =>
        new() { Type = SS.UiValueType.Map, Map = props };

    private static VecScene Parse(string text) => SceneParser.Parse(SceneText.ToProps(text, "patch")!)!;

    /// <summary>A scene whose header carries `style = { … }`, which the text format cannot write.</summary>
    private static VecScene Styled(SS.UiProp style, params SS.UiProp[] node) =>
        SceneParser.Parse(new[]
        {
            P("scene", Str("patch")), P("w", Num(100)), P("h", Num(100)),
            P("style", Map(style)),
            P("root", Arr(Map(node))),
        })!;

    private static bool Patch(VecScene scene, string id, params SS.UiProp[] keys) =>
        SceneParser.PatchNodes(new[] { P("nodes", Map(P(id, Map(keys)))) }, scene);

    private static string Said(VecScene scene) =>
        scene.Problems.Count == 0 ? "no problems" : string.Join(" | ", scene.Problems);

    private static bool Says(VecScene scene, string fragment)
    {
        foreach (var problem in scene.Problems)
        {
            if (problem.Contains(fragment, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    internal static void Run(TestRun run)
    {
        Console.WriteLine();
        InheritedKeysAreNotTheAuthorsWriting(run);
        AScrollOffsetSurvivesAPatch(run);
        GroupOpacityOnAShape(run);
        AStrayUnitOnALiteral(run);
        ACircleWithNoRadius(run);
        NumericNamesSurviveTheBridge(run);
    }

    /// <summary>A key a group handed down is not a key the author wrote on the child.</summary>
    internal static void InheritedKeysAreNotTheAuthorsWriting(TestRun run)
    {
        // `fit` on a `T` is none/ellipsis/shrink; on an `IMG` it is cover. A group hands `fit`
        // down to every child, so `CheckWord` has always read the node's own map -- and the
        // patch path handed it the merged one.
        var inherited = Parse("SCENE w=100 h=100\nG fit=cover { T id=lbl x=0 y=0 w=50 h=20 text=hi }");
        run.Check("patch: an inherited enum word is silent on the first parse",
            inherited.Problems.Count == 0, Said(inherited));

        var applied = Patch(inherited, "lbl", P("w", Num(60)));
        run.Check("patch: an inherited enum word stays silent after a patch",
            applied && inherited.Problems.Count == 0, $"applied={applied}, {Said(inherited)}");

        // A typo the author wrote IN the patch is still their mistake.
        var typo = Parse("SCENE w=100 h=100\nT id=lbl x=0 y=0 w=50 h=20 text=hi");
        Patch(typo, "lbl", P("align", Str("centerr")));
        run.Check("patch: a typo written in the patch is reported",
            Says(typo, "align \"centerr\""), Said(typo));

        // And one written on the declaration stays reported across a patch.
        var declared = Parse("SCENE w=100 h=100\nT id=lbl x=0 y=0 w=50 h=20 text=hi align=centerr");
        Patch(declared, "lbl", P("w", Num(60)));
        run.Check("patch: a typo on the declaration is still reported",
            Says(declared, "align \"centerr\""), Said(declared));
    }

    /// <summary>`so`/`sov` is the other reader of `own`, and a jump must not be invented.</summary>
    internal static void AScrollOffsetSurvivesAPatch(TestRun run)
    {
        // `so` is stroke opacity on every other op, so an ancestor `G so=0.5` is merged into an
        // `SC` below it. Nothing on the SC asked to scroll; only its `sov` is its own.
        var inherited = Parse("SCENE w=100 h=100\n"
                              + "G so=0.5 { SC id=list x=0 y=0 w=50 h=50 ch=200 sov=1 }");
        run.Check("patch: an inherited so does not scroll on the first parse",
            inherited.Root[0].Children[0].ScrollSet == null,
            inherited.Root[0].Children[0].ScrollSet == null ? "no jump" : "jump set");

        var applied = Patch(inherited, "list", P("ch", Num(300)));
        var scroll = inherited.Identified["list"];
        run.Check("patch: an inherited so does not scroll after a patch",
            applied && scroll.List[scroll.Index].ScrollSet == null,
            $"applied={applied}, " + (scroll.List[scroll.Index].ScrollSet == null ? "no jump" : "jump set"));

        // The documented form -- `so` and `sov` on the SC itself -- keeps working through a
        // patch that mentions neither.
        var own = Parse("SCENE w=100 h=100\nSC id=log x=0 y=0 w=50 h=50 ch=200 so=40 sov=1");
        var kept = Patch(own, "log", P("ch", Num(300)));
        var node = own.Identified["log"];
        var set = node.List[node.Index].ScrollSet;
        run.Check("patch: a declared so survives a patch that does not mention it",
            kept && set != null && Mathf.Abs(set!.Evaluate(new EvalContext()) - 40f) < 0.001f,
            $"applied={kept}, so={(set == null ? "null" : set.Evaluate(new EvalContext()).ToString())}");
    }

    /// <summary>S11: `o` is group opacity, and every other op accepted it in silence.</summary>
    internal static void GroupOpacityOnAShape(TestRun run)
    {
        var rect = Parse("SCENE w=100 h=100\nR x=0 y=0 w=10 h=10 o=0.5");
        run.Check("S11: o on an R is reported", Says(rect, "o is group opacity"), Said(rect));

        var header = Parse("SCENE w=100 h=100 o=0.5\nR x=0 y=0 w=10 h=10");
        run.Check("S11: o on the header is reported", Says(header, "o is group opacity"), Said(header));

        var def = Parse("SCENE w=100 h=100\nDEFS { GL id=g x1=0 y1=0 x2=1 y2=0 o=0.5 }\n"
                        + "R x=0 y=0 w=10 h=10");
        run.Check("S11: o on a gradient def is reported", Says(def, "o is group opacity"), Said(def));

        // The four ops that DO read it stay quiet. `USE` is a group as well.
        var group = Parse("SCENE w=100 h=100\nG o=0.5 { R x=0 y=0 w=10 h=10 }");
        run.Check("S11: o on a G is quiet", group.Problems.Count == 0, Said(group));

        var scroll = Parse("SCENE w=100 h=100\nSC id=l x=0 y=0 w=50 h=50 ch=200 o=0.5");
        run.Check("S11: o on an SC is quiet", scroll.Problems.Count == 0, Said(scroll));

        // THE PATCH PATH: a root `style = { o = 0.5 }` is documented to hand `o` to every node
        // below, where the groups read it and everything else ignores it. The author wrote
        // nothing on the shape, so no patch of it may report.
        var styled = Styled(P("o", Num(0.5f)),
            P("op", Str("R")), P("id", Str("bar")), P("x", Num(0)), P("y", Num(0)),
            P("w", Num(10)), P("h", Num(10)));
        run.Check("S11: an inherited style o is quiet on the first parse",
            styled.Problems.Count == 0, Said(styled));

        var applied = Patch(styled, "bar", P("w", Num(40)));
        run.Check("S11: an inherited style o is quiet after a patch",
            applied && styled.Problems.Count == 0, $"applied={applied}, {Said(styled)}");

        // An `o` written in the patch itself is the author's mistake and is reported.
        var patched = Parse("SCENE w=100 h=100\nR id=bar x=0 y=0 w=10 h=10");
        Patch(patched, "bar", P("o", Num(0.5f)));
        run.Check("S11: o written in a patch is reported",
            Says(patched, "o is group opacity"), Said(patched));
    }

    /// <summary>S6: `unit` follows a value, and a plain literal prints none.</summary>
    internal static void AStrayUnitOnALiteral(TestRun run)
    {
        var literal = Parse("SCENE w=100 h=100\nT x=0 y=0 w=50 h=20 text=\"OK\" unit=\" kPa\"");
        run.Check("S6: a unit on a plain literal is reported",
            Says(literal, "prints no value"), Said(literal));

        // The three forms that DO print it.
        var bound = Parse("SCENE w=100 h=100\nT x=0 y=0 w=50 h=20 text=\"$v\" unit=\" kPa\"");
        run.Check("S6: a unit on a bound value is quiet", bound.Problems.Count == 0, Said(bound));

        var template = Parse("SCENE w=100 h=100\nT x=0 y=0 w=50 h=20 text=\"p {$v} k\" unit=\" kPa\"");
        run.Check("S6: a unit on a literal holding a placeholder is quiet",
            template.Problems.Count == 0, Said(template));

        var plain = Parse("SCENE w=100 h=100\nT x=0 y=0 w=50 h=20 text=\"OK\"");
        run.Check("S6: a literal with no unit is quiet", plain.Problems.Count == 0, Said(plain));

        // A label with no text at all prints nothing, unit or no unit, and its text may arrive
        // by patch: the missing label is the symptom, not the unit.
        var empty = Parse("SCENE w=100 h=100\nT id=lbl x=0 y=0 w=50 h=20 unit=\" kPa\"");
        run.Check("S6: a unit on a label with no text is quiet",
            empty.Problems.Count == 0, Said(empty));

        var filled = Patch(empty, "lbl", P("text", Str("$v")));
        run.Check("S6: and still quiet once the patch binds its text",
            filled && empty.Problems.Count == 0, $"applied={filled}, {Said(empty)}");

        // THE PATCH PATH: a group hands `unit` down through its `style` map, which is
        // functional for the bound readouts under it. A plain-literal title beside them is not
        // a mistake its author made.
        var styled = Styled(P("unit", Str(" kPa")),
            P("op", Str("T")), P("id", Str("title")), P("x", Num(0)), P("y", Num(0)),
            P("w", Num(50)), P("h", Num(20)), P("text", Str("Reactor")));
        run.Check("S6: an inherited style unit is quiet on the first parse",
            styled.Problems.Count == 0, Said(styled));

        var applied = Patch(styled, "title", P("w", Num(60)));
        run.Check("S6: an inherited style unit is quiet after a patch",
            applied && styled.Problems.Count == 0, $"applied={applied}, {Said(styled)}");

        // A patch that turns a bound readout into a plain literal breaks it, and says so.
        var broken = Parse("SCENE w=100 h=100\nT id=v x=0 y=0 w=50 h=20 text=\"$v\" unit=\" kPa\"");
        Patch(broken, "v", P("text", Str("OK")));
        run.Check("S6: a patch that strands the unit is reported",
            Says(broken, "prints no value"), Said(broken));
    }

    /// <summary>S13: a `C` whose radius is written under the wrong name draws nothing.</summary>
    internal static void ACircleWithNoRadius(TestRun run)
    {
        var wrongName = Parse("SCENE w=100 h=100\nC cx=50 cy=50 ry=8");
        run.Check("S13: ry without rx is reported", Says(wrongName, "rx is missing"), Said(wrongName));

        var named = Parse("SCENE w=100 h=100\nC id=dot cx=50 cy=50 ry=8");
        run.Check("S13: the report names the node", Says(named, "C \"dot\""), Said(named));

        var negative = Parse("SCENE w=100 h=100\nC cx=50 cy=50 rx=-5");
        run.Check("S13: a negative radius is reported",
            Says(negative, "rx is negative"), Said(negative));

        var negativeRy = Parse("SCENE w=100 h=100\nC cx=50 cy=50 rx=8 ry=-3");
        run.Check("S13: a negative ry is reported",
            Says(negativeRy, "ry is negative"), Said(negativeRy));

        // An explicit zero is the correct drawing of a zero-sized thing -- a scene generated
        // from a zero datum writes exactly that, and `R w=0` is silent for the same reason --
        // and an expression radius passes through 0 on every cycle of a shrink animation.
        var zero = Parse("SCENE w=100 h=100\nC cx=50 cy=50 rx=0");
        run.Check("S13: an explicit rx=0 is quiet", zero.Problems.Count == 0, Said(zero));

        var animated = Parse("SCENE w=100 h=100\nC cx=50 cy=50 rx=\"=10*sin(t)\"");
        run.Check("S13: an animated radius is quiet", animated.Problems.Count == 0, Said(animated));

        // A radius that only ARRIVES by patch is not a mistake: `nodes` is the geometry patch.
        var later = Parse("SCENE w=100 h=100\nC id=dot cx=50 cy=50");
        run.Check("S13: a C with no radius at all is quiet", later.Problems.Count == 0, Said(later));

        var applied = Patch(later, "dot", P("rx", Num(8)));
        run.Check("S13: and still quiet once the patch supplies rx",
            applied && later.Problems.Count == 0, $"applied={applied}, {Said(later)}");

        // A COMPUTED radius that dips below 0 for one frame is not a mistake either, and
        // Problems is never cleared -- a report there would mark the scene for good.
        var dip = Parse("SCENE w=100 h=100\nC id=dot cx=50 cy=50 rx=5");
        var dipped = Patch(dip, "dot", P("rx", Num(-2)));
        run.Check("S13: a patch frame with a negative radius is quiet",
            dipped && dip.Problems.Count == 0, $"applied={dipped}, {Said(dip)}");

        // But `ry` arriving by patch with no `rx` anywhere is the same wrong name.
        var wrongInPatch = Parse("SCENE w=100 h=100\nC id=dot cx=50 cy=50");
        Patch(wrongInPatch, "dot", P("ry", Num(8)));
        run.Check("S13: ry without rx written in a patch is reported",
            Says(wrongInPatch, "rx is missing"), Said(wrongInPatch));

        // An `rx` handed down by a group's style is a radius, not a mistake.
        var styled = Styled(P("rx", Num(6)),
            P("op", Str("C")), P("cx", Num(50)), P("cy", Num(50)), P("ry", Num(8)));
        run.Check("S13: an inherited rx is quiet", styled.Problems.Count == 0, Said(styled));
    }

    /// <summary>S2: `nodes`, `data` and `ease` keyed by names that all read as numbers.</summary>
    internal static void NumericNamesSurviveTheBridge(TestRun run)
    {
        // ScriptedScreens serialises a Lua table as an ARRAY whenever every key reads as a
        // positive integer, a numeric-looking string key included, so `nodes = { ["1"] = ... }`
        // arrives with the key destroyed and slot `i` holding the name `i + 1`.
        var bar = Parse("SCENE w=100 h=100\nR id=1 x=0 y=0 w=10 h=10");
        var landed = SceneParser.PatchNodes(new[] { P("nodes", Arr(Map(P("w", Num(40))))) }, bar);
        var width = bar.Root[0].W.Evaluate(new EvalContext());
        run.Check("S2: a collapsed nodes patch reaches id 1",
            landed && Mathf.Abs(width - 40f) < 0.001f, $"applied={landed} w={width}, {Said(bar)}");

        // A sparse table leaves Nil holes, and the names must not shift: key 3 is slot 2.
        var third = Parse("SCENE w=100 h=100\nR id=3 x=0 y=0 w=10 h=10");
        var sparse = SceneParser.PatchNodes(
            new[] { P("nodes", Arr(default, default, Map(P("w", Num(7))))) }, third);
        var thirdWidth = third.Root[0].W.Evaluate(new EvalContext());
        run.Check("S2: a hole does not shift the names",
            sparse && Mathf.Abs(thirdWidth - 7f) < 0.001f,
            $"applied={sparse} w={thirdWidth}, {Said(third)}");

        // The map form is untouched.
        var named = Parse("SCENE w=100 h=100\nR id=bar x=0 y=0 w=10 h=10");
        var byName = Patch(named, "bar", P("w", Num(40)));
        run.Check("S2: a named nodes patch still works",
            byName && Mathf.Abs(named.Root[0].W.Evaluate(new EvalContext()) - 40f) < 0.001f,
            $"applied={byName}, {Said(named)}");

        // A patch built as a LIST carries no ids at all, so it never reached any node -- and
        // said nothing. Recovered, its names are the slot numbers, which is worth saying.
        var list = Parse("SCENE w=100 h=100\nR id=bar x=0 y=0 w=10 h=10");
        SceneParser.PatchNodes(new[] { P("nodes", Arr(Map(P("w", Num(40))))) }, list);
        run.Check("S2: a list-shaped patch says so",
            Says(list, "arrived as a list"), Said(list));

        // `data` collapses the same way, and `$1` is a name the expression parser reads.
        var numeric = new EvalContext();
        SceneParser.ReadData(new[] { P("data", Arr(Num(5))) }, numeric);
        run.Check("S2: a collapsed data payload names $1",
            numeric.Scalars.TryGetValue("1", out var one) && Mathf.Abs(one - 5f) < 0.001f,
            numeric.Scalars.TryGetValue("1", out var shown) ? $"{shown}" : "absent");

        // `data = {}` is an empty table, which the host ALSO delivers as an array: still a clear.
        var cleared = new EvalContext();
        cleared.Scalars["old"] = 1f;
        SceneParser.ReadData(new[] { P("data", Arr()) }, cleared);
        run.Check("S2: data={} still clears", cleared.Scalars.Count == 0,
            $"{cleared.Scalars.Count} scalars left");

        // And `ease`, whose keys are the same author names.
        var eased = new EvalContext();
        SceneParser.ReadData(new[] { P("data", Arr(Num(5))), P("ease", Arr(Num(0.5f))) }, eased);
        run.Check("S2: a collapsed ease names $1", eased.Eased.ContainsKey("1"),
            string.Join(",", eased.Eased.Keys));
    }
}
