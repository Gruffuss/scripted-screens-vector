using UnityEngine;

namespace ScriptedScreensVector.Tests;

/// <summary>
/// The conversion from a `sh` entry into the font underlay's normalised padding space.
/// </summary>
/// <remarks>
/// This is the half of the text shadow that fails quietly. A factor out by the sampling size
/// draws the shadow at a tenth of its size, which reads as "the blur is a bit subtle" rather
/// than as a bug, and nothing anywhere would say so. The numbers below are worked by hand
/// from the same relationship the shader uses: one unit of offset is the whole SDF padding,
/// which is `gradientScale` atlas texels, and a texel covers `fontSize / samplingPointSize`
/// rendered units.
/// </remarks>
internal static class TextShadowTests
{
    // A typical face as this project builds them: 48pt sampling, gradient scale 10.
    private const float Gradient = 10f;
    private const float Sampling = 48f;

    private static void Near(TestRun run, string name, float actual, double expected)
    {
        run.Check(name, actual, expected, 0.001d, 0, 0);
    }

    internal static void OffsetScale(TestRun run)
    {
        // At font size 48 the glyph is drawn at its sampling size, so one texel is one unit
        // and the full padding is 10 units. A 2-unit offset is therefore 0.2.
        var fit = TextShadow.Fit(new VecShadow(2f, 0f, 0f, 0f, Color.black), Gradient, Sampling, 48f);
        Near(run, "textshadow: 2 units at size 48", fit.OffsetX, 0.2d);

        // Half the font size, half as many rendered units per texel, so the SAME distance on
        // screen costs twice the normalised budget. Getting this backwards is the likely bug.
        var small = TextShadow.Fit(new VecShadow(2f, 0f, 0f, 0f, Color.black), Gradient, Sampling, 24f);
        Near(run, "textshadow: same offset at half the size", small.OffsetX, 0.4d);
    }

    internal static void YIsFlipped(TestRun run)
    {
        // Scenes are +Y down and the shader is +Y up, so a shadow cast downward -- the normal
        // case, and the one every mockup uses -- must come out negative.
        var fit = TextShadow.Fit(new VecShadow(0f, 3f, 0f, 0f, Color.black), Gradient, Sampling, 48f);
        Near(run, "textshadow: downward shadow flips sign", fit.OffsetY, -0.3d);
    }

    internal static void BlurIsHalved(TestRun run)
    {
        // `blur` is a CSS blur radius and the geometric shadow uses sigma = blur/2. Softness
        // spreads across the same space, so it takes the same halving or a text shadow reads
        // twice as soft as the box behind it.
        var fit = TextShadow.Fit(new VecShadow(0f, 0f, 4f, 0f, Color.black), Gradient, Sampling, 48f);
        Near(run, "textshadow: blur 4 gives softness 0.2", fit.Softness, 0.2d);
    }

    internal static void OversizeIsScaledWhole(TestRun run)
    {
        // 8 + 4 + 4/2 = 14 units against a 10-unit padding: budget 1.4, so everything is
        // scaled by 1/1.4. The point of scaling the WHOLE request is that the shadow keeps
        // its shape -- the ratio of offset to softness must survive.
        var shadow = new VecShadow(8f, 0f, 4f, 4f, Color.black);
        var fit = TextShadow.Fit(shadow, Gradient, Sampling, 48f);

        Near(run, "textshadow: oversize reports its factor", fit.Scale, 1d / 1.4d);
        Near(run, "textshadow: oversize fills the budget exactly",
            Mathf.Abs(fit.OffsetX) + fit.Dilate + fit.Softness, 1d);

        var unscaled = 0.8d / (0.4d + 0.2d);
        Near(run, "textshadow: oversize keeps its shape",
            fit.OffsetX / (fit.Dilate + fit.Softness), unscaled);
    }

    internal static void FitsAreLeftAlone(TestRun run)
    {
        // Anything within the budget must come back untouched, or every ordinary shadow would
        // be quietly shrunk by a scale factor that should not have applied.
        var fit = TextShadow.Fit(new VecShadow(1f, 1f, 2f, 0f, Color.black), Gradient, Sampling, 48f);
        Near(run, "textshadow: a shadow that fits is not scaled", fit.Scale, 1d);
    }

    internal static void NonsenseFontIsSafe(TestRun run)
    {
        // A face reporting no SDF scale must yield nothing rather than a division by zero
        // that poisons the material with NaN -- which paints the label as a black block.
        var fit = TextShadow.Fit(new VecShadow(2f, 2f, 2f, 0f, Color.black), 0f, Sampling, 48f);

        var finite = !float.IsNaN(fit.OffsetX) && !float.IsInfinity(fit.OffsetX)
                     && Mathf.Approximately(fit.OffsetX, 0f);

        run.Check("textshadow: no SDF scale yields zero, not NaN", finite ? 1d : 0d, 1d, 0.001d, 0, 0);
    }

    /// <summary>
    /// An offset shadow too big for the quad moves to a copy; a glow does not.
    /// </summary>
    /// <remarks>
    /// LiberationSans in game: both page warnings imply sampling / gradient scale = 8.65.
    /// A 13-point heading with `2 2 3` fit at 43% in the quad; as a copy only blur and spread
    /// count. A 14-point `0 0 6` glow has no offset to take out and stays at 54%.
    /// </remarks>
    internal static void OffsetShadowsMoveToACopy(TestRun run)
    {
        const float Gradient = 10f;
        const float Sampling = 86.5f;

        var heading = new VecShadow(2f, 2f, 3f, 0f, UnityEngine.Color.black);
        var headingInQuad = TextShadow.Fit(heading, Gradient, Sampling, 13f);
        var headingCast = TextShadow.ShouldCast(heading, Gradient, Sampling, 13f, out var headingFit);

        run.Check("text shadow: the heading case reproduces the 43% from the log",
            UnityEngine.Mathf.Abs(headingInQuad.Scale - 0.43f) < 0.01f, $"{headingInQuad.Scale:P0}");
        run.Check("text shadow: an offset shadow moves to a copy and fits whole",
            headingCast && headingFit.Scale > 0.99f && headingFit.OffsetX == 0f && headingFit.OffsetY == 0f,
            $"cast {headingCast}, scale {headingFit.Scale:P0}");

        var glow = new VecShadow(0f, 0f, 6f, 0f, UnityEngine.Color.cyan);
        var glowCast = TextShadow.ShouldCast(glow, Gradient, Sampling, 14f, out var glowFit);

        run.Check("text shadow: a glow keeps one label and its honest 54%",
            !glowCast && UnityEngine.Mathf.Abs(glowFit.Scale - 0.54f) < 0.01f,
            $"cast {glowCast}, scale {glowFit.Scale:P0}");

        var small = new VecShadow(1f, 1f, 1f, 0f, UnityEngine.Color.black);
        var smallCast = TextShadow.ShouldCast(small, Gradient, Sampling, 14f, out _);

        run.Check("text shadow: one that already fits stays on the label", !smallCast, $"cast {smallCast}");
    }
}
