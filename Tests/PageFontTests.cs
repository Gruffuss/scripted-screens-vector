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
        run.Check("FONT first request", requests.Begin(Inter, 0) && requests.Get(Inter)!.State == "pending", "new link pending");
        run.Check("FONT pending request once", !requests.Begin(Inter, 1000), "same URL remains pending");
        requests.Complete(Inter, new[] { "Inter", "Inter Bold" }, "", 1000);
        run.Check("FONT loaded request once", !requests.Begin(Inter, 2000), "same URL remains loaded");
        run.Check("FONT loaded faces report", requests.Get(Inter)!.Describe() == "loaded: Inter, Inter Bold", requests.Get(Inter)!.Describe());
        run.Check("FONT exact URL records", requests.Begin(Inter.ToLowerInvariant(), 0), "URL case is significant");
        foreach (var token in new[] { "refused", "download-failed", "disabled", "face-limit", "load-failed", "loader-unavailable", "" })
        {
            var retry = new PageFonts();
            retry.Begin(Inter, 0);
            if (token == "refused") retry.Refuse(Inter, "fonts mod not installed", 100);
            else retry.Complete(Inter, Array.Empty<string>(), token, 100);
            var expected = token == "refused" ? "refused" : "failed";
            run.Check($"FONT token state: {token}", retry.Get(Inter)!.State == expected, retry.Get(Inter)!.Describe());
            run.Check($"FONT token retained: {token}", retry.Get(Inter)!.Reason == (token == "refused" ? "fonts mod not installed" : token),
                retry.Get(Inter)!.Describe());
            run.Check($"FONT retry just before 60s: {token}", !retry.Begin(Inter, 159.999), "60s measured from failure, not request");
            run.Check($"FONT retry just after 60s: {token}", retry.Begin(Inter, 160.001)
                && retry.Get(Inter)!.State == "pending" && !retry.Begin(Inter, 300), "one fresh attempt after cooldown");
            retry.Refuse(Inter, "again", 400);
            run.Check($"FONT next failure restarts cooldown: {token}", !retry.Begin(Inter, 459.999), "each failure starts its own interval");
            run.Check($"FONT retry at exactly 60s: {token}", retry.Begin(Inter, 460), "at least 60 seconds");
        }
        var capped = new PageFonts();
        for (var i = 0; i < 48; i++)
        {
            var link = $"https://fonts.googleapis.com/css2?family=Face{i}";
            run.Check($"FONT session link {i + 1} accepted", capped.Begin(link, 0), "within session limit");
            capped.Refuse(link, "refused", 0);
        }
        run.Check("FONT session link 49 refused", !capped.Begin(Inter, 0)
            && capped.Get(Inter) is { State: "refused", Reason: "page font limit reached (48 links per session)" },
            capped.Get(Inter)?.Describe() ?? "missing record");
        run.Check("FONT session limit persists after cooldown", !capped.Begin(Inter, 1000)
            && !capped.Begin(Karla, 1000), "never download additional links");
        run.Check("FONT previous link can retry at limit", capped.Begin("https://fonts.googleapis.com/css2?family=Face0", 60),
            "retries do not consume a distinct link");
        run.Check("FONT unknown link unsettled", !requests.Settled(links), "second link has no request");
        requests.Begin(Karla, 0);
        run.Check("FONT pending link unsettled", !requests.Settled(links)
            && requests.Mismatch(links, "Inter SemiBold", true) == null, "no warning until all links settle");
        requests.Complete(Karla, new[] { "Karla", "Inter" }, "", 0);
        run.Check("FONT all loaded settled", requests.Settled(links), "both complete");
        var mismatch = requests.Mismatch(links, "Inter SemiBold", true);
        run.Check("FONT mismatch lists arrivals", mismatch == "font \"Inter SemiBold\" did not arrive; this page's fonts delivered: Inter, Inter Bold, Karla",
            mismatch ?? "no warning");
        run.Check("FONT registered elsewhere has no mismatch", requests.Mismatch(links, "Barlow", false) == null,
            "only TextLayer's current not-registered warning enables mismatch");
        run.Check("FONT missing family has mismatch", requests.Mismatch(links, "Barlow", true) != null, "fallback warning enables mismatch");
        run.Check("FONT delivered face matches", requests.Mismatch(links, "Inter Bold", true) == null, "regular and style names match exactly");
        run.Check("FONT matching is case sensitive", requests.Mismatch(links, "inter", true) != null, "TMP face names are exact");
        requests.Refuse(Karla, "not installed", 0);
        run.Check("FONT failure also settled", requests.Settled(links), "refusal does not wait for a callback");
        requests.Complete(Inter, Array.Empty<string>(), "disabled", 0);
        run.Check("FONT empty delivery warning", requests.Mismatch(links, "Inter", true) == "font \"Inter\" did not arrive; this page's fonts delivered: none",
            requests.Mismatch(links, "Inter", true) ?? "no warning");
        run.Check("no FONT no warnings", !requests.Settled(Array.Empty<string>())
            && requests.Mismatch(Array.Empty<string>(), "Existing", true) == null && requests.Mismatch(links, null, true) == null,
            "no declarations or no requested face");
    }
}
