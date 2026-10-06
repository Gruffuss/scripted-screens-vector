using System;
using System.Collections.Generic;
using UnityEngine;
using SS = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem;

namespace ScriptedScreensVector.Tests;

/// <summary>
/// The silent-failure sweep of 0.11.102: a value the parser read as nothing and said nothing
/// about.
/// </summary>
/// <remarks>
/// Every case here was watched failing on 0.11.101 before the fix went in, which is the only
/// reason any of them is worth keeping: each one is a value an author can plausibly write and
/// which used to be dropped without a word, so a test that passes by accident would hide the
/// same hole again.
///
/// No `#rrggbb` anywhere in a parsed scene: `Colours.TryParse` goes through
/// <c>ColorUtility.TryParseHtmlString</c>, a native ECall that throws outside the player.
/// `transparent` is handled before that call and is the colour these tests use. The one case
/// that NEEDS a hex literal -- a colour expression in a numeric slot -- lives in the offline
/// probe, which swaps in a managed parser.
/// </remarks>
internal static class SweepTests
{
    private static SS.UiValue Num(float value) => new() { Type = SS.UiValueType.Number, Number = value };

    private static SS.UiValue Str(string value) => new() { Type = SS.UiValueType.String, String = value };

    private static SS.UiValue Bool(bool value) => new() { Type = SS.UiValueType.Bool, Bool = value };

    private static SS.UiProp P(string key, SS.UiValue value) => new() { Key = key, Value = value };

    private static SS.UiValue Arr(params SS.UiValue[] items) =>
        new() { Type = SS.UiValueType.Array, Array = items };

    private static SS.UiValue Map(params SS.UiProp[] props) =>
        new() { Type = SS.UiValueType.Map, Map = props };

    /// <summary>A one-node scene whose node carries exactly the props given.</summary>
    private static VecScene Built(params SS.UiProp[] node)
    {
        var props = new[]
        {
            P("scene", Str("sweep")), P("w", Num(100)), P("h", Num(100)),
            P("root", Arr(Map(node))),
        };

        return SceneParser.Parse(props)!;
    }

