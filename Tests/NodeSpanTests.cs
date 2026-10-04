using UnityEngine;

namespace ScriptedScreensVector.Tests;

/// <summary>
/// `Tessellator.TagNodes` records which node produced which vertices, for editors that need
/// click-to-select over a built mesh. It is off in game, so nothing else would notice it breaking.
/// </summary>
internal static class NodeSpanTests
{
    internal static readonly (string, System.Action<TestRun>)[] All =
    {
        ("node spans", Spans),
        ("ver", Version),
    };

    /// <summary>
    /// `ver` is a plain number a scene can compare, and the way it DEGRADES is the feature: on a
    /// mod that lacks it the expression fails to parse and the attribute falls back to its
    /// default, which for `v` is visible. That is what lets an exported console show an "update
    /// your mod" banner on exactly the versions too old to draw it.
    /// </summary>
    private static void Version(TestRun run)
    {
        var ctx = new EvalContext();

        // `ver` reads the version of the assembly Expression was compiled into. In this suite
        // that is the TEST assembly, not the mod, so its absolute value cannot be asserted here
        // -- and an earlier version of this test passed for the wrong reason because 10000 also
        // satisfied "at least 1162". What is testable is the encoding and the degradation; the
        // mod's own number is read off the running build.
        var ver = Expression.Parse("ver", -1f).Evaluate(ctx);
        var assembly = typeof(Expression).Assembly.GetName().Version!;
        var expected = assembly.Major * 10000f + assembly.Minor * 100f + assembly.Build;
        run.Check("ver: encodes major*10000 + minor*100 + patch of its own assembly",
            Mathf.Approximately(ver, expected), $"{ver}, expected {expected}");

        // The comparison an exported scene actually writes.
        var current = Expression.Parse($"lt(ver,{(int)ver})", 1f).Evaluate(ctx);
        run.Check("ver: a scene built for this version hides its banner", current < 0.5f, $"{current}");

        var newer = Expression.Parse($"lt(ver,{(int)ver + 1})", 1f).Evaluate(ctx);
        run.Check("ver: a scene built for a NEWER version shows its banner", newer > 0.5f, $"{newer}");

        // An unknown variable must fall back to the attribute's default rather than evaluate to
        // zero or fail the scene -- this is the behaviour an old mod exhibits for `ver` itself.
        var unknown = Expression.Parse("lt(nosuchthing,1)", 1f).Evaluate(ctx);
        run.Check("ver: an unknown variable falls back to the attribute default, not 0",
            unknown > 0.5f, $"{unknown}");
    }

    private static VecScene Parse(string src) =>
        SceneParser.Parse(SceneText.ToProps(src, "spans")!)!;

    private static void Spans(TestRun run)
    {
        const string Src = "SCENE w=200 h=100 fit=stretch\n"
                           + "G id=outer {\n"
                           + " IMG id=a x=0 y=0 w=20 h=20 src=test:span\n"
                           + " IMG id=b x=40 y=0 w=20 h=20 src=test:span\n"
                           + "}\n";

        // IMG rather than a filled R: ColorUtility.TryParseHtmlString is a native ECall and
        // throws headless, so no scene parsed in this suite may carry a colour literal.
        ImageCache.Loaded["test:span"] = (8, 8, null);

        // Off by default: nothing is recorded, and that is what the game relies on.
        var quiet = new MeshBuilder();
        Tessellator.Emit(quiet, Parse(Src), new EvalContext(), new Rect(0f, 0f, 200f, 100f), 1f, true, new TessellationStats());
        run.Check("spans: nothing is recorded while TagNodes is off",
            Tessellator.NodeSpans.Count == 0, $"{Tessellator.NodeSpans.Count} span(s)");

        Tessellator.TagNodes = true;
        MeshBuilder mesh;
        try
        {
            mesh = new MeshBuilder();
            Tessellator.Emit(mesh, Parse(Src), new EvalContext(), new Rect(0f, 0f, 200f, 100f), 1f, true, new TessellationStats());
        }
        finally
        {
            Tessellator.TagNodes = false;
        }

        var spans = Tessellator.NodeSpans;
        VectorUtil.Find(spans, "a", out var a);
        VectorUtil.Find(spans, "b", out var b);
        VectorUtil.Find(spans, "outer", out var outer);

        run.Check("spans: every identified node is recorded",
            a.Count > 0 && b.Count > 0, $"a {a.Count}, b {b.Count}");

        // The whole point: a group encloses its children, so the smallest span containing a
        // vertex is the innermost node. If a group reported only its own geometry, a tool would
        // select the group for a click on its child.
        run.Check("spans: a group encloses its children",
            outer.First <= a.First && outer.First + outer.Count >= b.First + b.Count,
            $"outer {outer.First}+{outer.Count}, a {a.First}+{a.Count}, b {b.First}+{b.Count}");

        // Siblings must not overlap, or a point would belong to two leaves at once.
        run.Check("spans: siblings do not overlap",
            a.First + a.Count <= b.First, $"a ends {a.First + a.Count}, b starts {b.First}");

        // And a second rebuild must not accumulate the first one's spans.
        var again = new MeshBuilder();
        Tessellator.TagNodes = true;
        try
        {
            Tessellator.Emit(again, Parse(Src), new EvalContext(), new Rect(0f, 0f, 200f, 100f), 1f, true, new TessellationStats());
        }
        finally
        {
            Tessellator.TagNodes = false;
        }

        run.Check("spans: a rebuild replaces the previous spans rather than appending",
            Tessellator.NodeSpans.Count == spans.Count, $"{Tessellator.NodeSpans.Count} vs {spans.Count}");
    }

    private static class VectorUtil
    {
        internal static void Find(System.Collections.Generic.IReadOnlyList<Tessellator.NodeSpan> spans,
            string id, out Tessellator.NodeSpan found)
        {
            found = default;
            foreach (var span in spans)
            {
                if (string.Equals(span.Node.Id, id, System.StringComparison.Ordinal))
                {
                    found = span;
                    return;
                }
            }
        }
    }
}
