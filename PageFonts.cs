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
        internal double FailedAt;

        internal string Describe() => State == "loaded"
            ? $"loaded: {string.Join(", ", Faces)}"
            : State + (string.IsNullOrEmpty(Reason) ? "" : $": {Reason}");
    }

    // Deliberate retry interval: scene re-pushes must not download a failed link every tick.
    private const double RetrySeconds = 60;
    private const int LinkLimit = 48;
    private readonly HashSet<string> _requestedLinks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Request> _links = new(StringComparer.Ordinal);

    internal Request? Get(string link) => _links.TryGetValue(link, out var request) ? request : null;

    internal bool Begin(string link, double now)
    {
        var previous = Get(link);
        if (previous != null && (previous.State is "pending" or "loaded" || now - previous.FailedAt < RetrySeconds))
            return false;

        if (!_requestedLinks.Contains(link) && _requestedLinks.Count >= LinkLimit)
        {
            Refuse(link, "page font limit reached (48 links per session)", now);
            return false;
        }

        _requestedLinks.Add(link);
        _links[link] = new Request();
        return true;
    }

    internal void Refuse(string link, string reason, double now) =>
        _links[link] = new Request { State = "refused", Reason = reason, FailedAt = now };

    internal void Complete(string link, string[] faces, string reason, double now) =>
        _links[link] = faces.Length > 0
            ? new Request { State = "loaded", Faces = faces }
            : new Request { State = "failed", Reason = reason, FailedAt = now };

    internal bool Settled(IReadOnlyList<string> links)
    {
        if (links.Count == 0)
            return false;

        foreach (var link in links)
            if (Get(link) is null or { State: "pending" })
                return false;

        return true;
    }

    internal string? Mismatch(IReadOnlyList<string> links, string? family, bool notRegistered)
    {
        if (!notRegistered || string.IsNullOrEmpty(family) || !Settled(links))
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
}