    private static VecScene Parse(string text) => SceneParser.Parse(SceneText.ToProps(text, "sweep")!)!;

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
        FreeTextTakesANumber(run);
        LuaBooleansAreRead(run);
        ShadowStringsAreReported(run);
        SubstitutionStopsAtTheName(run);
        EnumTyposAreReported(run);
        PercentPairs(run);
        PlaceholderFormatStartsAtTheFirstColon(run);
    }

    /// <summary>C21: a number in a slot that holds free text or an identifier.</summary>
    internal static void FreeTextTakesANumber(TestRun run)
    {
        // `text=700` arrives as a Number because SceneText types any parseable token as one,
        // and the string accessor returned null for it: no label at all, nothing reported.
        var label = Parse("SCENE w=100 h=100\nT x=0 y=0 w=50 h=20 text=700");
        run.Check("C21: text=700 draws the number",
            label.Root.Count == 1 && label.Root[0].TextLiteral == "700",
            $"literal={label.Root[0].TextLiteral ?? "null"}, {Said(label)}");

        // The same hole on `id` cost the node its identity, so a `nodes` patch could not reach
        // it and an event never carried it.
        var identified = Parse("SCENE w=100 h=100\nR x=0 y=0 w=10 h=10 id=1");
        run.Check("C21: id=1 identifies the node", identified.Identified.ContainsKey("1"),
            string.Join(",", identified.Identified.Keys));

        // `missing=0` is the readout that shows a zero until data arrives; it used to keep the
        // default "--" instead.
        var missing = Parse("SCENE w=100 h=100\nT x=0 y=0 w=50 h=20 text=\"$v\" missing=0");
        run.Check("C21: missing=0 prints 0", missing.Root[0].TextMissing == "0",
            missing.Root[0].TextMissing ?? "null");

        var unit = Parse("SCENE w=100 h=100\nT x=0 y=0 w=50 h=20 text=\"$v\" unit=5");
        run.Check("C21: unit=5 is kept", unit.Root[0].TextUnit == "5", unit.Root[0].TextUnit ?? "null");

        // And the keys that must stay strict: a bare number is not a colour, and `f=5` has to
        // keep meaning nothing rather than becoming the string "5".
        var fill = Parse("SCENE w=100 h=100\nR x=0 y=0 w=10 h=10 f=5");
        run.Check("C21: f=5 is still not a colour", !fill.Root[0].HasFill, Said(fill));
    }

    /// <summary>C22: a Lua boolean in a numeric slot is 1 or 0, not absent.</summary>
    internal static void LuaBooleansAreRead(TestRun run)
    {
        // ScriptedScreens hands a Lua boolean over as UiValueType.Bool with Number left at 0,
        // so every numeric reader fell through to its fallback -- and `v`'s fallback is 1, so
        // `v = false` left the node VISIBLE.
        var hidden = Built(P("op", Str("R")), P("x", Num(0)), P("y", Num(0)),
            P("w", Num(10)), P("h", Num(10)), P("v", Bool(false)));
        var visible = hidden.Root[0].Visible;
        run.Check("C22: v=false hides the node",
            visible != null && Mathf.Abs(visible.Evaluate(new EvalContext())) < 0.0001f,
            visible == null ? "v was not read at all" : $"{visible.Evaluate(new EvalContext())}");

        // The mirror case: a default-0 key given `true`.
        var pressed = Built(P("op", Str("R")), P("x", Num(0)), P("y", Num(0)),
            P("w", Num(10)), P("h", Num(10)), P("press", Bool(true)));
        run.Check("C22: press=true is pressable", pressed.Root[0].Pressable, $"{pressed.Root[0].Pressable}");

        var lod = Built(P("op", Str("RP")), P("n", Num(2)), P("lod", Bool(true)));
        run.Check("C22: lod=true allows LOD", lod.Root[0].AllowLod, $"{lod.Root[0].AllowLod}");

        // In an array: a point list or a flags list holding a boolean.
        var flags = new EvalContext();
        SceneParser.ReadData(new[] { P("data", Map(P("flags", Arr(Bool(true), Bool(false), Num(5))))) }, flags);
        run.Check("C22: a boolean in a data array reads 1 or 0",
            flags.Arrays.TryGetValue("flags", out var read) && read.Length == 3
            && read[0] == 1f && read[1] == 0f && read[2] == 5f,
            flags.Arrays.TryGetValue("flags", out var shown) ? string.Join(",", shown) : "absent");

        // The nasty one: under `keep`, a name the payload does not carry keeps its old value.
        // A Bool was in none of the dictionaries, so `alarm = false` did not clear `alarm = 1`
        // -- the alarm stayed on.
        var data = new EvalContext();
        SceneParser.ReadData(new[] { P("data", Map(P("alarm", Bool(false)))) }, data);
        run.Check("C22: alarm=false reaches the scalars as 0",
            data.Scalars.TryGetValue("alarm", out var alarm) && alarm == 0f,
            data.Scalars.TryGetValue("alarm", out var shownAlarm) ? $"{shownAlarm}" : "absent");

        // `keep` and `snap` are read the same way, so `keep = true` used to WIPE the very
        // values it was asked to hold.
        var keep = new EvalContext();
        SceneParser.ReadData(new[] { P("keep", Bool(true)), P("data", Map(P("other", Num(2)))) }, keep);
        run.Check("C22: keep=true keeps", keep.KeepUnmentioned, $"{keep.KeepUnmentioned}");
    }

    /// <summary>C27: a shadow's four numbers are baked at parse time and never evaluated.</summary>
    internal static void ShadowStringsAreReported(TestRun run)
    {
        // `sh = { 0, "=2*t", 8, 0, col }` reads dy as 0: a shadow does not animate, which is
        // by design, but losing the value in silence is not.
        var animated = Parse("SCENE w=100 h=100\nR x=0 y=0 w=10 h=10 sh=[[0,\"=2*t\",8,0,transparent]]");
        run.Check("C27: an expression in a shadow offset is reported", Says(animated, "sh: dy"),
            Said(animated));

        var plain = Parse("SCENE w=100 h=100\nR x=0 y=0 w=10 h=10 sh=[[0,3,8,0,transparent]]");
        run.Check("C27: a plain shadow is still quiet", plain.Problems.Count == 0, Said(plain));

        // A Lua boolean in the same slot, for the C22 rule.
        var flagged = SceneParser.Parse(new[]
        {
            P("scene", Str("sweep")), P("w", Num(100)), P("h", Num(100)),
            P("root", Arr(Map(P("op", Str("R")), P("x", Num(0)), P("y", Num(0)),
                P("w", Num(10)), P("h", Num(10)),
                P("sh", Arr(Arr(Bool(true), Num(3), Num(8), Num(0), Str("transparent")))))))
        })!;
        run.Check("C27: a boolean shadow offset reads 1",
            flagged.Root[0].Shadows != null && flagged.Root[0].Shadows![0].Dx == 1f,
            flagged.Root[0].Shadows == null ? "no shadow" : $"{flagged.Root[0].Shadows![0].Dx}");
    }

    /// <summary>C25: `%name` ends at the first character a name cannot hold.</summary>
    internal static void SubstitutionStopsAtTheName(TestRun run)
    {
        // `%i` used to rewrite the first two characters of `%idx`, so one parameter corrupted
        // another by nothing but being declared first.
        const string Src = "SCENE w=100 h=100\n"
                           + "DEFS { SYM id=sy i=3 idx=7 { T x=0 y=0 w=50 h=20 text=\"i=%i idx=%idx\" } }\n"
                           + "USE ref=sy x=0 y=0";

        var scene = Parse(Src);
        var drawn = scene.Root.Count == 1 && scene.Root[0].Children.Count == 1
            ? scene.Root[0].Children[0].TextLiteral
            : null;

        run.Check("C25: %i does not eat %idx", drawn == "i=3 idx=7", drawn ?? "no label");

        // The narrowing that comes with it: a `%name` glued to more name characters is NOT a
        // parameter and stays literal, where it used to splice the shorter name.
        const string Glued = "SCENE w=100 h=100\n"
                             + "DEFS { SYM id=sy w=10 { T x=0 y=0 w=50 h=20 text=\"%wide\" } }\n"
                             + "USE ref=sy x=0 y=0";

        var glued = Parse(Glued);
        var literal = glued.Root.Count == 1 && glued.Root[0].Children.Count == 1
            ? glued.Root[0].Children[0].TextLiteral
            : null;

        run.Check("C25: an undeclared %wide stays literal", literal == "%wide", literal ?? "no label");
    }

    /// <summary>C28: a word that is not one of the accepted ones.</summary>
    internal static void EnumTyposAreReported(TestRun run)
    {
        var typo = Parse("SCENE w=100 h=100\nT x=0 y=0 w=50 h=20 text=hi fit=elipsis");
        run.Check("C28: a typo in a T's fit is reported", Says(typo, "fit"), Said(typo));

        var cap = Parse("SCENE w=100 h=100\nL p=[0,0,10,10] s=transparent sw=2 cap=rund");
        run.Check("C28: a typo in cap is reported", Says(cap, "cap"), Said(cap));

        // The default words have to be accepted: 62 of the 85 shipped scenes write at least
        // one, and a check listing only the non-default words would put the problem border on
        // every one of them.
        var defaults = Parse("SCENE w=100 h=100 fit=stretch\n"
                             + "T x=0 y=0 w=50 h=20 text=hi fit=none align=left valign=top weight=normal\n"
                             + "P d=\"M0 0 L10 10\" fr=nonzero\n"
                             + "L p=[0,0,10,10] s=transparent sw=2 cap=butt join=miter");
        run.Check("C28: the default words are words", defaults.Problems.Count == 0, Said(defaults));

        // A group hands `fit` down to every child, so a `T` under `G fit=cover` sees a word
        // from the IMG vocabulary. Only what the author wrote ON the node may be judged.
        var inherited = Parse("SCENE w=100 h=100\nG fit=cover { T x=0 y=0 w=50 h=20 text=hi }");
        run.Check("C28: an inherited fit is not judged by the child's vocabulary",
            inherited.Problems.Count == 0, Said(inherited));
    }

    /// <summary>C30: `%%` is a literal percent sign, and collapses to one.</summary>
    internal static void PercentPairs(TestRun run)
    {
        foreach (var (spec, expected) in new[]
                 {
                     ("%%", "%"),
                     ("100%%", "100%"),
                     ("%%%.0f", "%{0:F0}"),
                     ("100%% of %.0f", "100% of {0:F0}"),
                     ("%%%%", "%%"),
                 })
        {
            var actual = Printf.ToNet(spec);
            run.Check($"C30: \"{spec}\" -> \"{expected}\"", actual == expected, actual);
        }

        // A `%%` BEFORE the conversion was the wider half of the fault: the guard fired on the
        // first '%', so the real conversion after it was never looked at and the label printed
        // its own spec, with a FALSE "holds no conversion" to go with it.
        var scene = Parse("SCENE w=100 h=100\nT x=0 y=0 w=50 h=20 text=\"a {=7:%%%.0f} b\"");
        var printed = Tessellator.BindTemplate(scene.Root[0], new EvalContext(), scene, 0);
        run.Check("C30: %% before a conversion still formats", printed == "a %7 b", printed);
        run.Check("C30: and is not reported as holding no conversion", scene.Problems.Count == 0,
            Said(scene));
    }

    /// <summary>C32: `{=expr:spec}` splits at the FIRST colon, as `{$name:spec}` does.</summary>
    internal static void PlaceholderFormatStartsAtTheFirstColon(TestRun run)
    {
        var node = new VecNode { TextMissing = "--" };
        node.TextParts = SceneParser.TextTemplate("{=floor(t/60):%d:00}", null);

        var clock = Tessellator.BindTemplate(node, new EvalContext { Time = 125f }, new VecScene(), 0);
        run.Check("C32: a spec with a colon in it survives", clock == "2:00", clock);

        var tail = new VecNode { TextMissing = "--" };
        tail.TextParts = SceneParser.TextTemplate("{=t:%.0f s: ok}", null);
        var printed = Tessellator.BindTemplate(tail, new EvalContext { Time = 125f }, new VecScene(), 0);
        run.Check("C32: so does trailing text holding one", printed == "125 s: ok", printed);

        // One colon and none are unchanged.
        var one = new VecNode { TextMissing = "--" };
        one.TextParts = SceneParser.TextTemplate("{=t:%.0f}", null);
        run.Check("C32: one colon is unchanged",
            Tessellator.BindTemplate(one, new EvalContext { Time = 125f }, new VecScene(), 0) == "125",
            Tessellator.BindTemplate(one, new EvalContext { Time = 125f }, new VecScene(), 0));
    }

    /// <summary>C23: `n` is the count the author wrote, and the work budget is the limit.</summary>
    internal static void RepeatCountIsNotTruncated(TestRun run)
    {
        // The old 20,000 cap refused valid input -- 50,000 rects fit the vertex budget -- and
        // `n` reads back into the expressions, so clamping it also moved every instance.
        var fits = Parse("SCENE w=100 h=100\nRP n=50000 { R x=0 y=0 w=1 h=1 }");
        run.Check("C23: n=50000 is 50000", fits.Root[0].RepeatCount == 50000,
            $"{fits.Root[0].RepeatCount}");

        // And the silent failure the cap hid: RoundToInt overflows past 2^31 to int.MinValue,
        // which the clamp then turned into 0 -- an enormous `n` drew NOTHING.
        var huge = Parse("SCENE w=100 h=100\nRP n=3000000000 { R x=0 y=0 w=1 h=1 }");
        run.Check("C23: n past int range does not collapse to 0", huge.Root[0].RepeatCount > 0,
            $"{huge.Root[0].RepeatCount}");

        var nan = Parse("SCENE w=100 h=100\nRP n=nan { R x=0 y=0 w=1 h=1 }");
        run.Check("C23: a NaN count is 0", nan.Root[0].RepeatCount == 0, $"{nan.Root[0].RepeatCount}");

        // A nest of two legal counts was 400,000,000 iterations and 35 s of one rebuild, with
        // the cap in force: a per-node cap is not a work limit. The budget spans the rebuild.
        var nest = Parse("SCENE w=100 h=100 fit=stretch\n"
                         + "RP n=20000 { RP n=20000 { R x=0 y=0 w=1 h=1 } }");
        var stats = new TessellationStats();
        var started = System.Diagnostics.Stopwatch.StartNew();
        Tessellator.Emit(new MeshBuilder(), nest, new EvalContext(), new Rect(0f, 0f, 100f, 100f), 1f,
            true, stats);
        started.Stop();

        run.Check("C23: a nested repeat is stopped by the work budget and says so",
            stats.Starved != null && started.ElapsedMilliseconds < 5000,
            $"{stats.Starved ?? "nothing reported"} after {started.ElapsedMilliseconds} ms");

        // And a nest that fits still draws.
        var fine = Parse("SCENE w=100 h=100 fit=stretch\n"
                         + "RP n=20 { RP n=20 { R x=0 y=0 w=1 h=1 } }");
        var quiet = new TessellationStats();
        Tessellator.Emit(new MeshBuilder(), fine, new EvalContext(), new Rect(0f, 0f, 100f, 100f), 1f,
            true, quiet);
        run.Check("C23: a nest inside the budget is untouched", quiet.Starved == null,
            quiet.Starved ?? "nothing reported");
    }
}
