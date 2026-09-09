using System;
using System.Collections.Generic;
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
    private const int MaxVertices = 60000;
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

    [ThreadStatic] internal static double BandSampleMs;
    [ThreadStatic] internal static double BandStripMs;
    [ThreadStatic] internal static double BandFeatherMs;
    [ThreadStatic] internal static int BandQuads;

    [ThreadStatic] private static double[]? _opMilliseconds;
    [ThreadStatic] private static int[]? _opCounts;

    internal static double[] OpMilliseconds => _opMilliseconds ??= new double[9];

    internal static int[] OpCounts => _opCounts ??= new int[9];

    /// <summary>Ablation state for the scene currently being tessellated.</summary>
    [ThreadStatic] private static bool NoFill;

    [ThreadStatic] private static bool NoFeather;

    [ThreadStatic] private static bool NoEval;

    /// <summary>False when the camera could not be resolved; LOD then assumes full size.</summary>
    [ThreadStatic] private static bool ScreenSizeKnown;

    private const int MaxGradientDepth = 5;

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
    }

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

        stats.Text.Clear();
        if (TextFound != null)
            stats.Text.AddRange(TextFound);

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

        (TextFound ??= new List<TextPlacement>(16)).Clear();

        NoFill = scene.DebugNoFill;
        NoFeather = scene.DebugNoFeather;
        NoEval = scene.DebugNoEval;

        var scale = Fit(scene, target);
        var offset = Centre(scene, target, scale);

        // Scene (top-left, +Y down) -> UGUI rect (bottom-left, +Y up).
        var viewbox =
            Matrix4x4.Translate(new Vector3(target.xMin + offset.x, target.yMax - offset.y, 0f)) *
            Matrix4x4.Scale(new Vector3(scale.x, -scale.y, 1f));

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
        if (scene.Problems.Count > 0 || emitted == 0)
            EmitErrorMarker(vh, target, scene.Problems.Count);

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
            if (vh.currentVertCount + 4 > MaxVertices)
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
            _ => new List<Vector2>(),
        };
    }

    private static void EmitNode(MeshBuilder vh, VecScene scene, VecNode node, EvalContext context, Stack<Frame> stack, ref int emitted)
    {
        if (vh.currentVertCount >= MaxVertices)
            return;

        switch (node.Op)
        {
            case VecOp.Group:
            {
                var mark = Stopwatch.GetTimestamp();
                var parent = stack.Peek();
                var anchor = new Vector2(node.Ax.Evaluate(context), node.Ay.Evaluate(context));

                // Spec order: scale, then rotate, then translate, all about the anchor.
                var local =
                    Matrix4x4.Translate(new Vector3(node.Tx.Evaluate(context), node.Ty.Evaluate(context), 0f)) *
                    Matrix4x4.Translate(anchor) *
                    Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, -node.Rotate.Evaluate(context))) *
                    Matrix4x4.Scale(new Vector3(node.Sx.Evaluate(context), node.Sy.Evaluate(context), 1f)) *
                    Matrix4x4.Translate(-anchor);

                // An inherited clip was expressed in the parent's coordinates; re-express it
                // in this group's, so it keeps clipping the same area of the picture.
                var inverse = local.inverse;
                var clip = parent.Clip?.Transform(inverse);
                var sceneToLocal = inverse * parent.SceneToLocal;

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

                        // Intersecting two convex regions: clip one boundary by the other.
                        // The result is still convex, so the half-plane representation
                        // survives.
                        var own = clip == null ? outline : clip.ClipPolygon(outline);
                        var region = ClipRegion.FromPolygon(own);

                        // A rejected clip used to fall back to NO clip, which is the least
                        // safe default available: "draw everything" rather than "draw
                        // nothing" or "complain".
                        if (region == null)
                            scene.Problem($"clip \"{node.ClipRef}\" is degenerate; group drawn unclipped");

                        clip = region ?? clip;
                    }
                    else
                    {
                        scene.Problem($"clip \"{node.ClipRef}\" is not declared in defs");
                    }
                }

                stack.Push(new Frame
                {
                    Matrix = parent.Matrix * local,
                    Opacity = parent.Opacity * Mathf.Clamp01(node.Opacity.Evaluate(context)),
                    Clip = clip,
                    Scale = parent.Scale * Mathf.Abs(node.Sx.Evaluate(context)),
                    SceneToLocal = sceneToLocal,
                });

                // Charged before descending, so a group's own cost (transform build, clip
                // re-expression) is separated from its children's.
                Charge(VecOp.Group, mark);

                foreach (var child in node.Children)
                    EmitNode(vh, scene, child, context, stack, ref emitted);

                stack.Pop();
                break;
            }

            case VecOp.Repeat:
            {
                var instances = LodCount(node, scene, stack.Peek().Scale);

                for (var i = 0; i < instances; i++)
                {
                    if (vh.currentVertCount >= MaxVertices)
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

                Charge(VecOp.Ellipse, mark);
                break;
            }

            case VecOp.Band:
            {
                var mark = Stopwatch.GetTimestamp();
                EmitBand(vh, scene, node, context, stack.Peek());
                emitted++;
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
                Charge(node.Op, mark);
                break;
            }

            case VecOp.Text:
            {
                CollectText(scene, node, context, stack.Peek());
                emitted++;
                break;
            }

            case VecOp.Path:
            {
                var mark = Stopwatch.GetTimestamp();
                EmitPathData(vh, scene, node, context, stack.Peek());
                emitted++;
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
    private static void CollectText(VecScene scene, VecNode node, EvalContext context, Frame frame)
    {
        if (TextFound == null || NoFill)
            return;

        var body = node.TextLiteral;
        if (node.TextData != null)
            context.Strings.TryGetValue(node.TextData, out body);

        if (string.IsNullOrEmpty(body))
            return;

        var x = node.X.Evaluate(context);
        var y = node.Y.Evaluate(context);
        var w = node.W.Evaluate(context);
        var h = node.H.Evaluate(context);

        var a = frame.Matrix.MultiplyPoint3x4(new Vector2(x, y));
        var b = frame.Matrix.MultiplyPoint3x4(new Vector2(x + w, y + h));

        var paint = ResolvePaint(scene, node, context, frame, stroke: false);

        Rect? clip = null;
        if (frame.Clip != null)
            clip = frame.Clip.Bounds(frame.Matrix);

        TextFound.Add(new TextPlacement
        {
            Text = body!,
            Rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                                   Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)),
            Size = (node.TextSize?.Evaluate(context) ?? 12f) * frame.Scale,
            Colour = paint.At(new Vector2(x, y)),
            Align = node.Align,
            VAlign = node.VAlign,
            Font = node.FontFamily,
            Bold = node.Bold,
            CharSpacing = node.CharSpacing,
            Fit = node.Fit,
            MinSize = (node.MinSize?.Evaluate(context) ?? 6f) * frame.Scale,
            Rotation = -Mathf.Atan2(frame.Matrix.m10, frame.Matrix.m00) * Mathf.Rad2Deg,
            ClipRect = clip,
        });
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

        var segments = CornerSegments(Mathf.Max(rx, ry), scale);
        Arc(outline, new Vector2(x + w - rx, y + ry), rx, ry, -90f, 0f, segments);
        Arc(outline, new Vector2(x + w - rx, y + h - ry), rx, ry, 0f, 90f, segments);
        Arc(outline, new Vector2(x + rx, y + h - ry), rx, ry, 90f, 180f, segments);
        Arc(outline, new Vector2(x + rx, y + ry), rx, ry, 180f, 270f, segments);
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
        // Shadows first: they sit beneath the shape, and in declaration order like CSS.
        if (node.Shadows != null && closed)
        {
            foreach (var shadow in node.Shadows)
                Shadow.Emit(vh, outline, shadow, frame.Matrix, frame.Scale * ScreenScale, frame.Clip);
        }

        if (node.HasFill)
            FillContour(vh, node, context, frame, outline, null, ResolvePaint(scene, node, context, frame, stroke: false));

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
            if (vh.currentVertCount + samples * 2 > MaxVertices)
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
            FillContour(vh, node, context, frame, points, null, ResolvePaint(scene, node, context, frame, stroke: false));

        StrokeOutline(vh, scene, node, context, frame, points, node.Closed);
    }

    private static void EmitPathData(MeshBuilder vh, VecScene scene, VecNode node, EvalContext context, Frame frame)
    {
        if (node.Path == null || node.Path.IsEmpty)
            return;

        var subpaths = node.Path.Flatten(frame.Scale * ScreenScale);
        if (subpaths.Count == 0)
            return;

        if (node.HasFill)
            FillSubpaths(vh, scene, node, context, frame, subpaths);

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

        if (frame.Clip != null)
        {
            // Clipping a filled shape reshapes its boundary, so it happens before
            // triangulation rather than by discarding triangles afterwards.
            contour = frame.Clip.ClipPolygon(outer, ClippedScratch);
            if (contour.Count < 3)
                return;

            // A hole straddling the clip boundary needs a boolean subtraction, not a convex
            // clip. Say so rather than drawing something quietly wrong.
            if (holes is { Count: > 0 })
            {
                ScriptedScreensVectorPlugin.Log?.LogWarning("clipped fills cannot carry holes; holes ignored");
                holes = null;
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
        if (paint.IsGradient && paint.Gradient!.Radial && holes == null && Encloses(contour, paint.Gradient.Focus))
        {
            FillRadialBands(vh, contour, paint, frame.Matrix, frame.Scale);
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
            else if (holes == null && !NeedsRefinement(paint) && IsConvex(contour))
            {
                FanShared(vh, contour, paint, frame.Matrix);
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
            FeatherRing(vh, contour, paint, feather, frame.Matrix, frame.Clip);
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
        if (vh.currentVertCount + piece.Count > MaxVertices)
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

    private static double Elapsed(long mark)
    {
        return (Stopwatch.GetTimestamp() - mark) * 1000d / Stopwatch.Frequency;
    }

    private static void Charge(VecOp op, long mark)
    {
        OpMilliseconds[(int)op] += (Stopwatch.GetTimestamp() - mark) * 1000d / Stopwatch.Frequency;
        OpCounts[(int)op]++;
    }

    private static bool NeedsRefinement(Paint paint)
    {
        return paint.IsGradient && (paint.Gradient!.Radial || paint.Gradient.StopCount > 2);
    }

    /// <summary>
    /// Fans a convex outline, adding each vertex once and indexing it.
    /// </summary>
    /// <remarks>
    /// The per-triangle emitter duplicates every vertex three times, which for a quad means
    /// six vertices where four would do. Sharing matters here because a mote field emits
    /// thousands of these per frame.
    /// </remarks>
    private static void FanShared(MeshBuilder vh, List<Vector2> outline, Paint paint, Matrix4x4 matrix)
    {
        var count = outline.Count;
        if (count < 3 || vh.currentVertCount + count > MaxVertices)
            return;

        var origin = vh.currentVertCount;

        for (var i = 0; i < count; i++)
            vh.AddVert(matrix.MultiplyPoint3x4(outline[i]), paint.At(outline[i]), Vector2.zero);

        for (var i = 1; i < count - 1; i++)
            vh.AddTriangle(origin, origin + i, origin + i + 1);
    }

    private static bool IsConvex(List<Vector2> points)
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
    private static void FillRadialBands(MeshBuilder vh, List<Vector2> outline, Paint paint, Matrix4x4 matrix, float scale)
    {
        var count = outline.Count;
        // In bounding-box mode the focus is a 0..1 fraction, so it has to be mapped back
        // into the shape's own space before rings can be built around it.
        var focusPoint = paint.FromGradientSpace(paint.Gradient!.Focus);

        // Ring count follows the shape's ON-SCREEN radius, not just its stop count. Bands
        // are visible as steps once they are more than a couple of pixels apart, and the
        // same scene draws at very different sizes with console resolution and camera
        // distance -- a fixed count bands badly when large and wastes mesh when small.
        var sceneRadius = 0f;
        foreach (var point in outline)
            sceneRadius = Mathf.Max(sceneRadius, (point - focusPoint).magnitude);

        var screenRadius = sceneRadius * scale * ScreenScale;
        var rings = Mathf.Clamp(Mathf.CeilToInt(screenRadius / 2.5f), 12, 96);

        // More stops need at least enough bands to resolve each one.
        rings = Mathf.Min(Mathf.Max(rings, 8 * Mathf.Max(1, paint.Gradient.StopCount - 1)), 96);

        if (vh.currentVertCount + count * rings + 1 > MaxVertices)
            return;

        var focus = focusPoint;
        var origin = vh.currentVertCount;

        vh.AddVert(matrix.MultiplyPoint3x4(focus), paint.At(focus), Vector2.zero);

        for (var ring = 1; ring <= rings; ring++)
        {
            var ratio = ring / (float)rings;
            for (var i = 0; i < count; i++)
            {
                var point = focus + (outline[i] - focus) * ratio;
                vh.AddVert(matrix.MultiplyPoint3x4(point), paint.At(point), Vector2.zero);
            }
        }

        // Innermost ring fans out from the focus.
        for (var i = 0; i < count; i++)
            vh.AddTriangle(origin, origin + 1 + i, origin + 1 + (i + 1) % count);

        // Remaining rings are quad strips.
        for (var ring = 1; ring < rings; ring++)
        {
            var inner = origin + 1 + (ring - 1) * count;
            var outer = origin + 1 + ring * count;

            for (var i = 0; i < count; i++)
            {
                var next = (i + 1) % count;
                vh.AddTriangle(inner + i, outer + i, outer + next);
                vh.AddTriangle(inner + i, outer + next, inner + next);
            }
        }
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
        var refine = paint.IsGradient && (paint.Gradient!.Radial || paint.Gradient.StopCount > 2);

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

    private static void Subdivide(MeshBuilder vh, Vector2 a, Vector2 b, Vector2 c, Paint paint, Matrix4x4 matrix, int depth)
    {
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
        if (vh.currentVertCount + 3 > MaxVertices)
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

        var runs = new List<List<Vector2>>();
        var wasClosed = closed;

        if (node.DashPattern.Length > 0)
        {
            runs.AddRange(Stroke.Dash(Loop(points, closed), node.DashPattern, node.DashOffset.Evaluate(context)));
            wasClosed = false;
        }
        else
        {
            runs.Add(points);
        }

        foreach (var run in runs)
        {
            // A clipped stroke is cut into the pieces that fall inside, rather than being
            // redirected along the boundary the way a filled contour is.
            if (frame.Clip == null)
            {
                Stroke.Emit(vh, run, wasClosed, width, paint, node.Cap, node.Join, node.MiterLimit, feather, frame.Matrix, MaxVertices);
                continue;
            }

            foreach (var piece in frame.Clip.ClipPolyline(Loop(run, wasClosed)))
                Stroke.Emit(vh, piece, closed: false, width, paint, node.Cap, node.Join, node.MiterLimit, feather, frame.Matrix, MaxVertices);
        }
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
            if (scene.Gradients.TryGetValue(name!, out var gradient))
            {
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
        if (!string.IsNullOrEmpty(bound) && context.Colours.TryGetValue(bound!, out var supplied))
            return new Paint(supplied, null, opacity);

        return new Paint(stroke ? node.StrokeColour : node.Fill, null, opacity);
    }

    private static void FeatherEdge(MeshBuilder vh, List<Vector2> edge, List<Vector2> away, Paint paint, float width, Matrix4x4 matrix, ClipRegion? clip, float opacityScale = 1f)
    {
        // A ramp that starts transparent has nothing to feather: the edge is already
        // invisible, and a halo there would be brighter than the shape it borders.
        if (opacityScale <= 0.002f)
            return;

        if (vh.currentVertCount + edge.Count * 2 > MaxVertices)
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
    private static void FeatherRing(MeshBuilder vh, List<Vector2> outline, Paint paint, float width, Matrix4x4 matrix, ClipRegion? clip)
    {
        var count = outline.Count;
        if (count < 3 || vh.currentVertCount + count * 2 > MaxVertices)
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

            vh.AddVert(matrix.MultiplyPoint3x4(outline[i]), solid, Vector2.zero);
            vh.AddVert(matrix.MultiplyPoint3x4(outer), faded, Vector2.zero);
        }

        for (var i = 0; i < count; i++)
        {
            var a = origin + i * 2;
            var b = origin + ((i + 1) % count) * 2;
            vh.AddTriangle(a, a + 1, b + 1);
            vh.AddTriangle(a, b + 1, b);
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
