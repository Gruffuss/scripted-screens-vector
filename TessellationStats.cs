namespace ScriptedScreensVector;

/// <summary>
/// Per-surface tessellation diagnostics, filled on the thread that did the work.
/// </summary>
/// <remarks>
/// Exists because tessellation moved off the main thread. The counters inside
/// <see cref="Tessellator"/> are [ThreadStatic] so concurrent surfaces cannot corrupt each
/// other's numbers, which also means the main thread can no longer read them directly. One
/// of these travels with each job and is read after it lands.
/// </remarks>
internal sealed class TessellationStats
{
    internal readonly double[] OpMilliseconds = new double[12];

    internal readonly int[] OpCounts = new int[12];

    internal double BandSampleMs;

    internal double BandStripMs;

    internal double BandFeatherMs;

    internal int BandQuads;

    internal int Shapes;

    /// <summary>Text nodes found by the walk, for the main thread to realise as TMP.</summary>
    internal readonly System.Collections.Generic.List<TextPlacement> Text = new();

    /// <summary>Clickable node bounds, in draw order, for hit testing on the main thread.</summary>
    internal readonly System.Collections.Generic.List<HitRegion> Hits = new();

    /// <summary>Scroll containers the walk found, for wheel and drag to hit-test against.</summary>
    internal readonly System.Collections.Generic.List<ScrollRegion> Scrolls = new();

    /// <summary>What ran out of vertex budget this rebuild, or null. Per REBUILD.</summary>
    /// <remarks>
    /// Not a scene problem: a scene is only too large at the size it is currently drawn, so
    /// filing it with the parse-time faults would burn the marker on permanently after one
    /// close pass.
    /// </remarks>
    internal string? Starved;

    /// <summary>`IMG` nodes: drawn ones with their shape index, and sources still to load (index -1).</summary>
    internal readonly System.Collections.Generic.List<ImagePlacement> Images = new();

    /// <summary>Data names the scene asked for and did not get, this rebuild.</summary>
    internal readonly System.Collections.Generic.List<string> Missing = new();
}
