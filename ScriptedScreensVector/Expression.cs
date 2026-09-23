using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>
/// Values an expression can see while it is being evaluated.
/// </summary>
/// <remarks>
/// Repeat indices are a small stack rather than a dictionary: <c>i</c> is the innermost,
/// <c>i1</c> the one outside it, and so on, which is how nested repeats shadow.
/// </remarks>
internal sealed class EvalContext
{
    private readonly List<float> _indices = new(4);
    private readonly List<float> _counts = new(4);

    /// <summary>Seconds since the scene was first shown.</summary>
    internal float Time { get; set; }

    /// <summary>Scroll offset of the enclosing ScrollRect, in scene units. 0 when none.</summary>
    /// <remarks>
    /// Sampled on the main thread at dispatch, like the rect and the camera scale, because
    /// tessellation runs on a worker and no Unity object may be touched there.
    /// </remarks>
    internal float ScrollY { get; set; }

    /// <summary>Viewport height of the enclosing ScrollRect, in scene units. 0 when none.</summary>
    internal float ViewportH { get; set; }

    /// <summary>Where each `SC` container is scrolled to, in scene units, by node id.</summary>
    /// <remarks>
    /// Written by the main thread at dispatch and only read on the worker, the same contract
    /// as everything else Unity-sourced in here. Inside a container, `sy` and `vh` report that
    /// container rather than the enclosing ScrollRect.
    /// </remarks>
    internal Dictionary<string, float> ScrollOffsets { get; } = new(StringComparer.Ordinal);

    /// <summary>Named values from the paired data element. Never null.</summary>
    internal Dictionary<string, float> Scalars { get; } = new(StringComparer.Ordinal);

