using System.Collections.Generic;
using ScriptedScreensVector;
using UnityEngine;

namespace ScriptedScreensVector.Tests;

/// <summary>
/// The overlap test that decides whether text in draw order is affordable.
/// </summary>
/// <remarks>
/// The design note for this feature says not to start it without these: putting a label in
/// draw order means cutting the mesh, and a cut is another draw call. The naive version cuts
/// at every label and takes a page of thirty labelled tiles from one draw call to sixty.
///
/// So the assertions that matter are the ones about cutting **rarely**, not the one about
/// cutting correctly.
/// </remarks>
internal static class TextOrderTests
{
    /// <summary>Adds one shape of the given bounds, four vertices, to a builder.</summary>
    private static void AddShape(MeshBuilder builder, Rect box)
    {
        var colour = new Color32(255, 255, 255, 255);

        builder.AddVert(new Vector3(box.xMin, box.yMin), colour, Vector2.zero);
        builder.AddVert(new Vector3(box.xMax, box.yMin), colour, Vector2.zero);
        builder.AddVert(new Vector3(box.xMax, box.yMax), colour, Vector2.zero);
        builder.AddVert(new Vector3(box.xMin, box.yMax), colour, Vector2.zero);
        builder.MarkShape();
    }

    private static TextPlacement Label(Rect box, int afterShapes)
    {
        return new TextPlacement { Text = "x", Rect = box, ShapeIndex = afterShapes };
    }

    /// <summary>A label nothing covers must leave the mesh in one piece.</summary>
    internal static void UncoveredLabelForcesNoCut(TestRun run)
    {
        var builder = new MeshBuilder();
        builder.TrackBounds(true);

        AddShape(builder, Rect.MinMaxRect(0f, 0f, 100f, 40f));      // a panel
        var labels = new List<TextPlacement> { Label(Rect.MinMaxRect(10f, 10f, 60f, 30f), 1) };
        AddShape(builder, Rect.MinMaxRect(0f, 200f, 100f, 240f));   // far below, no overlap

        TextOrder.Assign(labels, builder);
        var slices = builder.Slices();
        TextOrder.Resolve(labels, builder, slices);

        run.Check("ztext: an uncovered label does not split the mesh", slices == 1, $"slices {slices}");
        run.Check("ztext: an uncovered label stays on top", labels[0].SliceDepth == 1,
            $"depth {labels[0].SliceDepth} of {slices}");
    }

    /// <summary>A shape declared after a label, and over it, must push the label underneath.</summary>
    internal static void CoveredLabelIsCutUnder(TestRun run)
    {
        var builder = new MeshBuilder();
        builder.TrackBounds(true);

        AddShape(builder, Rect.MinMaxRect(0f, 0f, 100f, 40f));
        var labels = new List<TextPlacement> { Label(Rect.MinMaxRect(10f, 10f, 60f, 30f), 1) };
        AddShape(builder, Rect.MinMaxRect(20f, 5f, 80f, 35f));      // sits over the label

        TextOrder.Assign(labels, builder);
        var slices = builder.Slices();
        TextOrder.Resolve(labels, builder, slices);

        run.Check("ztext: a covering shape splits the mesh", slices == 2, $"slices {slices}");
        run.Check("ztext: the label draws under the covering shape", labels[0].SliceDepth == 1,
            $"depth {labels[0].SliceDepth} of {slices}");
    }

    /// <summary>A shape already drawn when the label was collected is underneath it anyway.</summary>
    internal static void EarlierShapeIsIgnored(TestRun run)
    {
        var builder = new MeshBuilder();
        builder.TrackBounds(true);

        // Overlaps the label exactly, but is declared BEFORE it, so it is already behind.
        AddShape(builder, Rect.MinMaxRect(0f, 0f, 100f, 40f));
        var labels = new List<TextPlacement> { Label(Rect.MinMaxRect(10f, 10f, 60f, 30f), 1) };

        TextOrder.Assign(labels, builder);
        var slices = builder.Slices();

        run.Check("ztext: a shape declared before the label forces no cut", slices == 1, $"slices {slices}");
    }

