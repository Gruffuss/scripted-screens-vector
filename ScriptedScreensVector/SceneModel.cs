using System;
using System.Collections.Generic;
using UnityEngine;
using SS = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem;

namespace ScriptedScreensVector;

internal enum VecOp
{
    Group,
    Repeat,
    Rect,
    Ellipse,
    Band,
    Polyline,
    SampledLine,
    Spline,
    Path,
}

internal enum FitMode
{
    Stretch,
    Contain,
    Cover,
}

/// <summary>
/// One node of a parsed scene. Attributes are compiled <see cref="Expression"/>s, so a
/// literal number and an animated expression are the same thing to the tessellator.
/// </summary>
internal sealed class VecNode
{
    internal VecOp Op;
    internal List<VecNode> Children = new();

    // Geometry. Rect uses X/Y/W/H/Rx/Ry; ellipse uses Cx/Cy/Rx/Ry.
    internal Expression X = Expression.Constant(0f);
    internal Expression Y = Expression.Constant(0f);
    internal Expression W = Expression.Constant(0f);
    internal Expression H = Expression.Constant(0f);
    internal Expression Rx = Expression.Constant(0f);
    internal Expression Ry = Expression.Constant(0f);

    // Group transform.
    internal Expression Tx = Expression.Constant(0f);
    internal Expression Ty = Expression.Constant(0f);
    internal Expression Rotate = Expression.Constant(0f);
    internal Expression Sx = Expression.Constant(1f);
    internal Expression Sy = Expression.Constant(1f);
    internal Expression Ax = Expression.Constant(0f);
    internal Expression Ay = Expression.Constant(0f);
    internal Expression Opacity = Expression.Constant(1f);

    /// <summary>Repeat count, and sample count for <see cref="VecOp.Band"/>.</summary>
    internal int RepeatCount;

    /// <summary>
    /// Whether this repeat may shed instances when drawn small. Never null-safe to ignore:
    /// dropping instances from a gauge's tick marks would be a rendering bug, while dropping
    /// them from a particle field is invisible.
    /// </summary>
    internal bool AllowLod;

    /// <summary>
    /// Fill opacity at a band's <c>y2</c> edge, ramping from <c>fo</c> at the <c>y</c> edge.
    /// Null when absent, which keeps a plain band exactly as it was.
    /// </summary>
    /// <remarks>
    /// Exists because the alternatives cannot express a ramp measured from a MOVING edge.
    /// A gradient is linear in space and anchored to the shape's bounding box, so on a
    /// rippling surface it reaches only FADE/(FADE+amplitude) of full opacity at a crest and
    /// starts partway up the ramp in a trough — a bright line exactly where the fade should
    /// vanish. Feathering ramps outward from a solid edge, which is the opposite direction.
    ///
    /// A per-column ramp between the band's own two edges follows the wave exactly, because
    /// each column interpolates between its own sampled endpoints. It costs no extra
    /// geometry: the strip already emits a vertex on each edge.
    ///
    /// This replaces the idiom of stacking N abutting bands of rising opacity, which was
    /// measured at 160 of one console's 184 bands and two thirds of its tessellation cost.
    /// </remarks>
    internal Expression? EdgeOpacity;

    /// <summary>Second edge of a band. Evaluated per sample alongside <see cref="Y"/>.</summary>
    internal Expression Y2 = Expression.Constant(0f);

    /// <summary>Literal point list for L / SP, as flat x,y pairs.</summary>
    internal float[] Points = System.Array.Empty<float>();

    /// <summary>Parsed SVG path data for P. Static geometry; only tolerance varies.</summary>
    internal PathData? Path;

    /// <summary>Fill rule for multi-subpath fills. <c>true</c> selects even-odd.</summary>
    internal bool EvenOdd;

    /// <summary>Closes the point list, making it a polygon outline rather than an open line.</summary>
    internal bool Closed;

    /// <summary>Clip path this group applies to its subtree, by name.</summary>
    internal string? ClipRef;

    internal bool HasFill;
    internal string? FillGradient;

    /// <summary>Data-payload colour binding, from <c>f = "$name"</c>.</summary>
    internal string? FillData;

