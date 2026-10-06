namespace ScriptedScreensVector.Tests;

/// <summary>
/// The printf-to-.NET translation behind a `T` node's <c>fmt</c>.
/// </summary>
/// <remarks>
/// Worth pinning because the failure is quiet: a spec this reads wrongly does not throw, it
/// formats the number differently, and "the readout shows 40.00 instead of 40.0" is the kind
/// of thing nobody reports for a week. The unsupported cases matter as much as the supported
/// ones -- an unrecognised spec must come back unchanged rather than be quietly turned into
/// something else. Unchanged is not rejected, though: with no `{0:` in it the spec skips the
/// fast path, `string.Format` returns a string holding no placeholder as it stands, and the
/// label prints the spec itself where the number should be. The scene reports that
/// (`SceneModel.CheckFormat`: "holds no conversion, so it prints itself instead of the
/// number") for a label a number actually goes through AND a spec that could have been a
/// mistyped conversion -- one holding a letter, or a `%` with something after it. A spec of
/// pure punctuation (`%%`, `50%`, `]`) prints itself too and is deliberate literal text, so
/// it is silent; see `FormatReportTests`. The `missing` text is for a spec .NET itself
/// refuses, `{0:Z}`, and never for these.
/// </remarks>
internal static class PrintfTests
{
    private static void Same(TestRun run, string spec, string expected)
    {
        var actual = Printf.ToNet(spec);
        if (actual == expected)
            run.Pass($"printf: \"{spec}\" -> \"{actual}\"");
        else
            run.Fail($"printf: \"{spec}\" gave \"{actual}\", expected \"{expected}\"");
    }

    internal static void NumericConversions(TestRun run)
    {
        Same(run, "%.1f", "{0:F1}");
        Same(run, "%.0f", "{0:F0}");
        Same(run, "%f", "{0:F2}");        // printf's own default precision
        Same(run, "%d", "{0:F0}");
        Same(run, "%.3e", "{0:E3}");
        Same(run, "%X", "{0:X}");

        // printf's %x is lower case and %X upper; both used to translate to "X".
        Same(run, "%x", "{0:x}");

        // And the one that mattered: .NET's X is an INTEGER format, so handing it a float
        // throws rather than declining. That throw escaped the tessellation worker and took
        // the whole surface with it -- a console blanked by one `{=255:%x}` in one label,
        // with no problem reported and nothing in the log. The part must round to an integer.
        foreach (var (spec, value, expected) in new[]
                 {
                     ("%x", 255f, "ff"),
                     ("%X", 255f, "FF"),
                     ("%x", 4096.7f, "1000"),
                 })
        {
            var part = TextPart.Computed(Expression.Constant(value), spec);
            string printed;
            try
            {
                printed = part.Hex
                    ? ((long)value).ToString(part.Spec, System.Globalization.CultureInfo.InvariantCulture)
                    : value.ToString(part.Spec, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (System.Exception e)
            {
                printed = e.GetType().Name;
            }

            run.Check($"printf: {spec} of {value} prints {expected} and does not throw",
                printed == expected, printed);
        }
    }

    internal static void KeepsSurroundingText(TestRun run)
    {
        // The spec is a whole string, not just the conversion: a unit written inline has to
        // survive, since that is how most people will write one before finding `unit`.
        Same(run, "%.1f kPa", "{0:F1} kPa");
        Same(run, "T %.2f", "T {0:F2}");
    }

    internal static void UnreadableSpecsAreLeftAlone(TestRun run)
    {
        // No conversion at all: a constant label.
        Same(run, "no format here", "no format here");

        // A literal percent sign, which printf spells "%%": not a conversion, and it collapses
        // to the one sign the author asked for. It used to come back as the pair, so a `fmt`
        // of "%%" drew "%%" on the console -- and the guard that declined it fired on the
        // FIRST '%', so a pair in front of a real conversion declined the whole spec too.
        Same(run, "%%", "%");
        Same(run, "100%%", "100%");
        Same(run, "%%%.0f", "%{0:F0}");
        Same(run, "100%% of %.0f", "100% of {0:F0}");
        Same(run, "%%%%", "%%");

        // A conversion this does not support. Returned unchanged, and unchanged is what the
        // label then shows: string.Format accepts a string with no placeholder, so `%s` draws
        // `%s` where the number should be (`%s kPa` with a unit). It is not rejected and never
        // reaches the `missing` text; the problem list says "holds no conversion, so it prints
        // itself instead of the number", because `s` is a letter where a conversion was meant.
        // What this pins is that it is not reinterpreted.
        Same(run, "%s", "%s");

        // Trailing percent with nothing after it must not run off the end of the string.
        Same(run, "100%", "100%");
    }

    internal static void RepeatedCallsAgree(TestRun run)
    {
        // The translation is cached, so the second call takes a different path through the
        // code than the first. They have to agree.
        var first = Printf.ToNet("%.4f");
        var second = Printf.ToNet("%.4f");

        if (first == second && second == "{0:F4}")
            run.Pass($"printf: cached call agrees (\"{second}\")");
        else
            run.Fail($"printf: cached call gave \"{second}\", first gave \"{first}\"");
    }
}
