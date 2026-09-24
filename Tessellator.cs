using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>
/// Walks a parsed scene and emits triangles into a <see cref="MeshBuilder"/>.
/// </summary>
/// <remarks>
/// Scene space is top-left origin with +Y down (§2 of the spec); UGUI rect space is
/// bottom-left with +Y up. The viewbox transform built in <see cref="Emit"/> does the flip
/// once, so every node below it can be written in the coordinates the spec describes.
///
/// Repeats are expanded here rather than in the parser. That is the whole point: a scene
/// costing two nodes of Lua construction can expand to thousands of quads on the client,
/// which is what keeps the per-tick instruction budget survivable.
///
/// **Coordinate spaces for paint and clipping.** Gradient geometry and clip outlines are
/// interpreted in the *local* space of the node or group that uses them, not in absolute
/// scene space. For artwork with no group transforms — most of it — those are the same
/// thing. Under a transform it means a gradient rotates with its group rather than staying
/// pinned to the screen, which is nearly always what is wanted and avoids an inverse
/// transform per vertex.
/// </remarks>
internal static class Tessellator
{
    /// <summary>Most vertices one surface's mesh may hold.</summary>
    /// <remarks>
    /// A surface's geometry is split across as many meshes as it needs -- see
    /// <see cref="VectorSlice"/> -- so this is the total across all of them, eight meshes'
    /// worth. It is a sanity bound rather than a hardware one: a scene this size is a mistake,
    /// and a runaway one should not be able to eat memory without limit.
    ///
    /// The PER-MESH limit is the real constraint and lives in <see cref="MeshBuilder.PerMesh"/>.
    /// It is UGUI's, not ours: its own VertexHelper throws at 65,000 because the
    /// CanvasRenderer batcher works in 16-bit indices. Asking a mesh for 32-bit indices and
    /// handing it to a CanvasRenderer anyway took the game down natively, with nothing in the
    /// log. More meshes is the supported answer, and the one TextMeshPro uses.
    /// </remarks>
    private const int MaxVertices = 480000;

    /// <summary>A single shape larger than one mesh cannot be split and is refused outright.</summary>
    private const int MaxPerShape = MeshBuilder.PerMesh;
    private const int MinCornerSegments = 3;
    private const int MaxCornerSegments = 16;
    private const int MinEllipseSegments = 6;
    private const int MaxEllipseSegments = 48;

    /// <summary>
    /// Auto-feather target, in screen pixels. Slightly over one pixel: enough to cover the
    /// hard step between covered and uncovered pixels, small enough not to look soft.
    /// </summary>
    private const float AutoFeatherPixels = 1.3f;

    /// <summary>Parameter span a triangle may cover before a gradient needs it subdivided.</summary>
    private const float GradientTolerance = 0.06f;

    /// <summary>Screen pixels per canvas unit for the surface currently being tessellated.</summary>
    [ThreadStatic] private static float ScreenScale;

    /// <summary>Canvas back to scene space, undoing the viewbox fit; and the fit's X scale and Y/X ratio.</summary>
    [ThreadStatic] private static Matrix4x4 _viewboxInverse;
    [ThreadStatic] private static float _viewboxScaleX;
    [ThreadStatic] private static float _viewboxRatio;

    /// <summary>
    /// Per-op-type time and counts for the current rebuild.
    /// </summary>
    /// <remarks>
    /// Six hypotheses about where the cost lives have now been falsified by measurement —
    /// vertex count, expression depth, `lossyScale`, `VertexHelper`, mesh upload, and band
    /// triangulation. Each was plausible and each was wrong, so this stops inferring and
    /// attributes time directly to the node type that spent it.
    ///
    /// One timestamp pair per shape, which at 800 shapes is about 0.05 ms against a 19 ms
    /// rebuild: visible in principle, negligible against what is being hunted.
    /// </remarks>
    // Phase timers inside YS, the largest single op in a real console. Same approach as the
    // per-op timing that found the clip allocation: measure the parts rather than argue
    // about which one is slow.
    /// <summary>
    /// Text found during the walk, for the main thread to turn into TMP children.
    /// </summary>
    /// <remarks>
    /// Collected rather than drawn because TMP is main-thread and builds its own mesh. The
    /// list belongs to the job, so it is [ThreadStatic] like every other scratch here and
    /// copied into the stats object at the end of Emit.
    /// </remarks>
    [ThreadStatic] internal static List<TextPlacement>? TextFound;

    /// <summary>Clickable node bounds found during the walk, in draw order.</summary>
    [ThreadStatic] internal static List<HitRegion>? HitsFound;
    [ThreadStatic] internal static List<ImagePlacement>? ImagesFound;

    [ThreadStatic] internal static List<ScrollRegion>? ScrollsFound;

    /// <summary>The op that first ran out of vertex budget this rebuild, or null.</summary>
    /// <remarks>
    /// Every budget check used to be a bare `return`, so a scene over the ceiling simply drew
    /// less than it asked for, with nothing in the log and nothing on screen to say which part
    /// went missing. Recording the first op to be refused turns that into the magenta border
    /// and a named problem, which is what the rest of the diagnostics already do for every
    /// other kind of fault.
    /// </remarks>
    [ThreadStatic] private static string? _starved;

    /// <summary>Index triples for one radial band, reused so a fill allocates nothing.</summary>
    [ThreadStatic] private static List<int>? _ringIndices;

    private static List<int> RingIndices => _ringIndices ??= new List<int>(512);

    /// <summary>The outline a radial fill builds its rings from, after densifying.</summary>
    [ThreadStatic] private static List<Vector2>? _ringOutline;

    private static List<Vector2> RingOutline => _ringOutline ??= new List<Vector2>(RadialFill.MaxOutlinePoints + 64);


    [ThreadStatic] internal static double BandSampleMs;
    [ThreadStatic] internal static double BandStripMs;
    [ThreadStatic] internal static double BandFeatherMs;
    [ThreadStatic] internal static int BandQuads;

    [ThreadStatic] private static double[]? _opMilliseconds;
    [ThreadStatic] private static int[]? _opCounts;

    internal static double[] OpMilliseconds => _opMilliseconds ??= new double[12];

    internal static int[] OpCounts => _opCounts ??= new int[12];

    /// <summary>Ablation state for the scene currently being tessellated.</summary>
    [ThreadStatic] private static bool NoFill;

    [ThreadStatic] private static bool NoFeather;

    [ThreadStatic] private static bool NoEval;

    /// <summary>False when the camera could not be resolved; LOD then assumes full size.</summary>
    [ThreadStatic] private static bool ScreenSizeKnown;

    private const int MaxGradientDepth = 5;

    /// <summary>What an unresolved binding draws in. Deliberately not a design colour.</summary>
    private static readonly Color Magenta = new(1f, 0f, 1f, 1f);

    // Scratch buffers, shared per THREAD rather than per process. Reusing them is the
    // difference between allocating four Lists per shape per frame -- roughly 270,000
    // allocations a second for a mote field -- and allocating none.
    //
    // [ThreadStatic] is what makes tessellation safe to run off the main thread, and safe to
    // run for several consoles at once. Every one of these was a plain static while the
    // rebuild was main-thread only; sharing them across worker threads would corrupt one
    // surface's geometry with another's, intermittently and invisibly.
    //
    // They cannot use inline initialisers: a [ThreadStatic] field is only initialised on the
    // thread that happens to run the static constructor, and every other thread would see
    // null. Hence the lazy properties.
    [ThreadStatic] private static List<Vector2>? _outlineScratch;
    [ThreadStatic] private static List<Vector2>? _clippedScratch;
    [ThreadStatic] private static List<Vector2>? _quadScratch;

    private static List<Vector2> OutlineScratch => _outlineScratch ??= new List<Vector2>(64);

    private static List<Vector2> ClippedScratch => _clippedScratch ??= new List<Vector2>(64);

    private static List<Vector2> QuadScratch => _quadScratch ??= new List<Vector2>(8);

    /// <summary>Transform, opacity and clip state, pushed and popped as groups are entered.</summary>
    private struct Frame
    {
        internal Matrix4x4 Matrix;
        internal float Opacity;

        /// <summary>
        /// Canvas units per scene unit for this frame, carried rather than re-derived.
        /// </summary>
        /// <remarks>
        /// <c>Matrix4x4.lossyScale</c> is a **native ECall** that decomposes the matrix, not
        /// a field read. The tessellator was calling it two or three times per shape, so a
        /// 1,700-mote field made roughly 4,000 managed-to-native transitions per rebuild —
        /// which is exactly what the in-game profile showed: cost per shape that barely moved
        /// with vertex count or expression depth.
        ///
        /// It was never needed. The root scale comes straight out of the viewbox fit, and a
        /// group's is its parent's times its own scale attribute.
        /// </remarks>
        internal float Scale;

        /// <summary>Clip region in this frame's own local coordinates, or null.</summary>
        internal ClipRegion? Clip;

        /// <summary>
        /// Scene space to this frame's local space, for placing a declared clip.
        /// </summary>
        /// <remarks>
        /// Clip paths are declared in `defs`, at scene level, and §7 reads as scene
        /// coordinates. The code previously took a declared outline as-is, i.e. in the
        /// *referencing group's* local space. Identical when the group has no transform —
        /// which every demo and every test happens to be — and wrong the moment one does,
        /// which is exactly how this reached a user instead of a test.
        /// </remarks>
        internal Matrix4x4 SceneToLocal;

        /// <summary>
        /// A concave clip, as convex pieces in local coordinates. When set, <see cref="Clip"/>
        /// is unused and every leaf is emitted once per piece.
        /// </summary>
        internal List<ClipRegion>? Pieces;

        /// <summary>The whole clip outline in canvas space, for masking labels. Null when unclipped.</summary>
        internal List<Vector2>? CanvasClip;
    }

    /// <summary>True on the second and later piece of a concave clip: geometry only, no text, hits or scrolls.</summary>
    [ThreadStatic] private static bool _repeatPiece;

    /// <summary>
    /// Tessellates a scene and harvests its diagnostics before returning.
    /// </summary>
    /// <remarks>
    /// The counters are [ThreadStatic], so a caller on the main thread cannot read them after
    /// a worker filled them in — it would see its own empty copy. They are copied out here,
    /// on the thread that produced them, while the job still owns the stats object.
    /// </remarks>
    internal static int Emit(MeshBuilder vh, VecScene scene, EvalContext context, Rect target, float screenPixelsPerUnit, bool screenSizeKnown, TessellationStats stats)
    {
        var shapes = EmitCore(vh, scene, context, target, screenPixelsPerUnit, screenSizeKnown);

        System.Array.Copy(OpMilliseconds, stats.OpMilliseconds, stats.OpMilliseconds.Length);
        System.Array.Copy(OpCounts, stats.OpCounts, stats.OpCounts.Length);
        stats.BandSampleMs = BandSampleMs;
        stats.BandStripMs = BandStripMs;
        stats.BandFeatherMs = BandFeatherMs;
        stats.BandQuads = BandQuads;
        stats.Shapes = shapes;
        stats.DrewTime = DrewTime || scene.DefsUseTime;
        stats.AlphaGroups.Clear();
        if (AlphaFound != null)
            stats.AlphaGroups.AddRange(AlphaFound);

        stats.Text.Clear();
        if (TextFound != null)
            stats.Text.AddRange(TextFound);

        stats.Hits.Clear();
        if (HitsFound != null)
            stats.Hits.AddRange(HitsFound);

        stats.Images.Clear();
        if (ImagesFound != null)
            stats.Images.AddRange(ImagesFound);

        stats.Starved = PeekStarved();
        _starved = null;

        stats.Scrolls.Clear();
        if (ScrollsFound != null)
            stats.Scrolls.AddRange(ScrollsFound);

        stats.Missing.Clear();
        foreach (var name in context.Missing)
            stats.Missing.Add(name);

        context.Missing.Clear();

        return shapes;
    }

    private static int EmitCore(MeshBuilder vh, VecScene scene, EvalContext context, Rect target, float screenPixelsPerUnit, bool screenSizeKnown)
    {
        vh.Clear();

        // Canvas units per scene unit comes from the viewbox; screen pixels per canvas unit
        // comes from the camera. Only the product is a real on-screen size.
        ScreenScale = Mathf.Max(0.0001f, screenPixelsPerUnit);
        ScreenSizeKnown = screenSizeKnown;
        System.Array.Clear(OpMilliseconds, 0, OpMilliseconds.Length);
        System.Array.Clear(OpCounts, 0, OpCounts.Length);
        BandSampleMs = 0d;
        BandStripMs = 0d;
        BandFeatherMs = 0d;
        BandQuads = 0;
        DrewTime = false;

        _starved = null;

        (TextFound ??= new List<TextPlacement>(16)).Clear();
        (HitsFound ??= new List<HitRegion>(16)).Clear();
        (ImagesFound ??= new List<ImagePlacement>(4)).Clear();
        (ScrollsFound ??= new List<ScrollRegion>(4)).Clear();
        (AlphaFound ??= new List<AlphaGroup>(4)).Clear();
        _idPath?.Clear();
        context.ScopeId = null;

        vh.TrackBounds(scene.TextInOrder);

        NoFill = scene.DebugNoFill;
        NoFeather = scene.DebugNoFeather;
        NoEval = scene.DebugNoEval;

        // Clips and gradients declared with expressions are re-read here, once, before the
        // walk: everything below sees resolved numbers exactly as a static declaration gives.
        scene.ResolveLiveDefs(context);

        var scale = Fit(scene, target);
        var offset = Centre(scene, target, scale);

        // Scene (top-left, +Y down) -> UGUI rect (bottom-left, +Y up).
        var viewbox =
            Matrix4x4.Translate(new Vector3(target.xMin + offset.x, target.yMax - offset.y, 0f)) *
            Matrix4x4.Scale(new Vector3(scale.x, -scale.y, 1f));

        _viewboxInverse = AffineInverse(viewbox);
        _viewboxScaleX = Mathf.Max(0.0001f, Mathf.Abs(scale.x));
        _viewboxRatio = Mathf.Abs(scale.y) / _viewboxScaleX;

        var stack = new Stack<Frame>();
        stack.Push(new Frame
        {
            Matrix = viewbox,
            Opacity = 1f,
            Clip = null,
            Scale = Mathf.Abs(scale.x),
            SceneToLocal = Matrix4x4.identity,
        });

        var emitted = 0;
        foreach (var node in scene.Root)
            EmitNode(vh, scene, node, context, stack, ref emitted);


        // A scene that drew nothing, or one that parsed with problems, gets a visible
        // marker. Silence is the worst possible failure mode here: a bad clip id, a rejected
        // non-convex clip and an unknown op all render as an empty console, which looks
        // exactly like the screen being switched off.
        // Running out of room is a fault of THIS rebuild, not of the scene, so it is not filed
        // in `Problems` -- that list is parse-time and is never cleared, which would have left
        // the border burned on for good after one close pass and no way to see it clear when
        // you walked away again. It is peeked here for the marker and harvested into the
        // rebuild's stats, where it lives exactly as long as it is true.
        var starved = PeekStarved();

        if (scene.Problems.Count > 0 || starved != null || emitted == 0)
            EmitErrorMarker(vh, target, scene.Problems.Count + (starved == null ? 0 : 1));

        return emitted;
    }

    /// <summary>
    /// Draws a hatched magenta border so a broken scene is obviously broken.
    /// </summary>
    /// <remarks>
    /// Deliberately ugly and deliberately magenta — the same colour `VectorTest.lua` uses for
    /// "the postfix did not run" — so it reads as a fault rather than as artwork. The stripe
    /// count is the number of distinct problems, so a glance says whether it is one thing or
    /// several; the detail is in `BepInEx/LogOutput.log`.
    /// </remarks>
    private static void EmitErrorMarker(MeshBuilder vh, Rect target, int problems)
    {
        var colour = new Color32(255, 0, 200, 190);
        var thickness = Mathf.Max(2f, Mathf.Min(target.width, target.height) * 0.02f);

        void Bar(float x, float y, float w, float h)
        {
            if (Starved(vh, 4, "the error marker"))
                return;

            var origin = vh.currentVertCount;
            vh.AddVert(new Vector3(x, y), colour, Vector2.zero);
            vh.AddVert(new Vector3(x + w, y), colour, Vector2.zero);
            vh.AddVert(new Vector3(x + w, y + h), colour, Vector2.zero);
            vh.AddVert(new Vector3(x, y + h), colour, Vector2.zero);
            vh.AddTriangle(origin, origin + 1, origin + 2);
            vh.AddTriangle(origin, origin + 2, origin + 3);
        }

        Bar(target.xMin, target.yMin, target.width, thickness);
        Bar(target.xMin, target.yMax - thickness, target.width, thickness);
        Bar(target.xMin, target.yMin, thickness, target.height);
        Bar(target.xMax - thickness, target.yMin, thickness, target.height);

        // One stripe per problem, along the top edge.
        var stripes = Mathf.Clamp(problems, 0, 8);
        for (var i = 0; i < stripes; i++)
        {
            Bar(target.xMin + thickness * (2f + i * 2f), target.yMax - thickness * 4f,
                thickness, thickness * 3f);
        }
    }

    /// <summary>
    /// Builds a node's outline in its own coordinates, without emitting anything. Used to
    /// turn a clip-path declaration into a polygon.
    /// </summary>
    internal static List<Vector2> Outline(VecNode node, EvalContext context)
    {
        return node.Op switch
        {
            VecOp.Rect => RectOutline(node, context, 1f),
            VecOp.Ellipse => EllipseOutline(node, context),
            VecOp.Polyline => FromFlat(node.Points),
            VecOp.Path => PathOutline(node),
            _ => new List<Vector2>(),
        };
    }

    private static List<Vector2> PathOutline(VecNode node)
    {
        if (node.Path == null || node.Path.IsEmpty)
            return new List<Vector2>();

        // Flattened at a fine fixed scale: a clip outline is built once, not per rebuild.
        var outer = Outermost(node.Path.Flatten(4f));
        return outer == null ? new List<Vector2>() : new List<Vector2>(outer);
    }

    private static bool IsLeaf(VecOp op)
    {
        return op is VecOp.Rect or VecOp.Ellipse or VecOp.Band or VecOp.Polyline or VecOp.SampledLine
            or VecOp.Spline or VecOp.Path or VecOp.Image;
    }

    /// <summary>Emits a leaf once per piece of a concave clip.</summary>
    private static void EmitPieces(MeshBuilder vh, VecScene scene, VecNode node, EvalContext context, Stack<Frame> stack, ref int emitted)
    {
        var frame = stack.Pop();
        var saved = _repeatPiece;

        for (var k = 0; k < frame.Pieces!.Count; k++)
        {
            var piece = frame;
            piece.Pieces = null;
            piece.Clip = frame.Pieces[k];
            stack.Push(piece);

            _repeatPiece = saved || k > 0;
            EmitNode(vh, scene, node, context, stack, ref emitted);
            stack.Pop();
        }

        _repeatPiece = saved;
        stack.Push(frame);
    }

