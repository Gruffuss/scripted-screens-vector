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

    internal static void LabelShear(TestRun run)
    {
        // Scene onto a canvas with the Y flip, as Tessellator builds it.
        var viewbox = Matrix4x4.Translate(new Vector3(0f, 200f, 0f)) * Matrix4x4.Scale(new Vector3(2f, -2f, 1f));

        var turned = viewbox * Tessellator.GroupMatrix(50f, 50f, 0f, 0f, 37f, 1.5f, 1.5f, null);
        run.Check("shear: a rotation and even scale leave labels alone", Tessellator.LabelShear(turned, 3f) == null, "");

        // matrix(1,0,-0.5,1): x' = x - 0.5 y. Scene up is -y, so going up the label moves +0.5 in x.
        var skewed = viewbox * Tessellator.GroupMatrix(0f, 0f, 0f, 0f, 0f, 1f, 1f, new[] { 1f, 0f, -0.5f, 1f, 0f, 0f });
        var k = Tessellator.LabelShear(skewed, 2f);
        run.Check("shear: skew goes to the glyphs, up leans right", k is { } s && Mathf.Abs(s.x - 1f) < 0.001f && Mathf.Abs(s.y - 0.5f) < 0.001f && Mathf.Abs(s.w - 1f) < 0.001f,
            k.HasValue ? k.Value.ToString() : "null");

        // scale(2,1) with the label laid out at the frame's scale of 2: half height left to apply.
        var wide = viewbox * Tessellator.GroupMatrix(0f, 0f, 0f, 0f, 0f, 2f, 1f, null);
        var q = Tessellator.LabelShear(wide, 4f);
        run.Check("shear: a one-axis stretch squashes the other axis", q is { } t && Mathf.Abs(t.x - 1f) < 0.001f && Mathf.Abs(t.w - 0.5f) < 0.001f,
            q.HasValue ? q.Value.ToString() : "null");
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

    private static ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem.UiProp Num(string key, float value)
    {
        return new() { Key = key, Value = new() { Type = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem.UiValueType.Number, Number = value } };
    }

    private static EvalContext Payload(bool keep, bool snap, params (string Key, float Value)[] values)
    {
        var map = new ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem.UiProp[values.Length];
        for (var i = 0; i < values.Length; i++)
            map[i] = Num(values[i].Key, values[i].Value);

        var props = new List<ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem.UiProp>
        {
            new() { Key = "data", Value = new() { Type = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem.UiValueType.Map, Map = map } },
        };
        if (keep) props.Add(Num("keep", 1f));
        if (snap) props.Add(Num("snap", 1f));

        var context = new EvalContext();
        SceneParser.ReadData(props.ToArray(), context);
        return context;
    }

    internal static void DataSnap(TestRun run)
    {
        var snapped = Payload(true, true, ("x", 5f));
        var eased = Payload(true, false, ("y", 5f));
        run.Check("snap: snap=1 marks the payload's numbers", snapped.Snapped.Contains("x") && eased.Snapped.Count == 0,
            $"{snapped.Snapped.Count} / {eased.Snapped.Count}");

        // Two parked patches: the later one must add to the earlier, not replace it.
        var parked = Payload(true, false, ("a", 1f), ("b", 2f));
        parked.MergeFrom(Payload(true, true, ("b", 3f), ("c", 4f)));
        run.Check("merge: a later patch keeps what an earlier one carried",
            parked.Scalars["a"] == 1f && parked.Scalars["b"] == 3f && parked.Scalars["c"] == 4f && parked.KeepUnmentioned,
            string.Join(",", parked.Scalars));
        run.Check("merge: snap follows each name's latest payload",
            parked.Snapped.Contains("b") && parked.Snapped.Contains("c") && !parked.Snapped.Contains("a"), string.Join(",", parked.Snapped));

        parked.MergeFrom(Payload(true, false, ("b", 9f)));
        run.Check("merge: an eased resend clears a name's snap", !parked.Snapped.Contains("b"), string.Join(",", parked.Snapped));

        parked.MergeFrom(Payload(false, false, ("z", 1f)));
        run.Check("merge: a full payload replaces everything",
            parked.Scalars.Count == 1 && parked.Scalars.ContainsKey("z") && !parked.KeepUnmentioned && parked.Snapped.Count == 0,
            string.Join(",", parked.Scalars));

        // An empty string is a value: it clears a label rather than being skipped. Only the empty
        // one is testable here -- any other string goes through ColorUtility, a native call.
        var empty = new EvalContext();
        SceneParser.ReadData(new[]
        {
            new ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem.UiProp
            {
                Key = "data", Value = new()
                {
                    Type = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem.UiValueType.Map,
                    Map = new[]
                    {
                        new ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem.UiProp
                        {
                            Key = "z", Value = new() { Type = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem.UiValueType.String, String = "" },
                        },
                    },
                },
            },
            Num("keep", 1f),
        }, empty);
        run.Check("data: an empty string is a value", empty.Strings.TryGetValue("z", out var cleared) && cleared == "",
            string.Join(",", empty.Strings));

        // CSS's `transparent` is a colour, not an unresolved name drawn magenta.
        var clear = new EvalContext();
        SceneParser.ReadData(new[]
        {
            new ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem.UiProp
            {
                Key = "data", Value = new()
                {
                    Type = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem.UiValueType.Map,
                    Map = new[]
                    {
                        new ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem.UiProp
                        {
                            Key = "f", Value = new() { Type = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem.UiValueType.String, String = "transparent" },
                        },
                    },
                },
            },
        }, clear);
        run.Check("data: transparent is a colour with no alpha", clear.Colours.TryGetValue("f", out var none) && none.a == 0f,
            string.Join(",", clear.Colours));

        // A name that changes kind under `keep` must lose its old kind: a string is looked up
        // before a number, so a stale string hid every later number.
        parked.MergeFrom(empty);
        run.Check("merge: a number resent as a string drops the number",
            parked.Strings.ContainsKey("z") && !parked.Scalars.ContainsKey("z"), string.Join(",", parked.Scalars));
        parked.MergeFrom(Payload(true, false, ("z", 2f)));
        run.Check("merge: a string resent as a number drops the string",
            !parked.Strings.ContainsKey("z") && parked.Scalars["z"] == 2f, string.Join(",", parked.Strings));

        // Mid-glide: 0 -> 10 at blend 0.5 shows 5, and a new payload must ease from 5, not 10.
        var live = new EvalContext();
        live.Scalars["v"] = 10f;
        live.Previous["v"] = 0f;
        live.Previous["gone"] = 3f;
        live.Rebase(0.5f);
        run.Check("rebase: a new payload eases from what is on screen", Mathf.Abs(live.Previous["v"] - 5f) < 0.001f, $"{live.Previous["v"]}");
        run.Check("rebase: names no longer sent are dropped", !live.Previous.ContainsKey("gone"), string.Join(",", live.Previous));
        live.Rebase(1f);
        run.Check("rebase: a finished blend starts from the target", live.Previous["v"] == 10f, $"{live.Previous["v"]}");
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

    /// <summary>`at` on IMG: where the picture sits in the room its fit leaves.</summary>
    internal static void ImagePosition(TestRun run)
    {
        // A 200x100 box with a square picture: contain leaves 100 free across.
        var centred = Tessellator.ImageRect(0f, 0f, 200f, 100f, 50, 50, 1);
        var left = Tessellator.ImageRect(0f, 0f, 200f, 100f, 50, 50, 1, 0f, 0.5f);
        var right = Tessellator.ImageRect(0f, 0f, 200f, 100f, 50, 50, 1, 1f, 0.5f);

        run.Check("at: contain centres by default", Mathf.Abs(centred.xMin - 50f) < 0.001f, $"{centred.xMin}");
        run.Check("at: 0 is flush left and 1 flush right",
            Mathf.Abs(left.xMin) < 0.001f && Mathf.Abs(right.xMax - 200f) < 0.001f,
            $"{left.xMin} .. {right.xMax}");
        run.Check("at: alignment does not change the picture's size",
            Mathf.Abs(left.width - centred.width) < 0.001f && Mathf.Abs(left.height - centred.height) < 0.001f,
            $"{left.width}x{left.height}");

        // Cover overflows, so the free space is negative and the same numbers pick an edge.
        var coverTop = Tessellator.ImageRect(0f, 0f, 200f, 100f, 50, 50, 2, 0.5f, 0f);
        var coverBottom = Tessellator.ImageRect(0f, 0f, 200f, 100f, 50, 50, 2, 0.5f, 1f);
        run.Check("at: under cover it chooses which edge is kept",
            Mathf.Abs(coverTop.yMin) < 0.001f && Mathf.Abs(coverBottom.yMax - 100f) < 0.001f,
            $"top {coverTop.yMin}, bottom {coverBottom.yMax}");

        // Fill leaves nothing free, so alignment cannot move anything.
        var fill = Tessellator.ImageRect(0f, 0f, 200f, 100f, 50, 50, 0, 0f, 0f);
        run.Check("at: fill is unaffected",
            Mathf.Abs(fill.width - 200f) < 0.001f && Mathf.Abs(fill.xMin) < 0.001f, $"{fill}");

        // `off` is scene units after `at`: CSS `right 10px` is at 1, off -10.
        var inset = Tessellator.ImageRect(0f, 0f, 200f, 100f, 50, 50, 1, 1f, 0.5f, -10f, 0f);
        run.Check("off: added after at", Mathf.Abs(inset.xMax - 190f) < 0.001f, $"{inset.xMax}");
        var shifted = Tessellator.ImageRect(0f, 0f, 200f, 100f, 50, 50, 0, 0f, 0f, 5f, -3f);
        run.Check("off: moves a filled picture too",
            Mathf.Abs(shifted.xMin - 5f) < 0.001f && Mathf.Abs(shifted.yMin + 3f) < 0.001f && Mathf.Abs(shifted.width - 200f) < 0.001f,
            $"{shifted}");

        // none: one scene unit per texel, placed by at.
        var none = Tessellator.ImageRect(0f, 0f, 200f, 100f, 30, 20, 3, 0f, 1f);
        run.Check("fit none: natural size, placed by at",
            none.width == 30f && none.height == 20f && Mathf.Abs(none.xMin) < 0.001f && Mathf.Abs(none.yMax - 100f) < 0.001f, $"{none}");

        // A filled picture moved by `off` no longer covers its box: the drawn area is cut to the
        // picture rather than stretching the texture's edge texels across the gap.
        ImageCache.Loaded["test:pic"] = (64, 32, null);
        var scene = SceneParser.Parse(SceneText.ToProps("SCENE w=200 h=100 fit=stretch\nIMG x=0 y=0 w=200 h=100 src=test:pic off=[50,0]", "img")!)!;
        var mesh = new MeshBuilder();
        Tessellator.Emit(mesh, scene, new EvalContext(), new Rect(0f, 0f, 200f, 100f), 1f, true, new TessellationStats());
        float minU = 1f, maxU = 0f, minX = float.MaxValue;
        for (var i = 0; i < mesh.currentVertCount; i++)
        {
            var (position, uv) = mesh.Vertex(i);
            minU = Mathf.Min(minU, uv.x);
            maxU = Mathf.Max(maxU, uv.x);
            minX = Mathf.Min(minX, position.x);
        }

        run.Check("off: the gap a moved picture leaves is not drawn",
            mesh.currentVertCount > 0 && minU > -0.001f && maxU < 1.001f && minX > 49.9f,
            $"{mesh.currentVertCount} verts, u {minU:0.###}..{maxU:0.###}, x from {minX:0.#}");

        // tile: a 64x32 picture at 50x25 across a 200x100 box, anchored top-left and moved 10 right.
        var tiled = SceneParser.Parse(SceneText.ToProps("SCENE w=200 h=100 fit=stretch\nIMG x=0 y=0 w=200 h=100 src=test:pic tile=[50,25] at=[0,0] off=[10,0]", "tile")!)!;
        var tiledMesh = new MeshBuilder();
        Tessellator.Emit(tiledMesh, tiled, new EvalContext(), new Rect(0f, 0f, 200f, 100f), 1f, true, new TessellationStats());
        float tMinX = float.MaxValue, tMaxX = float.MinValue;
        var leftEdgeU = true;
        for (var i = 0; i < tiledMesh.currentVertCount; i++)
        {
            var (position, uv) = tiledMesh.Vertex(i);
            tMinX = Mathf.Min(tMinX, position.x);
            tMaxX = Mathf.Max(tMaxX, position.x);

            // The first column starts at -40 and is cut at 0, so the box's left edge shows the
            // picture from 40/50 of the way across.
            if (Mathf.Abs(position.x) < 0.01f)
                leftEdgeU &= Mathf.Abs(uv.x - 0.8f) < 0.001f;
        }

        // Columns start at -40, 10, 60, 110, 160: five, the first and last cut, four rows. Every
        // piece is a quad: 20 quads, 80 vertices.
        run.Check("tile: repeats across the whole box and no further",
            tiledMesh.currentVertCount == 80 && Mathf.Abs(tMinX) < 0.01f && Mathf.Abs(tMaxX - 200f) < 0.01f,
            $"{tiledMesh.currentVertCount} verts, x {tMinX:0.##}..{tMaxX:0.##}");
        run.Check("tile: a cut tile shows the matching part of the picture", leftEdgeU, "");

        // rep per axis: one column, repeated down: four quads.
        var column = SceneParser.Parse(SceneText.ToProps("SCENE w=200 h=100 fit=stretch\nIMG x=0 y=0 w=200 h=100 src=test:pic tile=[50,25] at=[0,0] rep=[\"once\",\"repeat\"]", "rep")!)!;
        var columnMesh = new MeshBuilder();
        Tessellator.Emit(columnMesh, column, new EvalContext(), new Rect(0f, 0f, 200f, 100f), 1f, true, new TessellationStats());
        run.Check("tile: rep once across, repeat down, is one column", columnMesh.currentVertCount == 16 && column.Problems.Count == 0,
            $"{columnMesh.currentVertCount} verts, problems [{string.Join("; ", column.Problems)}]");

        // Nine-slice on the 64x32 picture: 8-texel insets drawn 10 units wide on a 200x100 box.
        MeshBuilder Sliced(string extra)
        {
            var sliced = SceneParser.Parse(SceneText.ToProps("SCENE w=200 h=100 fit=stretch\nIMG w=200 h=100 src=test:pic slice=[8,8,8,8] " + extra, "9s")!)!;
            var built = new MeshBuilder();
            Tessellator.Emit(built, sliced, new EvalContext(), new Rect(0f, 0f, 200f, 100f), 1f, true, new TessellationStats());
            return built;
        }

        var nine = Sliced("bw=[10,10,10,10]");
        var cornerOk = false;
        for (var i = 0; i < nine.currentVertCount; i++)
        {
            // Scene (10,10) is the top-left corner's inner point: canvas (10, 90), texture u 8/64,
            // v 1 - 8/32 (textures are +V up).
            var (position, uv) = nine.Vertex(i);
            if (Mathf.Abs(position.x - 10f) < 0.01f && Mathf.Abs(position.y - 90f) < 0.01f)
                cornerOk |= Mathf.Abs(uv.x - 0.125f) < 0.001f && Mathf.Abs(uv.y - 0.75f) < 0.001f;
        }

        run.Check("slice: nine pieces, corners at the border width showing the slice", nine.currentVertCount == 36 && cornerOk,
            $"{nine.currentVertCount} verts, corner {(cornerOk ? "ok" : "wrong")}");
        run.Check("slice: mid=0 leaves the middle out", Sliced("bw=[10,10,10,10] mid=0").currentVertCount == 32, "");
        run.Check("slice: borders wider than the box are scaled down together, and the rows meet",
            Sliced("bw=[60,60,60,60]").currentVertCount == 24, $"{Sliced("bw=[60,60,60,60]").currentVertCount} verts");

        // Tile sizes: a 64x32 picture. One axis at 0 keeps the aspect, CSS `50px auto`.
        bool Near((float W, float H) got, float w, float h) => Mathf.Abs(got.W - w) < 0.01f && Mathf.Abs(got.H - h) < 0.01f;
        var autoH = Tessellator.TileSize(50f, 0f, 64, 32, 200f, 100f, 0, 0, 0);
        var autoW = Tessellator.TileSize(0f, 25f, 64, 32, 200f, 100f, 0, 0, 0);
        run.Check("tile size: one axis at 0 keeps the picture's aspect", Near(autoH, 50f, 25f) && Near(autoW, 50f, 25f), $"{autoH} / {autoW}");

        var contain = Tessellator.TileSize(0f, 0f, 64, 32, 200f, 50f, 1, 0, 0);
        var cover = Tessellator.TileSize(0f, 0f, 64, 32, 200f, 50f, 2, 0, 0);
        run.Check("tile size: contain and cover fit the box", Near(contain, 100f, 50f) && Near(cover, 200f, 100f), $"{contain} / {cover}");

        // round: 200 wide holds 3.33 tiles of 60, so 3 of 66.67; an auto height follows.
        var rounded = Tessellator.TileSize(60f, 30f, 64, 32, 200f, 100f, 0, 2, 0);
        var roundedAuto = Tessellator.TileSize(60f, 0f, 64, 32, 200f, 100f, 0, 2, 0);
        run.Check("tile size: round fits whole tiles, an auto axis in proportion",
            Near(rounded, 66.667f, 30f) && Near(roundedAuto, 66.667f, 33.333f), $"{rounded} / {roundedAuto}");

        // Axes: 200 wide, 60 per tile.
        var spaced = Tessellator.TileAxis(3, 0f, 200f, 0f, 200f, 60f, 0.5f, 0f);
        run.Check("tile axis: space puts whole tiles edge to edge with even gaps",
            Mathf.Abs(spaced.First) < 0.001f && Mathf.Abs(spaced.Step - 70f) < 0.001f && spaced.Count == 3, $"{spaced}");
        var once = Tessellator.TileAxis(1, 0f, 200f, 0f, 200f, 50f, 0.5f, 0f);
        run.Check("tile axis: once is the anchored copy alone", Mathf.Abs(once.First - 75f) < 0.001f && once.Count == 1, $"{once}");
        var lonely = Tessellator.TileAxis(3, 0f, 200f, 0f, 200f, 150f, 0f, 0f);
        run.Check("tile axis: space with room for one falls back to once", lonely.Count == 1 && Mathf.Abs(lonely.First) < 0.001f, $"{lonely}");

        // srep: 8-texel slices drawn 8 wide, so edge tiles are 48 long in a 180-long edge.
        // repeat: 5 across (centred, both ends cut) on top, bottom and middle; left/right stretched.
        run.Check("srep: stretch is nine pieces", Sliced("bw=[8,8,8,8]").currentVertCount == 36, "");
        run.Check("srep: repeat centres whole tiles and cuts the ends",
            Sliced("bw=[8,8,8,8] srep=[\"repeat\",\"stretch\"]").currentVertCount == 21 * 4,
            $"{Sliced("bw=[8,8,8,8] srep=[\"repeat\",\"stretch\"]").currentVertCount / 4} pieces");
        run.Check("srep: round resizes so whole tiles fit",
            Sliced("bw=[8,8,8,8] srep=[\"round\",\"stretch\"]").currentVertCount == 18 * 4,
            $"{Sliced("bw=[8,8,8,8] srep=[\"round\",\"stretch\"]").currentVertCount / 4} pieces");
        run.Check("srep: space spreads whole tiles",
            Sliced("bw=[8,8,8,8] srep=[\"space\",\"stretch\"]").currentVertCount == 15 * 4,
            $"{Sliced("bw=[8,8,8,8] srep=[\"space\",\"stretch\"]").currentVertCount / 4} pieces");

        // smp=point asks the cache for a texture of its own, so the smooth one is untouched.
        var pixelated = SceneParser.Parse(SceneText.ToProps("SCENE w=200 h=100\nIMG w=200 h=100 src=test:pic smp=point", "px")!)!;
        var pixelatedMesh = new MeshBuilder();
        var pixelatedStats = new TessellationStats();
        Tessellator.Emit(pixelatedMesh, pixelated, new EvalContext(), new Rect(0f, 0f, 200f, 100f), 1f, true, pixelatedStats);
        var asked = pixelatedStats.Images.Count > 0 ? pixelatedStats.Images[0].Src : "(none)";
        run.Check("smp: point sampling is its own cache entry", asked == "point:test:pic", asked);

        // scale-down: none when it fits, contain when it does not.
        var small = Tessellator.ImageRect(0f, 0f, 200f, 100f, 30, 20, 4);
        var large = Tessellator.ImageRect(0f, 0f, 200f, 100f, 400, 400, 4);
        run.Check("fit scale-down: natural size when it fits, contained when it does not",
            small.width == 30f && Mathf.Abs(large.height - 100f) < 0.001f && Mathf.Abs(large.width - 100f) < 0.001f,
            $"{small} / {large}");
    }

    /// <summary>A scene animates only where a rebuild reached something that reads `t`.</summary>
    internal static void AnimatedReached(TestRun run)
    {
        bool Drew(string body)
        {
            var scene = SceneParser.Parse(SceneText.ToProps("SCENE w=100 h=100 fit=stretch\n" + body, "anim")!)!;
            var stats = new TessellationStats();
            Tessellator.Emit(new MeshBuilder(), scene, new EvalContext(), new Rect(0f, 0f, 100f, 100f), 1f, true, stats);
            return scene.UsesTime && stats.DrewTime;
        }

        const string Blink = "R x==10+5*sin(t*6) y=10 w=20 h=20";
        run.Check("animated: a blink that is drawn animates", Drew(Blink), "");
        run.Check("animated: a blink under v=0 does not", !Drew("G v=0 { " + Blink + " }"), "");
        run.Check("animated: nor under a group at o=0", !Drew("G o=0 { " + Blink + " }"), "");
        run.Check("animated: nor under a data-hidden group", !Drew("G v=$show { " + Blink + " }"), "");
        run.Check("animated: a group whose own v reads t does, hidden or not",
            Drew("G v==step(2,mod(t,1)) { R x=10 y=10 w=20 h=20 }"), "");
        // An `o` over `t` alone is faded by the renderer instead (see the probe); one that also
        // reads data cannot be, and rebuilds.
        run.Check("animated: so does one whose own o reads t and data", Drew("G o==$k*t { R x=10 y=10 w=20 h=20 }"), "");

        // Values the time check used to miss, so a scene animating only through them froze.
        run.Check("animated: text size reading t counts", Drew("T w=50 h=10 text=hi size==8+sin(t)"), "");
        run.Check("animated: an array index reading t counts", Drew("T w=50 h=10 text=\"$rows[mod(floor(t),4)]\""), "");
        run.Check("animated: a fo2 ramp reading t counts",
            Drew("YS n=4 x==i*10 y=10 y2=30 fo=1 fo2==0.5+0.5*sin(t)"), "");
        run.Check("animated: a placeholder index reading t counts", Drew("T w=50 h=10 text=\"{$rows[mod(floor(t),4)]}\""), "");
    }

    /// <summary>A text with several bound values, each printed through its own format.</summary>
    internal static void TextTemplate(TestRun run)
    {
        var node = new VecNode { TextFormat = "%.2f", TextUnit = null, TextMissing = "--" };
        node.TextParts = SceneParser.TextTemplate("set {$press:%.1f} kPa, trip {$trip:%.0f}, raw {$raw}, row {$rows[1]:%d} {$gone} {$open", node.TextFormat);
        var scene = new VecScene();
        var context = new EvalContext();
        context.Scalars["press"] = 101.26f;
        context.Scalars["trip"] = 88.4f;
        context.Scalars["raw"] = 3.14159f;
        context.Arrays["rows"] = new[] { 5f, 7.6f };

        var first = Tessellator.BindTemplate(node, context, scene, 0);
        run.Check("template: each placeholder prints through its own format, or the node's fmt",
            first == "set 101.3 kPa, trip 88, raw 3.14, row 8 -- {$open", first);
        run.Check("template: a missing value is reported and the rest still prints",
            context.Missing.Count == 1 && context.Missing.Contains("gone"), string.Join(",", context.Missing));

        var again = Tessellator.BindTemplate(node, context, scene, 0);
        run.Check("template: an unchanged label reuses its string", ReferenceEquals(first, again), again);

        var before = System.GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
            Tessellator.BindTemplate(node, context, scene, 0);
        var bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
        run.Check("template: an unchanged label allocates nothing", bytes == 0, $"{bytes} B over 100 rebuilds");

        context.Scalars["trip"] = 88.2f;
        var rounded = Tessellator.BindTemplate(node, context, scene, 0);
        run.Check("template: a value that prints the same digits reuses its string", ReferenceEquals(first, rounded), rounded);

        context.Strings["trip"] = "";
        var cleared = Tessellator.BindTemplate(node, context, scene, 0);
        run.Check("template: a string value prints as it is", cleared == "set 101.3 kPa, trip , raw 3.14, row 8 -- {$open", cleared);

        run.Check("template: a text with no placeholder keeps the literal path",
            SceneParser.TextTemplate("100 {kPa} $x", null) == null, "");
    }

    /// <summary>When a label may be left alone: only when applying it again would change nothing.</summary>
    internal static void LabelUnchanged(TestRun run)
    {
        TextPlacement Base() => new()
        {
            Text = "62%", Rect = new Rect(10f, 20f, 56f, 12f), Size = 7f, Colour = new Color(0.37f, 0.85f, 0.66f, 1f),
            Align = 1, VAlign = 1, Font = "Barlow", Bold = false, CharSpacing = 0f, Fit = 0, MinSize = 0f,
            Rotation = 0f, ClipRect = new Rect(0f, 0f, 200f, 200f), Wrap = false, LineHeight = 0f,
            Shadow = new VecShadow(1f, 1f, 2f, 0f, Color.black, false),
        };

        var a = Base();
        run.Check("label: an identical placement is unchanged", a.SameAs(Base()), "");

        var text = Base(); text.Text = "63%";
        run.Check("label: changed text is a change", !a.SameAs(text), "");

        // A string with the same characters built separately is still the same text.
        var rebuilt = Base(); rebuilt.Text = new string("62%".ToCharArray());
        run.Check("label: the same text in a new string is unchanged", a.SameAs(rebuilt), "");

        var moved = Base(); moved.Rect = new Rect(10.001f, 20f, 56f, 12f);
        run.Check("label: a moved box is a change", !a.SameAs(moved), "");

        var colour = Base(); colour.Colour = Color.red;
        run.Check("label: a new colour is a change", !a.SameAs(colour), "");

        var size = Base(); size.Size = 7.5f;
        run.Check("label: a new size is a change", !a.SameAs(size), "");

        var font = Base(); font.Font = "Barlow Bold";
        run.Check("label: a new font is a change", !a.SameAs(font), "");

        var outlined = Base(); outlined.OutlineWidth = 1f;
        var recoloured = Base(); recoloured.OutlineColour = Color.red;
        run.Check("label: a new outline width or colour is a change", !a.SameAs(outlined) && !a.SameAs(recoloured), "");

        // `ow` is scene units and reaches the label in canvas units, like `size`.
        var stroked = SceneParser.Parse(SceneText.ToProps("SCENE w=100 h=100 fit=stretch\nT w=80 h=12 text=hi ow=0.5", "ow")!)!;
        var strokedStats = new TessellationStats();
        Tessellator.Emit(new MeshBuilder(), stroked, new EvalContext(), new Rect(0f, 0f, 200f, 200f), 1f, true, strokedStats);
        var reached = strokedStats.Text.Count == 1 ? strokedStats.Text[0].OutlineWidth : -1f;
        run.Check("outline: ow scales with the scene", Mathf.Abs(reached - 1f) < 0.001f, $"{reached}");

        var turned = Base(); turned.Rotation = 90f;
        run.Check("label: a rotation is a change", !a.SameAs(turned), "");

        var shadow = Base(); shadow.Shadow = new VecShadow(1f, 1f, 2f, 0f, Color.red, false);
        run.Check("label: a new shadow colour is a change", !a.SameAs(shadow), "");

        var noShadow = Base(); noShadow.Shadow = null;
        run.Check("label: losing a shadow is a change", !a.SameAs(noShadow), "");

        var clip = Base(); clip.ClipRect = null;
        run.Check("label: losing a clip is a change", !a.SameAs(clip), "");

        // Where the label sits among the mesh slices is ordered separately, not a label property.
        var reordered = Base(); reordered.SliceDepth = 3; reordered.ShapeIndex = 9;
        run.Check("label: slice order alone is not a change to the label", a.SameAs(reordered), "");

        // Reference-typed parts compare by reference: cautious, never stale.
        var polygon = new System.Collections.Generic.List<Vector2> { Vector2.zero, Vector2.one, Vector2.right };
        var withA = Base(); withA.ClipPolygon = polygon;
        var withSame = Base(); withSame.ClipPolygon = polygon;
        var withCopy = Base(); withCopy.ClipPolygon = new System.Collections.Generic.List<Vector2>(polygon);
        run.Check("label: the same clip polygon object is unchanged", withA.SameAs(withSame), "");
        run.Check("label: a rebuilt clip polygon counts as a change", !withA.SameAs(withCopy), "");
    }
}
