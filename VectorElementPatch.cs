using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using SS = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem;
using Motherboard = Assets.Scripts.Objects.Items.Motherboard;
using CartridgeIntegratedCircuitLua = ScriptedScreens.CartridgeIntegratedCircuitLua;
using ProgrammableVisorGlasses = ScriptedScreens.ProgrammableVisorGlasses;

namespace ScriptedScreensVector;

/// <summary>
/// The entire integration with ScriptedScreens: one postfix that claims elements of type
/// <c>vector</c> and routes their props to a <see cref="VectorGraphic"/>.
/// </summary>
/// <remarks>
/// ScriptedScreens already does the hard part for unrecognised element types. Verified
/// against 0.9.5.0: it creates <c>Ui:&lt;id&gt;</c> with a RectTransform and CanvasRenderer,
/// parents it to the surface root, sets the layer, applies the rect, and caches it in
/// <c>state.SurfaceElementRoots[surface]</c> so it survives later upserts. It then falls to
/// a final <c>else</c> that adds an <see cref="Image"/> tinted from <c>style.bg</c>.
///
/// Its stale-component cleanup destroys exactly one thing — <c>CircleGraphic</c>, when the
/// type is not <c>circle</c> — so <see cref="VectorGraphic"/> is never touched.
///
/// Elements come in two flavours, distinguished by which prop they carry (spec §1):
/// a <c>root</c> prop makes it a structure element, a <c>data</c> prop makes it the data
/// element paired to it by <c>scene</c> id. They are separate elements because
/// <c>set_props</c> merges and then upserts the whole element, so sharing one element would
/// resend the structure on every data tick.
/// </remarks>
[HarmonyPatch(typeof(SS), "ApplyElementInternal")]
internal static class VectorElementPatch
{
    /// <summary>Element type this mod claims. Verified free against the 27 built-in types.</summary>
    internal const string ElementType = "vector";

    private const string SurfaceChildName = "VectorSurface";

    /// <summary>Structure surfaces by "surface/sceneId", so data elements can find them.</summary>
    private static readonly Dictionary<string, VectorGraphic> Scenes = new(StringComparer.Ordinal);

    /// <summary>Data that arrived before its structure. Spec §1: either order must work.</summary>
    private static readonly Dictionary<string, EvalContext> PendingData = new(StringComparer.Ordinal);

    /// <summary>
    /// A `nodes` patch whose structure has not arrived yet, kept beside its data in
    /// <see cref="PendingData"/> and applied when the graphic appears.
    /// </summary>
    private static readonly Dictionary<string, SS.UiProp[]> PendingPatches = new(StringComparer.Ordinal);

    private static void Postfix(Motherboard? board, CartridgeIntegratedCircuitLua? cartridge,
        ProgrammableVisorGlasses? visor, SS.BoardState state, string surface, SS.UiElement element)
    {
        try
        {
            if (state == null || string.IsNullOrEmpty(surface) || element == null)
                return;

            if (!string.Equals(element.Type, ElementType, StringComparison.OrdinalIgnoreCase))
                return;

            if (!state.SurfaceElementRoots.TryGetValue(surface, out var roots) || roots == null)
                return;

            if (!roots.TryGetValue(element.Id, out var host) || host == null)
                return;

            ReportProbe(element);

            // The fallback Image occupies the host's one Graphic slot. Make it invisible rather
            // than destroying it -- ScriptedScreens re-adds it on every upsert. Done for every
            // vector element: a data-only one used to keep ScriptedScreens' default grey, which
            // showed as a grey square wherever the data element was placed on screen.
            var fallback = host.GetComponent<Image>();
            if (fallback != null)
                fallback.color = Color.clear;

            var sceneId = ReadString(element.Props, "scene") ?? element.Id;

            // Keyed by BOARD as well as surface and scene. Every console names its
            // surface "main", so two consoles running the same script both hashed to
            // "main/gas": whichever registered last owned the entry and the other's
            // data went to the wrong graphic. Presented as one console's clipping
            // breaking when the other started, and swapping on restart.
            var key = RuntimeHelpers.GetHashCode(state).ToString(CultureInfo.InvariantCulture)
                      + "/" + surface + "/" + sceneId;

            if (HasProp(element.Props, "data") || HasProp(element.Props, "nodes"))
            {
                ApplyData(key, element, roots);
                return;
            }

            if (HasProp(element.Props, "root") || HasProp(element.Props, "src"))
                ApplyStructure(key, host, element, board, cartridge, visor, surface);
        }
        catch (Exception ex)
        {
            ScriptedScreensVectorPlugin.Log?.LogError($"vector element failed: {ex}");
        }
    }

