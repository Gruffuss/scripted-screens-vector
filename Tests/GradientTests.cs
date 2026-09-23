using System;
using UnityEngine;

namespace ScriptedScreensVector.Tests;

/// <summary>
/// Tests for gradient parameterisation and stop interpolation.
/// </summary>
/// <remarks>
/// The area assertions used elsewhere cannot catch a gradient bug: wrong colours still
/// produce perfectly valid geometry, and the triangulator's stall warning stays silent. So
/// these check colour directly, at hand-computed positions.
///
/// The clamping cases matter as much as the interpolating ones. Out-of-range parameters
/// clamping to an end stop is *correct*, and it is what made a mis-placed gradient in
/// `GradientDemo.lua` look like a broken renderer: the shape sat past the end of its own
/// axis and painted one flat colour.
/// </remarks>
internal static class GradientTests
{
    /// <summary>
    /// Parses #rrggbb without <c>ColorUtility</c>.
    /// </summary>
    /// <remarks>
    /// <c>ColorUtility.TryParseHtmlString</c> is a native ECall and throws
    /// <c>SecurityException</c> outside the Unity player, unlike <c>Vector2</c> and
    /// <c>Mathf</c> which are ordinary managed code. Only the hex parsing is unavailable, so
    /// the tests do it themselves and exercise the interpolation, which is the part with
    /// logic worth testing.
    /// </remarks>
    private static Color Hex(string hex)
    {
        var text = hex.TrimStart('#');
        var value = Convert.ToInt64(text, 16);

        if (text.Length <= 6)
        {
            return new Color(
                ((value >> 16) & 0xFF) / 255f,
                ((value >> 8) & 0xFF) / 255f,
                (value & 0xFF) / 255f,
                1f);
        }

        return new Color(
            ((value >> 24) & 0xFF) / 255f,
            ((value >> 16) & 0xFF) / 255f,
            ((value >> 8) & 0xFF) / 255f,
            (value & 0xFF) / 255f);
    }

    private static Gradient Linear(params (float at, string colour)[] stops)
    {
        var gradient = new Gradient
        {
            Radial = false,
            Start = new Vector2(100f, 0f),
            End = new Vector2(200f, 0f),
        };

        foreach (var (at, colour) in stops)
        {
            gradient.Positions.Add(at);
            gradient.Colours.Add(Hex(colour));
        }

        return gradient;
    }

    private static bool ColourMatches(Color actual, string expected, double tolerance = 0.02d)
    {
        var want = Hex(expected);

        return Math.Abs(actual.r - want.r) <= tolerance
               && Math.Abs(actual.g - want.g) <= tolerance
               && Math.Abs(actual.b - want.b) <= tolerance;
    }

    private static string Describe(Color c)
    {
        return $"#{Mathf.RoundToInt(c.r * 255f):X2}{Mathf.RoundToInt(c.g * 255f):X2}{Mathf.RoundToInt(c.b * 255f):X2}";
    }

    private static void Expect(TestRun run, string name, Color actual, string expected)
    {
        if (ColourMatches(actual, expected))
            run.Pass($"{name}  got {Describe(actual)}");
        else
            run.Fail($"{name}: expected {expected}, got {Describe(actual)}");
    }

    /// <summary>Parameter is 0 at the axis start, 1 at its end, and linear between.</summary>
    internal static void LinearParameter(TestRun run)
    {
        var gradient = Linear((0f, "#000000"), (1f, "#FFFFFF"));

        var ok = Math.Abs(gradient.Parameter(new Vector2(100f, 0f))) < 0.001f
                 && Math.Abs(gradient.Parameter(new Vector2(200f, 0f)) - 1f) < 0.001f
                 && Math.Abs(gradient.Parameter(new Vector2(150f, 0f)) - 0.5f) < 0.001f
                 // Off-axis points project onto the axis; y must not matter here.
                 && Math.Abs(gradient.Parameter(new Vector2(150f, 999f)) - 0.5f) < 0.001f;

        if (ok)
            run.Pass("gradient: linear parameter");
        else
            run.Fail($"gradient: linear parameter wrong (start={gradient.Parameter(new Vector2(100f, 0f))}, " +
                     $"mid={gradient.Parameter(new Vector2(150f, 0f))}, end={gradient.Parameter(new Vector2(200f, 0f))})");
    }

