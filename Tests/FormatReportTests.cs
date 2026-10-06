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
    /// Braces in a spec, and the one place the two routes genuinely differ.
    /// </summary>
    /// <remarks>
    /// `{{0}}` is a label deliberately printing `{0}`: it holds no letter and no `%`, so it is
    /// literal text and is silent either way. `x{{y` prints `x{y` and drops the number, and its
    /// letters say a conversion was meant, so as a `fmt` it is reported -- but a PLACEHOLDER
    /// cannot hold it at all: the braces do not balance, so no `}` closes the placeholder and the
    /// whole text stays literal, exactly as an unclosed `{$v` does. There is no spec there to
    /// check, and nothing drawn is wrong, so nothing is said.
    /// </remarks>
    internal static void BraceEscapesInASpec(TestRun run)
    {
        var fmt = Scene("text=$v fmt=\"x{{y\"");
        run.Check("fmt: node fmt=\"x{{y\" is reported", fmt.Problems.Count > 0, Said(fmt));

        var scene = Scene("text=\"<{$v:x{{y}>\"");
        var drawn = Tessellator.BindTemplate(scene.Root[0], Context(), scene, 0);
        run.Check("fmt: {$v:x{{y} never parses, so the text stays literal",
            drawn == "<{$v:x{{y}>", drawn);
        run.Check("fmt: and an unparsed placeholder is not reported", scene.Problems.Count == 0,
            Said(scene));
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