    /// <summary>Live surfaces, for the stats tool.</summary>
    /// <summary>True while ScriptedScreens is capturing a surface to a PNG.</summary>
    /// <remarks>
    /// The capture is synchronous and destructive: it calls <c>RebuildSurfaceFromModel</c>,
    /// which recreates every host on the surface, then <c>Canvas.ForceUpdateCanvases</c>, then
    /// clones the tree and renders the clone -- all inside one call, with no frame in between.
    ///
    /// A surface rebuilt that way has never tessellated, because tessellation is dispatched to
    /// a worker and landed on a later frame. So the clone was of an empty graphic and every
    /// capture of a vector console came back blank. Knowing a capture is running is what lets
    /// the geometry be built inline instead, which is legal precisely because the tessellator
    /// contains no Unity call.
    /// </remarks>
    internal static bool Capturing { get; private set; }

    [HarmonyPatch(typeof(SS), "TryCaptureSurfaceShared")]
    private static class CapturePatch
    {
        private static void Prefix() => Capturing = true;

        private static void Postfix() => Capturing = false;
    }

    internal static IEnumerable<VectorGraphic> LiveSurfaces()
    {
        return Scenes.Values;
    }

    private static void ApplyStructure(string key, GameObject host, SS.UiElement element,
        Motherboard? board, CartridgeIntegratedCircuitLua? cartridge,
        ProgrammableVisorGlasses? visor, string surface)
    {
        // `src` is the text form. It is converted to the same props the table form
        // arrives as and parsed by the same code, so the two cannot diverge.
        var source = ReadString(element.Props, "src");

        var graphic = EnsureSurface(host);

        // ScriptedScreens' own forwarder, on a child with no graphic of its own so Unity
        // never routes a pointer event straight to it -- see VectorGraphic.SetClickTarget.
        var clicks = EnsureForwarder(host);
        if (clicks != null)
        {
            if (board != null)
                clicks.Configure(board, surface, element.Id, "click", string.Empty);
            else if (visor != null)
                clicks.Configure(visor, surface, element.Id, "click", string.Empty);
            else if (cartridge != null)
                clicks.Configure(cartridge, surface, element.Id, "click", string.Empty);

            graphic.SetClickTarget(clicks, element.Id);
        }

        // Re-sending the SAME text to the SAME graphic is nothing to do: identical text is
        // identical geometry, so the scene already standing is this one. Installing it anyway
        // reset `t` and dropped every `nodes` patch, so a script that re-upserts its structure
        // each tick -- `set_props` re-upserts the whole element, which makes that an easy shape
        // to write -- held every animation at t=0 for ever. Parse and rebuild are skipped with
        // it, which is the larger half of the saving.
        //
        // Only the text form: the table form arrives as a tree with nothing cheap to compare,
        // so it keeps installing on every upsert.
        if (!string.IsNullOrEmpty(source)
            && string.Equals(graphic.StructureText, source, StringComparison.Ordinal))
            return;

        // An empty or blank `src` is not a refusal -- a script that leaves it off, or blanks
        // it, means the table form on the same element -- so it goes straight to the props.
        // Anything else goes to the parser, and `null` back from the parser IS a refusal. The
        // test is on WHITESPACE because `ToProps` answers null for a blank string before it
        // clears `Rejected`, so reading the field after one of those gave some earlier scene's
        // reason.
        var props = string.IsNullOrWhiteSpace(source)
            ? element.Props
            : SceneText.ToProps(source!, ReadString(element.Props, "scene"));

        // A refused `src` used to be papered over by the `root` beside it: the fallback drew
        // the table scene, which parses fine and reports nothing, so an author editing `src`
        // got the other picture, no border, no entry in vector_stats -- only the log line.
        var refused = props == null
            ? SceneText.Rejected ?? "the scene text could not be read"
            : null;

        var scene = SceneParser.Parse(props ?? element.Props);

        // A scene that will not parse must SAY so. Returning here left the console blank with
        // nothing on screen and no entry in vector_stats -- only a log line, which is the last
        // place an author looks. It now draws the same magenta border any other scene problem
        // does, and the tool names the line that stopped it.
        if (scene == null)
        {
            scene = new VecScene { Id = ReadString(element.Props, "scene") ?? "?" };
            scene.Problem(refused ?? "the scene could not be read");
        }
        else if (refused != null)
        {
            scene.Problem($"src: {refused}");
        }

        graphic.SetScene(scene);
        graphic.StructureText = source;

        // A rebuild -- which is what a capture does -- makes a NEW graphic with an empty
        // context, and the data element ScriptedScreens replays afterwards is one payload, not
        // the merged state. Under `keep = 1` that payload is a patch, so everything the script
        // sent on an earlier tick and has no reason to resend simply vanishes: the console kept
        // its artwork and lost every label's text.
        if (Scenes.TryGetValue(key, out var previous)
            && !ReferenceEquals(previous, null)
            && !ReferenceEquals(previous, graphic))
        {
            // Only for the SAME structure. A scene that fills named slots can come back as a
            // different structure with the same slot names meaning other things -- a page
            // rebuilt from scratch -- and the old values then landed in the wrong slots: a
            // capture showed stale text in other fonts, and dark blocks.
            //
            // The text comparison alone could not say that for a TABLE-form scene, where both
            // sides are null and `string.Equals(null, null)` is true: any table-form page
            // pushed with a scene id the board had used before inherited the previous run's
            // data and clock. `Scenes` is never pruned, so "before" could be an hour ago.
            //
            // `Capturing` is what makes the table form answerable. The carry-over exists for
            // the rebuild a capture does, where the structure is identical by construction;
            // outside a capture a table-form scene has nothing to compare, so it starts clean,
            // which is what an author re-pushing a page means by it.
            var sameStructure = Capturing
                                || (!string.IsNullOrEmpty(source)
                                    && string.Equals(previous.StructureText, source, StringComparison.Ordinal));

            if (sameStructure)
            {
                graphic.SetData(previous.Snapshot());

                // And the same clock: the rebuilt graphic is the live console from now on, and
                // starting its `t` afresh made every capture restart every animation on screen.
                graphic.ContinueClockFrom(previous);
            }

            // The old host is only destroyed at the end of the frame, and a capture copies the
            // surface before that: both pages were drawn, the old one's labels over the new,
            // which showed as doubled lines and garbled text wherever the two differed.
            if (previous && previous.transform.parent != null && previous.transform.parent != host.transform)
                previous.transform.parent.gameObject.SetActive(false);
        }

        Scenes[key] = graphic;

        // The patch first, then the data: that is the order they would have arrived in, and a
        // patch rewrites the node a later datum fills.
        if (PendingPatches.TryGetValue(key, out var patch))
        {
            graphic.PatchScene(patch);
            PendingPatches.Remove(key);
        }

        if (PendingData.TryGetValue(key, out var waiting))
        {
            graphic.SetData(waiting);
            PendingData.Remove(key);
        }

        ScriptedScreensVectorPlugin.Log?.LogInfo(
            $"scene \"{scene.Id}\": {scene.Root.Count} root node(s), animated={scene.UsesTime}");
    }

