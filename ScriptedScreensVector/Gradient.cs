using System;
using System.Collections.Generic;
using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>
/// A gradient definition, sampled per vertex at tessellation time.
/// </summary>
/// <remarks>
/// Baked into vertex colours rather than driven by a shader, which is what lets a gradient
/// batch with the rest of the ScriptedScreens UI instead of forcing its own material.
///
/// The accuracy consequence is worth stating plainly. Vertex colours interpolate linearly
/// across a triangle, so a **two-stop linear** gradient is exact — its colour really is an
/// affine function of position. A multi-stop linear gradient is piecewise affine and a
/// radial gradient is not affine at all, so both need geometry fine enough to follow the
/// curve. <see cref="Tessellator"/> subdivides for that; see its gradient refinement pass.
/// </remarks>
internal sealed class Gradient
{
    internal bool Radial;

    /// <summary>
    /// Coordinates are fractions of the filled shape's bounding box rather than scene units.
    /// </summary>
    /// <remarks>
    /// The fix for gradients being unusable in live UI. Declared coordinates are static
    /// floats, so a ramp is anchored in *space* and cannot follow a tank level or a bar that
    /// moves — the exact case a gas UI is made of. In bounding-box mode the ramp spans
    /// whatever the shape happens to be at that instant, so it tracks the geometry for free
    /// and costs nothing per frame: no expression evaluation, just a rect the tessellator
    /// already has to compute.
    ///
    /// SVG calls this `objectBoundingBox`, and 0..1 means the same thing here.
    /// </remarks>
    internal bool BoundingBox;

    // Linear: axis endpoints. Radial: centre, radius, and focus.
    internal Vector2 Start;
    internal Vector2 End;
    internal Vector2 Focus;
    internal float Radius = 1f;

    internal readonly List<float> Positions = new();
    internal readonly List<Color> Colours = new();

    /// <summary>Stop count, which drives how finely gradient-filled geometry is refined.</summary>
    internal int StopCount => Positions.Count;

    /// <summary>Gradient parameter at a point, in 0..1 before clamping.</summary>
    internal float Parameter(Vector2 point)
    {
        if (!Radial)
        {
            var axis = End - Start;
            var lengthSquared = axis.sqrMagnitude;
            if (lengthSquared < 0.000001f)
                return 0f;

            return Vector2.Dot(point - Start, axis) / lengthSquared;
        }

        if (Radius < 0.000001f)
            return 0f;

        return Vector2.Distance(point, Focus) / Radius;
    }

    internal Color Sample(float t)
    {
        if (Positions.Count == 0)
            return Color.white;

        t = Mathf.Clamp01(t);

        if (t <= Positions[0])
            return Colours[0];

        for (var i = 1; i < Positions.Count; i++)
        {
            if (t > Positions[i])
                continue;

            var span = Positions[i] - Positions[i - 1];
            var local = span < 0.000001f ? 0f : (t - Positions[i - 1]) / span;
            return Color.Lerp(Colours[i - 1], Colours[i], local);
        }

        return Colours[^1];
    }

    internal Color At(Vector2 point)
    {
        return Sample(Parameter(point));
    }

    /// <summary>
    /// How much of the gradient a triangle covers, used to decide whether it must be
    /// subdivided before its corner colours can represent it.
    /// </summary>
    /// <remarks>
    /// Samples the centroid and edge midpoints as well as the corners, and that is the
    /// entire point. Corner-only spread is blind to the case that matters: ear clipping
    /// produces slivers spanning a shape, and on a radial gradient both ends of such a
    /// sliver sit on the rim at nearly the same parameter while its middle passes straight
    /// through the focus. Corner spread is then ~0, nothing subdivides, and the fill renders
    /// as flat facets radiating from the centre.
    /// </remarks>
    internal float SpreadOver(Vector2 a, Vector2 b, Vector2 c)
    {
        var lowest = float.MaxValue;
        var highest = float.MinValue;

        Sample(a);
        Sample(b);
        Sample(c);
        Sample((a + b + c) / 3f);
        Sample((a + b) * 0.5f);
        Sample((b + c) * 0.5f);
        Sample((c + a) * 0.5f);

        return highest - lowest;

        void Sample(Vector2 point)
        {
            var t = Mathf.Clamp01(Parameter(point));
            lowest = Mathf.Min(lowest, t);
            highest = Mathf.Max(highest, t);
        }
    }
}

