using UnityEngine;
using SS = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem;

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
        ("root style", RootStyle),
        ("closed spline", ClosedSpline),
        ("hits on every shape", ShapeHits),
        ("v on a leaf", LeafVisibility),
        ("recursion caps", RecursionCaps),
        ("open shapes are clickable on the stroke", OpenShapeHitArea),
        ("colour expressions", ColourExpressions),
    };

    /// <summary>
    /// `f = "=if(hover,#A,#B)"` and `"=mix(#A,#B,t)"`. A colour is not a float, so colours are
    /// not a type in the evaluator: they exist only where a colour is asked for, read by
    /// EvaluateColour off the same tree.
    /// </summary>
    /// <remarks>
    /// The literal must become a Color at PARSE time. Unity's colour parser is a native ECall
    /// and tessellation runs on a worker, where an ECall throws -- which is why this test can
    /// only check the parse side here, in a suite that has no colour parser at all. The
    /// headless run therefore proves the opposite thing from the in-game one: that nothing
    /// evaluates a colour STRING.
    /// </remarks>
    private static void ColourExpressions(TestRun run)
    {
        var ctx = new EvalContext();

        // An expression of numbers is not a colour, and must not claim to be.
        run.Check("colour expr: a numeric expression is not a colour",
            !Expression.Parse("=1+2", 0f).IsColour, "claimed to be a colour");

        // `mix` and `if` between colours are, and `if` between numbers is not.
        run.Check("colour expr: if between numbers is not a colour",
            !Expression.Parse("=if(t,1,2)", 0f).IsColour, "claimed to be a colour");

        // Anything with a `#` in it cannot be parsed in THIS suite at all: the literal is
        // turned into a Color at parse time by Unity's parser, which is a native ECall and
        // throws headless. That is the point of the design -- no colour string survives into
        // evaluation, so nothing parses one on the tessellation worker. The literal cases live
        // in the offline probe, which swaps a managed parser in.
    }

    /// <summary>
    /// An open shape has no interior, but its hit outline was the path's own points, which
    /// close implicitly for the point-in-polygon test. A three-sided box therefore answered
    /// clicks in the empty middle it had never drawn -- seen in game as soon as `L` became
    /// clickable at all in 0.11.74.
    /// </summary>
    private static void OpenShapeHitArea(TestRun run)
    {
        // Three sides of a 40x30 box: across the top, down the right, back along the bottom.
        // The middle is empty, the stroke is 6 wide. The stroke colour is a `$` binding rather
        // than a literal: a colour literal goes through a native ECall that throws headless.
        const string Open = "SCENE w=100 h=100 fit=stretch\n"
                            + "L id=k p=[10,10,50,10,50,40,10,40] sw=6 s=$ink press=1\n";

        var hit = Hits(Open);
        run.Check("open hit: the shape still registers one region", hit.Length == 1, $"{hit.Length}");

        if (hit.Length != 1)
            return;

        // Points are given as fractions of the region's own bounding box, because RecordHit
        // stores CANVAS coordinates: scene space is +Y down and the canvas is +Y up, so a
        // scene-space point tests the wrong place and reads as a miss either way.
        run.Check("open hit: a point ON the stroke hits", Inside(hit[0], 0.95f, 0.5f), "miss");

        // The empty middle, far from every edge, must now miss.
        run.Check("open hit: the empty middle does NOT hit", !Inside(hit[0], 0.5f, 0.5f), "hit");

        // A CLOSED shape keeps its interior: that is the region it fills, and scenes rely on it.
        const string Closed = "SCENE w=100 h=100 fit=stretch\n"
                              + "Y id=k p=[10,10,50,10,50,40,10,40] sw=6 s=$ink press=1\n";
        var shut = Hits(Closed);
        run.Check("open hit: a CLOSED shape still answers in its middle",
            shut.Length == 1 && Inside(shut[0], 0.5f, 0.5f), "miss");
    }

    private static HitRegion[] Hits(string src)
    {
        var found = new System.Collections.Generic.List<HitRegion>();
        Tessellator.HitsFound = found;
        try
        {
            Tessellator.Emit(new MeshBuilder(), Parse(src), new EvalContext(),
                new Rect(0f, 0f, 100f, 100f), 1f, true, new TessellationStats());
        }
        finally
        {
            Tessellator.HitsFound = null;
        }

        return found.ToArray();
    }

    /// <summary>
    /// Point in polygon, the same crossing rule the hit test uses, at a fraction of the
    /// region's bounding box so the caller needs to know nothing about canvas orientation.
    /// </summary>
    private static bool Inside(HitRegion region, float fx, float fy)
    {
        var x = region.Rect.xMin + fx * region.Rect.width;
        var y = region.Rect.yMin + fy * region.Rect.height;
        var poly = region.Outline;
        if (poly == null || poly.Length < 3)
            return false;

        var inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if (poly[i].y > y != poly[j].y > y
                && x < (poly[j].x - poly[i].x) * (y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>
    /// Three inputs that recursed without a limit. Unlike the parser hangs, these end the
    /// PROCESS: a .NET stack overflow cannot be caught, so in game it takes Stationeers down
    /// rather than stalling a frame. A symbol that uses itself is an ordinary thing to write
    /// by mistake, which is what makes it worth a guard rather than a note.
    /// </summary>
    /// <remarks>
    /// Like the other guards here, every case KILLS an unguarded build instead of failing, so
    /// this cannot be watched to fail inside the suite. It was confirmed out of process against
    /// the unguarded parser: "Stack overflow.", exit 127, for the symbol cases.
    /// </remarks>
    private static void RecursionCaps(TestRun run)
    {
        // A symbol that uses itself, and two that use each other. Declared in `defs`, which
        // is the only place `SYM` lives -- the text format has no syntax for it, so a
        // text-form version of this test reports "op SYM is not supported" and proves nothing.
        static SS.UiValue Use(string reference) => Map(Prop("op", Str("USE")), Prop("ref", Str(reference)));

        static VecScene Build(SS.UiValue defs, SS.UiValue root) => SceneParser.Parse(new[]
        {
            Prop("scene", Str("cycles")), Prop("w", Num(100f)), Prop("h", Num(100f)),
            Prop("defs", defs), Prop("root", root),
        })!;

        var self = Build(Arr(Map(Prop("op", Str("SYM")), Prop("id", Str("a")), Prop("c", Arr(Use("a"))))),
            Arr(Use("a")));
        run.Check("recursion: a symbol that uses itself is refused, not fatal",
            self.Problems.Count == 1 && self.Problems[0].Contains("uses itself"),
            self.Problems.Count > 0 ? self.Problems[0] : "(no problem reported)");

        var cycle = Build(Arr(
                Map(Prop("op", Str("SYM")), Prop("id", Str("a")), Prop("c", Arr(Use("b")))),
                Map(Prop("op", Str("SYM")), Prop("id", Str("b")), Prop("c", Arr(Use("a"))))),
            Arr(Use("a")));
        run.Check("recursion: a cycle of symbols is refused and names the chain",
            cycle.Problems.Count == 1 && cycle.Problems[0].Contains("a -> b -> a"),
            cycle.Problems.Count > 0 ? cycle.Problems[0] : "(no problem reported)");

        // The guard must clear between uses, or the same symbol twice is wrongly refused.
        var body = Arr(Map(Prop("op", Str("IMG")), Prop("x", Num(0f)), Prop("y", Num(0f)),
            Prop("w", Num(10f)), Prop("h", Num(10f)), Prop("src", Str("test:span"))));
        ImageCache.Loaded["test:span"] = (8, 8, null);

        var twice = Build(Arr(Map(Prop("op", Str("SYM")), Prop("id", Str("a")), Prop("c", body))),
            Arr(Use("a"), Use("a")));
        run.Check("recursion: the same symbol used twice is not a cycle",
            twice.Problems.Count == 0 && twice.Root.Count == 2,
            $"{twice.Root.Count} root node(s), {twice.Problems.Count} problem(s)");

        // The same cycle written in SCENE TEXT, which is the form an author writes. `SYM`
        // lives inside a `DEFS` block: at root level it is an unsupported op, so a harness
        // that puts it there never reaches the expander and passes for the wrong reason.
        var textCycle = Parse("SCENE w=100 h=100\nDEFS {\n SYM id=a {\n  USE ref=b\n }\n"
                              + " SYM id=b {\n  USE ref=a\n }\n}\nUSE ref=a\n");
        run.Check("recursion: a text-form symbol cycle inside DEFS is refused too",
            textCycle.Problems.Count == 1 && textCycle.Problems[0].Contains("a -> b -> a"),
            textCycle.Problems.Count > 0 ? textCycle.Problems[0] : "(no problem reported)");

        // Arrays nested past the cap, which recursed through ReadValue.
        var deep = SceneText.ToProps("SCENE w=10 h=10\nR p=" + new string('[', 2000) + "\n", "deep");
        run.Check("recursion: arrays nested too deep are refused",
            deep == null && SceneText.Rejected != null && SceneText.Rejected.Contains("nest"),
            SceneText.Rejected ?? "(accepted)");

        // A normally nested array must still parse, or the cap is too tight.
        run.Check("recursion: an ordinary array still parses",
            SceneText.ToProps("SCENE w=10 h=10\nR p=[1,2,3,4]\n", "deep") != null,
            SceneText.Rejected ?? "ok");

        // And the expression parser's own parenthesis depth.
        var nested = Expression.Parse("=" + new string('(', 500) + "1", 42f);
        run.Check("recursion: an over-nested expression falls back to its default",
            Mathf.Approximately(nested.Evaluate(new EvalContext()), 42f),
            $"{nested.Evaluate(new EvalContext())}");

        var fine = Expression.Parse("=((((1+2))))", 0f);
        run.Check("recursion: ordinary parentheses still evaluate",
            Mathf.Approximately(fine.Evaluate(new EvalContext()), 3f),
            $"{fine.Evaluate(new EvalContext())}");

        // `(` was not the only recursion. A run of prefix `-` descends once per sign, and a
        // chain of `^` once per operator; 0.11.77 counted neither, and both killed the process
        // at 100,000 (measured out of process: "Stack overflow.", exit 127). They share one
        // depth counter now.
        //
        // The bare form matters most: a value needs no leading `=` to be parsed as an
        // expression, so these arrive through ANY numeric attribute, not only `=`-prefixed ones.
        foreach (var (what, text) in new[]
                 {
                     ("a run of prefix -", "=" + new string('-', 500) + "1"),
                     ("a chain of ^", "=" + string.Join("^", new string('1', 500).ToCharArray())),
                     ("the same with no leading =", new string('-', 500) + "1"),
                 })
        {
            var guarded = Expression.Parse(text, 42f);
            run.Check($"recursion: {what} falls back to the attribute default",
                Mathf.Approximately(guarded.Evaluate(new EvalContext()), 42f),
                $"{guarded.Evaluate(new EvalContext())}");
        }

        // And short runs of both must still evaluate, or the cap is too tight.
        run.Check("recursion: ---1 is still -1",
            Mathf.Approximately(Expression.Parse("=---1", 0f).Evaluate(new EvalContext()), -1f),
            $"{Expression.Parse("=---1", 0f).Evaluate(new EvalContext())}");
        run.Check("recursion: 2^3^2 is still right-associative (512)",
            Mathf.Approximately(Expression.Parse("=2^3^2", 0f).Evaluate(new EvalContext()), 512f),
            $"{Expression.Parse("=2^3^2", 0f).Evaluate(new EvalContext())}");
    }

    /// <summary>
    /// `v = 0` was checked in the group case only, so on a shape or a label it parsed, was
    /// accepted as a known attribute, and then did nothing: the node drew as though the
    /// attribute had never been written.
    /// </summary>
    private static void LeafVisibility(TestRun run)
    {
        static int Verts(string src)
        {
            var mesh = new MeshBuilder();
            Tessellator.Emit(mesh, Parse("SCENE w=100 h=100 fit=stretch\n" + src), new EvalContext(),
                new Rect(0f, 0f, 100f, 100f), 1f, true, new TessellationStats());
            return mesh.currentVertCount;
        }

        // An IMG, not a filled R: a colour literal cannot be parsed in this suite, and an `R`
        // with no fill draws nothing either way, which would make the v=0 case pass for the
        // wrong reason -- as the first version of this test did.
        ImageCache.Loaded["test:span"] = (8, 8, null);

        // Every scene here keeps one always-visible node, so none of the three is empty: an
        // empty scene reports a problem and draws a border, which an earlier version of this
        // test measured instead of the node under test.
        const string Keep = "IMG x=20 y=0 w=10 h=10 src=test:span\n";
        var absent = Verts(Keep);
        var hidden = Verts(Keep + "IMG x=0 y=0 w=10 h=10 src=test:span v=0\n");
        var shown = Verts(Keep + "IMG x=0 y=0 w=10 h=10 src=test:span v=1\n");

        run.Check("v: a shape with v=0 draws exactly what an absent one draws",
            hidden == absent, $"{hidden} verts vs {absent} with no second node at all");
        run.Check("v: the same shape with v=1 draws more", shown > absent,
            $"{shown} verts vs {absent}");

        // And the hit region goes with it, as it already did for a group.
        var hits = new System.Collections.Generic.List<HitRegion>();
        Tessellator.HitsFound = hits;
        try
        {
            Tessellator.Emit(new MeshBuilder(),
                Parse("SCENE w=100 h=100 fit=stretch\nIMG id=k x=0 y=0 w=10 h=10 src=test:span press=1 v=0\n"),
                new EvalContext(), new Rect(0f, 0f, 100f, 100f), 1f, true, new TessellationStats());
        }
        finally
        {
            Tessellator.HitsFound = null;
        }

        run.Check("v: a hidden shape registers no hit", hits.Count == 0, $"{hits.Count} region(s)");
    }

    /// <summary>
    /// A `style` on the scene root was simply never read, so the defaults an author wrote in
    /// the one obvious place did nothing. `fit` is the exception and must stay one: on the root
    /// it means how the viewBox meets the surface.
    /// </summary>
    private static void RootStyle(TestRun run)
    {
        // `size` on a `T`, because it is read unconditionally: `sw` is only read when the node
        // has a stroke to apply it to, so an inherited `sw` on an unstroked shape proves nothing.
        static float Size(VecNode node) => node.TextSize.Evaluate(new EvalContext());

        // A bare paint attribute on the root, which is the only form scene text has: `style`
        // is a map and the text format has no map syntax.
        var bare = Parse("SCENE w=100 h=100 size=5\n T x=0 y=0 w=10 h=10 text=hi\n");
        run.Check("root style: a bare paint attribute on the root is inherited",
            Mathf.Approximately(Size(bare.Root[0]), 5f), $"size {Size(bare.Root[0])}");

        var own = Parse("SCENE w=100 h=100 size=5\n T x=0 y=0 w=10 h=10 text=hi size=2\n");
        run.Check("root style: a node's own value still wins",
            Mathf.Approximately(Size(own.Root[0]), 2f), $"size {Size(own.Root[0])}");

        // The table form's explicit `style` map, built as ScriptedScreens delivers it.
        var table = SceneParser.Parse(new[]
        {
            Prop("scene", Str("root")),
            Prop("w", Num(100f)), Prop("h", Num(100f)),
            Prop("style", Map(Prop("size", Num(7f)))),
            Prop("root", Arr(Map(Prop("op", Str("T")), Prop("text", Str("hi")),
                Prop("x", Num(0f)), Prop("y", Num(0f)), Prop("w", Num(10f)), Prop("h", Num(10f))))),
        })!;
        run.Check("root style: an explicit `style` map on the root is inherited",
            Mathf.Approximately(Size(table.Root[0]), 7f), $"size {Size(table.Root[0])}");

        // `fit` is the exclusion: on the root it means how the viewBox meets the surface, and
        // inherited it would reach both a `T` (shrink-to-fit) and an `IMG` (how a picture fills).
        var fit = Parse("SCENE w=100 h=100 fit=shrink\n T x=0 y=0 w=10 h=10 text=hi\n");
        run.Check("root style: the root's own `fit` is not inherited as a text default",
            fit.Root[0].Fit == TextFit.None, $"fit {fit.Root[0].Fit}");

        // And a text `fit` default on a `G` must still work, or the exclusion went too far.
        var viaGroup = Parse("SCENE w=100 h=100\n G fit=shrink {\n T x=0 y=0 w=10 h=10 text=hi\n}\n");
        run.Check("root style: a text `fit` default on a G still reaches its child",
            viaGroup.Root[0].Children[0].Fit == TextFit.Shrink,
            $"fit {viaGroup.Root[0].Children[0].Fit}");
    }

    /// <summary>
    /// `SP close=1` wraps the curve through the last span back to the first point, which is
    /// what lets it be filled. Open and closed must differ in exactly that span, and the ring
    /// must not repeat its first point (a filler and a stroker each close it their own way).
    /// </summary>
    private static void ClosedSpline(TestRun run)
    {
        var square = new[]
        {
            new Vector2(0f, 0f), new Vector2(10f, 0f),
            new Vector2(10f, 10f), new Vector2(0f, 10f),
        };

        var open = Stroke.Spline(square, 8);
        var ring = Stroke.Spline(square, 8, closed: true);

        run.Check("spline: closing adds exactly one span",
            ring.Count == open.Count - 1 + 8, $"open {open.Count}, ring {ring.Count}");

        run.Check("spline: the ring does not repeat its first point",
            (ring[^1] - ring[0]).magnitude > 0.01f, $"{ring[0]} .. {ring[^1]}");

        // The wrap is the point: the closed ring must enclose area the open curve does not.
        run.Check("spline: the ring encloses the author's square",
            Mathf.Abs(Triangulator.SignedArea(ring)) > 90f,
            $"area {Mathf.Abs(Triangulator.SignedArea(ring)):0.0}");

        // Tangents are taken around the ring, so no vertex sits where the open curve's
        // clamped end tangents put it. Checking the corner nearest the seam is enough.
        var seam = ring[0];
        run.Check("spline: the seam is a curve point, not the raw corner",
            (seam - square[0]).magnitude < 3f, $"{seam}");

        // Two points cannot make a ring, and must not be mangled into one.
        var line = Stroke.Spline(new[] { new Vector2(0f, 0f), new Vector2(10f, 0f) }, 4, closed: true);
        run.Check("spline: two points stay an open curve",
            (line[^1] - new Vector2(10f, 0f)).magnitude < 0.01f, $"{line[^1]}");
    }

    /// <summary>
    /// A hit region was recorded only on the path a rectangle and an ellipse take, so `press`,
    /// `xy` and `drag` on a polygon, a polyline, a spline or a `P` did nothing at all -- and
    /// said nothing either, which is the part that makes it a bug rather than a limit.
    /// </summary>
    private static void ShapeHits(TestRun run)
    {
        foreach (var (src, what) in new[]
                 {
                     ("R id=k x=0 y=0 w=10 h=10 press=1\n", "a rectangle"),
                     ("C id=k cx=5 cy=5 rx=5 press=1\n", "an ellipse"),
                     ("Y id=k p=[0,0,10,0,10,10] press=1\n", "a polygon"),
                     ("L id=k p=[0,0,10,0,10,10] press=1\n", "a polyline"),
                     ("SP id=k p=[0,0,10,0,10,10,0,10] close=1 press=1\n", "a closed spline"),
                     ("P id=k d=\"M0 0 L10 0 L10 10 Z\" press=1\n", "a path"),
                 })
        {
            var hits = new System.Collections.Generic.List<HitRegion>();
            Tessellator.HitsFound = hits;
            try
            {
                Tessellator.Emit(new MeshBuilder(), Parse("SCENE w=100 h=100 fit=stretch\n" + src),
                    new EvalContext(), new Rect(0f, 0f, 100f, 100f), 1f, true, new TessellationStats());
            }
            finally
            {
                Tessellator.HitsFound = null;
            }

            var mine = 0;
            foreach (var hit in hits)
                if (string.Equals(hit.Id, "k", System.StringComparison.Ordinal))
                    mine++;

            // Exactly one: a shape registered twice fires its press twice.
            run.Check($"hits: {what} registers exactly one region", mine == 1, $"{mine} region(s)");
        }
    }

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

    // The table form by hand, for the cases scene text cannot express (it has no map syntax).
    private static SS.UiProp Prop(string key, SS.UiValue value) => new() { Key = key, Value = value };
    private static SS.UiValue Num(float n) => new() { Type = SS.UiValueType.Number, Number = n };
    private static SS.UiValue Str(string s) => new() { Type = SS.UiValueType.String, String = s };
    private static SS.UiValue Arr(params SS.UiValue[] items) => new() { Type = SS.UiValueType.Array, Array = items };
    private static SS.UiValue Map(params SS.UiProp[] props) => new() { Type = SS.UiValueType.Map, Map = props };

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
