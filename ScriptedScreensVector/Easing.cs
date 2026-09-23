using System;
using System.Globalization;
using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>
/// A named timing curve for a value's glide: how far along it is at a given fraction of its
/// duration.
/// </summary>
/// <remarks>
/// Written to CSS's definitions, because those are the ones authors know and the ones a page
/// translated from CSS has to reproduce. Every named curve is a cubic Bézier over the unit
/// square with its first and last control points pinned at the corners, so one solver serves
/// all of them; `steps` is the one that is not.
///
/// Evaluated once per glide per rebuild -- never per vertex -- so the Newton solve below is
/// nothing beside the geometry it feeds.
/// </remarks>
internal readonly struct Easing
{
    /// <summary>A straight line, and what a payload without a stated curve gets.</summary>
    internal static readonly Easing Linear = new(0f, 0f, 1f, 1f, 0);

    private readonly float _x1;
    private readonly float _y1;
    private readonly float _x2;
    private readonly float _y2;

    /// <summary>Step count for `steps(n)`; 0 for a Bézier.</summary>
    private readonly int _steps;

    private Easing(float x1, float y1, float x2, float y2, int steps)
    {
        _x1 = x1;
        _y1 = y1;
        _x2 = x2;
        _y2 = y2;
        _steps = steps;
    }

    /// <summary>True when this curve is the identity, so a caller can skip the solve.</summary>
    internal bool IsLinear => _steps == 0
                              && Mathf.Abs(_x1 - _y1) < 0.0001f
                              && Mathf.Abs(_x2 - _y2) < 0.0001f;

    /// <summary>
    /// Reads a CSS timing function. Anything unrecognised is reported and falls back to linear,
    /// which glides rather than jumps -- the safe direction for a typo.
    /// </summary>
    internal static Easing Parse(string? name, Action<string>? problem = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Linear;

        var text = name!.Trim();

        switch (text.ToUpperInvariant())
        {
            case "LINEAR": return Linear;
            case "EASE": return new Easing(0.25f, 0.1f, 0.25f, 1f, 0);
            case "EASE-IN": return new Easing(0.42f, 0f, 1f, 1f, 0);
            case "EASE-OUT": return new Easing(0f, 0f, 0.58f, 1f, 0);
            case "EASE-IN-OUT": return new Easing(0.42f, 0f, 0.58f, 1f, 0);
        }

        if (TryArguments(text, "cubic-bezier", out var bezier) && bezier.Length == 4)
        {
            // Only x is constrained: a curve outside 0..1 horizontally has no single answer
            // for "how far along at time p", which is why CSS says the same.
            return new Easing(Mathf.Clamp01(bezier[0]), bezier[1], Mathf.Clamp01(bezier[2]), bezier[3], 0);
        }

        if (TryArguments(text, "steps", out var steps) && steps.Length >= 1)
        {
            var count = Mathf.Max(1, Mathf.RoundToInt(steps[0]));
            return new Easing(0f, 0f, 1f, 1f, count);
        }

        problem?.Invoke($"unknown easing \"{text}\"; using linear");
        return Linear;
    }

    /// <summary>How far along the value is at <paramref name="progress"/> of its duration.</summary>
    internal float Evaluate(float progress)
    {
        var p = Mathf.Clamp01(progress);

        // CSS `steps(n)` defaults to jump-end: the value holds, then jumps, and reaches its
        // destination only at the very end.
        if (_steps > 0)
            return Mathf.Clamp01(Mathf.Floor(p * _steps) / _steps);

        if (IsLinear)
            return p;

        return Bezier(SolveX(p));
    }

    /// <summary>The parameter whose x equals <paramref name="x"/>, by Newton with a bisection guard.</summary>
    /// <remarks>
    /// Newton alone diverges where the curve is nearly flat in x -- `cubic-bezier(0,0,1,1)` with
    /// extreme handles is exactly that -- so a failed step falls back to bisection, which cannot.
    /// </remarks>
    private float SolveX(float x)
    {
        var t = x;

        for (var i = 0; i < 8; i++)
        {
            var error = CurveX(t) - x;
            if (Mathf.Abs(error) < 0.0001f)
                return t;

            var slope = SlopeX(t);
            if (Mathf.Abs(slope) < 0.000001f)
                break;

            t -= error / slope;
        }

        float low = 0f, high = 1f;
        t = x;

        for (var i = 0; i < 20; i++)
        {
            var at = CurveX(t);
            if (Mathf.Abs(at - x) < 0.0001f)
                return t;

            if (at < x)
                low = t;
            else
                high = t;

            t = (low + high) * 0.5f;
        }

        return t;
    }

    private float CurveX(float t) => Cubic(t, _x1, _x2);

    private float Bezier(float t) => Cubic(t, _y1, _y2);

    private static float Cubic(float t, float a, float b)
    {
        var inverse = 1f - t;
        return 3f * inverse * inverse * t * a + 3f * inverse * t * t * b + t * t * t;
    }

    private float SlopeX(float t)
    {
        var inverse = 1f - t;
        return 3f * inverse * inverse * _x1
               + 6f * inverse * t * (_x2 - _x1)
               + 3f * t * t * (1f - _x2);
    }

    /// <summary>Reads `name(a, b, ...)` into its numbers, or reports that it is not that.</summary>
    private static bool TryArguments(string text, string function, out float[] values)
    {
        values = Array.Empty<float>();

        if (!text.StartsWith(function, StringComparison.OrdinalIgnoreCase))
            return false;

        var open = text.IndexOf('(', StringComparison.Ordinal);
        var close = text.LastIndexOf(')');
        if (open < 0 || close < open)
            return false;

        var parts = text[(open + 1)..close].Split(',');
        var parsed = new float[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            // A trailing word is `steps(4, end)`, which is the default and the only form served.
            if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed[i]))
            {
                if (i == 0)
                    return false;

                System.Array.Resize(ref parsed, i);
                break;
            }
        }

        values = parsed;
        return values.Length > 0;
    }
}
