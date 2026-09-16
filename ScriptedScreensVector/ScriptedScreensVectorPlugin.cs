using BepInEx.Logging;
using HarmonyLib;
using StationeersMods.Interface;
using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>
/// LaunchPad entrypoint. Client-side only: the vector layer is a rendering concern and a
/// headless server draws nothing.
/// </summary>
[StationeersMod(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION_CONST)]
public sealed class ScriptedScreensVectorPlugin : ModBehaviour
{
    private static bool _initialized;

    /// <summary>Held for the process lifetime so the patch is never collected or unpatched.</summary>
    private static Harmony? _harmony;

    internal static ManualLogSource? Log { get; private set; }

    /// <inheritdoc />
    public override void OnLoaded(ContentHandler contentHandler)
    {
        base.OnLoaded(contentHandler);

        if (_initialized)
            return;

        _initialized = true;

        try
        {
            Log = new ManualLogSource(PluginInfo.PLUGIN_NAME);
            try
            {
                BepInEx.Logging.Logger.Sources.Add(Log);
            }
            catch
            {
                // Logger already disposed or source name taken; logging is non-essential.
            }

            if (Application.isBatchMode)
            {
                Log.LogInfo("Headless server detected, skipping vector renderer.");
                return;
            }

            // base.OnLoaded created Config; LaunchPad surfaces that instance in its
            // settings UI, so binding here is what makes the tunables appear in game.
            VectorConfig.Load(Config);
            VectorStatsTool.TryRegister();
            Log.LogInfo(Config != null
                ? "Level-of-detail settings registered with LaunchPad."
                : "No ConfigFile from LaunchPad; level-of-detail settings use defaults.");

            // Resolving the patch target throws if ScriptedScreens is absent or its
            // internals moved. About.xml declares the dependency so LaunchPad loads it
            // first, but a failure here must not take the rest of the game down.
            FrameMonitor.Install();

            // Diagnostics shows scroll reports, so the event can be checked without a subscriber mod.
            VectorGraphic.ScrollChanged += (host, id, offset, max, view) =>
            {
                if (VectorConfig.Diagnostics)
                    Log.LogInfo($"scroll {host.name}/{id}: {offset:F1} of {max:F1}, view {view:F1}");
            };

            _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            // The ASSEMBLY, not one type. PatchAll(Type) registers exactly that class, so a
            // second patch -- the capture hook lives in a nested one -- compiles in and is
            // then silently never applied. No error, no warning: the flag it sets simply
            // stays false for ever, and everything downstream looks like it is not working.
            _harmony.PatchAll(typeof(VectorElementPatch).Assembly);
            Log.LogInfo($"Patched ScriptedScreens; element type \"{VectorElementPatch.ElementType}\" is live.");
        }
        catch (System.Exception ex)
        {
            Log?.LogError($"Vector layer failed to install, element type unavailable: {ex}");
            Debug.LogError(ex);
        }
    }
}