    /// <summary>
    /// Samples <see cref="FillGradient"/> at an expression instead of by position, from
    /// <c>f = { grad = "name", at = "=expr" }</c>. This is how a colour animates.
    /// </summary>
    internal Expression? FillGradientAt;
    internal Color Fill = Color.white;
    internal Expression FillOpacity = Expression.Constant(1f);

    internal bool HasStroke;
    internal string? StrokeGradient;

    /// <summary>Data-payload colour binding, from <c>s = "$name"</c>.</summary>
    internal string? StrokeData;

    /// <summary>Expression-sampled gradient for the stroke; see <see cref="FillGradientAt"/>.</summary>
    internal Expression? StrokeGradientAt;
    internal Color StrokeColour = Color.white;
    internal Expression StrokeWidth = Expression.Constant(1f);
    internal Expression StrokeOpacity = Expression.Constant(1f);
    internal CapStyle Cap = CapStyle.Butt;
    internal JoinStyle Join = JoinStyle.Miter;
    internal float MiterLimit = 4f;
    internal float[] DashPattern = System.Array.Empty<float>();
    internal Expression DashOffset = Expression.Constant(0f);

    /// <summary>
    /// Edge feather in scene units. Negative means "auto": pick a width that lands near
    /// one pixel on screen, which is what makes edges read as smooth rather than jagged.
    /// </summary>
    internal Expression Feather = Expression.Constant(-1f);

    /// <summary>
    /// Feather for a band's sampled edge only, from <c>fea_edge</c>. Null falls back to
    /// <see cref="Feather"/>.
    /// </summary>
    internal Expression? EdgeFeather;

    /// <summary>True if this node or anything under it references <c>t</c>.</summary>
    internal bool UsesTime;
}

/// <summary>A parsed scene: viewbox plus node tree.</summary>
internal sealed class VecScene
{
    internal string Id = string.Empty;
    internal float ViewWidth = 100f;
    internal float ViewHeight = 100f;
    internal FitMode Fit = FitMode.Stretch;
    internal List<VecNode> Root = new();
    internal bool UsesTime;

    /// <summary>Gradients declared in <c>defs</c>, by id.</summary>
    internal readonly Dictionary<string, Gradient> Gradients = new(StringComparer.Ordinal);

    /// <summary>Clip outlines declared in <c>defs</c>, by id, in scene coordinates.</summary>
    internal readonly Dictionary<string, List<Vector2>> Clips = new(StringComparer.Ordinal);

    /// <summary>
    /// Ablation switches, set from Lua so a measurement can be taken without restarting.
    /// </summary>
    /// <remarks>
    /// Five plausible hotspots have been falsified by measurement — vertex count, expression
    /// depth, `Matrix4x4.lossyScale`, `VertexHelper`, and mesh upload. What is left is ~2.5 us
    /// per shape of diffuse work with no obvious peak, and the only way to localise that
    /// without a profiler is to remove one stage at a time and subtract.
    ///
    /// Set on the scene root: `nofill = 1`, `nofeather = 1`, `noeval = 1`.
    /// </remarks>
    internal bool DebugNoFill;

    internal bool DebugNoFeather;

    /// <summary>Evaluate every attribute once and reuse, isolating evaluation from geometry.</summary>
    internal bool DebugNoEval;

    /// <summary>
    /// Problems found while parsing: unknown ops, missing clips, unusable gradients.
    /// </summary>
    /// <remarks>
    /// A scene that draws nothing is indistinguishable from a console that is off, and the
    /// warnings explaining why only reach `BepInEx/LogOutput.log` — not where a script author
    /// is looking. Collecting them here lets the renderer mark the surface visibly, so the
    /// failure at least announces itself.
    /// </remarks>
    internal readonly List<string> Problems = new();

    internal void Problem(string message)
    {
        if (Problems.Count < 16 && !Problems.Contains(message))
            Problems.Add(message);

        ScriptedScreensVectorPlugin.Log?.LogWarning($"scene \"{Id}\": {message}");
    }
}

