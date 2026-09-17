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
    private static void Prefix(GameObject sourceRoot)
    {
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
