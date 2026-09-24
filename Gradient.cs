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

    /// <summary>Conic: parameter is the angle around <see cref="Start"/>, from <see cref="Angle"/>.</summary>
    internal bool Conic;

    /// <summary>Conic start angle, degrees clockwise from twelve o'clock.</summary>
    internal float Angle;

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

    /// <summary>
    /// What happens past the ends of the ramp: 0 pad (hold the end colours), 1 repeat, 2 reflect
    /// -- SVG `spreadMethod`, CSS `repeating-*-gradient` -- or 3 none, transparent past the
    /// ends, which is CSS `mask-repeat: no-repeat` on a sized gradient. Conic gradients already go all the way
    /// round and ignore it.
    /// </summary>
    internal int Spread;

    /// <summary>
    /// The parts of the declaration that were written as expressions rather than numbers.
    /// Null for a static gradient, which is every gradient that does not ask for otherwise.
    /// </summary>
    internal GradientSlots? Slots;

    /// <summary>Stop count, which drives how finely gradient-filled geometry is refined.</summary>
    internal int StopCount => Positions.Count;

    /// <summary>
    /// Re-reads the live parts of the declaration for this rebuild.
    /// </summary>
    /// <remarks>
    /// Called once per rebuild, before the tree walk, not per vertex: the resolved numbers
    /// land in the same fields a static gradient parses into, so everything downstream —
    /// sampling, banding, refinement — is unchanged and costs nothing extra.
    /// </remarks>
    /// <summary>
    /// This gradient as seen from where it is being used. A def reading `sy`/`vh` is resolved
    /// again here, inside whichever scroll container the walk is in, on a copy: the same def
    /// used in two containers would otherwise end on whichever was drawn last, and a mask or a
    /// text gradient is read again after the walk.
    /// </summary>
    /// <remarks>Allocates, but only for a def that reads the scroll position.</remarks>
    internal Gradient AtUse(EvalContext context)
    {
        if (Slots is not { UsesScroll: true })
            return this;

        var copy = new Gradient
        {
            Radial = Radial, Conic = Conic, Angle = Angle, BoundingBox = BoundingBox,
            Start = Start, End = End, Focus = Focus, Radius = Radius, Spread = Spread, Slots = Slots,
        };
        copy.Positions.AddRange(Positions);
        copy.Colours.AddRange(Colours);
        copy.Resolve(context);
        return copy;
    }

    internal void Resolve(EvalContext context)
    {
        var slots = Slots;
        if (slots == null)
            return;

        if (Conic)
        {
            Start = new Vector2(Value(slots.Cx, Start.x), Value(slots.Cy, Start.y));
            Angle = Value(slots.Angle, Angle);
        }
        else if (Radial)
        {
            Start = new Vector2(Value(slots.Cx, Start.x), Value(slots.Cy, Start.y));
            Focus = new Vector2(Value(slots.Fx, Focus.x), Value(slots.Fy, Focus.y));
            Radius = Mathf.Max(0.0001f, Value(slots.Radius, Radius));
        }
        else
        {
            Start = new Vector2(Value(slots.X1, Start.x), Value(slots.Y1, Start.y));
            End = new Vector2(Value(slots.X2, End.x), Value(slots.Y2, End.y));
        }

        var positions = slots.StopPositions;
        var colours = slots.StopColours;

        for (var i = 0; i < Positions.Count; i++)
        {
            if (positions != null && i < positions.Length && positions[i] != null)
                Positions[i] = Mathf.Clamp01(positions[i]!.Evaluate(context));

            if (colours == null || i >= colours.Length || colours[i] == null)
                continue;

            // A colour the payload never supplied reads as a fault rather than as a design
            // decision, the same choice `f = "$name"` makes on a node.
            Colours[i] = context.Colour(colours[i]!, out var supplied)
                ? supplied
                : new Color(1f, 0f, 1f, 1f);
        }

        // Live positions can cross each other between ticks, and sampling walks the stops in
        // order. Insertion sort, in place, carrying the slots with the values so the next
        // rebuild still reads each stop's own expression.
        if (positions != null)
            SortLive(slots);

        float Value(Expression? expression, float fallback)
        {
            return expression == null ? fallback : expression.Evaluate(context);
        }
    }

    private void SortLive(GradientSlots slots)
    {
        for (var i = 1; i < Positions.Count; i++)
        {
            var position = Positions[i];
            var colour = Colours[i];
            var slotPosition = Slot(slots.StopPositions, i);
            var slotColour = Slot(slots.StopColours, i);

            var j = i - 1;
            while (j >= 0 && Positions[j] > position)
            {
                Positions[j + 1] = Positions[j];
                Colours[j + 1] = Colours[j];
                Move(slots.StopPositions, j, j + 1);
                Move(slots.StopColours, j, j + 1);
                j--;
            }

            Positions[j + 1] = position;
            Colours[j + 1] = colour;
            Put(slots.StopPositions, j + 1, slotPosition);
            Put(slots.StopColours, j + 1, slotColour);
        }

        static T? Slot<T>(T?[]? array, int index) where T : class
        {
            return array != null && index < array.Length ? array[index] : null;
        }

        static void Move<T>(T?[]? array, int from, int to) where T : class
        {
            if (array != null && from < array.Length && to < array.Length)
                array[to] = array[from];
        }

        static void Put<T>(T?[]? array, int index, T? value) where T : class
        {
            if (array != null && index < array.Length)
                array[index] = value;
        }
    }

    /// <summary>Gradient parameter at a point, in 0..1 before clamping.</summary>
    internal float Parameter(Vector2 point)
    {
        if (Conic)
            return ConicParameter(point - Start);

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

    /// <summary>Conic parameter of an offset from the centre, 0..1 clockwise from <see cref="Angle"/>.</summary>
    internal float ConicParameter(Vector2 offset)
    {
        if (offset.sqrMagnitude < 1e-12f)
            return 0f;

        return Mathf.Repeat(Heading(offset) - Angle, 360f) / 360f;
    }

    /// <summary>Degrees clockwise from twelve o'clock, in scene space (+Y down).</summary>
    internal static float Heading(Vector2 offset)
    {
        return Mathf.Atan2(offset.x, -offset.y) * Mathf.Rad2Deg;
    }

    internal Color Sample(float t)
    {
        if (Spread == 3)
            return t < 0f || t > 1f ? Beyond(t) : SampleWithin(t);

        return SampleWithin(Spread == 0 ? t : Wrap(t));
    }

    /// <summary>Past the ends under `spread = none`: the nearer end colour, fully transparent.</summary>
    /// <remarks>Keeping the colour rather than going to clear black leaves no dark fringe where
    /// vertex colours blend across the edge.</remarks>
    internal Color Beyond(float t)
    {
        var colour = SampleWithin(t < 0f ? 0f : 1f);
        colour.a = 0f;
        return colour;
    }

    /// <summary>A parameter past the ramp's ends brought back into it by <see cref="Spread"/>.</summary>
    internal float Wrap(float t)
    {
        if (Spread == 1)
            return t - Mathf.Floor(t);

        var m = t - 2f * Mathf.Floor(t * 0.5f);
        return m > 1f ? 2f - m : m;
    }

    /// <summary>The ramp at a parameter, held at its end colours outside 0..1.</summary>
    internal Color SampleWithin(float t)
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
            // Unclamped under a spread: past 1 the ramp keeps changing, and a triangle spanning
            // several periods must read as spanning them.
            var t = Spread is 0 ? Mathf.Clamp01(Parameter(point)) : Parameter(point);
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

    /// <summary>The ramp's colour at a parameter already brought within 0..1, with this paint's alpha.</summary>
    internal Color32 AtParameter(float t, bool beyond = false)
    {
        var colour = beyond ? Gradient!.Beyond(t) : Gradient!.SampleWithin(t);
        colour.a *= Alpha;
        return colour;
    }

    /// <summary>Colour at a point in the shape's own coordinate space.</summary>
    internal Color32 At(Vector2 point)
    {
        // A conic's angle has to be measured in the shape's own space: through a bounding box
        // that is not square, fractions would bend every angle but the four axes.
        if (Gradient is { Conic: true } && _useBounds)
        {
            var conic = Gradient.Sample(Gradient.ConicParameter(point - FromGradientSpace(Gradient.Start)));
            conic.a *= Alpha;
            return conic;
        }

        var colour = Gradient?.At(ToGradientSpace(point)) ?? Colour;
        colour.a *= Alpha;
        return colour;
    }
}

