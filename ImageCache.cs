using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.Networking;

namespace ScriptedScreensVector;

/// <summary>
/// Textures for `IMG` nodes, fetched once per source and shared by every surface.
/// </summary>
/// <remarks>
/// Requests start on the main thread when a rebuild reports a source it could not draw; the
/// tessellator itself only asks <see cref="TryGet"/>, which is safe from a worker. Nothing is
/// drawn for an image until its texture is here, and <see cref="Version"/> moving on is what
/// tells a surface to rebuild.
///
/// ponytail: textures live for the session; evict by last use if many large images appear.
/// </remarks>
internal static class ImageCache
{
    private sealed class Entry
    {
        internal Texture2D? Texture;
        internal volatile bool Ready;
        internal volatile bool Failed;
        internal string? Error;
        internal int Width;
        internal int Height;
    }

    private static readonly ConcurrentDictionary<string, Entry> Entries = new();

    private static int _version;

    /// <summary>Moves on whenever a request finishes, loaded or failed.</summary>
    internal static int Version => _version;

    /// <summary>Worker-safe: the loaded size, or false while pending, failed or never asked.</summary>
    internal static bool TryGet(string src, out int width, out int height, out string? error)
    {
        width = 0;
        height = 0;
        error = null;

        if (!Entries.TryGetValue(src, out var entry))
            return false;

        if (entry.Failed)
        {
            error = entry.Error ?? "failed to load";
            return false;
        }

        if (!entry.Ready)
            return false;

        width = entry.Width;
        height = entry.Height;
        return true;
    }

    /// <summary>Main thread: the texture, or null while it is not loaded.</summary>
    internal static Texture2D? Texture(string src)
    {
        return Entries.TryGetValue(src, out var entry) && entry.Ready ? entry.Texture : null;
    }

    /// <summary>Key prefix for a picture sampled point-filtered (`smp = point`).</summary>
    /// <remarks>
    /// Filtering belongs to the texture, and the texture is shared by every console showing that
    /// source, so a pixelated picture is a texture of its own: fetched once more and cached under
    /// this key, leaving the smooth one as it was.
    /// </remarks>
    internal const string PointPrefix = "point:";

    /// <summary>Main thread: starts fetching a source, once.</summary>
    internal static void Request(string src)
    {
        var entry = new Entry();
        if (!Entries.TryAdd(src, entry))
            return;

        var point = src.StartsWith(PointPrefix, System.StringComparison.Ordinal);
        var url = point ? src.Substring(PointPrefix.Length) : src;

        UnityWebRequest request;
        try
        {
            request = UnityWebRequestTexture.GetTexture(new System.Uri(url), nonReadable: true);
        }
        catch (System.Exception ex)
        {
            Fail(entry, src, ex.Message);
            return;
        }

        request.SendWebRequest().completed += _ =>
        {
            try
            {
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Fail(entry, src, request.error);
                    return;
                }

                var texture = DownloadHandlerTexture.GetContent(request);
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = point ? FilterMode.Point : FilterMode.Bilinear;

                entry.Texture = texture;
                entry.Width = texture.width;
                entry.Height = texture.height;
                entry.Ready = true;
                System.Threading.Interlocked.Increment(ref _version);
            }
            catch (System.Exception ex)
            {
                Fail(entry, src, ex.Message);
            }
            finally
            {
                request.Dispose();
            }
        };
    }

    private static void Fail(Entry entry, string src, string? error)
    {
        entry.Error = string.IsNullOrEmpty(error) ? "failed to load" : error;
        entry.Failed = true;
        System.Threading.Interlocked.Increment(ref _version);
        ScriptedScreensVectorPlugin.Log?.LogWarning($"vector: image \"{src}\" failed: {entry.Error}");
    }
}
