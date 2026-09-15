using System.Collections.Generic;

namespace ScriptedScreensVector;

/// <summary>
/// Test stand-in for <c>ImageCache</c>, which downloads with UnityWebRequest and so needs the
/// player. The tessellator only asks it whether a source is loaded and how big it is.
/// </summary>
internal static class ImageCache
{
    internal static readonly Dictionary<string, (int W, int H, string? Error)> Loaded = new();

    internal static bool TryGet(string src, out int width, out int height, out string? error)
    {
        width = 0;
        height = 0;
        error = null;

        if (!Loaded.TryGetValue(src, out var entry))
            return false;

        if (entry.Error != null)
        {
            error = entry.Error;
            return false;
        }

        width = entry.W;
        height = entry.H;
        return true;
    }
}