/// <summary>
/// The live parts of a gradient declaration: geometry or stops written as expressions.
/// </summary>
/// <remarks>
/// A gradient is resolved once at parse time and stored in the scene, which is why a ramp
/// could not follow a value the way <c>f = "$name"</c> on a node can. These are the
/// declaration's expressions kept alongside the resolved numbers, re-read per rebuild by
/// <see cref="Gradient.Resolve"/>. Null members are the ones that really were numbers.
/// </remarks>
internal sealed class GradientSlots
{
    internal Expression? X1;
    internal Expression? Y1;
    internal Expression? X2;
    internal Expression? Y2;
    internal Expression? Cx;
    internal Expression? Cy;
    internal Expression? Radius;
    internal Expression? Fx;
    internal Expression? Fy;
    internal Expression? Angle;

    /// <summary>Per stop: its position as an expression, or null when it is a number.</summary>
    internal Expression?[]? StopPositions;

    /// <summary>Per stop: the name a <c>$slot</c> colour reads, or null when it is a literal.</summary>
    internal string?[]? StopColours;

    internal bool UsesTime;

    /// <summary>
    /// Reads `sy` or `vh`, which mean the scroll container around the shape using it -- so the
    /// def is resolved where it is used rather than once per rebuild. See <see cref="Gradient.AtUse"/>.
    /// </summary>
    internal bool UsesScroll;

