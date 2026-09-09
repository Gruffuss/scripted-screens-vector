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
    internal readonly double[] OpMilliseconds = new double[9];

    internal readonly int[] OpCounts = new int[9];

    internal double BandSampleMs;

    internal double BandStripMs;

    internal double BandFeatherMs;

    internal int BandQuads;

    internal int Shapes;

    /// <summary>Text nodes found by the walk, for the main thread to realise as TMP.</summary>
    internal readonly System.Collections.Generic.List<TextPlacement> Text = new();
}
