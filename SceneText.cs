using System;
using System.Collections.Generic;
using SS = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem;

namespace ScriptedScreensVector;

/// <summary>
/// Parses the text scene format into the same prop structures the table form arrives as.
/// </summary>
/// <remarks>
/// **This is a front end, not a second scene builder.** It produces
/// <c>SS.UiProp[]</c> / <c>SS.UiValue</c> and hands them to <see cref="SceneParser.Parse"/>,
/// so the text form and the table form go through one code path and cannot drift apart.
/// Writing a parallel builder would have meant every future attribute being added twice, and
/// the round-trip test proving only that both copies had been updated.
///
/// The point of the format is the Lua instruction budget. Building a scene as nested tables
/// costs roughly a dozen instructions per node, and a page of a few hundred nodes is a
/// serious fraction of the 50,000 per tick. A string literal is already in the compiled
/// chunk: building it costs **nothing**.
///
/// Grammar, one node per line:
///
/// <code>
/// # comments run to end of line
/// SCENE w=200 h=200 fit=stretch
/// DEFS {
///     GL id=liquid units=bbox x1=0 y1=0 x2=0 y2=1 stops=[[0,#5FD9A8],[1,#2E8B6E]]
///     CP id=tank { R x=10 y=10 w=44 h=60 rx=8 }
/// }
/// R x=10 y=10 w=44 h=60 rx=8 f=#0B1622
/// G t=[28,78] {
///     RP n=9 { R x=-0.6 y=-16 w=1.2 h=4 f=#5FD9A8 }
/// }
/// </code>
///
/// Values: a number is a number; <c>[a,b,c]</c> is an array and nests; anything else is a
/// string, which covers colours, <c>@gradient</c> refs, enum words and <c>=expressions</c>.
/// Double quotes wrap a string containing spaces, commas or brackets.
/// </remarks>
internal static class SceneText
{
    /// <summary>
    /// Parsed scenes, keyed by the source text. A structure element resent unchanged --
    /// which is most of them, since the point is that structure rarely changes -- reparses
    /// nothing.
    /// </summary>
    private static readonly Dictionary<string, SS.UiProp[]> Cache = new(StringComparer.Ordinal);

    private const int MaxCache = 32;

    /// <summary>Reused while unescaping one quoted value. Parsing is single-threaded.</summary>
    private static readonly System.Text.StringBuilder QuotedScratch = new();

    /// <summary>
    /// Why the last <see cref="ToProps"/> call failed, for the surface to show and the stats tool
    /// to report. A scene that will not parse used to draw nothing with only a log line to say
    /// so, which reads as a dead console.
    /// </summary>
    internal static string? Rejected;

    /// <summary>Converts scene text into the props the table form would have produced.</summary>
    internal static SS.UiProp[]? ToProps(string text, string? sceneId)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        Rejected = null;

        if (Cache.TryGetValue(text, out var cached))
            return cached;

        List<Node> nodes;
        try
        {
            nodes = ParseBlock(new Reader(text), 0);
        }
        catch (FormatException ex)
        {
            ScriptedScreensVectorPlugin.Log?.LogWarning($"vector scene \"{sceneId ?? "?"}\" did not parse: {ex.Message}");
            Rejected = ex.Message;
            return null;
        }

        if (nodes.Count == 0)
        {
            ScriptedScreensVectorPlugin.Log?.LogWarning($"vector scene \"{sceneId ?? "?"}\" did not parse: it has no nodes");
            Rejected = "the scene text has no nodes";
            return null;
        }

        var props = new List<SS.UiProp>();
        var root = new List<SS.UiValue>();
        var defs = new List<SS.UiValue>();

        if (!string.IsNullOrEmpty(sceneId))
            props.Add(Prop("scene", Str(sceneId!)));

        foreach (var node in nodes)
        {
            switch (node.Op)
            {
                // SCENE carries the viewbox and fit; it is not a drawable node.
                case "SCENE":
                    foreach (var pair in node.Props)
                        props.Add(Prop(pair.Key, pair.Value));
                    break;

                case "DEFS":
                    foreach (var child in node.Children)
                        defs.Add(ToValue(child));
                    break;

                default:
                    root.Add(ToValue(node));
                    break;
            }
        }

        props.Add(Prop("root", Arr(root)));
        if (defs.Count > 0)
            props.Add(Prop("defs", Arr(defs)));

        var result = props.ToArray();

        // Bounded, and cleared wholesale rather than evicted by age: a scene that changes
        // every tick is not what this cache is for, and tracking use order to serve that
        // case would cost more than the parse it saves.
        if (Cache.Count >= MaxCache)
            Cache.Clear();

