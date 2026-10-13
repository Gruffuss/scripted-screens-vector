namespace ScriptedScreensVector;

/// <summary>
/// The regression suite's renderer preset; benchmarks can select the owner's LOD settings.
/// </summary>
/// <remarks>
/// The real <c>VectorConfig</c> reads BepInEx <c>ConfigEntry</c> values out of
/// <c>ModBehaviour.Config</c>, which does not exist outside the game — so it is the one thing
/// standing between the tessellator and a headless test, the same role <c>Stubs.cs</c> plays
/// for the logger and <c>ScriptedScreensStubs.cs</c> for the host's value types.
///
/// Curve and count LOD are explicitly enabled for the existing regression corpus;
/// the game's unbound defaults for those two switches are off. Preserve this preset
/// when comparing its fingerprints. Each benchmark restores any switches it changes.
/// </remarks>
internal static class VectorConfig
{
    internal static bool Diagnostics => false;

    internal static float Dither => 1f;

    internal static float BlurDensity => 1f;

    internal static bool BlurLod { get; set; } = true;

    internal static bool PixelEdges => true;

    internal static bool CurveLodEnabled { get; set; } = true;

    internal static float PixelsPerSegment => 3f;

    internal static int MinimumSegments => 8;

    internal static bool CountLodEnabled { get; set; } = true;

    internal static float CountFloor => 0.45f;

    internal static float CountFullDetailPixels => 320f;
}