    internal bool Any =>
        X1 != null || Y1 != null || X2 != null || Y2 != null
        || Cx != null || Cy != null || Radius != null || Fx != null || Fy != null
        || Angle != null || StopPositions != null || StopColours != null;
}

/// <summary>Parses the <c>defs</c> array: gradient and clip-path declarations.</summary>
internal static class GradientParser
{
    internal static void ReadStops(float[] flatPositions, string[] colours, Gradient into,
        Expression?[]? positionSlots = null, string?[]? colourSlots = null)
    {
        var count = Mathf.Min(flatPositions.Length, colours.Length);
        List<Expression?>? keptPositions = null;
        List<string?>? keptColours = null;

        for (var i = 0; i < count; i++)
        {
            if (!ColorUtility.TryParseHtmlString(colours[i], out var colour))
                continue;

            into.Positions.Add(Mathf.Clamp01(flatPositions[i]));
            into.Colours.Add(colour);

            // Slots travel with the stops they belong to: a dropped stop takes its slot with
            // it, so the two stay index for index however many are rejected.
            if (positionSlots != null)
                (keptPositions ??= new List<Expression?>(count)).Add(i < positionSlots.Length ? positionSlots[i] : null);

            if (colourSlots != null)
                (keptColours ??= new List<string?>(count)).Add(i < colourSlots.Length ? colourSlots[i] : null);
        }

        if (keptPositions != null || keptColours != null)
        {
            var slots = into.Slots ??= new GradientSlots();
            slots.StopPositions = keptPositions?.ToArray();
            slots.StopColours = keptColours?.ToArray();
        }

        // Sampling walks the stops in order and assumes they ascend. Live positions are
        // sorted per rebuild instead, by the resolve that knows how to carry their slots.
        for (var i = 1; into.Slots?.StopPositions == null && i < into.Positions.Count; i++)
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