/// <summary>
/// Builds a <see cref="VecScene"/> from ScriptedScreens' structured props.
/// </summary>
/// <remarks>
/// Parsing happens once, when the structure element is upserted — not per frame. Malformed
/// nodes are skipped with a warning rather than aborting: a console with one bad node
/// should still draw the rest.
/// </remarks>
internal static class SceneParser
{
    private const int MaxRepeat = 20000;

    internal static VecScene? Parse(SS.UiProp[] props)
    {
        var root = PropValue(props, "root");
        if (root == null || root.Value.Type != SS.UiValueType.Array)
            return null;

        var scene = new VecScene
        {
            Id = PropString(props, "scene") ?? string.Empty,
            ViewWidth = Mathf.Max(1f, PropNumber(props, "w", 100f)),
            ViewHeight = Mathf.Max(1f, PropNumber(props, "h", 100f)),
            Fit = ParseFit(PropString(props, "fit")),
            DebugNoFill = PropNumber(props, "nofill", 0f) > 0.5f,
            DebugNoFeather = PropNumber(props, "nofeather", 0f) > 0.5f,
            DebugNoEval = PropNumber(props, "noeval", 0f) > 0.5f,
        };

        ParseDefs(PropValue(props, "defs"), scene);

        foreach (var child in ParseNodes(root.Value, scene))
            scene.Root.Add(child);

        foreach (var node in scene.Root)
            scene.UsesTime |= node.UsesTime;

        return scene;
    }

    /// <summary>Reads the paired data element's values into an evaluation context.</summary>
    internal static void ReadData(SS.UiProp[] props, EvalContext into)
    {
        into.Scalars.Clear();
        into.Arrays.Clear();
        into.Colours.Clear();

        var data = PropValue(props, "data");
        if (data == null || data.Value.Type != SS.UiValueType.Map || data.Value.Map == null)
            return;

        foreach (var entry in data.Value.Map)
        {
            if (string.IsNullOrEmpty(entry.Key))
                continue;

            switch (entry.Value.Type)
            {
                case SS.UiValueType.Number:
                    into.Scalars[entry.Key] = entry.Value.Number;
                    break;

                case SS.UiValueType.String when !string.IsNullOrEmpty(entry.Value.String):
                    if (ColorUtility.TryParseHtmlString(entry.Value.String, out var dataColour))
                        into.Colours[entry.Key] = dataColour;

                    break;

                case SS.UiValueType.Array when entry.Value.Array != null:
                {
                    var source = entry.Value.Array;
                    var values = new float[source.Length];
                    for (var i = 0; i < source.Length; i++)
                        values[i] = source[i].Type == SS.UiValueType.Number ? source[i].Number : 0f;

                    into.Arrays[entry.Key] = values;
                    break;
                }
            }
        }
    }

    private static List<VecNode> ParseNodes(SS.UiValue array, VecScene scene)
    {
        var nodes = new List<VecNode>();
        if (array.Array == null)
            return nodes;

        foreach (var item in array.Array)
        {
            if (item.Type != SS.UiValueType.Map || item.Map == null)
                continue;

            var node = ParseNode(item.Map, scene);
            if (node != null)
                nodes.Add(node);
        }

        return nodes;
    }

