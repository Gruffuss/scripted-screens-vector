using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ScriptedScreensVector;

/// <summary>
/// Turns the text placements a rebuild found into TMP children, and keeps them.
/// </summary>
/// <remarks>
/// Main thread only, driven when a tessellation job lands.
///
/// **Objects are pooled by index, not rebuilt.** The nth placement reuses the nth object.
/// Destroying and recreating them would give up TMP's own caching and put a per-frame
/// allocation where the point of the exercise was removing one.
///
/// Pooling by index is safe because <see cref="Show"/> reassigns **every** property of the
/// label it takes, so an object that served a different placement last rebuild carries
/// nothing forward. That is load bearing: without it, a node that stops being emitted would
/// shift every later placement onto an object still configured for something else. The font
/// was the one property that leaked, and it is reset explicitly below.
///
/// It is also why this is worth having at all: a ScriptedScreens `label` element costs about
/// 300 Lua instructions to declare and has to be re-declared to change its text, against a
/// string in a data payload here.
/// </remarks>
internal sealed class TextLayer
{
    private readonly List<TextMeshProUGUI> _pool = new();

    /// <summary>The face a fresh label starts with, restored when a placement names none.</summary>
    private TMP_FontAsset? _defaultFont;
    private readonly List<RectTransform> _masks = new();
    private readonly Transform _parent;

    internal TextLayer(Transform parent)
    {
        _parent = parent;
    }

    /// <summary>Applies a rebuild's placements, reusing objects and hiding the surplus.</summary>
    internal void Apply(List<TextPlacement> placements)
    {
        for (var i = 0; i < placements.Count; i++)
            Show(i, placements[i]);

        // Surplus is disabled rather than destroyed: a scene that alternates between two
        // pages would otherwise churn objects every time it switched.
        for (var i = placements.Count; i < _pool.Count; i++)
        {
            if (_pool[i] != null)
                _pool[i].gameObject.SetActive(false);
        }
    }

    private void Show(int index, TextPlacement placement)
    {
        while (_pool.Count <= index)
            Create();

        var label = _pool[index];
        var mask = _masks[index];

        if (label == null || mask == null)
            return;

        // The mask carries the clip; the label sits inside it at the placement's rect. With
        // no clip the mask is made to match the label exactly, so it never cuts anything.
        var clip = placement.ClipRect ?? placement.Rect;

        Place(mask, clip);
        Place(label.rectTransform, placement.Rect);

        // The label's rect is expressed relative to the mask, since it is its child.
        label.rectTransform.anchoredPosition =
            new Vector2(placement.Rect.center.x - clip.center.x,
                        placement.Rect.center.y - clip.center.y);

        var masking = mask.GetComponent<RectMask2D>();
        if (masking != null)
            masking.enabled = placement.ClipRect.HasValue;

        label.gameObject.SetActive(true);
        label.text = placement.Text;
        label.color = placement.Colour;
        label.fontSize = Mathf.Max(1f, placement.Size);
        label.characterSpacing = placement.CharSpacing;
        label.fontStyle = placement.Bold ? FontStyles.Bold : FontStyles.Normal;
        label.alignment = Alignment(placement);

        // Single line unless the node asked otherwise. TMP wraps by default, and a label
        // that silently becomes two lines moves its own text off the baseline the scene
        // placed it on -- which looks like a bug in the console, not a long string.
        label.enableWordWrapping = placement.Wrap;

        // TMP's lineSpacing is a PERCENTAGE OFFSET from the font's natural line height, not
        // a multiplier, so `lh = 1` is 0 and `lh = 1.4` is +40. Written as a CSS-style
        // multiple because that is what an author porting a page already has in hand.
        label.lineSpacing = placement.LineHeight > 0.01f
            ? (placement.LineHeight - 1f) * 100f
            : 0f;

        // `ellipsis` and `shrink` are TMP's own overflow modes, so the fitting is done by the
        // text engine that knows the glyph metrics rather than guessed at here.
        switch (placement.Fit)
        {
            case TextFit.Ellipsis:
                label.enableAutoSizing = false;
                label.overflowMode = TextOverflowModes.Ellipsis;
                break;

            case TextFit.Shrink:
                label.enableAutoSizing = true;
                label.fontSizeMin = Mathf.Max(1f, placement.MinSize);
                label.fontSizeMax = Mathf.Max(1f, placement.Size);
                label.overflowMode = TextOverflowModes.Overflow;
                break;

            default:
                label.enableAutoSizing = false;
                label.overflowMode = TextOverflowModes.Overflow;
                break;
        }

        label.rectTransform.localRotation = Quaternion.Euler(0f, 0f, placement.Rotation);

        // Reset rather than "leave it alone", because the pool is by index: a placement with
        // no `font` must not inherit the face of whatever used this object last rebuild. This
        // was a real leak whenever placement ORDER changed -- a repeat whose count grew, a
        // scroll container gaining a row -- not only when a node disappeared.
        if (!string.IsNullOrEmpty(placement.Font))
            ApplyFont(label, placement.Font!);
        else if (_defaultFont != null && label.font != _defaultFont)
            label.font = _defaultFont;
    }

    /// <summary>
    /// Resolves a family name through TMP's own registry, which is what the companion fonts
    /// mod populates. An unknown name leaves the current face rather than blanking the label.
    /// </summary>
    private static void ApplyFont(TextMeshProUGUI label, string family)
    {
        if (label.font != null && label.font.name == family)
            return;

        if (MaterialReferenceManager.TryGetFontAsset(TMP_TextUtilities.GetSimpleHashCode(family), out var asset))
            label.font = asset;
    }

    private static void Place(RectTransform rect, Rect where)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(where.width, where.height);
        rect.anchoredPosition = where.center;
    }

    private static TextAlignmentOptions Alignment(TextPlacement placement)
    {
        return (placement.VAlign, placement.Align) switch
        {
            (0, 0) => TextAlignmentOptions.TopLeft,
            (0, 1) => TextAlignmentOptions.Top,
            (0, 2) => TextAlignmentOptions.TopRight,
            (1, 0) => TextAlignmentOptions.Left,
            (1, 1) => TextAlignmentOptions.Center,
            (1, 2) => TextAlignmentOptions.Right,
            (2, 0) => TextAlignmentOptions.BottomLeft,
            (2, 1) => TextAlignmentOptions.Bottom,
            _ => TextAlignmentOptions.BottomRight,
        };
    }

    private void Create()
    {
        var maskObject = new GameObject("VecTextClip", typeof(RectTransform), typeof(RectMask2D));
        var mask = maskObject.GetComponent<RectTransform>();
        mask.SetParent(_parent, worldPositionStays: false);
        maskObject.GetComponent<RectMask2D>().enabled = false;

        var labelObject = new GameObject("VecText", typeof(RectTransform), typeof(TextMeshProUGUI));
        var label = labelObject.GetComponent<TextMeshProUGUI>();
        label.rectTransform.SetParent(mask, worldPositionStays: false);
        label.raycastTarget = false;
        label.richText = true;

        _defaultFont ??= label.font;

        _masks.Add(mask);
        _pool.Add(label);
    }

    internal void Destroy()
    {
        foreach (var mask in _masks)
        {
            if (mask != null)
                UnityEngine.Object.Destroy(mask.gameObject);
        }

        _masks.Clear();
        _pool.Clear();
    }
}
