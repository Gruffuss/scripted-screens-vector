using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>Where a `T` node ended up, in canvas space, after the tree walk.</summary>
/// <remarks>
/// Text cannot be part of the mesh. TMP builds its own geometry on its own GameObject and is
/// main-thread only, while tessellation runs on a worker — so the walk **collects** these and
/// <see cref="VectorGraphic"/> turns them into TMP children when the job lands.
///
/// That split is the whole design of text here, and it has two consequences worth knowing:
/// text updates at the rebuild rate rather than instantly, and a text node cannot be clipped
/// by the geometric clipper, which reshapes contours before triangulation and has nothing to
/// say about a child object.
/// </remarks>
internal struct TextPlacement
{
    internal string Text;
    internal Rect Rect;          // canvas space, already transformed
    internal float Size;
    internal Color Colour;
    internal int Align;          // 0 left, 1 centre, 2 right
    internal int VAlign;         // 0 top, 1 middle, 2 bottom
    internal string? Font;
    internal bool Bold;
    internal float CharSpacing;
    internal int Fit;            // 0 none, 1 ellipsis, 2 shrink
    internal float MinSize;
    internal float Rotation;     // degrees, from the group transform
    internal Rect? ClipRect;     // canvas-space bounds of the enclosing clip, if any
}

/// <summary>Fit modes for <c>T</c>.</summary>
internal static class TextFit
{
    internal const int None = 0;
    internal const int Ellipsis = 1;
    internal const int Shrink = 2;

    internal static int Parse(string? value)
    {
        return value?.ToUpperInvariant() switch
        {
            "ELLIPSIS" => Ellipsis,
            "SHRINK" => Shrink,
            _ => None,
        };
    }
}

internal static class TextAlign
{
    internal static int Horizontal(string? value)
    {
        return value?.ToUpperInvariant() switch
        {
            "CENTER" or "CENTRE" => 1,
            "RIGHT" => 2,
            _ => 0,
        };
    }

    internal static int Vertical(string? value)
    {
        return value?.ToUpperInvariant() switch
        {
            "MIDDLE" or "CENTER" or "CENTRE" => 1,
            "BOTTOM" => 2,
            _ => 0,
        };
    }
}


/// <summary>A clickable node's bounds, in canvas space, in draw order.</summary>
/// <remarks>
/// Bounds rather than the tessellated shape. The walk knows each node's outline, but keeping
/// every contour alive past the rebuild to hit-test against later is a lot of memory for a
/// test that is only ever run on a click — and a row, a button and a tile, which is what
/// carries `click`, are rectangles whose bounds are their shape.
/// </remarks>
internal struct HitRegion
{
    internal string Id;
    internal Rect Rect;
}
