using System.Collections.Generic;
using UnityEngine;

namespace ScriptedScreensVector.Tests;

/// <summary>
/// The 2026-09-15 additions whose maths can be pinned without the player: conic angles,
/// colour filters, the group matrix, convex partition, image fit, forced scroll, and the
/// inset shadow's alpha field.
/// </summary>
/// <remarks>
/// Rendering checks (inset shadows against the CSS formula, seams in concave clips, the
/// radial mask) need the scene parser, and so a stand-in for Unity's native colour parser;
/// they live in an offline probe. What is here is the arithmetic those renders depend on.
/// </remarks>
internal static class RequirementTests
{
    internal static void ConicAngles(TestRun run)
    {
        var g = new Gradient { Conic = true, Start = new Vector2(50f, 50f), Angle = 0f };

        // Scene +Y is down: straight up is twelve o'clock, +X is three.
        run.Check("conic: 12 o'clock is 0", Mathf.Abs(g.Parameter(new Vector2(50f, 10f))) < 0.001f, $"{g.Parameter(new Vector2(50f, 10f))}");
        run.Check("conic: 3 o'clock is a quarter", Mathf.Abs(g.Parameter(new Vector2(90f, 50f)) - 0.25f) < 0.001f, $"{g.Parameter(new Vector2(90f, 50f))}");
        run.Check("conic: 9 o'clock is three quarters", Mathf.Abs(g.Parameter(new Vector2(10f, 50f)) - 0.75f) < 0.001f, $"{g.Parameter(new Vector2(10f, 50f))}");

        g.Angle = 90f;
        run.Check("conic: a=90 starts at 3 o'clock", Mathf.Abs(g.Parameter(new Vector2(90f, 50f))) < 0.001f, $"{g.Parameter(new Vector2(90f, 50f))}");

        // Wedge colours never wrap: the far edge of the last wedge is 1, not back to 0.
        var p = Tessellator.ConicWedgeParameter(35, 10f, 5f);
        run.Check("conic: last wedge's far edge is 1, not 0", Mathf.Abs(p - 1f) < 0.0001f, $"{p}");
    }

    internal static void Filters(TestRun run)
    {
        var red = new Color(1f, 0f, 0f, 0.5f);
        var gray = ColourFilter.Apply(red, ColourFilter.Grayscale, 1f);
        run.Check("filter: grayscale(1) of red is its luminance", Mathf.Abs(gray.r - 0.2126f) < 0.001f && Mathf.Abs(gray.g - gray.r) < 0.001f, $"{gray}");
        run.Check("filter: alpha is untouched", Mathf.Abs(gray.a - 0.5f) < 0.0001f, $"{gray.a}");

        var hue = ColourFilter.Apply(Color.red, ColourFilter.HueRotate, 360f);
        run.Check("filter: hue-rotate(360) is identity", Mathf.Abs(hue.r - 1f) < 0.002f && hue.g < 0.002f && hue.b < 0.002f, $"{hue}");

        var same = ColourFilter.Apply(new Color(0.3f, 0.6f, 0.9f), ColourFilter.Saturate, 1f);
        run.Check("filter: saturate(1) is identity", Mathf.Abs(same.r - 0.3f) < 0.002f && Mathf.Abs(same.b - 0.9f) < 0.002f, $"{same}");

        var half = ColourFilter.Apply(Color.black, ColourFilter.Invert, 0.5f);
        run.Check("filter: invert(0.5) of black is mid grey", Mathf.Abs(half.r - 0.5f) < 0.001f, $"{half}");

        run.Check("filter: keys map to ops", ColourFilter.OpFor("sep") == ColourFilter.Sepia && ColourFilter.OpFor("x") < 0, "");

        // Inner group's filter first, then the parent's: invert, then brightness.
        var parent = new VertexTint(null, new List<int> { ColourFilter.Brightness }, new List<float> { 0.5f }, null);
        var child = new VertexTint(parent, new List<int> { ColourFilter.Invert }, new List<float> { 1f }, null);
        var c = child.ApplyFilters(Color.black);
        run.Check("filter: nested groups apply innermost first", Mathf.Abs(c.r - 0.5f) < 0.001f, $"{c}");
    }

