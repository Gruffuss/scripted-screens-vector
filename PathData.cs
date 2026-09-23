using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>One flattened subpath: a point list plus whether it closes.</summary>
internal sealed class SubPath
{
    internal List<Vector2> Points = new();
    internal bool Closed;
}

/// <summary>
/// SVG path data: parsed once into commands, flattened per draw at a tolerance chosen from
/// the on-screen size.
/// </summary>
/// <remarks>
/// Parsing and flattening are deliberately separate. The command list is static — path `d`
/// is a string, so it cannot contain expressions and its geometry never animates. Only the
/// <em>tolerance</em> varies, with console resolution and camera distance. Flattening at
/// draw time is what makes a curve smooth when the player walks up to a console and cheap
/// when they walk away, and the result is cached against a quantised scale so it is not
/// recomputed every frame.
///
/// Spec §11.4 notes this needs hysteresis; the quantisation here provides it, since a small
/// scale change does not cross a bucket boundary.
/// </remarks>
internal sealed class PathData
{
    private enum Op
    {
        Move,
        Line,
        Quadratic,
        Cubic,
        Arc,
        Close,
    }

    private readonly struct Command
    {
        internal readonly Op Op;
        internal readonly float[] Args;

        internal Command(Op op, float[] args)
        {
            Op = op;
            Args = args;
        }
    }

    private readonly List<Command> _commands = new();

    private List<SubPath>? _cached;
    private float _cachedScale = -1f;

    internal bool IsEmpty => _commands.Count == 0;

    /// <summary>Flattens at a tolerance derived from scale, reusing the last result when close.</summary>
    internal List<SubPath> Flatten(float scale)
    {
        // Quantise so ordinary camera drift does not retessellate; ~12% steps.
        var bucket = Mathf.Max(0.05f, Mathf.Pow(1.12f, Mathf.Round(Mathf.Log(Mathf.Max(scale, 0.001f)) / Mathf.Log(1.12f))));

        if (_cached != null && Mathf.Approximately(bucket, _cachedScale))
            return _cached;

        _cached = Build(bucket);
        _cachedScale = bucket;
        return _cached;
    }

    private List<SubPath> Build(float scale)
    {
        var subpaths = new List<SubPath>();
        SubPath? current = null;

        var cursor = Vector2.zero;
        var start = Vector2.zero;

        foreach (var command in _commands)
        {
            switch (command.Op)
            {
                case Op.Move:
                    current = new SubPath();
                    subpaths.Add(current);
                    cursor = new Vector2(command.Args[0], command.Args[1]);
                    start = cursor;
                    current.Points.Add(cursor);
                    break;

                case Op.Line:
                    current ??= Begin(subpaths, cursor);
                    cursor = new Vector2(command.Args[0], command.Args[1]);
                    current.Points.Add(cursor);
                    break;

                case Op.Quadratic:
                {
                    current ??= Begin(subpaths, cursor);
                    var control = new Vector2(command.Args[0], command.Args[1]);
                    var end = new Vector2(command.Args[2], command.Args[3]);
                    FlattenQuadratic(current.Points, cursor, control, end, scale);
                    cursor = end;
                    break;
                }

                case Op.Cubic:
                {
                    current ??= Begin(subpaths, cursor);
                    var c1 = new Vector2(command.Args[0], command.Args[1]);
                    var c2 = new Vector2(command.Args[2], command.Args[3]);
                    var end = new Vector2(command.Args[4], command.Args[5]);
                    FlattenCubic(current.Points, cursor, c1, c2, end, scale);
                    cursor = end;
                    break;
                }

                case Op.Arc:
                {
                    current ??= Begin(subpaths, cursor);
                    var end = new Vector2(command.Args[5], command.Args[6]);
                    FlattenArc(current.Points, cursor, command.Args[0], command.Args[1], command.Args[2],
                        command.Args[3] > 0.5f, command.Args[4] > 0.5f, end, scale);
                    cursor = end;
                    break;
                }

                case Op.Close:
                    if (current != null)
                    {
                        current.Closed = true;
                        cursor = start;
                        current = null;
                    }

                    break;
            }
        }

        subpaths.RemoveAll(p => p.Points.Count < 2);
        return subpaths;
    }

    private static SubPath Begin(List<SubPath> into, Vector2 at)
    {
        var path = new SubPath();
        path.Points.Add(at);
        into.Add(path);
        return path;
    }

