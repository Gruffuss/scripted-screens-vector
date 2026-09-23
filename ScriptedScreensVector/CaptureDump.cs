using System;
using System.Globalization;
using System.IO;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SS = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem;

namespace ScriptedScreensVector;

/// <summary>
/// With Diagnostics on, writes what a screen capture is about to copy: every object under the
/// surface, whether it is active, and what each label and graphic will draw.
/// </summary>
/// <remarks>
/// A capture that looks wrong while the console looks right has only this to explain it: the
/// copy is taken from this hierarchy, in this state. Files go to the temp folder
/// (`ScriptedScreensVector-captures`), never the mod folder, so nothing can be published.
/// </remarks>
[HarmonyPatch(typeof(SS), "CaptureRootToPng")]
internal static class CaptureDump
{
    /// <summary>
    /// Objects this capture switched on, innermost first, to be switched off again after.
    /// </summary>
    private static readonly System.Collections.Generic.List<GameObject> Woken = new();

    private static void Prefix(GameObject sourceRoot)
    {
        Wake(sourceRoot);

        if (!VectorConfig.Diagnostics || sourceRoot == null)
            return;

        try
        {
            var text = new StringBuilder();
            Walk(sourceRoot.transform, 0, text);

            var folder = Path.Combine(Path.GetTempPath(), "ScriptedScreensVector-captures");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "capture-" + DateTime.Now.ToString("HHmmss-fff", CultureInfo.InvariantCulture) + ".txt");
            File.WriteAllText(path, text.ToString());
            ScriptedScreensVectorPlugin.Log?.LogInfo($"capture dump: {path}");
        }
        catch (Exception ex)
        {
            ScriptedScreensVectorPlugin.Log?.LogWarning($"capture dump failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Switches on anything between the surface and the scene that is switched off, so the
    /// capture has something to copy.
    /// </summary>
    /// <remarks>
    /// A capture clones the surface root and renders the clone. An inactive object clones as
    /// an inactive object, so the camera photographs the world behind it and the picture
    /// comes back as terrain and sky — deterministically, for a console that looks perfectly
    /// correct in game. What switches it off is not established; the log line above names the
    /// object so the next capture answers it rather than another theory.
    ///
    /// It also has to be the **live** objects rather than the clone: an inactive Graphic gets
    /// no canvas update, so its mesh and its labels do not exist yet to be copied. Waking the
    /// chain and forcing one canvas update builds them, and <see cref="Restore"/> puts every
    /// object back. No frame is drawn in between — the capture renders its own camera into
    /// its own texture — so nothing of this reaches the player's screen.
    /// </remarks>
    private static void Wake(GameObject? sourceRoot)
    {
        Woken.Clear();
        if (sourceRoot == null || sourceRoot.activeInHierarchy)
            return;

        for (var node = sourceRoot.transform; node != null; node = node.parent)
        {
            if (!node.gameObject.activeSelf)
                Woken.Add(node.gameObject);
        }

        if (Woken.Count == 0)
            return;

        // Which ones were off, recorded BEFORE they are switched on -- afterwards there is
        // nothing left to see, which is how the first version of this line managed to say
        // "woke 1" without ever saying what.
        var names = new StringBuilder();
        foreach (var woken in Woken)
        {
            if (names.Length > 0)
                names.Append(", ");

            names.Append(woken.name);
        }

        // Outermost first: switching on a child of something still off does nothing useful.
        for (var i = Woken.Count - 1; i >= 0; i--)
            Woken[i].SetActive(true);

        // The graphics under it are dirty from waking; this is what builds their meshes and
        // labels, and the vector surfaces build inline because a capture is in progress.
        Canvas.ForceUpdateCanvases();

        if (!VectorConfig.Diagnostics)
            return;

        // The chain as well as the names: the surface root is always created active and
        // parented to the console's screen object, so where the switched-off one sits says
        // whose it is.
        var chain = new StringBuilder();
        for (var node = sourceRoot.transform; node != null; node = node.parent)
        {
            if (chain.Length > 0)
                chain.Append(" < ");

            chain.Append(node.gameObject.name);
        }

        ScriptedScreensVectorPlugin.Log?.LogInfo(
            $"vector capture: woke {Woken.Count} switched-off object(s) ({names}); the capture would have been blank. Chain: {chain}");
    }

    private static void Finalizer()
    {
        for (var i = 0; i < Woken.Count; i++)
        {
            if (Woken[i] != null)
                Woken[i].SetActive(false);
        }

        Woken.Clear();
    }

    private static void Walk(Transform node, int depth, StringBuilder into)
    {
        var go = node.gameObject;
        into.Append(' ', depth * 2).Append(go.name)
            .Append(go.activeInHierarchy ? "" : " [INACTIVE]");

        if (node is RectTransform rect)
            into.Append(FormattableString.Invariant($" rect={rect.anchoredPosition.x:F0},{rect.anchoredPosition.y:F0} {rect.rect.width:F0}x{rect.rect.height:F0}"));

        foreach (var component in go.GetComponents<Component>())
        {
            switch (component)
            {
                case TextMeshProUGUI label:
                    var shown = label.text ?? string.Empty;
                    if (shown.Length > 40) shown = shown.Substring(0, 40) + "...";
                    shown = shown.Replace("\n", "\\n", StringComparison.Ordinal);
                    into.Append(FormattableString.Invariant(
                        $" | TMP \"{shown}\" col={ColorUtility.ToHtmlStringRGBA(label.color)} en={label.enabled} mat={(label.fontSharedMaterial != null ? label.fontSharedMaterial.name : "null")} verts={label.textInfo?.meshInfo?[0].vertexCount ?? -1}"));
                    break;
                case VectorGraphic vector:
                    into.Append(FormattableString.Invariant($" | Vector scene={vector.SceneId} en={vector.enabled}"));
                    break;
                case Graphic graphic:
                    into.Append(FormattableString.Invariant($" | {graphic.GetType().Name} col={ColorUtility.ToHtmlStringRGBA(graphic.color)} en={graphic.enabled} mat={(graphic.material != null ? graphic.material.name : "null")}"));
                    break;
                case UnityEngine.UI.Mask mask:
                    into.Append(FormattableString.Invariant($" | Mask en={mask.enabled} show={mask.showMaskGraphic}"));
                    break;
                case RectMask2D:
                    into.Append(" | RectMask2D");
                    break;
                case GlyphColours:
                    into.Append(" | GlyphColours");
                    break;
            }
        }

        into.Append('\n');
        for (var i = 0; i < node.childCount; i++)
            Walk(node.GetChild(i), depth + 1, into);
    }
}