    private static VecNode? ParseNode(SS.UiProp[] map, VecScene scene)
    {
        var op = PropString(map, "op");
        if (string.IsNullOrEmpty(op))
            return null;

        var node = new VecNode();

        switch (op!.ToUpperInvariant())
        {
            case "G":
                node.Op = VecOp.Group;
                node.Tx = Pair(map, "t", 0, 0f);
                node.Ty = Pair(map, "t", 1, 0f);
                node.Sx = Pair(map, "s", 0, 1f);
                node.Sy = Pair(map, "s", 1, 1f);
                node.Ax = Pair(map, "a", 0, 0f);
                node.Ay = Pair(map, "a", 1, 0f);
                node.Rotate = Attr(map, "r", 0f);
                node.Opacity = Attr(map, "o", 1f);
                node.ClipRef = PropString(map, "clip");
                break;

            case "RP":
            {
                node.Op = VecOp.Repeat;
                node.RepeatCount = Mathf.Clamp(Mathf.RoundToInt(PropNumber(map, "n", 0f)), 0, MaxRepeat);

                // OFF by default. Shedding instances makes a field pop in and out and
                // visibly dims it, because apparent density drops with the count. Temporal
                // LOD in VectorGraphic gets the same saving with no visual artefact, so
                // count reduction is reserved for scenes that explicitly ask for it.
                var lod = PropValue(map, "lod");
                node.AllowLod = lod?.Type == SS.UiValueType.Number && lod.Value.Number > 0.5f;

                break;
            }

            case "R":
                node.Op = VecOp.Rect;
                node.X = Attr(map, "x", 0f);
                node.Y = Attr(map, "y", 0f);
                node.W = Attr(map, "w", 0f);
                node.H = Attr(map, "h", 0f);
                node.Rx = Attr(map, "rx", 0f);
                node.Ry = HasKey(map, "ry") ? Attr(map, "ry", 0f) : node.Rx;
                break;

            case "C":
                node.Op = VecOp.Ellipse;
                node.X = Attr(map, "cx", 0f);
                node.Y = Attr(map, "cy", 0f);
                node.Rx = Attr(map, "rx", 0f);
                node.Ry = HasKey(map, "ry") ? Attr(map, "ry", 0f) : node.Rx;
                break;

            case "L":
            case "Y":
                node.Op = VecOp.Polyline;
                node.Closed = string.Equals(op, "Y", StringComparison.OrdinalIgnoreCase);
                node.Points = Numbers(map, "p");
                break;

            case "P":
                node.Op = VecOp.Path;
                node.Path = PathData.Parse(PropString(map, "d") ?? string.Empty);
                node.EvenOdd = string.Equals(PropString(map, "fr"), "evenodd", StringComparison.OrdinalIgnoreCase);
                break;

            case "SP":
                node.Op = VecOp.Spline;
                node.Points = Numbers(map, "p");
                node.RepeatCount = Mathf.Clamp(Mathf.RoundToInt(PropNumber(map, "seg", 8f)), 1, 32);
                break;

            case "LS":
                // Stroked sibling of YS: n samples of x/y joined into one open path.
                node.Op = VecOp.SampledLine;
                node.RepeatCount = Mathf.Clamp(Mathf.RoundToInt(PropNumber(map, "n", 0f)), 0, MaxRepeat);
                node.X = Attr(map, "x", 0f);
                node.Y = Attr(map, "y", 0f);
                break;

            case "YS":
                // Sampled band: n samples of x/y/y2, joined as a triangle strip. This is
                // the connected-surface primitive RP cannot express -- RP instantiates
                // separate shapes, which is what produces a staircase instead of a curve.
                node.Op = VecOp.Band;
                node.RepeatCount = Mathf.Clamp(Mathf.RoundToInt(PropNumber(map, "n", 0f)), 0, MaxRepeat);
                node.X = Attr(map, "x", 0f);
                node.Y = Attr(map, "y", 0f);
                node.Y2 = Attr(map, "y2", 0f);

                // Optional per-column opacity ramp toward the y2 edge.
                if (HasKey(map, "fo2"))
                    node.EdgeOpacity = Attr(map, "fo2", 0f);

                break;

            default:
                // Unimplemented ops (P, L, Y) are skipped rather than treated as errors:
                // the spec defines them, this renderer does not draw them yet.
                scene.Problem($"op \"{op}\" is not supported");
                return null;
        }

        ParseFill(map, node);
        ParseStroke(map, node);

        var children = PropValue(map, "c");
        if (children != null && children.Value.Type == SS.UiValueType.Array)
        {
            foreach (var child in ParseNodes(children.Value, scene))
                node.Children.Add(child);
        }

        node.UsesTime = NodeUsesTime(node);
        foreach (var child in node.Children)
            node.UsesTime |= child.UsesTime;

        return node;
    }

