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
        var props = string.IsNullOrEmpty(source)
            ? element.Props
            : SceneText.ToProps(source!, ReadString(element.Props, "scene")) ?? element.Props;

        var scene = SceneParser.Parse(props);
        if (scene == null)
            return;

        // The fallback Image occupies the host's one Graphic slot. Make it invisible
        // rather than destroying it — ScriptedScreens re-adds it on every upsert.
        var fallback = host.GetComponent<Image>();
        if (fallback != null)
            fallback.color = Color.clear;

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
            if (string.Equals(previous.StructureText, source, StringComparison.Ordinal))
                graphic.SetData(previous.Snapshot());

            // The old host is only destroyed at the end of the frame, and a capture copies the
            // surface before that: both pages were drawn, the old one's labels over the new,
            // which showed as doubled lines and garbled text wherever the two differed.
            if (previous && previous.transform.parent != null && previous.transform.parent != host.transform)
                previous.transform.parent.gameObject.SetActive(false);
        }

        Scenes[key] = graphic;

        if (PendingData.TryGetValue(key, out var waiting))
        {
            graphic.SetData(waiting);
            PendingData.Remove(key);
        }

        ScriptedScreensVectorPlugin.Log?.LogInfo(
            $"scene \"{scene.Id}\": {scene.Root.Count} root node(s), animated={scene.UsesTime}");
    }

    private static void ApplyData(string key, SS.UiElement element, Dictionary<string, GameObject> roots)
    {
        var context = new EvalContext();
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
            graphic.PatchScene(element.Props);

        if (graphic != null)
            graphic.SetData(context);
        else if (PendingData.TryGetValue(key, out var waiting))
            waiting.MergeFrom(context);   // a later patch must not drop an earlier one
        else
            PendingData[key] = context;
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

    private static string? ReadString(SS.UiProp[] props, string key)
    {
        var value = Find(props, key);
        return value?.Type == SS.UiValueType.String ? value.Value.String : null;
    }

    private static float ReadNumber(SS.UiProp[] props, string key, float fallback)
    {
        var value = Find(props, key);
        return value?.Type == SS.UiValueType.Number ? value.Value.Number : fallback;
    }
}