    /// <summary>
    /// A child frame's clip: the parent's (single region or pieces) re-expressed through
    /// <paramref name="inverse"/>, intersected with an outline of its own in local space.
    /// </summary>
    private static void CombineClip(Frame parent, Matrix4x4 inverse, List<Vector2>? own, out ClipRegion? clip, out List<ClipRegion>? pieces)
    {
        List<ClipRegion>? inherited = null;
        if (parent.Pieces != null)
        {
            inherited = new List<ClipRegion>(parent.Pieces.Count);
            foreach (var piece in parent.Pieces)
                inherited.Add(piece.Transform(inverse));
        }
        else if (parent.Clip != null)
        {
            inherited = new List<ClipRegion> { parent.Clip.Transform(inverse) };
        }

        if (own == null)
        {
            clip = inherited is { Count: 1 } ? inherited[0] : null;
            pieces = inherited is { Count: 1 } ? null : inherited;
            return;
        }

        var parts = ConvexPartition.Split(own);
        var result = new List<ClipRegion>();

        foreach (var part in parts)
        {
            if (inherited == null)
            {
                var region = ClipRegion.FromPolygon(part);
                if (region != null)
                    result.Add(region);

                continue;
            }

            foreach (var outer in inherited)
            {
                var region = ClipRegion.FromPolygon(new List<Vector2>(outer.ClipPolygon(part)));
                if (region != null)
                    result.Add(region);
            }
        }

        // One piece is an ordinary convex clip. None means nothing inside survives, which is
        // an empty piece list rather than "unclipped".
        clip = result.Count == 1 ? result[0] : null;
        pieces = result.Count == 1 ? null : result;
    }

    /// <summary>A label's clip outline in canvas space: its own, cut by the parent's when that one is convex.</summary>
    private static List<Vector2>? CombineCanvasClip(List<Vector2>? parent, List<Vector2>? own)
    {
        if (own == null)
            return parent;

        if (parent == null)
            return own;

        // ponytail: a concave parent is not intersected; the label keeps its own outline.
        var region = Tessellator.IsConvex(parent) ? ClipRegion.FromPolygon(parent) : null;
        return region == null ? own : new List<Vector2>(region.ClipPolygon(own));
    }

    private static List<Vector2> ToCanvas(Matrix4x4 matrix, List<Vector2> points)
    {
        var result = new List<Vector2>(points.Count);
        foreach (var point in points)
            result.Add(matrix.MultiplyPoint3x4(point));

        return result;
    }

    [ThreadStatic] internal static List<AlphaGroup>? AlphaFound;

    /// <summary>Whether this group is faded by its renderer on this visit rather than rebuilt.</summary>
    private static bool Fades(VecNode node, EvalContext context)
    {
        return node.AlphaOnly && context.RepeatDepth == 0 && AlphaFound != null && !_repeatPiece;
    }

    /// <summary>Set when the walk reaches a node whose own values read `t`.</summary>
    [ThreadStatic] private static bool DrewTime;

    /// <summary>Ids from the root down to the node being walked, for hit regions' `hover` scope.</summary>
    [ThreadStatic] private static List<string>? _idPath;

    private static void EmitNode(MeshBuilder vh, VecScene scene, VecNode node, EvalContext context, Stack<Frame> stack, ref int emitted)
    {
        // A node with an id is the scope `hover` and `down` answer to, for itself and whatever
        // it holds, until a nearer id takes over.
        if (string.IsNullOrEmpty(node.Id))
        {
            EmitNodeCore(vh, scene, node, context, stack, ref emitted);
            return;
        }

        var path = _idPath ??= new List<string>(8);
        var saved = context.ScopeId;
        context.ScopeId = node.Id;
        path.Add(node.Id!);
        EmitNodeCore(vh, scene, node, context, stack, ref emitted);
        path.RemoveAt(path.Count - 1);
        context.ScopeId = saved;
    }

    private static void EmitNodeCore(MeshBuilder vh, VecScene scene, VecNode node, EvalContext context, Stack<Frame> stack, ref int emitted)
    {
        // Before any early-out: a group hidden by a `t`-driven `v` or `o` must still count,
        // since time is what will show it again. What it hides is never reached, and that is
        // the point -- an animation inside a hidden group costs no rebuilds.
        //
        // A group that only fades is the exception: the renderer applies its `o` every frame,
        // so reaching it is no reason to rebuild. Inside a repeat its `o` differs per instance
        // and one mesh cannot carry them all, so there it counts as usual.
        DrewTime |= node.SelfUsesTime && !Fades(node, context);

        if (Starved(vh, 1, "a node"))
            return;

        if (stack.Peek().Pieces != null && IsLeaf(node.Op))
        {
            EmitPieces(vh, scene, node, context, stack, ref emitted);
            return;
        }

        switch (node.Op)
        {
            case VecOp.Group:
            {
                var mark = Stopwatch.GetTimestamp();
                var parent = stack.Peek();
                var anchor = new Vector2(node.Ax.Evaluate(context), node.Ay.Evaluate(context));

                // `v = 0` is CSS visibility: the subtree is not there at all, so its hit
                // regions and scroll containers go with it.
                if (node.Visible != null && node.Visible.Evaluate(context) <= 0.5f)
                {
                    Charge(VecOp.Group, mark);
                    break;
                }

                // A fading group is drawn at full opacity and faded by its renderer; see AlphaOnly.
                var fades = Fades(node, context);
                var alpha = fades ? parent.Opacity : parent.Opacity * Mathf.Clamp01(node.Opacity.Evaluate(context));

                // A fully transparent group draws nothing today, child by child. Stopping at
                // the group makes a hidden subtree free instead of merely cheap, which is what
                // a page carrying two skins and showing one is made of. Skipped only when
                // nothing under it has to be reached anyway (clicks, scroll, pictures), since
                // a browser still sends a click to an `opacity: 0` element.
                if (alpha <= 0.002f && !node.Interactive)
                {
                    Charge(VecOp.Group, mark);
                    break;
                }

                var sx = node.Sx.Evaluate(context);
                var local = GroupMatrix(
                    node.Tx.Evaluate(context), node.Ty.Evaluate(context), anchor.x, anchor.y,
                    node.Rotate.Evaluate(context), sx, node.Sy.Evaluate(context), node.GroupMatrix);

                // An inherited clip was expressed in the parent's coordinates; re-express it
                // in this group's, so it keeps clipping the same area of the picture.
                var inverse = AffineInverse(local);
                var sceneToLocal = inverse * parent.SceneToLocal;
                List<Vector2>? ownOutline = null;

                if (!string.IsNullOrEmpty(node.ClipRef))
                {
                    if (scene.Clips.TryGetValue(node.ClipRef!, out var declared))
                    {
                        // Declared in scene coordinates, so bring it into this group's local
                        // space before use. With no transforms anywhere this is the identity
                        // and nothing changes.
                        var outline = new List<Vector2>(declared.Count);
                        foreach (var point in declared)
                            outline.Add(sceneToLocal.MultiplyPoint3x4(point));

                        // A rejected clip used to fall back to NO clip, which is the least
                        // safe default available: "draw everything" rather than "draw
                        // nothing" or "complain".
                        if (Mathf.Abs(Triangulator.SignedArea(outline)) < 0.000001f)
                        {
                            // A live clip that has shrunk to nothing means nothing is inside
                            // it -- a bar at 0%, a list with no room. Drawing the subtree
                            // unclipped there would flood the screen with what the clip
                            // exists to hide, so it is the one degenerate case that hides
                            // rather than reports.
                            if (scene.ClipNodes.ContainsKey(node.ClipRef!))
                            {
                                Charge(VecOp.Group, mark);
                                break;
                            }

                            scene.Problem($"clip \"{node.ClipRef}\" is degenerate; group drawn unclipped");
                        }
                        else
                        {
                            ownOutline = outline;
                        }
                    }
                    else
                    {
                        scene.Problem($"clip \"{node.ClipRef}\" is not declared in defs");
                    }
                }

                // Convex clips intersect into one region; a concave one becomes convex pieces,
                // each intersected with whatever this group inherited.
                CombineClip(parent, inverse, ownOutline, out var clip, out var pieces);
                var canvasClip = CombineCanvasClip(parent.CanvasClip,
                    ownOutline == null ? null : ToCanvas(parent.Matrix * local, ownOutline));

                var matrixScale = node.GroupMatrix is { } gm
                    ? Mathf.Sqrt(Mathf.Abs(gm[0] * gm[3] - gm[1] * gm[2]))
                    : 1f;

                stack.Push(new Frame
                {
                    Matrix = parent.Matrix * local,
                    Opacity = alpha,
                    Clip = clip,
                    Pieces = pieces,
                    CanvasClip = canvasClip,
                    Scale = parent.Scale * Mathf.Abs(sx) * matrixScale,
                    SceneToLocal = sceneToLocal,
                });

                // Filters and a mask tint every colour emitted under the group, labels included.
                var savedTint = vh.Tint;
                if (node.Filters != null || node.MaskGradient != null)
                    vh.Tint = GroupTint(vh, scene, node, context, parent.Matrix * local);

                // Charged before descending, so a group's own cost (transform build, clip
                // re-expression) is separated from its children's.
                Charge(VecOp.Group, mark);

                // Meshes of its own, so the renderer can fade exactly this and nothing else.
                var firstShape = vh.ShapeCount;
                if (fades)
                    vh.ForceCutBefore(firstShape);

                EmitGroupChildren(vh, scene, node, context, stack, ref emitted, vh.Tint == savedTint ? null : vh.Tint!.Mask);

                if (fades && vh.ShapeCount > firstShape)
                {
                    vh.ForceCutBefore(vh.ShapeCount);
                    AlphaFound!.Add(new AlphaGroup(firstShape, vh.ShapeCount, node.Opacity));
                }

                vh.Tint = savedTint;
                stack.Pop();
                break;
            }

            case VecOp.Repeat:
            {
                var instances = LodCount(node, scene, stack.Peek().Scale);

                for (var i = 0; i < instances; i++)
                {
                    if (Starved(vh, 1, "a repeat"))
                        break;

                    // `n` stays the authored count inside expressions: reducing it would
                    // change where instances sit, not just how many there are, so a thinned
                    // field would redistribute itself rather than simply thin out.
                    context.PushRepeat(i, node.RepeatCount);
                    foreach (var child in node.Children)
                        EmitNode(vh, scene, child, context, stack, ref emitted);

                    context.PopRepeat();
                }

                break;
            }

            case VecOp.Rect:
            {
                var mark = Stopwatch.GetTimestamp();
                var frame = stack.Peek();
                var outline = RectOutline(node, context, frame.Scale * ScreenScale, OutlineScratch);
                if (outline.Count >= 3)
                {
                    FillAndStroke(vh, scene, node, context, frame, outline, closed: true);
                    emitted++;
                }

                // A shape is finished, so the geometry may be cut here if it has to be split
                // across meshes. Inside a shape it may not: a feather ring indexes the fill's
                // vertices and a shadow's rings index each other.
                vh.MarkShape();
                Charge(VecOp.Rect, mark);
                break;
            }

            case VecOp.Ellipse:
            {
                var mark = Stopwatch.GetTimestamp();
                var frame = stack.Peek();
                var outline = EllipseOutline(node, context, OutlineScratch, frame.Scale);
                if (outline.Count >= 3)
                {
                    FillAndStroke(vh, scene, node, context, frame, outline, closed: true);
                    emitted++;
                }

                vh.MarkShape();
                Charge(VecOp.Ellipse, mark);
                break;
            }

            case VecOp.Band:
            {
                var mark = Stopwatch.GetTimestamp();
                EmitBand(vh, scene, node, context, stack.Peek());
                emitted++;
                vh.MarkShape();
                Charge(VecOp.Band, mark);
                break;
            }

            case VecOp.Polyline:
            case VecOp.Spline:
            case VecOp.SampledLine:
            {
                var mark = Stopwatch.GetTimestamp();
                EmitPath(vh, scene, node, context, stack.Peek());
                emitted++;
                vh.MarkShape();
                Charge(node.Op, mark);
                break;
            }

            case VecOp.Scroll:
            {
                var mark = Stopwatch.GetTimestamp();
                EmitScroll(vh, scene, node, context, stack, ref emitted);
                Charge(VecOp.Scroll, mark);
                break;
            }

            case VecOp.Image:
            {
                var mark = Stopwatch.GetTimestamp();
                EmitImage(vh, scene, node, context, stack.Peek());
                emitted++;
                Charge(VecOp.Image, mark);
                break;
            }

            case VecOp.Text:
            {
                CollectText(vh, scene, node, context, stack.Peek(), vh.ShapeCount);
                emitted++;
                break;
            }

            case VecOp.Path:
            {
                var mark = Stopwatch.GetTimestamp();
                EmitPathData(vh, scene, node, context, stack.Peek());
                emitted++;
                vh.MarkShape();
                Charge(VecOp.Path, mark);
                break;
            }
        }
    }

    /// <summary>
    /// Resolves a text node and records where it landed, in canvas space.
    /// </summary>
    /// <remarks>
    /// The rect's corners go through the frame matrix, so the node moves, scales and scrolls
    /// with its group exactly like geometry does. Rotation is recovered from the matrix
    /// rather than tracked separately, since a group's angle is already baked into it.
    ///
    /// The clip is passed on as a bounding box only. The geometric clipper reshapes contours
    /// before triangulation, which a TMP child is not made of; a rect is what RectMask2D can
    /// enforce, and anything rounder waits for the stencil path.
    /// </remarks>
    /// <summary>
    /// A group's local transform: scale, rotate, then translate about the anchor, with an
    /// optional CSS matrix() innermost.
    /// </summary>
    /// <remarks>
    /// Built by hand rather than through <c>Quaternion.Euler</c>, which is a native call: that
    /// kept every rotated group out of the headless tests. The rotation matches what
    /// <c>Matrix4x4.Rotate(Quaternion.Euler(0, 0, -r))</c> produced -- counter-clockwise in the
    /// flipped canvas, clockwise on screen, as scenes are written.
    ///
    /// CSS reads `transform: translate rotate scale matrix` left to right as matrices multiplied
    /// in that order, so the matrix is the innermost factor and acts on points first.
    /// </remarks>
    internal static Matrix4x4 GroupMatrix(float tx, float ty, float ax, float ay, float rotateDegrees, float sx, float sy, float[]? m)
    {
        var radians = -rotateDegrees * Mathf.Deg2Rad;
        var cos = Mathf.Cos(radians);
        var sin = Mathf.Sin(radians);

        var rotation = Matrix4x4.identity;
        rotation.m00 = cos;
        rotation.m01 = -sin;
        rotation.m10 = sin;
        rotation.m11 = cos;

        var css = Matrix4x4.identity;
        if (m is { Length: 6 })
        {
            // matrix(a, b, c, d, e, f): x' = a x + c y + e, y' = b x + d y + f.
            css.m00 = m[0];
            css.m10 = m[1];
            css.m01 = m[2];
            css.m11 = m[3];
            css.m03 = m[4];
            css.m13 = m[5];
        }

        return Matrix4x4.Translate(new Vector3(tx, ty, 0f)) *
               Matrix4x4.Translate(new Vector3(ax, ay, 0f)) *
               rotation *
               Matrix4x4.Scale(new Vector3(sx, sy, 1f)) *
               css *
               Matrix4x4.Translate(new Vector3(-ax, -ay, 0f));
    }

    /// <summary>Inverse of a 2-D affine matrix, without the native <c>Matrix4x4.inverse</c>.</summary>
    internal static Matrix4x4 AffineInverse(Matrix4x4 m)
    {
        var det = m.m00 * m.m11 - m.m01 * m.m10;
        if (Mathf.Abs(det) < 1e-12f)
            return Matrix4x4.identity;

        var i = Matrix4x4.identity;
        i.m00 = m.m11 / det;
        i.m01 = -m.m01 / det;
        i.m10 = -m.m10 / det;
        i.m11 = m.m00 / det;
        i.m03 = -(i.m00 * m.m03 + i.m01 * m.m13);
        i.m13 = -(i.m10 * m.m03 + i.m11 * m.m13);
        return i;
    }

    /// <summary>The tint a group applies: its filters, evaluated now, over its parent's.</summary>
    private static VertexTint GroupTint(MeshBuilder vh, VecScene scene, VecNode node, EvalContext context, Matrix4x4 groupMatrix)
    {
        var ops = new List<int>();
        var amounts = new List<float>();

        if (node.Filters != null)
        {
            foreach (var (op, amount) in node.Filters)
            {
                ops.Add(op);
                amounts.Add(amount.Evaluate(context));
            }
        }

        MaskInfo? mask = null;
        if (node.MaskGradient != null)
        {
            if (scene.Gradients.TryGetValue(node.MaskGradient, out var gradient))
                mask = new MaskInfo { Gradient = gradient.AtUse(context), CanvasToLocal = AffineInverse(groupMatrix) };
            else
                scene.Problem($"mask gradient \"{node.MaskGradient}\" is not declared in defs");
        }

        return new VertexTint(vh.Tint, ops, amounts, mask);
    }

    /// <summary>Emits a group's children, then multiplies its mask into what they drew.</summary>
    /// <remarks>
    /// The mask is applied after the children rather than per vertex as they are added, because
    /// a `units = "bbox"` mask spans the group's content and that is only known once it exists.
    /// Labels read the same <see cref="MaskInfo"/> later, by which time its bounds are set.
    /// </remarks>
    private static void EmitGroupChildren(MeshBuilder vh, VecScene scene, VecNode node, EvalContext context, Stack<Frame> stack, ref int emitted, MaskInfo? mask)
    {
        var from = vh.currentVertCount;
        var labelsFrom = TextFound?.Count ?? 0;

        foreach (var child in node.Children)
            EmitNode(vh, scene, child, context, stack, ref emitted);

        if (mask != null)
        {
            List<Rect>? labels = null;
            if (TextFound != null && TextFound.Count > labelsFrom)
            {
                labels = new List<Rect>(TextFound.Count - labelsFrom);
                for (var i = labelsFrom; i < TextFound.Count; i++)
                    labels.Add(TextFound[i].Rect);
            }

            vh.MaskRange(from, mask, NeedsRefinement(new Paint(Color.white, mask.Gradient, 1f)), ScreenScale, labels);
        }
    }

