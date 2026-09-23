using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SS = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem;

namespace ScriptedScreensVector;

/// <summary>
/// The vector layer's draw surface: a UGUI mesh covering one ScriptedScreens element.
/// </summary>
/// <remarks>
/// Lives on a <em>child</em> of the element's host GameObject, not the host itself, because
/// UGUI allows one <see cref="Graphic"/> per GameObject and ScriptedScreens has already put
/// its fallback <see cref="Image"/> on the host for unrecognised element types.
///
/// The animation rule that makes this worth building: a scene whose attributes never
/// reference <c>t</c> is tessellated once and then costs nothing per frame. Only
/// time-varying scenes mark themselves dirty in <see cref="Update"/>.
/// </remarks>
internal sealed class VectorGraphic : MaskableGraphic, IPointerClickHandler, IScrollHandler, IBeginDragHandler, IDragHandler
{
    /// <summary>
    /// Per-surface rebuild cost, reported periodically.
    /// </summary>
    /// <remarks>
    /// Deliberately **per surface**, not aggregated. The first version summed every surface
    /// and divided by their count, which quietly averaged unrelated things: a console
    /// animating at 28 Hz beside a static one reported "14 Hz each", below a floor of 15 that
    /// nothing had actually breached, and `ms per rebuild` blended two different scenes into
    /// a number describing neither. Comparing two runs against that is not possible.
    ///
    /// Each line now names its scene, so A/B runs can be read directly.
    /// </remarks>
    private static class Stats
    {
        private const float ReportIntervalSeconds = 5f;

        private static readonly List<VectorGraphic> Live = new();
        private static readonly Stopwatch Timer = new();
        private static float _nextReport;

        internal static void Register(VectorGraphic graphic)
        {
            if (!Live.Contains(graphic))
                Live.Add(graphic);
        }

        internal static void Unregister(VectorGraphic graphic)
        {
            Live.Remove(graphic);
        }

        internal static void ReportIfDue()
        {
            if (!VectorConfig.Diagnostics)
                return;

            if (!Timer.IsRunning)
                Timer.Start();

            var now = (float)Timer.Elapsed.TotalSeconds;
            if (now < _nextReport)
                return;

            _nextReport = now + ReportIntervalSeconds;

            foreach (var graphic in Live)
            {
                if (graphic == null)
                    continue;

                if (graphic._rebuilds == 0)
                {
                    // Zero is a result, not an absence: it means culled, paused, or a scene
                    // with no `t` reference. Silence would look like the surface had gone.
                    ScriptedScreensVectorPlugin.Log?.LogInfo(
                        $"vector \"{graphic._sceneId}\": idle (off screen, paused, or static)");
                    continue;
                }

                var hz = graphic._rebuilds / ReportIntervalSeconds;
                var perRebuild = graphic._milliseconds / graphic._rebuilds;
                var load = graphic._milliseconds / (ReportIntervalSeconds * 1000d) * 100d;

                var tessellate = graphic._tessellateMs / graphic._rebuilds;
                var upload = graphic._uploadMs / graphic._rebuilds;

                // Label which half is on the frame. Tessellation now runs on a worker, so
                // the big number no longer costs frames and reading it as though it does
                // would send the next round of tuning straight back at the wrong target.
                // Main-thread cost is the upload, and only the upload.
                var mainThread = graphic._uploadMs / (ReportIntervalSeconds * 1000d) * 100d;

                // Worker load from CPU time, not stopwatch time. A descheduled worker keeps
                // accumulating wall milliseconds while burning no CPU, so the stopwatch
                // figure is an upper bound and can overstate load badly on a busy machine.
                var cpuLoad = graphic._tessellateCpuMs / (ReportIntervalSeconds * 1000d) * 100d;
                var workerText = graphic._tessellateCpuMs > 0d
                    ? $"{cpuLoad:F1}% of a worker core (cpu; {load:F1}% wall)"
                    : $"{load:F1}% of a worker core (wall)";

                ScriptedScreensVectorPlugin.Log?.LogInfo(
                    $"vector \"{graphic._sceneId}\": {hz:F0} Hz, {perRebuild:F2} ms/rebuild " +
                    $"(tessellate {tessellate:F2} off-thread + upload {upload:F2} on-thread), " +
                    $"{mainThread:F2}% of a frame on the main thread, " +
                    $"{workerText}, {graphic._peakVertices} verts, " +
                    $"{graphic._lastShapeCount} shapes, " +
                    (graphic._screenPixels < 0f ? "size UNKNOWN" : $"{graphic._screenPixels:F0} px") +
                    $", job still running at Update on {graphic._lateAtUpdate} frame(s), landed in LateUpdate {graphic._rescuedLate}, received {graphic._payloads} data payload(s) and {graphic._structures} structure(s)");

                // Where the time actually went, biggest first. Six inferences about this
                // have been wrong; this is measured per node type.
                var names = new[] { "G", "RP", "R", "C", "YS", "L/Y", "LS", "SP", "P", "T", "SC", "-" };
                var order = new int[names.Length];
                for (var i = 0; i < order.Length; i++)
                    order[i] = i;

                System.Array.Sort(order, (a, b) => graphic._opMs[b].CompareTo(graphic._opMs[a]));

                var breakdown = new System.Text.StringBuilder();
                for (var i = 0; i < 4; i++)
                {
                    var op = order[i];
                    if (graphic._opMs[op] <= 0d)
                        continue;

                    if (breakdown.Length > 0)
                        breakdown.Append(", ");

                    breakdown.Append(
                        $"{names[op]} {graphic._opMs[op] / graphic._rebuilds:F2}ms x{graphic._opCount[op]}");
                }

                if (breakdown.Length > 0)
                    ScriptedScreensVectorPlugin.Log?.LogInfo($"    by op: {breakdown}");

                if (graphic._bandSampleMs + graphic._bandStripMs + graphic._bandFeatherMs > 0d)
                {
                    ScriptedScreensVectorPlugin.Log?.LogInfo(
                        $"    YS phases: sample {graphic._bandSampleMs / graphic._rebuilds:F2}ms, "
                        + $"strip {graphic._bandStripMs / graphic._rebuilds:F2}ms, "
                        + $"feather {graphic._bandFeatherMs / graphic._rebuilds:F2}ms, "
                        + $"{graphic._bandQuads} quads");
                }

                graphic._rebuilds = 0;
                graphic._lateAtUpdate = 0;
                graphic._payloads = 0;
                graphic._structures = 0;
                graphic._rescuedLate = 0;
                graphic._countersSince = now;
                graphic._milliseconds = 0d;
                graphic._tessellateMs = 0d;
                graphic._uploadMs = 0d;
                graphic._peakVertices = 0;
                graphic._tessellateCpuMs = 0d;
                graphic._bandSampleMs = 0d;
                graphic._bandStripMs = 0d;
                graphic._bandFeatherMs = 0d;
                System.Array.Clear(graphic._opMs, 0, graphic._opMs.Length);
            }
        }
    }

    /// <summary>
    /// CPU time actually consumed by the calling thread, in milliseconds.
    /// </summary>
    /// <remarks>
    /// A Stopwatch measures WALL time. A worker that gets descheduled mid-job keeps
    /// accumulating elapsed milliseconds while consuming no CPU at all, so reporting
    /// stopwatch time as though it were CPU load overstates it — possibly by a lot on a busy
    /// machine, and the difference is invisible unless measured.
    ///
    /// GetThreadTimes reports kernel + user time in 100 ns units. Its resolution is the
    /// scheduler quantum (~15.6 ms), which is far too coarse for one job but averages out
    /// across the ~150 jobs in a five-second reporting window.
    /// </remarks>
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetThreadTimes(
        IntPtr thread, out long creation, out long exit, out long kernel, out long user);

    private static double ThreadCpuMilliseconds()
    {
        try
        {
            if (GetThreadTimes(GetCurrentThread(), out _, out _, out var kernel, out var user))
                return (kernel + user) / 10000d;
        }
        catch (System.EntryPointNotFoundException)
        {
            // Not Windows. Fall back to reporting nothing rather than a wrong number.
        }
        catch (System.DllNotFoundException)
        {
        }

        return -1d;
    }

    private static Camera? _camera;

    private TextLayer? _text;