    /// <summary>
    /// The case the design note warns about: thirty labelled tiles must stay one draw call.
    /// </summary>
    /// <remarks>
    /// This is the whole reason the overlap test exists. Cutting at every label would give
    /// thirty meshes here; the tiles do not overlap each other, so the answer is one.
    /// </remarks>
    internal static void AGridOfLabelledTilesStaysOneMesh(TestRun run)
    {
        var builder = new MeshBuilder();
        builder.TrackBounds(true);
        var labels = new List<TextPlacement>();

        for (var row = 0; row < 6; row++)
        {
            for (var column = 0; column < 5; column++)
            {
                var x = column * 100f;
                var y = row * 50f;

                AddShape(builder, Rect.MinMaxRect(x, y, x + 90f, y + 40f));
                labels.Add(Label(Rect.MinMaxRect(x + 5f, y + 5f, x + 85f, y + 35f), builder.ShapeCount));
            }
        }

        TextOrder.Assign(labels, builder);
        var slices = builder.Slices();
        TextOrder.Resolve(labels, builder, slices);

        run.Check("ztext: 30 labelled tiles stay in one mesh", slices == 1,
            $"{labels.Count} labels produced {slices} meshes");

        var onTop = 0;
        foreach (var label in labels)
        {
            if (label.SliceDepth == slices)
                onTop++;
        }

        run.Check("ztext: every tile label stays in the top layer", onTop == labels.Count,
            $"{onTop} of {labels.Count}");
    }

    /// <summary>A forced cut is taken even though the vertex budget had room to spare.</summary>
    /// <remarks>
    /// The ordinary cuts are candidates picked to fill 60,000 vertices. This one is a
    /// requirement, and a greedy scan that ignored it would put the covering shape in the
    /// same mesh as the geometry under the label — which draws the label on top again, the
    /// bug this whole feature exists to fix, and silently.
    /// </remarks>
    internal static void ForcedCutBeatsTheVertexBudget(TestRun run)
    {
        var builder = new MeshBuilder();
        builder.TrackBounds(true);

        AddShape(builder, Rect.MinMaxRect(0f, 0f, 100f, 40f));
        var labels = new List<TextPlacement> { Label(Rect.MinMaxRect(10f, 10f, 60f, 30f), 1) };
        AddShape(builder, Rect.MinMaxRect(20f, 5f, 80f, 35f));

        TextOrder.Assign(labels, builder);

        var slices = builder.Slices();
        var used = builder.currentVertCount;

        run.Check("ztext: the cut is taken on 8 vertices, not 60,000", slices == 2 && used == 8,
            $"{slices} meshes over {used} vertices");
    }

    /// <summary>A label before every shape stays visible rather than hiding under slice 0.</summary>
    /// <remarks>
    /// The surface's own renderer draws before any child, so nothing can be placed under it.
    /// A label in that position keeps the old behaviour — on top — because failing toward
    /// readable text is better than failing toward a label nobody can see.
    /// </remarks>
    internal static void LabelBeforeAllGeometryStaysVisible(TestRun run)
    {
        var builder = new MeshBuilder();
        builder.TrackBounds(true);

        var labels = new List<TextPlacement> { Label(Rect.MinMaxRect(10f, 10f, 60f, 30f), 0) };
        AddShape(builder, Rect.MinMaxRect(0f, 0f, 100f, 40f));

        TextOrder.Assign(labels, builder);
        var slices = builder.Slices();
        TextOrder.Resolve(labels, builder, slices);

        run.Check("ztext: a label before all geometry is not cut under slice 0",
            slices == 1 && labels[0].SliceDepth == 1,
            $"{slices} meshes, depth {labels[0].SliceDepth}");
    }

    /// <summary>`ztext` must survive the scene-text form, which is how the HTML mod arrives.</summary>
    /// <remarks>
    /// The table form sets it as an element prop directly. The `src` form has to carry it on
    /// a SCENE node, and a flag the HTML mod cannot reach is a flag it does not have.
    /// </remarks>
    internal static void ZtextSurvivesTheSrcForm(TestRun run)
    {
        var props = SceneText.ToProps(
            "SCENE vb=[0,0,100,100] ztext=1\nR x=0 y=0 w=10 h=10 f=#fff", "s");

        var found = false;
        if (props != null)
        {
            foreach (var prop in props)
            {
                if (prop.Key == "ztext")
                    found = true;
            }
        }

        run.Check("ztext: the src form carries the flag", found,
            props == null ? "scene text did not parse" : $"{props.Length} props");
    }

