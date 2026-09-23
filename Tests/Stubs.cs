using System.Collections.Generic;

namespace ScriptedScreensVector;

/// <summary>
/// Stand-in for the BepInEx log sink so production geometry code compiles outside the game.
/// </summary>
/// <remarks>
/// Shaped to match what <c>Triangulator</c> calls: <c>Plugin.Log?.LogWarning(string)</c>.
/// The real <see cref="ScriptedScreensVectorPlugin"/> file is simply not compiled into this
/// assembly, so there is no conflict and production code needs no test hook.
///
/// Captured rather than discarded: the triangulator warns when clipping stalls, and a test
/// asserting that a shape triangulated cleanly should also be able to assert that nothing
/// complained.
/// </remarks>
internal sealed class StubLog
{
    internal List<string> Warnings { get; } = new();

    internal void LogWarning(string message)
    {
        Warnings.Add(message);
    }

    internal void LogInfo(string message)
    {
    }

    internal void LogError(string message)
    {
        Warnings.Add("ERROR: " + message);
    }
}

internal static class ScriptedScreensVectorPlugin
{
    internal static StubLog? Log { get; } = new();
}
