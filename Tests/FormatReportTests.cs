using System;
using System.Collections.Generic;

namespace ScriptedScreensVector.Tests;

/// <summary>
/// Which `fmt` and which `{$v:spec}` carry a problem, and what each one draws.
/// </summary>
/// <remarks>
/// The reporting rule, as of 0.11.103: a spec is reported only when a NUMBER CANNOT GO THROUGH
/// IT -- `string.Format` refuses it for both the float and the rounded long, or it prints the
/// same characters whatever number it is given AND holds a letter or a `%` with something after
/// it, the only two shapes a conversion can take (printf's `%f`, `%d`, `%x`; .NET's `F2`, `D3`,
/// `X`). A spec of pure punctuation and digits holds no conversion either, but it cannot be a
/// mistyped one, so it is literal text the author asked for and is silent.
///
/// 0.11.100 asked instead whether the output came back EQUAL TO THE SPEC, which reported an
/// empty spec, `%%`, `50%` and `]` -- all four drawing exactly what they drew before it -- and
/// a consumer's correctly rendered widgets came back with the magenta problem border. That is
/// the regression every silent case here pins.
///
/// Both checks go through the same `SceneModel.CheckFormat`, so each spec is listed once and
/// run down both routes: as the node's own `fmt`, and as a placeholder's spec inside a literal.
/// </remarks>
internal static class FormatReportTests
{
    private static VecScene Scene(string attrs) =>
        SceneParser.Parse(SceneText.ToProps(
            "SCENE w=100 h=100\nT x=0 y=0 w=90 h=12 " + attrs + "\n", "fmt")!)!;

    private static string Said(VecScene scene) =>
        scene.Problems.Count == 0 ? "no problems" : string.Join(" | ", scene.Problems);

    private static EvalContext Context()
    {
        var context = new EvalContext();
        context.Scalars["v"] = 1.25f;
        return context;
    }

    private static string Show(string spec) => spec.Length == 0 ? "(empty)" : spec;

    /// <summary>Every spec, down both routes, with the verdict each must reach.</summary>
    private static readonly (string Spec, bool Reported)[] Specs =
    {
        // Literal text, and nothing in it can have been meant as a conversion. Silent.
        ("", false),
        ("%%", false),
        ("50%", false),
        ("]", false),
        ("{{0}}", false),

        // A conversion that is not one: a letter, or a `%` cut short. Reported.
        ("%q", true),
        ("nonsense", true),
        ("N2", true),
        ("%-", true),

        // An escaped brace next to letters: it prints `x{y` and drops the number, so it reads
        // as a mistyped conversion. Down BOTH routes -- 0.11.103's brace count read `{{` as two
        // opens, so `{$v:x{{y}` never closed, stayed literal text and said nothing, while the
        // same spec as a node `fmt` was reported.
        ("x{{y", true),

        // Formats that print the number. Silent.
        ("%.1f", false),
        ("%x", false),
        ("{0:F1}", false),
        ("{0:D3}", false),
        ("{0} kPa", false),
        ("{0,6:0.0}", false),
        ("100%% of %.0f", false),
        ("%.0f%%", false),

        // A well-formed composite format no number has a conversion for: the label draws its
        // `missing` text, which is worth a word.
        ("{0:Z}", true),
    };

    internal static void ReportsOnlyWhatANumberCannotGoThrough(TestRun run)
    {
        foreach (var (spec, reported) in Specs)
        {
            var node = Scene("text=$v fmt=\"" + spec + "\"");
            run.Check($"fmt: node fmt=\"{Show(spec)}\" {(reported ? "reported" : "silent")}",
                (node.Problems.Count > 0) == reported, Said(node));

            var part = Scene("text=\"<{$v:" + spec + "}>\"");
            run.Check($"fmt: placeholder {{$v:{Show(spec)}}} {(reported ? "reported" : "silent")}",
                (part.Problems.Count > 0) == reported, Said(part));
        }
    }

