using TMPro;
using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>
/// The glyph colours a tinted label last drew with, kept so a screen capture's copy shows them.
/// </summary>
/// <remarks>
/// Gradient fills, filters and masks reach text through TMP's pre-render hook, which TextLayer
/// subscribes on the labels it owns. A screen capture draws a COPY of the console, and a copy
/// carries serialised fields but no event subscriptions: it rebuilt its glyphs in the plain label
/// colour, so a grey, faded label came out green in every capture while the console was right.
///
/// The colours are serialised here, and only a copy -- which was never marked as owned --
/// subscribes and replays them. On the owning label this component is inert.
/// </remarks>
[RequireComponent(typeof(TextMeshProUGUI))]
internal sealed class GlyphColours : MonoBehaviour
{
    [SerializeField] private Color32[] _colours = System.Array.Empty<Color32>();

    /// <summary>The glyph positions too: a skewed label is sheared in the same hook.</summary>
    [SerializeField] private Vector3[] _vertices = System.Array.Empty<Vector3>();

    /// <summary>True on the label TextLayer made; a capture's copy is created with it false.</summary>
    [System.NonSerialized] internal bool Owned;

    /// <summary>Set around AddComponent, whose OnEnable runs before the caller can mark it.</summary>
    [System.ThreadStatic] internal static bool Creating;

    private TextMeshProUGUI? _label;

    internal void Store(Color32[] colours, Vector3[] vertices, int count)
    {
        if (_colours.Length != count)
            _colours = new Color32[count];

        if (_vertices.Length != count)
            _vertices = new Vector3[count];

        System.Array.Copy(colours, _colours, count);
        System.Array.Copy(vertices, _vertices, Mathf.Min(count, vertices.Length));
    }

    private void OnEnable()
    {
        if (Creating)
            Owned = true;

        if (Owned || _label != null)
            return;

        _label = GetComponent<TextMeshProUGUI>();
        _label.OnPreRenderText += Replay;
    }

    private void Replay(TMP_TextInfo info)
    {
        if (info == null || info.meshInfo.Length == 0)
            return;

        var mesh = info.meshInfo[0];

        if (mesh.colors32 == null || mesh.colors32.Length < _colours.Length || _colours.Length == 0)
            return;

        var count = Mathf.Min(mesh.vertexCount, _colours.Length);
        System.Array.Copy(_colours, mesh.colors32, count);

        if (mesh.vertices != null && mesh.vertices.Length >= count && _vertices.Length >= count)
            System.Array.Copy(_vertices, mesh.vertices, count);
    }
}