    /// <summary>The `src` text this surface was built from; null for the table form.</summary>
    internal string? StructureText { get; set; }

    /// <summary>The scene this surface is showing, for the stats tool.</summary>
    internal string SceneId => _sceneId;

    private readonly List<HitRegion> _hits = new();

    /// <summary>Where each `SC` container landed last rebuild, for wheel and drag to find.</summary>
    private readonly List<ScrollRegion> _scrolls = new();

    /// <summary>How far each `SC` is scrolled, in scene units. Survives rebuilds.</summary>
    private readonly Dictionary<string, float> _offsets = new(StringComparer.Ordinal);

    /// <summary>SC nodes carrying `so`/`sov`, and the version last applied per id.</summary>
    private readonly List<VecNode> _forcedScrollNodes = new();
    private readonly Dictionary<string, float> _forcedApplied = new(StringComparer.Ordinal);

    /// <summary>
    /// Raised when an `SC` container's offset, scroll range or viewport changed, once the
    /// rebuild that shows it has landed.
    /// </summary>
    /// <remarks>
    /// Client-side and main-thread; nothing crosses the network. For other mods on the same
    /// client (a page script wants `scroll` events and a real `scrollTop`); a chip cannot see
    /// it. Arguments: the `vector` element's host object, the `SC` id, then offset, maximum
    /// offset and viewport height, all in scene units. At most once per container per rebuild,
    /// so it runs at the rebuild rate (MaximumHz at most), not per wheel event.
    /// </remarks>
    public static event Action<GameObject, string, float, float, float>? ScrollChanged;

    private readonly Dictionary<string, Vector3> _reportedScrolls = new(StringComparer.Ordinal);

    /// <summary>
    /// The current state of one `SC` under a `vector` element's host: offset, maximum offset
    /// and viewport height in scene units. False when the host has no such container yet.
    /// </summary>
    public static bool TryGetScroll(GameObject host, string scrollId, out float offset, out float max, out float view)
    {
        offset = max = view = 0f;
        var graphic = host != null ? host.GetComponentInChildren<VectorGraphic>() : null;
        if (graphic == null)
            return false;

        foreach (var region in graphic._scrolls)
        {
            if (region.Id != scrollId)
                continue;

            graphic._offsets.TryGetValue(region.Id, out var at);
            offset = region.Clamp(at);
            max = region.Max;
            view = region.View;
            return true;
        }

        return false;
    }

    private void ReportScrolls()
    {
        if (ScrollChanged == null)
            return;

        var host = transform.parent != null ? transform.parent.gameObject : gameObject;
        foreach (var region in _scrolls)
        {
            _offsets.TryGetValue(region.Id, out var at);
            var now = new Vector3(region.Clamp(at), region.Max, region.View);
            if (_reportedScrolls.TryGetValue(region.Id, out var last) && last == now)
                continue;

            _reportedScrolls[region.Id] = now;
            try
            {
                ScrollChanged(host, region.Id, now.x, now.y, now.z);
            }
            catch (Exception ex)
            {
                ScriptedScreensVectorPlugin.Log?.LogWarning($"a ScrollChanged handler failed: {ex}");
            }
        }
    }

    private Vector2 _dragLast;
    private SS.UiPointerDownForwarder? _forwarder;
    private string _elementId = string.Empty;

    private ScrollRect? _scroll;
    private bool _scrollSearched;
    private float _lastScrollY = float.NaN;

    /// <summary>When the counters were last cleared, so a rate can be a real rate.</summary>
    /// <remarks>
    /// `vector_stats` used to divide the rebuild count by the five-second reporting interval,
    /// which is only right if a report has just happened. Reports only happen when Diagnostics
    /// is on, so with it off the counters were never cleared and the figure was the LIFETIME
    /// total over five -- a console quietly sitting still reported two thousand rebuilds a
    /// second. Dividing by the elapsed time instead is right either way.
    /// </remarks>
    private float _countersSince = -1f;

    /// <summary>Vertices in the mesh as it stands, unaffected by the stats reset.</summary>
    /// <remarks>
    /// `_peakVertices` is cleared every reporting interval, so a STATIC scene -- which does not
    /// rebuild and therefore never refills it -- reported zero vertices for ever after. That is
    /// exactly the scene whose size you most want to ask about.
    /// </remarks>
    private int _lastVertices;

    /// <summary>Extra meshes for a surface that does not fit in one. Usually empty.</summary>
    /// <remarks>
    /// This graphic presents slice 0 itself; each child presents the next. They are created in
    /// emission order and draw in sibling order, which reproduces the order a single mesh got
    /// from its triangle list.
    /// </remarks>
    private readonly List<VectorSlice> _slices = new();

    /// <summary>How many meshes the last rebuild needed, for the stats line.</summary>
    private int _sliceCount = 1;

    /// <summary>The on-screen size bucket the current geometry was built for.</summary>
    /// <remarks>
    /// Segment counts, the feather width and curve sampling all follow on-screen size, but
    /// that size is only read when a rebuild is DISPATCHED -- and a static scene dispatches
    /// only when its data or its scene changes. So a static scene kept whatever detail it had
    /// when it was last built, for ever. Built while off screen, where the scale is unknown
    /// and treated as 1, it stayed at minimum detail no matter how close you walked.
    ///
    /// Animated scenes never showed this because they rebuild constantly and pick the current
    /// size up on the way past.
    /// </remarks>
    private int _builtForBucket = int.MinValue;

    private double _tessellateCpuMs;
    private double _bandSampleMs;
    private double _bandStripMs;
    private double _bandFeatherMs;
    private int _bandQuads;

    /// <summary>The tessellation job in flight, or null.</summary>
    /// <remarks>
    /// While this is non-null a worker thread owns <c>_builder</c>, <c>_context</c> and the
    /// stats object. Nothing on the main thread may touch them until it lands.
    /// </remarks>
    private Task<(double Wall, double Cpu)>? _job;

    private VecScene? _pendingScene;
    private EvalContext? _pendingData;
    private bool _needsRebuild = true;

    /// <summary>New values are waiting. Rate-limited, unlike a structural rebuild.</summary>
    private bool _dataDirty;

    /// <summary>
    /// Glides with a stated duration: when each started, how long it runs and its curve.
    /// </summary>
    /// <remarks>
    /// Separate from the scene-wide blend because these do not share its clock. A name here
    /// runs from the moment its payload applied, for the time that payload asked for, whatever
    /// the gap to the next payload turns out to be.
    /// </remarks>
    private readonly Dictionary<string, (float Start, float Seconds, Easing Curve, float Delay)> _glides = new(StringComparer.Ordinal);

    /// <summary>When the last stated glide ends, so the scene keeps redrawing until it does.</summary>
    private float _glidesUntil;

    /// <summary>Reused while dropping finished glides; a dictionary cannot be edited mid-walk.</summary>
    private readonly List<string> _finishedGlides = new();
    private float _jobScreenPixels = -1f;
    private readonly TessellationStats _stats = new();

    private readonly EvalContext _context = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly MeshBuilder _builder = new();

    /// <summary>
    /// The surface's geometry. **Serialised deliberately**, so a capture clone keeps it.
    /// </summary>
    /// <remarks>
    /// ScriptedScreens' screen capture clones the surface tree with <c>Instantiate</c> and
    /// renders the clone. A mesh handed to a CanvasRenderer is not a serialised property, so
    /// the clone's renderer had nothing to draw and every capture of a vector console came
    /// back blank — which is why the whole port had to be checked by eye, and is recorded in
    /// the port feedback as probably its single biggest time cost.
    ///
    /// Instantiate copies serialised references as references when they point outside the
    /// hierarchy being cloned, which is how the HTML mod's RawImage keeps its live
    /// RenderTexture. A mesh behaves the same way, so marking the field is the whole fix: the
    /// clone shares the live mesh and paints exactly what is on screen.
    /// </remarks>
    [SerializeField] private Mesh? _mesh;

    /// <summary>
    /// True on the instance that created the mesh; false on a capture clone.
    /// </summary>
    /// <remarks>
    /// NOT serialised, and that is the point — it is how a clone knows what it is. A clone
    /// must never clear or destroy the mesh it is sharing, or capturing a console would wipe
    /// the console.
    /// </remarks>
    private bool _ownsMesh;

