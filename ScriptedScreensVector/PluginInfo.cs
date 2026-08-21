namespace ScriptedScreensVector;

/// <summary>
/// Mod identity for LaunchPad.
/// </summary>
/// <remarks>
/// <see cref="PLUGIN_VERSION_CONST"/> is generated at compile time from the csproj
/// <c>Version</c> property (see the <c>GenerateVersionConst</c> target). It is emitted into
/// <c>$(RootNamespace)</c>, so this namespace and <c>ModName</c> must stay in step.
/// </remarks>
internal static partial class PluginInfo
{
    /// <summary>Stable mod identity (Harmony ID, Workshop metadata, etc.).</summary>
    internal const string PLUGIN_GUID = "zedle.stationeers.scriptedscreens.vector";

    /// <summary>Human-readable mod name for logs and the mod list.</summary>
    internal const string PLUGIN_NAME = "ScriptedScreens Vector";

    internal static readonly string PLUGIN_VERSION =
        typeof(PluginInfo).Assembly.GetName().Version?.ToString() ?? PLUGIN_VERSION_CONST;
}