    internal static void GroupMatrix(TestRun run)
    {
        // The matrix is innermost: scale x2 first, then rotate, then translate.
        var m = Tessellator.GroupMatrix(100f, 0f, 0f, 0f, 90f, 1f, 1f, new[] { 2f, 0f, 0f, 1f, 0f, 0f });
        var p = m.MultiplyPoint3x4(new Vector3(10f, 0f, 0f));
        run.Check("matrix: m acts before t r s", Mathf.Abs(p.x - 100f) < 0.001f && Mathf.Abs(p.y + 20f) < 0.001f, $"{p}");

        var back = Tessellator.AffineInverse(m).MultiplyPoint3x4(p);
        run.Check("matrix: managed inverse round-trips", (back - new Vector3(10f, 0f, 0f)).magnitude < 0.001f, $"{back}");

        var e = Tessellator.GroupMatrix(0f, 0f, 0f, 0f, 0f, 1f, 1f, new[] { 1f, 0f, 0f, 1f, 7f, 9f });
        var moved = e.MultiplyPoint3x4(Vector3.zero);
        run.Check("matrix: e and f translate", Mathf.Abs(moved.x - 7f) < 0.001f && Mathf.Abs(moved.y - 9f) < 0.001f, $"{moved}");
    }

    internal static void Partition(TestRun run)
    {
        var l = new List<Vector2> { new(0, 0), new(100, 0), new(100, 40), new(40, 40), new(40, 100), new(0, 100) };
        var pieces = ConvexPartition.Split(l);
        var area = 0f;
        var convex = true;
        foreach (var piece in pieces)
        {
            area += Mathf.Abs(Triangulator.SignedArea(piece));
            convex &= Tessellator.IsConvex(piece);
        }

        run.Check("partition: an L tiles into convex pieces", convex && Mathf.Abs(area - 6400f) < 1f && pieces.Count <= 3,
            $"{pieces.Count} pieces, area {area}");
    }

    internal static void ImageFit(TestRun run)
    {
        var contain = Tessellator.ImageRect(0f, 0f, 100f, 100f, 200, 100, 1);
        var cover = Tessellator.ImageRect(0f, 0f, 100f, 100f, 200, 100, 2);
        run.Check("img: contain letterboxes a wide picture", Mathf.Approximately(contain.height, 50f) && Mathf.Approximately(contain.y, 25f), $"{contain}");
        run.Check("img: cover overflows the long side", Mathf.Approximately(cover.width, 200f) && Mathf.Approximately(cover.x, -50f), $"{cover}");
        var uv = Tessellator.ImageUv(new Rect(0f, 0f, 10f, 10f), new Vector2(0f, 0f));
        run.Check("img: scene top-left is texture top-left", uv == new Vector2(0f, 1f), $"{uv}");
    }

    internal static void ForcedScrollOnce(TestRun run)
    {
        var offsets = new Dictionary<string, float>();
        var applied = new Dictionary<string, float>();
        ForcedScroll.Apply(offsets, applied, "a", 1f, 50f);
        offsets["a"] = 10f;
        var again = ForcedScroll.Apply(offsets, applied, "a", 1f, 50f);
        run.Check("sov: an unchanged version leaves the wheel's offset alone", !again && offsets["a"] == 10f, $"{offsets["a"]}");
        var bumped = ForcedScroll.Apply(offsets, applied, "a", 2f, 70f);
        run.Check("sov: a new version applies", bumped && offsets["a"] == 70f, $"{offsets["a"]}");
    }

    internal static void InsetField(TestRun run)
    {
        // A 100x40 box: inradius from the centroid is 20.
        var box = new List<Vector2> { new(0, 0), new(100, 0), new(100, 40), new(0, 40) };
        run.Check("inset: inradius of a box", Mathf.Abs(Shadow.Inradius(box) - 20f) < 0.001f, $"{Shadow.Inradius(box)}");

        var field = new Shadow.InsetField(box, Mathf.Sign(Triangulator.SignedArea(box)), 0f, 4f, Color.black);
        run.Check("inset: depth is positive inside, negative outside",
            Mathf.Abs(field.Depth(new Vector2(50f, 10f)) - 10f) < 0.001f && Mathf.Abs(field.Depth(new Vector2(50f, -5f)) + 5f) < 0.001f,
            $"{field.Depth(new Vector2(50f, 10f))} / {field.Depth(new Vector2(50f, -5f))}");
        run.Check("inset: half alpha on the edge, clear deep inside",
            Mathf.Abs(field.Alpha(new Vector2(50f, 0f)) - 0.5f) < 0.01f && field.Alpha(new Vector2(50f, 20f)) < 0.01f,
            $"{field.Alpha(new Vector2(50f, 0f))} / {field.Alpha(new Vector2(50f, 20f))}");
    }
}