    /// <summary>Bounds tracking is off unless asked for, so no scene pays for it.</summary>
    internal static void BoundsAreNotTrackedByDefault(TestRun run)
    {
        var builder = new MeshBuilder();

        AddShape(builder, Rect.MinMaxRect(0f, 0f, 100f, 40f));

        var box = builder.ShapeBounds(0);
        run.Check("ztext: shape bounds cost nothing when unused", box.width == 0f && box.height == 0f,
            $"bounds {box}");
    }

    /// <summary>
    /// Text in draw order is ON unless a scene asks otherwise, and `ztext = 0` still opts out.
    /// </summary>
    /// <remarks>
    /// A default is exactly the sort of thing that flips back by accident during an unrelated
    /// edit and is never noticed, because both states render something plausible. This is the
    /// only thing that would catch it.
    /// </remarks>
    internal static void DrawOrderIsTheDefault(TestRun run)
    {
        var on = Parse("SCENE vb=[0,0,100,100]");
        var off = Parse("SCENE vb=[0,0,100,100] ztext=0");
        var explicitly = Parse("SCENE vb=[0,0,100,100] ztext=1");

        run.Check("ztext: on by default", on == true, $"got {Describe(on)}");
        run.Check("ztext: ztext=0 opts out", off == false, $"got {Describe(off)}");
        run.Check("ztext: ztext=1 still works", explicitly == true, $"got {Describe(explicitly)}");
    }

    /// <summary>Parses a one-shape scene and reports its draw-order flag, or null on failure.</summary>
    /// <remarks>
    /// The rect carries NO fill on purpose. `ParseFill` goes through
    /// <c>ColorUtility.TryParseHtmlString</c>, which is a native ECall and throws
    /// <c>SecurityException</c> outside the player — so a scene with `f=#fff` takes the whole
    /// run down here while working perfectly in game. Nothing about this flag needs a colour.
    /// </remarks>
    private static bool? Parse(string header)
    {
        var props = SceneText.ToProps(header + "\nR x=0 y=0 w=10 h=10", "s");
        if (props == null)
            return null;

        return SceneParser.Parse(props)?.TextInOrder;
    }

    private static string Describe(bool? value)
    {
        return value == null ? "scene did not parse" : value.Value.ToString();
    }

    /// <summary>What bounds tracking costs a scene that never overlaps anything.</summary>
    /// <remarks>
    /// The question this answers is whether `ztext` could simply be the default, rather than
    /// a flag every caller has to remember. Reported, not asserted: it is a measurement, and
    /// a timing assertion would fail on a busy machine for reasons unrelated to the code.
    ///
    /// Note this is .NET 8 and the game is Mono, so the absolute figure does not transfer —
    /// the project has been caught by that before. What does transfer is the shape: four
    /// float comparisons against list appends that are already happening.
    /// </remarks>
    internal static void BoundsTrackingCost(TestRun run)
    {
        const int Shapes = 10000;   // 40,000 vertices — a dense console

        var off = Time(Shapes, false);
        var on = Time(Shapes, true);

        run.Pass($"ztext: bounds tracking over {Shapes * 4:N0} verts -- "
                 + $"off {off:F2} ms, on {on:F2} ms, delta {on - off:F2} ms");
    }

    private static double Time(int shapes, bool track)
    {
        var builder = new MeshBuilder();
        var watch = new System.Diagnostics.Stopwatch();

        // Two passes; the first lets the lists reach their high-water mark and the JIT run,
        // so the timed one measures steady state rather than allocation.
        for (var pass = 0; pass < 2; pass++)
        {
            builder.Clear();
            builder.TrackBounds(track);
            watch.Restart();

            for (var i = 0; i < shapes; i++)
                AddShape(builder, Rect.MinMaxRect(i % 100, i % 50, i % 100 + 9f, i % 50 + 9f));

            watch.Stop();
        }

        return watch.Elapsed.TotalMilliseconds;
    }
}
