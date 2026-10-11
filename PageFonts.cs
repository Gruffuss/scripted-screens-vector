using System;
using System.Collections.Generic;

namespace ScriptedScreensVector;

/// <summary>Page font decisions, free of Unity. The bridge owns one instance on the main thread.</summary>
internal sealed class PageFonts
{
    internal sealed class Request
    {
        internal string State = "pending";
        internal string? Reason;
        internal string[] Faces = Array.Empty<string>();

        internal string Describe() => State == "loaded"
            ? $"loaded: {string.Join(", ", Faces)}"
            : State + (string.IsNullOrEmpty(Reason) ? "" : $": {Reason}");
    }

    private readonly Dictionary<string, Request> _links = new(StringComparer.Ordinal);

    internal Request? Get(string link) => _links.TryGetValue(link, out var request) ? request : null;

    internal bool Begin(string link)
    {
        if (Get(link) is { State: "pending" or "loaded" })
            return false;

        _links[link] = new Request();
        return true;
    }

    internal void Refuse(string link, string reason) =>
        _links[link] = new Request { State = "refused", Reason = reason };

    internal void Complete(string link, string[] faces, string reason) =>
        _links[link] = faces.Length > 0
            ? new Request { State = "loaded", Faces = faces }
            : new Request { State = reason == "disabled" ? "disabled" : "download-failed", Reason = reason };

    internal bool Settled(IReadOnlyList<string> links)
    {
        if (links.Count == 0)
            return false;

        foreach (var link in links)
            if (Get(link) is null or { State: "pending" })
                return false;

        return true;
    }

    internal string? Mismatch(IReadOnlyList<string> links, string? family)
    {
        if (string.IsNullOrEmpty(family) || !Settled(links))
            return null;

        var delivered = new List<string>();
        foreach (var link in links)
        {
            foreach (var face in Get(link)!.Faces)
            {
                if (string.Equals(face, family, StringComparison.Ordinal))
                    return null;
                if (!delivered.Contains(face))
                    delivered.Add(face);
            }
        }

        return $"font \"{family}\" did not arrive; this page's fonts delivered: "
               + (delivered.Count == 0 ? "none" : string.Join(", ", delivered));
    }

    internal static bool FaceResolved(string? requested, string? assigned) =>
        string.IsNullOrEmpty(requested) || string.Equals(requested, assigned, StringComparison.Ordinal);
}
