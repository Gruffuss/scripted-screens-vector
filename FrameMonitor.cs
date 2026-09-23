using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>
/// Reports actual frame times, independent of whether any vector surface exists.
/// </summary>
/// <remarks>
/// Every other number this mod logs is its own stopwatch timing its own code. That misses
/// anything it does not wrap — GPU upload stalls, extra draw calls, garbage collection
/// triggered elsewhere — and it cannot be compared against "the console is off", because
/// with no surfaces there is nothing to report.
///
/// This runs for the whole session regardless, so the same measurement exists on both sides
/// of an A/B. Turn the console off, or set Renderer.Enabled = false, and compare.
///
/// The 1% figure matters as much as the mean: a rebuild every few frames shows up as a
/// spike, and a mean can hide a stutter that is obvious to look at.
/// </remarks>
internal sealed class FrameMonitor : MonoBehaviour
{
    private const float ReportIntervalSeconds = 5f;
    private const int Capacity = 1024;

    private readonly float[] _samples = new float[Capacity];

    private int _count;
    private float _elapsed;

    internal static void Install()
    {
        var host = new GameObject(nameof(FrameMonitor))
        {
            hideFlags = HideFlags.HideAndDontSave,
        };

        DontDestroyOnLoad(host);
        host.AddComponent<FrameMonitor>();
    }

    private void Update()
    {
        if (!VectorConfig.Diagnostics)
            return;

        var delta = Time.unscaledDeltaTime;

        if (_count < Capacity)
            _samples[_count++] = delta;

        _elapsed += delta;
        if (_elapsed < ReportIntervalSeconds || _count < 10)
            return;

        Report();

        _elapsed = 0f;
        _count = 0;
    }

    private void Report()
    {
        var total = 0f;
        var worst = 0f;

        for (var i = 0; i < _count; i++)
        {
            total += _samples[i];
            worst = Mathf.Max(worst, _samples[i]);
        }

        var mean = total / _count;

        // 99th percentile by partial selection: cheap enough at this sample count and far
        // more honest than a mean when the cost arrives as periodic spikes.
        var sorted = new float[_count];
        System.Array.Copy(_samples, sorted, _count);
        System.Array.Sort(sorted);
        var p99 = sorted[Mathf.Min(_count - 1, Mathf.FloorToInt(_count * 0.99f))];

        ScriptedScreensVectorPlugin.Log?.LogInfo(
            $"frame: {mean * 1000f:F2} ms mean ({1f / mean:F0} FPS), " +
            $"{p99 * 1000f:F2} ms p99, {worst * 1000f:F2} ms worst, " +
            $"vector {(VectorConfig.RendererEnabled ? "ON" : "OFF")}");
    }
}