    /// <summary>
    /// A .NET composite format inside a placeholder reaches the formatter whole.
    /// </summary>
    /// <remarks>
    /// The placeholder's closing `}` was taken as the FIRST one, which is inside the spec for
    /// every composite format: `{$v:{0:F1}}` handed `{0:F1` to the formatter, which no number
    /// can take, so the label drew its `missing` text and carried a problem -- while the same
    /// `{0:F1}` as the node's `fmt` printed correctly. One value, one format, two answers.
    /// </remarks>
    internal static void ACompositeFormatSurvivesAPlaceholder(TestRun run)
    {
        foreach (var (spec, expected) in new[]
                 {
                     ("{0:F1}", "<1.2>"),
                     ("{0:D3}", "<001>"),
                     ("{0} kPa", "<1.25 kPa>"),
                     ("{0,6:0.0}", "<   1.3>"),

                     // Braces that are only escapes: balanced, so they close where they should,
                     // and `{{0}}` prints `{0}` exactly as the same spec on a `fmt` does.
                     ("{{0}}", "<{0}>"),
                 })
        {
            var scene = Scene("text=\"<{$v:" + spec + "}>\"");
            var drawn = Tessellator.BindTemplate(scene.Root[0], Context(), scene, 0);
            run.Check($"fmt: {{$v:{spec}}} draws {expected}", drawn == expected, drawn);
        }
    }

    /// <summary>
    /// Braces in a spec read the same in a placeholder as in a node `fmt`.
    /// </summary>
    /// <remarks>
    /// `{{0}}` is a label deliberately printing `{0}`: it holds no letter and no `%`, so it is
    /// literal text and is silent either way. `x{{y` prints `x{y` and drops the number, and its
    /// letters say a conversion was meant, so it is reported either way.
    ///
    /// 0.11.103 counted braces to find a placeholder's closer, which read `{{` as two OPENS: a
    /// placeholder could not hold an escaped brace at all -- `{$v:x{{y}` never terminated, the
    /// whole text stayed literal and nothing was said, while the same spec as a node `fmt` drew
    /// `x{y` and was reported. The SPEC is now read as the composite format it is, so the two
    /// routes answer alike.
    /// </remarks>
    internal static void BraceEscapesInASpec(TestRun run)
    {
        var fmt = Scene("text=$v fmt=\"x{{y\"");
        run.Check("fmt: node fmt=\"x{{y\" is reported", fmt.Problems.Count > 0, Said(fmt));

        var scene = Scene("text=\"<{$v:x{{y}>\"");
        var drawn = Tessellator.BindTemplate(scene.Root[0], Context(), scene, 0);
        run.Check("fmt: {$v:x{{y} draws <x{y>, as the same spec on a fmt does",
            drawn == "<x{y>", drawn);
        run.Check("fmt: and is reported, as the same spec on a fmt is", scene.Problems.Count > 0,
            Said(scene));

        // An escape that is the WHOLE spec: balanced, so it closes, and it holds no letter and
        // no `%`, so it is literal text and stays silent -- `{{0}}` either way.
        var escape = Scene("text=\"<{$v:{{}>\"");
        var one = Tessellator.BindTemplate(escape.Root[0], Context(), escape, 0);
        run.Check("fmt: {$v:{{} draws <{>", one == "<{>", one);
        run.Check("fmt: and an escape with no letter is silent", escape.Problems.Count == 0,
            Said(escape));
    }