    /// <summary>Two stops: the exact case, and the one the demo's top-left rect uses.</summary>
    internal static void TwoStopInterpolation(TestRun run)
    {
        var gradient = Linear((0f, "#000000"), (1f, "#FFFFFF"));

        Expect(run, "gradient: 2-stop at 0.0", gradient.Sample(0f), "#000000");
        Expect(run, "gradient: 2-stop at 0.5", gradient.Sample(0.5f), "#7F7F7F");
        Expect(run, "gradient: 2-stop at 1.0", gradient.Sample(1f), "#FFFFFF");
    }

    /// <summary>
    /// Four stops — the "heat" ramp from the demo. This is the case the user asked about:
    /// do transitions between interior stops actually interpolate?
    /// </summary>
    internal static void MultiStopInterpolation(TestRun run)
    {
        var gradient = Linear(
            (0f, "#2E8B6E"),
            (0.4f, "#5FD9A8"),
            (0.7f, "#F59E0B"),
            (1f, "#E23D3D"));

        // Exactly on each stop.
        Expect(run, "gradient: 4-stop at 0.00", gradient.Sample(0f), "#2E8B6E");
        Expect(run, "gradient: 4-stop at 0.40", gradient.Sample(0.4f), "#5FD9A8");
        Expect(run, "gradient: 4-stop at 0.70", gradient.Sample(0.7f), "#F59E0B");
        Expect(run, "gradient: 4-stop at 1.00", gradient.Sample(1f), "#E23D3D");

        // Halfway between stops 2 and 3: the midpoint of #5FD9A8 and #F59E0B.
        Expect(run, "gradient: 4-stop at 0.55", gradient.Sample(0.55f), "#AABB5A");

        // A quarter of the way from stop 1 to stop 2.
        Expect(run, "gradient: 4-stop at 0.10", gradient.Sample(0.1f), "#3B9C7E");
    }

    /// <summary>
    /// Out-of-range parameters clamp. Correct, and the reason a mis-placed gradient reads
    /// as a flat colour rather than as an error.
    /// </summary>
    internal static void ClampsOutsideRange(TestRun run)
    {
        var gradient = Linear((0f, "#2E8B6E"), (1f, "#E23D3D"));

        Expect(run, "gradient: clamps below 0", gradient.Sample(-3f), "#2E8B6E");
        Expect(run, "gradient: clamps above 1", gradient.Sample(2.28f), "#E23D3D");

        // The exact failure from GradientDemo: axis x 10..90, shape drawn at x 108..192.
        var misplaced = new Gradient { Radial = false, Start = new Vector2(10f, 0f), End = new Vector2(90f, 0f) };
        misplaced.Positions.Add(0f);
        misplaced.Colours.Add(Hex("#2E8B6E"));
        misplaced.Positions.Add(1f);
        misplaced.Colours.Add(Hex("#E23D3D"));

        var atLeftEdge = misplaced.At(new Vector2(108f, 30f));
        var atRightEdge = misplaced.At(new Vector2(192f, 30f));

        if (ColourMatches(atLeftEdge, "#E23D3D") && ColourMatches(atRightEdge, "#E23D3D"))
            run.Pass("gradient: misplaced axis is flat end-stop (the demo bug, reproduced)");
        else
            run.Fail($"gradient: misplaced axis expected flat #E23D3D, got {Describe(atLeftEdge)} .. {Describe(atRightEdge)}");
    }

