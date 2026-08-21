using BepInEx.Configuration;

namespace ScriptedScreensVector;

/// <summary>
/// Tunables for the two level-of-detail mechanisms, in
/// <c>BepInEx/config/ScriptedScreensVector.cfg</c>.
/// </summary>
/// <remarks>
/// Exists because the right curve is a judgement call that can only be made by looking at a
/// console while walking around it, and rebuilding the mod for each guess is a slow way to
/// find out. Every value is read live, so editing the file and reloading a scene is enough.
///
/// Both mechanisms can be switched off entirely. Off means full quality and full cost, which
/// is the correct behaviour for anyone who would rather spend the frames.
///
/// **Binds into <c>ModBehaviour.Config</c>, not a `ConfigFile` of its own.** LaunchPad's
/// `StationeersModsEntrypoint.Configs()` reports exactly that instance to its settings UI, so
/// a privately constructed `ConfigFile` writes a perfectly good .cfg that LaunchPad never
/// shows — which is what happened first time round.
/// </remarks>
internal static class VectorConfig
{
    private static ConfigFile? _file;

    private static ConfigEntry<bool>? _rendererEnabled;
    private static ConfigEntry<bool>? _diagnostics;
    private static ConfigEntry<bool>? _smoothData;
    private static ConfigEntry<bool>? _cullOffScreen;
    private static ConfigEntry<bool>? _pauseWithGame;
    private static ConfigEntry<bool>? _rateLodEnabled;
    private static ConfigEntry<float>? _maximumHz;
    private static ConfigEntry<float>? _minimumHz;
    private static ConfigEntry<float>? _fullRatePixels;
    private static ConfigEntry<bool>? _countLodEnabled;
    private static ConfigEntry<float>? _countFloor;
    private static ConfigEntry<float>? _countFullDetailPixels;

    /// <summary>
    /// Master switch. False draws nothing while leaving everything else in place, which is
    /// what makes a like-for-like A/B against "the console is off" possible.
    /// </summary>
    internal static bool RendererEnabled => _rendererEnabled?.Value ?? true;

    /// <summary>
    /// Performance logging. Off by default — it is a development tool, and a line every five
    /// seconds is noise in an ordinary session. The instrumentation stays compiled in so it
    /// can be switched back on without a rebuild.
    /// </summary>
    internal static bool Diagnostics => _diagnostics?.Value ?? false;

    /// <summary>Interpolate `$name` values between data payloads.</summary>
    internal static bool SmoothData => _smoothData?.Value ?? true;

    /// <summary>Stop rebuilding scenes that are off screen or behind the camera.</summary>
    internal static bool CullOffScreen => _cullOffScreen?.Value ?? true;

    /// <summary>Freeze animation with the game rather than running on unscaled time.</summary>
    internal static bool PauseWithGame => _pauseWithGame?.Value ?? true;

    /// <summary>Rate LOD: reduce rebuild frequency for elements drawn small.</summary>
    internal static bool RateLodEnabled => _rateLodEnabled?.Value ?? true;

    /// <summary>Rebuild ceiling regardless of size. Rendering is unaffected and stays per-frame.</summary>
    internal static float MaximumHz => _maximumHz?.Value ?? 30f;

    /// <summary>Floor for the smallest elements. Below ~12 Hz motion starts to read as choppy.</summary>
    internal static float MinimumHz => _minimumHz?.Value ?? 15f;

    /// <summary>On-screen width at which the full rate is restored.</summary>
    internal static float FullRatePixels => _fullRatePixels?.Value ?? 220f;

    /// <summary>Count LOD: shed instances from large repeats. Opt-in per node via <c>lod = 1</c>.</summary>
    private static ConfigEntry<bool>? _curveLodEnabled;
    private static ConfigEntry<float>? _pixelsPerSegment;
    private static ConfigEntry<int>? _minimumSegments;

    internal static bool CurveLodEnabled => _curveLodEnabled?.Value ?? true;

    internal static float PixelsPerSegment => _pixelsPerSegment?.Value ?? 3f;

    internal static int MinimumSegments => _minimumSegments?.Value ?? 8;

    internal static bool CountLodEnabled => _countLodEnabled?.Value ?? true;

    /// <summary>Smallest fraction of instances ever drawn. Higher keeps a field looking full.</summary>
    internal static float CountFloor => _countFloor?.Value ?? 0.45f;

