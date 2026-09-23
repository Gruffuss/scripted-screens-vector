namespace ScriptedScreensVector;

/// <summary>
/// The renderer's config, as it behaves with nothing bound.
/// </summary>
/// <remarks>
/// The real <c>VectorConfig</c> reads BepInEx <c>ConfigEntry</c> values out of
/// <c>ModBehaviour.Config</c>, which does not exist outside the game — so it is the one thing
/// standing between the tessellator and a headless test, the same role <c>Stubs.cs</c> plays
/// for the logger and <c>ScriptedScreensStubs.cs</c> for the host's value types.
///
/// **These are not invented numbers.** Every real property reads
/// <c>_entry?.Value ?? &lt;default&gt;</c>, so an unbound config returns exactly what is
/// below — which is also what the game uses until someone edits the .cfg. Copied from
/// VectorConfig.cs; if a default changes there and not here, a test measuring segment counts
/// will disagree with the game and this file is the first place to look.
/// </remarks>
internal static class VectorConfig
{
    internal static bool CurveLodEnabled => true;

    internal static float PixelsPerSegment => 3f;

    internal static int MinimumSegments => 8;

    internal static bool CountLodEnabled => true;

    internal static float CountFloor => 0.45f;

    internal static float CountFullDetailPixels => 320f;
}