    /// <summary>
    /// The faceting bug: a sliver spanning a radial gradient must report a large spread.
    /// </summary>
    /// <remarks>
    /// Both ends of such a sliver sit on the rim at nearly the same parameter, so a
    /// corner-only spread reports ~0 and nothing subdivides — which is exactly how the
    /// radial fill rendered as flat facets radiating from the centre. Sampling the interior
    /// is what makes the difference, so this asserts the two cases separately.
    /// </remarks>
    internal static void SliverSpreadSeesInterior(TestRun run)
    {
        var gradient = new Gradient
        {
            Radial = true,
            Start = new Vector2(0f, 0f),
            Focus = new Vector2(0f, 0f),
            Radius = 30f,
        };

        gradient.Positions.Add(0f);
        gradient.Colours.Add(Hex("#FFFFFF"));
        gradient.Positions.Add(1f);
        gradient.Colours.Add(Hex("#000000"));

        // The shape ear clipping actually produces: all three corners on the rim, forming
        // a long thin triangle that reaches across the disc. Every corner is at parameter
        // 1.0, so corner-only spread is exactly zero however much gradient it covers.
        Vector2 Rim(float degrees)
        {
            var r = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * 30f;
        }

        var a = Rim(175f);
        var b = Rim(185f);
        var c = Rim(0f);

        var cornersOnly = Mathf.Max(gradient.Parameter(a), Mathf.Max(gradient.Parameter(b), gradient.Parameter(c)))
                          - Mathf.Min(gradient.Parameter(a), Mathf.Min(gradient.Parameter(b), gradient.Parameter(c)));

        var actual = gradient.SpreadOver(a, b, c);

        // Corner spread is ~0 and would never trip the tolerance; the true spread across
        // the triangle is ~1.0 because an edge midpoint lands on the focus.
        if (cornersOnly < 0.01f && actual > 0.9f)
            run.Pass($"gradient: sliver spread sees interior (corners {cornersOnly:F3} -> actual {actual:F3})");
        else
            run.Fail($"gradient: sliver spread wrong (corners {cornersOnly:F3}, actual {actual:F3}; " +
                     "expected corners < 0.01 and actual > 0.9)");
    }

    /// <summary>A triangle far from the focus should not be subdivided needlessly.</summary>
    internal static void FlatRegionSpreadStaysSmall(TestRun run)
    {
        var gradient = new Gradient
        {
            Radial = true,
            Start = Vector2.zero,
            Focus = Vector2.zero,
            Radius = 30f,
        };

        gradient.Positions.Add(0f);
        gradient.Colours.Add(Hex("#FFFFFF"));
        gradient.Positions.Add(1f);
        gradient.Colours.Add(Hex("#000000"));

        // A small triangle out at the rim: genuinely flat, must not subdivide.
        var spread = gradient.SpreadOver(new Vector2(28f, 0f), new Vector2(29f, 1f), new Vector2(29f, -1f));

        if (spread < 0.06f)
            run.Pass($"gradient: flat region stays coarse (spread {spread:F3})");
        else
            run.Fail($"gradient: flat region spread {spread:F3} would subdivide needlessly");
    }

    /// <summary>
    /// Alpha interpolates like any other channel, and the RGB you fade *to* matters.
    /// </summary>
    /// <remarks>
    /// UGUI composites with straight (non-premultiplied) alpha, so interpolating between two
    /// stops blends RGB and A independently. Fading white to transparent *black* therefore
    /// passes through grey at half alpha — the classic muddy midtone. Fading white to
    /// transparent *white* keeps the hue and only drops alpha, which is almost always what
    /// is wanted. Both are asserted here so the difference is a documented fact rather than
    /// a surprise.
    /// </remarks>
    internal static void AlphaInterpolation(TestRun run)
    {
        var clean = Linear((0f, "#FFFFFFFF"), (1f, "#FFFFFF00"));
        var muddy = Linear((0f, "#FFFFFFFF"), (1f, "#00000000"));

        var cleanMid = clean.Sample(0.5f);
        var muddyMid = muddy.Sample(0.5f);

        // Alpha halves in both cases.
        var alphaOk = Math.Abs(cleanMid.a - 0.5f) < 0.01f && Math.Abs(muddyMid.a - 0.5f) < 0.01f;

        // RGB holds in the first and collapses to grey in the second.
        var cleanRgbOk = cleanMid.r > 0.99f && cleanMid.g > 0.99f && cleanMid.b > 0.99f;
        var muddyRgbOk = Math.Abs(muddyMid.r - 0.5f) < 0.01f;

        if (alphaOk && cleanRgbOk && muddyRgbOk)
        {
            run.Pass($"gradient: alpha interpolates (clean mid {Describe(cleanMid)}@{cleanMid.a:F2}, " +
                     $"muddy mid {Describe(muddyMid)}@{muddyMid.a:F2})");
        }
        else
        {
            run.Fail($"gradient: alpha wrong (clean {Describe(cleanMid)}@{cleanMid.a:F2}, " +
                     $"muddy {Describe(muddyMid)}@{muddyMid.a:F2})");
        }
    }

