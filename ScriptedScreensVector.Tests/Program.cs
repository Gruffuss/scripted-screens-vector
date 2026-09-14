using System;
using System.Collections.Generic;

namespace ScriptedScreensVector.Tests;

/// <summary>Minimal pass/fail harness. No test framework, so `dotnet run` is the whole story.</summary>
internal sealed class TestRun
{
    private readonly List<string> _failures = new();

    internal bool Check(string name, double actual, double expected, double tolerance, int warnings, int triangles)
    {
        var error = expected == 0d ? Math.Abs(actual) : Math.Abs(actual - expected) / Math.Abs(expected);
        var ok = error <= tolerance && warnings == 0;

        Console.WriteLine(
            $"  {(ok ? "PASS" : "FAIL")}  {name,-28} area={actual,10:F2} expected={expected,10:F2} " +
            $"err={error * 100d,6:F3}%  tris={triangles,4}  warnings={warnings}");

        if (ok)
            return true;

        _failures.Add(warnings > 0
            ? $"{name}: triangulator warned (likely a stall)"
            : $"{name}: area off by {error * 100d:F3}% (got {actual:F2}, expected {expected:F2})");

        return false;
    }

    internal void Pass(string message)
    {
        Console.WriteLine($"  PASS  {message}");
    }

    internal void Fail(string message)
    {
        Console.WriteLine($"  FAIL  {message}");
        _failures.Add(message);
    }

    internal int Report()
    {
        Console.WriteLine();

        if (_failures.Count == 0)
        {
            Console.WriteLine("All triangulator tests passed.");
            return 0;
        }

        Console.WriteLine($"{_failures.Count} failure(s):");
        foreach (var failure in _failures)
            Console.WriteLine($"  - {failure}");

        return 1;
    }
}

internal static class Program
{
    private static int Main()
    {
        Console.WriteLine("Triangulator regression tests");
        Console.WriteLine("(area is the assertion: a clipping stall always loses area)");
        Console.WriteLine();

        var run = new TestRun();

        TriangulatorTests.DuplicatedClosingVertex(run);
        TriangulatorTests.ConcaveStar(run);
        TriangulatorTests.SquareWithSquareHole(run);
        TriangulatorTests.TwoHoles(run);
        TriangulatorTests.ConcentricRing(run);

        Console.WriteLine();
        ClipTests.SubjectInside(run);
        ClipTests.SubjectOutside(run);
        ClipTests.PartialOverlap(run);
        ClipTests.SubjectSwallowsRegion(run);
        ClipTests.ClippedContourTriangulates(run);
        ClipTests.PolylineCut(run);
        ClipTests.PolylineMisses(run);
        ClipTests.DiagonalBoundaryStillClips(run);
        ClipTests.BufferedMatchesAllocating(run);
        ClipTests.ClampInsideRespectsBoundary(run);
        SceneTextTests.MatchesTableForm(run);
        SceneTextTests.DefsAndValueForms(run);
        SceneTextTests.ValueTyping(run);
        SceneTextTests.MalformedIsReported(run);
        SceneTextTests.ScrollContainerParses(run);
        SceneTextTests.ShippedExampleParses(run);
        SceneTextTests.QuotedEscapes(run);

        Console.WriteLine();
        ScrollTests.Clamps(run);
        ScrollTests.ShortContentCannotScroll(run);
        ScrollTests.StepSizes(run);

        Console.WriteLine();
        PrintfTests.NumericConversions(run);
        PrintfTests.KeepsSurroundingText(run);
        PrintfTests.UnreadableSpecsAreLeftAlone(run);
        PrintfTests.RepeatedCallsAgree(run);

        Console.WriteLine();
        TextShadowTests.OffsetScale(run);
        TextShadowTests.YIsFlipped(run);
        TextShadowTests.BlurIsHalved(run);
        TextShadowTests.OversizeIsScaledWhole(run);
        TextShadowTests.FitsAreLeftAlone(run);
        TextShadowTests.NonsenseFontIsSafe(run);

        Console.WriteLine();
        RadialFillTests.RingsFollowSize(run);
        RadialFillTests.StopsCannotOutvoteTheScreen(run);
        RadialFillTests.InnerRingsAreCoarser(run);
        RadialFillTests.TheSavingIsReal(run);
        RadialFillTests.StitchCoversTheAnnulus(run);

        Console.WriteLine();
        GradientTests.LinearParameter(run);
        GradientTests.RadialParameter(run);
        GradientTests.TwoStopInterpolation(run);
        GradientTests.MultiStopInterpolation(run);
        GradientTests.SortsOutOfOrderStops(run);
        GradientTests.ClampsOutsideRange(run);
        GradientTests.SliverSpreadSeesInterior(run);
        GradientTests.FlatRegionSpreadStaysSmall(run);
        GradientTests.AlphaFromHexStops(run);
        GradientTests.AlphaInterpolation(run);
        GradientTests.BoundingBoxGradientTracksShape(run);

        var exit = run.Report();

        // Only when asked: the benchmark takes a few seconds and is not a pass/fail check.
        if (Environment.GetCommandLineArgs().Length > 1 &&
            Array.IndexOf(Environment.GetCommandLineArgs(), "--bench") >= 0)
        {
            Benchmark.Run();
        }

        return exit;
    }
}
