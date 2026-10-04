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
        ("path guard", PathGuard),
        ("scene text guard", TextGuard),
    };

    /// <summary>
    /// Scene text must never fail to terminate. Inside `[ ]` the reader skips space and tab only,
    /// so a newline, `{` or `}` was neither skipped nor recognised and ReadValue returned an empty
    /// token without advancing -- the loop then appended empties until it ran out of memory.
    /// </summary>
    /// <remarks>
    /// Like the path guard, every case here EXHAUSTS MEMORY on an unguarded build rather than
    /// returning a wrong answer, so it cannot be watched to fail inside the suite.
    /// `R p=[1,` newline `2]` is an ordinary thing for an author to write.
    /// </remarks>
    private static void TextGuard(TestRun run)
    {
        const string Head = "SCENE w=10 h=10\n";

        foreach (var (src, what) in new[]
                 {
                     (Head + "R p=[1,\n2]\n", "an array spanning a newline"),
                     (Head + "R p=[1,}]\n", "a brace inside an array"),
                     (Head + "R p=[1,{]\n", "an open brace inside an array"),
                     (Head + "R p=[1,2\n", "an array never closed"),
                 })
        {
            var props = SceneText.ToProps(src, "guard");
            run.Check($"scene text: {what} is refused, not hung",
                props == null && SceneText.Rejected != null,
                SceneText.Rejected ?? "(accepted -- the guard did not fire)");
        }

        // A well-formed array on one line must still parse, or the guard is too eager.
        var ok = SceneText.ToProps(Head + "R p=[1,2,3,4]\n", "guard");
        run.Check("scene text: a one-line array still parses", ok != null, SceneText.Rejected ?? "ok");
    }

    /// <summary>
    /// A malformed `d` must stop, not spin. Two shapes of input consumed nothing per pass and so
    /// looped for ever: a number after `Z`, which re-enters `case 'Z'` through the repeated-command
    /// rule, and any character `Number()` cannot read, which it reports as 0 without advancing.
    /// </summary>
    /// <remarks>
    /// Every case here HANGS on a build without the guard -- the failure is non-termination, not a
    /// wrong answer, so this test cannot be watched to fail in the suite without hanging it. It was
    /// confirmed against the unguarded parser out of process, with a timeout.
    /// </remarks>
    private static void PathGuard(TestRun run)
    {
        foreach (var d in new[]
                 {
                     "M0 0 L10 0 Z 5",      // a number after Z
                     "M0 0 L10 0 )",        // a stray character where a number is expected
                     "M0 0 L10 0 ;",
                     "M0 0 L10 0 :",
                     "M0 0 Z Z 1 2 3",
                 })
        {
            var path = PathData.Parse(d);
            run.Check($"path: \"{d}\" stops and says why",
                path.Rejected != null, path.Rejected ?? "(parsed clean, guard did not fire)");
        }

        // And a well-formed path must still parse with nothing reported, or the guard is too eager.
        var good = PathData.Parse("M0 0 L10 0 L10 10 Z");
        run.Check("path: a valid path is untouched by the guard",
            good.Rejected == null, good.Rejected ?? "ok");
    }

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