    private static bool NodeUsesTime(VecNode node)
    {
        return node.X.UsesTime || node.Y.UsesTime || node.W.UsesTime || node.H.UsesTime
               || node.Rx.UsesTime || node.Ry.UsesTime
               || node.Tx.UsesTime || node.Ty.UsesTime || node.Rotate.UsesTime
               || node.Sx.UsesTime || node.Sy.UsesTime || node.Ax.UsesTime || node.Ay.UsesTime
               || node.Y2.UsesTime
               || node.Opacity.UsesTime || node.FillOpacity.UsesTime || node.Feather.UsesTime
               || node.EdgeFeather is { UsesTime: true }
               || node.StrokeWidth.UsesTime || node.StrokeOpacity.UsesTime || node.DashOffset.UsesTime
               || node.FillGradientAt is { UsesTime: true } || node.StrokeGradientAt is { UsesTime: true };
    }

    private static void ParseFill(SS.UiProp[] map, VecNode node)
    {
        // Map form: f = { grad = "name", at = "=expr" } samples the ramp at an expression
        // rather than by position, which is how a colour is animated or driven by data.
        var paint = PropValue(map, "f");
        if (paint?.Type == SS.UiValueType.Map && paint.Value.Map != null)
        {
            var name = PropString(paint.Value.Map, "grad");
            if (!string.IsNullOrEmpty(name))
            {
                node.HasFill = true;
                node.FillGradient = name;
                node.FillGradientAt = Attr(paint.Value.Map, "at", 0f);
                node.FillOpacity = Attr(map, "fo", 1f);
                node.Feather = Attr(map, "fea", -1f);

        if (HasKey(map, "fea_edge"))
            node.EdgeFeather = Attr(map, "fea_edge", -1f);
            }

            return;
        }

        var fill = PropString(map, "f");
        if (string.IsNullOrEmpty(fill) || string.Equals(fill, "none", StringComparison.OrdinalIgnoreCase))
            return;

        // Gradient (@name) and data ($name) paints are spec'd but not implemented yet;
        // fall back to white so the shape is visible rather than silently absent.
        if (fill![0] == '@')
        {
            node.HasFill = true;
            node.FillGradient = fill[1..];
            node.FillOpacity = Attr(map, "fo", 1f);
            node.Feather = Attr(map, "fea", -1f);

        if (HasKey(map, "fea_edge"))
            node.EdgeFeather = Attr(map, "fea_edge", -1f);
            return;
        }

        // $name binds the colour to the data payload, re-read every tick.
        if (fill[0] == '$')
        {
            node.HasFill = true;
            node.FillData = fill[1..];
            node.FillOpacity = Attr(map, "fo", 1f);
            node.Feather = Attr(map, "fea", -1f);

        if (HasKey(map, "fea_edge"))
            node.EdgeFeather = Attr(map, "fea_edge", -1f);
            return;
        }

        if (!ColorUtility.TryParseHtmlString(fill, out var colour))
            return;

        node.HasFill = true;
        node.Fill = colour;
        node.FillOpacity = Attr(map, "fo", 1f);
        node.Feather = Attr(map, "fea", -1f);

        if (HasKey(map, "fea_edge"))
            node.EdgeFeather = Attr(map, "fea_edge", -1f);
    }