    /// <summary>
    /// A conic fill as wedges from its centre, each coloured within its own span of angles.
    /// </summary>
    /// <remarks>
    /// Vertex colours cannot cross the seam where 360 degrees meets 0: a triangle spanning it
    /// blends from one end of the ramp to the other, straight through every colour between.
    /// Wedges start exactly at the gradient's start angle, and each vertex's angle is taken
    /// relative to its own wedge's middle, so no triangle ever interpolates across the seam --
    /// and a ramp whose ends differ gets the hard edge CSS draws there.
    ///
    /// Wedges are cut from a convex outline with the convex clipper, so this path needs a convex
    /// shape; anything else falls back to triangulation with subdivision.
    /// </remarks>
    private static void FillConicWedges(MeshBuilder vh, List<Vector2> contour, Paint paint, Matrix4x4 matrix, float scale)
    {
        var gradient = paint.Gradient!;
        var region = ClipRegion.FromPolygon(contour);
        if (region == null)
            return;

        var centre = paint.FromGradientSpace(gradient.Start);

        var radius = 0f;
        foreach (var point in contour)
            radius = Mathf.Max(radius, (point - centre).magnitude);

        if (radius < 0.0001f)
            return;

        var wedges = ConicWedges(radius * scale * ScreenScale, gradient.StopCount);
        var step = 360f / wedges;
        var reach = radius * 2f + 1f;

        var triangle = QuadScratch;
        var piece = new List<Vector2>(8);

        for (var k = 0; k < wedges; k++)
        {
            var from = gradient.Angle + k * step;
            var middle = from + step * 0.5f;

            triangle.Clear();
            triangle.Add(centre);
            triangle.Add(centre + Direction(from) * reach);
            triangle.Add(centre + Direction(from + step) * reach);

            var cut = region.ClipPolygon(triangle, piece);
            if (cut.Count < 3 || Starved(vh, cut.Count, "a conic gradient"))
                continue;

            var origin = vh.currentVertCount;
            foreach (var point in cut)
            {
                var offset = point - centre;
                var relative = offset.sqrMagnitude < 1e-10f ? 0f : Mathf.DeltaAngle(middle, Gradient.Heading(offset));

                Color colour = gradient.Sample(ConicWedgeParameter(k, step, relative));
                colour.a *= paint.Alpha;
                vh.AddVert(matrix.MultiplyPoint3x4(point), colour, Vector2.zero);
            }

            for (var i = 1; i < cut.Count - 1; i++)
                vh.AddTriangle(origin, origin + i, origin + i + 1);
        }
    }

    /// <summary>Wedge count: ~2.5 screen px of arc at the rim, enough for the stops, 36..360.</summary>
    internal static int ConicWedges(float screenRadius, int stopCount)
    {
        var byArc = Mathf.CeilToInt(2f * Mathf.PI * Mathf.Max(0f, screenRadius) / 2.5f);
        var byStops = 8 * Mathf.Max(1, stopCount - 1);
        return Mathf.Clamp(Mathf.Max(byArc, byStops), 36, 360);
    }

    /// <summary>
    /// The ramp parameter of a vertex in wedge <paramref name="k"/>, from its angle relative to
    /// the wedge's middle. Never wraps: the result stays within the wedge's own span.
    /// </summary>
    internal static float ConicWedgeParameter(int k, float step, float relativeDegrees)
    {
        var within = Mathf.Clamp(relativeDegrees, -step * 0.5f, step * 0.5f);
        return (k * step + step * 0.5f + within) / 360f;
    }