    /// <summary>
    /// One payload buffer, reused. Reading a payload into it allocates nothing.
    /// </summary>
    /// <remarks>
    /// A fresh context per payload is eight dictionaries, a set and their buckets -- 3.1 KB
    /// measured, which is nothing twice a second and 1.6 MB/s at the rate a compiled page
    /// sends. It is handed over and replaced only when something KEEPS it: a graphic parking
    /// it behind a running job, or a payload waiting for a structure that has not arrived.
    /// </remarks>
    private static EvalContext _payloadBuffer = new();

    private static void ApplyData(string key, SS.UiElement element, Dictionary<string, GameObject> roots)
    {
        var context = _payloadBuffer;
        SceneParser.ReadData(element.Props, context);

        // A graphic whose host has left the surface is being replaced: a rebuild cleared the
        // hosts and is re-applying elements. Data arriving now belongs to the graphic its
        // structure is about to create, so it waits for it instead of going to the old one,
        // which is how a page's full payload sent ahead of its new structure was lost.
        Scenes.TryGetValue(key, out var graphic);
        if (graphic != null && (!graphic || graphic.transform.parent == null
                                || !roots.ContainsValue(graphic.transform.parent.gameObject)))
        {
            graphic = null;
        }

        // A geometry patch is applied to the live scene; it is not evaluator data.
        if (graphic != null)
        {
            graphic.PatchScene(element.Props);
        }
        else if (HasProp(element.Props, "nodes"))
        {
            // No graphic yet: the structure has not arrived, or the surface is mid-rebuild.
            // The payload's DATA waits below, and the `nodes` patch used to be dropped here --
            // so a script that sent its geometry patch ahead of the structure lost it with
            // nothing reported. It waits with the data and is applied in the same order.
            //
            // MERGED, not assigned. This used to overwrite, which dropped the earlier of two
            // patches queued before the structure arrived -- the same fault the data path
            // below was written to avoid, in the branch directly above it. Two patches in one
            // tick is ordinary: the host appends one op per upsert and never coalesces them,
            // and it flushes at the end of every Execute whether or not a commit was called.
            PendingPatches[key] = PendingPatches.TryGetValue(key, out var queued)
                ? MergeProps(queued, element.Props)
                : element.Props;
        }

        // A payload carrying NO `data` prop must not touch evaluator data at all. `ReadData`
        // clears every map and returns early when the prop is absent, so an empty context used
        // to arrive here and REPLACE the scene's data wholesale. That one behaviour caused two
        // separate faults, both measured on a console on 2026-10-06:
        //   - two elements naming ONE scene: `ApplyData` keys by scene name, so a second element
        //     carrying only `nodes` reached the same graphic and wiped the first's data. A page
        //     declaring `data` on one element and `nodes` on another left `$n` UNRESOLVED, with
        //     nothing reported.
        //   - `since($name)` restarted after a `nodes` patch: the wipe dropped `ArrivedAt`, so
        //     the next real payload re-stamped every name. That is why a consumer had moved its
        //     geometry patches onto a separate element in the first place -- working around this.
        // `data = {}` still means "clear": that is a payload that HAS the prop. Only its absence
        // is inert now.
        if (HasProp(element.Props, "data"))
        {
            if (graphic != null)
            {
                if (graphic.SetData(context))
                    _payloadBuffer = new EvalContext();
            }
            else if (PendingData.TryGetValue(key, out var waiting))
            {
                waiting.MergeFrom(context);   // a later patch must not drop an earlier one
            }
            else
            {
                PendingData[key] = context;
                _payloadBuffer = new EvalContext();
            }
        }
    }