    /// <summary>Reads gradient (GL/GR) and clip-path (CP) declarations.</summary>
    private static void ParseDefs(SS.UiValue? defs, VecScene scene)
    {
        if (defs == null || defs.Value.Type != SS.UiValueType.Array || defs.Value.Array == null)
            return;

        foreach (var entry in defs.Value.Array)
        {
            if (entry.Type != SS.UiValueType.Map || entry.Map == null)
                continue;

            var map = entry.Map;
            var op = PropString(map, "op")?.ToUpperInvariant();
            var id = PropString(map, "id");

            if (string.IsNullOrEmpty(id))
                continue;

            switch (op)
            {
                case "GL":
                case "GR":
                {
                    // `units = "bbox"` (SVG's objectBoundingBox): coordinates become 0..1
                    // fractions of whatever shape the gradient fills, so the ramp tracks
                    // moving geometry without any per-frame evaluation.
                    var bbox = string.Equals(PropString(map, "units"), "bbox", StringComparison.OrdinalIgnoreCase);
                    var gradient = new Gradient { Radial = op == "GR", BoundingBox = bbox };

                    if (gradient.Radial)
                    {
                        var cx = PropNumber(map, "cx", 0f);
                        var cy = PropNumber(map, "cy", 0f);
                        gradient.Radius = Mathf.Max(0.0001f, PropNumber(map, "r", 1f));
                        gradient.Start = new Vector2(cx, cy);
                        gradient.Focus = new Vector2(PropNumber(map, "fx", cx), PropNumber(map, "fy", cy));
                    }
                    else
                    {
                        gradient.Start = new Vector2(PropNumber(map, "x1", 0f), PropNumber(map, "y1", 0f));
                        gradient.End = new Vector2(PropNumber(map, "x2", 0f), PropNumber(map, "y2", 1f));
                    }

                    ReadStops(map, gradient);
                    scene.Gradients[id!] = gradient;
                    break;
                }

                case "CP":
                {
                    var outline = ClipOutline(map);
                    if (outline is { Count: >= 3 })
                        scene.Clips[id!] = outline;
                    else
                        scene.Problem($"clip \"{id}\" has no usable shape (must be a convex R, C or Y)");

                    break;
                }
            }
        }
    }

    /// <summary>
    /// Stops are pairs: <c>stops = {{0, "#fff"}, {1, "#000"}}</c>.
    /// </summary>
    private static void ReadStops(SS.UiProp[] map, Gradient gradient)
    {
        var stops = PropValue(map, "stops");
        if (stops == null || stops.Value.Type != SS.UiValueType.Array || stops.Value.Array == null)
            return;

        var positions = new List<float>();
        var colours = new List<string>();

        foreach (var stop in stops.Value.Array)
        {
            if (stop.Type != SS.UiValueType.Array || stop.Array == null || stop.Array.Length < 2)
                continue;

            if (stop.Array[0].Type != SS.UiValueType.Number || stop.Array[1].Type != SS.UiValueType.String)
                continue;

            positions.Add(stop.Array[0].Number);
            colours.Add(stop.Array[1].String ?? string.Empty);
        }

        GradientParser.ReadStops(positions.ToArray(), colours.ToArray(), gradient);
    }

    /// <summary>
    /// Builds a clip outline in scene coordinates from a CP declaration's shape.
    /// </summary>
    /// <remarks>
    /// Clip shapes are static geometry: they are evaluated once here with an empty context,
    /// so an expression referencing <c>t</c> in a clip is silently constant. Spec §7 treats
    /// clips as layout, not animation.
    /// </remarks>
    private static List<Vector2>? ClipOutline(SS.UiProp[] map)
    {
        var children = PropValue(map, "c");
        if (children == null || children.Value.Type != SS.UiValueType.Array || children.Value.Array == null)
            return null;

        foreach (var item in children.Value.Array)
        {
            if (item.Type != SS.UiValueType.Map || item.Map == null)
                continue;

            var node = ParseNode(item.Map, new VecScene());
            if (node != null)
                return Tessellator.Outline(node, new EvalContext());
        }

        return null;
    }

    private static void ParseStroke(SS.UiProp[] map, VecNode node)
    {
        var paint = PropValue(map, "s");
        if (paint?.Type == SS.UiValueType.Map && paint.Value.Map != null)
        {
            var name = PropString(paint.Value.Map, "grad");
            if (!string.IsNullOrEmpty(name))
            {
                node.HasStroke = true;
                node.StrokeGradient = name;
                node.StrokeGradientAt = Attr(paint.Value.Map, "at", 0f);
                ReadStrokeStyle(map, node);
            }

            return;
        }

        var stroke = PropString(map, "s");
        if (string.IsNullOrEmpty(stroke) || string.Equals(stroke, "none", StringComparison.OrdinalIgnoreCase))
            return;

        // Gradient / data paints are spec'd but unimplemented; white keeps the line visible.
        if (stroke![0] == '@')
        {
            node.HasStroke = true;
            node.StrokeGradient = stroke[1..];
        }
        else if (stroke[0] == '$')
        {
            node.HasStroke = true;
            node.StrokeData = stroke[1..];
        }
        else if (ColorUtility.TryParseHtmlString(stroke, out var colour))
        {
            node.HasStroke = true;
            node.StrokeColour = colour;
        }
        else
        {
            return;
        }

        ReadStrokeStyle(map, node);
    }

