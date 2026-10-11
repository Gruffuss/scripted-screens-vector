using System;
using System.Linq;

namespace ScriptedScreensVector.Tests;

internal static class PageFontTests
{
    private const string Inter = "https://fonts.googleapis.com/css2?family=Inter:wght@400;700";
    private const string Karla = "https://fonts.googleapis.com/css2?family=Karla";

    private static VecScene Parse(string body) =>
        SceneParser.Parse(SceneText.ToProps("SCENE w=100 h=100\n" + body, "fonts")!)!;

    internal static void Run(TestRun run)
    {
        var single = Parse($"DEFS {{ FONT src=\"{Inter}\" }}");
        run.Check("FONT parsed", single.FontLinks.SequenceEqual(new[] { Inter }) && single.Problems.Count == 0,
            string.Join(" | ", single.Problems));
        var multiple = Parse($"DEFS {{\nFONT src=\"{Inter}\"\nFONT src=\"{Karla}\"\n}}");
        run.Check("FONT declaration order", multiple.FontLinks.SequenceEqual(new[] { Inter, Karla }),
            string.Join(", ", multiple.FontLinks));
        var dedup = Parse($"DEFS {{\nFONT src=\"{Inter}\"\nFONT src=\"{Inter}\"\nFONT src=\"{Karla}\"\nFONT src=\"{Inter.ToLowerInvariant()}\"\n}}");
        run.Check("FONT exact dedup", dedup.FontLinks.SequenceEqual(new[] { Inter, Karla, Inter.ToLowerInvariant() }),
            string.Join(", ", dedup.FontLinks));
        foreach (var def in new[] { "FONT", "FONT src=\"\"", "FONT src=123" })
        {
            var invalid = Parse($"DEFS {{ {def} }}");
            run.Check($"FONT invalid src: {def}", invalid.FontLinks.Count == 0
                && invalid.Problems.Any(p => p.Contains("FONT: src needs a non-empty string URL", StringComparison.Ordinal)),
                string.Join(" | ", invalid.Problems));
        }
        foreach (var key in new[] { "sorce", "text", "id" })
        {
            var invalid = Parse($"DEFS {{ FONT src=\"{Inter}\" {key}=unexpected }}");
            run.Check($"FONT unknown key: {key}", invalid.Problems.Any(p => p.Contains($"unknown attribute \"{key}\"", StringComparison.Ordinal)),
                string.Join(" | ", invalid.Problems));
        }
        var rootFont = Parse($"FONT src=\"{Inter}\"");
        run.Check("FONT outside DEFS", rootFont.FontLinks.Count == 0 && rootFont.Root.Count == 0
            && rootFont.Problems.Contains("op \"FONT\" is not supported"), string.Join(" | ", rootFont.Problems));
        var plain = Parse("T x=0 y=0 w=50 h=20 text=hello font=Existing");
        run.Check("no FONT unchanged", plain.FontLinks.Count == 0 && plain.Root.Count == 1
            && plain.Root[0].FontFamily == "Existing" && plain.Root[0].TextLiteral == "hello"
            && plain.Problems.Count == 0 && !plain.UsesTime && !plain.LiveDefs, "existing label and scene flags");
        var clip = Parse($"DEFS {{ CP id=box {{\nR x=0 y=0 w=10 h=10\nFONT src=\"{Inter}\"\n}} }}");
        run.Check("FONT counted as nested def", clip.Problems.Any(p => p.Contains("a def belongs in DEFS", StringComparison.Ordinal))
            && clip.FontLinks.Count == 0, string.Join(" | ", clip.Problems));

        var requests = new PageFonts();
        var links = new[] { Inter, Karla };
        run.Check("FONT first request", requests.Begin(Inter) && requests.Get(Inter)!.State == "pending", "new link pending");
        run.Check("FONT pending request once", !requests.Begin(Inter), "same URL remains pending");
        requests.Complete(Inter, new[] { "Inter", "Inter Bold" }, "");
        run.Check("FONT loaded request once", !requests.Begin(Inter), "same URL remains loaded");
        run.Check("FONT loaded faces report", requests.Get(Inter)!.Describe() == "loaded: Inter, Inter Bold", requests.Get(Inter)!.Describe());
        run.Check("FONT exact URL records", requests.Begin(Inter.ToLowerInvariant()), "URL case is significant");
        foreach (var failure in new[] { "refused", "download-failed", "disabled", "empty" })
        {
            var retry = new PageFonts();
            retry.Begin(Inter);
            if (failure == "refused") retry.Refuse(Inter, "fonts mod not installed");
            else retry.Complete(Inter, Array.Empty<string>(), failure == "empty" ? "" : failure);
            var expected = failure == "empty" ? "download-failed" : failure;
            run.Check($"FONT reports {failure}", retry.Get(Inter)!.State == expected, retry.Get(Inter)!.Describe());
            run.Check($"FONT retry after {failure}", retry.Begin(Inter) && retry.Get(Inter)!.State == "pending"
                && !retry.Begin(Inter), "failed link accepts one fresh attempt");
        }
        run.Check("FONT unknown link unsettled", !requests.Settled(links), "second link has no request");
        requests.Begin(Karla);
        run.Check("FONT pending link unsettled", !requests.Settled(links)
            && requests.Mismatch(links, "Inter SemiBold") == null, "no warning until all links settle");
        requests.Complete(Karla, new[] { "Karla", "Inter" }, "");
        run.Check("FONT all loaded settled", requests.Settled(links), "both complete");
        var mismatch = requests.Mismatch(links, "Inter SemiBold");
        run.Check("FONT mismatch lists arrivals", mismatch == "font \"Inter SemiBold\" did not arrive; this page's fonts delivered: Inter, Inter Bold, Karla",
            mismatch ?? "no warning");
        run.Check("FONT delivered face matches", requests.Mismatch(links, "Inter Bold") == null, "regular and style names match exactly");
        run.Check("FONT matching is case sensitive", requests.Mismatch(links, "inter") != null, "TMP face names are exact");
        requests.Refuse(Karla, "not installed");
        run.Check("FONT failure also settled", requests.Settled(links), "refusal does not wait for a callback");
        requests.Complete(Inter, Array.Empty<string>(), "disabled");
        run.Check("FONT empty delivery warning", requests.Mismatch(links, "Inter") == "font \"Inter\" did not arrive; this page's fonts delivered: none",
            requests.Mismatch(links, "Inter") ?? "no warning");
        run.Check("no FONT no warnings", !requests.Settled(Array.Empty<string>())
            && requests.Mismatch(Array.Empty<string>(), "Existing") == null && requests.Mismatch(links, null) == null,
            "no declarations or no requested face");
        run.Check("FONT fallback cannot skip", !PageFonts.FaceResolved("Inter", "LiberationSans")
            && !PageFonts.FaceResolved("Inter", null), "retry unresolved requested face");
        run.Check("FONT resolved can skip", PageFonts.FaceResolved("Inter", "Inter"), "requested asset assigned");
        run.Check("FONT differently cased cannot skip", !PageFonts.FaceResolved("Inter", "inter"), "face comparison is exact");
        run.Check("no FONT skip unchanged", PageFonts.FaceResolved(null, "LiberationSans")
            && PageFonts.FaceResolved("", null), "no requested family preserves skip");
    }
}
