namespace ScriptedScreensVector.Tests;

/// <summary>
/// The scale handed to the tessellator when the projection declines to measure one.
/// </summary>
/// <remarks>
/// These exist because the fallback shipped wrong and nothing could have caught it. The camera
/// half — whether the projection resolves at all — needs a near plane and cannot run headless,
/// but the DECISION is pure arithmetic and is where the fault actually lived.
///
/// <see cref="RegressionNotOne"/> is the one that matters: it fails on 0.11.107 and passes on
/// 0.11.108, and it was watched failing before it was trusted.
/// </remarks>
internal static class ScaleChoiceTests
{
    internal static void Run(TestRun run)
    {
        Measured(run);
        CarriesLastKnown(run);
        FirstEverIsOne(run);
        RegressionNotOne(run);
    }

    /// <summary>A measured scale wins, whatever is remembered.</summary>
    private static void Measured(TestRun run)
    {
        var chosen = ScaleChoice.Choose(known: true, measured: 60f, lastKnown: 7f);
        run.Check("scale: a measured scale is used as-is", chosen == 60f, $"{chosen}");

        // And it is not quietly blended with, or clamped by, the remembered one.
        var small = ScaleChoice.Choose(known: true, measured: 0.5f, lastKnown: 900f);
        run.Check("scale: a measured scale is not clamped by the remembered one", small == 0.5f, $"{small}");
    }

    /// <summary>The whole point: an unknown scale carries the last one forward.</summary>
    private static void CarriesLastKnown(TestRun run)
    {
        var chosen = ScaleChoice.Choose(known: false, measured: -1f, lastKnown: 60f);
        run.Check("scale: unknown carries the last resolved scale", chosen == 60f, $"{chosen}");
    }

    /// <summary>Before anything has ever resolved there is nothing to carry, so 1 stands.</summary>
    private static void FirstEverIsOne(TestRun run)
    {
        var never = ScaleChoice.Choose(known: false, measured: -1f, lastKnown: -1f);
        run.Check("scale: nothing ever resolved falls back to 1", never == 1f, $"{never}");

        // Zero is "never resolved" too -- a scale of zero is not a usable one to carry.
        var zero = ScaleChoice.Choose(known: false, measured: -1f, lastKnown: 0f);
        run.Check("scale: a zero last-known is not carried", zero == 1f, $"{zero}");
    }

    /// <summary>
    /// The regression, in one line: an unknown scale on a surface that WAS being drawn must not
    /// collapse to 1.
    /// </summary>
    /// <remarks>
    /// 1 against a real 60 px/unit is a sixtyfold error in every screen-pixel-derived quantity,
    /// which in game was "ALL vectors became blurry when too close" while the text stayed sharp.
    /// Watched failing against the old `known ? measured : 1f` before this was trusted.
    /// </remarks>
    private static void RegressionNotOne(TestRun run)
    {
        var chosen = ScaleChoice.Choose(known: false, measured: 0f, lastKnown: 60f);
        run.Check(
            "scale: REGRESSION -- unknown must not collapse to 1",
            chosen != 1f && chosen == 60f,
            $"got {chosen}, 1 would be a 60x feather on every shape");
    }
}