    /// <summary>Unit vector at a heading, degrees clockwise from twelve o'clock, scene space.</summary>
    private static Vector2 Direction(float degrees)
    {
        var radians = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(radians), -Mathf.Cos(radians));
    }

    /// <summary>
    /// Where a label goes and how far it is turned, from the frame carrying it.
    /// </summary>
    /// <remarks>
    /// The box is the label's UNROTATED size placed at its transformed centre; TextLayer turns it
    /// by <paramref name="rotation"/>. Taking the bounds of two transformed corners instead -- as
    /// this once did -- is only right at 0 degrees: under a rotating group the box changed shape
    /// as a needle swung, 45 degrees making it square whatever the text.
    ///
    /// The rotation is NOT negated. The viewbox matrix already flips scene +Y-down into canvas
    /// +Y-up, so the angle read back out of it is in UGUI's counter-clockwise convention.
    /// Negating it a second time spun every label the opposite way to the group carrying it: a
    /// dial's readout turned against its needle, correct only at 0 degrees. Measured before the
    /// fix: needle +60, text -60. Pinned by TextShadowTests.LabelsTurnWithTheirGroup.
    /// </remarks>
    /// <summary>
    /// The part of a frame's transform a turned, evenly scaled label box cannot carry, in the
    /// label's own space (+Y up), or null when the frame only rotates and scales evenly.
    /// </summary>
    /// <remarks>
    /// The matrix maps scene space (+Y down) to canvas (+Y up), so the label's up is scene -Y:
    /// its axes land on c1 = M(1,0) and c2 = M(0,-1). Expressed in the turned basis (u along c1,
    /// v a quarter turn from it) and divided by the scale the label is laid out at, that is
    /// [[|c1|, c2.u], [0, c2.v]] / scale -- the identity for any rotation and even scale.
    /// </remarks>
    internal static Vector4? LabelShear(Matrix4x4 matrix, float scale)
    {
        if (scale <= 0.0001f)
            return null;

        var c1 = new Vector2(matrix.m00, matrix.m10);
        var c2 = new Vector2(-matrix.m01, -matrix.m11);
        var length = c1.magnitude;
        if (length < 1e-6f)
            return null;

        var u = c1 / length;
        var v = new Vector2(-u.y, u.x);

        var a = length / scale;
        var b = Vector2.Dot(c2, u) / scale;
        var d = Vector2.Dot(c2, v) / scale;

        if (Mathf.Abs(a - 1f) < 0.001f && Mathf.Abs(b) < 0.001f && Mathf.Abs(d - 1f) < 0.001f)
            return null;

        return new Vector4(a, b, 0f, d);
    }

    internal static Rect LabelBox(Matrix4x4 matrix, float x, float y, float w, float h, out float rotation)
    {
        var centre = matrix.MultiplyPoint3x4(new Vector2(x + w * 0.5f, y + h * 0.5f));
        var across = matrix.MultiplyVector(new Vector3(w, 0f, 0f)).magnitude;
        var down = matrix.MultiplyVector(new Vector3(0f, h, 0f)).magnitude;

        rotation = Mathf.Atan2(matrix.m10, matrix.m00) * Mathf.Rad2Deg;
        return new Rect(centre.x - across * 0.5f, centre.y - down * 0.5f, across, down);
    }

    private static void CollectText(MeshBuilder vh, VecScene scene, VecNode node, EvalContext context, Frame frame, int shapeIndex)
    {
        if (TextFound == null || NoFill || _repeatPiece)
            return;

        // Note an invisible `T` is still collected. The label pool is keyed by placement
        // ORDER, so skipping one would shift every label after it onto the wrong text --
        // the saving is a TMP update, the cost is a scene that shows the wrong numbers.
        var body = node.TextLiteral;
        if (node.TextParts != null)
            body = BindTemplate(node, context, scene, TextFound.Count);
        else if (node.TextData != null)
            body = BindText(node, context, scene, TextFound.Count);

        if (string.IsNullOrEmpty(body))
            return;

        var x = node.X.Evaluate(context);
        var y = node.Y.Evaluate(context);
        var w = node.W.Evaluate(context);
        var h = node.H.Evaluate(context);

        // The label's box is its UNROTATED size placed at its transformed centre; TextLayer turns
        // it by Rotation below. Taking the bounds of two transformed corners instead -- as this
        // did -- is only right at 0 degrees: under a rotating group the box changed shape as the
        // needle swung, 45 degrees making it square whatever the text.
        var box = LabelBox(frame.Matrix, x, y, w, h, out var rotation);

        // A skew or a one-axis stretch cannot be expressed by a RectTransform, which only turns
        // and scales evenly: `m=[1,0,-0.5,1,0,0]` sheared the box around upright letters. The
        // label is laid out at the frame's even scale instead and the rest goes to the glyphs.
        //
        // Only the SCENE's own transforms count, not the viewbox fit. With `fit=stretch` into a box
        // of another shape the fit scales X and Y differently; shapes follow it, and text never
        // has -- it keeps its letterforms. Taking the fit into the shear squeezed every label in
        // such a scene (example 14, seen in game 2026-09-15). The fit is undone first.
        var sceneMatrix = Matrix4x4.Scale(new Vector3(1f, -1f, 1f)) * _viewboxInverse * frame.Matrix;
        var shear = LabelShear(sceneMatrix, frame.Scale / _viewboxScaleX);
        if (shear.HasValue)
        {
            var centre = box.center;
            var across = w * frame.Scale;
            var down = h * frame.Scale * _viewboxRatio;
            box = new Rect(centre.x - across * 0.5f, centre.y - down * 0.5f, across, down);
        }

        var paint = ResolvePaint(scene, node, context, frame, stroke: false);

        // Group opacity and `fo` reach the label through its own alpha, because they cannot
        // reach it any other way: a label is a TMP object with its own geometry, so no amount
        // of mesh drawn around it can fade it. True whatever the draw order -- `ztext` decides
        // whether a shape can COVER a label, never whether it can tint one. Without this a
        // `G o=0.3` fades all its artwork and leaves the text -- the readable part -- at full
        // strength, which is the opposite of what was asked for.
        // ResolvePaint has already multiplied `fo` and the group's opacity into the paint. This
        // line used to multiply them in a second time, so text at `o=0.5` drew at 0.25.
        Color colour = paint.At(new Vector2(x, y));

        // A gradient fill is looked up per glyph vertex by the text layer. The label itself is
        // then white at the opacity a flat colour would have had, so rich-text colours still
        // multiply and fading is unchanged.
        TextGradient? textGradient = null;
        if (paint.IsGradient)
        {
            var full = new Paint(Color.white, paint.Gradient, 1f);
            textGradient = new TextGradient
            {
                Paint = full.WithBounds(new Vector2(x, y), new Vector2(w, h)),
                CanvasToLocal = AffineInverse(frame.Matrix),
            };

            colour = new Color(1f, 1f, 1f, colour.a);
        }

        // A label faded to nothing is not placed at all. TMP work is main-thread work, and
        // the two-shapes-with-opposing-opacity idiom means a scene showing one of two states
        // carries both. Safe because the pool reassigns every property of the object it
        // takes, so a shifted placement inherits nothing from the previous occupant.
        if (colour.a <= 0.002f)
            return;

        // RectMask2D serves an axis-aligned rectangle; anything else also carries its outline,
        // which TextLayer masks with a stencil.
        Rect? clip = null;
        List<Vector2>? clipPolygon = null;
        if (frame.CanvasClip is { Count: >= 3 } outline)
        {
            clip = BoundsOf(outline);
            if (!IsAxisAlignedRect(outline, clip.Value))
                clipPolygon = outline;
        }

        // The first outset shadow goes to the label's own underlay (or its offset copy); later
        // ones each get a copy of the label; an inset one is drawn by a copy laid over it.
        VecShadow? textShadow = null;
        VecShadow? insetShadow = null;
        List<VecShadow>? extraShadows = null;
        if (node.Shadows is { Length: > 0 })
        {
            foreach (var entry in node.Shadows)
            {
                // Scaled here, with the same factor as the font size, so TextLayer receives
                // canvas units and does not need the frame.
                var scaled = new VecShadow(
                    entry.Dx * frame.Scale,
                    entry.Dy * frame.Scale,
                    entry.Blur * frame.Scale,
                    entry.Spread * frame.Scale,
                    vh.Tint?.ApplyFilters(entry.Colour) ?? entry.Colour,
                    entry.Inset);

                if (entry.Inset)
                {
                    if (insetShadow == null)
                        insetShadow = scaled;
                    else
                        scene.Problem($"T{(string.IsNullOrEmpty(node.Id) ? "" : " \"" + node.Id + "\"")}: text takes one inset shadow; later ones not drawn");
                }
                else if (textShadow == null)
                {
                    textShadow = scaled;
                }
                else
                {
                    (extraShadows ??= new List<VecShadow>()).Add(scaled);
                }
            }
        }

        TextFound.Add(new TextPlacement
        {
            Text = body!,
            Rect = box,
            Size = (node.TextSize?.Evaluate(context) ?? 12f) * frame.Scale,
            Colour = colour,
            Align = node.Align,
            VAlign = node.VAlign,
            Font = node.FontFamily,
            Bold = node.Bold,
            CharSpacing = node.CharSpacing,
            Fit = node.Fit,
            MinSize = (node.MinSize?.Evaluate(context) ?? 6f) * frame.Scale,
            Rotation = rotation,
            Wrap = node.Wrap,
            LineHeight = node.LineHeight,
            Shadow = textShadow,
            OutlineWidth = Mathf.Max(0f, node.TextOutlineWidth?.Evaluate(context) ?? 0f) * frame.Scale,
            OutlineColour = node.TextOutlineColour,
            ExtraShadows = extraShadows?.ToArray(),
            InsetShadow = insetShadow,
            ClipRect = clip,
            ClipPolygon = clipPolygon,
            ShapeIndex = shapeIndex,
            Tint = vh.Tint,
            Shear = shear,
            Gradient = textGradient,
            FirstLine = node.FirstLine?.Scaled(frame.Scale),
        });
    }

    /// <summary>
    /// An image: its box (with corner radii) filled with the texture, as its own shape between
    /// two forced mesh cuts so its mesh can carry the texture alone.
    /// </summary>
    private static void EmitImage(MeshBuilder vh, VecScene scene, VecNode node, EvalContext context, Frame frame)
    {
        var src = node.ImageSource;
        if (string.IsNullOrEmpty(src))
            return;

        if (!ImageCache.TryGet(src!, out var texW, out var texH, out var error))
        {
            if (error != null)
                scene.Problem($"IMG \"{src}\": {error}");
            else if (!_repeatPiece)
                ImagesFound?.Add(new ImagePlacement { Src = src!, ShapeIndex = -1 });

            return;
        }

        var x = node.X.Evaluate(context);
        var y = node.Y.Evaluate(context);
        var w = node.W.Evaluate(context);
        var h = node.H.Evaluate(context);
        var box = RectOutline(node, context, frame.Scale * ScreenScale, new List<Vector2>(64));
        if (box.Count < 3 || texW <= 0 || texH <= 0)
            return;

        // The natural size fit works from is the cropped part's, not the whole texture's.
        var crop = node.ImageCrop;
        var natW = Mathf.Max(1, Mathf.RoundToInt(texW * crop.width));
        var natH = Mathf.Max(1, Mathf.RoundToInt(texH * crop.height));
        var ax = node.ImageAtX.Evaluate(context);
        var ay = node.ImageAtY.Evaluate(context);
        var ox = node.ImageOffX.Evaluate(context);
        var oy = node.ImageOffY.Evaluate(context);

        // `tile`: one picture of the tile's size placed by at/off, repeated to cover the box.
        // Tiled in geometry rather than with the texture's wrap mode, which is shared by every
        // console showing that source and would repeat the whole texture rather than a `uv` crop.
        var tiles = 1;
        var across = 1;
        var tileStep = Vector2.zero;
        Rect picture;
        var bounds = BoundsOf(box);

        if (node.ImageSlice != null)
        {
            // Nine-slice draws over the box itself; `fit`, `at`, `off` and `tile` do not apply.
            picture = new Rect(x, y, w, h);
            tiles = 9;
        }
        else if (node.ImageTileW != null)
        {
            var (tileW, tileH) = TileSize(node.ImageTileW.Evaluate(context), node.ImageTileH!.Evaluate(context),
                natW, natH, w, h, node.ImageTileFit, node.ImageRepeatX, node.ImageRepeatY);

            var (firstX, stepX, countX) = TileAxis(node.ImageRepeatX, x, w, bounds.xMin, bounds.xMax, tileW, ax, ox);
            var (firstY, stepY, countY) = TileAxis(node.ImageRepeatY, y, h, bounds.yMin, bounds.yMax, tileH, ay, oy);
            across = countX;

            if ((long)countX * countY > MaxImageTiles)
            {
                scene.Problem($"IMG \"{src}\": tile makes {(long)countX * countY} tiles, more than {MaxImageTiles}; drawn untiled");
                picture = ImageRect(x, y, w, h, natW, natH, node.ImageFit, ax, ay, ox, oy);
            }
            else
            {
                picture = new Rect(firstX, firstY, tileW, tileH);
                tileStep = new Vector2(stepX, stepY);
                tiles = countX * countY;
                if (tiles <= 0)
                    return;
            }
        }
        else
        {
            picture = ImageRect(x, y, w, h, natW, natH, node.ImageFit, ax, ay, ox, oy);
        }

        var alpha = Mathf.Clamp01(node.Opacity.Evaluate(context)) * frame.Opacity;
        Color32 colour = new Color(1f, 1f, 1f, alpha);

        var region = ClipRegion.FromPolygon(box);
        if (region == null)
            return;

        // Filters act on vertex colours, and an image's colour is in its texels: filtering the
        // white vertex would tint the picture flat rather than, say, invert it. So an image is
        // left unfiltered and says so. A group mask still applies, since that is alpha.
        var tint = vh.Tint;
        if (tint is { HasFilters: true })
            scene.Problem($"IMG \"{src}\": colour filters do not apply to images; drawn unfiltered");

        vh.Tint = null;

        // Where a picture does not cover its box -- contain, none, a tile, or any picture moved
        // by `off` -- the drawn area is the box cut to the picture, which CSS leaves empty. Drawn
        // whole, the texture's edge texels would smear across the gap.
        var piece = new List<Vector2>(4);
        var cut = new List<Vector2>(box.Count + 8);
        var clipped = new List<Vector2>(box.Count + 8);
        var shapeIndex = -1;

        // Every quad the picture is drawn as, with the part of the texture it shows.
        var pieces = _imagePieces ??= new List<(Rect At, Rect Crop)>(16);
        pieces.Clear();

        if (node.ImageSlice != null)
        {
            for (var k = 0; k < 9; k++)
            {
                var part = picture;
                var partCrop = crop;
                if (SlicePiece(node, k, picture, natW, natH, ref part, ref partCrop))
                    SliceTiles(node, k, picture, part, partCrop, crop, natW, natH, texW, texH, pieces);
            }

            if (pieces.Count > MaxImageTiles)
            {
                scene.Problem($"IMG \"{src}\": srep makes {pieces.Count} pieces, more than {MaxImageTiles}; edges stretched");
                pieces.Clear();
                for (var k = 0; k < 9; k++)
                {
                    var part = picture;
                    var partCrop = crop;
                    if (SlicePiece(node, k, picture, natW, natH, ref part, ref partCrop))
                        pieces.Add((part, partCrop));
                }
            }
        }
        else
        {
            for (var t = 0; t < tiles; t++)
            {
                pieces.Add((tiles == 1 ? picture
                    : new Rect(picture.xMin + (t % across) * tileStep.x, picture.yMin + (t / across) * tileStep.y,
                               picture.width, picture.height), crop));
            }
        }

        foreach (var (at, pieceCrop) in pieces)
        {

            List<Vector2> shape;
            if (at.xMin <= bounds.xMin + 0.001f && at.yMin <= bounds.yMin + 0.001f
                && at.xMax >= bounds.xMax - 0.001f && at.yMax >= bounds.yMax - 0.001f)
            {
                shape = box;
            }
            else
            {
                piece.Clear();
                piece.Add(new Vector2(at.xMin, at.yMin));
                piece.Add(new Vector2(at.xMax, at.yMin));
                piece.Add(new Vector2(at.xMax, at.yMax));
                piece.Add(new Vector2(at.xMin, at.yMax));
                shape = region.ClipPolygon(piece, cut);
            }

            if (frame.Clip != null)
                shape = frame.Clip.ClipPolygon(shape, clipped);

            if (shape.Count < 3 || Starved(vh, shape.Count, "an image"))
                continue;

            // A mesh of its own: cut before the first piece, draw, cut after.
            if (shapeIndex < 0)
            {
                vh.ForceCutBefore(vh.ShapeCount);
                shapeIndex = vh.ShapeCount;
            }

            var origin = vh.currentVertCount;
            foreach (var point in shape)
                vh.AddVert(frame.Matrix.MultiplyPoint3x4(point), colour, ImageUv(at, point, pieceCrop));

            for (var i = 1; i < shape.Count - 1; i++)
                vh.AddTriangle(origin, origin + i, origin + i + 1);
        }

        vh.Tint = tint;
        if (shapeIndex < 0)
            return;

        vh.MarkShape();
        vh.ForceCutBefore(vh.ShapeCount);

        // Every piece of a concave clip is its own mesh, so each is recorded for its texture.
        ImagesFound?.Add(new ImagePlacement { Src = src!, ShapeIndex = shapeIndex });
    }

    /// <summary>
    /// One of a nine-slice's pieces, row by row from the top left: where it is drawn and which
    /// part of the texture it shows. False for a piece with no area, or the middle under `mid = 0`.
    /// </summary>
    /// <remarks>
    /// Corners are drawn at the border widths, edges stretch between them, the middle stretches
    /// both ways -- CSS border-image with `stretch`. Borders wider than the box are scaled down
    /// together, as CSS does, so opposite corners meet rather than overlap.
    /// </remarks>
    internal static bool SlicePiece(VecNode node, int index, Rect box, int natW, int natH, ref Rect at, ref Rect crop)
    {
        var column = index % 3;
        var row = index / 3;
        if (column == 1 && row == 1 && !node.ImageMiddle)
            return false;

        var slice = node.ImageSlice!;
        var border = node.ImageBorder!;
        var fit = Mathf.Min(1f, Mathf.Min(
            border[1] + border[3] > box.width ? box.width / (border[1] + border[3]) : 1f,
            border[0] + border[2] > box.height ? box.height / (border[0] + border[2]) : 1f));

        float Edge(int i, float start, float size, float near, float far) =>
            i == 0 ? start : i == 1 ? start + near * fit : i == 2 ? start + size - far * fit : start + size;

        // Texture fractions of the (cropped) picture, clamped so over-large slices cannot cross.
        float Cut(int i, float near, float far, int texels) =>
            i == 0 ? 0f : i == 3 ? 1f
            : i == 1 ? Mathf.Clamp01(near / texels) : Mathf.Max(Mathf.Clamp01(near / texels), 1f - Mathf.Clamp01(far / texels));

        var x0 = Edge(column, box.xMin, box.width, border[3], border[1]);
        var x1 = Edge(column + 1, box.xMin, box.width, border[3], border[1]);
        var y0 = Edge(row, box.yMin, box.height, border[0], border[2]);
        var y1 = Edge(row + 1, box.yMin, box.height, border[0], border[2]);
        if (x1 - x0 <= 0.0001f || y1 - y0 <= 0.0001f)
            return false;

        var u0 = Cut(column, slice[3], slice[1], natW);
        var u1 = Cut(column + 1, slice[3], slice[1], natW);
        var v0 = Cut(row, slice[0], slice[2], natH);
        var v1 = Cut(row + 1, slice[0], slice[2], natH);

        at = Rect.MinMaxRect(x0, y0, x1, y1);
        crop = new Rect(crop.xMin + u0 * crop.width, crop.yMin + v0 * crop.height,
                        (u1 - u0) * crop.width, (v1 - v0) * crop.height);
        return true;
    }

    /// <summary>
    /// A tile's size: the `tile` numbers, the picture's own size where both are 0, its aspect kept
    /// where one is (CSS `background-size: 50px auto`), or fitted to the box for contain/cover.
    /// Then a `round` axis is stretched so a whole number of tiles fills the box.
    /// </summary>
    internal static (float W, float H) TileSize(float tileW, float tileH, int natW, int natH, float boxW, float boxH,
                                                int fit, int repeatX, int repeatY)
    {
        var aspect = natH > 0 ? natW / (float)natH : 1f;
        float tw, th;

        if (fit != 0)
        {
            var scale = fit == 1 ? Mathf.Min(boxW / natW, boxH / natH) : Mathf.Max(boxW / natW, boxH / natH);
            tw = natW * scale;
            th = natH * scale;
        }
        else if (tileW <= 0f && tileH <= 0f)
        {
            tw = natW;
            th = natH;
        }
        else if (tileW <= 0f)
        {
            th = tileH;
            tw = th * aspect;
        }
        else if (tileH <= 0f)
        {
            tw = tileW;
            th = tw / aspect;
        }
        else
        {
            tw = tileW;
            th = tileH;
        }

        // `round` rescales so whole tiles fill the box. An axis whose size came from the aspect
        // follows the rescale, as CSS keeps an `auto` axis in proportion.
        var autoW = fit == 0 && tileW <= 0f && tileH > 0f;
        var autoH = fit == 0 && tileH <= 0f && tileW > 0f;

        if (repeatX == 2 && tw > 0f && boxW > 0f)
        {
            var rounded = boxW / Mathf.Max(1f, Mathf.Round(boxW / tw));
            if (autoH && repeatY != 2)
                th *= rounded / tw;
            tw = rounded;
        }

        if (repeatY == 2 && th > 0f && boxH > 0f)
        {
            var rounded = boxH / Mathf.Max(1f, Mathf.Round(boxH / th));
            if (autoW && repeatX != 2)
                tw *= rounded / th;
            th = rounded;
        }

        return (Mathf.Max(0.0001f, tw), Mathf.Max(0.0001f, th));
    }

    /// <summary>
    /// Where the tiles along one axis start, how far apart they are and how many there are.
    /// </summary>
    /// <remarks>
    /// `repeat` and `round` run from the anchored copy in both directions until the drawn area is
    /// covered; `once` is the anchored copy alone; `space` fits as many whole tiles as the box
    /// holds, first and last against its edges, and falls back to `once` when fewer than two fit.
    /// </remarks>
    internal static (float First, float Step, int Count) TileAxis(int mode, float boxStart, float boxLength,
                                                                  float coverMin, float coverMax, float size, float at, float off)
    {
        var anchor = boxStart + (boxLength - size) * at + off;

        if (mode == 3)
        {
            var fits = Mathf.FloorToInt(boxLength / size + 0.0001f);
            if (fits >= 2)
                return (boxStart, size + (boxLength - fits * size) / (fits - 1), fits);

            mode = 1;
        }

        if (mode == 1)
            return (anchor, size, 1);

        var first = anchor + Mathf.Floor((coverMin - anchor) / size) * size;
        return (first, size, Mathf.Max(0, Mathf.CeilToInt((coverMax - first) / size - 0.0001f)));
    }

    [ThreadStatic] private static List<(Rect At, Rect Crop)>? _imagePieces;

    /// <summary>
    /// One nine-slice region, split into tiles when its `srep` mode is not `stretch`.
    /// </summary>
    /// <remarks>
    /// CSS `border-image-repeat`: an edge's tiles keep the source part's proportions at the
    /// border's width -- the top and bottom edges scale by their own height, the left and right by
    /// their own width, and the middle takes the top edge's scale across and the left edge's
    /// down. `repeat` centres the tiles and cuts the ends, `round` resizes them so whole tiles fit,
    /// `space` spreads whole tiles with even gaps. A tile cut by its region shows the matching
    /// part of the texture, so nothing is squeezed.
    /// </remarks>
    private static void SliceTiles(VecNode node, int index, Rect box, Rect region, Rect regionCrop, Rect crop,
                                   int natW, int natH, int texW, int texH, List<(Rect At, Rect Crop)> into)
    {
        var column = index % 3;
        var row = index / 3;
        var modeX = column == 1 ? node.ImageSliceRepeatX : 4;
        var modeY = row == 1 ? node.ImageSliceRepeatY : 4;

        var texelsW = regionCrop.width * texW;
        var texelsH = regionCrop.height * texH;

        // Scale from source texels to scene units: this piece's own for an edge, the top (or left)
        // edge's for the middle.
        var scaleX = row == 1 ? EdgeScale(node, 1, 7, box, natW, natH, texW, texH, crop, vertical: true)
            : texelsH > 0f ? region.height / texelsH : 0f;
        var scaleY = column == 1 ? EdgeScale(node, 3, 5, box, natW, natH, texW, texH, crop, vertical: false)
            : texelsW > 0f ? region.width / texelsW : 0f;

        if (scaleX <= 0f || texelsW <= 0f)
            modeX = 4;
        if (scaleY <= 0f || texelsH <= 0f)
            modeY = 4;

        if (modeX == 4 && modeY == 4)
        {
            into.Add((region, regionCrop));
            return;
        }

        var (firstX, stepX, countX, sizeX) = SliceAxis(modeX, region.xMin, region.width, texelsW * scaleX);
        var (firstY, stepY, countY, sizeY) = SliceAxis(modeY, region.yMin, region.height, texelsH * scaleY);

        for (var j = 0; j < countY; j++)
        {
            for (var i = 0; i < countX; i++)
            {
                var tile = new Rect(firstX + i * stepX, firstY + j * stepY, sizeX, sizeY);
                var x0 = Mathf.Max(tile.xMin, region.xMin);
                var x1 = Mathf.Min(tile.xMax, region.xMax);
                var y0 = Mathf.Max(tile.yMin, region.yMin);
                var y1 = Mathf.Min(tile.yMax, region.yMax);
                if (x1 - x0 <= 0.0001f || y1 - y0 <= 0.0001f)
                    continue;

                var u0 = (x0 - tile.xMin) / tile.width;
                var u1 = (x1 - tile.xMin) / tile.width;
                var v0 = (y0 - tile.yMin) / tile.height;
                var v1 = (y1 - tile.yMin) / tile.height;
                into.Add((Rect.MinMaxRect(x0, y0, x1, y1),
                    new Rect(regionCrop.xMin + u0 * regionCrop.width, regionCrop.yMin + v0 * regionCrop.height,
                             (u1 - u0) * regionCrop.width, (v1 - v0) * regionCrop.height)));
            }
        }
    }

    /// <summary>The scale of the first usable of two edge pieces, along the axis across the edge.</summary>
    private static float EdgeScale(VecNode node, int first, int second, Rect box, int natW, int natH, int texW, int texH,
                                   Rect crop, bool vertical)
    {
        foreach (var index in new[] { first, second })
        {
            var region = box;
            var pieceCrop = crop;
            if (!SlicePiece(node, index, box, natW, natH, ref region, ref pieceCrop))
                continue;

            var texels = vertical ? pieceCrop.height * texH : pieceCrop.width * texW;
            var length = vertical ? region.height : region.width;
            if (texels > 0f && length > 0f)
                return length / texels;
        }

        return 0f;
    }

    /// <summary>Tiles along one axis of a nine-slice region: first, step, count and tile size.</summary>
    internal static (float First, float Step, int Count, float Size) SliceAxis(int mode, float start, float length, float size)
    {
        if (mode == 4 || size <= 0f)
            return (start, length, 1, length);

        if (mode == 2)
            size = length / Mathf.Max(1f, Mathf.Round(length / size));

        // Repeat is centred on the region, as CSS centres border-image tiles. Round fills it with
        // whole tiles, so they start at its edge; centring an even count would cut both ends.
        var (first, step, count) = TileAxis(mode == 3 ? 3 : 0, start, length, start, start + length, size,
                                            mode == 2 ? 0f : 0.5f, 0f);
        return (first, step, count, size);
    }

    /// <summary>Tiles one `IMG` may draw; past it the picture is drawn once and a problem logged.</summary>
    private const int MaxImageTiles = 4096;

    /// <summary>
    /// Where the picture sits in its box: the fit decides its size, the alignment decides
    /// which part of the free space it takes.
    /// </summary>
    /// <remarks>
    /// <paramref name="ax"/> and <paramref name="ay"/> are fractions of the free space, 0.5
    /// each being centred -- so 0 is flush left or top, 1 flush right or bottom, and for
    /// `cover`, where the free space is negative, the same numbers choose which edge of the
    /// picture is kept. `fill` leaves nothing free, so they do not apply there. `ox`/`oy` are
    /// scene units added afterwards, fill included -- CSS `right 10px` is `at` 1 and `off` -10.
    ///
    /// Fits are CSS object-fit: fill (0), contain (1), cover (2), none (3, one scene unit per
    /// texel) and scale-down (4, contain when the picture is larger than the box, else none).
    /// </remarks>
    internal static Rect ImageRect(float x, float y, float w, float h, int texW, int texH, int fit,
                                   float ax = 0.5f, float ay = 0.5f, float ox = 0f, float oy = 0f)
    {
        if (fit == 0 || w <= 0f || h <= 0f)
            return new Rect(x + ox, y + oy, w, h);

        if (fit == 4)
            fit = texW > w || texH > h ? 1 : 3;

        float pw, ph;
        if (fit == 3)
        {
            pw = texW;
            ph = texH;
        }
        else
        {
            var boxAspect = w / h;
            var picAspect = texW / (float)texH;

            // Contain fits the long side; cover fills the short one and overflows the other.
            var widthLimited = fit == 1 ? picAspect > boxAspect : picAspect < boxAspect;
            pw = widthLimited ? w : h * picAspect;
            ph = widthLimited ? w / picAspect : h;
        }

        return new Rect(x + (w - pw) * ax + ox, y + (h - ph) * ay + oy, pw, ph);
    }

    /// <summary>Texture coordinate of a scene point. Scenes are +Y down, textures +V up.</summary>
    internal static Vector2 ImageUv(Rect picture, Vector2 point)
    {
        return ImageUv(picture, point, new Rect(0f, 0f, 1f, 1f));
    }

    /// <summary>The same, within a crop of the texture given top-down, as `uv` is written.</summary>
    internal static Vector2 ImageUv(Rect picture, Vector2 point, Rect crop)
    {
        var fx = (point.x - picture.xMin) / Mathf.Max(0.0001f, picture.width);
        var fy = (point.y - picture.yMin) / Mathf.Max(0.0001f, picture.height);
        return new Vector2(crop.xMin + fx * crop.width, 1f - (crop.yMin + fy * crop.height));
    }

    private static Rect BoundsOf(List<Vector2> points)
    {
        var min = points[0];
        var max = points[0];
        foreach (var point in points)
        {
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    /// <summary>True when every point sits on the edge of <paramref name="bounds"/> and it has four corners' worth of area.</summary>
    internal static bool IsAxisAlignedRect(List<Vector2> points, Rect bounds)
    {
        const float Tolerance = 0.01f;
        foreach (var p in points)
        {
            var onX = Mathf.Abs(p.x - bounds.xMin) < Tolerance || Mathf.Abs(p.x - bounds.xMax) < Tolerance;
            var onY = Mathf.Abs(p.y - bounds.yMin) < Tolerance || Mathf.Abs(p.y - bounds.yMax) < Tolerance;
            if (!(onX && onY))
                return false;
        }

        return Mathf.Abs(Mathf.Abs(Triangulator.SignedArea(points)) - bounds.width * bounds.height) < Tolerance * (bounds.width + bounds.height + 1f);
    }

    private static List<Vector2> RectOutline(VecNode node, EvalContext context, float scale)
    {
        return RectOutline(node, context, scale, new List<Vector2>(64));
    }

    private static List<Vector2> RectOutline(VecNode node, EvalContext context, float scale, List<Vector2> outline)
    {
        outline.Clear();

        // NoEval: fixed geometry, so the cost of evaluating attributes is removed while the
        // same number of shapes and vertices is still produced.
        var x = NoEval ? 40f : node.X.Evaluate(context);
        var y = NoEval ? 40f : node.Y.Evaluate(context);
        var w = NoEval ? 2f : node.W.Evaluate(context);
        var h = NoEval ? 2f : node.H.Evaluate(context);

        if (w <= 0f || h <= 0f)
            return outline;

        var rx = Mathf.Clamp(node.Rx.Evaluate(context), 0f, w * 0.5f);
        var ry = Mathf.Clamp(node.Ry.Evaluate(context), 0f, h * 0.5f);

        if (rx < 0.01f || ry < 0.01f)
        {
            outline.Add(new Vector2(x, y));
            outline.Add(new Vector2(x + w, y));
            outline.Add(new Vector2(x + w, y + h));
            outline.Add(new Vector2(x, y + h));
            return outline;
        }

        // Per-corner radii, CSS order tl/tr/br/bl. The arcs below run tr, br, bl, tl, so the
        // indices are shuffled rather than the drawing order changed -- the winding matters
        // to everything downstream and is not worth disturbing for readability here.
        var tl = rx;
        var tr = rx;
        var br = rx;
        var bl = rx;

        if (node.CornerRadii != null)
        {
            var half = Mathf.Min(w, h) * 0.5f;
            tl = Mathf.Clamp(node.CornerRadii[0].Evaluate(context), 0f, half);
            tr = Mathf.Clamp(node.CornerRadii[1].Evaluate(context), 0f, half);
            br = Mathf.Clamp(node.CornerRadii[2].Evaluate(context), 0f, half);
            bl = Mathf.Clamp(node.CornerRadii[3].Evaluate(context), 0f, half);
        }

        var segments = CornerSegments(Mathf.Max(Mathf.Max(tl, tr), Mathf.Max(br, bl)), scale);

        Corner(outline, new Vector2(x + w - tr, y + tr), tr, -90f, 0f, segments);
        Corner(outline, new Vector2(x + w - br, y + h - br), br, 0f, 90f, segments);
        Corner(outline, new Vector2(x + bl, y + h - bl), bl, 90f, 180f, segments);
        Corner(outline, new Vector2(x + tl, y + tl), tl, 180f, 270f, segments);
        return outline;
    }

    private static List<Vector2> EllipseOutline(VecNode node, EvalContext context)
    {
        return EllipseOutline(node, context, new List<Vector2>(MaxEllipseSegments));
    }

    /// <summary>Segments around a full ellipse, from its on-screen size.</summary>
    /// <remarks>
    /// This was a flat 32 for every ellipse, which is the wrong figure at both ends: a
    /// one-unit mote two screen pixels across got 32 vertices for a shape no rounder than a
    /// hexagon would be, while a large dial face got the same 32 and showed facets.
    ///
    /// Rounded-rect corners had adapted to on-screen size from the start; a circle is the
    /// same problem and simply never got the same treatment. Motes are the case that makes
    /// it matter: a gas console draws several hundred, and 32 verts each against ~8 is the
    /// difference between circles being affordable as particles and not.
    ///
    /// Quantised to fours so that walking toward a console does not retessellate every frame.
    /// </remarks>
    private static int EllipseSegments(float radius, float scale)
    {
        var screenRadius = Mathf.Abs(radius * scale) * ScreenScale;

        // One segment per ~3 screen pixels of circumference.
        var wanted = Mathf.CeilToInt(6.2831853f * screenRadius / 3f);
        wanted = Mathf.CeilToInt(wanted / 4f) * 4;

        return Mathf.Clamp(wanted, MinEllipseSegments, MaxEllipseSegments);
    }

    private static List<Vector2> EllipseOutline(VecNode node, EvalContext context, List<Vector2> outline, float scale = 1f)
    {
        outline.Clear();

        var rx = node.Rx.Evaluate(context);
        var ry = node.Ry.Evaluate(context);
        if (rx <= 0f || ry <= 0f)
            return outline;

        Arc(outline, new Vector2(node.X.Evaluate(context), node.Y.Evaluate(context)),
            rx, ry, 0f, 360f, EllipseSegments(Mathf.Max(rx, ry), scale), closed: true);

        return outline;
    }

    private static void FillAndStroke(MeshBuilder vh, VecScene scene, VecNode node, EvalContext context, Frame frame, List<Vector2> outline, bool closed)
    {
        if (node.Clickable && !_repeatPiece && !string.IsNullOrEmpty(node.Id))
            RecordHit(node.Id!, outline, frame.Matrix, frame.CanvasClip, context, node.Pressable);

        // Shadows first: they sit beneath the shape, and in declaration order like CSS.
        if (closed)
            EmitShadows(vh, node, context, outline, frame);

        if (node.HasFill)
            FillContour(vh, node, context, frame, outline, null, ResolvePaint(scene, node, context, frame, stroke: false));

        // Inset shadows sit over the fill and under the stroke, as CSS paints them.
        if (closed)
            EmitInsetShadows(vh, scene, node, context, outline, frame);

        StrokeOutline(vh, scene, node, context, frame, outline, closed);
    }

    private static void EmitBand(MeshBuilder vh, VecScene scene, VecNode node, EvalContext context, Frame frame)
    {
        var samples = node.RepeatCount;
        if (samples < 2 || !node.HasFill)
            return;

        var sampleMark = Stopwatch.GetTimestamp();

        // Curve LOD needs the on-screen width, which needs the two end points. Evaluating
        // them first costs two samples and saves however many the distance allows.
        context.PushRepeat(0, samples);
        var firstX = node.X.Evaluate(context);
        context.PopRepeat();
        context.PushRepeat(samples - 1, samples);
        var lastX = node.X.Evaluate(context);
        context.PopRepeat();

        var used = CurveSamples(samples, lastX - firstX, frame.Scale);
        var stride = (samples - 1) / (float)(used - 1);

        var top = new List<Vector2>(used);
        var bottom = new List<Vector2>(used);

        for (var k = 0; k < used; k++)
        {
            // Fractional index: the author's expressions are continuous in `i`, so this
            // walks the same curve with fewer segments rather than a different curve.
            var i = k == used - 1 ? samples - 1 : k * stride;

            context.PushRepeat(i, samples);
            var x = node.X.Evaluate(context);
            top.Add(new Vector2(x, node.Y.Evaluate(context)));
            bottom.Add(new Vector2(x, node.Y2.Evaluate(context)));
            context.PopRepeat();
        }

        BandQuads += used - 1;
        BandSampleMs += Elapsed(sampleMark);

        // Paint is resolved INSIDE a repeat scope, matching the geometry above.
        //
        // It used to be resolved after PopRepeat, which made `i` mean two different things
        // within one node: the sample index in y/y2, but the *enclosing* repeat in fo/f.
        // Nothing warned; the wrong one silently evaluated to 0. Paint is per-shape rather
        // than per-sample so it binds to sample 0 — the point is that `i1` now means the same
        // thing in every attribute of the node.
        context.PushRepeat(0, samples);

        // Per-column opacity ramp: `fo` at the sampled edge, `fo2` at the opposite one.
        var ramped = node.EdgeOpacity != null;
        var opacityY = Mathf.Clamp01(node.FillOpacity.Evaluate(context)) * frame.Opacity;
        var opacityY2 = ramped
            ? Mathf.Clamp01(node.EdgeOpacity!.Evaluate(context)) * frame.Opacity
            : opacityY;

        var paint = ResolvePaint(scene, node, context, frame, stroke: false,
            opacityOverride: ramped ? 1f : -1f);

        var edgeFeather = EdgeFeatherWidth(node, context, frame.Scale);
        context.PopRepeat();

        // A bounding-box gradient spans the whole band, not each quad of it.
        if (paint.Gradient is { BoundingBox: true })
        {
            var min = top[0];
            var max = top[0];
            foreach (var point in top) { min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
            foreach (var point in bottom) { min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
            paint = paint.WithBounds(min, max - min);
        }

        // A band is ALREADY a strip: quads between adjacent samples, no triangulation of any
        // kind required. It briefly went through FillContour so that clipping and gradients
        // would work on it, which meant ear-clipping a closed wave contour — non-convex, so
        // it missed the convex fast path and hit an O(n^3) clipper. Measured on .NET 8: 21 us
        // for a 16-sample band, 140 us for 40 samples, against 0.9 us for a plain rect. One
        // band cost as much as a hundred rectangles, and Mono is worse.
        // A ramped band builds its paint at full opacity, so IsInvisible cannot judge it
        // from the paint alone — the ramp endpoints are the truth there.
        if (ramped ? opacityY <= 0.002f && opacityY2 <= 0.002f : IsInvisible(paint))
            return;

        if (!NoFill)
        {
            var stripMark = Stopwatch.GetTimestamp();
            EmitStrip(vh, top, bottom, paint, frame, opacityY, opacityY2, ramped);
            BandStripMs += Elapsed(stripMark);
        }

        if (edgeFeather > 0f && !NoFeather)
        {
            var featherMark = Stopwatch.GetTimestamp();
            // The ramp's opacity at the sampled edge, not the paint's. Paint is built at
            // full opacity for a ramped band, so passing it straight through would halo the
            // gas surface at brightness 1 exactly where the ramp says zero.
            FeatherEdge(vh, top, bottom, paint, edgeFeather, frame.Matrix, frame.Clip,
                ramped ? opacityY : 1f);
            BandFeatherMs += Elapsed(featherMark);
        }
    }

    /// <summary>
    /// Emits a quad strip between two edges, clipping and refining per quad when needed.
    /// </summary>
    /// <remarks>
    /// The unclipped, flat-paint case shares vertices along the strip: n samples give 2n
    /// vertices rather than 4 per quad. Clipping or a gradient that needs refinement falls
    /// back to per-quad handling, where a quad is convex and therefore cheap to clip — still
    /// linear in sample count, unlike triangulating the whole contour.
    /// </remarks>
    private static void EmitStrip(MeshBuilder vh, List<Vector2> top, List<Vector2> bottom, Paint paint, Frame frame, float opacityY = 1f, float opacityY2 = 1f, bool ramped = false)
    {
        var samples = top.Count;

        // A clip that provably contains the whole strip is a clip that changes nothing, so
        // it must not cost the shared-vertex path. Falling back to per-quad for any clip at
        // all doubled the vertices (4 per quad rather than 2 per sample) and rebuilt a list
        // per quad, for bands that were nowhere near their tank's boundary.
        var clipped = frame.Clip != null && !frame.Clip.Covers(top, bottom);

        if (!clipped && !NeedsRefinement(paint))
        {
            if (Starved(vh, samples * 2, "a band"))
                return;

            var origin = vh.currentVertCount;

            for (var i = 0; i < samples; i++)
            {
                vh.AddVert(frame.Matrix.MultiplyPoint3x4(top[i]),
                    Fade(paint.At(top[i]), opacityY, ramped), Vector2.zero);
                vh.AddVert(frame.Matrix.MultiplyPoint3x4(bottom[i]),
                    Fade(paint.At(bottom[i]), opacityY2, ramped), Vector2.zero);
            }

            for (var i = 0; i < samples - 1; i++)
            {
                var a = origin + i * 2;
                vh.AddTriangle(a, a + 1, a + 3);
                vh.AddTriangle(a, a + 3, a + 2);
            }

            return;
        }

        var quad = new List<Vector2>(4);

        for (var i = 0; i < samples - 1; i++)
        {
            quad.Clear();
            quad.Add(top[i]);
            quad.Add(top[i + 1]);
            quad.Add(bottom[i + 1]);
            quad.Add(bottom[i]);

            var piece = clipped ? frame.Clip!.ClipPolygon(quad, QuadScratch) : quad;
            if (piece.Count < 3)
                continue;

            if (NeedsRefinement(paint))
            {
                for (var j = 1; j < piece.Count - 1; j++)
                    Subdivide(vh, piece[0], piece[j], piece[j + 1], paint, frame.Matrix, 0);
            }
            else if (ramped)
            {
                // Clipping can put a vertex anywhere in the column, so the ramp is recovered
                // from its position between this column's two edges rather than from which
                // corner it started as.
                var edgeY = (top[i].y + top[i + 1].y) * 0.5f;
                var farY = (bottom[i].y + bottom[i + 1].y) * 0.5f;
                FanRamped(vh, piece, paint, frame.Matrix, edgeY, farY, opacityY, opacityY2);
            }
            else
            {
                FanShared(vh, piece, paint, frame.Matrix);
            }
        }
    }

    /// <summary>Draws a node's shadows beneath an outline, in declaration order like CSS.</summary>
    /// <remarks>
    /// Extracted because it had exactly one caller and that caller was reached only from `R`
    /// and `C`. Every other closed shape -- a filled `Y`, a closed `P`, a `SP` -- parsed `sh`
    /// and silently drew nothing, which is the quiet-ignore failure the attribute whitelist
    /// exists to catch and cannot, because `sh` is a real key on a real op.
    /// </remarks>
    private static void EmitShadows(MeshBuilder vh, VecNode node, EvalContext context, List<Vector2> outline, Frame frame)
    {
        if (node.Shadows == null || outline.Count < 3)
            return;

        var opacity = ShadowOpacity(node, context, frame);

        // CSS paints the first shadow on top, so the list is drawn last to first. It was drawn in
        // list order, the reverse of CSS and of text shadows here.
        for (var k = node.Shadows.Length - 1; k >= 0; k--)
        {
            var shadow = node.Shadows[k];
            if (!shadow.Inset)
                Shadow.Emit(vh, outline, Faded(shadow, opacity), frame.Matrix, frame.Scale * ScreenScale, frame.Clip);
        }
    }

    /// <summary>
    /// How far a shape's shadows fade: the group's `o` and the shape's own `fo`.
    /// </summary>
    /// <remarks>
    /// Shadows used to take neither, so `G o=0.2` faded a card to a ghost and left its halo at
    /// full strength -- the opposite of CSS, where `opacity` takes the box-shadow with the box.
    /// `fo` counts too: a shape faded to nothing already casts nothing (it is skipped whole), and
    /// a shape halfway there should not cast a full shadow. A shadow's own colour alpha is a
    /// separate thing and is untouched, which is what a nearly transparent carrier fill relies on.
    /// </remarks>
    private static float ShadowOpacity(VecNode node, EvalContext context, Frame frame)
    {
        return frame.Opacity * Mathf.Clamp01(node.FillOpacity.Evaluate(context));
    }

    private static VecShadow Faded(VecShadow shadow, float opacity)
    {
        if (opacity >= 0.9999f)
            return shadow;

        var colour = shadow.Colour;
        colour.a *= opacity;
        return new VecShadow(shadow.Dx, shadow.Dy, shadow.Blur, shadow.Spread, colour, shadow.Inset);
    }

    /// <summary>Inset shadows, inside the outline. Convex outlines only.</summary>
    private static void EmitInsetShadows(MeshBuilder vh, VecScene scene, VecNode node, EvalContext context, List<Vector2> outline, Frame frame)
    {
        if (node.Shadows == null || outline.Count < 3)
            return;

        var opacity = ShadowOpacity(node, context, frame);

        // First on top, as for outset shadows.
        for (var k = node.Shadows.Length - 1; k >= 0; k--)
        {
            var shadow = node.Shadows[k];
            if (!shadow.Inset)
                continue;

            // The rings are clipped to the shape by the convex clipper, so a concave outline
            // cannot be served. Refused out loud rather than drawn leaking past the shape.
            if (!IsConvex(outline))
            {
                scene.Problem($"{node.Op}{(string.IsNullOrEmpty(node.Id) ? "" : " \"" + node.Id + "\"")}: inset shadow needs a convex shape; not drawn");
                continue;
            }

            Shadow.EmitInset(vh, outline, Faded(shadow, opacity), frame.Matrix, frame.Scale * ScreenScale, frame.Clip);
        }
    }

    private static void EmitPath(MeshBuilder vh, VecScene scene, VecNode node, EvalContext context, Frame frame)
    {
        List<Vector2> points;

        switch (node.Op)
        {
            case VecOp.SampledLine:
            {
                var samples = node.RepeatCount;
                if (samples < 2)
                    return;

                // Same curve LOD as a band: fractional indices walk the author's own curve
                // at a step suited to how large it is actually drawn.
                context.PushRepeat(0, samples);
                var firstX = node.X.Evaluate(context);
                context.PopRepeat();
                context.PushRepeat(samples - 1, samples);
                var lastX = node.X.Evaluate(context);
                context.PopRepeat();

                var used = CurveSamples(samples, lastX - firstX, frame.Scale);
                var stride = (samples - 1) / (float)(used - 1);

                points = new List<Vector2>(used);
                for (var k = 0; k < used; k++)
                {
                    var i = k == used - 1 ? samples - 1 : k * stride;
                    context.PushRepeat(i, samples);
                    points.Add(new Vector2(node.X.Evaluate(context), node.Y.Evaluate(context)));
                    context.PopRepeat();
                }

                break;
            }

            case VecOp.Spline:
                points = Stroke.Spline(FromFlat(node.Points), node.RepeatCount);
                break;

            default:
                points = FromFlat(node.Points);
                break;
        }

        if (points.Count < 2)
            return;

        if (node.HasFill && node.Closed)
        {
            EmitShadows(vh, node, context, points, frame);
            FillContour(vh, node, context, frame, points, null, ResolvePaint(scene, node, context, frame, stroke: false));
            EmitInsetShadows(vh, scene, node, context, points, frame);
        }

        StrokeOutline(vh, scene, node, context, frame, points, node.Closed);
    }

    /// <summary>The largest closed subpath, which is the outer contour by the same rule the
    /// triangulator uses. Null when a path has no closed subpath to cast a shadow from.</summary>
    private static List<Vector2>? Outermost(List<SubPath> subpaths)
    {
        List<Vector2>? best = null;
        var bestArea = 0f;

        foreach (var sub in subpaths)
        {
            if (!sub.Closed || sub.Points.Count < 3)
                continue;

            var area = Mathf.Abs(Triangulator.SignedArea(sub.Points));
            if (area <= bestArea)
                continue;

            bestArea = area;
            best = sub.Points;
        }

        return best;
    }

    private static void EmitPathData(MeshBuilder vh, VecScene scene, VecNode node, EvalContext context, Frame frame)
    {
        if (node.Path == null || node.Path.IsEmpty)
            return;

        var subpaths = node.Path.Flatten(frame.Scale * ScreenScale);
        if (subpaths.Count == 0)
            return;

        if (node.HasFill)
        {
            // The OUTER contour only, which is the largest closed subpath -- the same rule
            // the triangulator uses to tell an outline from its holes. Shadowing every closed
            // subpath would draw a solid shadow behind each hole; punching one out needs the
            // polygon boolean this renderer does not have, and is the limit already recorded
            // for holes inside a clipped fill.
            var outer = Outermost(subpaths);
            if (outer != null)
                EmitShadows(vh, node, context, outer, frame);

            FillSubpaths(vh, scene, node, context, frame, subpaths);

            if (outer != null)
                EmitInsetShadows(vh, scene, node, context, outer, frame);
        }

        foreach (var sub in subpaths)
            StrokeOutline(vh, scene, node, context, frame, sub.Points, sub.Closed);
    }

    /// <summary>
    /// Decides which subpaths are holes and which are separate filled regions, then fills.
    /// </summary>
    /// <remarks>
    /// This is where <c>fr</c> takes effect. The largest closed subpath is the outer
    /// contour. Under <c>evenodd</c> every further contour is a hole; under <c>nonzero</c>
    /// only those wound *against* the outer one are, since a contour wound the same way adds
    /// to the winding number instead of cancelling it.
    ///
    /// Winding comparison, not scanline evaluation: exact for nested non-overlapping
    /// contours, approximate where contours partially overlap.
    /// </remarks>
    private static void FillSubpaths(MeshBuilder vh, VecScene scene, VecNode node, EvalContext context, Frame frame, List<SubPath> subpaths)
    {
        List<Vector2>? outer = null;
        var outerArea = 0f;

        foreach (var sub in subpaths)
        {
            if (!sub.Closed && subpaths.Count > 1)
                continue;

            var area = Triangulator.SignedArea(sub.Points);
            if (Mathf.Abs(area) <= Mathf.Abs(outerArea))
                continue;

            outerArea = area;
            outer = sub.Points;
        }

        if (outer == null)
            return;

        var holes = new List<List<Vector2>>();
        var regions = new List<List<Vector2>>();

        foreach (var sub in subpaths)
        {
            if (ReferenceEquals(sub.Points, outer) || (!sub.Closed && subpaths.Count > 1))
                continue;

            var area = Triangulator.SignedArea(sub.Points);
            if (node.EvenOdd || area * outerArea < 0f)
                holes.Add(sub.Points);
            else
                regions.Add(sub.Points);
        }

        var paint = ResolvePaint(scene, node, context, frame, stroke: false);

        FillContour(vh, node, context, frame, outer, holes, paint);

        foreach (var region in regions)
            FillContour(vh, node, context, frame, region, null, paint);
    }

    /// <summary>Fills an arbitrary contour: clip, triangulate, refine for gradients, feather.</summary>
    private static void FillContour(MeshBuilder vh, VecNode node, EvalContext context, Frame frame, List<Vector2> outer, List<List<Vector2>>? holes, Paint paint, bool ringFeather = true)
    {
        // Nothing this paint can draw will be seen. Skipping here rather than emitting
        // transparent geometry is what makes opacity usable as a visibility switch.
        if (IsInvisible(paint))
            return;

        var contour = outer;

        // Where the fill put its outline vertices, when it took the shared-fan path. -1 when
        // it did not, because only that path lays down one vertex per contour point in order.
        var shared = -1;

        // Set when a hole crosses the clip: the fill, already clipped triangle by triangle.
        List<Vector2>? soupVertices = null;
        List<int>? soupIndices = null;

        if (frame.Clip != null)
        {
            // Clipping a filled shape reshapes its boundary, so it happens before
            // triangulation rather than by discarding triangles afterwards.
            contour = frame.Clip.ClipPolygon(outer, ClippedScratch);
            if (contour.Count < 3)
                return;

            // A hole wholly inside the clip is untouched by it, so it is kept and bridged into
            // the clipped outline as usual. One wholly outside went with the part clipped away.
            // A hole crossing the clip boundary -- or the cut between two pieces of a concave
            // clip -- is handled by triangulating the unclipped shape and clipping each triangle,
            // which is exact since a triangle clipped by a convex region stays convex. Every hole
            // used to be dropped, so a path with a hole inside a clipped group drew solid -- which
            // is every evenodd path in an inline svg on an HTML page.
            if (holes is { Count: > 0 })
            {
                List<List<Vector2>>? kept = null;
                var straddles = false;

                foreach (var hole in holes)
                {
                    var inside = 0;
                    foreach (var point in hole)
                    {
                        if (frame.Clip.Contains(point))
                            inside++;
                    }

                    if (inside == hole.Count)
                        (kept ??= new List<List<Vector2>>()).Add(hole);
                    else if (inside > 0 || frame.Clip.ClipPolygon(hole).Count >= 3)
                        straddles = true;
                }

                if (straddles)
                {
                    if (!Triangulator.Triangulate(outer, holes, out var whole, out var wholeIndices))
                        return;

                    // ponytail: unshared vertices per clipped triangle; fine for the rare crossing hole.
                    soupVertices = new List<Vector2>();
                    soupIndices = new List<int>();
                    var triangle = new List<Vector2>(3);
                    for (var i = 0; i + 2 < wholeIndices.Count; i += 3)
                    {
                        triangle.Clear();
                        triangle.Add(whole[wholeIndices[i]]);
                        triangle.Add(whole[wholeIndices[i + 1]]);
                        triangle.Add(whole[wholeIndices[i + 2]]);

                        var piece = frame.Clip.ClipPolygon(triangle);
                        var first = soupVertices.Count;
                        soupVertices.AddRange(piece);
                        for (var k = 1; k + 1 < piece.Count; k++)
                        {
                            soupIndices.Add(first);
                            soupIndices.Add(first + k);
                            soupIndices.Add(first + k + 1);
                        }
                    }
                }

                holes = straddles ? holes : kept;
            }
        }

        // Bind a bounding-box gradient to this shape before anything samples it.
        if (paint.Gradient is { BoundingBox: true })
        {
            var min = contour[0];
            var max = contour[0];
            foreach (var point in contour)
            {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            paint = paint.WithBounds(min, max - min);
        }

        // A radial gradient is a function of distance from its focus, so geometry that
        // radiates from the focus follows it exactly. Ear clipping instead produces slivers
        // reaching across the shape, and subdividing a sliver only yields smaller slivers --
        // hence the residual streaking that survived the refinement fix.
        if (holes == null && RadialBandsFit(contour, paint))
        {
            FillRadialBands(vh, contour, paint, frame.Matrix, frame.Scale);
        }
        else if (holes == null && !NoFill && paint.Gradient is { Conic: true } && IsConvex(contour))
        {
            FillConicWedges(vh, contour, paint, frame.Matrix, frame.Scale);
        }
        else
        {
                // A convex outline needs no triangulation at all: fan it with SHARED vertices.
            // Ear clipping a four-point rect allocates several lists and runs an O(n^2)
            // search to rediscover that a quad is two triangles.
            if (NoFill)
            {
                // Everything up to and including the outline still runs; only the vertex
                // emission is skipped.
            }
            else if (soupVertices != null)
            {
                EmitTriangles(vh, soupVertices, soupIndices!, paint, frame.Matrix);
            }
            else if (holes == null && !NeedsRefinement(paint) && !LeavesRamp(contour, paint) && IsConvex(contour))
            {
                shared = FanShared(vh, contour, paint, frame.Matrix);
            }
            else if (Triangulator.Triangulate(contour, holes, out var vertices, out var indices))
            {
                EmitTriangles(vh, vertices, indices, paint, frame.Matrix);
            }
            else
            {
                return;
            }
        }

        if (!ringFeather || NoFeather)
            return;

        // Ramp the width down toward the threshold rather than switching off at it. A stack
        // of thin bands crosses a hard cutoff as the camera moves, so antialiasing appeared
        // and disappeared and the whole thing shimmered.
        var feather = FeatherWidth(node, context, frame.Scale) * FeatherFalloff(contour, FeatherWidth(node, context, frame.Scale));
        if (feather > 0.0001f)
            FeatherRing(vh, contour, paint, feather, frame.Matrix, frame.Clip, shared);
    }

    /// <summary>
    /// How much feather a shape of this size should get, 0..1 of the requested width.
    /// </summary>
    /// <remarks>
    /// Feathering roughly triples a small shape's geometry — on a 1x1 mote the ring is most
    /// of it — and below about twice the feather width the ramp is wider than the shape, so
    /// it reads as blur rather than as a soft edge.
    ///
    /// This was a hard cutoff and it **popped**: a stack of thin bands crosses the threshold
    /// as the camera moves, so antialiasing switched on and off and the whole thing
    /// shimmered. Ramping toward the threshold instead trades a cliff for a gradient.
    /// </remarks>
    private static float FeatherFalloff(List<Vector2> outline, float feather)
    {
        if (feather <= 0.0001f || outline.Count == 0)
            return 0f;

        var min = outline[0];
        var max = outline[0];

        foreach (var point in outline)
        {
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        var smallest = Mathf.Min(max.x - min.x, max.y - min.y);

        // Full feather at 3x the width, none at 1x, smooth between.
        var t = Mathf.Clamp01((smallest / feather - 1f) * 0.5f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// How many instances of a repeat to actually draw at the current on-screen size.
    /// </summary>
    /// <remarks>
    /// Density per screen pixel is what should stay constant, so the count scales with
    /// **area** — a console at half the linear size gets a quarter of the instances and
    /// looks identical, because the instances it lost were sub-pixel anyway.
    ///
    /// This targets the multiplication rather than the per-shape constant. Sixteen tanks of
    /// motes across eight visible consoles is ~12,000 animated quads, but only the console
    /// being looked at is anywhere near full size; the rest are small and distant. Per-quad
    /// micro-optimisation cannot fix that arithmetic and LOD can.
    ///
    /// Bucketed so ordinary camera drift does not change the count every frame, for the same
    /// reason <see cref="PathData"/> quantises its flattening tolerance: a count change means
    /// a topology rebuild, and popping every frame would cost more than it saves.
    /// </remarks>
    private static int LodCount(VecNode node, VecScene scene, float frameScale)
    {
        // Unknown on-screen size must never shed instances: failing toward full detail
        // costs frames, failing toward minimum makes the scene look broken.
        if (!node.AllowLod || node.RepeatCount <= 1 || !ScreenSizeKnown)
            return node.RepeatCount;

        if (!VectorConfig.CountLodEnabled)
            return node.RepeatCount;

        var full = VectorConfig.CountFullDetailPixels;
        var screenWidth = scene.ViewWidth * frameScale * ScreenScale;
        if (screenWidth >= full)
            return node.RepeatCount;

        // Linear, not area. Area scaling ("constant density per screen pixel") is the
        // theoretically tidy answer and the wrong perceptual one: a console is read as an
        // object, so what registers is its internal density. Quartering the instances at
        // half size empties it out visibly. Linear halves them instead, and the floor keeps
        // a distant field looking populated rather than abandoned.
        var linear = Mathf.Clamp01(screenWidth / full);

        // Sixteenths rather than eighths: a bucket change is a rebuild, but coarse steps
        // read as instances popping, and that is the artefact worth avoiding.
        var bucket = Mathf.Ceil(linear * 16f) / 16f;
        var fraction = Mathf.Max(bucket, VectorConfig.CountFloor);

        return Mathf.Max(1, Mathf.RoundToInt(node.RepeatCount * fraction));
    }

    /// <summary>
    /// True when a paint cannot produce a visible pixel, so its shape can be skipped.
    /// </summary>
    /// <remarks>
    /// There is no conditional node in the format — the way to show one of two things is to
    /// give each an opacity expression and let one evaluate to zero. That idiom is worth
    /// supporting properly: without this check both alternatives are fully tessellated and
    /// emitted every rebuild, and a console with a dozen such toggles pays for all of them
    /// forever.
    ///
    /// Only a flat colour can be judged from its own alpha. A gradient's stops carry their
    /// own, so the test there is the multiplier alone — which is exactly what an `fo`
    /// expression drives, and it stays correct for a band whose paint is deliberately built
    /// at full opacity for a per-column ramp.
    /// </remarks>
    /// <summary>
    /// How many points to actually evaluate along a sampled curve, given the authored count.
    /// </summary>
    /// <remarks>
    /// `YS` and `LS` join their samples with STRAIGHT LINES, so the sample count is what
    /// decides whether a wave reads as a curve or as a polygon. The count an author needs
    /// depends on how large the thing is drawn, which they cannot know.
    ///
    /// This is safe to do automatically in a way that shedding repeat instances is not.
    /// `i` is a float, and x/y are expressions over it, so evaluating at 0, 1.7, 3.4 … traces
    /// the *same curve* at a coarser step. Nothing is dropped and nothing changes meaning —
    /// unlike a repeat, where losing instances loses content. It is the same idea as the
    /// tolerance already used when flattening a path's béziers.
    ///
    /// Quantised so ordinary camera drift does not change the count every frame, and never
    /// above the authored figure: that is the author's statement of how much detail the
    /// curve actually has.
    /// </remarks>
    private static int CurveSamples(int authored, float widthScene, float frameScale)
    {
        if (authored < 3 || !VectorConfig.CurveLodEnabled || !ScreenSizeKnown)
            return authored;

        var screenWidth = Mathf.Abs(widthScene) * frameScale * ScreenScale;
        if (screenWidth <= 0f)
            return authored;

        var wanted = Mathf.CeilToInt(screenWidth / Mathf.Max(0.5f, VectorConfig.PixelsPerSegment));

        // Bucket to quarters of the authored count so the topology is stable across small
        // camera movements.
        var step = Mathf.Max(1, authored / 4);
        wanted = Mathf.CeilToInt(wanted / (float)step) * step;

        return Mathf.Clamp(wanted, Mathf.Min(authored, VectorConfig.MinimumSegments), authored);
    }

    private static bool IsInvisible(Paint paint)
    {
        if (paint.Alpha <= 0.002f)
            return true;

        return !paint.IsGradient && paint.Colour.a * paint.Alpha <= 0.002f;
    }

    /// <summary>Scales a colour's alpha, for the per-column band opacity ramp.</summary>
    private static Color32 Fade(Color32 colour, float scale, bool active)
    {
        if (!active)
            return colour;

        colour.a = (byte)Mathf.Clamp(Mathf.RoundToInt(colour.a * scale), 0, 255);
        return colour;
    }

    /// <summary>Fans a clipped piece, ramping alpha by each vertex's depth into the band.</summary>
    private static void FanRamped(MeshBuilder vh, List<Vector2> piece, Paint paint, Matrix4x4 matrix, float edgeY, float farY, float opacityY, float opacityY2)
    {
        if (Starved(vh, piece.Count, "a ramped fill"))
            return;

        var span = farY - edgeY;
        var origin = vh.currentVertCount;

        for (var i = 0; i < piece.Count; i++)
        {
            var depth = Mathf.Abs(span) < 0.00001f
                ? 0f
                : Mathf.Clamp01((piece[i].y - edgeY) / span);

            vh.AddVert(matrix.MultiplyPoint3x4(piece[i]),
                Fade(paint.At(piece[i]), Mathf.Lerp(opacityY, opacityY2, depth), active: true),
                Vector2.zero);
        }

        for (var i = 1; i < piece.Count - 1; i++)
            vh.AddTriangle(origin, origin + i, origin + i + 1);
    }

    /// <summary>Records a clickable node's canvas-space bounds.</summary>
    /// <summary>Records a clickable node's bounds, tagging it with its repeat index.</summary>
    /// <remarks>
    /// A clickable node inside a repeat is ONE node standing for n rows, so the id alone
    /// cannot say which was hit. Inside a repeat the value becomes `id:i`, which the handler
    /// splits. Outside one it is the bare id, so nothing that already works changes.
    /// </remarks>
    /// <summary>Resolves a `T` node's bound text: a string, or a formatted number.</summary>
    /// <remarks>
    /// **`fmt` exists because most console text is a number with a unit.** Without it the chip
    /// formats a string per label per tick and ships it; with it the chip sends the number it
    /// already had in the payload, and the formatting happens here -- off the main thread and
    /// off the instruction budget entirely.
    ///
    /// A name may hold a string or a number, so both are tried, string first: a payload that
    /// deliberately sends "OFFLINE" for a numeric readout should show that word rather than a
    /// formatted zero.
    /// </remarks>
    private static string? BindText(VecNode node, EvalContext context, VecScene scene, int ordinal)
    {
        var name = node.TextData!;

        if (node.TextIndex != null)
        {
            var at = node.TextIndex.Evaluate(context);
            var element = StringSlot(context, name, at);
            if (element != null)
                return WithUnit(element, node, scene, ordinal);

            // A numeric array formats exactly as a scalar does.
            if (context.Arrays.ContainsKey(name))
                return Format(node, context.Element(name, at), scene, ordinal);

            context.Missing.Add(name);
            return node.TextMissing;
        }

        if (context.Strings.TryGetValue(name, out var text))
            return WithUnit(text, node, scene, ordinal);

        if (context.Scalars.ContainsKey(name))
            return Format(node, context.Scalar(name), scene, ordinal);

        // Neither a string nor a number under that name. Reported once per rebuild, and the
        // node renders its placeholder rather than vanishing: an empty box on a console is
        // indistinguishable from a layout mistake.
        context.Missing.Add(name);
        return node.TextMissing;
    }

    [ThreadStatic] private static char[]? _textBuffer;

    /// <summary>
    /// A text with several bound values, `"set {$press:%.1f} kPa, trip {$trip:%.0f}"`, written
    /// into the reused buffer so a label whose characters did not change makes no string.
    /// </summary>
    /// <remarks>
    /// Each placeholder resolves exactly as a whole-text binding does -- a string first, then a
    /// number through its format -- and one with no value prints `missing` in its place while
    /// the rest of the text stands.
    /// </remarks>
    internal static string BindTemplate(VecNode node, EvalContext context, VecScene scene, int ordinal)
    {
        var at = 0;

        foreach (var part in node.TextParts!)
        {
            if (part.Literal != null)
            {
                Append(part.Literal, ref at);
                continue;
            }

            if (part.Value != null)
            {
                AppendNumber(part, part.Value.Evaluate(context), node, ref at);
                continue;
            }

            var name = part.Name!;
            if (part.Index != null)
            {
                var index = part.Index.Evaluate(context);
                var element = StringSlot(context, name, index);
                if (element != null)
                {
                    Append(element, ref at);
                }
                else if (context.Arrays.ContainsKey(name))
                {
                    AppendNumber(part, context.Element(name, index), node, ref at);
                }
                else
                {
                    context.Missing.Add(name);
                    Append(node.TextMissing, ref at);
                }
            }
            else if (context.Strings.TryGetValue(name, out var text))
            {
                Append(text, ref at);
            }
            else if (context.Scalars.ContainsKey(name))
            {
                AppendNumber(part, context.Scalar(name), node, ref at);
            }
            else
            {
                context.Missing.Add(name);
                Append(node.TextMissing, ref at);
            }
        }

        if (node.TextUnit != null)
            Append(node.TextUnit, ref at);

        return Remember(scene, ordinal, _textBuffer.AsSpan(0, at));
    }

    /// <summary>
    /// One slot of a string array, or null -- without reporting the name missing, which
    /// `StringElement` does and which was wrong for every label bound to a NUMERIC array.
    /// </summary>
    private static string? StringSlot(EvalContext context, string name, float index)
    {
        return context.StringArrays.ContainsKey(name) ? context.StringElement(name, index) : null;
    }

    private static void Append(string text, ref int at)
    {
        Reserve(at + text.Length);
        text.AsSpan().CopyTo(_textBuffer.AsSpan(at));
        at += text.Length;
    }

    private static void AppendNumber(in TextPart part, float value, VecNode node, ref int at)
    {
        if (part.Split)
        {
            Reserve(at + part.Prefix.Length + 64 + part.Suffix.Length);
            var start = at;
            part.Prefix.AsSpan().CopyTo(_textBuffer.AsSpan(at));
            at += part.Prefix.Length;

            if (value.TryFormat(_textBuffer.AsSpan(at, 64), out var written, part.Spec, CultureInfo.InvariantCulture))
            {
                at += written;
                part.Suffix.AsSpan().CopyTo(_textBuffer.AsSpan(at));
                at += part.Suffix.Length;
                return;
            }

            at = start;
        }

        // The formats the buffer cannot take: a literal brace, or no conversion at all.
        string printed;
        try
        {
            printed = part.Net == null
                ? value.ToString("0.##", CultureInfo.InvariantCulture)
                : string.Format(CultureInfo.InvariantCulture, part.Net, value);
        }
        catch (FormatException)
        {
            printed = node.TextMissing;
        }

        Append(printed, ref at);
    }

    /// <summary>Grows the text buffer keeping what is already written.</summary>
    private static void Reserve(int length)
    {
        if (_textBuffer == null)
            _textBuffer = new char[Math.Max(128, length * 2)];
        else if (_textBuffer.Length < length)
            Array.Resize(ref _textBuffer, Math.Max(_textBuffer.Length * 2, length));
    }

    /// <summary>A bound string with its unit, without concatenating when nothing changed.</summary>
    private static string WithUnit(string text, VecNode node, VecScene scene, int ordinal)
    {
        if (string.IsNullOrEmpty(node.TextUnit))
            return text;

        var unit = node.TextUnit!;
        var buffer = TextBuffer(text.Length + unit.Length);
        text.AsSpan().CopyTo(buffer);
        unit.AsSpan().CopyTo(buffer.AsSpan(text.Length));
        return Remember(scene, ordinal, buffer.AsSpan(0, text.Length + unit.Length));
    }

    /// <summary>
    /// A number printed through the node's format, written into a reused buffer and turned into
    /// a string only when the characters differ from what this label showed last rebuild.
    /// </summary>
    /// <remarks>
    /// `string.Format` boxed the number and made two strings per label per rebuild (the text and
    /// the text with its unit) whether or not a single character had changed -- 88 bytes a label
    /// on every frame of an animating console. The printf translation is always one `{0:SPEC}`
    /// between a literal prefix and suffix, which `float.TryFormat` can fill directly; anything
    /// else (a literal brace, or no conversion at all) keeps the old path and its behaviour.
    /// </remarks>
    private static string Format(VecNode node, float value, VecScene scene, int ordinal)
    {
        string? spec = "0.##";
        string prefix = string.Empty, suffix = string.Empty;

        if (node.TextFormat != null && !Printf.TrySplit(Printf.ToNet(node.TextFormat), out prefix, out spec, out suffix))
            return FormatSlow(node, value);

        var unit = node.TextUnit ?? string.Empty;

        try
        {
            var buffer = TextBuffer(prefix.Length + 64 + suffix.Length + unit.Length);
            prefix.AsSpan().CopyTo(buffer);
            var at = prefix.Length;

            if (!value.TryFormat(buffer.AsSpan(at, 64), out var written, spec, CultureInfo.InvariantCulture))
                return FormatSlow(node, value);

            at += written;
            suffix.AsSpan().CopyTo(buffer.AsSpan(at));
            at += suffix.Length;
            unit.AsSpan().CopyTo(buffer.AsSpan(at));
            at += unit.Length;

            return Remember(scene, ordinal, buffer.AsSpan(0, at));
        }
        catch (FormatException)
        {
            return node.TextMissing;
        }
    }

    /// <summary>The original formatter, kept for the formats the fast path does not take.</summary>
    private static string FormatSlow(VecNode node, float value)
    {
        if (node.TextFormat == null)
            return value.ToString("0.##", CultureInfo.InvariantCulture) + node.TextUnit;

        try
        {
            return string.Format(CultureInfo.InvariantCulture, Printf.ToNet(node.TextFormat), value)
                   + node.TextUnit;
        }
        catch (FormatException)
        {
            return node.TextMissing;
        }
    }

    private static char[] TextBuffer(int length)
    {
        var buffer = _textBuffer;
        if (buffer == null || buffer.Length < length)
            _textBuffer = buffer = new char[Math.Max(128, length * 2)];

        return buffer;
    }

    /// <summary>Last rebuild's string for this label if it reads the same, else a new one.</summary>
    private static string Remember(VecScene scene, int ordinal, ReadOnlySpan<char> chars)
    {
        var cache = scene.TextCache;
        while (cache.Count <= ordinal)
            cache.Add(null);

        var previous = cache[ordinal];
        if (previous != null && chars.SequenceEqual(previous.AsSpan()))
            return previous;

        var made = new string(chars);
        cache[ordinal] = made;
        return made;
    }

    private static void RecordHit(string id, List<Vector2> outline, Matrix4x4 matrix, List<Vector2>? clip, EvalContext context, bool press = false)
    {
        if (HitsFound == null || outline.Count == 0)
            return;

        if (context.RepeatDepth > 0)
        {
            id = id + ":" + Mathf.RoundToInt(context.Index(0))
                     .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var canvas = new Vector2[outline.Count];
        var min = (Vector2)matrix.MultiplyPoint3x4(outline[0]);
        var max = min;

        for (var i = 0; i < outline.Count; i++)
        {
            var p = (Vector2)matrix.MultiplyPoint3x4(outline[i]);
            canvas[i] = p;
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }

        HitsFound.Add(new HitRegion
        {
            Id = id,
            Rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y),
            Outline = canvas,
            Clip = clip?.ToArray(),
            Press = press,
            Scope = _idPath?.ToArray(),
            Index = context.RepeatDepth > 0 ? Mathf.RoundToInt(context.Index(0)) : -1,
        });
    }

    private static double Elapsed(long mark)
    {
        return (Stopwatch.GetTimestamp() - mark) * 1000d / Stopwatch.Frequency;
    }

    /// <summary>Walks an <c>SC</c>: clip to its box, translate its children by the offset.</summary>
    /// <remarks>
    /// It is a group with two extras. The clip is the container's own rounded box intersected
    /// with whatever it inherited, which stays convex and so stays geometric -- no stencil,
    /// and text inside gets the same box as a RectMask2D.
    ///
    /// The second extra is that <c>sy</c> and <c>vh</c> are rebound to *this* container for
    /// the duration of the subtree, so the pattern that pins a header or a fade to a
    /// ScriptedScreens scrollview works identically inside one of these. They are saved and
    /// restored rather than pushed on a stack because containers do not nest usefully: the
    /// inner one would need its own wheel target inside the outer one's, and the pointer
    /// cannot be in both.
    /// </remarks>
    private static void EmitScroll(MeshBuilder vh, VecScene scene, VecNode node, EvalContext context, Stack<Frame> stack, ref int emitted)
    {
        var parent = stack.Peek();

        var height = node.H.Evaluate(context);
        var content = node.ContentH.Evaluate(context);
        var offset = 0f;

        if (string.IsNullOrEmpty(node.Id))
        {
            scene.Problem("SC has no id, so it cannot be scrolled");
        }
        else if (context.ScrollOffsets.TryGetValue(node.Id!, out var stored))
        {
            offset = Mathf.Clamp(stored, 0f, Mathf.Max(0f, content - height));
        }

        // The box is written in the parent's coordinates, like any other shape's rect.
        var box = RectOutline(node, context, parent.Scale * ScreenScale, new List<Vector2>(64));
        if (box.Count < 3)
            return;

        if (Mathf.Abs(Triangulator.SignedArea(box)) < 0.000001f)
        {
            scene.Problem($"SC \"{node.Id}\" is degenerate; its children are not drawn");
            return;
        }

        var inverse = Matrix4x4.Translate(new Vector3(0f, offset, 0f));

        // The viewport clips in the parent's space; its children are shifted by the offset,
        // so the combined clip is built there and then moved with them.
        CombineClip(parent, Matrix4x4.identity, box, out var viewport, out var viewportPieces);
        if (viewportPieces != null)
        {
            for (var k = 0; k < viewportPieces.Count; k++)
                viewportPieces[k] = viewportPieces[k].Transform(inverse);
        }

        if (ScrollsFound != null && !_repeatPiece && !string.IsNullOrEmpty(node.Id))
        {
            var min = (Vector2)parent.Matrix.MultiplyPoint3x4(box[0]);
            var max = min;
            foreach (var point in box)
            {
                var p = (Vector2)parent.Matrix.MultiplyPoint3x4(point);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }

            ScrollsFound.Add(new ScrollRegion
            {
                Id = node.Id!,
                Rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y),
                View = height,
                Content = content,
                ToScene = parent.Scale > 0.0001f ? 1f / parent.Scale : 1f,
            });
        }

        var local = Matrix4x4.Translate(new Vector3(0f, -offset, 0f));

        stack.Push(new Frame
        {
            Matrix = parent.Matrix * local,
            Opacity = parent.Opacity * Mathf.Clamp01(node.Opacity.Evaluate(context)),
            Clip = viewport?.Transform(inverse),
            Pieces = viewportPieces,
            CanvasClip = CombineCanvasClip(parent.CanvasClip, ToCanvas(parent.Matrix, box)),
            Scale = parent.Scale,
            SceneToLocal = inverse * parent.SceneToLocal,
        });

        var savedScroll = context.ScrollY;
        var savedViewport = context.ViewportH;
        context.ScrollY = offset;
        context.ViewportH = height;

        foreach (var child in node.Children)
            EmitNode(vh, scene, child, context, stack, ref emitted);

        context.ScrollY = savedScroll;
        context.ViewportH = savedViewport;
        stack.Pop();
    }

    /// <summary>
    /// Strokes, with the budget noticed from outside.
    /// </summary>
    /// <remarks>
    /// <c>Stroke.Emit</c> takes the ceiling as a number and stops when it reaches it, and it
    /// has no way to say so. Comparing the count before and after catches the one case the
    /// helper below cannot: a stroke that stopped halfway along its own path.
    /// </remarks>
    private static void StrokeWithBudget(MeshBuilder vh, List<Vector2> points, bool closed, float width, Paint paint, VecNode node, float feather, Matrix4x4 matrix)
    {
        var before = vh.currentVertCount;

        Stroke.Emit(vh, points, closed, width, paint, node.Cap, node.Join, node.MiterLimit, feather, matrix, MaxVertices);

        if (vh.currentVertCount >= MaxVertices && vh.currentVertCount > before)
            _starved ??= "a stroke";
    }

    /// <summary>Reads the starvation note without clearing it. A method, not a field read,
    /// because the analyser cannot see that <see cref="Starved"/> writes it.</summary>
    private static string? PeekStarved() => _starved;

    /// <summary>True when <paramref name="extra"/> more vertices will not fit, and says so.</summary>
    internal static bool Starved(MeshBuilder vh, int extra, string op)
    {
        // One shape has to fit one mesh whole: a cut can only fall between shapes, so nothing
        // downstream can rescue a single shape bigger than a mesh.
        if (extra > MaxPerShape)
        {
            _starved ??= op + " (too large for one mesh on its own)";
            return true;
        }

        if (vh.currentVertCount + extra <= MaxVertices)
            return false;

        _starved ??= op;
        return true;
    }

    private static void Charge(VecOp op, long mark)
    {
        OpMilliseconds[(int)op] += (Stopwatch.GetTimestamp() - mark) * 1000d / Stopwatch.Frequency;
        OpCounts[(int)op]++;
    }

    private static bool NeedsRefinement(Paint paint)
    {
        if (!paint.IsGradient)
            return false;

        var gradient = paint.Gradient!;
        if (gradient.Radial || gradient.Conic || gradient.StopCount > 2 || gradient.Spread != 0)
            return true;

        // A two-stop linear gradient is exact only while its stops span the whole parameter.
        // Stops at, say, 0 and 0.64 hold the last colour flat from there on, and that flat
        // part is NOT affine -- interpolating two corner colours across it ramps straight
        // past the stop instead of stopping at it. Cutting at the stop lines fixes it, and
        // that is what refinement does.
        return gradient.StopCount > 0
               && (gradient.Positions[0] > 0.0001f || gradient.Positions[gradient.StopCount - 1] < 0.9999f);
    }

    /// <summary>
    /// Whether a shape reaches past either end of its gradient, where the ramp holds its end
    /// colour flat and interpolating corners would run straight through that flat part.
    /// </summary>
    /// <remarks>
    /// Only asked of a two-stop linear gradient — anything else refines already — and only
    /// of a shape that has one, so the ordinary case never walks this.
    /// </remarks>
    private static bool LeavesRamp(List<Vector2> points, Paint paint)
    {
        if (!paint.IsGradient)
            return false;

        var gradient = paint.Gradient!;
        foreach (var point in points)
        {
            var t = gradient.Parameter(paint.ToGradientSpace(point));
            if (t < -0.0001f || t > 1.0001f)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Fans a convex outline, adding each vertex once and indexing it.
    /// </summary>
    /// <remarks>
    /// The per-triangle emitter duplicates every vertex three times, which for a quad means
    /// six vertices where four would do. Sharing matters here because a mote field emits
    /// thousands of these per frame.
    /// </remarks>
    /// <summary>Fans a convex outline, and reports where its vertices landed.</summary>
    /// <remarks>
    /// The index is returned so the feather ring can INDEX these rather than emit its own
    /// copies. It lays down exactly one vertex per outline point, in order, at
    /// <c>matrix * outline[i]</c> coloured <c>paint.At(outline[i])</c> -- which is, point for
    /// point and colour for colour, what the ring's inner edge would otherwise duplicate.
    /// </remarks>
    private static int FanShared(MeshBuilder vh, List<Vector2> outline, Paint paint, Matrix4x4 matrix)
    {
        var count = outline.Count;
        if (count < 3 || Starved(vh, count, "a fill"))
            return -1;

        var origin = vh.currentVertCount;

        for (var i = 0; i < count; i++)
            vh.AddVert(matrix.MultiplyPoint3x4(outline[i]), paint.At(outline[i]), Vector2.zero);

        for (var i = 1; i < count - 1; i++)
            vh.AddTriangle(origin, origin + i, origin + i + 1);

        return origin;
    }

    internal static bool IsConvex(List<Vector2> points)
    {
        if (points.Count < 4)
            return true;

        var sign = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            var c = points[(i + 2) % points.Count];

            var cross = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
            if (Mathf.Abs(cross) < 0.0001f)
                continue;

            var current = cross > 0f ? 1 : -1;
            if (sign == 0)
                sign = current;
            else if (sign != current)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Fills a shape as concentric bands scaled toward a radial gradient's focus.
    /// </summary>
    /// <remarks>
    /// Every ring is the outline scaled toward the focus, so ring vertices land at evenly
    /// spaced gradient parameters and the bands follow the colour ramp instead of cutting
    /// across it. No slivers, no T-junctions, and every vertex is shared with its
    /// neighbours — which is what removes the streaking that midpoint subdivision could not.
    ///
    /// Requires the focus to lie inside the outline: the rings are a homothety about the
    /// focus, and their union only reconstructs the shape when it is star-shaped about that
    /// point. Callers check with <see cref="Encloses"/> and fall back to ear clipping.
    /// </remarks>
    private static void FillRadialBands(MeshBuilder vh, List<Vector2> shape, Paint paint, Matrix4x4 matrix, float scale)
    {
        // Points along every long edge, so colour is sampled round each ring and not only at
        // the corners -- see RadialFill.Densify for what went wrong without it.
        var outline = RingOutline;
        RadialFill.Densify(shape, RadialFill.PixelsPerBand / Mathf.Max(0.0001f, scale * ScreenScale), outline);

        var count = outline.Count;

        // In bounding-box mode the focus is a 0..1 fraction, so it has to be mapped back
        // into the shape's own space before rings can be built around it.
        var focus = paint.FromGradientSpace(paint.Gradient!.Focus);

        var sceneRadius = 0f;
        foreach (var point in outline)
            sceneRadius = Mathf.Max(sceneRadius, (point - focus).magnitude);

        var screenRadius = sceneRadius * scale * ScreenScale;
        // A spread ramp repeats out to the shape's edge, and each period wants the rings a whole
        // ramp gets.
        var stops = paint.Gradient.StopCount;
        if (paint.Gradient.Spread != 0)
        {
            var reach = 0f;
            foreach (var point in outline)
                reach = Mathf.Max(reach, paint.Gradient.Parameter(paint.ToGradientSpace(point)));

            stops = Mathf.Max(2, stops) * Mathf.Clamp(Mathf.CeilToInt(reach), 1, MaxSpreadPeriods);
        }

        var rings = RadialFill.Rings(screenRadius, stops);

        if (Starved(vh, RadialFill.VertexCount(count, rings), "a radial gradient"))
            return;

        var indices = RingIndices;
        var origin = vh.currentVertCount;

        vh.AddVert(matrix.MultiplyPoint3x4(focus), paint.At(focus), Vector2.zero);

        // Rings carry fewer points the closer they sit to the focus, because a ring at a
        // tenth of the radius has a tenth of the circumference and the full outline would
        // oversample it tenfold. Bases are recorded as they are laid down, since the counts
        // differ and the stitch below needs both.
        var previousBase = origin;
        var previousCount = 0;

        for (var ring = 1; ring <= rings; ring++)
        {
            var ratio = ring / (float)rings;
            var points = RadialFill.Points(count, ring, rings);
            var thisBase = vh.currentVertCount;

            for (var k = 0; k < points; k++)
            {
                // Sampled along the ORIGINAL outline by parameter, not by index, so a ring
                // with fewer points still follows the same curve rather than a prefix of it.
                var at = outline[k * count / points];
                var point = focus + (at - focus) * ratio;
                vh.AddVert(matrix.MultiplyPoint3x4(point), paint.At(point), Vector2.zero);
            }

            if (ring == 1)
            {
                // Innermost ring fans out from the focus.
                for (var k = 0; k < points; k++)
                    vh.AddTriangle(origin, thisBase + k, thisBase + (k + 1) % points);
            }
            else
            {
                indices.Clear();
                RadialFill.Stitch(previousBase, previousCount, thisBase, points, indices);

                for (var k = 0; k + 2 < indices.Count; k += 3)
                    vh.AddTriangle(indices[k], indices[k + 1], indices[k + 2]);
            }

            previousBase = thisBase;
            previousCount = points;
        }
    }

    /// <summary>
    /// Whether a fill can be built as concentric bands around its gradient's focus.
    /// </summary>
    /// <remarks>
    /// The focus has to be tested in the SHAPE's space. In `units = "bbox"` the gradient's own
    /// focus is a 0..1 fraction, and testing that raw against an outline at, say, x 10..100,
    /// y 243..293 never finds it inside -- so every bounding-box radial fell through to ear
    /// clipping plus subdivision. Measured: one 90x50 rounded rect cost ~49,000 vertices that
    /// way against a page total of 4,786 without it. FillRadialBands already mapped the focus
    /// back; only this gate had forgotten.
    /// </remarks>
    internal static bool RadialBandsFit(List<Vector2> contour, Paint paint)
    {
        return paint.IsGradient
               && paint.Gradient!.Radial
               && Encloses(contour, paint.FromGradientSpace(paint.Gradient.Focus));
    }

    /// <summary>Point-in-polygon by crossing count, used to validate the banding fallback.</summary>
    private static bool Encloses(List<Vector2> polygon, Vector2 point)
    {
        var inside = false;

        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];

            if (a.y > point.y == b.y > point.y)
                continue;

            var crossing = (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x;
            if (point.x < crossing)
                inside = !inside;
        }

        return inside;
    }

    /// <summary>
    /// Emits triangles, subdividing where a gradient cannot be represented by interpolating
    /// its corners.
    /// </summary>
    /// <remarks>
    /// Vertex colours interpolate linearly across a triangle. A two-stop linear gradient is
    /// therefore already exact and is emitted untouched. Multi-stop linear gradients are
    /// piecewise affine and radial gradients are not affine at all, so those get midpoint
    /// subdivision until each triangle spans a small enough slice of the gradient. Without
    /// it, a radial gradient across a large shape shows as flat facets.
    /// </remarks>
    private static void EmitTriangles(MeshBuilder vh, List<Vector2> vertices, List<int> indices, Paint paint, Matrix4x4 matrix)
    {
        var refine = NeedsRefinement(paint) || LeavesRamp(vertices, paint);

        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var a = vertices[indices[i]];
            var b = vertices[indices[i + 1]];
            var c = vertices[indices[i + 2]];

            if (refine)
                Subdivide(vh, a, b, c, paint, matrix, 0);
            else
                Triangle(vh, a, b, c, paint, matrix);
        }
    }

    [ThreadStatic] private static List<Vector2>? _bandIn;
    [ThreadStatic] private static List<Vector2>? _bandOut;
    [ThreadStatic] private static List<float>? _bandInT;
    [ThreadStatic] private static List<float>? _bandOutT;
    [ThreadStatic] private static List<float>? _bandCuts;

    /// <summary>Periods of a repeating ramp one triangle is cut into; past it the ramp is left smeared.</summary>
    private const int MaxSpreadPeriods = 256;

    /// <summary>
    /// A triangle under a linear gradient, cut along the stop lines so each piece is exact.
    /// </summary>
    /// <remarks>
    /// A linear gradient is affine between two stops, so vertex colours reproduce it exactly
    /// once no piece spans a stop. Midpoint subdivision was used before and never met its
    /// tolerance on a gradient spanning the whole shape: every triangle went to full depth,
    /// 1,024 triangles each, and ten 171x52 rounded boxes with a three-stop fill came to about
    /// 231,000 vertices. Cut at the stops they are a few hundred.
    /// </remarks>
    private static void BandTriangle(MeshBuilder vh, Vector2 a, Vector2 b, Vector2 c, Paint paint, Matrix4x4 matrix)
    {
        var gradient = paint.Gradient!;
        var ta = gradient.Parameter(paint.ToGradientSpace(a));
        var tb = gradient.Parameter(paint.ToGradientSpace(b));
        var tc = gradient.Parameter(paint.ToGradientSpace(c));
        var low = Mathf.Min(ta, Mathf.Min(tb, tc));
        var high = Mathf.Max(ta, Mathf.Max(tb, tc));

        var input = _bandIn ??= new List<Vector2>(8);
        var output = _bandOut ??= new List<Vector2>(8);
        var inputT = _bandInT ??= new List<float>(8);
        var outputT = _bandOutT ??= new List<float>(8);

        // The parameters to cut at: the stops, or under a spread the stops of every period the
        // triangle covers plus each period's edge, where `repeat` jumps back to the first colour.
        var cuts = _bandCuts ??= new List<float>(16);
        cuts.Clear();
        var spread = gradient.Spread is 1 or 2 && Mathf.Floor(high) - Mathf.Floor(low) < MaxSpreadPeriods;
        var clear = gradient.Spread == 3;
        if (clear)
        {
            // `spread = none`: the ramp's ends are hard edges into transparency, so cut there too.
            cuts.Add(0f);
            cuts.AddRange(gradient.Positions);
            cuts.Add(1f);
        }
        else if (spread)
        {
            for (var period = Mathf.Floor(low); period <= high; period++)
            {
                cuts.Add(period);
                var mirrored = gradient.Spread == 2 && ((int)period & 1) != 0;
                for (var k = 0; k < gradient.Positions.Count; k++)
                {
                    var p = gradient.Positions[mirrored ? gradient.Positions.Count - 1 - k : k];
                    cuts.Add(period + (mirrored ? 1f - p : p));
                }
            }
        }
        else
        {
            cuts.AddRange(gradient.Positions);
        }

        var from = float.NegativeInfinity;
        var any = false;
        for (var k = 0; k <= cuts.Count; k++)
        {
            var to = k < cuts.Count ? cuts[k] : float.PositiveInfinity;
            if (to <= low || from >= high)
            {
                from = Mathf.Max(from, to);
                continue;
            }

            // Which period this band is in, so its vertices are coloured within it: the vertex
            // on a `repeat` seam belongs to the end of one period and the start of the next,
            // and sampling it by position alone would give both bands the first colour.
            var middle = (Mathf.Max(from, low) + Mathf.Min(to, high)) * 0.5f;
            var period = spread ? Mathf.Floor(middle) : 0f;
            var outside = clear && (middle < 0f || middle > 1f);

            input.Clear(); inputT.Clear();
            input.Add(a); input.Add(b); input.Add(c);
            inputT.Add(ta); inputT.Add(tb); inputT.Add(tc);

            ClipScalar(input, inputT, output, outputT, from, keepAbove: true);
            ClipScalar(output, outputT, input, inputT, to, keepAbove: false);
            from = to;

            if (input.Count < 3 || Starved(vh, input.Count, "a gradient band"))
                continue;

            any = true;
            var origin = vh.currentVertCount;
            for (var j = 0; j < input.Count; j++)
            {
                var colour = spread ? paint.AtParameter(Local(inputT[j] - period, period))
                    : outside ? paint.AtParameter(middle, beyond: true)
                    : clear ? paint.AtParameter(inputT[j])
                    : paint.At(input[j]);
                vh.AddVert(matrix.MultiplyPoint3x4(input[j]), colour, Vector2.zero);
            }
            for (var j = 1; j + 1 < input.Count; j++)
                vh.AddTriangle(origin, origin + j, origin + j + 1);
        }

        if (!any)
            Triangle(vh, a, b, c, paint, matrix);

        float Local(float within, float period) =>
            gradient.Spread == 2 && ((int)period & 1) != 0 ? 1f - within : within;
    }

    /// <summary>Clips a convex polygon to one side of <c>t = edge</c>, t being affine in the point.</summary>
    private static void ClipScalar(List<Vector2> points, List<float> values, List<Vector2> into, List<float> intoValues, float edge, bool keepAbove)
    {
        into.Clear();
        intoValues.Clear();
        if (float.IsInfinity(edge))
        {
            into.AddRange(points);
            intoValues.AddRange(values);
            return;
        }

        for (var i = 0; i < points.Count; i++)
        {
            var j = (i + 1) % points.Count;
            var vi = values[i];
            var vj = values[j];
            var inI = keepAbove ? vi >= edge : vi <= edge;
            var inJ = keepAbove ? vj >= edge : vj <= edge;

            if (inI)
            {
                into.Add(points[i]);
                intoValues.Add(vi);
            }

            if (inI != inJ)
            {
                var s = (edge - vi) / (vj - vi);
                into.Add(Vector2.Lerp(points[i], points[j], s));
                intoValues.Add(edge);
            }
        }
    }

    private static void Subdivide(MeshBuilder vh, Vector2 a, Vector2 b, Vector2 c, Paint paint, Matrix4x4 matrix, int depth)
    {
        if (depth == 0 && paint.Gradient is { Radial: false, Conic: false })
        {
            BandTriangle(vh, a, b, c, paint, matrix);
            return;
        }

        if (depth < MaxGradientDepth)
        {
            if (paint.Gradient!.SpreadOver(paint.ToGradientSpace(a), paint.ToGradientSpace(b), paint.ToGradientSpace(c)) > GradientTolerance)
            {
                var ab = (a + b) * 0.5f;
                var bc = (b + c) * 0.5f;
                var ca = (c + a) * 0.5f;

                Subdivide(vh, a, ab, ca, paint, matrix, depth + 1);
                Subdivide(vh, ab, b, bc, paint, matrix, depth + 1);
                Subdivide(vh, ca, bc, c, paint, matrix, depth + 1);
                Subdivide(vh, ab, bc, ca, paint, matrix, depth + 1);
                return;
            }
        }

        Triangle(vh, a, b, c, paint, matrix);
    }

    private static void Triangle(MeshBuilder vh, Vector2 a, Vector2 b, Vector2 c, Paint paint, Matrix4x4 matrix)
    {
        if (Starved(vh, 3, "a triangle"))
            return;

        var origin = vh.currentVertCount;
        vh.AddVert(matrix.MultiplyPoint3x4(a), paint.At(a), Vector2.zero);
        vh.AddVert(matrix.MultiplyPoint3x4(b), paint.At(b), Vector2.zero);
        vh.AddVert(matrix.MultiplyPoint3x4(c), paint.At(c), Vector2.zero);
        vh.AddTriangle(origin, origin + 1, origin + 2);
    }

    private static void StrokeOutline(MeshBuilder vh, VecScene scene, VecNode node, EvalContext context, Frame frame, List<Vector2> points, bool closed)
    {
        if (!node.HasStroke)
            return;

        var width = node.StrokeWidth.Evaluate(context);
        if (width <= 0f)
            return;

        var paint = ResolvePaint(scene, node, context, frame, stroke: true);
        var feather = FeatherWidth(node, context, frame.Scale);

        // An undashed stroke is one run -- the outline itself -- so it is drawn straight
        // rather than through a list built to hold it. The list was the last per-shape
        // allocation on this path, and a page of bordered boxes redraws every one of them
        // on every rebuild.
        if (node.DashPattern.Length == 0)
        {
            StrokeRun(vh, points, closed, width, paint, node, feather, frame);
            return;
        }

        foreach (var run in Stroke.Dash(Loop(points, closed), node.DashPattern, node.DashOffset.Evaluate(context)))
            StrokeRun(vh, run, false, width, paint, node, feather, frame);
    }

    /// <summary>
    /// Draws one run of a stroke, clipped if the frame clips.
    /// </summary>
    /// <remarks>
    /// A method rather than a local function on purpose: a local function capturing the
    /// frame and the paint allocates a display class per call, which is what replaced the
    /// list this was written to remove. Measured, both times.
    /// </remarks>
    private static void StrokeRun(MeshBuilder vh, List<Vector2> run, bool runClosed, float width, Paint paint, VecNode node, float feather, Frame frame)
    {
        // A clipped stroke is cut into the pieces that fall inside, rather than being
        // redirected along the boundary the way a filled contour is.
        if (frame.Clip == null)
        {
            StrokeWithBudget(vh, run, runClosed, width, paint, node, feather, frame.Matrix);
            return;
        }

        foreach (var piece in frame.Clip.ClipPolyline(Loop(run, runClosed)))
            StrokeWithBudget(vh, piece, closed: false, width, paint, node, feather, frame.Matrix);
    }

    private static Paint ResolvePaint(VecScene scene, VecNode node, EvalContext context, Frame frame, bool stroke, float opacityOverride = -1f)
    {
        var name = stroke ? node.StrokeGradient : node.FillGradient;

        // A band with `fo2` scales alpha per vertex instead, so its paint is built at full
        // opacity and the ramp applied during emission. A ratio against `fo` would not do:
        // the common case starts at fo = 0.
        var opacity = opacityOverride >= 0f
            ? opacityOverride
            : Mathf.Clamp01((stroke ? node.StrokeOpacity : node.FillOpacity).Evaluate(context)) * frame.Opacity;

        if (!string.IsNullOrEmpty(name))
        {
            if (scene.Gradients.TryGetValue(name!, out var found))
            {
                var gradient = found.AtUse(context);
                var at = stroke ? node.StrokeGradientAt : node.FillGradientAt;
                if (at == null)
                    return new Paint(Color.white, gradient, opacity);

                // Sampled at an expression rather than by position: one flat colour for the
                // whole shape, so it needs no per-vertex lookup and no subdivision.
                return new Paint(gradient.Sample(at.Evaluate(context)), null, opacity);
            }

            scene.Problems.Add($"unknown gradient \"{name}\"");
            ScriptedScreensVectorPlugin.Log?.LogWarning($"unknown gradient \"{name}\"");
        }

        // A data-bound colour is re-read every tick, so an alarm can go red without the
        // structure being resent.
        var bound = stroke ? node.StrokeData : node.FillData;
        if (!string.IsNullOrEmpty(bound))
        {
            // `f = "$cols[i]"`: one node inside a repeat, one array in the payload.
            var index = stroke ? node.StrokeIndex : node.FillIndex;
            if (index != null)
            {
                if (context.ColourElement(bound!, index.Evaluate(context), out var element))
                    return new Paint(element, null, opacity);

                return new Paint(Magenta, null, opacity);
            }

            if (context.Colour(bound!, out var supplied))
                return new Paint(supplied, null, opacity);

            // A colour the payload never supplied. Magenta rather than the default white,
            // because white is a colour somebody meant to use and magenta is not -- an
            // unresolved binding should look like a fault, not like a design decision.
            context.Missing.Add(bound!);
            return new Paint(Magenta, null, opacity);
        }

        return new Paint(stroke ? node.StrokeColour : node.Fill, null, opacity);
    }

    private static void FeatherEdge(MeshBuilder vh, List<Vector2> edge, List<Vector2> away, Paint paint, float width, Matrix4x4 matrix, ClipRegion? clip, float opacityScale = 1f)
    {
        // A ramp that starts transparent has nothing to feather: the edge is already
        // invisible, and a halo there would be brighter than the shape it borders.
        if (opacityScale <= 0.002f)
            return;

        if (Starved(vh, edge.Count * 2, "an edge feather"))
            return;

        var origin = vh.currentVertCount;

        for (var i = 0; i < edge.Count; i++)
        {
            var outward = (edge[i] - away[i]).normalized;
            if (outward.sqrMagnitude < 0.0001f)
                outward = Vector2.up;

            var solid = Fade(paint.At(edge[i]), opacityScale, opacityScale < 1f);
            var faded = solid;
            faded.a = 0;

            var outer = edge[i] + outward * width;

            // Clipped rather than skipped. This whole strip used to be dropped whenever a
            // clip was present, which is why a gas fade inside a tank had no ramp at all.
            if (clip != null)
                outer = clip.ClampInside(edge[i], outer);

            vh.AddVert(matrix.MultiplyPoint3x4(edge[i]), solid, Vector2.zero);
            vh.AddVert(matrix.MultiplyPoint3x4(outer), faded, Vector2.zero);
        }

        for (var i = 0; i < edge.Count - 1; i++)
        {
            var a = origin + i * 2;
            vh.AddTriangle(a, a + 1, a + 3);
            vh.AddTriangle(a, a + 3, a + 2);
        }
    }

    /// <summary>
    /// Wraps a closed contour in a ring of quads fading to zero alpha.
    /// </summary>
    /// <remarks>
    /// Normals come from the adjacent edges rather than from the centre. A centre-radial
    /// normal is wrong for anything long and thin — on a 1x1 mote or a 2-unit column it
    /// would feather the short sides far more than the long ones.
    /// </remarks>
    /// <summary>
    /// A soft ramp from the shape's edge out to nothing.
    /// </summary>
    /// <remarks>
    /// **The inner edge is the fill's own outline, and is shared with it when possible.** The
    /// ring used to emit two vertices per point: one at the outline, solid, and one offset
    /// outward at zero alpha. The first was an exact duplicate -- same position through the
    /// same matrix, same colour from the same paint -- of a vertex the fill had just written.
    ///
    /// Measured on 400 circles at 1591 px: 60 vertices each with the ring, 20 without. Sharing
    /// takes a feathered shape from three rings' worth to two, a third off everything that
    /// feathers, and the picture does not change by one pixel.
    ///
    /// Only the shared-fan fill qualifies. Ear clipping builds its own vertex list, and a
    /// radial fill emits concentric rings, so neither leaves the outline sitting in order at a
    /// known index. <paramref name="sharedBase"/> is -1 for those and the ring emits both edges
    /// as before.
    /// </remarks>
    private static void FeatherRing(MeshBuilder vh, List<Vector2> outline, Paint paint, float width, Matrix4x4 matrix, ClipRegion? clip, int sharedBase = -1)
    {
        var count = outline.Count;
        var share = sharedBase >= 0;

        if (count < 3 || Starved(vh, share ? count : count * 2, "a feather ring"))
            return;

        var winding = Mathf.Sign(Triangulator.SignedArea(outline));
        var origin = vh.currentVertCount;

        for (var i = 0; i < count; i++)
        {
            var previous = outline[(i - 1 + count) % count];
            var next = outline[(i + 1) % count];

            var incoming = (outline[i] - previous).normalized;
            var outgoing = (next - outline[i]).normalized;

            var normal = new Vector2(incoming.y + outgoing.y, -(incoming.x + outgoing.x)).normalized * winding;
            if (normal.sqrMagnitude < 0.0001f)
                normal = Vector2.up;

            var solid = paint.At(outline[i]);
            var faded = solid;
            faded.a = 0;

            var outer = outline[i] + normal * width;

            // Pull the outer edge back inside the clip. Where the contour lies on the clip
            // boundary this collapses the ring to zero width, so a cut edge gets no halo
            // while an untouched edge keeps its full feather. Building the ring from the
            // already-clipped contour and not doing this is what let haloes escape.
            if (clip != null)
                outer = clip.ClampInside(outline[i], outer);

            if (!share)
                vh.AddVert(matrix.MultiplyPoint3x4(outline[i]), solid, Vector2.zero);

            vh.AddVert(matrix.MultiplyPoint3x4(outer), faded, Vector2.zero);
        }

        for (var i = 0; i < count; i++)
        {
            var next = (i + 1) % count;

            // Inner comes from the fill when shared, from this ring when not; outer always
            // from this ring. Same two triangles either way, different index bases.
            var innerA = share ? sharedBase + i : origin + i * 2;
            var innerB = share ? sharedBase + next : origin + next * 2;
            var outerA = share ? origin + i : origin + i * 2 + 1;
            var outerB = share ? origin + next : origin + next * 2 + 1;

            vh.AddTriangle(innerA, outerA, outerB);
            vh.AddTriangle(innerA, outerB, innerB);
        }
    }

    /// <summary>
    /// Resolves the feather width in scene units.
    /// </summary>
    /// <remarks>
    /// A negative value means auto, which targets a little over one screen pixel. Feather
    /// has to be specified in screen terms: the same scene is drawn at wildly different
    /// sizes depending on console resolution and camera distance, and a fixed scene-unit
    /// feather would be invisible when small and a blurry halo when large.
    /// </remarks>
    /// <summary>
    /// Feather width for a band's sampled edge: <c>fea_edge</c> if given, else <c>fea</c>.
    /// </summary>
    /// <remarks>
    /// Separate because a soft liquid or gas surface wants a wide ramp on the surface itself
    /// while the tank walls stay crisp. Without it that shape needs a stack of faked strips.
    /// </remarks>
    private static float EdgeFeatherWidth(VecNode node, EvalContext context, float frameScale)
    {
        if (node.EdgeFeather == null)
            return FeatherWidth(node, context, frameScale);

        var requested = node.EdgeFeather.Evaluate(context);
        return requested >= 0f ? requested : FeatherWidth(node, context, frameScale);
    }

    private static float FeatherWidth(VecNode node, EvalContext context, float frameScale)
    {
        var requested = node.Feather.Evaluate(context);
        if (requested >= 0f)
            return requested;

        var scale = frameScale * ScreenScale;
        return scale > 0.0001f ? AutoFeatherPixels / scale : 0f;
    }

    /// <summary>
    /// One rounded corner, or the sharp point it collapses to at radius zero.
    /// </summary>
    /// <remarks>
    /// A zero-radius corner must emit exactly one vertex. Running the arc anyway would emit
    /// `segments` coincident points, which the triangulator then has to strip and the feather
    /// would treat as a run of zero-length edges with no usable normal.
    /// </remarks>
    private static void Corner(List<Vector2> outline, Vector2 centre, float radius, float from, float to, int segments)
    {
        // At radius zero the arc centre IS the rect corner, so one vertex there is the
        // whole answer.
        if (radius < 0.01f)
        {
            outline.Add(centre);
            return;
        }

        Arc(outline, centre, radius, radius, from, to, segments);
    }

    private static int CornerSegments(float radius, float scale)
    {
        return Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(Mathf.Abs(radius * scale)) * 1.6f), MinCornerSegments, MaxCornerSegments);
    }

    private static void Arc(List<Vector2> into, Vector2 centre, float rx, float ry, float fromDegrees, float toDegrees, int segments, bool closed = false)
    {
        var count = closed ? segments : segments + 1;

        for (var i = 0; i < count; i++)
        {
            var angle = Mathf.Deg2Rad * Mathf.Lerp(fromDegrees, toDegrees, i / (float)segments);
            into.Add(centre + new Vector2(Mathf.Cos(angle) * rx, Mathf.Sin(angle) * ry));
        }
    }

    private static List<Vector2> Loop(List<Vector2> points, bool closed)
    {
        if (!closed || points.Count < 2)
            return points;

        return new List<Vector2>(points) { points[0] };
    }

    private static List<Vector2> FromFlat(float[] flat)
    {
        var points = new List<Vector2>(flat.Length / 2);
        for (var i = 0; i + 1 < flat.Length; i += 2)
            points.Add(new Vector2(flat[i], flat[i + 1]));

        return points;
    }

    private static Vector2 Fit(VecScene scene, Rect target)
    {
        var sx = target.width / scene.ViewWidth;
        var sy = target.height / scene.ViewHeight;

        return scene.Fit switch
        {
            FitMode.Contain => Vector2.one * Mathf.Min(sx, sy),
            FitMode.Cover => Vector2.one * Mathf.Max(sx, sy),
            _ => new Vector2(sx, sy),
        };
    }

    private static Vector2 Centre(VecScene scene, Rect target, Vector2 scale)
    {
        if (scene.Fit == FitMode.Stretch)
            return Vector2.zero;

        return new Vector2(
            (target.width - scene.ViewWidth * scale.x) * 0.5f,
            (target.height - scene.ViewHeight * scale.y) * 0.5f);
    }
}