    /// <summary>
    /// The braces the composite reading is NARROWED for: every one of these drew a value on
    /// 0.11.103 and still does.
    /// </summary>
    /// <remarks>
    /// Reading the whole placeholder as a composite format -- the obvious fix, and the one that
    /// had to be narrowed three times -- turned each of these into literal text with a problem
    /// border beside it. A `}` immediately after a closer is the common shape: it is a brace the
    /// LABEL wanted, not an escape inside the spec. See `SceneParser.Closing`.
    /// </remarks>
    internal static void ABraceAfterTheCloserIsTheLabelsOwn(TestRun run)
    {
        var rows = new (string Text, string Drawn)[]
        {
            // No spec at all, so there is nothing to escape in.
            ("{$v}}", "1.25}"),
            ("{{$v}}", "{1.25}"),
            ("{{{$v}}}", "{{1.25}}"),
            ("{=1+2}}", "3}"),
            ("{x:{$v}}", "{x:1.25}"),

            // A spec holding no `{{` has no literal `{` to pair a `}}` with, so the first `}`
            // after it closes the placeholder and the next `}` is the label's own.
            ("{$v:%.1f}}", "1.2}"),
            ("a {$v:%.0f}} b {$v}", "a 1} b 1.25"),
            ("{a:{b:{$v:{0:F1}}}}", "{a:{b:1.2}}"),

            // A format item is skipped whole, so its `}` is not half of an escape either.
            ("{$v:{0}}}", "1.25}"),

            // A brace in the NAME is nobody's escape: the name region is counted, as it always
            // was, so this reads the name `v{x}` -- unresolved, `missing` text, no stray `}`.
            ("{$v{x}}", "--"),
        };

        foreach (var row in rows)
        {
            var scene = Scene("text=\"" + row.Text + "\"");
            var drawn = Tessellator.BindTemplate(scene.Root[0], Context(), scene, 0);
            run.Check($"template: {row.Text} draws {row.Drawn}", drawn == row.Drawn, drawn);
            run.Check($"template: {row.Text} is silent", scene.Problems.Count == 0, Said(scene));
        }
    }

    /// <summary>
    /// A placeholder that is never closed is a mistake, and is reported.
    /// </summary>
    /// <remarks>
    /// `text = "a {$press"` drew `a {$press` in silence: the author asked for a value and got
    /// their own text back, which is the fault `{$v:%q}` carries and reports. What it DRAWS is
    /// unchanged -- an unparsed placeholder is still ordinary text -- so no working scene moves
    /// a pixel; only the silence goes.
    ///
    /// A spec whose own braces eat the closer is the same fault: `{$v:{0:F1}` has one `}` and
    /// the format item takes it, so there is nothing left to close the placeholder.
    /// </remarks>
    internal static void AnUnclosedPlaceholderIsReported(TestRun run)
    {
        var scene = Scene("text=\"a {$press\"");
        run.Check("template: an unclosed placeholder is reported", scene.Problems.Count > 0,
            Said(scene));

        var drawn = Tessellator.BindTemplate(scene.Root[0], Context(), scene, 0);
        run.Check("template: and still draws its own text", drawn == "a {$press", drawn);

        var eaten = Scene("text=\"a {$v:{0:F1}\"");
        run.Check("template: a spec whose format item takes the only closer is reported",
            eaten.Problems.Count > 0, Said(eaten));

        var escape = Scene("text=\"a {$v:{{\"");
        run.Check("template: a spec whose escape leaves no closer is reported",
            escape.Problems.Count > 0, Said(escape));

        var computed = Scene("text=\"a {=1+2\"");
        run.Check("template: an unclosed {= is reported too", computed.Problems.Count > 0,
            Said(computed));

        var closed = Scene("text=\"a {$v:%.1f}\"");
        run.Check("template: a closed placeholder is silent", closed.Problems.Count == 0,
            Said(closed));
    }

    /// <summary>
    /// An empty spec is the DEFAULT format, not "print nothing".
    /// </summary>
    /// <remarks>
    /// `fmt = ""` ran the value through an empty composite format, which consumed the number and
    /// printed no characters at all: the label drew nothing and vanished from the console, with
    /// a problem saying only that the spec held no conversion. `{$v:}` drew the empty string in
    /// the middle of its literal the same way. An empty spec now means what an absent one means.
    /// </remarks>
    internal static void AnEmptySpecIsTheDefaultFormat(TestRun run)
    {
        var node = Scene("text=$v fmt=\"\"");
        run.Check("fmt: an empty fmt is no fmt", node.Root[0].TextFormat == null,
            node.Root[0].TextFormat ?? "null");

        var scene = Scene("text=\"<{$v:}>\"");
        var drawn = Tessellator.BindTemplate(scene.Root[0], Context(), scene, 0);
        run.Check("fmt: {$v:} draws the number through the default format", drawn == "<1.25>", drawn);
    }
}
