using System.Text;
using SS = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem;

namespace ScriptedScreensVector.Tests;

/// <summary>
/// The text scene format against the table form it has to match.
/// </summary>
/// <remarks>
/// These compare <b>props</b>, not tessellated scenes, and that is the stronger test rather
/// than a weaker one: <c>SceneText</c> is a front end that hands its output to the same
/// <c>SceneParser</c> the table form uses, so identical props mean identical scenes by
/// construction. Comparing rendered output would test the parser twice and the front end
/// once.
///
/// It also has to be this way round. <c>SceneParser</c> reaches
/// <c>ColorUtility.TryParseHtmlString</c>, a native ECall that throws <c>SecurityException</c>
/// outside the player, so a full text-to-scene comparison cannot run headless at all.
/// </remarks>
internal static class SceneTextTests
{
    private const string Nl = "\n";

    /// <summary>Renders a prop tree as text so two can be compared and a diff read.</summary>
    private static string Dump(SS.UiProp[]? props, int depth = 0)
    {
        if (props == null)
            return "<null>";

        var sb = new StringBuilder();
        var pad = new string(' ', depth * 2);

        foreach (var prop in props)
        {
            sb.Append(pad).Append(prop.Key).Append(" = ").Append(Dump(prop.Value, depth)).Append('\n');
        }

        return sb.ToString();
    }

    private static string Dump(SS.UiValue value, int depth)
    {
        switch (value.Type)
        {
            case SS.UiValueType.Number:
                return value.Number.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

            case SS.UiValueType.String:
                return "\"" + value.String + "\"";

            case SS.UiValueType.Array:
            {
                var sb = new StringBuilder("[");
                for (var i = 0; i < (value.Array?.Length ?? 0); i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(Dump(value.Array![i], depth));
                }

                return sb.Append(']').ToString();
            }

            case SS.UiValueType.Map:
                return "{\n" + Dump(value.Map, depth + 1) + new string(' ', depth * 2) + "}";

            default:
                return "nil";
        }
    }

    private static SS.UiValue Num(float v) => new() { Type = SS.UiValueType.Number, Number = v };
    private static SS.UiValue Str(string v) => new() { Type = SS.UiValueType.String, String = v };
    private static SS.UiValue Arr(params SS.UiValue[] v) => new() { Type = SS.UiValueType.Array, Array = v };
    private static SS.UiValue Map(params SS.UiProp[] v) => new() { Type = SS.UiValueType.Map, Map = v };
    private static SS.UiProp P(string k, SS.UiValue v) => new() { Key = k, Value = v };

    private static void Same(TestRun run, string name, SS.UiProp[]? actual, SS.UiProp[] expected)
    {
        var a = Dump(actual);
        var b = Dump(expected);
        run.Check($"scenetext: {name}", a == b ? 1d : 0d, 1d, 0.001d, 0, 0);

        if (a != b)
        {
            System.Console.WriteLine("  --- got ---\n" + a);
            System.Console.WriteLine("  --- want ---\n" + b);
        }
    }

    /// <summary>A scene header, one shape, and a group with a child.</summary>
    internal static void MatchesTableForm(TestRun run)
    {
        const string text = @"
# a comment, ignored
SCENE w=200 h=200 fit=stretch
R x=10 y=10 w=44 h=60 rx=8 f=#0B1622
G t=[28,78] {
    C cx=0 cy=0 rx=3 ry=3 f=#5FD9A8
}
";
        var actual = SceneText.ToProps(text, "demo");

        var rect = Map(P("op", Str("R")), P("x", Num(10)), P("y", Num(10)),
                       P("w", Num(44)), P("h", Num(60)), P("rx", Num(8)), P("f", Str("#0B1622")));

        var circle = Map(P("op", Str("C")), P("cx", Num(0)), P("cy", Num(0)),
                         P("rx", Num(3)), P("ry", Num(3)), P("f", Str("#5FD9A8")));

        var group = Map(P("op", Str("G")), P("t", Arr(Num(28), Num(78))),
                        P("c", Arr(circle)));

        var expected = new[]
        {
            P("scene", Str("demo")),
            P("w", Num(200)), P("h", Num(200)), P("fit", Str("stretch")),
            P("root", Arr(rect, group)),
        };

        Same(run, "header, shape, nested group", actual, expected);
    }