    /// <summary>On-screen width at which every instance is drawn.</summary>
    internal static float CountFullDetailPixels => _countFullDetailPixels?.Value ?? 320f;

    internal static void Load(ConfigFile file)
    {
        if (_file != null || file == null)
            return;

        _file = file;

        _rendererEnabled = _file.Bind(
            "Renderer", "Enabled", true,
            "Draw vector elements. Turn off to measure what the renderer actually costs: " +
            "the scene, the elements and the mod all stay loaded, only the geometry stops. " +
            "Compare the 'frame:' lines in the log with it on and off.");

        _diagnostics = _file.Bind(
            "Diagnostics", "Enabled", false,
            "Log rebuild cost and frame times every 5 seconds. Development tool; leave off " +
            "for normal play. Lines look like: 'vector \"scene\": 28 Hz, 4.31 ms/rebuild " +
            "(tessellate 4.26 + upload 0.05), 12.1% of a core, ...' and 'frame: 19.02 ms " +
            "mean (53 FPS), 23.20 ms p99, ...'");

        _smoothData = _file.Bind(
            "Renderer", "SmoothData", true,
            "Ease $name values between data payloads instead of snapping. Data arrives about " +
            "twice a second while the scene draws at display rate, so without this a gauge " +
            "needle steps visibly while t-driven motion beside it stays smooth.");

        _cullOffScreen = _file.Bind(
            "Renderer", "CullOffScreen", true,
            "Stop rebuilding a scene when it is off screen or behind the camera. Unity culls " +
            "the drawing either way, but the mesh was still being rebuilt every tick for " +
            "consoles nobody could see.");

        _pauseWithGame = _file.Bind(
            "Renderer", "PauseWithGame", true,
            "Freeze animation while the game is paused. Off uses unscaled time, so scenes " +
            "keep moving (and keep costing CPU) while everything else is stopped.");

        _rateLodEnabled = _file.Bind(
            "Rate LOD", "Enabled", true,
            "Rebuild animated scenes less often when they are drawn small. Every shape still " +
            "draws in the right place; only the update rate falls, which is invisible at " +
            "small sizes. Off = always rebuild at MaximumHz.");

        _maximumHz = _file.Bind(
            "Rate LOD", "MaximumHz", 30f,
            "Ceiling on mesh rebuilds per second, independent of frame rate. Rendering still " +
            "happens every frame. 30 is ample for drift, ripple and sweeping needles.");

        _minimumHz = _file.Bind(
            "Rate LOD", "MinimumHz", 15f,
            "Floor for the smallest elements. Below about 12 the motion starts to read as " +
            "choppy rather than merely less detailed.");

        _fullRatePixels = _file.Bind(
            "Rate LOD", "FullRatePixels", 220f,
            "On-screen width, in pixels, at which MaximumHz is restored. Lower means the " +
            "full rate is kept further away.");

        _curveLodEnabled = _file.Bind(
            "Curve LOD", "Enabled", true,
            "Sample YS and LS curves more coarsely when they are drawn small. These join " +
            "their samples with straight lines, so the count decides whether a wave reads " +
            "as a curve or a polygon -- and the right count depends on on-screen size, " +
            "which the author cannot know. Unlike Count LOD nothing is dropped: the same " +
            "curve is walked with a longer step, so this is safe to leave on.");

        _pixelsPerSegment = _file.Bind(
            "Curve LOD", "PixelsPerSegment", 3f,
            "Target on-screen length of one segment, in pixels. Lower is smoother and " +
            "costs more. Below about 2 the facets are already invisible.");

        _minimumSegments = _file.Bind(
            "Curve LOD", "MinimumSegments", 8,
            "Floor on samples for a curve drawn very small, so a wave never collapses to a " +
            "couple of straight lines. Never exceeds the authored count.");

        _countLodEnabled = _file.Bind(
            "Count LOD", "Enabled", true,
            "Allow repeats that opted in with lod = 1 to shed instances when drawn small. " +
            "Saves more than rate LOD but instances visibly pop in and out. Off = never shed.");

        _countFloor = _file.Bind(
            "Count LOD", "MinimumFraction", 0.45f,
            "Smallest fraction of instances ever drawn. Raise it if fields look sparse at a " +
            "distance; the saving falls accordingly.");

        _countFullDetailPixels = _file.Bind(
            "Count LOD", "FullDetailPixels", 320f,
            "On-screen width at which every instance is drawn.");
    }
}