    /// <summary>Eight-digit hex carries alpha through to the sampled colour.</summary>
    internal static void AlphaFromHexStops(TestRun run)
    {
        var gradient = Linear((0f, "#5FD9A8CC"), (1f, "#2E8B6EFF"));

        var start = gradient.Sample(0f);
        var end = gradient.Sample(1f);

        if (Math.Abs(start.a - 0.8f) < 0.01f && Math.Abs(end.a - 1f) < 0.01f)
            run.Pass($"gradient: alpha from hex stops ({start.a:F2} -> {end.a:F2})");
        else
            run.Fail($"gradient: alpha from hex stops wrong ({start.a:F2} -> {end.a:F2}, expected 0.80 -> 1.00)");
    }

    /// <summary>
    /// A bounding-box gradient spans whatever shape it fills, so it tracks moving geometry.
    /// </summary>
    /// <remarks>
    /// This is the fix for gradients being unusable in live UI: declared coordinates are
    /// static, so a ramp anchored in scene space cannot follow a tank level. In bbox mode the
    /// same declaration covers a short shape and a tall one identically.
    /// </remarks>
    internal static void BoundingBoxGradientTracksShape(TestRun run)
    {
        var gradient = new Gradient
        {
            Radial = false,
            BoundingBox = true,
            Start = new Vector2(0f, 0f),   // 0..1 fractions, not scene units
            End = new Vector2(0f, 1f),
        };

        gradient.Positions.Add(0f);
        gradient.Colours.Add(Hex("#000000"));
        gradient.Positions.Add(1f);
        gradient.Colours.Add(Hex("#FFFFFF"));

        // The same gradient bound to two shapes of very different height.
        var shortShape = new Paint(Color.white, gradient, 1f).WithBounds(new Vector2(0f, 100f), new Vector2(20f, 10f));
        var tallShape = new Paint(Color.white, gradient, 1f).WithBounds(new Vector2(0f, 0f), new Vector2(20f, 200f));

        // Midpoint of each should be mid-grey in both cases.
        var shortMid = shortShape.At(new Vector2(10f, 105f));
        var tallMid = tallShape.At(new Vector2(10f, 100f));

        var ok = Math.Abs(shortMid.r / 255f - 0.5f) < 0.03f
                 && Math.Abs(tallMid.r / 255f - 0.5f) < 0.03f;

        // And the bottom of the short shape should be white, where a scene-space gradient
        // declared 0..1 would have clamped to its end stop everywhere.
        var shortBottom = shortShape.At(new Vector2(10f, 110f));
        ok &= shortBottom.r > 250;

        if (ok)
            run.Pass($"gradient: bbox tracks shape (short mid {shortMid.r}, tall mid {tallMid.r}, short end {shortBottom.r})");
        else
            run.Fail($"gradient: bbox wrong (short mid {shortMid.r}, tall mid {tallMid.r}, short end {shortBottom.r}; expected ~128, ~128, 255)");
    }

    /// <summary>Radial parameter is distance from the focus over the radius.</summary>
    internal static void RadialParameter(TestRun run)
    {
        var gradient = new Gradient
        {
            Radial = true,
            Start = new Vector2(50f, 50f),
            Focus = new Vector2(50f, 50f),
            Radius = 20f,
        };

        gradient.Positions.Add(0f);
        gradient.Colours.Add(Hex("#FFFFFF"));
        gradient.Positions.Add(1f);
        gradient.Colours.Add(Hex("#000000"));

        var ok = Math.Abs(gradient.Parameter(new Vector2(50f, 50f))) < 0.001f
                 && Math.Abs(gradient.Parameter(new Vector2(70f, 50f)) - 1f) < 0.001f
                 && Math.Abs(gradient.Parameter(new Vector2(60f, 50f)) - 0.5f) < 0.001f;

        if (ok)
            run.Pass("gradient: radial parameter");
        else
            run.Fail($"gradient: radial parameter wrong (centre={gradient.Parameter(new Vector2(50f, 50f))}, " +
                     $"edge={gradient.Parameter(new Vector2(70f, 50f))})");
    }

    /// <summary>Stops given out of order are sorted, since sampling walks them in order.</summary>
    internal static void SortsOutOfOrderStops(TestRun run)
    {
        var gradient = Linear((1f, "#FFFFFF"), (0f, "#000000"), (0.5f, "#FF0000"));
        GradientParser.SortStops(gradient);

        Expect(run, "gradient: sorted stops at 0.0", gradient.Sample(0f), "#000000");
        Expect(run, "gradient: sorted stops at 0.5", gradient.Sample(0.5f), "#FF0000");
        Expect(run, "gradient: sorted stops at 1.0", gradient.Sample(1f), "#FFFFFF");
    }
}
