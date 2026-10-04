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
    };

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
