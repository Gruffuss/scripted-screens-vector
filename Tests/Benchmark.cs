using System;
using System.Diagnostics;
using UnityEngine;

namespace ScriptedScreensVector.Tests;

/// <summary>
/// Where the per-quad cost actually goes.
/// </summary>
/// <remarks>
/// The in-game measurement said 7.8 ms for 1,700 animated quads — about 4.6 µs each — and
/// the first guesses at a cause (vertex count, triangulation) were assertions, not
/// measurements. A 3.5x reduction in vertices cannot explain a 50x gap.
///
/// This benchmarks the pieces that run per shape per frame, using the real expressions from
/// <c>StressTest.lua</c>, so the breakdown is evidence rather than intuition.
/// </remarks>
internal static class Benchmark
{
    private const int Warmup = 20_000;
    private const int Iterations = 200_000;

    private static double TimeNs(Action action, int iterations)
    {
        for (var i = 0; i < Warmup; i++)
            action();

        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
            action();

        stopwatch.Stop();
        return stopwatch.Elapsed.TotalMilliseconds * 1_000_000d / iterations;
    }

    internal static void Run()
    {
        Console.WriteLine();
        Console.WriteLine("Per-shape cost breakdown (the mote from StressTest.lua)");
        Console.WriteLine("in-game measurement was ~4600 ns per quad at 1,700 quads");
        Console.WriteLine();

        var context = new EvalContext { Time = 12.34f };
        context.PushRepeat(457f, 1700f);

        // The five attributes a mote actually evaluates every frame.
        var x = Expression.Parse(
            "=mod(hash(i)*200+9*sin(t*(0.4+hash(i+9)*1.1)+hash(i+1)*6.283),200)", 0f);
        var y = Expression.Parse("=mod(hash(i+2)*200-t*(6+hash(i+5)*14),200)", 0f);
        var w = Expression.Parse("=1+step(0.7,hash(i+4))", 0f);
        var h = Expression.Parse("=1+step(0.7,hash(i+4))", 0f);
        var fo = Expression.Parse("=0.35+0.45*tri(t*0.5+hash(i+3))", 0f);

        var perAttribute = TimeNs(() => x.Evaluate(context), Iterations);
        Console.WriteLine($"  one complex expression (x)           {perAttribute,8:F1} ns");

        var allFive = TimeNs(
            () =>
            {
                x.Evaluate(context);
                y.Evaluate(context);
                w.Evaluate(context);
                h.Evaluate(context);
                fo.Evaluate(context);
            },
            Iterations);

        Console.WriteLine($"  all five attributes of one mote      {allFive,8:F1} ns");

        // A literal attribute, for contrast: this is what a static scene pays.
        var constant = Expression.Constant(42f);
        var constantCost = TimeNs(() => constant.Evaluate(context), Iterations);
        Console.WriteLine($"  a constant attribute                 {constantCost,8:F1} ns");

        // The allocation the old outline path did per shape, per frame.
        var allocCost = TimeNs(
            () =>
            {
                var list = new System.Collections.Generic.List<Vector2>(64);
                list.Add(Vector2.zero);
            },
            Iterations);

        Console.WriteLine($"  one List<Vector2>(64) allocation     {allocCost,8:F1} ns");

        // Triangulating a four-point rect: what the fast path now skips.
        var quad = new System.Collections.Generic.List<Vector2>
        {
            new(0f, 0f), new(2f, 0f), new(2f, 2f), new(0f, 2f),
        };

        var triangulateCost = TimeNs(
            () => Triangulator.Triangulate(quad, null, out _, out _),
            Iterations / 4);

        Console.WriteLine($"  ear-clipping a 4-point quad          {triangulateCost,8:F1} ns");

        // --- what hoisting i-invariant subtrees would buy ---
        //
        // hash(i), hash(i+9), hash(i+1) are constant for a given instance: only t varies.
        // The hoisted form replaces each with a per-instance constant, so the frame loop
        // evaluates a much smaller tree. These are the same expressions with those
        // subtrees already collapsed, which is the best case direction 1 could reach.
        Console.WriteLine();
        Console.WriteLine("  --- if i-invariant subtrees were hoisted to per-instance constants ---");

        var hashOnly = Expression.Parse("=hash(i)", 0f);
        Console.WriteLine($"  a single hash() call                 {TimeNs(() => hashOnly.Evaluate(context), Iterations),8:F1} ns");

        var xHoisted = Expression.Parse("=mod(100+9*sin(t*0.9+3.1),200)", 0f);
        var yHoisted = Expression.Parse("=mod(120-t*11,200)", 0f);
        var wHoisted = Expression.Constant(2f);
        var hHoisted = Expression.Constant(2f);
        var foHoisted = Expression.Parse("=0.35+0.45*tri(t*0.5+0.7)", 0f);

        var hoistedX = TimeNs(() => xHoisted.Evaluate(context), Iterations);
        Console.WriteLine($"  hoisted x                            {hoistedX,8:F1} ns   (was {perAttribute:F1})");

        var hoistedAll = TimeNs(
            () =>
            {
                xHoisted.Evaluate(context);
                yHoisted.Evaluate(context);
                wHoisted.Evaluate(context);
                hHoisted.Evaluate(context);
                foHoisted.Evaluate(context);
            },
            Iterations);

        Console.WriteLine($"  hoisted, all five attributes         {hoistedAll,8:F1} ns   (was {allFive:F1})");
        Console.WriteLine($"  saving per shape per frame           {allFive - hoistedAll,8:F1} ns" +
                          $"   ({(allFive - hoistedAll) / allFive * 100d:F0}% of expression cost)");

        // --- is VertexHelper the hidden cost? ---
        //
        // It appends to several parallel lists per vertex (position, colour, four UV
        // channels, normal, tangent). If that dominates, the fix is to stop using it and
        // build a Mesh from preallocated arrays instead -- which is pure C# and needs no
        // shader.
        try
        {
            var vh = new UnityEngine.UI.VertexHelper();
            var quadVerts = TimeNs(
                () =>
                {
                    vh.Clear();
                    for (var i = 0; i < 4; i++)
                        vh.AddVert(Vector3.zero, Color.white, Vector2.zero);

                    vh.AddTriangle(0, 1, 2);
                    vh.AddTriangle(0, 2, 3);
                },
                Iterations / 10);

            Console.WriteLine();
            Console.WriteLine($"  VertexHelper: 4 verts + 2 tris       {quadVerts,8:F1} ns");
            Console.WriteLine($"  VertexHelper: 14 verts (old path)    {quadVerts * 3.5,8:F1} ns  (estimated)");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"  VertexHelper unavailable headless: {ex.GetType().Name}");
        }

