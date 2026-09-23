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
    Text,
    Scroll,
    Image,
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

    /// <summary>`SC` content height, in scene units. Below `H` means nothing scrolls.</summary>
    internal Expression ContentH = Expression.Constant(0f);
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

    /// <summary>CSS-style drop shadows, drawn beneath the shape in declaration order.</summary>
    internal VecShadow[]? Shadows;

    /// <summary>`m=[a,b,c,d,e,f]` on a group: CSS matrix(), composed after t r s.</summary>
    internal float[]? GroupMatrix;

    /// <summary>`bri con sat hue gray sep inv` on a group, in the order written.</summary>
    internal List<(int Op, Expression Amount)>? Filters;

    /// <summary>`mask=@gradient` on a group: every colour under it takes the gradient's alpha.</summary>
    internal string? MaskGradient;

    /// <summary>`so` / `sov` on SC: a forced scroll offset, applied once per new version.</summary>
    internal Expression? ScrollSet;
    internal Expression? ScrollSetVersion;

    /// <summary>`IMG`: source URL and object-fit (0 fill, 1 contain, 2 cover, 3 none, 4 scale-down).</summary>
    internal string? ImageSource;
    internal int ImageFit;

    /// <summary>`uv=[u0,v0,u1,v1]` on IMG: the part of the texture shown, v from the top.</summary>
    internal Rect ImageCrop = new(0f, 0f, 1f, 1f);

    /// <summary>
    /// `at = { ax, ay }`: where the picture sits in whatever room `fit` leaves it, as
    /// fractions of the free space. Centred by default, which is what every image did before
    /// this existed.
    /// </summary>
    internal Expression ImageAtX = Expression.Constant(0.5f);

    internal Expression ImageAtY = Expression.Constant(0.5f);

    /// <summary>`off = { ox, oy }`: scene units added after `at` places the picture.</summary>
    internal Expression ImageOffX = Expression.Constant(0f);

    internal Expression ImageOffY = Expression.Constant(0f);

    /// <summary>`tile = { tw, th }`: the picture repeated at that size; 0 is its natural size. Null untiled.</summary>
    internal Expression? ImageTileW;

    internal Expression? ImageTileH;

    /// <summary>`tile = "contain"` (1) or `"cover"` (2): the tile's size fitted to the box. 0 for a size.</summary>
    internal int ImageTileFit;

    /// <summary>`rep = { x, y }` per axis: 0 repeat, 1 once, 2 round, 3 space -- CSS background-repeat.</summary>
    internal int ImageRepeatX;

    internal int ImageRepeatY;

    /// <summary>`slice = { t, r, b, l }`: nine-slice insets in texels of the (cropped) picture. Null unsliced.</summary>
    internal float[]? ImageSlice;

    /// <summary>`bw = { t, r, b, l }`: the drawn border widths in scene units; defaults to the slice.</summary>
    internal float[]? ImageBorder;

    /// <summary>
    /// `srep = { h, v }`: how a nine-slice's edges and middle fill their length, CSS
    /// `border-image-repeat` -- 4 stretch (default), 0 repeat, 2 round, 3 space. `h` is the top and
    /// bottom edges and the middle across; `v` the left and right edges and the middle down.
    /// </summary>
    internal int ImageSliceRepeatX = 4;

    internal int ImageSliceRepeatY = 4;

    /// <summary>`mid = 0` leaves a nine-slice's middle undrawn, CSS border-image without `fill`.</summary>
    internal bool ImageMiddle = true;

    /// <summary>`fl="..."` on T: first-line overrides.</summary>
    internal FirstLineStyle? FirstLine;

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

    /// <summary>Index expressions for `f`/`s` bound to a colour ARRAY; null for a scalar.</summary>
    internal Expression? FillIndex;

    internal Expression? StrokeIndex;

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

    /// <summary>
    /// Whether this node's OWN values read `t`, children aside. What decides animation is
    /// whether a rebuild actually reached such a node: one inside a hidden group draws nothing
    /// and must not keep the scene rebuilding.
    /// </summary>
    internal bool SelfUsesTime;

    /// <summary>
    /// A group whose only animation is its own `o`, over `t` alone, above static content with
    /// no text: it is drawn at full opacity into a mesh of its own and faded by the renderer
    /// every frame, so a blinking dot costs no rebuilds at all.
    /// </summary>
    internal bool AlphaOnly;

    internal bool UsesScroll;

    /// <summary>The node's <c>id</c>, when it has one. Patch target and, later, hit target.</summary>
    internal string? Id;

    /// <summary>
    /// The props this node was built from, kept only for identified nodes so a patch can be
    /// merged onto them and the node re-parsed exactly.
    /// </summary>
    internal SS.UiProp[]? SourceProps;

    // --- T only -------------------------------------------------------------
    internal string? TextLiteral;
    internal string? TextData;

    /// <summary>Index expression for `text = "$rows[i]"`; null for a plain `$name`.</summary>
    internal Expression? TextIndex;

    /// <summary>
    /// A literal `text` with `{$name}` / `{$name:%.1f}` placeholders, split once at parse. Null
    /// for a text with none, which keeps the plain literal and `$name` paths.
    /// </summary>
    internal TextPart[]? TextParts;

    /// <summary>printf-style numeric format for a bound NUMBER, e.g. "%.1f".</summary>
    internal string? TextFormat;

    /// <summary>Literal suffix appended after the formatted number.</summary>
    internal string? TextUnit;

    /// <summary>`ow`: text outline width in scene units, centred on the glyph edge. Null for none.</summary>
    internal Expression? TextOutlineWidth;

    /// <summary>`oc`: text outline colour; black by default.</summary>
    internal Color TextOutlineColour = Color.black;

    /// <summary>What to render when the bound value is absent. Defaults to "--".</summary>
    internal string TextMissing = "--";

    /// <summary>`wrap = 1`: let the text run to more than one line inside its box.</summary>
    internal bool Wrap;

    /// <summary>`lh`: line height as a multiple of the font size. 0 leaves the font's own.</summary>
    internal float LineHeight;
    internal Expression? TextSize;
    internal string? FontFamily;
    internal bool Bold;
    internal float CharSpacing;
    internal int Align;
    internal int VAlign;
    internal int Fit;
    internal Expression? MinSize;

    /// <summary>
    /// Per-corner radii from `rx = [tl, tr, br, bl]`, CSS order. Null for a uniform `rx`.
    /// </summary>
    internal Expression[]? CornerRadii;

    /// <summary>
    /// True when the node opts into hit testing with <c>click = 1</c>.
    /// </summary>
    /// <remarks>
    /// Explicit rather than "any node with an id", because an id is also how a node is
    /// patched, and patch targets are common — making every one of them swallow clicks would
    /// be a surprise.
    /// </remarks>
    internal bool Clickable;

    /// <summary>
    /// `v` on a group: 0 takes the whole subtree out of the drawing, hit regions and all.
    /// Null when the group never said, which is every group that does not ask for it.
    /// </summary>
    /// <remarks>
    /// CSS `visibility: hidden`, where `o = 0` is `opacity: 0` — both invisible, but only
    /// this one stops being there. Kept separate rather than folded into opacity because a
    /// page that fades something out and still wants it clickable is the ordinary case, and
    /// it is the one a browser serves.
    /// </remarks>
    internal Expression? Visible;

    /// <summary>
    /// True when this node or anything under it does something a rebuild has to reach even
    /// when nothing of it is visible: a hit region, a scroll container, a picture.
    /// </summary>
    /// <remarks>
    /// What lets a fully transparent group be skipped whole. A browser still sends clicks to
    /// an `opacity: 0` element, so dropping the walk for a hidden subtree that owns one would
    /// be a behaviour change rather than an optimisation.
    /// </remarks>
    internal bool Interactive;
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

    internal bool UsesScroll;

    /// <summary>
    /// Nodes carrying an <c>id</c>, with where they sit and the props they were built from,
    /// so a patch can re-parse one in place.
    /// </summary>
    /// <remarks>
    /// The props are kept because a patch has to be a MERGE, not a partial apply: re-running
    /// the shape parser over only the changed keys would reset every key the patch did not
    /// mention back to its default. Merging onto the original and re-parsing is exact, and it
    /// costs one prop array per identified node -- not per node.
    /// </remarks>
    internal readonly Dictionary<string, (List<VecNode> List, int Index, SS.UiProp[] Props)> Identified
        = new(StringComparer.Ordinal);

    /// <summary>
    /// Symbol bodies declared in <c>defs</c>, by id: the children a <c>USE</c> instantiates.
    /// </summary>
    internal readonly Dictionary<string, (SS.UiValue[] Body, SS.UiProp[] Params)> Symbols
        = new(StringComparer.Ordinal);

    /// <summary>Gradients declared in <c>defs</c>, by id.</summary>
    internal readonly Dictionary<string, Gradient> Gradients = new(StringComparer.Ordinal);

    /// <summary>Clip outlines declared in <c>defs</c>, by id, in scene coordinates.</summary>
    internal readonly Dictionary<string, List<Vector2>> Clips = new(StringComparer.Ordinal);

    /// <summary>
    /// Clip shapes whose geometry is expressions, by id: re-cut once per rebuild.
    /// </summary>
    internal readonly Dictionary<string, VecNode> ClipNodes = new(StringComparer.Ordinal);

    /// <summary>True when any clip or gradient in <c>defs</c> has to be re-read per rebuild.</summary>
    internal bool LiveDefs;

    /// <summary>Whether anything in <c>defs</c> references <c>t</c>.</summary>
    /// <remarks>
    /// Kept apart from <see cref="UsesTime"/> because that one is recomputed from the root
    /// nodes whenever a patch lands, and a defs declaration is not a root node.
    /// </remarks>
    internal bool DefsUseTime;

    /// <summary>Whether a gradient def reads `sy` or `vh`, and so has to follow scrolling.</summary>
    internal bool DefsUseScroll;

    /// <summary>
    /// Re-reads the live parts of <c>defs</c> for this rebuild: clip outlines that are
    /// expressions, gradient geometry and gradient stops bound to the payload.
    /// </summary>
    /// <remarks>
    /// Once per rebuild, before the walk, so everything downstream reads the same resolved
    /// numbers a static declaration would have parsed into. A scene with no live defs — the
    /// usual case — pays one boolean test.
    /// </remarks>
    internal void ResolveLiveDefs(EvalContext context)
    {
        if (!LiveDefs)
            return;

        foreach (var pair in ClipNodes)
            Clips[pair.Key] = Tessellator.Outline(pair.Value, context);

        foreach (var gradient in Gradients.Values)
            gradient.Resolve(context);
    }

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

    /// <summary>
    /// Labels obey scene order rather than all drawing on top. **On by default**; `ztext = 0`
    /// restores the old behaviour.
    /// </summary>
    /// <remarks>
    /// This shipped opt-in and the default was flipped the same day, after measuring what it
    /// actually costs a scene that never overlaps anything: **0.19 ms per 40,000 vertices** on
    /// .NET 8, so roughly half a millisecond under Mono on a dense console — about 2.5% of one
    /// tessellation, and off the frame, since the whole rebuild runs on a worker.
    ///
    /// Against that, opt-in meant every caller had to remember a flag to get the behaviour the
    /// scene already describes by declaration order. A shape declared after a label covering it
    /// is not a surprise; a shape declared after a label NOT covering it was.
    ///
    /// Nothing is silently reinterpreted either: a cut is forced only where a later shape's
    /// bounds genuinely overlap the label's, which a well-authored scene mostly avoids by
    /// construction. The `12-click.lua` example has seven labels and nineteen shapes and forces
    /// zero cuts, because its rows, pills and accent bars were already placed not to collide.
    ///
    /// `ztext = 0` is kept for the one case that wants the old rule: text floating above
    /// artwork drawn after it, deliberately.
    /// </remarks>
    internal bool TextInOrder;

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

    /// <summary>
    /// The text each label carried last rebuild, by its place in the rebuild's label order.
    /// </summary>
    /// <remarks>
    /// A readout that prints the same characters as last time reuses last time's string instead
    /// of making a new one: measured at 88 bytes a label per rebuild otherwise, paid on every
    /// frame of an animating console for labels whose text had not changed. Keyed by order,
    /// which is also how the label pool is keyed, so a scene whose labels shift just misses
    /// and makes the string -- never shows the wrong one, since the characters are compared.
    /// Touched only by this scene's own rebuild, and a surface runs one at a time.
    /// </remarks>
    internal readonly List<string?> TextCache = new();

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
            TextInOrder = PropNumber(props, "ztext", 1f) > 0.5f,
            DebugNoFeather = PropNumber(props, "nofeather", 0f) > 0.5f,
            DebugNoEval = PropNumber(props, "noeval", 0f) > 0.5f,
        };

        // Malformed expressions are the parser's business, not the log's alone.
        Expression.Report = message => scene.Problem(message);

        ParseDefs(PropValue(props, "defs"), scene);

        foreach (var child in ParseNodes(root.Value, scene))
            scene.Root.Add(child);

        foreach (var node in scene.Root)
        {
            scene.UsesTime |= node.UsesTime;
            scene.UsesScroll |= node.UsesScroll;
        }

        // A clip or gradient that animates is as much a reason to rebuild as a node that does.
        scene.UsesTime |= scene.DefsUseTime;
        scene.UsesScroll |= scene.DefsUseScroll;

        Reindex(scene);
        Expression.Report = null;

        return scene;
    }

    /// <summary>Reads the paired data element's values into an evaluation context.</summary>
    /// <summary>
    /// Applies `nodes = { id = { field = value } }` from the data element to a scene.
    /// </summary>
    /// <remarks>
    /// Returns true when anything changed, so the caller can skip a rebuild otherwise.
    ///
    /// This is a prop on the DATA element rather than a method on the element handle. The
    /// handle belongs to ScriptedScreens; this mod is a postfix on ApplyElementInternal and
    /// sees props, never method calls, so `handle:set_node(...)` is not a thing it can add.
    ///
    /// A patch is merged onto the node's original props and the node re-parsed, not applied
    /// key by key: a partial apply would reset every key the patch did not mention.
    /// </remarks>
    internal static bool PatchNodes(SS.UiProp[] props, VecScene scene)
    {
        var patch = PropValue(props, "nodes");
        if (patch?.Type != SS.UiValueType.Map || patch.Value.Map == null)
            return false;

        var changed = false;

        foreach (var entry in patch.Value.Map)
        {
            if (string.IsNullOrEmpty(entry.Key)
                || entry.Value.Type != SS.UiValueType.Map || entry.Value.Map == null)
            {
                continue;
            }

            if (!scene.Identified.TryGetValue(entry.Key, out var target))
            {
                scene.Problems.Add($"patch for unknown node id \"{entry.Key}\"");
                continue;
            }

            var merged = Merge(target.Props, entry.Value.Map);

            var replacement = ParseNode(merged, scene);
            if (replacement == null)
                continue;

            // Children are not patchable and are kept as they were, so a patch on a group
            // does not cost a re-parse of everything under it.
            var previous = target.List[target.Index];
            if (replacement.Children.Count == 0 && previous.Children.Count > 0)
                replacement.Children.AddRange(previous.Children);

            replacement.Id = entry.Key;
            replacement.SourceProps = merged;

            target.List[target.Index] = replacement;
            changed = true;
        }

        if (changed)
        {
            Reindex(scene);

            // A patch can introduce or remove a `t` or `sy` reference, and the rebuild gate
            // reads these off the scene.
            scene.UsesTime = scene.DefsUseTime;
            scene.UsesScroll = scene.DefsUseScroll;

            foreach (var node in scene.Root)
            {
                scene.UsesTime |= node.UsesTime;
                scene.UsesScroll |= node.UsesScroll;
            }
        }

        return changed;
    }

    /// <summary>Patch keys win; everything else is kept from the original.</summary>
    private static SS.UiProp[] Merge(SS.UiProp[] original, SS.UiProp[] patch)
    {
        var merged = new List<SS.UiProp>(original.Length + patch.Length);

        foreach (var prop in original)
        {
            var overridden = false;
            foreach (var change in patch)
            {
                if (string.Equals(prop.Key, change.Key, StringComparison.OrdinalIgnoreCase))
                {
                    overridden = true;
                    break;
                }
            }

            if (!overridden)
                merged.Add(prop);
        }

        merged.AddRange(patch);
        return merged.ToArray();
    }

    internal static void ReadData(SS.UiProp[] props, EvalContext into)
    {
        into.Scalars.Clear();
        into.Arrays.Clear();
        into.Colours.Clear();
        into.Strings.Clear();
        into.StringArrays.Clear();
        into.ColourArrays.Clear();
        into.Snapped.Clear();
        into.Eased.Clear();
        into.KeepUnmentioned = PropNumber(props, "keep", 0f) > 0.5f;

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

                case SS.UiValueType.String when entry.Value.String != null:
                    // Stored as a string whether or not it is also a colour: a scene may
                    // want to display "#FF0000" as text. An empty string is a value too -- it
                    // clears a label -- where skipping it left the previous text showing.
                    into.Strings[entry.Key] = entry.Value.String;

                    if (entry.Value.String.Length > 0
                        && ColorUtility.TryParseHtmlString(entry.Value.String, out var dataColour))
                        into.Colours[entry.Key] = dataColour;

                    break;

                case SS.UiValueType.Array when entry.Value.Array != null:
                {
                    var source = entry.Value.Array;

                    // An array of strings is a different animal from an array of numbers and
                    // is stored separately. Deciding by the FIRST element rather than
                    // per-element: a payload mixing the two is a mistake in the script, and
                    // guessing per slot would make it render as half a list.
                    if (source.Length > 0 && source[0].Type == SS.UiValueType.String)
                    {
                        var text = new string[source.Length];
                        var colours = new Color[source.Length];
                        var anyColour = false;

                        for (var i = 0; i < source.Length; i++)
                        {
                            text[i] = source[i].String ?? string.Empty;

                            // Stored as both, exactly as a scalar string is: a scene may want
                            // to paint with "#FF0000" and another to print it.
                            if (ColorUtility.TryParseHtmlString(text[i], out var parsed))
                            {
                                colours[i] = parsed;
                                anyColour = true;
                            }
                        }

                        into.StringArrays[entry.Key] = text;
                        if (anyColour)
                            into.ColourArrays[entry.Key] = colours;

                        break;
                    }

                    var values = new float[source.Length];
                    for (var i = 0; i < source.Length; i++)
                        values[i] = source[i].Type == SS.UiValueType.Number ? source[i].Number : 0f;

                    into.Arrays[entry.Key] = values;
                    break;
                }
            }
        }

        ReadEasing(props, into);

        // `snap = 1`: this payload's numbers apply at once instead of easing in. It wins over
        // any timing the same payload states, because "at once" and "over 0.6 s" cannot both
        // be true and the shorter answer is the one that cannot surprise anyone.
        if (PropNumber(props, "snap", 0f) > 0.5f)
        {
            foreach (var name in into.Scalars.Keys)
                into.Snapped.Add(name);
            foreach (var name in into.Arrays.Keys)
                into.Snapped.Add(name);

            into.Eased.Clear();
        }
    }

    /// <summary>
    /// `ease = { name = { seconds, "curve" } }` on the data element: how long that value's
    /// glide takes and the curve it follows.
    /// </summary>
    /// <remarks>
    /// Also accepts `name = seconds` for a linear glide of a stated length, which is the form
    /// most hand-written consoles want -- "move this needle over a quarter of a second" needs
    /// no curve named -- and an optional third element, a delay in seconds, during which the
    /// value holds where it is before the glide begins.
    ///
    /// Nothing here is required: a name without an entry keeps the behaviour every scene has
    /// had, which is to glide across the measured gap between payloads.
    /// </remarks>
    private static void ReadEasing(SS.UiProp[] props, EvalContext into)
    {
        var timings = PropValue(props, "ease");
        if (timings?.Type != SS.UiValueType.Map || timings.Value.Map == null)
            return;

        foreach (var entry in timings.Value.Map)
        {
            if (string.IsNullOrEmpty(entry.Key))
                continue;

            var seconds = 0f;
            var curve = Easing.Linear;
            var delay = 0f;

            switch (entry.Value.Type)
            {
                case SS.UiValueType.Number:
                    seconds = entry.Value.Number;
                    break;

                case SS.UiValueType.Array when entry.Value.Array is { Length: > 0 }:
                {
                    var parts = entry.Value.Array;
                    if (parts[0].Type != SS.UiValueType.Number)
                        continue;

                    seconds = parts[0].Number;

                    if (parts.Length > 1 && parts[1].Type == SS.UiValueType.String)
                        curve = Easing.Parse(parts[1].String, message => ScriptedScreensVectorPlugin.Log?.LogWarning($"ease \"{entry.Key}\": {message}"));

                    if (parts.Length > 2 && parts[2].Type == SS.UiValueType.Number)
                        delay = Mathf.Max(0f, parts[2].Number);

                    break;
                }

                default:
                    continue;
            }

            // A zero or negative duration is a snap, and saying so here means the glide code
            // never has to divide by it.
            if (seconds <= 0f)
            {
                into.Snapped.Add(entry.Key);
                into.Eased.Remove(entry.Key);
                continue;
            }

            into.Eased[entry.Key] = (seconds, curve, delay);
        }
    }

    /// <summary>
    /// Rebuilds the id index by walking the finished tree.
    /// </summary>
    /// <remarks>
    /// Done after the tree is assembled, not during parsing: ParseNodes returns a temporary
    /// list that callers copy out of, so anything recorded against that list would point at
    /// a collection nobody keeps, and a patch would silently write nowhere.
    /// </remarks>
    private static void Reindex(VecScene scene)
    {
        scene.Identified.Clear();
        Reindex(scene, scene.Root);
    }

    private static void Reindex(VecScene scene, List<VecNode> list)
    {
        for (var i = 0; i < list.Count; i++)
        {
            var node = list[i];

            if (!string.IsNullOrEmpty(node.Id) && node.SourceProps != null)
                scene.Identified[node.Id!] = (list, i, node.SourceProps);

            if (node.Children.Count > 0)
                Reindex(scene, node.Children);
        }
    }

    private static List<VecNode> ParseNodes(SS.UiValue array, VecScene scene, SS.UiProp[]? inherited = null)
    {
        var nodes = new List<VecNode>();
        if (array.Array == null)
            return nodes;

        foreach (var item in array.Array)
        {
            if (item.Type != SS.UiValueType.Map || item.Map == null)
                continue;

            // Inherited defaults lose to anything the node states itself, which is what
            // makes `style` a default rather than an override.
            var map = inherited == null ? item.Map : Merge(inherited, item.Map);

            var node = ParseNode(map, scene, inherited);
            if (node != null)
                nodes.Add(node);
        }

        return nodes;
    }

    /// <summary>
    /// Replaces `%name` references in a symbol body with the instance's parameters.
    /// </summary>
    /// <remarks>
    /// Textual, at parse time, rather than a scoped variable in the evaluator. A scope would
    /// mean push/pop around every instance the way repeats do, for something that never
    /// changes after parsing -- symbol parameters are structure, not animation. Substituting
    /// once means an instance costs exactly what writing the nodes out would have.
    ///
    /// `%name` alone as a value takes the parameter's type, so a number stays a number.
    /// Inside a longer string it is spliced textually, which is what makes it work in
    /// expressions: `y="=%top+i*4"`.
    /// </remarks>
    private static SS.UiValue[] Substitute(SS.UiValue[] body, SS.UiProp[] args)
    {
        var output = new SS.UiValue[body.Length];
        for (var i = 0; i < body.Length; i++)
            output[i] = Substitute(body[i], args);

        return output;
    }

    private static SS.UiValue Substitute(SS.UiValue value, SS.UiProp[] args)
    {
        switch (value.Type)
        {
            case SS.UiValueType.String when !string.IsNullOrEmpty(value.String):
            {
                var text = value.String!;

                // A whole-value reference keeps the parameter's own type.
                if (text.Length > 1 && text[0] == '%')
                {
                    foreach (var arg in args)
                    {
                        if (string.Equals(arg.Key, text[1..], StringComparison.OrdinalIgnoreCase))
                            return arg.Value;
                    }
                }

                if (text.IndexOf('%', StringComparison.Ordinal) < 0)
                    return value;

                foreach (var arg in args)
                    text = text.Replace("%" + arg.Key, Textual(arg.Value), StringComparison.OrdinalIgnoreCase);

                return new SS.UiValue { Type = SS.UiValueType.String, String = text };
            }

            case SS.UiValueType.Array when value.Array != null:
                return new SS.UiValue { Type = SS.UiValueType.Array, Array = Substitute(value.Array, args) };

            case SS.UiValueType.Map when value.Map != null:
            {
                var map = new SS.UiProp[value.Map.Length];
                for (var i = 0; i < map.Length; i++)
                    map[i] = new SS.UiProp { Key = value.Map[i].Key, Value = Substitute(value.Map[i].Value, args) };

                return new SS.UiValue { Type = SS.UiValueType.Map, Map = map };
            }

            default:
                return value;
        }
    }

    private static string Textual(SS.UiValue value)
    {
        return value.Type switch
        {
            SS.UiValueType.Number => value.Number.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture),
            SS.UiValueType.String => value.String ?? string.Empty,
            _ => string.Empty,
        };
    }

    /// <summary>A group's `style` map, merged onto whatever it inherited.</summary>
    /// <summary>Paint keys a `G` may carry directly, as defaults for its subtree.</summary>
    /// <remarks>
    /// The text format has no map syntax, so `style = { … }` cannot be written there at all.
    /// Rather than invent one, a text-form group carries its defaults as ordinary attributes,
    /// exactly the way `SYM` already carries its parameter defaults:
    ///
    /// <code>G fea=0 f=#c6c6c8 { … }</code>
    ///
    /// This is a whitelist, not "every key on the group": a `G` legitimately owns `t`, `r`,
    /// `s`, `a`, `o` and `clip`, and inheriting those to every child would apply each
    /// transform twice. Only paint keys, which a group has no use for itself, are eligible.
    /// </remarks>
    private static readonly string[] GroupPaintDefaults =
    {
        "f", "fo", "fea", "fea_edge", "fr", "sh",
        "s_", "so", "sw", "cap", "join", "ml", "dash", "dofs",
        "size", "font", "weight", "cspace", "align", "valign", "fit", "min_size",
    };

    private static SS.UiProp[]? ParseStyle(SS.UiProp[] map, SS.UiProp[]? inherited)
    {
        var style = PropValue(map, "style");
        var declared = style?.Type == SS.UiValueType.Map ? style.Value.Map : null;

        // Paint keys written straight on the group, for the text format's benefit. Collected
        // even when `style` is also present, with `style` winning: it is the explicit form.
        List<SS.UiProp>? direct = null;
        foreach (var prop in map)
        {
            if (string.IsNullOrEmpty(prop.Key))
                continue;

            var key = prop.Key;

            // `s` is stroke colour on a shape and SCALE on a group, so it cannot be
            // inherited from a bare attribute without breaking every scaled group that
            // exists. `s_` is the way to say stroke colour as a group default.
            if (string.Equals(key, "s_", StringComparison.Ordinal))
                key = "s";
            else if (System.Array.IndexOf(GroupPaintDefaults, key) < 0)
                continue;

            (direct ??= new List<SS.UiProp>()).Add(new SS.UiProp { Key = key, Value = prop.Value });
        }

        var own = direct == null
            ? declared
            : declared == null ? direct.ToArray() : Merge(direct.ToArray(), declared);

        if (own == null)
            return inherited;

        return inherited == null ? own : Merge(inherited, own);
    }

    /// <summary>
    /// Every attribute name the renderer reads, across all ops.
    /// </summary>
    /// <remarks>
    /// One union rather than a set per op. It catches the error that actually happens -- a
    /// typo, `fille` or `strke` -- which otherwise vanishes silently because unknown keys are
    /// ignored by design. It does NOT catch a real key on the wrong op, `rx` on a band say;
    /// that needs per-op sets and risks rejecting combinations that are merely unusual.
    /// </remarks>
    private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "op", "id", "c", "style", "click", "lod",
        "x", "y", "w", "h", "cx", "cy", "rx", "ry", "x1", "y1", "x2", "y2", "y2",
        "n", "p", "d", "seg", "t", "r", "s", "s_", "a", "o", "clip", "ref", "params", "ch",
        "f", "fo", "fo2", "fr", "fea", "fea_edge", "sh",
        "sw", "so", "cap", "join", "ml", "dash", "dofs", "sd", "sdo",
        "grad", "at", "units", "spread", "stops", "fx", "fy",
        "text", "size", "align", "valign", "font", "weight", "cspace", "fit", "min_size",
        "fmt", "unit", "missing", "wrap", "lh",
        "fat", "sat", "m", "bri", "con", "hue", "gray", "sep", "inv", "mask", "sov", "src", "fl", "uv", "v", "at", "off", "tile", "rep", "srep", "smp", "slice", "bw", "mid", "ow", "oc",
    };

    private static void Validate(SS.UiProp[] map, VecScene scene, string? op, string? id)
    {
        foreach (var prop in map)
        {
            if (string.IsNullOrEmpty(prop.Key) || KnownKeys.Contains(prop.Key))
                continue;

            // Symbol parameters are arbitrary by definition, so a USE is exempt.
            if (string.Equals(op, "USE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(op, "SYM", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            scene.Problem($"{op ?? "node"}{(string.IsNullOrEmpty(id) ? "" : " \"" + id + "\"")}: unknown attribute \"{prop.Key}\"");
        }
    }

    private static VecNode? ParseNode(SS.UiProp[] map, VecScene scene, SS.UiProp[]? inherited = null)
    {
        var op = PropString(map, "op");
        if (string.IsNullOrEmpty(op))
            return null;

        var node = new VecNode();

        switch (op!.ToUpperInvariant())
        {
            // USE instantiates a symbol as a group: the group carries the placement, the
            // symbol's body becomes its children with params substituted.
            case "USE":
            {
                node.Op = VecOp.Group;
                node.Tx = Attr(map, "x", 0f);
                node.Ty = Attr(map, "y", 0f);
                node.Opacity = Attr(map, "o", 1f);
                node.ClipRef = PropString(map, "clip");

                var reference = PropString(map, "ref");
                if (string.IsNullOrEmpty(reference) || !scene.Symbols.TryGetValue(reference!, out var symbol))
                {
                    scene.Problems.Add($"unknown symbol \"{reference}\"");
                    break;
                }

                // Params: the symbol's declared defaults, overridden by whatever the
                // instance passes. Everything on the USE is fair game as an override, so a
                // symbol can take `w` and `f` without declaring them specially.
                var args = Merge(symbol.Params, map);

                var instantiated = new SS.UiValue
                {
                    Type = SS.UiValueType.Array,
                    Array = Substitute(symbol.Body, args),
                };

                foreach (var child in ParseNodes(instantiated, scene, inherited))
                    node.Children.Add(child);

                break;
            }

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
                node.Visible = HasKey(map, "v") ? Attr(map, "v", 1f) : null;
                node.ClipRef = PropString(map, "clip");

                // CSS matrix(a,b,c,d,e,f), composed after t r s the way a transform list is.
                if (HasKey(map, "m"))
                {
                    var m = Numbers(map, "m");
                    if (m.Length == 6)
                        node.GroupMatrix = m;
                    else
                        scene.Problem($"G: m takes six numbers, {m.Length} given");
                }

                // Filters in the order written, since CSS applies a filter list in order.
                foreach (var prop in map)
                {
                    var filterOp = string.IsNullOrEmpty(prop.Key) ? -1 : ColourFilter.OpFor(prop.Key);
                    if (filterOp < 0)
                        continue;

                    node.Filters ??= new List<(int, Expression)>();
                    node.Filters.Add((filterOp, Attr(map, prop.Key, 1f)));
                }

                var mask = PropString(map, "mask");
                if (!string.IsNullOrEmpty(mask) && mask![0] == '@')
                    node.MaskGradient = mask[1..];
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

            // A group that clips to its own box and slides its children inside it. Scroll
            // position is client-side state keyed by `id`, so an SC without one draws but
            // never moves -- reported at rebuild, since that is where the offset is read.
            case "SC":
                node.Op = VecOp.Scroll;
                node.X = Attr(map, "x", 0f);
                node.Y = Attr(map, "y", 0f);
                node.W = Attr(map, "w", 0f);
                node.H = Attr(map, "h", 0f);
                node.ContentH = Attr(map, "ch", 0f);
                node.CornerRadii = ParseCorners(map);
                node.Rx = node.CornerRadii != null ? node.CornerRadii[0] : Attr(map, "rx", 0f);
                node.Ry = HasKey(map, "ry") ? Attr(map, "ry", 0f) : node.Rx;
                node.Opacity = Attr(map, "o", 1f);

                // A script set scrollTop: `sov` changes, `so` is applied once, and wheel and
                // drag own the offset again afterwards.
                if (HasKey(map, "so") && HasKey(map, "sov"))
                {
                    node.ScrollSet = Attr(map, "so", 0f);
                    node.ScrollSetVersion = Attr(map, "sov", 0f);
                }
                break;

            case "R":
                node.Op = VecOp.Rect;
                node.X = Attr(map, "x", 0f);
                node.Y = Attr(map, "y", 0f);
                node.W = Attr(map, "w", 0f);
                node.H = Attr(map, "h", 0f);
                node.CornerRadii = ParseCorners(map);
                node.Rx = node.CornerRadii != null ? node.CornerRadii[0] : Attr(map, "rx", 0f);
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

            case "T":
            {
                node.Op = VecOp.Text;
                node.X = Attr(map, "x", 0f);
                node.Y = Attr(map, "y", 0f);
                node.W = Attr(map, "w", 0f);
                node.H = Attr(map, "h", 0f);
                node.TextSize = Attr(map, "size", 12f);
                node.MinSize = Attr(map, "min_size", 6f);

                if (HasKey(map, "ow"))
                    node.TextOutlineWidth = Attr(map, "ow", 0f);

                var outline = PropString(map, "oc");
                if (outline != null)
                {
                    if (ColorUtility.TryParseHtmlString(outline, out var outlineColour))
                        node.TextOutlineColour = outlineColour;
                    else
                        scene.Problem($"T: oc \"{outline}\" is not a colour");
                }

                // `text` is either a literal or a $name binding, resolved per rebuild.
                var body = PropString(map, "text");
                if (!string.IsNullOrEmpty(body) && body![0] == '$')
                {
                    var bound = SplitBinding(body[1..]);
                    node.TextData = bound.Name;
                    node.TextIndex = bound.Index;
                }
                else
                {
                    node.TextLiteral = body;
                }

                // `fmt` turns the node into a NUMBER formatter: the chip sends the value it
                // already has in the payload and does no string work at all.
                node.TextFormat = PropString(map, "fmt");
                node.TextUnit = PropString(map, "unit");
                if (node.TextLiteral != null)
                    node.TextParts = TextTemplate(node.TextLiteral, node.TextFormat);

                var missing = PropString(map, "missing");
                if (missing != null)
                    node.TextMissing = missing;

                node.FontFamily = PropString(map, "font");
                node.Align = TextAlign.Horizontal(PropString(map, "align"));
                node.VAlign = TextAlign.Vertical(PropString(map, "valign"));
                node.Fit = TextFit.Parse(PropString(map, "fit"));
                node.CharSpacing = PropNumber(map, "cspace", 0f);

                // Off by default, and that is the deliberate half. A readout that grows a
                // second line pushes its own baseline up and shunts the layout around it,
                // which on a console reads as a rendering fault. A paragraph asks for it.
                node.Wrap = PropNumber(map, "wrap", 0f) > 0.5f;

                // A multiple of the font size, like CSS `line-height: 1.4`, not an absolute.
                // TMP's own lineSpacing is a percentage OFFSET from the font's natural line
                // height, so the conversion happens where it is applied rather than here.
                node.LineHeight = PropNumber(map, "lh", 0f);

                var weight = PropString(map, "weight");
                node.Bold = weight != null
                            && (weight.Equals("bold", StringComparison.OrdinalIgnoreCase)
                                || (float.TryParse(weight, out var numeric) && numeric >= 600f));

                var firstLine = PropString(map, "fl");
                if (!string.IsNullOrEmpty(firstLine))
                    node.FirstLine = FirstLineStyle.Parse(firstLine!, scene);

                // Text colour comes from `f`, like every other shape.
                break;
            }

            case "IMG":
                // A textured quad, drawn in scene order in a mesh of its own.
                node.Op = VecOp.Image;
                node.X = Attr(map, "x", 0f);
                node.Y = Attr(map, "y", 0f);
                node.W = Attr(map, "w", 0f);
                node.H = Attr(map, "h", 0f);
                node.Rx = Attr(map, "rx", 0f);
                node.Ry = HasKey(map, "ry") ? Attr(map, "ry", 0f) : node.Rx;
                node.Opacity = Attr(map, "o", 1f);
                node.ImageSource = PropString(map, "src");
                node.ImageFit = (PropString(map, "fit") ?? "fill").ToUpperInvariant() switch
                {
                    "CONTAIN" => 1,
                    "COVER" => 2,
                    "NONE" => 3,
                    "SCALE-DOWN" => 4,
                    _ => 0,
                };

                if (string.IsNullOrEmpty(node.ImageSource))
                    scene.Problem("IMG has no src");
                else if (string.Equals(PropString(map, "smp"), "point", StringComparison.OrdinalIgnoreCase))
                    node.ImageSource = ImageCache.PointPrefix + node.ImageSource;

                node.ImageAtX = Pair(map, "at", 0, 0.5f);
                node.ImageAtY = Pair(map, "at", 1, 0.5f);
                node.ImageOffX = Pair(map, "off", 0, 0f);
                node.ImageOffY = Pair(map, "off", 1, 0f);
                if (HasKey(map, "slice"))
                {
                    var slice = Numbers(map, "slice");
                    var border = HasKey(map, "bw") ? Numbers(map, "bw") : slice;
                    if (slice.Length == 4 && border.Length == 4 && System.Array.TrueForAll(slice, v => v >= 0f)
                        && System.Array.TrueForAll(border, v => v >= 0f))
                    {
                        node.ImageSlice = slice;
                        node.ImageBorder = border;
                        node.ImageMiddle = PropNumber(map, "mid", 1f) > 0.5f;

                        var srep = PropValue(map, "srep");
                        if (srep is { Type: SS.UiValueType.String })
                        {
                            node.ImageSliceRepeatX = node.ImageSliceRepeatY = RepeatMode(srep.Value.String, scene);
                        }
                        else if (srep is { Type: SS.UiValueType.Array, Array: { Length: 2 } modes })
                        {
                            node.ImageSliceRepeatX = RepeatMode(modes[0].String, scene);
                            node.ImageSliceRepeatY = RepeatMode(modes[1].String, scene);
                        }
                    }
                    else
                    {
                        scene.Problem("IMG: slice and bw take four numbers, top right bottom left, none negative");
                    }
                }

                if (HasKey(map, "tile"))
                {
                    var fit = PropString(map, "tile");
                    node.ImageTileFit = fit?.ToUpperInvariant() switch
                    {
                        "CONTAIN" => 1,
                        "COVER" => 2,
                        _ => 0,
                    };

                    node.ImageTileW = node.ImageTileFit != 0 ? Expression.Constant(0f) : Pair(map, "tile", 0, 0f);
                    node.ImageTileH = node.ImageTileFit != 0 ? Expression.Constant(0f) : Pair(map, "tile", 1, 0f);

                    // One word for both axes, or one per axis.
                    var rep = PropValue(map, "rep");
                    if (rep is { Type: SS.UiValueType.String })
                    {
                        node.ImageRepeatX = node.ImageRepeatY = RepeatMode(rep.Value.String, scene);
                    }
                    else if (rep is { Type: SS.UiValueType.Array, Array: { Length: 2 } both })
                    {
                        node.ImageRepeatX = RepeatMode(both[0].String, scene);
                        node.ImageRepeatY = RepeatMode(both[1].String, scene);
                    }
                }

                if (HasKey(map, "uv"))
                {
                    var uv = Numbers(map, "uv");
                    if (uv.Length == 4 && uv[2] > uv[0] && uv[3] > uv[1])
                        node.ImageCrop = Rect.MinMaxRect(Mathf.Clamp01(uv[0]), Mathf.Clamp01(uv[1]), Mathf.Clamp01(uv[2]), Mathf.Clamp01(uv[3]));
                    else
                        scene.Problem("IMG: uv takes [u0,v0,u1,v1] with u1 > u0 and v1 > v0");
                }
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

        Validate(map, scene, op, PropString(map, "id"));

        ParseFill(map, node);
        ParseStroke(map, node);

        var children = PropValue(map, "c");
        if (children != null && children.Value.Type == SS.UiValueType.Array)
        {
            // A group's `style` becomes the default for everything under it, combined with
            // whatever it inherited itself, so defaults nest.
            var style = ParseStyle(map, inherited);

            foreach (var child in ParseNodes(children.Value, scene, style))
                node.Children.Add(child);
        }

        node.Id = PropString(map, "id");
        node.Clickable = PropNumber(map, "click", 0f) > 0.5f;
        if (!string.IsNullOrEmpty(node.Id))
            node.SourceProps = map;

        node.SelfUsesTime = NodeUsesTime(node) || node.Visible is { UsesTime: true };
        node.UsesTime = node.SelfUsesTime;
        node.AlphaOnly = node.Op == VecOp.Group
                         && node.Opacity.UsesTime && node.Opacity.ReadsOnlyTime()
                         && !NodeUsesTime(node, withOpacity: false) && node.Visible is not { UsesTime: true }
                         && !node.Children.Exists(c => c.UsesTime || HoldsText(c));
        node.UsesScroll = NodeUsesScroll(node);
        node.Interactive = node.Clickable || node.Op is VecOp.Scroll or VecOp.Image;
        foreach (var child in node.Children)
        {
            node.UsesTime |= child.UsesTime;
            node.UsesScroll |= child.UsesScroll;
            node.Interactive |= child.Interactive;
        }

        return node;
    }

    private static bool HoldsText(VecNode node)
    {
        return node.Op == VecOp.Text || node.Children.Exists(HoldsText);
    }

    private static bool NodeUsesTime(VecNode node, bool withOpacity = true)
    {
        return node.X.UsesTime || node.Y.UsesTime || node.W.UsesTime || node.H.UsesTime
               || node.Rx.UsesTime || node.Ry.UsesTime
               || node.Tx.UsesTime || node.Ty.UsesTime || node.Rotate.UsesTime
               || node.Sx.UsesTime || node.Sy.UsesTime || node.Ax.UsesTime || node.Ay.UsesTime
               || node.Y2.UsesTime
               || (withOpacity && node.Opacity.UsesTime) || node.FillOpacity.UsesTime || node.Feather.UsesTime
               || node.EdgeFeather is { UsesTime: true }
               || node.StrokeWidth.UsesTime || node.StrokeOpacity.UsesTime || node.DashOffset.UsesTime
               || node.FillGradientAt is { UsesTime: true } || node.StrokeGradientAt is { UsesTime: true }
               || node.ImageAtX.UsesTime || node.ImageAtY.UsesTime
               || node.ImageOffX.UsesTime || node.ImageOffY.UsesTime
               || node.ImageTileW is { UsesTime: true } || node.ImageTileH is { UsesTime: true }
               || node.TextOutlineWidth is { UsesTime: true }
               || node.EdgeOpacity is { UsesTime: true } || node.ContentH.UsesTime
               || node.FillIndex is { UsesTime: true } || node.StrokeIndex is { UsesTime: true }
               || node.TextIndex is { UsesTime: true } || node.TextSize is { UsesTime: true }
               || node.MinSize is { UsesTime: true }
               || node.ScrollSet is { UsesTime: true } || node.ScrollSetVersion is { UsesTime: true }
               || (node.CornerRadii != null && System.Array.Exists(node.CornerRadii, e => e.UsesTime))
               || (node.TextParts != null && System.Array.Exists(node.TextParts, p => p.Index is { UsesTime: true }))
               || (node.Filters != null && node.Filters.Exists(f => f.Amount.UsesTime));
    }

    private static bool NodeUsesScroll(VecNode node)
    {
        return node.X.UsesScroll || node.Y.UsesScroll || node.W.UsesScroll || node.H.UsesScroll
               || node.Rx.UsesScroll || node.Ry.UsesScroll
               || node.Tx.UsesScroll || node.Ty.UsesScroll || node.Rotate.UsesScroll
               || node.Sx.UsesScroll || node.Sy.UsesScroll || node.Ax.UsesScroll || node.Ay.UsesScroll
               || node.Y2.UsesScroll
               || node.Opacity.UsesScroll || node.FillOpacity.UsesScroll || node.Feather.UsesScroll
               || node.EdgeFeather is { UsesScroll: true }
               || node.StrokeWidth.UsesScroll || node.StrokeOpacity.UsesScroll || node.DashOffset.UsesScroll
               || node.FillGradientAt is { UsesScroll: true } || node.StrokeGradientAt is { UsesScroll: true }
               || node.ImageAtX.UsesScroll || node.ImageAtY.UsesScroll
               || node.ImageOffX.UsesScroll || node.ImageOffY.UsesScroll
               || node.ImageTileW is { UsesScroll: true } || node.ImageTileH is { UsesScroll: true }
               || node.TextOutlineWidth is { UsesScroll: true }
               || node.EdgeOpacity is { UsesScroll: true } || node.ContentH.UsesScroll
               || node.FillIndex is { UsesScroll: true } || node.StrokeIndex is { UsesScroll: true }
               || node.TextIndex is { UsesScroll: true } || node.TextSize is { UsesScroll: true }
               || node.MinSize is { UsesScroll: true }
               || (node.CornerRadii != null && System.Array.Exists(node.CornerRadii, e => e.UsesScroll))
               || (node.TextParts != null && System.Array.Exists(node.TextParts, p => p.Index is { UsesScroll: true }));
    }

    private static void ParseFill(SS.UiProp[] map, VecNode node)
    {
        // Map form: f = { grad = "name", at = "=expr" } samples the ramp at an expression
        // rather than by position, which is how a colour is animated or driven by data.
        node.Shadows = ParseShadows(map);

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

            // `fat` is the text form of { grad, at }: sample the ramp at an expression.
            if (HasKey(map, "fat"))
                node.FillGradientAt = Attr(map, "fat", 0f);
            node.FillOpacity = Attr(map, "fo", 1f);
            node.Feather = Attr(map, "fea", -1f);

        if (HasKey(map, "fea_edge"))
            node.EdgeFeather = Attr(map, "fea_edge", -1f);
            return;
        }

        // $name binds the colour to the data payload, re-read every tick.
        if (fill[0] == '$')
        {
            var bound = SplitBinding(fill[1..]);
            node.HasFill = true;
            node.FillData = bound.Name;
            node.FillIndex = bound.Index;
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

            if (op == "SYM" && !string.IsNullOrEmpty(id))
            {
                var body = PropValue(map, "c");
                if (body?.Type == SS.UiValueType.Array && body.Value.Array != null)
                {
                    // Defaults come from a `params` map, or -- since the text format has no
                    // map syntax -- from the SYM's own attributes, so
                    // `SYM id=knob w=40 f=#fff { ... }` declares defaults the natural way.
                    var defaults = PropValue(map, "params");
                    scene.Symbols[id!] = (
                        body.Value.Array,
                        defaults?.Type == SS.UiValueType.Map && defaults.Value.Map != null
                            ? defaults.Value.Map
                            : map);
                }

                continue;
            }

            if (string.IsNullOrEmpty(id))
                continue;

            switch (op)
            {
                case "GC":
                {
                    // Conic: the parameter is the angle around (cx, cy), from `a` degrees
                    // measured clockwise from twelve o'clock, as CSS conic-gradient.
                    var bboxConic = string.Equals(PropString(map, "units"), "bbox", StringComparison.OrdinalIgnoreCase);
                    var conicSlots = new GradientSlots
                    {
                        Cx = Live(map, "cx"), Cy = Live(map, "cy"), Angle = Live(map, "a"),
                    };

                    var conic = new Gradient
                    {
                        Conic = true,
                        BoundingBox = bboxConic,
                        Start = new Vector2(PropNumber(map, "cx", 0f), PropNumber(map, "cy", 0f)),
                        Angle = PropNumber(map, "a", 0f),
                    };

                    ReadStops(map, conic, conicSlots);
                    Publish(scene, id!, conic, conicSlots);
                    break;
                }

                case "GL":
                case "GR":
                {
                    // `units = "bbox"` (SVG's objectBoundingBox): coordinates become 0..1
                    // fractions of whatever shape the gradient fills, so the ramp tracks
                    // moving geometry without any per-frame evaluation.
                    var bbox = string.Equals(PropString(map, "units"), "bbox", StringComparison.OrdinalIgnoreCase);
                    var gradient = new Gradient { Radial = op == "GR", BoundingBox = bbox };
                    gradient.Spread = (PropString(map, "spread") ?? "pad").ToUpperInvariant() switch
                    {
                        "REPEAT" => 1,
                        "REFLECT" => 2,
                        "NONE" => 3,
                        _ => 0,
                    };
                    var slots = new GradientSlots();

                    if (gradient.Radial)
                    {
                        var cx = PropNumber(map, "cx", 0f);
                        var cy = PropNumber(map, "cy", 0f);
                        gradient.Radius = Mathf.Max(0.0001f, PropNumber(map, "r", 1f));
                        gradient.Start = new Vector2(cx, cy);
                        gradient.Focus = new Vector2(PropNumber(map, "fx", cx), PropNumber(map, "fy", cy));

                        slots.Cx = Live(map, "cx");
                        slots.Cy = Live(map, "cy");
                        slots.Radius = Live(map, "r");

                        // An unstated focus is the centre, and stays the centre when the
                        // centre moves: it takes the same expression rather than a stale copy.
                        slots.Fx = HasKey(map, "fx") ? Live(map, "fx") : slots.Cx;
                        slots.Fy = HasKey(map, "fy") ? Live(map, "fy") : slots.Cy;
                    }
                    else
                    {
                        gradient.Start = new Vector2(PropNumber(map, "x1", 0f), PropNumber(map, "y1", 0f));
                        gradient.End = new Vector2(PropNumber(map, "x2", 0f), PropNumber(map, "y2", 1f));

                        slots.X1 = Live(map, "x1");
                        slots.Y1 = Live(map, "y1");
                        slots.X2 = Live(map, "x2");
                        slots.Y2 = Live(map, "y2");
                    }

                    ReadStops(map, gradient, slots);
                    Publish(scene, id!, gradient, slots);
                    break;
                }

                case "CP":
                {
                    var (shape, outline) = ClipOutline(map);

                    // A clip written with expressions is re-cut every rebuild, so a masked
                    // bar or a scrolling list can change size from the data payload alone,
                    // with no new structure. Its shape here is meaningless -- there is no
                    // payload yet, so a width of `$w` cuts nothing -- and rejecting it for
                    // that would reject exactly the clips this exists for.
                    if (shape != null && IsLiveShape(shape))
                    {
                        scene.Clips[id!] = outline ?? new List<Vector2>();
                        scene.ClipNodes[id!] = shape;
                        scene.LiveDefs = true;
                        scene.DefsUseTime |= shape.UsesTime;
                    }
                    else if (outline is { Count: >= 3 })
                    {
                        scene.Clips[id!] = outline;
                    }
                    else
                    {
                        scene.Problem($"clip \"{id}\" has no usable shape (must be an R, C, Y or P)");
                    }

                    break;
                }
            }
        }
    }

    /// <summary>
    /// Stops are pairs: <c>stops = {{0, "#fff"}, {1, "#000"}}</c>. A position may be an
    /// expression and a colour may be a <c>$name</c> from the data payload; both are then
    /// re-read every rebuild.
    /// </summary>
    private static void ReadStops(SS.UiProp[] map, Gradient gradient, GradientSlots slots)
    {
        gradient.Slots = slots;

        var stops = PropValue(map, "stops");
        if (stops == null || stops.Value.Type != SS.UiValueType.Array || stops.Value.Array == null)
            return;

        var positions = new List<float>();
        var colours = new List<string>();
        List<Expression?>? positionSlots = null;
        List<string?>? colourSlots = null;

        foreach (var stop in stops.Value.Array)
        {
            if (stop.Type != SS.UiValueType.Array || stop.Array == null || stop.Array.Length < 2)
                continue;

            if (stop.Array[1].Type != SS.UiValueType.String)
                continue;

            Expression? livePosition = null;

            switch (stop.Array[0].Type)
            {
                case SS.UiValueType.Number:
                    positions.Add(stop.Array[0].Number);
                    break;

                case SS.UiValueType.String when !string.IsNullOrEmpty(stop.Array[0].String):
                    livePosition = Expression.Parse(stop.Array[0].String!, 0f);
                    positions.Add(0f);
                    break;

                default:
                    continue;
            }

            var colour = stop.Array[1].String ?? string.Empty;
            string? liveColour = null;

            // `$name` is a colour from the payload. The literal stands in until the first
            // resolve, and has to parse or the stop would be dropped as unreadable.
            if (colour.Length > 1 && colour[0] == '$')
            {
                liveColour = colour[1..];
                colour = "#FF00FF";
            }

            colours.Add(colour);

            if (livePosition != null)
                positionSlots ??= Fill(positions.Count - 1);

            if (liveColour != null)
                colourSlots ??= FillNames(colours.Count - 1);

            positionSlots?.Add(livePosition);
            colourSlots?.Add(liveColour);
        }

        // A list that only started once a live stop appeared still has to describe every
        // stop before it, or the arrays would not line up.
        Pad(positionSlots, positions.Count);
        Pad(colourSlots, positions.Count);

        GradientParser.ReadStops(positions.ToArray(), colours.ToArray(), gradient,
            positionSlots?.ToArray(), colourSlots?.ToArray());

        static List<Expression?> Fill(int count)
        {
            var list = new List<Expression?>(count + 4);
            for (var i = 0; i < count; i++)
                list.Add(null);

            return list;
        }

        static List<string?> FillNames(int count)
        {
            var list = new List<string?>(count + 4);
            for (var i = 0; i < count; i++)
                list.Add(null);

            return list;
        }

        static void Pad<T>(List<T?>? list, int count) where T : class
        {
            while (list != null && list.Count < count)
                list.Add(null);
        }
    }

    /// <summary>
    /// A gradient attribute written as a string: an expression, live from the payload.
    /// </summary>
    /// <remarks>
    /// Numbers keep the path they always had — <c>PropNumber</c> resolves them once, and the
    /// gradient carries no expressions at all, so a static declaration costs exactly what it
    /// did before.
    /// </remarks>
    private static Expression? Live(SS.UiProp[] map, string key)
    {
        var value = PropValue(map, key);
        if (value?.Type != SS.UiValueType.String || string.IsNullOrEmpty(value.Value.String))
            return null;

        return Expression.Parse(value.Value.String!, 0f);
    }

    private static void Publish(VecScene scene, string id, Gradient gradient, GradientSlots slots)
    {
        gradient.Slots = slots.Any ? slots : null;
        scene.Gradients[id] = gradient;

        if (gradient.Slots == null)
            return;

        slots.UsesTime = Time(slots.X1) || Time(slots.Y1) || Time(slots.X2) || Time(slots.Y2)
                         || Time(slots.Cx) || Time(slots.Cy) || Time(slots.Radius)
                         || Time(slots.Fx) || Time(slots.Fy) || Time(slots.Angle)
                         || AnyTime(slots.StopPositions);

        scene.LiveDefs = true;
        scene.DefsUseTime |= slots.UsesTime;

        slots.UsesScroll = Scroll(slots.X1) || Scroll(slots.Y1) || Scroll(slots.X2) || Scroll(slots.Y2)
                           || Scroll(slots.Cx) || Scroll(slots.Cy) || Scroll(slots.Radius)
                           || Scroll(slots.Fx) || Scroll(slots.Fy) || Scroll(slots.Angle)
                           || (slots.StopPositions != null && System.Array.Exists(slots.StopPositions, e => e is { UsesScroll: true }));
        scene.DefsUseScroll |= slots.UsesScroll;

        static bool Scroll(Expression? expression) => expression is { UsesScroll: true };

        static bool Time(Expression? expression) => expression is { UsesTime: true };

        static bool AnyTime(Expression?[]? expressions)
        {
            if (expressions == null)
                return false;

            foreach (var expression in expressions)
            {
                if (expression is { UsesTime: true })
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Builds a clip outline in scene coordinates from a CP declaration's shape.
    /// </summary>
    /// <remarks>
    /// A clip of plain numbers is cut once, here. One written with expressions keeps its
    /// node and is re-cut per rebuild (<see cref="VecScene.ResolveLiveDefs"/>), so a clip can
    /// follow the data the way a shape does.
    /// </remarks>
    private static (VecNode? Shape, List<Vector2>? Outline) ClipOutline(SS.UiProp[] map)
    {
        var children = PropValue(map, "c");
        if (children == null || children.Value.Type != SS.UiValueType.Array || children.Value.Array == null)
            return (null, null);

        foreach (var item in children.Value.Array)
        {
            if (item.Type != SS.UiValueType.Map || item.Map == null)
                continue;

            var node = ParseNode(item.Map, new VecScene());
            if (node != null)
                return (node, Tessellator.Outline(node, new EvalContext()));
        }

        return (null, null);
    }

    /// <summary>A clip shape whose geometry is expressions rather than numbers.</summary>
    private static bool IsLiveShape(VecNode node)
    {
        return !node.X.IsConstant || !node.Y.IsConstant || !node.W.IsConstant || !node.H.IsConstant
               || !node.Rx.IsConstant || !node.Ry.IsConstant
               || (node.CornerRadii != null && System.Array.Exists(node.CornerRadii, r => !r.IsConstant));
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

            if (HasKey(map, "sat"))
                node.StrokeGradientAt = Attr(map, "sat", 0f);
        }
        else if (stroke[0] == '$')
        {
            node.HasStroke = true;
            var boundStroke = SplitBinding(stroke[1..]);
            node.StrokeData = boundStroke.Name;
            node.StrokeIndex = boundStroke.Index;
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
    /// <summary>Splits a `$` binding into its name and, when present, its index expression.</summary>
    /// <remarks>
    /// `rows[i]` becomes ("rows", i). `row` becomes ("row", null). The `$` is already gone by
    /// the time this is called.
    ///
    /// The index is parsed with the ordinary expression parser, so it is not limited to a
    /// bare `i` -- `$rows[n-1-i]` reverses a list, and `$cols[mod(i,4)]` cycles a palette.
    /// </remarks>
    /// <summary>
    /// Splits `"set {$press:%.1f} kPa, trip {$trip:%.0f}"` into literals and bindings, or returns
    /// null when the text holds no `{$`. A placeholder without its own format takes the node's
    /// `fmt`; one never closed stays literal text.
    /// </summary>
    internal static TextPart[]? TextTemplate(string body, string? fallbackFormat)
    {
        var open = body.IndexOf("{$", StringComparison.Ordinal);
        if (open < 0)
            return null;

        var parts = new List<TextPart>();
        var at = 0;

        while (open >= 0)
        {
            var close = body.IndexOf('}', open);
            if (close < 0)
                break;

            if (open > at)
                parts.Add(TextPart.Text(body[at..open]));

            // The format follows the last ':' after any index, so `$rows[i]` may hold anything.
            var inner = body[(open + 2)..close];
            var bracket = inner.LastIndexOf(']');
            var colon = inner.IndexOf(':', Math.Max(bracket, 0));
            var bound = SplitBinding(colon < 0 ? inner : inner[..colon]);
            parts.Add(TextPart.Binding(bound.Name, bound.Index, colon < 0 ? fallbackFormat : inner[(colon + 1)..]));

            at = close + 1;
            open = body.IndexOf("{$", at, StringComparison.Ordinal);
        }

        if (at < body.Length)
            parts.Add(TextPart.Text(body[at..]));

        return parts.ToArray();
    }

    private static int RepeatMode(string? word, VecScene scene)
    {
        switch (word?.ToUpperInvariant())
        {
            case null:
            case "REPEAT":
                return 0;
            case "ONCE":
            case "NO-REPEAT":
                return 1;
            case "ROUND":
                return 2;
            case "SPACE":
                return 3;
            case "STRETCH":
                return 4;
            default:
                scene.Problem($"IMG: \"{word}\" is not repeat, once, round, space or stretch");
                return 0;
        }
    }

    private static (string Name, Expression? Index) SplitBinding(string body)
    {
        var open = body.IndexOf('[', StringComparison.Ordinal);
        if (open <= 0 || body.Length < 3 || body[^1] != ']')
            return (body, null);

        var inner = body[(open + 1)..^1];
        if (inner.Length == 0)
            return (body, null);

        return (body[..open], Expression.Parse(inner, 0f));
    }

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
    /// <summary>
    /// `sh = { { dx, dy, blur, spread, "#rrggbbaa" }, ... }` -- CSS box-shadow order, and a
    /// list so several compose. A single shadow may be given unwrapped.
    /// </summary>
    private static VecShadow[]? ParseShadows(SS.UiProp[] map)
    {
        var value = PropValue(map, "sh");
        if (value?.Type != SS.UiValueType.Array || value.Value.Array == null)
            return null;

        var entries = value.Value.Array;
        if (entries.Length == 0)
            return null;

        // One shadow written without the outer braces: { 0, 3, 8, 0, "#0000001f" }.
        if (entries[0].Type != SS.UiValueType.Array)
        {
            var single = ParseShadow(entries);
            return single.HasValue ? new[] { single.Value } : null;
        }

        var list = new List<VecShadow>(entries.Length);
        foreach (var entry in entries)
        {
            if (entry.Type != SS.UiValueType.Array || entry.Array == null)
                continue;

            var parsed = ParseShadow(entry.Array);
            if (parsed.HasValue)
                list.Add(parsed.Value);
        }

        return list.Count > 0 ? list.ToArray() : null;
    }

    private static VecShadow? ParseShadow(SS.UiValue[] parts)
    {
        if (parts.Length < 5)
            return null;

        static float Num(SS.UiValue v) => v.Type == SS.UiValueType.Number ? v.Number : 0f;

        var text = parts[4].Type == SS.UiValueType.String ? parts[4].String : null;
        if (string.IsNullOrEmpty(text) || !ColorUtility.TryParseHtmlString(text, out var colour))
            return null;

        // Sixth field: `inset` (or 1), as CSS writes it.
        var inset = parts.Length > 5
                    && ((parts[5].Type == SS.UiValueType.String && string.Equals(parts[5].String, "inset", StringComparison.OrdinalIgnoreCase))
                        || (parts[5].Type == SS.UiValueType.Number && parts[5].Number > 0.5f)
                        || (parts[5].Type == SS.UiValueType.Bool && parts[5].Number > 0.5f));

        return new VecShadow(Num(parts[0]), Num(parts[1]), Num(parts[2]), Num(parts[3]), colour, inset);
    }

    /// <summary>`rx = [tl, tr, br, bl]` in CSS order, or null when `rx` is a single value.</summary>
    private static Expression[]? ParseCorners(SS.UiProp[] map)
    {
        var value = PropValue(map, "rx");
        if (value?.Type != SS.UiValueType.Array || value.Value.Array == null)
            return null;

        var corners = new Expression[4];
        for (var i = 0; i < 4; i++)
            corners[i] = Pair(map, "rx", i, 0f);

        return corners;
    }

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
                Expression.Parse(array[index].String!, fallback),
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
