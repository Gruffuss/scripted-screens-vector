using System;

namespace ScriptedScreensVector;

/// <summary>
/// The TextMeshPro <c>_OutlineWidth</c> that draws a CSS <c>-webkit-text-stroke</c> of a given width.
/// </summary>
/// <remarks>
/// Its own file, and free of Unity, so the SUITE owns the arithmetic; the material calls around it
/// cannot run headless.
///
/// TMP's SDF shader draws an outline of <c>_OutlineWidth</c> W reaching
/// <c>W * ScaleRatioA * gradientScale</c> atlas pixels OUTSIDE the glyph edge, and as far again
/// inside. And TMP recomputes <c>ScaleRatioA</c> from W itself, whenever the label's mesh padding
/// is updated: <c>(gradientScale - 1) / (gradientScale * max(1, other + W))</c>, where
/// <c>other</c> is the material's bold weight over 4, plus face dilate, plus outline softness.
/// So past about <c>W = 1 - other</c> the ratio shrinks, and a wider W buys less and less.
///
/// Measured in game 2026-10-11 on a 1:1 viewbox at size 96: <c>ow = 8</c> reached 7-8 px outside
/// the letters on LiberationSans and 6-7 px on Barlow, where CSS puts 4. Up to 0.11.112 the width
/// was <c>ow * pointSize / (gradientScale * size)</c>, which reaches <c>ow * ScaleRatioA</c>
/// outward: 0.9 times the width on LiberationSans, 0.83 on Barlow, against CSS's half. Both
/// readings match that model to within 0.3 px, and the ratio read off the live material at W = 1
/// (0.7579 on LiberationSans, 0.7018 on Pacifico) matches its formula exactly.
/// </remarks>
internal static class TextOutline
{
    /// <summary>
    /// How far outside the glyph edge, in atlas pixels, TMP draws an outline of
    /// <c>_OutlineWidth</c> <paramref name="width"/>. It reaches as far inside as well.
    /// </summary>
    // Assumes the material has TMP's ratios on (no RATIOS_OFF keyword). Both font
    // sources in use measured that way; a font with ratios off would need ratio 1 here.
    internal static float Outward(float width, float gradientScale, float other)
        => width * (gradientScale - 1f) / Math.Max(1f, other + width);

    /// <summary>
    /// The <c>_OutlineWidth</c> for a stroke <paramref name="stroke"/> scene units wide on text
    /// <paramref name="size"/> scene units high, half of it outside the letters as CSS draws it.
    /// <paramref name="drawn"/> is the share of the stroke the font can carry: 1 when it all fits,
    /// less when the font's atlas padding runs out first.
    /// </summary>
    internal static float Width(float stroke, float size, float pointSize, float gradientScale, float other,
        out float drawn)
    {
        drawn = 1f;

        // CSS puts half the stroke outside the edge. In atlas pixels that is:
        var target = stroke * pointSize / (2f * Math.Max(1f, size));
        if (target <= 0f)
            return 0f;

        var spread = gradientScale - 1f;
        float width;
        if (target <= spread * (1f - other))
            width = target / spread;                       // the ratio holds
        else if (target < spread)
            width = target * other / (spread - target);    // the ratio shrinks with W: solved for it
        else
            width = float.PositiveInfinity;

        // The shader's range ends at 1, and with it the widest outline this font's padding allows.
        if (width > 1f)
        {
            width = 1f;
            drawn = Outward(1f, gradientScale, other) / target;
        }

        return width;
    }
}