    private VecScene? _scene;
    private float _startTime;
    private float _lastRebuild;

    /// <summary>Shortest gap between payloads worth blending across.</summary>
    private const float MinBlendSeconds = 0.05f;

    /// <summary>Longest. Beyond this a scene is effectively static and should just snap.</summary>
    private const float MaxBlendSeconds = 1.5f;

    private float _dataArrived;
    private float _dataInterval = MinBlendSeconds;

    private string _sceneId = "?";
    private int _rebuilds;
    private double _milliseconds;
    private double _tessellateMs;
    private double _uploadMs;
    private readonly double[] _opMs = new double[12];
    private readonly int[] _opCount = new int[12];
    private int _peakVertices;
    private float _screenPixels = -1f;
    private int _lastShapeCount;

    /// <summary>Installs a parsed structure. Resets the clock so animations start at t=0.</summary>
    internal void SetScene(VecScene scene)
    {
        _structures++;
        if (_job != null)
        {
            _pendingScene = scene;
            return;
        }

        ApplyScene(scene);
    }

    private void ApplyScene(VecScene scene)
    {
        _forcedScrollNodes.Clear();
        CollectForcedScrolls(scene.Root, _forcedScrollNodes);

        _scene = scene;
        _needsRebuild = true;
        _sceneId = string.IsNullOrEmpty(scene.Id) ? "?" : scene.Id;
        _startTime = Now();
        SetVerticesDirty();
    }

    /// <summary>Replaces the data bindings referenced as <c>$name</c>. Cheap; called per tick.</summary>
    /// <summary>
    /// Gives the graphic what it needs to dispatch a click through ScriptedScreens.
    /// </summary>
    /// <remarks>
    /// The forwarder is ScriptedScreens' own <c>UiPointerDownForwarder</c>, put on a CHILD
    /// object with no graphic. Unity therefore never routes a pointer event to it, and it
    /// only ever fires when this class calls it — which is what lets the node id be resolved
    /// by hit test first and passed as the event's value. Left on this object it would also
    /// receive the click itself, and its 0.25 s debounce would swallow whichever of the two
    /// arrived second, at random.
    /// </remarks>
    internal void SetClickTarget(SS.UiPointerDownForwarder forwarder, string elementId)
    {
        _forwarder = forwarder;
        _elementId = elementId;
    }