        Cache[text] = result;
        return result;
    }

    // ---------------------------------------------------------------- tree

    private sealed class Node
    {
        internal string Op = "";
        internal readonly List<KeyValuePair<string, SS.UiValue>> Props = new();
        internal readonly List<Node> Children = new();
    }

    private static SS.UiValue ToValue(Node node)
    {
        var props = new List<SS.UiProp> { Prop("op", Str(node.Op)) };

        foreach (var pair in node.Props)
            props.Add(Prop(pair.Key, pair.Value));

        if (node.Children.Count > 0)
        {
            var children = new List<SS.UiValue>(node.Children.Count);
            foreach (var child in node.Children)
                children.Add(ToValue(child));

            props.Add(Prop("c", Arr(children)));
        }

        return new SS.UiValue { Type = SS.UiValueType.Map, Map = props.ToArray() };
    }

    // ---------------------------------------------------------------- reader

    private sealed class Reader
    {
        internal readonly string Text;
        internal int At;

        internal Reader(string text)
        {
            Text = text;
        }
    }

    private const int MaxDepth = 32;

    /// <summary>
    /// A parse failure that says WHERE. "expected an op" on its own sent a session hunting
    /// through a whole page for an unquoted `data:` URL; the line and its text name it at once.
    /// </summary>
    private static FormatException Fail(Reader r, string what)
    {
        var line = 1;
        var start = 0;
        for (var i = 0; i < r.At && i < r.Text.Length; i++)
        {
            if (r.Text[i] != '\n')
                continue;

            line++;
            start = i + 1;
        }

        var end = r.Text.IndexOf('\n', start);
        var text = (end < 0 ? r.Text.Substring(start) : r.Text.Substring(start, end - start)).Trim();
        if (text.Length > 90)
            text = text.Substring(0, 90) + "...";

        return new FormatException($"{what}, line {line}: {text}");
    }

    private static List<Node> ParseBlock(Reader r, int depth)
    {
        if (depth > MaxDepth)
            throw Fail(r, "nesting too deep");

        var nodes = new List<Node>();

        while (true)
        {
            SkipTrivia(r);
            if (r.At >= r.Text.Length)
                break;

            if (r.Text[r.At] == '}')
            {
                r.At++;
                break;
            }

            nodes.Add(ParseNode(r, depth));
        }

        return nodes;
    }

    private static Node ParseNode(Reader r, int depth)
    {
        var node = new Node { Op = ReadToken(r) };
        if (node.Op.Length == 0)
            throw Fail(r, "expected an op (a value holding spaces, `=` or `;` -- a data: URL, say -- must be quoted)");

        while (true)
        {
            SkipSpaces(r);

            if (r.At >= r.Text.Length)
                return node;

            var c = r.Text[r.At];

            if (c == '\n' || c == '\r' || c == '#')
                return node;

            if (c == '{')
            {
                r.At++;
                node.Children.AddRange(ParseBlock(r, depth + 1));
                return node;
            }

            if (c == '}')
                return node;

            var key = ReadToken(r);
            if (key.Length == 0)
                return node;

            SkipSpaces(r);
            if (r.At < r.Text.Length && r.Text[r.At] == '=')
            {
                r.At++;
                node.Props.Add(new KeyValuePair<string, SS.UiValue>(key, ReadValue(r)));
            }
            else
            {
                // A bare word is a flag: `lod` means `lod=1`.
                node.Props.Add(new KeyValuePair<string, SS.UiValue>(key, Num(1f)));
            }
        }
    }

    /// <summary>
    /// How deep `[` may nest. Each level is a stack frame in ReadValue, and a stack overflow
    /// is not catchable in .NET -- it ends the process rather than the parse, so a scene of
    /// `[[[[[[...` would take the game down with it. Far past anything an author writes: the
    /// deepest array in any example here is two.
    /// </summary>
    private const int MaxArrayDepth = 32;

    private static SS.UiValue ReadValue(Reader r, int depth = 0)
    {
        if (r.At < r.Text.Length && r.Text[r.At] == '[')
        {
            if (depth >= MaxArrayDepth)
                throw Fail(r, $"arrays may not nest more than {MaxArrayDepth} deep");

            r.At++;
            var items = new List<SS.UiValue>();

            while (true)
            {
                SkipSpaces(r);
                if (r.At >= r.Text.Length)
                    throw Fail(r, "unterminated [");

                if (r.Text[r.At] == ']')
                {
                    r.At++;
                    break;
                }

                if (r.Text[r.At] == ',')
                {
                    r.At++;
                    continue;
                }

                // EVERY pass must consume something. SkipSpaces steps over space and tab only, so
                // a newline inside `[ ]` -- or a `{` or `}` -- is not skipped, is not `]`, is not
                // `,`, and ReadValue returns an empty token without advancing. The loop then
                // appended empty values until it ran out of memory. `R p=[1,` newline `2]` is an
                // ordinary thing to write, so this was reachable by hand.
                //
                // An array must stay on one line; saying so is better than guessing, because
                // letting a line break through would make a missing `]` swallow the rest of the
                // scene instead of being reported here.
                var before = r.At;
                items.Add(ReadValue(r, depth + 1));

                if (r.At == before)
                    throw Fail(r, "an array must be closed with ] on the same line");
            }

            return Arr(items);
        }

        // An expression is read to whitespace and nothing else. The ordinary token rule
        // breaks on ',' '[' ']', and expressions are full of all three -- `clamp($x,0,1)`,
        // `$name[i]`. Consequence, and the one rule the format imposes: an unquoted
        // expression cannot contain a space. Quote it if it needs one.
        if (r.At < r.Text.Length && r.Text[r.At] == '=')
            return Str(ReadUntilSpace(r));

        var token = ReadToken(r);

        // A leading '=' marks an expression and must stay a string; so must a colour, an
        // @gradient reference and every enum word. Only a clean number becomes a number.
        if (token.Length > 0 && token[0] != '=' && token[0] != '#' && token[0] != '@'
            && float.TryParse(token, System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            return Num(number);
        }

        return Str(token);
    }

    /// <summary>Reads to the next whitespace, keeping every structural character.</summary>
    private static string ReadUntilSpace(Reader r)
    {
        var from = r.At;
        while (r.At < r.Text.Length && !char.IsWhiteSpace(r.Text[r.At]))
            r.At++;

        return r.Text.Substring(from, r.At - from);
    }

    /// <summary>Reads one token: quoted, or up to whitespace or a structural character.</summary>
    private static string ReadToken(Reader r)
    {
        SkipSpaces(r);

        if (r.At < r.Text.Length && r.Text[r.At] == '"')
        {
            r.At++;

            // Backslash escapes, so a quoted value can contain the quote that delimits it.
            // Without them a page rendering an inch mark or a JSON fragment had to substitute
            // some other character and hope nobody looked closely.
            //
            // `\"` and `\\` are the two that must exist. `\n` and `\t` come along because a
            // caller who has escapes will try them, and a literal "n" where a newline was
            // meant is worse than either. Anything else keeps its backslash, so an unknown
            // escape is visible rather than silently eaten.
            var quoted = QuotedScratch;
            quoted.Length = 0;

            while (r.At < r.Text.Length && r.Text[r.At] != '"')
            {
                var c = r.Text[r.At];

                if (c == '\\' && r.At + 1 < r.Text.Length)
                {
                    r.At++;
                    var escaped = r.Text[r.At];
                    r.At++;

                    quoted.Append(escaped switch
                    {
                        '"' => "\"",
                        '\\' => "\\",
                        'n' => "\n",
                        't' => "\t",
                        _ => "\\" + escaped,
                    });

                    continue;
                }

                quoted.Append(c);
                r.At++;
            }

            if (r.At < r.Text.Length)
                r.At++;

            return quoted.ToString();
        }

        var from = r.At;
        while (r.At < r.Text.Length)
        {
            var c = r.Text[r.At];
            if (char.IsWhiteSpace(c) || c == '=' || c == '{' || c == '}'
                || c == '[' || c == ']' || c == ',' || c == '#' && r.At != from)
            {
                break;
            }

            r.At++;
        }

        return r.Text.Substring(from, r.At - from);
    }

    private static void SkipSpaces(Reader r)
    {
        while (r.At < r.Text.Length && (r.Text[r.At] == ' ' || r.Text[r.At] == '\t'))
            r.At++;
    }

    private static void SkipTrivia(Reader r)
    {
        while (r.At < r.Text.Length)
        {
            var c = r.Text[r.At];

            if (char.IsWhiteSpace(c))
            {
                r.At++;
                continue;
            }

            if (c == '#')
            {
                while (r.At < r.Text.Length && r.Text[r.At] != '\n')
                    r.At++;

                continue;
            }

            return;
        }
    }

    // ---------------------------------------------------------------- helpers

    private static SS.UiProp Prop(string key, SS.UiValue value)
    {
        return new SS.UiProp { Key = key, Value = value };
    }

    private static SS.UiValue Num(float v)
    {
        return new SS.UiValue { Type = SS.UiValueType.Number, Number = v };
    }

    private static SS.UiValue Str(string v)
    {
        return new SS.UiValue { Type = SS.UiValueType.String, String = v };
    }

    private static SS.UiValue Arr(List<SS.UiValue> items)
    {
        return new SS.UiValue { Type = SS.UiValueType.Array, Array = items.ToArray() };
    }
}