    /// <summary>Segment count from the control polygon's on-screen length.</summary>
    private static int Steps(float controlLength, float scale)
    {
        return Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(controlLength * scale) * 1.4f), 2, 64);
    }

    private static void FlattenQuadratic(List<Vector2> into, Vector2 p0, Vector2 p1, Vector2 p2, float scale)
    {
        var steps = Steps(Vector2.Distance(p0, p1) + Vector2.Distance(p1, p2), scale);

        for (var i = 1; i <= steps; i++)
        {
            var t = i / (float)steps;
            var u = 1f - t;
            into.Add(u * u * p0 + 2f * u * t * p1 + t * t * p2);
        }
    }

    private static void FlattenCubic(List<Vector2> into, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float scale)
    {
        var length = Vector2.Distance(p0, p1) + Vector2.Distance(p1, p2) + Vector2.Distance(p2, p3);
        var steps = Steps(length, scale);

        for (var i = 1; i <= steps; i++)
        {
            var t = i / (float)steps;
            var u = 1f - t;
            into.Add(u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3);
        }
    }

    /// <summary>
    /// SVG elliptical arc: endpoint parameterisation converted to centre form.
    /// </summary>
    /// <remarks>
    /// Follows the conversion in the SVG implementation notes, including the radius
    /// correction for radii too small to span the endpoints — without it a slightly
    /// undersized radius produces NaN vertices rather than a visibly wrong curve.
    /// </remarks>
    private static void FlattenArc(List<Vector2> into, Vector2 from, float rx, float ry, float rotation, bool largeArc, bool sweep, Vector2 to, float scale)
    {
        rx = Mathf.Abs(rx);
        ry = Mathf.Abs(ry);

        if (rx < 0.0001f || ry < 0.0001f || (from - to).sqrMagnitude < 0.000001f)
        {
            into.Add(to);
            return;
        }

        var phi = rotation * Mathf.Deg2Rad;
        var cosPhi = Mathf.Cos(phi);
        var sinPhi = Mathf.Sin(phi);

        var dx = (from.x - to.x) * 0.5f;
        var dy = (from.y - to.y) * 0.5f;

        var x1 = cosPhi * dx + sinPhi * dy;
        var y1 = -sinPhi * dx + cosPhi * dy;

        // Scale radii up if they cannot reach across the chord.
        var lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
        if (lambda > 1f)
        {
            var root = Mathf.Sqrt(lambda);
            rx *= root;
            ry *= root;
        }

        var numerator = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
        var denominator = rx * rx * y1 * y1 + ry * ry * x1 * x1;
        var factor = denominator < 0.0001f ? 0f : Mathf.Sqrt(Mathf.Max(0f, numerator / denominator));

        if (largeArc == sweep)
            factor = -factor;

        var cx1 = factor * rx * y1 / ry;
        var cy1 = -factor * ry * x1 / rx;

        var cx = cosPhi * cx1 - sinPhi * cy1 + (from.x + to.x) * 0.5f;
        var cy = sinPhi * cx1 + cosPhi * cy1 + (from.y + to.y) * 0.5f;

        var startAngle = Mathf.Atan2((y1 - cy1) / ry, (x1 - cx1) / rx);
        var endAngle = Mathf.Atan2((-y1 - cy1) / ry, (-x1 - cx1) / rx);
        var sweepAngle = endAngle - startAngle;

        if (!sweep && sweepAngle > 0f)
            sweepAngle -= Mathf.PI * 2f;
        else if (sweep && sweepAngle < 0f)
            sweepAngle += Mathf.PI * 2f;

        var steps = Steps(Mathf.Abs(sweepAngle) * Mathf.Max(rx, ry), scale);

        for (var i = 1; i <= steps; i++)
        {
            var angle = startAngle + sweepAngle * (i / (float)steps);
            var px = Mathf.Cos(angle) * rx;
            var py = Mathf.Sin(angle) * ry;
            into.Add(new Vector2(cx + cosPhi * px - sinPhi * py, cy + sinPhi * px + cosPhi * py));
        }
    }

    /// <summary>Parses SVG path syntax. Unknown commands abort the rest of the string.</summary>
    internal static PathData Parse(string d)
    {
        var path = new PathData();
        if (string.IsNullOrWhiteSpace(d))
            return path;

        var reader = new Reader(d);
        var cursor = Vector2.zero;
        var start = Vector2.zero;
        var previous = '\0';

        // Smooth continuations (S, T) reflect the previous curve's trailing control point
        // through the current point. Tracked per curve family: an S after a non-cubic, or a
        // T after a non-quadratic, reflects nothing and the control point coincides with
        // the cursor, which is what SVG specifies.
        var lastCubicControl = Vector2.zero;
        var lastQuadraticControl = Vector2.zero;
        var previousWasCubic = false;
        var previousWasQuadratic = false;

        while (reader.SkipSeparators())
        {
            var c = reader.Peek();
            char command;

            if (char.IsLetter(c))
            {
                command = c;
                reader.Advance();
            }
            else if (previous != '\0')
            {
                // Repeated coordinate sets reuse the last command, as SVG allows.
                command = previous == 'M' ? 'L' : previous == 'm' ? 'l' : previous;
            }
            else
            {
                break;
            }

            var relative = char.IsLower(command);
            var origin = relative ? cursor : Vector2.zero;

            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                    cursor = origin + reader.Point();
                    start = cursor;
                    path._commands.Add(new Command(Op.Move, new[] { cursor.x, cursor.y }));
                    break;

                case 'L':
                    cursor = origin + reader.Point();
                    path._commands.Add(new Command(Op.Line, new[] { cursor.x, cursor.y }));
                    break;

                case 'H':
                    cursor = new Vector2((relative ? cursor.x : 0f) + reader.Number(), cursor.y);
                    path._commands.Add(new Command(Op.Line, new[] { cursor.x, cursor.y }));
                    break;

                case 'V':
                    cursor = new Vector2(cursor.x, (relative ? cursor.y : 0f) + reader.Number());
                    path._commands.Add(new Command(Op.Line, new[] { cursor.x, cursor.y }));
                    break;

                case 'Q':
                {
                    var control = origin + reader.Point();
                    cursor = origin + reader.Point();
                    path._commands.Add(new Command(Op.Quadratic, new[] { control.x, control.y, cursor.x, cursor.y }));
                    lastQuadraticControl = control;
                    break;
                }

                case 'T':
                {
                    var control = previousWasQuadratic ? cursor * 2f - lastQuadraticControl : cursor;
                    cursor = origin + reader.Point();
                    path._commands.Add(new Command(Op.Quadratic, new[] { control.x, control.y, cursor.x, cursor.y }));
                    lastQuadraticControl = control;
                    break;
                }

                case 'C':
                {
                    var c1 = origin + reader.Point();
                    var c2 = origin + reader.Point();
                    cursor = origin + reader.Point();
                    path._commands.Add(new Command(Op.Cubic, new[] { c1.x, c1.y, c2.x, c2.y, cursor.x, cursor.y }));
                    lastCubicControl = c2;
                    break;
                }

                case 'S':
                {
                    var c1 = previousWasCubic ? cursor * 2f - lastCubicControl : cursor;
                    var c2 = origin + reader.Point();
                    cursor = origin + reader.Point();
                    path._commands.Add(new Command(Op.Cubic, new[] { c1.x, c1.y, c2.x, c2.y, cursor.x, cursor.y }));
                    lastCubicControl = c2;
                    break;
                }

                case 'A':
                {
                    var rx = reader.Number();
                    var ry = reader.Number();
                    var rotation = reader.Number();
                    var largeArc = reader.Number();
                    var sweep = reader.Number();
                    cursor = origin + reader.Point();
                    path._commands.Add(new Command(Op.Arc, new[] { rx, ry, rotation, largeArc, sweep, cursor.x, cursor.y }));
                    break;
                }

                case 'Z':
                    cursor = start;
                    path._commands.Add(new Command(Op.Close, Array.Empty<float>()));
                    break;

                default:
                    ScriptedScreensVectorPlugin.Log?.LogWarning($"path: unknown command '{command}', stopping");
                    return path;
            }

            var upper = char.ToUpperInvariant(command);
            previousWasCubic = upper is 'C' or 'S';
            previousWasQuadratic = upper is 'Q' or 'T';
            previous = command;
        }

        return path;
    }

    private sealed class Reader
    {
        private readonly string _text;
        private int _at;

        internal Reader(string text)
        {
            _text = text;
        }

        internal char Peek()
        {
            return _text[_at];
        }

        internal void Advance()
        {
            _at++;
        }

        internal bool SkipSeparators()
        {
            while (_at < _text.Length && (char.IsWhiteSpace(_text[_at]) || _text[_at] == ','))
                _at++;

            return _at < _text.Length;
        }

        internal Vector2 Point()
        {
            return new Vector2(Number(), Number());
        }

        internal float Number()
        {
            SkipSeparators();

            var begin = _at;
            if (_at < _text.Length && (_text[_at] == '-' || _text[_at] == '+'))
                _at++;

            while (_at < _text.Length && (char.IsDigit(_text[_at]) || _text[_at] == '.'))
                _at++;

            // Exponent form, e.g. 1e-3.
            if (_at < _text.Length && (_text[_at] == 'e' || _text[_at] == 'E'))
            {
                _at++;
                if (_at < _text.Length && (_text[_at] == '-' || _text[_at] == '+'))
                    _at++;

                while (_at < _text.Length && char.IsDigit(_text[_at]))
                    _at++;
            }

            var span = _text[begin.._at];
            return float.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0f;
        }
    }
}
