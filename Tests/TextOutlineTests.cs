using System;

namespace ScriptedScreensVector.Tests;

/// <summary>
/// The outline width handed to TextMeshPro for a label's <c>ow</c>.
/// </summary>
/// <remarks>
/// The material half cannot run headless; the arithmetic can, and it is where the fault lived:
/// up to 0.11.112 an <c>ow = 8</c> outline reached 7-8 px outside the letters where CSS puts 4.
/// <see cref="HalfOutside"/> is the one that matters, and was watched failing on the old formula.
/// The fonts are the two measured in game: LiberationSans (gradient scale 10, 86-point atlas) and
/// Barlow (6, 48); their bold weight is TMP's default 0.75, so <c>other</c> is 0.1875.
/// </remarks>
internal static class TextOutlineTests
{
    private const float Other = 0.1875f;

    internal static void Run(TestRun run)
    {
        HalfOutside(run);
        WhereTheRatioShrinks(run);
        PastThePadding(run);
        NoStroke(run);
    }

    /// <summary>Screen pixels outside the letters, for text <paramref name="size"/> high.</summary>
    private static float Outside(float width, float size, float pointSize, float gradientScale)
        => TextOutline.Outward(width, gradientScale, Other) * size / pointSize;

    /// <summary>CSS -webkit-text-stroke: half the stroke outside the glyph edge.</summary>
    private static void HalfOutside(TestRun run)
    {
        foreach (var (font, pointSize, gradient) in new[] { ("LiberationSans", 86f, 10f), ("Barlow", 48f, 6f) })
        {
            foreach (var (stroke, size) in new[] { (8f, 96f), (2f, 24f), (1f, 12f), (4f, 40f) })
            {
                var width = TextOutline.Width(stroke, size, pointSize, gradient, Other, out var drawn);
                var outside = Outside(width, size, pointSize, gradient);
                run.Check($"outline: {font} ow={stroke} size={size} reaches ow/2 outside",
                    Math.Abs(outside - stroke / 2f) < 0.01f && drawn == 1f,
                    $"{outside:F3} px, want {stroke / 2f:F3} (W={width:F4}, drawn {drawn:F2})");
            }
        }

        // The case measured in game, against the formula it replaced: 7.2 px where CSS puts 4.
        var old = 8f * 86f / (10f * 96f);
        run.Check("outline: the old formula is the one measured at 7.2 px",
            Math.Abs(Outside(old, 96f, 86f, 10f) - 7.2f) < 0.05f, $"{Outside(old, 96f, 86f, 10f):F2} px");
    }

    /// <summary>Past W = 1 - other TMP shrinks its ratio; the inversion must follow it there.</summary>
    private static void WhereTheRatioShrinks(TestRun run)
    {
        // LiberationSans at its own point size, so a scene unit is an atlas pixel. 7.45 atlas px
        // outside sits between where the ratio starts to shrink, spread * (1 - other) = 7.3125,
        // and the padding's limit, 9 / 1.1875 = 7.579.
        const float pointSize = 86f, gradient = 10f, size = 86f;
        var stroke = 2f * 7.45f;
        var width = TextOutline.Width(stroke, size, pointSize, gradient, Other, out var drawn);
        var outside = TextOutline.Outward(width, gradient, Other);
        run.Check("outline: past the ratio's knee the width still reaches ow/2",
            width > 1f - Other && width <= 1f && Math.Abs(outside - 7.45f) < 0.001f && drawn == 1f,
            $"W={width:F4}, {outside:F4} atlas px, drawn {drawn:F3}");
    }

    /// <summary>Wider than the padding can carry: capped at 1 and the shortfall reported.</summary>
    private static void PastThePadding(TestRun run)
    {
        var width = TextOutline.Width(40f, 24f, 86f, 10f, Other, out var drawn);
        var limit = TextOutline.Outward(1f, 10f, Other);
        var asked = 40f * 86f / (2f * 24f);
        run.Check("outline: past the padding the width is capped at 1",
            width == 1f, $"W={width}");
        run.Check("outline: the share drawn is what the cap reaches over what was asked",
            Math.Abs(drawn - limit / asked) < 1e-4f && drawn < 1f, $"drawn {drawn:F4}, want {limit / asked:F4}");
    }

    /// <summary>No stroke, no outline.</summary>
    private static void NoStroke(TestRun run)
    {
        var width = TextOutline.Width(0f, 24f, 86f, 10f, Other, out var drawn);
        run.Check("outline: ow=0 is width 0", width == 0f && drawn == 1f, $"W={width}, drawn {drawn}");
    }
}