    /// <summary>
    /// Routes a click to the node under the pointer, as the element's own click event.
    /// </summary>
    /// <remarks>
    /// The node id travels as the event's VALUE, not as its id: Lua registers handlers per
    /// element, and a vector node is not an element. So a scene declares `on_click` once on
    /// its structure element and receives `(nodeId, playerName)`.
    ///
    /// Last match wins, because the list is in draw order and the thing drawn last is the
    /// thing on top.
    /// </remarks>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (_forwarder == null || eventData == null || _hits.Count == 0)
            return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform, eventData.position, eventData.pressEventCamera, out var local))
        {
            return;
        }

        string? hit = null;
        for (var i = 0; i < _hits.Count; i++)
        {
            if (_hits[i].Contains(local))
                hit = _hits[i].Id;
        }

        if (hit == null)
            return;

        _forwarder.Value = hit;
        _forwarder.Id = _elementId;
        _forwarder.EventName = "click";
        _forwarder.OnPointerClick(eventData);
    }


    /// <summary>Wheel over an <c>SC</c> container scrolls it.</summary>
    /// <remarks>
    /// **Nothing is sent anywhere.** The offset is client-side state, so a wheel notch costs
    /// one rebuild and no network traffic, and the list keeps up with the mouse rather than
    /// with the 0.5 s tick. That is the whole reason this is a scene node and not a
    /// ScriptedScreens `scrollview` with the scene inside it.
    /// </remarks>
    public void OnScroll(PointerEventData eventData)
    {
        if (eventData == null || !Locate(eventData.position, eventData.pressEventCamera, out var region))
            return;

        // Wheel up is away from the reader, which shows earlier content, which is a smaller
        // offset -- scenes are +Y down and the offset counts downward from the top.
        Move(region, -eventData.scrollDelta.y * region.WheelStep);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData != null)
            _dragLast = eventData.position;
    }

    /// <summary>Dragging inside a container moves the content with the pointer.</summary>
    public void OnDrag(PointerEventData eventData)
    {
        if (eventData == null || !Locate(eventData.position, eventData.pressEventCamera, out var region))
            return;

        // Screen pixels, not canvas: the delta is only ever compared against itself and
        // scaled into scene units, and the canvas conversion is already in ToScene.
        var moved = eventData.position - _dragLast;
        _dragLast = eventData.position;

        // Canvas Y is up and scene Y is down, so dragging up reveals later content.
        Move(region, moved.y * region.ToScene);
    }

    /// <summary>Finds the container under a screen point, last drawn wins.</summary>
    private bool Locate(Vector2 screen, Camera? camera, out ScrollRegion region)
    {
        region = default;

        if (_scrolls.Count == 0
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screen, camera, out var local))
        {
            return false;
        }

        var found = false;
        for (var i = 0; i < _scrolls.Count; i++)
        {
            if (!_scrolls[i].Rect.Contains(local))
                continue;

            region = _scrolls[i];
            found = true;
        }

        return found && region.Max > 0f;
    }

    private void Move(ScrollRegion region, float by)
    {
        _offsets.TryGetValue(region.Id, out var offset);
        var next = region.Clamp(offset + by);

        if (Mathf.Approximately(next, offset))
            return;

        _offsets[region.Id] = next;
        _needsRebuild = true;
    }

    /// <summary>True when the surface has changed on-screen size enough to be worth rebuilding.</summary>
    /// <remarks>
    /// Buckets are multiplicative, roughly 15% apart, for the same reason the path flattener
    /// quantises its own scale: ordinary camera drift stays inside a bucket, so standing
    /// still and breathing does not retessellate anything, while actually walking up to a
    /// console does. An unknown size gets its own bucket, so coming into view counts as a
    /// change and the scene is rebuilt at its real size.
    /// </remarks>
    private bool ScaleChanged()
    {
        var bucket = ScaleBucket();

        if (bucket == _builtForBucket)
            return false;

        _builtForBucket = bucket;
        return true;
    }

    private int ScaleBucket()
    {
        var scale = ScreenPixelsPerCanvasUnit();

        if (scale <= 0f)
            return int.MinValue + 1;

        return Mathf.FloorToInt(Mathf.Log(scale) / Mathf.Log(1.15f));
    }

    /// <summary>
    /// Tessellates and uploads on the spot, for a capture that cannot wait for a worker.
    /// </summary>
    /// <remarks>
    /// Legal on the main thread for the same reason it is legal off it: the rebuild path is
    /// plain managed arithmetic with no Unity call in it. This is the slow way round and is
    /// only ever taken while a capture is running -- ordinary rendering keeps the work off the
    /// frame, which is the whole point of the threading.
    ///
    /// A job already in flight owns the builder and the context, so it is waited for and
    /// landed rather than raced. A capture is a debugging action; a few milliseconds of block
    /// is a fair price for a picture that is not blank.
    /// </remarks>
    private void BuildNow()
    {
        if (_job != null)
        {
            try
            {
                _job.Wait(250);
            }
            catch (System.AggregateException ex)
            {
                ScriptedScreensVectorPlugin.Log?.LogWarning($"vector \"{_sceneId}\": capture wait failed: {ex.GetBaseException().Message}");
            }

            LandJob();
        }

        if (_job != null || _scene == null)
            return;

        var rect = rectTransform.rect;
        if (rect.width <= 0f || rect.height <= 0f)
            return;

        _context.Time = Now() - _startTime;
        _context.Blend = 1f;

        // A capture shows where the values ARE going, not where a glide has got to, which is
        // what `Blend = 1` above says for the scene-wide one.
        _context.NameBlend.Clear();

        SampleScroll(rect);

        ApplyForcedScrolls();

        _context.ScrollOffsets.Clear();
        foreach (var pair in _offsets)
            _context.ScrollOffsets[pair.Key] = pair.Value;

        var screenScale = ScreenPixelsPerCanvasUnit();
        var known = screenScale > 0f;

        Tessellator.Emit(_builder, _scene, _context, rect, known ? screenScale : 1f, known, _stats);

        if (_scene.TextInOrder)
            TextOrder.Assign(_stats.Text, _builder);

        _sliceCount = _builder.Slices();

        if (_scene.TextInOrder)
            TextOrder.Resolve(_stats.Text, _builder, _sliceCount);
        _lastShapeCount = _stats.Shapes;
        _lastVertices = _builder.currentVertCount;
        _builtForBucket = ScaleBucket();
        _needsRebuild = false;
        _dataDirty = false;

        EnsureMesh();
        _builder.Apply(_mesh!);
        canvasRenderer.SetMesh(_mesh);

        // Kept: a blank capture is otherwise silent, and this says whether geometry existed
        // at the moment the picture was taken. It fires only during a capture.
        ScriptedScreensVectorPlugin.Log?.LogInfo(
            $"vector capture: \"{_sceneId}\" built inline, {_builder.currentVertCount} verts across {_sliceCount} mesh(es)");

        // A capture builds from inside UGUI's rebuild loop, where labels and masks cannot ask for
        // a rebuild of their own. Observed: after one capture a gradient label lost its colours
        // and masked labels went blank until something else happened to rebuild them. The next
        // normal rebuild redoes all of it outside the loop.
        _needsRebuild = true;

        // SLICES BEFORE TEXT, matching the normal path exactly. Sibling order is draw order
        // and both are children of this graphic, so creation order decides who draws over
        // whom. Under `ztext` (the default) OrderTextWithSlices rearranges them afterwards and
        // this only sets the starting point; under `ztext = 0` nothing reorders them, so
        // creating them the other way round would put geometry over every label permanently.
        // The two paths differing here is a bug by construction either way: a capture must
        // leave the surface in the state a normal rebuild would.
        ApplySlices();
        ApplyImages();

        if (_stats.Text.Count > 0 || _text != null)
        {
            _text ??= new TextLayer(rectTransform);
            _text.Apply(_stats.Text);

            if (_scene != null && _scene.TextInOrder)
                OrderTextWithSlices();
        }
    }

    /// <summary>Hands slices 1..n to child graphics, creating and retiring them as needed.</summary>
    /// <summary>The image cache version this surface last rebuilt against, while it waits on one.</summary>
    private int _imagesWaitingOn = -1;

    /// <summary>The texture slice 0 carries when an `IMG` is the first thing drawn.</summary>
    [SerializeField] private Texture? _texture;

    public override Texture mainTexture => _texture != null ? _texture : base.mainTexture;

    /// <summary>
    /// Hands each drawn image's texture to the mesh carrying it, and starts loading the rest.
    /// </summary>
    private void ApplyImages()
    {
        Texture? first = null;
        var sliceTextures = _slices.Count > 0 ? new Texture?[_slices.Count] : null;
        var waiting = false;

        foreach (var image in _stats.Images)
        {
            if (image.ShapeIndex < 0)
            {
                ImageCache.Request(image.Src);
                waiting = true;
                continue;
            }

            var slice = _builder.SlicesBefore(_builder.ShapeStart(image.ShapeIndex));
            var texture = ImageCache.Texture(image.Src);

            if (slice == 0)
                first = texture;
            else if (sliceTextures != null && slice - 1 < sliceTextures.Length)
                sliceTextures[slice - 1] = texture;
        }

        if (_texture != first)
        {
            _texture = first;
            SetMaterialDirty();
        }

        for (var i = 0; i < _slices.Count; i++)
        {
            if (_slices[i] != null)
                _slices[i].SetTexture(sliceTextures?[i]);
        }

        // Rebuilt when any request finishes. A static scene has nothing else to wake it.
        _imagesWaitingOn = waiting ? ImageCache.Version : -1;
    }

    private void ApplySlices()
    {
        var extra = Mathf.Max(0, _sliceCount - 1);

        while (_slices.Count < extra)
        {
            var host = new GameObject("VectorSlice", typeof(RectTransform), typeof(CanvasRenderer), typeof(VectorSlice));
            var rect = host.GetComponent<RectTransform>();

            rect.SetParent(rectTransform, worldPositionStays: false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var slice = host.GetComponent<VectorSlice>();
            slice.material = material;

            _slices.Add(slice);
        }

        for (var i = 0; i < _slices.Count; i++)
        {
            var slice = _slices[i];
            if (slice == null)
                continue;

            if (i < extra)
            {
                slice.gameObject.SetActive(true);
                slice.Present(_builder, i + 1);
            }
            else
            {
                // Disabled rather than destroyed: a surface that crosses the threshold as you
                // walk toward it would otherwise churn objects every few steps.
                slice.gameObject.SetActive(false);
            }
        }
    }

    /// <summary>
    /// Interleaves labels and meshes so that both obey scene order.
    /// </summary>
    /// <remarks>
    /// UGUI draws a parent before its children and children in sibling order, so the surface's
    /// own mesh is always first and everything else is arranged behind that. A label that has
    /// to sit under a shape is parked before the mesh carrying that shape:
    ///
    ///   surface mesh (slice 0) - labels at depth 1 - slice 1 - labels at depth 2 - ...
    ///
    /// Only called when the scene asked for `ztext`. Without it, labels keep the position
    /// their creation order gave them -- after every slice, which is where text has always
    /// drawn -- and no scene written before this existed changes.
    /// </remarks>
    private void OrderTextWithSlices()
    {
        if (_text == null)
            return;

        var placements = _stats.Text;
        var extra = Mathf.Max(0, _sliceCount - 1);
        var sibling = 0;

        for (var depth = 1; depth <= _sliceCount; depth++)
        {
            for (var i = 0; i < placements.Count; i++)
            {
                if (placements[i].SliceDepth != depth)
                    continue;

                // Moved only when it is not already there: re-setting an unchanged sibling index
                // every rebuild is hierarchy churn for nothing, the same fault as re-applying
                // an unchanged label.
                var mask = _text.MaskFor(i);
                if (mask != null)
                {
                    if (mask.GetSiblingIndex() != sibling)
                        mask.SetSiblingIndex(sibling);

                    sibling++;
                }
            }

            // The mesh this depth sits on top of. Slice d is child d-1; the last depth has
            // no slice after it, which is the ordinary "text on top" case.
            var slice = depth - 1;
            if (slice < extra && slice < _slices.Count && _slices[slice] != null)
            {
                var sliceTransform = _slices[slice].transform;
                if (sliceTransform.GetSiblingIndex() != sibling)
                    sliceTransform.SetSiblingIndex(sibling);

                sibling++;
            }
        }
    }

    /// <summary>Writes one surface's state into the vector_stats report.</summary>
    internal void Describe(System.Text.StringBuilder into)
    {
        into.AppendLine($"scene \"{_sceneId}\"");

        if (_scene == null)
        {
            into.AppendLine("  no scene parsed");
            return;
        }

        into.AppendLine($"  nodes {_scene.Root.Count} root, {_lastShapeCount} shapes emitted, {_lastVertices} verts"
                        + (_sliceCount > 1 ? $" across {_sliceCount} meshes" : string.Empty));

        var tess = _rebuilds > 0 ? _tessellateMs / _rebuilds : 0d;
        var up = _rebuilds > 0 ? _uploadMs / _rebuilds : 0d;

        var window = _countersSince < 0f ? 0f : Now() - _countersSince;
        var rate = window > 0.05f ? _rebuilds / window : 0f;

        into.AppendLine($"  rebuilds {VectorStatsTool.N(rate)}/s over {VectorStatsTool.N(window, 0)}s, "
                        + $"{VectorStatsTool.N(tess)} ms tessellate off-thread, "
                        + $"{VectorStatsTool.N(up)} ms upload on-thread");

        var size = _screenPixels < 0f ? "size unknown" : VectorStatsTool.N(_screenPixels, 0) + " px";
        into.AppendLine($"  on screen {size}, animated {_scene.UsesTime}, scroll-driven {_scene.UsesScroll}");

        if (_stats.Starved != null)
            into.AppendLine($"  TOO LARGE at this size: {_stats.Starved} was dropped");

        if (_scene.Problems.Count > 0)
        {
            into.AppendLine("  PROBLEMS");
            foreach (var problem in _scene.Problems)
                into.AppendLine($"    {problem}");
        }

        if (_stats.Missing.Count > 0)
        {
            into.AppendLine("  UNRESOLVED DATA NAMES (drawn magenta)");
            foreach (var name in _stats.Missing)
                into.AppendLine($"    ${name}");
        }

        var dropped = _builder.Dropped;
        if (dropped != null)
            into.AppendLine($"  DROPPED: {dropped} -- everything in it is missing from the console");

        var textWarnings = _text?.Warnings;
        if (textWarnings is { Count: > 0 })
        {
            into.AppendLine("  TEXT");
            foreach (var warning in textWarnings)
                into.AppendLine($"    {warning}");
        }

        // Every section above counts. This line used to consider only two of them, so a
        // dropped shape or a shrunk text shadow still read as "no problems".
        if (_scene.Problems.Count == 0 && _stats.Missing.Count == 0 && _stats.Starved == null
            && dropped == null && textWarnings is not { Count: > 0 })
            into.AppendLine("  no problems");
    }

    /// <summary>Applies a `nodes = { id = { ... } }` patch from the data element.</summary>
    internal void PatchScene(SS.UiProp[] props)
    {
        if (_scene == null)
            return;

        if (SceneParser.PatchNodes(props, _scene))
            _needsRebuild = true;
    }

    /// <summary>
    /// The data this surface is currently showing, as a payload that can be replayed.
    /// </summary>
    /// <remarks>
    /// A capture calls <c>RebuildSurfaceFromModel</c>, which destroys every host and builds a
    /// new graphic with an empty context. ScriptedScreens replays the stored elements, but the
    /// data element is only the LAST payload -- and under `keep = 1` a payload is a patch, so
    /// replaying it recovers whatever that one tick happened to mention and nothing else. The
    /// merged state lives here and nowhere else, so it has to be handed over directly.
    ///
    /// Marked as a patch, because that is what it is: it restores what was on screen without
    /// claiming to be the whole truth, and the next real payload still decides for itself.
    /// </remarks>
    internal EvalContext Snapshot()
    {
        var copy = new EvalContext { KeepUnmentioned = true };

        foreach (var pair in _context.Scalars)
            copy.Scalars[pair.Key] = pair.Value;
        foreach (var pair in _context.Arrays)
            copy.Arrays[pair.Key] = pair.Value;
        foreach (var pair in _context.Colours)
            copy.Colours[pair.Key] = pair.Value;
        foreach (var pair in _context.Strings)
            copy.Strings[pair.Key] = pair.Value;
        foreach (var pair in _context.StringArrays)
            copy.StringArrays[pair.Key] = pair.Value;
        foreach (var pair in _context.ColourArrays)
            copy.ColourArrays[pair.Key] = pair.Value;

        return copy;
    }

    /// <summary>
    /// Takes a payload. Returns true when the context was KEPT rather than read and dropped,
    /// so the caller knows it may not reuse its buffer.
    /// </summary>
    internal bool SetData(EvalContext source)
    {
        _payloads++;
        if (_job != null)
        {
            // One payload may arrive while another is still parked. A full payload replaces
            // it; a `keep = 1` patch is folded in, since replacing it lost what the earlier
            // patch carried.
            if (_pendingData == null)
            {
                _pendingData = source;
                return true;
            }

            _pendingData.MergeFrom(source);
            return false;
        }

        ApplyData(source);
        return false;
    }

    private static bool EasesSomething(EvalContext source)
    {
        foreach (var name in source.Scalars.Keys)
        {
            if (!source.Snapped.Contains(name))
                return true;
        }

        foreach (var name in source.Arrays.Keys)
        {
            if (!source.Snapped.Contains(name))
                return true;
        }

        return false;
    }

    private void ApplyData(EvalContext source)
    {
        // `keep = 1` means a payload is a PATCH: names it does not mention hold their last
        // value. Without it a payload is the whole truth and anything absent is gone, which
        // is what forces a console to resend every string it displays on every tick.
        var keep = source.KeepUnmentioned;

        // Only a payload carrying EASED numbers restarts the blend clock. A patch of snapped
        // values or strings used to restart it too, so a snapped value sent beside an eased one
        // collapsed the eased glide to the 0.05 s minimum -- it jumped. Patches arriving within
        // that minimum of each other are one batch, so two data elements sending in the same
        // tick do not shorten the glide either. A full payload always starts a batch, as before.
        var now = Now();
        var batch = !keep || (EasesSomething(source) && now - _dataArrived >= MinBlendSeconds);

        if (batch)
        {
            // Keep what is on screen: the next frames ease from it to the new payload rather
            // than snapping, which is what stops a data-driven gauge stepping at the tick rate.
            _context.Rebase(VectorConfig.SmoothData
                ? Mathf.Clamp01((now - _dataArrived) / _dataInterval)
                : 1f);

            _dataInterval = Mathf.Clamp(now - _dataArrived, MinBlendSeconds, MaxBlendSeconds);
            _dataArrived = now;

            // Hand the old arrays over rather than copying them: ReadData allocates fresh ones
            // for every payload, so the outgoing set can simply become the previous set.
            _context.PreviousArrays.Clear();
            foreach (var pair in _context.Arrays)
                _context.PreviousArrays[pair.Key] = pair.Value;
        }

        if (!keep)
            _context.Scalars.Clear();
        else
            _context.Evict(source);

        foreach (var pair in source.Scalars)
            _context.Scalars[pair.Key] = pair.Value;

        if (!keep)
            _context.Arrays.Clear();

        foreach (var pair in source.Arrays)
            _context.Arrays[pair.Key] = pair.Value;

        // `snap = 1`: no previous value, so these names read their new value at once.
        foreach (var name in source.Snapped)
        {
            _context.Previous.Remove(name);
            _context.PreviousArrays.Remove(name);
            _glides.Remove(name);
            _context.NameBlend.Remove(name);
        }

        // A stated glide starts now and runs for as long as the payload asked, regardless of
        // when the next payload arrives. Restating a name restarts its glide from whatever it
        // is showing, which `Rebase` above has already written into `Previous`.
        foreach (var pair in source.Eased)
        {
            _glides[pair.Key] = (now, pair.Value.Seconds, pair.Value.Curve, pair.Value.Delay);
            _glidesUntil = Mathf.Max(_glidesUntil, now + pair.Value.Delay + pair.Value.Seconds);
        }

        // A name this payload carries without timing goes back to the scene-wide blend; its
        // old glide would otherwise hold a stale fraction for ever.
        foreach (var name in source.Scalars.Keys)
        {
            if (!source.Eased.ContainsKey(name))
                Forget(name);
        }

        foreach (var name in source.Arrays.Keys)
        {
            if (!source.Eased.ContainsKey(name))
                Forget(name);
        }

        // Colours were parsed into `source` by ReadData and then dropped on the floor: this
        // copy did not exist, so `f = "$name"` never resolved and every data-bound fill fell
        // back to node.Fill, which is white. Not blended like scalars are -- a colour is not
        // a scalar, and easing one would need a per-channel lerp with no way to say "snap".
        if (!keep)
            _context.Colours.Clear();

        foreach (var pair in source.Colours)
            _context.Colours[pair.Key] = pair.Value;

        if (!keep)
            _context.Strings.Clear();

        foreach (var pair in source.Strings)
            _context.Strings[pair.Key] = pair.Value;

        // String and colour ARRAYS, which ReadData fills and this method used to drop on the
        // floor -- exactly the fault the comment above records for Colours, repeated the day
        // indexed bindings were added. `text = "$rows[i]"` resolved to nothing without it.
        if (!keep)
            _context.StringArrays.Clear();

        foreach (var pair in source.StringArrays)
            _context.StringArrays[pair.Key] = pair.Value;

        if (!keep)
            _context.ColourArrays.Clear();

        foreach (var pair in source.ColourArrays)
            _context.ColourArrays[pair.Key] = pair.Value;

        // Asked for here, not only through SetVerticesDirty: that reaches UpdateGeometry after
        // this frame's Update has passed, so a payload parked behind a running job waited an
        // extra frame -- a scene fed every frame rebuilt on every second one (35 Hz at 71 FPS).
        //
        // Its own flag rather than `_needsRebuild`, because the two deserve different answers:
        // a new STRUCTURE must draw at once, while new VALUES are just the next frame of an
        // animation and belong under the same ceiling as `t`. Without the split a page sending
        // 37 payloads a second rebuilt 37 times a second on every console it owned, on screen
        // or not, ignoring MaximumHz, rate LOD and the off-screen cull -- none of which a
        // hand-written console at 2 Hz could ever have revealed.
        _dataDirty = true;
        SetVerticesDirty();

        void Forget(string name)
        {
            if (_glides.Count == 0)
                return;

            _glides.Remove(name);
            _context.NameBlend.Remove(name);
        }
    }

    /// <summary>
    /// Works out how far along each stated glide is, for this rebuild.
    /// </summary>
    /// <remarks>
    /// Main thread, at dispatch, with everything else the clock decides -- the worker reads
    /// the fractions and never the time. A finished glide is dropped rather than left at 1,
    /// so the dictionary empties itself and the common case stays a count test.
    /// </remarks>
    private void SampleGlides(float now)
    {
        if (_glides.Count == 0)
        {
            if (_context.NameBlend.Count > 0)
                _context.NameBlend.Clear();

            return;
        }

        _finishedGlides.Clear();

        foreach (var pair in _glides)
        {
            // Clamped at the bottom as well as the top, so during a delay the fraction is 0 and
            // the value holds exactly where the payload found it.
            var progress = Mathf.Clamp01(
                (now - pair.Value.Start - pair.Value.Delay) / Mathf.Max(0.0001f, pair.Value.Seconds));
            _context.NameBlend[pair.Key] = VectorConfig.SmoothData ? pair.Value.Curve.Evaluate(progress) : 1f;

            if (progress >= 1f)
                _finishedGlides.Add(pair.Key);
        }

        foreach (var name in _finishedGlides)
        {
            _glides.Remove(name);
            _context.NameBlend.Remove(name);
            _context.Previous.Remove(name);
            _context.PreviousArrays.Remove(name);
        }
    }

    /// <summary>Shapes emitted by the last rebuild. Diagnostic.</summary>
    internal int ShapeCount => _lastShapeCount;

    /// <summary>
    /// How many screen pixels one canvas unit covers right now, or -1 when unknown.
    /// </summary>
    /// <remarks>
    /// Everything that adapts to on-screen size — rebuild rate, feather width, curve
    /// flattening — needs this, and it cannot come from the tessellation matrix: that matrix
    /// is built from the element's rect in **canvas** units and stops there, so distance is
    /// invisible to it.
    ///
    /// The camera is the awkward part. A world-space Canvas normally has a null
    /// <c>worldCamera</c> unless an event camera was assigned, so it has to fall back to the
    /// main camera. Treating world coordinates as screen coordinates in that case — which an
    /// earlier version did — reports a two-metre console as about two pixels wide, which
    /// starves the rebuild rate and sheds nearly every instance.
    ///
    /// **Unknown returns -1, and callers treat that as full quality.** Failing toward full
    /// detail costs performance; failing toward minimum detail makes the mod look broken,
    /// and that is the wrong way round.
    /// </remarks>
    private float ScreenPixelsPerCanvasUnit()
    {
        var rect = rectTransform.rect;
        if (rect.width <= 0f)
            return -1f;

        var target = canvas;
        if (target == null)
            return -1f;

        if (target.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            // Overlay canvases are already in screen space; scale is whatever the canvas
            // scaler applied, which lossyScale reports faithfully here.
            return Mathf.Abs(target.transform.lossyScale.x);
        }

        var camera = target.worldCamera != null ? target.worldCamera : ResolveCamera();
        if (camera == null)
            return -1f;

        var left = rectTransform.TransformPoint(new Vector3(rect.xMin, rect.yMin, 0f));
        var right = rectTransform.TransformPoint(new Vector3(rect.xMax, rect.yMin, 0f));

        var a = camera.WorldToScreenPoint(left);
        var b = camera.WorldToScreenPoint(right);

        // Behind the camera: the projection wraps and the distance is meaningless.
        if (a.z <= 0.01f || b.z <= 0.01f)
            return -1f;

        var pixels = Vector2.Distance(new Vector2(a.x, a.y), new Vector2(b.x, b.y));
        if (pixels <= 0.01f || pixels > 100000f)
            return -1f;

        return pixels / rect.width;
    }

    /// <summary>
    /// Whether any part of this element is within the viewport.
    /// </summary>
    /// <remarks>
    /// Projects the rect's four corners and tests their bounding box against the screen,
    /// generously padded — a partly visible console must still animate, and the box is a
    /// loose bound for a rotated rect.
    ///
    /// Unknown counts as visible. Failing toward drawing costs frames; failing toward not
    /// drawing means a console that stays blank, which reads as the mod being broken.
    /// </remarks>
    private bool IsOnScreen()
    {
        var target = canvas;
        if (target == null)
            return true;

        if (target.renderMode == RenderMode.ScreenSpaceOverlay)
            return true;

        var camera = target.worldCamera != null ? target.worldCamera : ResolveCamera();
        if (camera == null)
            return true;

        var rect = rectTransform.rect;

        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;
        var anyInFront = false;

        for (var i = 0; i < 4; i++)
        {
            var corner = new Vector3(
                i is 0 or 3 ? rect.xMin : rect.xMax,
                i is 0 or 1 ? rect.yMin : rect.yMax,
                0f);

            var projected = camera.WorldToScreenPoint(rectTransform.TransformPoint(corner));
            if (projected.z <= 0.01f)
                continue;

            anyInFront = true;
            minX = Mathf.Min(minX, projected.x);
            minY = Mathf.Min(minY, projected.y);
            maxX = Mathf.Max(maxX, projected.x);
            maxY = Mathf.Max(maxY, projected.y);
        }

        // Entirely behind the camera.
        if (!anyInFront)
            return false;

        const float margin = 64f;
        return maxX >= -margin && minX <= Screen.width + margin
               && maxY >= -margin && minY <= Screen.height + margin;
    }

    /// <summary>
    /// Main camera, cached. <c>Camera.main</c> is a tag search and this runs every frame.
    /// </summary>
    private static Camera? ResolveCamera()
    {
        if (_camera != null)
            return _camera;

        _camera = Camera.main;
        return _camera;
    }

    /// <inheritdoc />
    protected override void OnEnable()
    {
        base.OnEnable();
        Stats.Register(this);
    }

    /// <inheritdoc />
    protected override void OnDisable()
    {
        Stats.Unregister(this);
        base.OnDisable();
    }

    /// <summary>Frames on which a job was still running when Update came round, and how many LateUpdate then landed.</summary>
    private int _lateAtUpdate;
    private int _payloads;
    private int _structures;
    private int _rescuedLate;

    /// <summary>
    /// A second chance to land a job in the same frame, and to start the next one.
    /// </summary>
    /// <remarks>
    /// A job finishing a moment after Update used to wait a whole frame, and no new job can
    /// start while one is held, so a scene fed every frame fell to every second frame for as
    /// long as the worker ran slightly late (a garbage collection was enough). LateUpdate is
    /// still before the canvas renders, so a job landed here is also on screen a frame sooner.
    /// </remarks>
    private void LateUpdate()
    {
        if (_job == null || !_job.IsCompleted)
            return;

        _rescuedLate++;
        LandJob();
        ApplyDeferred();

        if (VectorConfig.RendererEnabled && _scene != null
            && (_needsRebuild || (_dataDirty && DueForRebuild())))
        {
            Dispatch();
        }
    }

    private void Update()
    {
        if (_job != null && !_job.IsCompleted)
            _lateAtUpdate++;

        // Land first: a finished job releases the shared state that any deferred scene or
        // data payload is waiting on, so both can happen in the same frame.
        LandJob();
        ApplyDeferred();

        // The entire per-frame cost of a static scene is this one boolean.
        // A scene with no `t` still has to redraw while data is easing to a new value.
        // A stated glide keeps its own clock, so the scene-wide window does not cover it: a
        // 0.6 s ease across a 0.5 s tick would otherwise stop redrawing four fifths of the way
        // through and arrive with a jump.
        var blending = VectorConfig.SmoothData
                       && (Now() - _dataArrived < _dataInterval || Now() < _glidesUntil);
        // A scroll-driven scene has no `t`, so without this it would be judged static and
        // freeze the moment it was first drawn. Only rebuild when the offset actually moved.
        var scrolled = _scene is { UsesScroll: true } && ScrollMoved();

        var animated = _scene != null && (_scene.UsesTime || blending || scrolled);

        // Walking toward a console changes how much detail its geometry should have. Nothing
        // else notices for a static scene, so the size bucket is what asks for the rebuild.
        if (VectorConfig.RendererEnabled && _scene != null && !animated && ScaleChanged())
            _needsRebuild = true;

        if (_imagesWaitingOn >= 0 && ImageCache.Version != _imagesWaitingOn)
        {
            _imagesWaitingOn = -1;
            _needsRebuild = true;
        }

        if (VectorConfig.RendererEnabled && _scene != null && _job == null
            && (_needsRebuild || ((animated || _dataDirty) && DueForRebuild())))
        {
            Dispatch();
        }

        Stats.ReportIfDue();
    }

    /// <summary>
    /// Applies a scene or data payload that arrived while a job held the shared state.
    /// </summary>
    /// <remarks>
    /// The sync patch runs on the main thread whenever ScriptedScreens delivers an element,
    /// which may be mid-job. Blocking it until the worker finished would hand the main
    /// thread back the exact cost this design removes, so the payload waits a frame instead.
    /// At a 0.5 s tick against a sub-frame job, that is almost never even reached.
    /// </remarks>
    private void ApplyDeferred()
    {
        if (_job != null)
            return;

        if (_pendingScene != null)
        {
            var scene = _pendingScene;
            _pendingScene = null;
            ApplyScene(scene);
        }

        if (_pendingData != null)
        {
            var data = _pendingData;
            _pendingData = null;
            ApplyData(data);
        }
    }

    /// <summary>
    /// Rate-limits rebuilds by on-screen size: temporal level of detail.
    /// </summary>
    /// <remarks>
    /// Preferred over shedding instances, which is what the first LOD attempt did. Dropping
    /// members of a field makes motes pop in and out *and* changes its apparent density, so
    /// the field visibly dims as you walk away. Updating a distant console at 10 Hz instead
    /// of 60 has neither problem: every mote is still there, in the right place, just
    /// resampled less often — and at that size the motion is a few pixels of slow drift
    /// where the difference cannot be seen.
    ///
    /// It attacks the same arithmetic. Seven distant consoles at 10 Hz cost a sixth of seven
    /// at 60 Hz, which is the multi-console problem, without a visual artefact class.
    ///
    /// Fast motion on a *large* console is unaffected: full rate is restored well before an
    /// element is big enough to read detail in.
    /// </remarks>
    /// <summary>
    /// The scene clock. Game time by default, so animation stops when the game pauses.
    /// </summary>
    /// <remarks>
    /// Originally unscaled, on the reasoning that animation should not depend on game speed.
    /// That was wrong twice over: a console visibly kept animating while everything around it
    /// was frozen, and it went on rebuilding meshes for a paused game.
    /// </remarks>
    private static float Now()
    {
        return VectorConfig.PauseWithGame ? Time.time : Time.unscaledTime;
    }

    private bool DueForRebuild()
    {
        // Paused: the clock is not advancing, so a rebuild would produce identical geometry.
        if (VectorConfig.PauseWithGame && Time.timeScale <= 0f)
            return false;

        // Off screen: Unity culls the draw, but nothing was stopping the rebuild.
        if (VectorConfig.CullOffScreen && !IsOnScreen())
            return false;

        var maximum = VectorConfig.MaximumHz;

        if (!VectorConfig.RateLodEnabled)
            return Elapsed(maximum);

        var scale = ScreenPixelsPerCanvasUnit();

        // Unknown size: assume full quality rather than minimum. This is the safe direction.
        if (scale < 0f)
            return Elapsed(maximum);

        var width = rectTransform.rect.width * scale;
        var minimum = Mathf.Min(VectorConfig.MinimumHz, maximum);

        // sqrt rather than linear: the drop-off was too steep near the threshold, so a
        // console only slightly smaller than full size lost far more rate than it lost
        // detail. This holds the rate up longer and eases into the floor.
        var t = Mathf.Sqrt(Mathf.Clamp01(width / VectorConfig.FullRatePixels));
        var hz = Mathf.Lerp(minimum, maximum, t);

        return Elapsed(hz);
    }

    private bool Elapsed(float hz)
    {
        var period = 1f / Mathf.Max(1f, hz);
        var now = Time.unscaledTime;
        var since = now - _lastRebuild;

        if (since < period)
            return false;

        // Advance by whole periods rather than snapping to now, so a rebuild that lands late
        // does not push the whole schedule late and beat against the frame rate.
        _lastRebuild = now - Mathf.Min(period * 0.5f, since - period);
        return true;
    }

    /// <summary>
    /// Builds and uploads the mesh directly, bypassing <c>VertexHelper</c>.
    /// </summary>
    /// <remarks>
    /// The base implementation routes through <c>VertexHelper</c> and <c>OnPopulateMesh</c>,
    /// which was measured at ~259 ns per vertex in game and dominated everything else.
    /// Overriding here is the supported way to supply a Graphic's mesh: masking, batching and
    /// material handling all key off <c>canvasRenderer.SetMesh</c>, which is exactly what the
    /// base does at the end of the same path.
    /// </remarks>
    protected override void UpdateGeometry()
    {
        // A capture clone has a mesh it did not make and no scene of its own. Present what it
        // was given and touch nothing: clearing here would wipe the geometry of the live
        // console it was cloned from, which is a far worse bug than a blank capture.
        if (!_ownsMesh && _mesh != null)
        {
            canvasRenderer.SetMesh(_mesh);
            return;
        }

        EnsureMesh();

        // ScriptedScreens' capture rebuilds the surface and then clones it inside a single
        // call, so there is no frame in which a worker could have produced anything. This is
        // the one hook that runs in between -- the capture calls ForceUpdateCanvases before it
        // clones -- so build here and now, and the clone inherits a populated mesh.
        if (VectorElementPatch.Capturing && _scene != null && VectorConfig.RendererEnabled)
        {
            BuildNow();
            return;
        }

        // Must not touch _builder: a worker may own it right now. The mesh is main-thread
        // only, so clearing that is safe.
        if (_scene == null || !VectorConfig.RendererEnabled)
            _mesh!.Clear();

        canvasRenderer.SetMesh(_mesh);
        _needsRebuild = true;
    }

    private void EnsureMesh()
    {
        if (_mesh != null)
            return;

        _mesh = new Mesh { name = "VectorSurface" };
        _ownsMesh = true;

        // NO 32-BIT INDEX FORMAT HERE. Setting it crashes the game natively, with nothing in
        // the log: this mesh goes to a CanvasRenderer, and UGUI's batcher assumes 16-bit
        // indices throughout. The property exists on Mesh and the enum value is in the shipped
        // build -- which is exactly the trap this project has hit before. Verifying that two
        // APIs exist is not verifying that they compose.
        _mesh.MarkDynamic();
    }

    /// <summary>
    /// Hands a rebuild to a worker thread, capturing everything it will read.
    /// </summary>
    /// <remarks>
    /// Every Unity-side input is read here, on the main thread, because none of it is legal
    /// anywhere else: rectTransform.rect, the canvas camera behind ScreenPixelsPerCanvasUnit,
    /// and Time.*. What the job then runs is pure managed arithmetic over Vector2, Matrix4x4
    /// and Color, which is what makes this safe at all — the rebuild path was checked and
    /// contains no native call. ColorUtility.TryParseHtmlString, the one that would have
    /// broken it, lives in scene and defs parsing and never runs during tessellation.
    ///
    /// The mesh upload stays on the main thread, where it belongs. It was measured at
    /// 0.12 ms against 15 ms of tessellation, so this moves ~99% of the cost off the frame.
    /// </remarks>
    private void Dispatch()
    {
        var rect = rectTransform.rect;
        if (rect.width <= 0f || rect.height <= 0f)
            return;

        EnsureMesh();

        var now = Now();
        _context.Time = now - _startTime;

        // Blend across the gap the last two payloads were actually separated by, so this
        // self-tunes to whatever tick rate the script happens to use.
        _context.Blend = VectorConfig.SmoothData
            ? Mathf.Clamp01((now - _dataArrived) / _dataInterval)
            : 1f;

        SampleScroll(rect);

        // Handed over at dispatch like everything else Unity-sourced: the worker reads this
        // and the main thread writes it, never at the same time, because only one job for a
        // surface is ever in flight.
        ApplyForcedScrolls();
        SampleGlides(Now());

        _context.ScrollOffsets.Clear();
        foreach (var pair in _offsets)
            _context.ScrollOffsets[pair.Key] = pair.Value;

        var screenScale = ScreenPixelsPerCanvasUnit();
        var known = screenScale > 0f;

        _jobScreenPixels = known ? rect.width * screenScale : -1f;
        _needsRebuild = false;
        _dataDirty = false;
        _builtForBucket = ScaleBucket();

        var scene = _scene!;
        var context = _context;
        var builder = _builder;
        var stats = _stats;
        var scale = known ? screenScale : 1f;

        _job = Task.Run(() =>
        {
            var cpuBefore = ThreadCpuMilliseconds();
            var mark = Stopwatch.GetTimestamp();

            Tessellator.Emit(builder, scene, context, rect, scale, known, stats);

            // Off-thread on purpose: this is a rect test per label per later shape, and it
            // has to happen before Slices() is asked how the mesh divides.
            if (scene.TextInOrder)
                TextOrder.Assign(stats.Text, builder);

            var wall = (Stopwatch.GetTimestamp() - mark) * 1000d / Stopwatch.Frequency;
            var cpuAfter = ThreadCpuMilliseconds();

            return (wall, cpuBefore < 0d || cpuAfter < 0d ? -1d : cpuAfter - cpuBefore);
        });
    }

    private static void CollectForcedScrolls(List<VecNode> nodes, List<VecNode> into)
    {
        foreach (var node in nodes)
        {
            if (node.Op == VecOp.Scroll && node.ScrollSetVersion != null && !string.IsNullOrEmpty(node.Id))
                into.Add(node);

            CollectForcedScrolls(node.Children, into);
        }
    }

    /// <summary>Applies a script-set scroll offset once per new `sov`; wheel and drag own it after.</summary>
    private void ApplyForcedScrolls()
    {
        if (_forcedScrollNodes.Count == 0)
            return;

        // A jump is a command, not a value to ease: read mid-blend, a new `sov` passed through
        // several fractional versions and `so` landed short of its target (149.1 for 150).
        var blend = _context.Blend;
        _context.Blend = 1f;
        foreach (var node in _forcedScrollNodes)
        {
            ForcedScroll.Apply(_offsets, _forcedApplied, node.Id!,
                node.ScrollSetVersion!.Evaluate(_context), node.ScrollSet!.Evaluate(_context));
        }

        _context.Blend = blend;
    }

    /// <summary>True when the enclosing ScrollRect has moved since the last rebuild.</summary>
    private bool ScrollMoved()
    {
        if (!_scrollSearched)
        {
            _scrollSearched = true;
            _scroll = GetComponentInParent<ScrollRect>();
        }

        if (_scroll == null)
            return false;

        var now = _scroll.verticalNormalizedPosition;
        if (!float.IsNaN(_lastScrollY) && Mathf.Abs(now - _lastScrollY) < 0.0001f)
            return false;

        _lastScrollY = now;
        return true;
    }

    /// <summary>
    /// Reads the enclosing ScrollRect into the context, in scene units.
    /// </summary>
    /// <remarks>
    /// Main thread only, hence here rather than in the evaluator: a worker may not touch a
    /// Unity object. The search is cached including its failure, since most scenes are not
    /// in a scroll view and GetComponentInParent walks the whole ancestry.
    ///
    /// Units: the element's own rect maps onto the viewbox, so scene-units-per-canvas-unit
    /// is viewbox height over rect height. A fade written at `y = "=sy"` therefore lands at
    /// the top of the viewport whatever the console's size.
    /// </remarks>
    private void SampleScroll(Rect rect)
    {
        if (!_scrollSearched)
        {
            _scrollSearched = true;
            _scroll = GetComponentInParent<ScrollRect>();
        }

        if (_scroll == null || _scene == null || rect.height <= 0f)
        {
            _context.ScrollY = 0f;
            _context.ViewportH = 0f;
            return;
        }

        var viewport = _scroll.viewport != null ? _scroll.viewport : _scroll.GetComponent<RectTransform>();
        if (viewport == null)
            return;

        var content = _scroll.content;
        var toScene = _scene.ViewHeight / rect.height;

        var viewportH = viewport.rect.height;
        var contentH = content != null ? content.rect.height : viewportH;

        // normalizedPosition is 1 at the top and 0 at the bottom; scenes are +Y down.
        var hidden = Mathf.Max(0f, contentH - viewportH);
        var offset = hidden * (1f - Mathf.Clamp01(_scroll.verticalNormalizedPosition));

        _context.ScrollY = offset * toScene;
        _context.ViewportH = viewportH * toScene;
    }

    /// <summary>Uploads a finished job's geometry and records what it cost.</summary>
    private void LandJob()
    {
        if (_job == null || !_job.IsCompleted)
            return;

        var job = _job;
        _job = null;

        if (job.IsFaulted)
        {
            // An exception on a worker is otherwise swallowed whole and the surface simply
            // stops updating — a blank console with nothing in the log to explain it.
            ScriptedScreensVectorPlugin.Log?.LogError(
                $"vector \"{_sceneId}\": tessellation failed: {job.Exception?.GetBaseException()}");
            return;
        }

        // Timed separately because they are different animals: Emit is our managed code,
        // while Apply and SetMesh are native calls that push the whole vertex buffer at the
        // engine. Lumping them together hid which one costs, and three theories were tested
        // against the combined figure before anyone thought to split it.
        var tessellateMs = job.Result.Wall;

        if (job.Result.Cpu >= 0d)
            _tessellateCpuMs += job.Result.Cpu;

        _stopwatch.Restart();

        _sliceCount = _builder.Slices();

        if (_scene != null && _scene.TextInOrder)
            TextOrder.Resolve(_stats.Text, _builder, _sliceCount);

        _builder.Apply(_mesh!);
        canvasRenderer.SetMesh(_mesh);

        // Everything past the first mesh goes to a child. A scene that fits one mesh -- which
        // is nearly all of them -- never creates any and never pays for this.
        ApplySlices();

        ApplyImages();

        _stopwatch.Stop();

        // Text is realised here, not in the job: TMP is main-thread and builds its own mesh.
        if (_stats.Text.Count > 0 || _text != null)
        {
            _text ??= new TextLayer(rectTransform);
            _text.Apply(_stats.Text);

            if (_scene != null && _scene.TextInOrder)
                OrderTextWithSlices();
        }

        _lastShapeCount = _stats.Shapes;
        _lastVertices = _builder.currentVertCount;

        for (var i = 0; i < _opMs.Length; i++)
        {
            _opMs[i] += _stats.OpMilliseconds[i];
            _opCount[i] = _stats.OpCounts[i];
        }

        _bandSampleMs += _stats.BandSampleMs;
        _bandStripMs += _stats.BandStripMs;
        _bandFeatherMs += _stats.BandFeatherMs;
        _bandQuads = _stats.BandQuads;

        _hits.Clear();
        _hits.AddRange(_stats.Hits);

        _scrolls.Clear();
        _scrolls.AddRange(_stats.Scrolls);
        ReportScrolls();

        // Only take pointer events when the scene has something that answers them, so a
        // plain decorative surface stays transparent to the pointer as it always was.
        raycastTarget = _hits.Count > 0 || _scrolls.Count > 0;

        // Started on the first rebuild, not on the first REPORT: reports only happen when
        // Diagnostics is on, and vector_stats has to give a real rate either way.
        if (_countersSince < 0f)
            _countersSince = Now();

        _rebuilds++;
        _milliseconds += tessellateMs + _stopwatch.Elapsed.TotalMilliseconds;
        _tessellateMs += tessellateMs;
        _uploadMs += _stopwatch.Elapsed.TotalMilliseconds;
        _peakVertices = Mathf.Max(_peakVertices, _builder.currentVertCount);
        _screenPixels = _jobScreenPixels;
    }

    /// <inheritdoc />
    protected override void OnDestroy()
    {
        _text?.Destroy();
        _text = null;

        if (_mesh != null && _ownsMesh)
        {
            Destroy(_mesh);
            _mesh = null;
        }

        base.OnDestroy();
    }
}
