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
        }
        catch (Exception ex)
        {
            // Never fatal: the mod's job is drawing, and the tool is a convenience.
            ScriptedScreensVectorPlugin.Log?.LogWarning($"vector_stats registration failed: {ex.Message}");
        }
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
