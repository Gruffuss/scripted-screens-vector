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
    private readonly List<bool> _samples = new(4);

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
    /// What an eased colour showed when its new value arrived; the lookup glides from it to
    /// <see cref="Colours"/> over the name's glide. Only names given an `ease` entry are here, so a
    /// colour without one still changes at once, as it always did.
    /// </summary>
    internal Dictionary<string, Color> PreviousColours { get; } = new(StringComparer.Ordinal);

    /// <summary>When each data name last arrived on this client, in <see cref="Clock"/> seconds.</summary>
    internal Dictionary<string, float> ArrivedAt { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The renderer's clock at this rebuild, in absolute seconds. `t` is this minus the scene's
    /// start and restarts with every structure; `since()` needs a clock that does not.
    /// </summary>
    internal float Clock { get; set; }

    /// <summary>The id of the nearest node with an `id` around what is being evaluated, set by the walk.</summary>
    internal string? ScopeId { get; set; }

    /// <summary>The ids from the scene root down to the clickable node under the pointer, or null.</summary>
    internal string[]? HoverScope { get; set; }

    /// <summary>That node's repeat index, or -1 outside a repeat.</summary>
    internal int HoverIndex { get; set; } = -1;

    /// <summary>The same for the clickable node a pointer is held down on.</summary>
    internal string[]? DownScope { get; set; }

    internal int DownIndex { get; set; } = -1;

    /// <summary>
    /// `hover` / `down`: 1 when the pointer is over (held on) a clickable node inside the nearest
    /// node with an `id` around this expression -- the node itself or an enclosing group, so a
    /// button's label can answer its button. Inside a repeat, the instance must match too.
    /// </summary>
    internal float Pointer(bool held)
    {
        var scope = held ? DownScope : HoverScope;
        var index = held ? DownIndex : HoverIndex;
        if (scope == null || ScopeId == null || System.Array.IndexOf(scope, ScopeId) < 0)
            return 0f;

        var instance = InstanceIndex;
        return index < 0 || instance < 0 || instance == index ? 1f : 0f;
    }

    /// <summary>`since($name)`: seconds since `name` last arrived, or a long time for one never sent.</summary>
    internal float Since(string name)
    {
        return ArrivedAt.TryGetValue(name, out var at) ? Mathf.Max(0f, Clock - at) : 1e6f;
    }

    /// <summary>A data colour, glided from its previous value when its payload eased it.</summary>
    internal bool Colour(string name, out Color colour)
    {
        if (!Colours.TryGetValue(name, out colour))
            return false;

        if (PreviousColours.Count > 0 && PreviousColours.TryGetValue(name, out var previous))
            colour = Color.Lerp(previous, colour, BlendFor(name));

        return true;
    }

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

    /// <summary>
    /// Pushes a repeat frame. <paramref name="sample"/> marks a `YS`/`LS` sample frame: `i`
    /// and `n` read it like any other, but it names a point along one shape rather than an
    /// instance of one, so it is not what identifies a hit region or a hovered node.
    /// </summary>
    internal void PushRepeat(float index, float count, bool sample = false)
    {
        _indices.Add(index);
        _counts.Add(count);
        _samples.Add(sample);
    }

    internal void PopRepeat()
    {
        _indices.RemoveAt(_indices.Count - 1);
        _counts.RemoveAt(_counts.Count - 1);
        _samples.RemoveAt(_samples.Count - 1);
    }

    /// <summary>
    /// The innermost REPEAT index, skipping sample frames; -1 outside any repeat. This is what
    /// identifies one instance of a repeated node, so it is what a hit region records and what
    /// `hover` and `down` compare against. Reading the innermost frame instead told a sampled
    /// shape that it was instance 0 of itself.
    /// </summary>
    internal int InstanceIndex
    {
        get
        {
            for (var at = _indices.Count - 1; at >= 0; at--)
            {
                if (!_samples[at])
                    return Mathf.RoundToInt(_indices[at]);
            }

            return -1;
        }
    }

    /// <summary>
    /// Empties the repeat stack, for the start of a build. A repeat left on it from a build
    /// that threw would make `i` and `n` wrong on every later rebuild of that graphic, with
    /// nothing reported -- the kind of fault that reads as a scene bug for weeks.
    /// </summary>
    internal void ResetRepeats()
    {
        _indices.Clear();
        _counts.Clear();
        _samples.Clear();
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
        {
            // Recorded like every other absent name. This was the one accessor that stayed
            // quiet, so `$arr[i]` in an EXPRESSION read 0 with nothing listed while the same
            // reference in a label reported. An out-of-range index is still silent here, as it
            // is for a colour array and for a string array read through StringElement: the
            // array is there, the slot is not. A string slot in a LABEL is the one exception --
            // Tessellator.BindText reports the name when the slot comes back null, unless the
            // node declares `missing`.
            Missing.Add(name);
            return 0f;
        }

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
        Since,
        Hover,
        Down,
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

        /// <summary>
        /// A colour literal, `#RGB` to `#RRGGBBAA`. It carries a Color, not a number, and is
        /// read by <see cref="EvaluateColour"/> rather than <see cref="Evaluate"/>.
        /// </summary>
        /// <remarks>
        /// Parsed to a Color HERE, at parse time, and never re-parsed. Unity's colour parser is
        /// a native ECall and tessellation runs on a worker thread, where an ECall throws, so
        /// the string must not survive into evaluation.
        /// </remarks>
        Colour,
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
        ["mix"] = 3,
        ["pi"] = 0, ["tau"] = 0,
    };

    private Kind _kind;
    private float _value;
    private Color _colour;

    /// <summary>
    /// How deep this tree is, measured once when it is parsed. Only the root's value is used,
    /// and only to choose between the recursive evaluator and the iterative one.
    /// </summary>
    private int _treeDepth;
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

    /// <summary>
    /// True when nothing but <c>t</c> and literals is involved -- no data, no repeat index, no
    /// scroll -- so it can be evaluated on the main thread with only a clock, while a rebuild
    /// may be using the shared context on a worker.
    /// </summary>
    internal bool ReadsOnlyTime()
    {
        switch (_kind)
        {
            case Kind.Constant:
            case Kind.Time:
                return true;
            case Kind.ScrollY:
            case Kind.ViewportH:
            case Kind.RepeatIndex:
            case Kind.RepeatCount:
            case Kind.Since:
            case Kind.Hover:
            case Kind.Down:
            case Kind.Scalar:
            case Kind.Element:
                return false;
        }

        foreach (var argument in _args)
        {
            if (argument != null && !argument.ReadsOnlyTime())
                return false;
        }

        return true;
    }

    /// <summary>True when nothing but a literal number is involved.</summary>
    internal bool IsConstant => _kind == Kind.Constant;

    /// <summary>That literal, where <see cref="IsConstant"/> is true; 0 otherwise.</summary>
    internal float ConstantValue => _kind == Kind.Constant ? _value : 0f;

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

    /// <summary>Set by the parser when an expression reads `hover` or `down`; the scene parser resets and reads it.</summary>
    [ThreadStatic] internal static bool SawPointer;

    internal static Expression Parse(string source, float fallback, bool colour = false)
    {
        try
        {
            var parser = new Parser(source);
            var expression = parser.ParseExpression();
            parser.ExpectEnd();
            expression._treeDepth = MeasureDepth(expression);

            // A colour literal is 0 on the numeric path, so `x = "=#5FD9A8"` put the shape at
            // 0, `w = "=#fff"` collapsed it, and `mix(#A,#B,t)` in a numeric slot was
            // `mix(0,0,t)` -- all of it silent, while the opposite mistake (a number where a
            // colour belongs) has always been reported. Checked here rather than at each call
            // site because this is the one place a numeric attribute's text becomes a tree;
            // the two attributes that WANT a colour pass `colour: true`.
            if (!colour && expression.ContainsColour)
            {
                Report?.Invoke($"expression \"{source}\" holds a colour, "
                               + "but this attribute takes a number");
            }

            return expression;
        }
        catch (FormatException ex)
        {
            Report?.Invoke($"expression \"{source}\": {ex.Message}");
            ScriptedScreensVectorPlugin.Log?.LogWarning($"expression \"{source}\": {ex.Message}");
            return Constant(fallback);
        }
    }

    /// <summary>
    /// Evaluates this expression as a COLOUR, for an attribute that wants one. Returns false
    /// when the expression does not describe a colour, so the caller can report it and fall
    /// back rather than drawing something arbitrary.
    /// </summary>
    /// <remarks>
    /// Colours are not a type in the evaluator -- every other value is a float, and a 32-bit
    /// RGBA does not survive one. They exist only where a colour is ASKED for: this walks the
    /// same tree, reading colour literals directly and taking the numeric arguments (a
    /// condition, a blend factor) through the ordinary float path. Anything else is not a
    /// colour, including arithmetic on one.
    /// </remarks>
    internal bool EvaluateColour(EvalContext context, out Color colour)
    {
        // An explicit stack, like every other walk here. `if` is a TAIL position -- the branch
        // taken is the whole answer -- so only `mix` leaves work behind, marked by a null on
        // the work stack and completed once both its ends are resolved.
        var work = new Stack<Expression?>();
        var blends = new Stack<Expression>();
        var done = new Stack<Color>();
        var failed = false;

        work.Push(this);

        while (work.Count > 0)
        {
            var node = work.Pop();

            if (node == null)
            {
                // `to` was pushed after `from`, so it pops first.
                var blend = blends.Pop();
                var to = done.Pop();
                var from = done.Pop();
                done.Push(Gradient.MixPremultiplied(from, to, Mathf.Clamp01(ColourArg(blend, 2, context))));
                continue;
            }

            if (node._kind == Kind.Colour)
            {
                done.Push(node._colour);
                continue;
            }

            if (node._kind == Kind.Call && node._args.Length == 3)
            {
                if (string.Equals(node._name, "if", StringComparison.Ordinal))
                {
                    // The condition is a number and ONLY the branch taken is evaluated, as
                    // before: the other one is never asked whether it is a colour.
                    var chosen = NonZero(ColourArg(node, 0, context)) ? node._args[1] : node._args[2];
                    if (chosen == null)
                    {
                        failed = true;
                        break;
                    }

                    work.Push(chosen);
                    continue;
                }

                if (string.Equals(node._name, "mix", StringComparison.Ordinal))
                {
                    // `mix(a, b, t)`: blended the way a gradient stop is, PREMULTIPLIED, so a
                    // mix towards a transparent colour fades out instead of drifting through
                    // its hue. `t` is read at the combine, after both ends, as it was.
                    if (node._args[0] == null || node._args[1] == null)
                    {
                        failed = true;
                        break;
                    }

                    blends.Push(node);
                    work.Push(null);
                    work.Push(node._args[1]);
                    work.Push(node._args[0]);
                    continue;
                }
            }

            // Anything else is not a colour, including arithmetic on one.
            colour = Color.white;
            return false;
        }

        if (!failed && done.Count == 1)
        {
            colour = done.Pop();
            return true;
        }

        colour = Color.white;
        return false;
    }

    /// <summary>
    /// A NUMERIC argument of a colour node -- `if`'s condition and `mix`'s blend factor --
    /// evaluated safely however deep it is.
    /// </summary>
    /// <remarks>
    /// This was the eighth recursive walk, and the one 0.11.85-0.11.86 missed while removing the
    /// other seven. <see cref="EvaluateColour"/> walks the colour tree on an explicit stack, but
    /// reached these two numbers through <see cref="Arg"/>, which calls <c>Evaluate</c> on the
    /// argument -- and only a ROOT is given a measured <see cref="_treeDepth"/> by
    /// <see cref="Parse"/>, so an argument's depth reads 0, the `> DeepTree` test is false, and it
    /// recursed at its subtree's full depth. A stack overflow cannot be caught in .NET: it ends
    /// the PROCESS, which is the game.
    ///
    /// Measured before the fix, out of process: `f = "=mix(#5FD9A8,#2E8B6E,----...1)"` survived
    /// 7,968 levels of prefix `-` and died at 8,125 with "Stack overflow.", exit 127 -- while the
    /// same number written as an ordinary numeric attribute survived 60,000. `if` behaved
    /// identically. `IsColour` never walks these arguments, so such an expression is accepted as
    /// a colour and then evaluated on the tessellation worker every rebuild.
    ///
    /// The root's depth covers every branch including this one, so testing it here is safe, and
    /// the memo is only allocated for a tree that is genuinely deep -- an ordinary `mix` keeps
    /// the single predictable branch through <see cref="Arg"/>.
    /// </remarks>
    private float ColourArg(Expression node, int index, EvalContext context)
    {
        if (_treeDepth > DeepTree && _memo == null)
        {
            var argument = index < node._args.Length ? node._args[index] : null;
            if (argument != null)
                return argument.EvaluateDeep(context);
        }

        return node.Arg(index, context);
    }

    /// <summary>
    /// True when this expression yields a colour, whatever it evaluates to. Checked once at
    /// parse time, so a colour attribute that is not a colour is reported rather than drawn.
    /// </summary>
    /// <remarks>
    /// EVERY branch that can be returned has to be a colour, not just the first one found.
    /// Checking only the top level let `if(c,#A,5)` and `mix(#A,1,t)` claim to be colours; at
    /// evaluation the non-colour branch simply failed, the paint fell through to its default,
    /// and the shape drew WHITE with nothing reported -- the silent-failure shape this whole
    /// mechanism exists to avoid.
    /// </remarks>
    internal bool IsColour
    {
        get
        {
            // Every branch that can be returned has to be a colour, so this is a pure
            // conjunction: push what must hold, fail on the first thing that does not, and
            // succeed when the stack empties. Iterative for the same reason as everything
            // else here -- the parser builds trees far deeper than the stack would take.
            var stack = new Stack<Expression>();
            stack.Push(this);

            while (stack.Count > 0)
            {
                var node = stack.Pop();

                if (node._kind == Kind.Colour)
                    continue;

                if (node._kind != Kind.Call || node._args.Length != 3)
                    return false;

                // `if(cond, a, b)`: the condition is a number, both OUTCOMES must be colours.
                if (string.Equals(node._name, "if", StringComparison.Ordinal))
                {
                    if (node._args[1] == null || node._args[2] == null)
                        return false;

                    stack.Push(node._args[1]!);
                    stack.Push(node._args[2]!);
                    continue;
                }

                // `mix(a, b, t)`: both ENDS must be colours; `t` is a number.
                if (string.Equals(node._name, "mix", StringComparison.Ordinal))
                {
                    if (node._args[0] == null || node._args[1] == null)
                        return false;

                    stack.Push(node._args[0]!);
                    stack.Push(node._args[1]!);
                    continue;
                }

                return false;
            }

            return true;
        }
    }

    /// <summary>True when a colour literal is anywhere in this expression.</summary>
    /// <remarks>
    /// Not the same question as <see cref="IsColour"/>, which asks whether every branch that can
    /// be RETURNED is a colour. This asks whether one is in there at all, which is what makes a
    /// numeric attribute's value wrong: `#5FD9A8+1` is not a colour by `IsColour` and is not a
    /// number either. Iterative for the same reason as every other walk here.
    /// </remarks>
    internal bool ContainsColour
    {
        get
        {
            var stack = new Stack<Expression>();
            stack.Push(this);

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node._kind == Kind.Colour)
                    return true;

                foreach (var argument in node._args)
                {
                    if (argument != null)
                        stack.Push(argument);
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Past this depth, evaluation stops recursing and walks the tree with an explicit stack.
    /// Chosen far above anything a scene produces -- a real expression is three or four deep --
    /// and far below where the call stack gives out, so neither path is ever close to a limit.
    /// </summary>
    private const int DeepTree = 256;

    /// <summary>Values of the nodes already evaluated, while the iterative path is running.</summary>
    /// <remarks>
    /// `[ThreadStatic]` because tessellation runs on a worker and captures evaluate on the main
    /// thread; null except inside <see cref="EvaluateDeep"/>, which is what makes
    /// <see cref="Arg"/> a single predictable branch for every ordinary expression.
    /// </remarks>
    [ThreadStatic] private static Dictionary<Expression, float>? _memo;

    internal float Evaluate(EvalContext context)
    {
        // A tree deep enough to overflow the stack is walked rather than recursed. The limit
        // this replaces was a cap that REFUSED such an expression, which is not a fix: the
        // input is merely deep, and a scene generated from a document nests further than a
        // person would type.
        if (_treeDepth > DeepTree && _memo == null)
            return EvaluateDeep(context);

        switch (_kind)
        {
            case Kind.Constant: return _value;
            case Kind.Time: return context.Time;
            case Kind.ScrollY: return context.ScrollY;
            case Kind.ViewportH: return context.ViewportH;
            case Kind.RepeatIndex: return context.Index(_depth);
            case Kind.RepeatCount: return context.Count();
            case Kind.Scalar: return context.Scalar(_name);
            case Kind.Since: return context.Since(_name);
            case Kind.Hover: return context.Pointer(held: false);
            case Kind.Down: return context.Pointer(held: true);
            case Kind.Element: return context.Element(_name, Arg(0, context));

            case Kind.Negate: return -Arg(0, context);
            case Kind.Add: return Arg(0, context) + Arg(1, context);
            case Kind.Subtract: return Arg(0, context) - Arg(1, context);
            case Kind.Multiply: return Arg(0, context) * Arg(1, context);
            case Kind.Divide: return Divide(Arg(0, context), Arg(1, context));
            // The guard has to be on the DIVISOR. This was Divide(a % b, 1f), which put the
            // zero check on the constant 1 and let `x % 0` return NaN -- and a NaN reaching a
            // coordinate poisons every vertex derived from it, silently. Zero matches what `/`
            // already does and what the docs promise.
            case Kind.Modulo: return Modulo(Arg(0, context), Arg(1, context));
            case Kind.Power: return Power(Arg(0, context), Arg(1, context));
            case Kind.Call: return Invoke(context);

            // A colour is not a number. Reaching here means a colour was written where a
            // number belongs (`x = "=#FF0000"`), which the parser reports; 0 keeps the shape
            // on screen instead of moving it somewhere unpredictable.
            case Kind.Colour: return 0f;

            default: return 0f;
        }
    }

    private float Arg(int index, EvalContext context)
    {
        var argument = _args[index];
        if (argument == null)
            return 0f;

        // While the iterative evaluator is running every child has already been computed, so
        // this reads a value instead of descending into one. That is the whole trick: the
        // switch above needs no second implementation.
        var memo = _memo;
        if (memo != null)
            return memo.TryGetValue(argument, out var known) ? known : 0f;

        return argument.Evaluate(context);
    }

    /// <summary>Depth of a tree, counted without recursing.</summary>
    private static int MeasureDepth(Expression root)
    {
        var deepest = 0;
        var stack = new Stack<(Expression Node, int Depth)>();
        stack.Push((root, 1));

        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();
            if (depth > deepest)
                deepest = depth;

            // Stop counting once it is past anything that matters: the only question this
            // answers is "deeper than DeepTree?", and a pathological tree should not cost a
            // full walk to find that out.
            if (deepest > DeepTree)
                return deepest;

            foreach (var child in node._args)
            {
                if (child != null)
                    stack.Push((child, depth + 1));
            }
        }

        return deepest;
    }

    /// <summary>
    /// How this node reads its arguments: <paramref name="probe"/> is the one argument it must
    /// evaluate before it can decide whether it reads any other (-1 when it decides nothing),
    /// and the return value is how many leading arguments it reads whatever they hold.
    /// </summary>
    /// <remarks>
    /// Mirrors what <see cref="Invoke"/> and the operator arms of <see cref="Evaluate"/>
    /// actually read. Nearly everything reads all its arguments; the exceptions are the whole
    /// list: `if`, `and` and `or` read the FIRST and then decide, `mod` guards on its DIVISOR
    /// and returns before it reads the dividend, so the one it always reads is the SECOND, and a
    /// numeric `mix` reads none -- <see cref="Invoke"/> has no case for it, because a mix is a
    /// colour, read by <see cref="EvaluateColour"/>. `step` reads argument 1 before argument 0
    /// but always reads both, so it needs nothing here: the set of names it records in
    /// <see cref="EvalContext.Missing"/> is the same either way. One switch rather than two
    /// predicates: this runs per node of a deep tree.
    /// </remarks>
    private int EagerArgs(out int probe)
    {
        probe = -1;

        if (_kind != Kind.Call)
            return _args.Length;

        switch (_name)
        {
            case "if":
            case "and":
            case "or":
                probe = 0;
                return 0;

            case "mod":
                probe = 1;
                return 0;

            case "mix":
                return 0;

            default:
                return _args.Length;
        }
    }

    /// <summary>
    /// Which argument is still needed once the probed one is in the memo, or -1 when none is.
    /// Mirrors <see cref="Invoke"/>: `if` takes one branch, `and` stops on a false left operand,
    /// `or` on a true one, and `mod` answers 0 for a zero divisor without reading the dividend.
    /// </summary>
    private int LazyBranch(EvalContext context)
    {
        switch (_name)
        {
            case "if": return NonZero(Arg(0, context)) ? 1 : 2;
            case "and": return NonZero(Arg(0, context)) ? 1 : -1;
            case "or": return NonZero(Arg(0, context)) ? -1 : 1;
            case "mod": return Mathf.Approximately(Arg(1, context), 0f) ? -1 : 0;
            default: return -1;
        }
    }

    /// <summary>
    /// Evaluates a deep tree with an explicit stack, bottom up, memoising each node's value so
    /// the ordinary switch can read its children instead of calling into them.
    /// </summary>
    /// <remarks>
    /// Stage-driven rather than collect-everything-then-evaluate, and that is what makes it
    /// LAZY. The first version listed EVERY node and evaluated all of them, so past
    /// <see cref="DeepTree"/> an `if` evaluated the branch it does not take, `and`/`or` their
    /// right-hand side, `mod` a dividend it discards and a numeric `mix` all three of its
    /// arguments. The VALUE came out the same -- every operator here is total -- but the work
    /// was done, and a data name read only there was recorded in
    /// <see cref="EvalContext.Missing"/>, so a scene that is correct at depth 256 reported an
    /// unresolved data name at 257.
    ///
    /// Stage 0 pushes what the node always reads, stage 1 runs for a node that decides, once its
    /// probed argument is known, and pushes the one argument it chose, stage 2 evaluates. The
    /// node's own frame goes on before its arguments, so it pops after them and the memo holds
    /// every value the ordinary switch will read. A node with nothing to resolve first is
    /// evaluated where it is found, which keeps the common node at one push and one pop.
    /// </remarks>
    private float EvaluateDeep(EvalContext context)
    {
        var memo = new Dictionary<Expression, float>(ReferenceComparer.Instance);
        var pending = new Stack<(Expression Node, int Stage)>();
        pending.Push((this, 0));

        _memo = memo;
        try
        {
            while (pending.Count > 0)
            {
                var (node, stage) = pending.Pop();

                if (stage == 0)
                {
                    var eager = node.EagerArgs(out var probe);

                    if (probe >= 0)
                    {
                        pending.Push((node, 1));

                        var first = probe < node._args.Length ? node._args[probe] : null;
                        if (first != null)
                            pending.Push((first, 0));

                        continue;
                    }

                    if (eager == 0)
                    {
                        // A leaf, or a numeric `mix`: nothing to resolve first, nothing to read.
                        memo[node] = node.Evaluate(context);
                        continue;
                    }

                    pending.Push((node, 2));

                    // Pushed in reverse, so argument 0 comes off the stack first and the names
                    // an argument reports land in the order the recursive path records them.
                    for (var i = eager - 1; i >= 0; i--)
                    {
                        var child = node._args[i];
                        if (child != null)
                            pending.Push((child, 0));
                    }

                    continue;
                }

                if (stage == 1)
                {
                    // The probed argument is in the memo, so Arg reads it instead of descending.
                    var branch = node.LazyBranch(context);
                    pending.Push((node, 2));

                    if (branch >= 0 && branch < node._args.Length && node._args[branch] != null)
                        pending.Push((node._args[branch]!, 0));

                    continue;
                }

                memo[node] = node.Evaluate(context);
            }

            return memo[this];
        }
        finally
        {
            _memo = null;
        }
    }

    /// <summary>Identity, so two equal-looking subtrees keep separate memo entries.</summary>
    private sealed class ReferenceComparer : IEqualityComparer<Expression>
    {
        internal static readonly ReferenceComparer Instance = new();

        public bool Equals(Expression? a, Expression? b) => ReferenceEquals(a, b);

        public int GetHashCode(Expression value) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }

    // Division by zero yields 0 rather than an infinity that would poison vertex
    // positions and produce an invisible or corrupt mesh.
    private static float Divide(float a, float b)
    {
        return Mathf.Approximately(b, 0f) ? 0f : a / b;
    }

    private static float Modulo(float a, float b)
    {
        return Mathf.Approximately(b, 0f) ? 0f : a % b;
    }

    // Same reason as Divide, and `^` is the one operator that reaches it on ordinary input:
    // `0^-1` is infinity, and a negative base with a fractional exponent -- `$v^0.5` the frame a
    // reading dips below zero -- is NaN. Nothing between here and Mesh.SetVertices rejects
    // either, and the scene is normally ONE mesh whose bounds every vertex feeds, so one
    // poisoned coordinate takes the whole console rather than the shape that asked for it. Zero
    // is what `/`, `%`, `mod` and `sqrt` of a negative already answer for a number with no value.
    private static float Power(float a, float b)
    {
        var value = Mathf.Pow(a, b);
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
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

    /// <summary>
    /// The `ver` encoding: `major*1000000 + minor*1000 + patch`, so 0.11.103 is 11103. Separate
    /// from the assembly read so the collisions that forced it can be tested on version numbers
    /// this assembly does not carry. See Parser.VersionNumber for what it is for and what the
    /// widths cost.
    /// </summary>
    internal static float EncodeVersion(System.Version version) =>
        version.Major * 1000000f + version.Minor * 1000f + version.Build;

    private sealed class Parser
    {
        private readonly string _text;
        private int _at;

        internal Parser(string source)
        {
            // A leading '=' marks an expression attribute; tolerate its absence.
            _text = source.StartsWith('=') ? source[1..] : source;
        }

        /// <summary>
        /// Parses the whole expression with NO recursion, so nesting is bounded by memory
        /// rather than by the call stack.
        /// </summary>
        /// <remarks>
        /// This was a recursive-descent parser, and every nesting construct -- `(`, a `$name[]`
        /// index, a call's arguments, a run of prefix `-`, a chain of `^` -- recursed once per
        /// level. Deep input therefore overflowed the stack, which in .NET cannot be caught: it
        /// ends the PROCESS, so one scene could take the game down. Depth caps were added
        /// against that and were the wrong answer: they refuse input that is merely deep, and a
        /// scene generated from a document can legitimately nest further than a person would
        /// type. The limit is gone rather than raised.
        ///
        /// Two stacks do what the call stack did. Operands and operators give ordinary
        /// precedence climbing within one level; the GROUP stack holds the one thing the
        /// recursion was really carrying -- which bracket we are inside and what to build when
        /// it closes. Precedence and associativity are unchanged: `2^3^2` is 512, `-2^2` is -4,
        /// `2^-3` works, and `---1` is -1.
        /// </remarks>
        internal Expression ParseExpression()
        {
            var operands = new List<Expression>(8);
            var operators = new List<Operator>(8);
            var groups = new List<Group>(4);

            while (true)
            {
                // --- an operand, with any prefix minus signs in front of it ---
                while (true)
                {
                    SkipSpace();
                    if (!Match('-'))
                        break;

                    operators.Add(new Operator(Kind.Negate, UnaryPrecedence, rightAssociative: true, unary: true));
                }

                SkipSpace();
                if (_at >= _text.Length)
                    throw new FormatException("unexpected end");

                if (Match('('))
                {
                    groups.Add(new Group(GroupKind.Paren, null, operands.Count, operators.Count));
                    continue;
                }

                if (Match('$'))
                {
                    var dataName = ReadName();
                    SkipSpace();
                    if (Match('['))
                    {
                        groups.Add(new Group(GroupKind.Index, dataName, operands.Count, operators.Count));
                        continue;
                    }

                    operands.Add(new Expression { _kind = Kind.Scalar, _name = dataName });
                }
                else if (_text[_at] == '#')
                {
                    operands.Add(ReadColour());
                }
                else if (char.IsDigit(_text[_at]) || _text[_at] == '.')
                {
                    operands.Add(Constant(ReadNumber()));
                }
                else if (char.IsLetter(_text[_at]) || _text[_at] == '_')
                {
                    var name = ReadName();
                    SkipSpace();

                    if (!Match('('))
                    {
                        operands.Add(Variable(name));
                    }
                    else if (name == "since")
                    {
                        // `since($name)` takes a data NAME, not its value, so it opens no
                        // group: there is no sub-expression to parse.
                        operands.Add(ReadSince());
                    }
                    else
                    {
                        if (!Arity.ContainsKey(name))
                            throw new FormatException("unknown function '" + name + "'");

                        var call = new Group(GroupKind.Call, name, operands.Count, operators.Count);
                        SkipSpace();

                        // A zero-argument function closes at once: `pi()`.
                        if (Match(')'))
                        {
                            operands.Add(CloseCall(call, null));
                        }
                        else
                        {
                            groups.Add(call);
                            continue;
                        }
                    }
                }
                else
                {
                    throw new FormatException("unexpected '" + _text[_at] + "'");
                }

                // --- an operator, a separator, or the end ---
                while (true)
                {
                    SkipSpace();

                    if (_at < _text.Length && TryReadBinary(out var binary))
                    {
                        ReduceWhile(operands, operators, binary, groups);
                        operators.Add(binary);
                        break;
                    }

                    if (groups.Count == 0)
                    {
                        // Nothing is open, so whatever is here belongs to the caller.
                        ReduceAll(operands, operators, 0, 0);
                        if (operands.Count != 1)
                            throw new FormatException("malformed expression");

                        return operands[0];
                    }

                    var group = groups[groups.Count - 1];

                    if (group.Kind == GroupKind.Call && Match(','))
                    {
                        ReduceAll(operands, operators, group.OperandMark, group.OperatorMark);
                        group.Arguments.Add(TakeOne(operands, group.OperandMark));
                        break;
                    }

                    if (group.Kind == GroupKind.Paren && Match(')'))
                    {
                        ReduceAll(operands, operators, group.OperandMark, group.OperatorMark);
                        if (operands.Count != group.OperandMark + 1)
                            throw new FormatException("malformed expression");

                        groups.RemoveAt(groups.Count - 1);
                        continue;
                    }

                    if (group.Kind == GroupKind.Call && Match(')'))
                    {
                        ReduceAll(operands, operators, group.OperandMark, group.OperatorMark);
                        group.Arguments.Add(TakeOne(operands, group.OperandMark));
                        groups.RemoveAt(groups.Count - 1);
                        operands.Add(CloseCall(group, group.Arguments));
                        continue;
                    }

                    if (group.Kind == GroupKind.Index && Match(']'))
                    {
                        ReduceAll(operands, operators, group.OperandMark, group.OperatorMark);
                        var index = TakeOne(operands, group.OperandMark);
                        groups.RemoveAt(groups.Count - 1);

                        operands.Add(new Expression
                        {
                            _kind = Kind.Element,
                            _name = group.Name!,
                            _args = new Expression?[] { index },
                            UsesTime = index.UsesTime,
                            UsesScroll = index.UsesScroll,
                        });
                        continue;
                    }

                    throw new FormatException(group.Kind == GroupKind.Paren
                        ? "expected ')'"
                        : group.Kind == GroupKind.Index
                            ? "expected ']'"
                            : "expected ',' or ')' in " + group.Name + "(...)");
                }
            }
        }

        /// <summary>A prefix minus binds looser than `^` and tighter than `*`.</summary>
        private const int UnaryPrecedence = 3;

        private enum GroupKind { Paren, Index, Call }

        /// <summary>One open bracket: what the recursion used to hold on the call stack.</summary>
        private sealed class Group
        {
            internal readonly GroupKind Kind;
            internal readonly string? Name;

            /// <summary>Where this group's own operands and operators begin.</summary>
            internal readonly int OperandMark;
            internal readonly int OperatorMark;

            internal readonly List<Expression> Arguments;

            internal Group(GroupKind kind, string? name, int operandMark, int operatorMark)
            {
                Kind = kind;
                Name = name;
                OperandMark = operandMark;
                OperatorMark = operatorMark;
                Arguments = new List<Expression>(4);
            }
        }

        private readonly struct Operator
        {
            internal readonly Kind Kind;
            internal readonly int Precedence;
            internal readonly bool RightAssociative;
            internal readonly bool Unary;

            internal Operator(Kind kind, int precedence, bool rightAssociative, bool unary = false)
            {
                Kind = kind;
                Precedence = precedence;
                RightAssociative = rightAssociative;
                Unary = unary;
            }
        }

        private bool TryReadBinary(out Operator op)
        {
            switch (_text[_at])
            {
                case '+': _at++; op = new Operator(Kind.Add, 1, false); return true;
                case '-': _at++; op = new Operator(Kind.Subtract, 1, false); return true;
                case '*': _at++; op = new Operator(Kind.Multiply, 2, false); return true;
                case '/': _at++; op = new Operator(Kind.Divide, 2, false); return true;
                case '%': _at++; op = new Operator(Kind.Modulo, 2, false); return true;

                // Right-associative and tighter than a prefix minus, so `2^3^2` is 512 and
                // `-2^2` is -(2^2).
                case '^': _at++; op = new Operator(Kind.Power, 4, true); return true;

                default: op = default; return false;
            }
        }

        /// <summary>Folds the operators that bind tighter than the one about to be pushed.</summary>
        private static void ReduceWhile(List<Expression> operands, List<Operator> operators,
            Operator next, List<Group> groups)
        {
            var operatorFloor = groups.Count > 0 ? groups[groups.Count - 1].OperatorMark : 0;
            var operandFloor = groups.Count > 0 ? groups[groups.Count - 1].OperandMark : 0;

            while (operators.Count > operatorFloor)
            {
                var top = operators[operators.Count - 1];
                var folds = next.RightAssociative
                    ? top.Precedence > next.Precedence
                    : top.Precedence >= next.Precedence;

                if (!folds)
                    break;

                Fold(operands, operators, operandFloor);
            }
        }

        private static void ReduceAll(List<Expression> operands, List<Operator> operators,
            int operandFloor, int operatorFloor)
        {
            while (operators.Count > operatorFloor)
                Fold(operands, operators, operandFloor);
        }

        private static void Fold(List<Expression> operands, List<Operator> operators, int operandFloor)
        {
            var op = operators[operators.Count - 1];
            operators.RemoveAt(operators.Count - 1);

            if (op.Unary)
            {
                if (operands.Count <= operandFloor)
                    throw new FormatException("malformed expression");

                var operand = operands[operands.Count - 1];
                operands.RemoveAt(operands.Count - 1);

                operands.Add(new Expression
                {
                    _kind = Kind.Negate,
                    _args = new Expression?[] { operand },
                    UsesTime = operand.UsesTime,
                    UsesScroll = operand.UsesScroll,
                });

                return;
            }

            if (operands.Count < operandFloor + 2)
                throw new FormatException("malformed expression");

            var right = operands[operands.Count - 1];
            operands.RemoveAt(operands.Count - 1);
            var left = operands[operands.Count - 1];
            operands.RemoveAt(operands.Count - 1);
            operands.Add(Binary(op.Kind, left, right));
        }

        private static Expression TakeOne(List<Expression> operands, int mark)
        {
            if (operands.Count != mark + 1)
                throw new FormatException("malformed expression");

            var value = operands[operands.Count - 1];
            operands.RemoveAt(operands.Count - 1);
            return value;
        }

        private static Expression CloseCall(Group group, List<Expression>? arguments)
        {
            var expected = Arity[group.Name!];
            var count = arguments == null ? 0 : arguments.Count;

            if (count != expected)
            {
                throw new FormatException(
                    group.Name + "() takes " + expected + " argument(s), got " + count);
            }

            var node = new Expression
            {
                _kind = Kind.Call,
                _name = group.Name!,
                _args = arguments == null ? System.Array.Empty<Expression>() : arguments.ToArray(),
            };

            if (arguments != null)
            {
                foreach (var argument in arguments)
                {
                    node.UsesTime |= argument.UsesTime;
                    node.UsesScroll |= argument.UsesScroll;
                }
            }

            return node;
        }

        /// <summary>A colour literal, turned into a Color at parse time; see Kind.Colour.</summary>
        private Expression ReadColour()
        {
            var start = _at++;
            while (_at < _text.Length && Uri.IsHexDigit(_text[_at]))
                _at++;

            var text = _text[start.._at];
            if (!Colours.TryParse(text, out var parsed))
                throw new FormatException("\"" + text + "\" is not a colour");

            return new Expression { _kind = Kind.Colour, _colour = parsed };
        }

        private Expression ReadSince()
        {
            SkipSpace();
            if (!Match('$'))
                throw new FormatException("since() takes a data name, e.g. since($jump)");

            var dataName = ReadName();
            SkipSpace();
            if (!Match(')'))
                throw new FormatException("expected ')' after since($name");

            return new Expression { _kind = Kind.Since, _name = dataName, UsesTime = true };
        }

        /// <summary>
        /// The running mod's version as `major*1000000 + minor*1000 + patch` -- 0.11.103 is 11103.
        /// </summary>
        /// <remarks>
        /// It is a plain number so a scene can compare it, and it exists so an exported console
        /// can tell a player their mod is too old to draw it. The degradation is the point: on a
        /// mod without `ver` the name is an unknown variable, the expression fails to parse, and
        /// the attribute falls back to its default. With `v="=lt(ver,NNNNN)"` that default is
        /// `visible`, so an "update the mod" banner appears on exactly the versions that lack the
        /// feature and hides itself on the ones that do not. The parse failure is also reported as
        /// a problem, which is a diagnostic rather than a fault.
        ///
        /// Computed once: the version cannot change while the game runs, and this is reached from
        /// the tessellation worker.
        ///
        /// The encoding CHANGED in 0.11.103. It was `major*10000 + minor*100 + patch`, which gave
        /// the patch field two digits and had already run out of them: 0.11.100 and 0.12.0 both
        /// read 1200, and order inverted, so 0.12.0 (1200) compared BELOW 0.11.101 (1201) and an
        /// `lt(ver, FLOOR)` banner appeared on the newer mod. A scene comparing `ver` to a literal
        /// written before 0.11.103 compares against the old scale and must be updated.
        ///
        /// The new widths are not infinite either, and the flaw is the same class further out: a
        /// patch of 1000 lands on the next minor (0.11.1000 and 0.12.0 both read 12000) and a
        /// minor of 1000 lands on the next major (0.1000.0 and 1.0.0 both read 1000000). It is
        /// also a float, so above 16777216 the integers are no longer exact and from major 17 up
        /// two adjacent patch numbers can read equal (17.0.0 and 17.0.1 both read 17000000).
        /// Three digits is what fits: the whole expression language is float, which carries seven
        /// exact decimal digits in total, so widening one field narrows another.
        /// </remarks>
        private static readonly float VersionNumber = ReadVersion();

        private static float ReadVersion()
        {
            var version = typeof(Expression).Assembly.GetName().Version;
            return version == null ? 0f : EncodeVersion(version);
        }

        private static Expression Variable(string name)
        {
            // `true` is 1 and `false` is 0, because 0.11.102 made a LUA boolean read as 1 and 0
            // everywhere a number is read and scene text has no boolean type of its own: `R
            // v=false` written in text became the identifier `false`, was reported as an unknown
            // variable, and fell back to the attribute's DEFAULT -- which for `v` is 1, so the
            // node the author hid stayed on screen. Measured on 0.11.104: `v=false` and `v=true`
            // both evaluated to 1, each with the problem `expression "false": unknown variable
            // 'false'`. Lower case only, as every other name here is and as Lua spells them.
            if (name == "true")
                return Constant(1f);

            if (name == "false")
                return Constant(0f);

            if (name == "ver")
                return Constant(VersionNumber);

            if (name == "t")
                return new Expression { _kind = Kind.Time, UsesTime = true };

            if (name == "sy")
                return new Expression { _kind = Kind.ScrollY, UsesScroll = true };

            if (name == "vh")
                return new Expression { _kind = Kind.ViewportH, UsesScroll = true };

            if (name == "n")
                return new Expression { _kind = Kind.RepeatCount };

            // Pointer state, kept per client. Noted for the scene, which redraws on a change
            // only when something reads it.
            if (name == "hover" || name == "down")
            {
                SawPointer = true;
                return new Expression { _kind = name == "hover" ? Kind.Hover : Kind.Down };
            }

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
            while (_at < _text.Length)
            {
                var c = _text[_at];
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    _at++;
                }
                else if (char.IsHighSurrogate(c) && _at + 1 < _text.Length && char.IsLowSurrogate(_text[_at + 1]))
                {
                    // A name outside the BMP is one TEXT ELEMENT in two UTF-16 units, and
                    // `char.IsLetterOrDigit` is false for each half on its own: `$name`
                    // reported "expected a name" for a name that works as an `id` and as a
                    // label's `$binding`, both of which read the author's text as written. The
                    // pair is taken whole, and an unpaired half still ends the name, so `_at`
                    // can never land between the two. The ASCII loop above never reaches here.
                    _at += 2;
                }
                else
                {
                    break;
                }
            }

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