    /// <summary>
    /// The click forwarder, parked on a child that draws nothing.
    /// </summary>
    /// <remarks>
    /// On the host it would receive pointer events itself as well as through the graphic, and
    /// its 0.25 s debounce would drop whichever arrived second — so the node id resolved by
    /// hit test would be a coin flip against a stale one. A child with no Graphic is never a
    /// raycast target, so it only ever fires when we call it.
    /// </remarks>
    private static SS.UiPointerDownForwarder? EnsureForwarder(GameObject host)
    {
        var existing = host.transform.Find("VecClicks");
        if (existing != null)
            return existing.GetComponent<SS.UiPointerDownForwarder>();

        var carrier = new GameObject("VecClicks", typeof(RectTransform));
        carrier.transform.SetParent(host.transform, worldPositionStays: false);

        return carrier.AddComponent<SS.UiPointerDownForwarder>();
    }

    private static VectorGraphic EnsureSurface(GameObject host)
    {
        var existing = host.transform.Find(SurfaceChildName);
        GameObject child;

        if (existing == null)
        {
            child = new GameObject(SurfaceChildName, typeof(RectTransform), typeof(CanvasRenderer))
            {
                layer = host.layer,
            };

            var rect = child.GetComponent<RectTransform>();
            rect.SetParent(host.transform, worldPositionStays: false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
        else
        {
            child = existing.gameObject;
        }

        var graphic = child.GetComponent<VectorGraphic>();
        if (graphic == null)
            graphic = child.AddComponent<VectorGraphic>();

        graphic.raycastTarget = false;
        return graphic;
    }

    /// <summary>
    /// Payload instrumentation, kept from the ceiling measurement: reports what survived
    /// the trip so a truncation is visible rather than looking like a rendering bug.
    /// </summary>
    private static void ReportProbe(SS.UiElement element)
    {
        var probe = Find(element.Props, "probe");
        if (probe == null)
            return;

        var nodes = 0;
        var chars = 0;
        var depth = Measure(probe.Value, 1, ref nodes, ref chars);

        var claimed = (int)ReadNumber(element.Props, "n", -1f);
        ScriptedScreensVectorPlugin.Log?.LogInfo(
            $"probe \"{element.Id}\": claimed={claimed} nodes={nodes} depth={depth} strChars={chars}");
    }

    private static int Measure(SS.UiValue value, int depth, ref int nodes, ref int chars)
    {
        nodes++;
        var deepest = depth;

        switch (value.Type)
        {
            case SS.UiValueType.String:
                chars += value.String?.Length ?? 0;
                break;

            case SS.UiValueType.Array when value.Array != null:
                foreach (var item in value.Array)
                    deepest = Math.Max(deepest, Measure(item, depth + 1, ref nodes, ref chars));
                break;

            case SS.UiValueType.Map when value.Map != null:
                foreach (var entry in value.Map)
                {
                    chars += entry.Key?.Length ?? 0;
                    deepest = Math.Max(deepest, Measure(entry.Value, depth + 1, ref nodes, ref chars));
                }

                break;
        }

        return deepest;
    }

    /// <summary>
    /// Two prop sets as one, later winning per key -- except `nodes`, which merges deeper.
    /// </summary>
    /// <remarks>
    /// `nodes` is a single property holding a map of node id to that node's patch, so taking the
    /// later one whole would drop every node the earlier patch touched. That was the fault this
    /// merge was added to fix, still present one level down.
    /// </remarks>
    private static SS.UiProp[] MergeProps(SS.UiProp[] earlier, SS.UiProp[] later)
    {
        if (earlier == null || earlier.Length == 0)
            return later;

        if (later == null || later.Length == 0)
            return earlier;

        var byKey = new Dictionary<string, SS.UiProp>(earlier.Length + later.Length,
            StringComparer.OrdinalIgnoreCase);

        foreach (var prop in earlier)
        {
            if (!string.IsNullOrEmpty(prop.Key))
                byKey[prop.Key] = prop;
        }

        foreach (var prop in later)
        {
            if (string.IsNullOrEmpty(prop.Key))
                continue;

            if (string.Equals(prop.Key, "nodes", StringComparison.OrdinalIgnoreCase)
                && byKey.TryGetValue(prop.Key, out var queued))
            {
                byKey[prop.Key] = new SS.UiProp
                {
                    Key = prop.Key,
                    Value = MergeNodePatches(queued.Value, prop.Value),
                };
                continue;
            }

            byKey[prop.Key] = prop;
        }

        var merged = new SS.UiProp[byKey.Count];
        byKey.Values.CopyTo(merged, 0);
        return merged;
    }

    /// <summary>Two `nodes` patches as one: per node id, then per property within a node.</summary>
    /// <remarks>
    /// A node patched by both keeps the earlier patch's other properties, because that is what it
    /// would have kept had the two patches arrived in turn -- a live patch merges into the node's
    /// current properties rather than replacing them.
    /// </remarks>
    private static SS.UiValue MergeNodePatches(SS.UiValue earlier, SS.UiValue later)
    {
        if (earlier.Type != SS.UiValueType.Map || earlier.Map == null)
            return later;

        if (later.Type != SS.UiValueType.Map || later.Map == null)
            return earlier;

        var byId = new Dictionary<string, SS.UiProp>(earlier.Map.Length + later.Map.Length,
            StringComparer.Ordinal);

        foreach (var node in earlier.Map)
        {
            if (!string.IsNullOrEmpty(node.Key))
                byId[node.Key] = node;
        }

        foreach (var node in later.Map)
        {
            if (string.IsNullOrEmpty(node.Key))
                continue;

            if (byId.TryGetValue(node.Key, out var before)
                && before.Value.Type == SS.UiValueType.Map && before.Value.Map != null
                && node.Value.Type == SS.UiValueType.Map && node.Value.Map != null)
            {
                byId[node.Key] = new SS.UiProp
                {
                    Key = node.Key,
                    Value = new SS.UiValue
                    {
                        Type = SS.UiValueType.Map,
                        Map = MergeProps(before.Value.Map, node.Value.Map),
                    },
                };
                continue;
            }

            byId[node.Key] = node;
        }

        var merged = new SS.UiProp[byId.Count];
        byId.Values.CopyTo(merged, 0);
        return new SS.UiValue { Type = SS.UiValueType.Map, Map = merged };
    }

    private static SS.UiValue? Find(SS.UiProp[] props, string key)
    {
        if (props == null)
            return null;

        foreach (var prop in props)
        {
            if (string.Equals(prop.Key, key, StringComparison.OrdinalIgnoreCase))
                return prop.Value;
        }

        return null;
    }

    private static bool HasProp(SS.UiProp[] props, string key)
    {
        return Find(props, key) != null;
    }

    /// <summary>
    /// `scene` and `src`, both of which are free text -- so a number is text too.
    /// </summary>
    /// <remarks>
    /// `scene = 1` is an ordinary thing for a script to write and arrived as a NUMBER, which
    /// this answered null for: the element took the host's own id instead, and the data element
    /// naming the same scene the same way never found it. Printed invariantly, as the scene
    /// parser prints a number used as text.
    /// </remarks>
    private static string? ReadString(SS.UiProp[] props, string key)
    {
        var value = Find(props, key);
        return value?.Type switch
        {
            SS.UiValueType.String => value.Value.String,
            SS.UiValueType.Number => value.Value.Number.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture),
            _ => null,
        };
    }

    /// <summary>A number, where a Lua boolean is 1 or 0 as it is everywhere else.</summary>
    private static float ReadNumber(SS.UiProp[] props, string key, float fallback)
    {
        var value = Find(props, key);
        return value?.Type switch
        {
            SS.UiValueType.Number => value.Value.Number,
            SS.UiValueType.Bool => value.Value.Bool ? 1f : 0f,
            _ => fallback,
        };
    }
}