        // --- per-shape work that is neither vertices nor expressions ---
        //
        // In-game data says cost is ~2.6 us per shape and barely moves with vertex count or
        // expression depth. So something per-shape dominates. Matrix4x4.lossyScale is the
        // prime suspect: it is a PROPERTY that decomposes the matrix -- three vector
        // magnitudes (a sqrt each) plus a determinant sign -- and the tessellator calls it
        // two or three times per shape while treating it like a field read.
        Console.WriteLine();
        Console.WriteLine("  --- per-shape work that is not vertices or expressions ---");

        // Quaternion.Euler is another native ECall, so build the rotation by hand.
        var matrix = Matrix4x4.identity;
        matrix.m00 = 2.2f; matrix.m01 = -0.47f;
        matrix.m10 = 0.47f; matrix.m11 = 2.2f;
        matrix.m03 = 3f; matrix.m13 = 5f;

        Console.WriteLine("  Matrix4x4.lossyScale        NATIVE ECall, cannot run headless");
        Console.WriteLine("                              (removed from the tessellator; see Frame.Scale)");

        var multiply = TimeNs(
            () =>
            {
                var _ = matrix.MultiplyPoint3x4(Vector3.one);
            },
            Iterations);

        Console.WriteLine($"  Matrix4x4.MultiplyPoint3x4           {multiply,8:F1} ns");

        // What it should cost if the scale were computed once and reused.
        var cached = 0f;
        var cachedRead = TimeNs(
            () =>
            {
                var _ = cached * 1.0001f;
            },
            Iterations);

        Console.WriteLine($"  a cached float instead               {cachedRead,8:F1} ns");

        // --- a YS band's contour, which is what a liquid surface produces ---
        //
        // EmitBand builds a closed contour (top edge forward, bottom edge back) and sends it
        // through FillContour. A wave is non-convex, so it misses the convex fast path and
        // lands in the ear clipper -- which is O(n^3) here, since IsEar rescans every
        // remaining vertex for each candidate.
        Console.WriteLine();
        Console.WriteLine("  --- triangulating a band contour (the GasUI shape) ---");

        foreach (var samples in new[] { 16, 24, 40 })
        {
            var contour = new System.Collections.Generic.List<Vector2>(samples * 2);
            for (var i = 0; i < samples; i++)
            {
                contour.Add(new Vector2(i * 4f, 40f + Mathf.Sin(i * 0.5f) * 6f));
            }

            for (var i = samples - 1; i >= 0; i--)
                contour.Add(new Vector2(i * 4f, 100f));

            var cost = TimeNs(
                () => Triangulator.Triangulate(contour, null, out _, out _),
                Math.Max(200, Iterations / 400));

            Console.WriteLine($"  YS band, {samples,3} samples ({contour.Count,3} pts)   {cost,10:F0} ns" +
                              $"   = {cost / 1000f,7:F2} us per band");
        }

        Console.WriteLine();
        Console.WriteLine($"  expressions + alloc + triangulation  {allFive + allocCost + triangulateCost,8:F1} ns");
        Console.WriteLine("  (remainder of the in-game 4600 ns is VertexHelper.AddVert,");
        Console.WriteLine("   feather normals, matrix transforms, and UGUI mesh upload)");
    }
}