    private static void ReadStrokeStyle(SS.UiProp[] map, VecNode node)
    {
        node.StrokeWidth = Attr(map, "sw", 1f);
        node.StrokeOpacity = Attr(map, "so", 1f);
        node.MiterLimit = PropNumber(map, "ml", 4f);
        node.DashPattern = Numbers(map, "dash");
        node.DashOffset = Attr(map, "dofs", 0f);

        node.Cap = PropString(map, "cap")?.ToUpperInvariant() switch
        {
            "ROUND" => CapStyle.Round,
            "SQUARE" => CapStyle.Square,
            _ => CapStyle.Butt,
        };

        node.Join = PropString(map, "join")?.ToUpperInvariant() switch
        {
            "ROUND" => JoinStyle.Round,
            "BEVEL" => JoinStyle.Bevel,
            _ => JoinStyle.Miter,
        };
    }

    /// <summary>Reads a flat array of numbers, used for point lists and dash patterns.</summary>
    private static float[] Numbers(SS.UiProp[] map, string key)
    {
        var value = PropValue(map, key);
        if (value == null || value.Value.Type != SS.UiValueType.Array || value.Value.Array == null)
            return System.Array.Empty<float>();

        var source = value.Value.Array;
        var numbers = new float[source.Length];
        for (var i = 0; i < source.Length; i++)
            numbers[i] = source[i].Type == SS.UiValueType.Number ? source[i].Number : 0f;

        return numbers;
    }

    private static FitMode ParseFit(string? fit)
    {
        return fit?.ToUpperInvariant() switch
        {
            "CONTAIN" => FitMode.Contain,
            "COVER" => FitMode.Cover,
            _ => FitMode.Stretch,
        };
    }

    /// <summary>Compiles one attribute: a number stays constant, a string is parsed.</summary>
    private static Expression Attr(SS.UiProp[] map, string key, float fallback)
    {
        var value = PropValue(map, key);
        if (value == null)
            return Expression.Constant(fallback);

        return value.Value.Type switch
        {
            SS.UiValueType.Number => Expression.Constant(value.Value.Number),
            SS.UiValueType.String when !string.IsNullOrEmpty(value.Value.String) =>
                Expression.Parse(value.Value.String, fallback),
            _ => Expression.Constant(fallback),
        };
    }

    /// <summary>Reads one component of a two-element array attribute such as <c>t</c> or <c>s</c>.</summary>
    private static Expression Pair(SS.UiProp[] map, string key, int index, float fallback)
    {
        var value = PropValue(map, key);
        if (value == null || value.Value.Type != SS.UiValueType.Array || value.Value.Array == null)
            return Expression.Constant(fallback);

        var array = value.Value.Array;
        if (index >= array.Length)
            return Expression.Constant(fallback);

        return array[index].Type switch
        {
            SS.UiValueType.Number => Expression.Constant(array[index].Number),
            SS.UiValueType.String when !string.IsNullOrEmpty(array[index].String) =>
                Expression.Parse(array[index].String, fallback),
            _ => Expression.Constant(fallback),
        };
    }

    private static bool HasKey(SS.UiProp[] map, string key)
    {
        return PropValue(map, key) != null;
    }

    private static SS.UiValue? PropValue(SS.UiProp[] props, string key)
    {
        if (props == null)
            return null;

        foreach (var prop in props)
        {
            if (string.Equals(prop.Key, key, StringComparison.OrdinalIgnoreCase))
                return prop.Value;
        }

        return null;
    }

    private static string? PropString(SS.UiProp[] props, string key)
    {
        var value = PropValue(props, key);
        return value?.Type == SS.UiValueType.String ? value.Value.String : null;
    }

    private static float PropNumber(SS.UiProp[] props, string key, float fallback)
    {
        var value = PropValue(props, key);
        return value?.Type == SS.UiValueType.Number ? value.Value.Number : fallback;
    }
}