/// <summary>
/// What a fill or stroke paints with: a flat colour, or a gradient looked up per vertex.
/// </summary>
internal readonly struct Paint
{
    internal readonly Color Colour;
    internal readonly Gradient? Gradient;
    internal readonly float Alpha;

    private readonly Vector2 _boundsMin;
    private readonly Vector2 _boundsSize;
    private readonly bool _useBounds;

    internal Paint(Color colour, Gradient? gradient, float alpha)
    {
        Colour = colour;
        Gradient = gradient;
        Alpha = alpha;
        _boundsMin = Vector2.zero;
        _boundsSize = Vector2.one;
        _useBounds = false;
    }

    private Paint(Color colour, Gradient? gradient, float alpha, Vector2 min, Vector2 size)
    {
        Colour = colour;
        Gradient = gradient;
        Alpha = alpha;
        _boundsMin = min;
        _boundsSize = size;
        _useBounds = true;
    }

    internal bool IsGradient => Gradient != null;

    /// <summary>
    /// Binds a bounding-box gradient to the shape actually being filled.
    /// </summary>
    internal Paint WithBounds(Vector2 min, Vector2 size)
    {
        if (Gradient is not { BoundingBox: true })
            return this;

        // A zero-extent axis would divide by zero; 1 leaves that axis unmapped, which is the
        // sane reading for a shape with no height or no width.
        var safe = new Vector2(
            Mathf.Abs(size.x) < 0.0001f ? 1f : size.x,
            Mathf.Abs(size.y) < 0.0001f ? 1f : size.y);

        return new Paint(Colour, Gradient, Alpha, min, safe);
    }

    /// <summary>Maps a point into the space the gradient's coordinates are expressed in.</summary>
    internal Vector2 ToGradientSpace(Vector2 point)
    {
        return _useBounds
            ? new Vector2((point.x - _boundsMin.x) / _boundsSize.x, (point.y - _boundsMin.y) / _boundsSize.y)
            : point;
    }

    /// <summary>The inverse, for geometry that has to be built around the gradient's focus.</summary>
    internal Vector2 FromGradientSpace(Vector2 point)
    {
        return _useBounds
            ? new Vector2(_boundsMin.x + point.x * _boundsSize.x, _boundsMin.y + point.y * _boundsSize.y)
            : point;
    }

    /// <summary>Colour at a point in the shape's own coordinate space.</summary>
    internal Color32 At(Vector2 point)
    {
        var colour = Gradient?.At(ToGradientSpace(point)) ?? Colour;
        colour.a *= Alpha;
        return colour;
    }
}

/// <summary>Parses the <c>defs</c> array: gradient and clip-path declarations.</summary>
internal static class GradientParser
{
    internal static void ReadStops(float[] flatPositions, string[] colours, Gradient into)
    {
        var count = Mathf.Min(flatPositions.Length, colours.Length);

        for (var i = 0; i < count; i++)
        {
            if (!ColorUtility.TryParseHtmlString(colours[i], out var colour))
                continue;

            into.Positions.Add(Mathf.Clamp01(flatPositions[i]));
            into.Colours.Add(colour);
        }

        // Sampling walks the stops in order and assumes they ascend.
        for (var i = 1; i < into.Positions.Count; i++)
        {
            if (into.Positions[i] >= into.Positions[i - 1])
                continue;

            ScriptedScreensVectorPlugin.Log?.LogWarning("gradient stops are not in ascending order; sorting");
            SortStops(into);
            break;
        }

        if (into.Positions.Count != 0)
            return;

        // A gradient with no usable stops would silently paint white; say so instead.
        ScriptedScreensVectorPlugin.Log?.LogWarning("gradient has no valid stops");
    }

    /// <summary>Internal so tests can exercise it without Unity's native colour parser.</summary>
    internal static void SortStops(Gradient gradient)
    {
        var order = new int[gradient.Positions.Count];
        for (var i = 0; i < order.Length; i++)
            order[i] = i;

        Array.Sort(order, (a, b) => gradient.Positions[a].CompareTo(gradient.Positions[b]));

        var positions = new List<float>(gradient.Positions);
        var colours = new List<Color>(gradient.Colours);

        gradient.Positions.Clear();
        gradient.Colours.Clear();

        foreach (var index in order)
        {
            gradient.Positions.Add(positions[index]);
            gradient.Colours.Add(colours[index]);
        }
    }
}