    /// <summary>defs, nested arrays, and the value forms that must stay strings.</summary>
    internal static void DefsAndValueForms(TestRun run)
    {
        const string text = @"
DEFS {
    GL id=liquid units=bbox x1=0 y1=0 x2=0 y2=1 stops=[[0,#5FD9A8],[1,#2E8B6E]]
}
YS n=12 x==6+i*3 y==64-$fill*52 y2=72 f=@liquid fo=0 fo2=0.62
";
        var actual = SceneText.ToProps(text, null);

        var band = Map(P("op", Str("YS")), P("n", Num(12)),
                       P("x", Str("=6+i*3")), P("y", Str("=64-$fill*52")), P("y2", Num(72)),
                       P("f", Str("@liquid")), P("fo", Num(0)), P("fo2", Num(0.62f)));

        var stops = Arr(Arr(Num(0), Str("#5FD9A8")), Arr(Num(1), Str("#2E8B6E")));

        var gradient = Map(P("op", Str("GL")), P("id", Str("liquid")), P("units", Str("bbox")),
                           P("x1", Num(0)), P("y1", Num(0)), P("x2", Num(0)), P("y2", Num(1)),
                           P("stops", stops));

        var expected = new[]
        {
            P("root", Arr(band)),
            P("defs", Arr(gradient)),
        };

        Same(run, "defs, nested arrays, expressions", actual, expected);
    }

    /// <summary>
    /// The value-typing rule, which is where a parser like this usually goes wrong.
    /// </summary>
    /// <remarks>
    /// A colour, an <c>@ref</c> and an <c>=expression</c> all have to survive as strings, and
    /// a bare word is a flag. Turning <c>#0B1622</c> into a number, or an expression into
    /// anything but a string, fails silently later as a shape that draws in the wrong place.
    /// </remarks>
    internal static void ValueTyping(TestRun run)
    {
        var actual = SceneText.ToProps("R x=-3.5 f=#fff s=@ramp fo==tri(t) lod j=\"a b\"", null);

        var shape = Map(P("op", Str("R")), P("x", Num(-3.5f)), P("f", Str("#fff")),
                        P("s", Str("@ramp")), P("fo", Str("=tri(t)")),
                        P("lod", Num(1)), P("j", Str("a b")));

        var expected = new[] { P("root", Arr(shape)) };

        Same(run, "numbers, colours, refs, expressions, flags, quotes", actual, expected);
    }

    /// <summary>Malformed input reports and returns null rather than throwing.</summary>
    internal static void MalformedIsReported(TestRun run)
    {
        var unterminated = SceneText.ToProps("R p=[1,2", null);
        run.Check("scenetext: unterminated array returns null", unterminated == null ? 1d : 0d, 1d, 0.001d, 0, 0);

        var empty = SceneText.ToProps("   \n # nothing \n ", null);
        run.Check("scenetext: blank source returns null", empty == null ? 1d : 0d, 1d, 0.001d, 0, 0);
    }

    /// <summary>`SC` needs no special case in the text format, and this proves it.</summary>
    /// <remarks>
    /// The grammar is op plus key/value pairs plus braces, so a new container op costs the
    /// front end nothing. Worth pinning anyway: `ch` is a key nothing else uses, and a
    /// container whose children were dropped would render as an empty box rather than as an
    /// error.
    /// </remarks>
    internal static void ScrollContainerParses(TestRun run)
    {
        var props = SceneText.ToProps(
            "SC id=list x=4 y=20 w=192 h=120 ch=480 rx=6 {" + Nl +
            "    RP n=12 { R x=8 y=\"=i*40\" w=176 h=34 f=#12202F }" + Nl +
            "}", null);

        var text = Dump(props);

        run.Check("scenetext: SC carries ch", text.Contains("ch = 480") ? 1d : 0d, 1d, 0.001d, 0, 0);
        run.Check("scenetext: SC keeps its id", text.Contains("\"list\"") ? 1d : 0d, 1d, 0.001d, 0, 0);
        run.Check("scenetext: SC keeps its children", text.Contains("\"RP\"") ? 1d : 0d, 1d, 0.001d, 0, 0);
    }

    /// <summary>The scene text that ships in `examples/13-src.lua`, parsed here.</summary>
    /// <remarks>
    /// A documented example that does not parse is worse than no example, and this format is
    /// exactly where that happens: values are read to whitespace, a bare word is a flag, and
    /// nested arrays sit inside a Lua long string. Pinning the shipped text means the file
    /// cannot rot quietly.
    /// </remarks>
    internal static void ShippedExampleParses(TestRun run)
    {
        var props = SceneText.ToProps(string.Join(Nl, new[]
        {
            "# viewbox and fit live on SCENE, not on the element props",
            "SCENE w=100 h=200 fit=stretch",
            "",
            "DEFS {",
            "    GL id=liquid units=bbox x1=0 y1=0 x2=0 y2=1 stops=[[0,#5FD9A8],[1,#2E8B6E]]",
            "    CP id=tank { R x=20 y=20 w=60 h=160 rx=10 }",
            "}",
            "",
            "R x=20 y=20 w=60 h=160 rx=10 f=#0B1622",
            "",
            "G clip=tank {",
            "    YS n=24 x==18+i*2.8 y==180-clamp($fill,0,1)*152+3*sin(i*0.5+t*1.6) y2=184 f=@liquid fea_edge=0",
            "}",
            "",
            "# Paint defaults on the group, since this format has no map syntax for `style`.",
            "G f=none s_=#1E3247 sw=2 fea=0 {",
            "    R x=20 y=20 w=60 h=160 rx=10",
            "    R x=26 y=26 w=48 h=6 rx=3",
            "}",
            "",
            "T x=20 y=190 w=60 h=10 text=TEXT size=7 cspace=2 align=center f=#3A5570",
        }), null);

        var text = Dump(props);

        run.Check("scenetext: example parses", props != null ? 1d : 0d, 1d, 0.001d, 0, 0);
        run.Check("scenetext: SCENE sets the viewbox", text.Contains("w = 100") ? 1d : 0d, 1d, 0.001d, 0, 0);
        run.Check("scenetext: nested stops survive",
            text.Contains("[[0, \"#5FD9A8\"], [1, \"#2E8B6E\"]]") ? 1d : 0d, 1d, 0.001d, 0, 0);
        run.Check("scenetext: the band expression is whole",
            text.Contains("=180-clamp($fill,0,1)*152+3*sin(i*0.5+t*1.6)") ? 1d : 0d, 1d, 0.001d, 0, 0);
        run.Check("scenetext: text=TEXT is a value, not a flag",
            text.Contains("text = \"TEXT\"") ? 1d : 0d, 1d, 0.001d, 0, 0);

        // The group's paint defaults have to arrive as ordinary keys on the G, which is the
        // whole mechanism: there is no map syntax here for `style` to use.
        run.Check("scenetext: group carries s_ as a default",
            text.Contains("s_ = \"#1E3247\"") ? 1d : 0d, 1d, 0.001d, 0, 0);
        run.Check("scenetext: bare fea=0 is a value, not a flag",
            text.Contains("fea = 0") ? 1d : 0d, 1d, 0.001d, 0, 0);
    }
}