    /// <summary>The previous payload's values, for interpolation.</summary>
    internal Dictionary<string, float> Previous { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// How far between the previous payload and the current one, 0..1.
    /// </summary>
    /// <remarks>
    /// Data arrives about twice a second while the scene draws at display rate, so a value
    /// read straight from the payload steps visibly — a gauge needle jumps in quarter-second
    /// increments next to motes drifting smoothly. Blending across the gap costs nothing and
    /// removes the difference.
    /// </remarks>
    internal float Blend { get; set; } = 1f;

    /// <summary>
    /// How far along each name's own glide is, 0..1, for the names that have one.
    /// </summary>
    /// <remarks>
    /// Written on the main thread at dispatch, like everything else clock-derived, and only
    /// read on the worker. Empty unless a payload asked for per-name timing.
    /// </remarks>
    internal Dictionary<string, float> NameBlend { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Timing a payload asked for, by name: how long that value's glide should take, and the
    /// curve it follows.
    /// </summary>
    /// <remarks>
    /// Carried on the payload rather than on the scene because it describes a CHANGE, not a
    /// value -- the same bar may glide over 0.6 s when a reading moves it and snap when the
    /// console switches mode. A name no payload mentions glides over the measured gap to the
    /// next payload, linearly, exactly as before.
    /// </remarks>
    internal Dictionary<string, (float Seconds, Easing Curve, float Delay)> Eased { get; } = new(StringComparer.Ordinal);

    /// <summary>Named arrays from the paired data element. Never null.</summary>
    internal Dictionary<string, float[]> Arrays { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The previous payload's arrays, blended the same way scalars are.
    /// </summary>
    /// <remarks>
    /// Initially left unsmoothed on the theory that stepped history "reads as correct" for a
    /// chart. It does not — a line that jumps twice a second beside smooth motion looks
    /// broken. And the case for smoothing is stronger here than for scalars: a history chart
    /// shifts each sample one slot per tick, so blending <c>old[i]</c> toward <c>new[i]</c>
    /// (which holds what used to be at <c>i+1</c>) produces an actual smooth scroll.
    /// </remarks>
    internal Dictionary<string, float[]> PreviousArrays { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Named colours from the data payload, referenced in paint as <c>$name</c>.
    /// </summary>
    /// <remarks>
    /// Spec §8: this is how state-driven recolouring — an alarm going red — happens without
    /// resending structure. Colours are not expression values, so they live beside the
    /// numeric bindings rather than inside them.
    /// </remarks>
    internal Dictionary<string, Color> Colours { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Raw string values from the payload, for <c>text = "$name"</c>.
    /// </summary>
    /// <remarks>
    /// Every string is stored here, including those that also parse as a colour: "#FF0000"
    /// is a legitimate thing to want to display. The two dictionaries overlap on purpose.
    /// </remarks>
    internal Dictionary<string, string> Strings { get; } = new(StringComparer.Ordinal);

    /// <summary>Arrays of strings, for `text = "$rows[i]"` inside a repeat.</summary>
    /// <remarks>
    /// Separate from <see cref="Arrays"/> rather than a variant of it, because the numeric
    /// arrays are interpolated between payloads and these cannot be: there is no halfway
    /// point between two strings, and a chart that scrolls smoothly and a label that reads
    /// half of one word are different requirements.
    /// </remarks>
    internal Dictionary<string, string[]> StringArrays { get; } = new(StringComparer.Ordinal);

    /// <summary>Arrays of colours, for `f = "$cols[i]"` inside a repeat.</summary>
    internal Dictionary<string, Color[]> ColourArrays { get; } = new(StringComparer.Ordinal);

    /// <summary>`keep = 1` on the data element: names this payload omits keep their values.</summary>
    /// <remarks>
    /// Off by default, so nothing that works today changes. It exists because strings cannot
    /// be interpolated and so are not blended, which meant every string a scene showed had to
    /// be resent every tick or it vanished -- a real console was shipping about a hundred
    /// strings a tick for that reason alone.
    ///
    /// Opt-in rather than the new default: with merging always on there is no way to clear a
    /// value, and the missing-name diagnostic would go quiet for any name ever sent once.
    /// </remarks>
    internal bool KeepUnmentioned { get; set; }

    /// <summary>
    /// Numeric names that apply without easing: `snap = 1` on the payload that carried them.
    /// </summary>
    /// <remarks>
    /// Per name rather than per payload, so two parked payloads merge correctly -- one eased,
    /// one snapped -- and so a snapped name never drags the rest of the scene's easing with it.
    /// For a page a value moves only when a transition says so, and the constants inside a
    /// running tween must not glide.
    /// </remarks>
    internal HashSet<string> Snapped { get; } = new(StringComparer.Ordinal);

    private readonly List<string> _staleScratch = new();

    /// <summary>
    /// Makes <see cref="Previous"/> what is on screen now, at <paramref name="blend"/>, before a
    /// new payload replaces the targets.
    /// </summary>
    /// <remarks>
    /// It used to be the old TARGETS, so a payload arriving before the last blend finished made
    /// every still-moving value jump to its end first. Invisible while payloads came at one
    /// steady rate -- the blend window is that rate -- but not once patches arrive in between.
    /// Names no longer in <see cref="Scalars"/> are dropped, so one that returns later snaps in
    /// as a new name always has.
    /// </remarks>
    internal void Rebase(float blend)
    {
        _staleScratch.Clear();
        foreach (var name in Previous.Keys)
        {
            if (!Scalars.ContainsKey(name))
                _staleScratch.Add(name);
        }

        foreach (var name in _staleScratch)
            Previous.Remove(name);

        foreach (var pair in Scalars)
        {
            // Each name rebases from what IT is showing. A name half way through a 0.6 s glide
            // of its own is not half way through the scene-wide one, and taking the scene's
            // figure would start the next glide from a place nothing ever drew.
            var own = NameBlend.Count > 0 && NameBlend.TryGetValue(pair.Key, out var named) ? named : blend;

            Previous[pair.Key] = own < 1f && Previous.TryGetValue(pair.Key, out var showing)
                ? showing + (pair.Value - showing) * own
                : pair.Value;
        }
    }

    /// <summary>
    /// Folds a later payload into this one, as if both had arrived: a patch (`keep = 1`) adds
    /// to what is here, anything else replaces it.
    /// </summary>
    /// <remarks>
    /// Needed wherever a payload waits -- behind a running rebuild, or before its scene exists.
    /// Keeping only the newest lost every value an earlier patch had carried.
    /// </remarks>
    /// <summary>
    /// Drops every name <paramref name="source"/> carries, from every kind, before its values are
    /// merged in. Without it a `keep` payload that sent a name as a number after it had been a
    /// string went on showing the string, since a string binding is looked up first.
    /// </summary>
    internal void Evict(EvalContext source)
    {
        EvictKeys(source.Scalars.Keys);
        EvictKeys(source.Arrays.Keys);
        EvictKeys(source.Colours.Keys);
        EvictKeys(source.Strings.Keys);
        EvictKeys(source.StringArrays.Keys);
        EvictKeys(source.ColourArrays.Keys);
    }

    private void EvictKeys<T>(Dictionary<string, T>.KeyCollection names)
    {
        foreach (var name in names)
        {
            Scalars.Remove(name);
            Arrays.Remove(name);
            Colours.Remove(name);
            Strings.Remove(name);
            StringArrays.Remove(name);
            ColourArrays.Remove(name);
        }
    }

    internal void MergeFrom(EvalContext later)
    {
        if (later.KeepUnmentioned)
            Evict(later);

        if (!later.KeepUnmentioned)
        {
            Scalars.Clear();
            Arrays.Clear();
            Colours.Clear();
            Strings.Clear();
            StringArrays.Clear();
            ColourArrays.Clear();
            Snapped.Clear();
            Eased.Clear();
            KeepUnmentioned = false;
        }

        foreach (var pair in later.Scalars)
        {
            Scalars[pair.Key] = pair.Value;
            if (later.Snapped.Contains(pair.Key)) Snapped.Add(pair.Key); else Snapped.Remove(pair.Key);
            Retime(later, pair.Key);
        }

        foreach (var pair in later.Arrays)
        {
            Arrays[pair.Key] = pair.Value;
            if (later.Snapped.Contains(pair.Key)) Snapped.Add(pair.Key); else Snapped.Remove(pair.Key);
            Retime(later, pair.Key);
        }

        foreach (var pair in later.Colours)
            Colours[pair.Key] = pair.Value;
        foreach (var pair in later.Strings)
            Strings[pair.Key] = pair.Value;
        foreach (var pair in later.StringArrays)
            StringArrays[pair.Key] = pair.Value;
        foreach (var pair in later.ColourArrays)
            ColourArrays[pair.Key] = pair.Value;
    }

    /// <summary>
    /// A name's timing follows the latest payload that carried the name, the way its snap does:
    /// a later payload that restates the value without timing means "glide it as usual", not
    /// "keep whatever the earlier one asked for".
    /// </summary>
    private void Retime(EvalContext later, string name)
    {
        if (later.Eased.TryGetValue(name, out var timing))
            Eased[name] = timing;
        else
            Eased.Remove(name);
    }

    /// <summary>
    /// Data names a scene asked for and did not get, gathered during a rebuild.
    /// </summary>
    /// <remarks>
    /// A missing value is not an error the parser can see -- the name is well formed and the
    /// payload simply lacks it, which happens on the first tick of every console and is only
    /// a fault if it persists. Collected rather than logged so it is reported once per
    /// rebuild instead of once per node per frame.
    /// </remarks>
    internal HashSet<string> Missing { get; } = new(StringComparer.Ordinal);

    internal void PushRepeat(float index, float count)
    {
        _indices.Add(index);
        _counts.Add(count);
    }

    internal void PopRepeat()
    {
        _indices.RemoveAt(_indices.Count - 1);
        _counts.RemoveAt(_counts.Count - 1);
    }

    /// <summary>How many repeats enclose the node being walked. 0 outside any.</summary>
    internal int RepeatDepth => _indices.Count;

    /// <summary>Repeat index <paramref name="depth"/> levels out; 0 is innermost.</summary>
    internal float Index(int depth)
    {
        var at = _indices.Count - 1 - depth;
        return at >= 0 ? _indices[at] : 0f;
    }

    internal float Count()
    {
        return _counts.Count > 0 ? _counts[^1] : 0f;
    }

    internal float Scalar(string name)
    {
        if (!Scalars.TryGetValue(name, out var value))
        {
            Missing.Add(name);
            return 0f;
        }

        var blend = BlendFor(name);
        if (blend >= 1f || !Previous.TryGetValue(name, out var previous))
            return value;

        return previous + (value - previous) * blend;
    }

    /// <summary>
    /// How far along a name's glide is: its own, when the payload gave it one, else the
    /// scene-wide blend across the measured payload gap.
    /// </summary>
    /// <remarks>
    /// The dictionary is empty for every scene that never asks for per-name timing, and the
    /// count test keeps that case at one integer compare -- this is read once per data
    /// reference per rebuild, which on a dense scene is thousands of calls.
    /// </remarks>
    internal float BlendFor(string name)
    {
        if (NameBlend.Count > 0 && NameBlend.TryGetValue(name, out var own))
            return own;

        return Blend;
    }

    /// <summary>One string from a data array, or null when there is nothing there.</summary>
    internal string? StringElement(string name, float index)
    {
        if (!StringArrays.TryGetValue(name, out var array) || array == null)
        {
            Missing.Add(name);
            return null;
        }

        var at = Mathf.RoundToInt(index);
        return at >= 0 && at < array.Length ? array[at] : null;
    }

    /// <summary>One colour from a data array. False when the name or the slot is absent.</summary>
    internal bool ColourElement(string name, float index, out Color colour)
    {
        colour = default;

        if (!ColourArrays.TryGetValue(name, out var array) || array == null)
        {
            Missing.Add(name);
            return false;
        }

        var at = Mathf.RoundToInt(index);
        if (at < 0 || at >= array.Length)
            return false;

        colour = array[at];
        return true;
    }

    internal float Element(string name, float index)
    {
        if (!Arrays.TryGetValue(name, out var array) || array == null)
            return 0f;

        var at = Mathf.RoundToInt(index);

        // Spec: out-of-range access yields 0 rather than failing.
        if (at < 0 || at >= array.Length)
            return 0f;

        var current = array[at];
        var blend = BlendFor(name);

        if (blend >= 1f
            || !PreviousArrays.TryGetValue(name, out var previous)
            || previous == null
            || at >= previous.Length)
        {
            return current;
        }

        return previous[at] + (current - previous[at]) * blend;
    }
}

/// <summary>
/// A compiled numeric attribute: either a constant, or an expression tree over
/// <c>t</c>, repeat indices, and data references.
/// </summary>
/// <remarks>
/// Parsed once when the scene structure arrives, then evaluated per frame. The important
/// property is <see cref="UsesTime"/>: a node whose attributes never mention <c>t</c> is
/// evaluated once and cached, so a static scene costs nothing per frame.
///
/// This is a tree walk rather than the stack program the spec describes. Same semantics,
/// and the tree is what a stack program would be compiled from; revisit if profiling says
/// the dispatch matters.
/// </remarks>
internal sealed class Expression
{
    private enum Kind
    {
        Constant,
        Time,
        ScrollY,
        ViewportH,
        RepeatIndex,
        RepeatCount,
        Scalar,
        Element,
        Negate,
        Add,
        Subtract,
        Multiply,
        Divide,
        Modulo,
        Power,
        Call,
    }

    private static readonly Dictionary<string, int> Arity = new(StringComparer.Ordinal)
    {
        ["sin"] = 1, ["cos"] = 1, ["tan"] = 1, ["abs"] = 1, ["sign"] = 1, ["sqrt"] = 1,
        ["floor"] = 1, ["ceil"] = 1, ["round"] = 1, ["saw"] = 1, ["tri"] = 1,
        ["not"] = 1, ["hash"] = 1,
        ["atan2"] = 2, ["min"] = 2, ["max"] = 2, ["mod"] = 2, ["pulse"] = 2,
        ["step"] = 2, ["eq"] = 2, ["lt"] = 2, ["gt"] = 2, ["lte"] = 2, ["gte"] = 2,
        ["and"] = 2, ["or"] = 2, ["hash2"] = 2,
        ["clamp"] = 3, ["lerp"] = 3, ["smoothstep"] = 3, ["if"] = 3,
        ["pi"] = 0, ["tau"] = 0,
    };

    private Kind _kind;
    private float _value;
    private string _name = string.Empty;
    private int _depth;
    private Expression?[] _args = System.Array.Empty<Expression>();

    /// <summary>True when this expression references <c>t</c> anywhere.</summary>
    internal bool UsesTime { get; private set; }

    /// <summary>True when this expression reads <c>sy</c> or <c>vh</c>.</summary>
    /// <remarks>
    /// Tracked separately from <see cref="UsesTime"/> because rebuilds are gated on time: a
    /// scene whose fades follow the scroll but never mention <c>t</c> would otherwise be
    /// treated as static and freeze in place the moment it was first drawn.
    /// </remarks>
    internal bool UsesScroll { get; private set; }

    /// <summary>True when nothing but a literal number is involved.</summary>
    internal bool IsConstant => _kind == Kind.Constant;

    internal static Expression Constant(float value)
    {
        return new Expression { _kind = Kind.Constant, _value = value };
    }

    /// <summary>
    /// Parses an attribute. Numbers become constants; strings starting with <c>=</c> are
    /// expressions. Anything unparseable falls back to <paramref name="fallback"/> so a
    /// typo degrades one attribute instead of killing the scene.
    /// </summary>
    /// <summary>
    /// Where a malformed expression is reported, set by the parser for the scene it is
    /// building. Static because Parse is, and thread-static because scenes may parse
    /// concurrently.
    /// </summary>
    [ThreadStatic] internal static System.Action<string>? Report;

    internal static Expression Parse(string source, float fallback)
    {
        try
        {
            var parser = new Parser(source);
            var expression = parser.ParseExpression();
            parser.ExpectEnd();
            return expression;
        }
        catch (FormatException ex)
        {
            Report?.Invoke($"expression \"{source}\": {ex.Message}");
            ScriptedScreensVectorPlugin.Log?.LogWarning($"expression \"{source}\": {ex.Message}");
            return Constant(fallback);
        }
    }

    internal float Evaluate(EvalContext context)
    {
        switch (_kind)
        {
            case Kind.Constant: return _value;
            case Kind.Time: return context.Time;
            case Kind.ScrollY: return context.ScrollY;
            case Kind.ViewportH: return context.ViewportH;
            case Kind.RepeatIndex: return context.Index(_depth);
            case Kind.RepeatCount: return context.Count();
            case Kind.Scalar: return context.Scalar(_name);
            case Kind.Element: return context.Element(_name, Arg(0, context));

            case Kind.Negate: return -Arg(0, context);
            case Kind.Add: return Arg(0, context) + Arg(1, context);
            case Kind.Subtract: return Arg(0, context) - Arg(1, context);
            case Kind.Multiply: return Arg(0, context) * Arg(1, context);
            case Kind.Divide: return Divide(Arg(0, context), Arg(1, context));
            case Kind.Modulo: return Divide(Arg(0, context) % Arg(1, context), 1f);
            case Kind.Power: return Mathf.Pow(Arg(0, context), Arg(1, context));
            case Kind.Call: return Invoke(context);

            default: return 0f;
        }
    }

    private float Arg(int index, EvalContext context)
    {
        var argument = _args[index];
        return argument?.Evaluate(context) ?? 0f;
    }

    // Division by zero yields 0 rather than an infinity that would poison vertex
    // positions and produce an invisible or corrupt mesh.
    private static float Divide(float a, float b)
    {
        return Mathf.Approximately(b, 0f) ? 0f : a / b;
    }

    private float Invoke(EvalContext context)
    {
        switch (_name)
        {
            case "pi": return Mathf.PI;
            case "tau": return Mathf.PI * 2f;

            case "sin": return Mathf.Sin(Arg(0, context));
            case "cos": return Mathf.Cos(Arg(0, context));
            case "tan": return Mathf.Tan(Arg(0, context));
            case "abs": return Mathf.Abs(Arg(0, context));
            case "sign": return Mathf.Sign(Arg(0, context));
            case "sqrt": return Mathf.Sqrt(Mathf.Max(0f, Arg(0, context)));
            case "floor": return Mathf.Floor(Arg(0, context));
            case "ceil": return Mathf.Ceil(Arg(0, context));
            case "round": return Mathf.Round(Arg(0, context));
            case "not": return Mathf.Approximately(Arg(0, context), 0f) ? 1f : 0f;
            case "hash": return Hash(Arg(0, context));

            case "saw": return Fract(Arg(0, context));
            case "tri": return 1f - Mathf.Abs(Fract(Arg(0, context)) * 2f - 1f);

            case "atan2": return Mathf.Atan2(Arg(0, context), Arg(1, context));
            case "min": return Mathf.Min(Arg(0, context), Arg(1, context));
            case "max": return Mathf.Max(Arg(0, context), Arg(1, context));
            case "hash2": return Hash(Arg(0, context) * 37.19f + Arg(1, context) * 91.73f);

            // Spec: always positive, unlike the % operator.
            case "mod":
            {
                var b = Arg(1, context);
                if (Mathf.Approximately(b, 0f))
                    return 0f;

                var r = Arg(0, context) % b;
                return r < 0f ? r + Mathf.Abs(b) : r;
            }

            case "pulse": return Fract(Arg(0, context)) < Arg(1, context) ? 1f : 0f;
            case "step": return Arg(1, context) >= Arg(0, context) ? 1f : 0f;

            case "eq": return Mathf.Approximately(Arg(0, context), Arg(1, context)) ? 1f : 0f;
            case "lt": return Arg(0, context) < Arg(1, context) ? 1f : 0f;
            case "gt": return Arg(0, context) > Arg(1, context) ? 1f : 0f;
            case "lte": return Arg(0, context) <= Arg(1, context) ? 1f : 0f;
            case "gte": return Arg(0, context) >= Arg(1, context) ? 1f : 0f;

            case "and": return NonZero(Arg(0, context)) && NonZero(Arg(1, context)) ? 1f : 0f;
            case "or": return NonZero(Arg(0, context)) || NonZero(Arg(1, context)) ? 1f : 0f;

            case "clamp": return Mathf.Clamp(Arg(0, context), Arg(1, context), Arg(2, context));
            case "lerp": return Mathf.LerpUnclamped(Arg(0, context), Arg(1, context), Arg(2, context));
            case "smoothstep": return Smoothstep(Arg(0, context), Arg(1, context), Arg(2, context));
            case "if": return NonZero(Arg(0, context)) ? Arg(1, context) : Arg(2, context);

            default: return 0f;
        }
    }

    private static bool NonZero(float value)
    {
        return !Mathf.Approximately(value, 0f);
    }

    private static float Fract(float value)
    {
        return value - Mathf.Floor(value);
    }

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        if (Mathf.Approximately(edge0, edge1))
            return x >= edge1 ? 1f : 0f;

        var t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// Deterministic pseudo-random in 0..1.
    /// </summary>
    /// <remarks>
    /// Integer avalanche over a fixed-point input, deliberately not
    /// <c>fract(sin(x) * large)</c>: transcendentals are not guaranteed bit-identical
    /// across platforms, and this value must agree on every client or a particle field
    /// would look different to each player. Nothing about it is transmitted.
    /// </remarks>
    private static float Hash(float value)
    {
        unchecked
        {
            var h = (uint)Mathf.RoundToInt(value * 4096f);
            h ^= 2747636419u;
            h *= 2654435769u;
            h ^= h >> 16;
            h *= 2654435769u;
            h ^= h >> 16;
            h *= 2654435769u;
            return h / (float)uint.MaxValue;
        }
    }

    private sealed class Parser
    {
        private readonly string _text;
        private int _at;

        internal Parser(string source)
        {
            // A leading '=' marks an expression attribute; tolerate its absence.
            _text = source.StartsWith('=') ? source[1..] : source;
        }

        internal Expression ParseExpression()
        {
            var left = ParseTerm();

            while (true)
            {
                SkipSpace();
                if (Match('+')) left = Binary(Kind.Add, left, ParseTerm());
                else if (Match('-')) left = Binary(Kind.Subtract, left, ParseTerm());
                else return left;
            }
        }

        private Expression ParseTerm()
        {
            var left = ParseUnary();

            while (true)
            {
                SkipSpace();
                if (Match('*')) left = Binary(Kind.Multiply, left, ParseUnary());
                else if (Match('/')) left = Binary(Kind.Divide, left, ParseUnary());
                else if (Match('%')) left = Binary(Kind.Modulo, left, ParseUnary());
                else return left;
            }
        }

        private Expression ParseUnary()
        {
            SkipSpace();
            if (Match('-'))
            {
                var operand = ParseUnary();
                var node = new Expression { _kind = Kind.Negate, _args = new Expression?[] { operand } };
                node.UsesTime = operand.UsesTime;
                node.UsesScroll = operand.UsesScroll;
                return node;
            }

            return ParsePower();
        }

        // Right-associative, so 2^3^2 is 2^(3^2).
        private Expression ParsePower()
        {
            var left = ParsePrimary();
            SkipSpace();
            return Match('^') ? Binary(Kind.Power, left, ParseUnary()) : left;
        }

        private Expression ParsePrimary()
        {
            SkipSpace();

            if (_at >= _text.Length)
                throw new FormatException("unexpected end");

            if (Match('('))
            {
                var inner = ParseExpression();
                SkipSpace();
                if (!Match(')'))
                    throw new FormatException("expected ')'");

                return inner;
            }

            if (Match('$'))
                return ParseDataReference();

            var c = _text[_at];
            if (char.IsDigit(c) || c == '.')
                return Constant(ReadNumber());

            if (char.IsLetter(c) || c == '_')
                return ParseIdentifier();

            throw new FormatException($"unexpected '{c}'");
        }

        private Expression ParseDataReference()
        {
            var name = ReadName();
            SkipSpace();

            if (!Match('['))
                return new Expression { _kind = Kind.Scalar, _name = name };

            var index = ParseExpression();
            SkipSpace();
            if (!Match(']'))
                throw new FormatException("expected ']'");

            return new Expression
            {
                _kind = Kind.Element,
                _name = name,
                _args = new Expression?[] { index },
                UsesTime = index.UsesTime,
                UsesScroll = index.UsesScroll,
            };
        }

        private Expression ParseIdentifier()
        {
            var name = ReadName();
            SkipSpace();

            if (!Match('('))
                return Variable(name);

            if (!Arity.TryGetValue(name, out var expected))
                throw new FormatException($"unknown function '{name}'");

            var args = new List<Expression>(expected);
            SkipSpace();

            if (!Match(')'))
            {
                while (true)
                {
                    args.Add(ParseExpression());
                    SkipSpace();
                    if (Match(',')) continue;
                    if (Match(')')) break;
                    throw new FormatException($"expected ',' or ')' in {name}(...)");
                }
            }

            if (args.Count != expected)
                throw new FormatException($"{name}() takes {expected} argument(s), got {args.Count}");

            var node = new Expression
            {
                _kind = Kind.Call,
                _name = name,
                _args = args.ToArray(),
            };

            foreach (var argument in args)
            {
                node.UsesTime |= argument.UsesTime;
                node.UsesScroll |= argument.UsesScroll;
            }

            return node;
        }

        private static Expression Variable(string name)
        {
            if (name == "t")
                return new Expression { _kind = Kind.Time, UsesTime = true };

            if (name == "sy")
                return new Expression { _kind = Kind.ScrollY, UsesScroll = true };

            if (name == "vh")
                return new Expression { _kind = Kind.ViewportH, UsesScroll = true };

            if (name == "n")
                return new Expression { _kind = Kind.RepeatCount };

            if (name == "i")
                return new Expression { _kind = Kind.RepeatIndex, _depth = 0 };

            // i1, i2, ... name enclosing repeats outward.
            if (name.Length > 1 && name[0] == 'i' &&
                int.TryParse(name[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var depth))
            {
                return new Expression { _kind = Kind.RepeatIndex, _depth = depth };
            }

            throw new FormatException($"unknown variable '{name}'");
        }

        private static Expression Binary(Kind kind, Expression left, Expression right)
        {
            return new Expression
            {
                _kind = kind,
                _args = new Expression?[] { left, right },
                UsesTime = left.UsesTime || right.UsesTime,
                UsesScroll = left.UsesScroll || right.UsesScroll,
            };
        }

        internal void ExpectEnd()
        {
            SkipSpace();
            if (_at < _text.Length)
                throw new FormatException($"trailing input at '{_text[_at]}'");
        }

        private string ReadName()
        {
            var start = _at;
            while (_at < _text.Length && (char.IsLetterOrDigit(_text[_at]) || _text[_at] == '_'))
                _at++;

            if (_at == start)
                throw new FormatException("expected a name");

            return _text[start.._at];
        }

        private float ReadNumber()
        {
            var start = _at;
            while (_at < _text.Length && (char.IsDigit(_text[_at]) || _text[_at] == '.'))
                _at++;

            var span = _text[start.._at];
            if (!float.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw new FormatException($"bad number '{span}'");

            return value;
        }

        private void SkipSpace()
        {
            while (_at < _text.Length && char.IsWhiteSpace(_text[_at]))
                _at++;
        }

        private bool Match(char c)
        {
            if (_at >= _text.Length || _text[_at] != c)
                return false;

            _at++;
            return true;
        }
    }
}
