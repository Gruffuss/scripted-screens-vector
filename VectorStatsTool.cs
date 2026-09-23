using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace ScriptedScreensVector;

/// <summary>
/// Registers a <c>vector_stats</c> tool with StationeersLua's MCP server, when it is there.
/// </summary>
/// <remarks>
/// **Bound by reflection, deliberately.** A compile-time reference to `StationeersLua.dll`
/// would make this mod fail to load without it, and StationeersLua is a separate mod that a
/// player using ScriptedScreens may not have. Reflection makes the tool appear when it can
/// and cost nothing when it cannot, which is the behaviour the roadmap asked for.
///
/// It is also the answer to a question left open since the port feedback: how to get a
/// scene's problems in front of the person writing it. The chip log is still unreachable --
/// nothing in ScriptedScreens or StationeersLua exposes a sink for it -- but an MCP tool
/// reaches the same author through the editor they are already using, and can carry the
/// numbers as well as the errors.
/// </remarks>
internal static class VectorStatsTool
{
    private const string Schema =
        "{\"type\":\"object\",\"properties\":{\"scene\":{\"type\":\"string\"," +
        "\"description\":\"Optional scene id to report on; omit for all.\"}}}";

    internal static void TryRegister()
    {
        try
        {
            var registry = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("StationeersLua.LuaMcpRegistry", throwOnError: false))
                .FirstOrDefault(t => t != null);

            if (registry == null)
            {
                ScriptedScreensVectorPlugin.Log?.LogInfo(
                    "StationeersLua MCP registry not present; vector_stats not registered.");
                return;
            }

            var register = registry.GetMethod("RegisterTool",
                new[] { typeof(string), typeof(string), typeof(string), typeof(Func<JObject?, string>) });

            if (register == null)
            {
                ScriptedScreensVectorPlugin.Log?.LogWarning(
                    "LuaMcpRegistry.RegisterTool not found with the expected signature; vector_stats not registered.");
                return;
            }

            register.Invoke(null, new object[]
            {
                "vector_stats",
                "Per-surface state of the ScriptedScreens vector layer: rebuild rate and cost, "
                + "geometry counts, on-screen size, and any scene problems or unresolved data names.",
                Schema,
                new Func<JObject?, string>(Handle),
            });

            ScriptedScreensVectorPlugin.Log?.LogInfo("registered MCP tool vector_stats");

            RegisterDocs(registry);
        }
        catch (Exception ex)
        {
            // Never fatal: the mod's job is drawing, and the tool is a convenience.
            ScriptedScreensVectorPlugin.Log?.LogWarning($"vector_stats registration failed: {ex.Message}");
        }
    }

    private const string DocRoot = "stationeers://vector/";

    /// <summary>
    /// Publishes the shipped docs and examples as MCP resources, as ScriptedScreens does for its
    /// own: a `vector` search scope, README and REFERENCE one resource per `##` section, the
    /// changelog, Patterns.lua, an index, and everything under examples/.
    /// </summary>
    /// <remarks>
    /// Split by section because the search returns at most two hits per resource: a whole
    /// 58 KB reference would answer every question with its first two matches. Files are read
    /// from the mod folder when a resource is read, so the docs never go stale against the DLL.
    /// Each call is optional: an older StationeersLua without one of them loses just that part.
    /// </remarks>
    private static void RegisterDocs(Type registry)
    {
        var folder = System.IO.Path.GetDirectoryName(typeof(VectorStatsTool).Assembly.Location);
        if (string.IsNullOrEmpty(folder))
            return;

        var resource = registry.GetMethod("RegisterDocumentationResource",
            new[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(Func<string>) });
        if (resource == null)
        {
            ScriptedScreensVectorPlugin.Log?.LogInfo("StationeersLua has no documentation resources; vector docs not registered.");
            return;
        }

        registry.GetMethod("RegisterDocumentationSearchScope", new[] { typeof(string), typeof(string) })
            ?.Invoke(null, new object[] { "vector", DocRoot });

        var index = new StringBuilder("\nSearch everything with scope `vector`, or read these URIs:\n\n");
        var count = 0;

        void Add(string key, string name, string description, string mime, Func<string> content)
        {
            resource.Invoke(null, new object[] { DocRoot + key, name, description, mime, content });
            index.Append("- `").Append(DocRoot).Append(key).Append("` -- ").Append(name).Append('\n');
            count++;
        }

        foreach (var (file, key, title, blurb, deep) in new[]
        {
            ("README.md", "guide", "Guide", "the authoring guide: scene structure, animation, data, text, clicks, scrolling, traps, performance", false),
            ("REFERENCE.md", "reference", "Reference", "every node, attribute, defs declaration and expression function", true),
        })
        {
            var path = System.IO.Path.Combine(folder, file);
            foreach (var (line, label, slug) in Sections(path, deep))
            {
                var wanted = line;
                var subsection = deep;
                Add($"{key}/{slug}", $"Vector {title}: {label}", $"ScriptedScreens Vector {blurb} -- section \"{label}\".",
                    "text/markdown", () => Section(path, wanted, subsection));
            }
        }

        Add("changelog", "Vector Changelog", "ScriptedScreens Vector release history, newest first.",
            "text/markdown", () => Read(System.IO.Path.Combine(folder, "CHANGELOG.md")));
        Add("patterns", "Vector Patterns.lua", "Copy-paste Lua building blocks for the vector layer: tanks, mote fields, bars, dials, charts, alarm lamps, a worked console.",
            "text/plain", () => Read(System.IO.Path.Combine(folder, "Patterns.lua")));

        index.Append("\nRunnable examples, one idea each: `").Append(DocRoot).Append("examples/index`.\n");
        index.Append("Live scene state and problems: the `vector_stats` tool. A picture of a console: `capture_scripted_screen`.\n");
        // The index is the quick start with the resource list appended: the one page an editor
        // needs before writing a first scene, and the map to everything else.
        var list = index.ToString();
        var quickstart = System.IO.Path.Combine(folder, "QUICKSTART.md");
        resource.Invoke(null, new object[] { DocRoot + "index", "Vector quick start and documentation index",
            "Start here: what the ScriptedScreens vector element is, a working script, the rules that fail silently, how to check a scene, and every documentation URI.",
            "text/markdown", new Func<string>(() => Read(quickstart) + list) });

        registry.GetMethod("RegisterBundledExampleDocumentation", new[] { typeof(string), typeof(string), typeof(string) })
            ?.Invoke(null, new object[] { folder!, DocRoot + "examples/", "ScriptedScreens Vector" });

        ScriptedScreensVectorPlugin.Log?.LogInfo($"registered {count + 1} vector documentation resources and the examples");
    }

    private static string Read(string path)
    {
        try
        {
            return System.IO.File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            return $"Could not read {path}: {ex.Message}";
        }
    }

    private static bool IsHeading(string line, bool deep)
    {
        return line.StartsWith("## ", StringComparison.Ordinal)
               || (deep && line.StartsWith("### ", StringComparison.Ordinal));
    }

    /// <summary>
    /// The sections of a markdown file: text before the first heading ("introduction"), then one
    /// per `##` heading -- and per `###` when <paramref name="deep"/> -- outside code fences.
    /// Each carries its heading line as the key, a readable label and a URI-safe slug.
    /// </summary>
    internal static System.Collections.Generic.List<(string Line, string Label, string Slug)> Sections(string path, bool deep)
    {
        var found = new System.Collections.Generic.List<(string, string, string)>();
        var used = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal) { "introduction" };
        var fence = false;
        var parent = "";

        foreach (var line in Read(path).Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
                fence = !fence;

            if (fence || !IsHeading(line, deep))
            {
                if (found.Count == 0 && line.Trim().Length > 0)
                    found.Add(("", "Introduction", "introduction"));

                continue;
            }

            var text = line.TrimStart('#').Trim();
            var label = text;
            if (line.StartsWith("## ", StringComparison.Ordinal))
                parent = text;
            else if (parent.Length > 0)
                label = parent + " / " + text;

            var slugText = new StringBuilder();
            foreach (var c in label)
            {
                if (char.IsLetterOrDigit(c))
                    slugText.Append(char.ToLowerInvariant(c));
                else if (slugText.Length > 0 && slugText[slugText.Length - 1] != '-')
                    slugText.Append('-');
            }

            var slug = slugText.ToString().TrimEnd('-');
            var unique = slug.Length == 0 ? "section" : slug;
            for (var n = 2; !used.Add(unique); n++)
                unique = $"{slug}-{n}";

            found.Add((line, label, unique));
        }

        return found;
    }

    /// <summary>The section starting at heading line <paramref name="line"/> ("" for the introduction), up to the next heading.</summary>
    internal static string Section(string path, string line, bool deep)
    {
        var text = new StringBuilder();
        var fence = false;
        var inside = line.Length == 0;

        foreach (var current in Read(path).Split('\n'))
        {
            if (current.StartsWith("```", StringComparison.Ordinal))
                fence = !fence;

            if (!fence && IsHeading(current, deep))
            {
                if (inside)
                    break;

                inside = current == line;
            }

            if (inside)
                text.Append(current).Append('\n');
        }

        return text.Length > 0 ? text.ToString() : $"That section is no longer in {System.IO.Path.GetFileName(path)}.";
    }

    private static string Handle(JObject? arguments)
    {
        try
        {
            var wanted = arguments?["scene"]?.ToString();
            var report = new StringBuilder();
            var found = 0;

            // First line, always. Asked for after a console port could not tell from in here
            // which build was actually running -- and "is the fix I just made even loaded"
            // is the first question anyone has when a report looks wrong.
            report.AppendLine($"ScriptedScreensVector {PluginInfo.PLUGIN_VERSION}");

            foreach (var graphic in VectorElementPatch.LiveSurfaces())
            {
                if (graphic == null)
                    continue;

                if (!string.IsNullOrEmpty(wanted)
                    && !string.Equals(graphic.SceneId, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                found++;
                graphic.Describe(report);
            }

            if (found == 0)
            {
                report.AppendLine("No vector surfaces are live. Load a console running a vector scene.");
                return report.ToString();
            }

            return report.ToString();
        }
        catch (Exception ex)
        {
            return "vector_stats failed: " + ex.Message;
        }
    }

    /// <summary>Formats a float the same way everywhere in the report.</summary>
    internal static string N(double value, int places = 2)
    {
        return value.ToString("0." + new string('#', places), CultureInfo.InvariantCulture);
    }
}
