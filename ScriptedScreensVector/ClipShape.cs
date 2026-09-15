using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ScriptedScreensVector;

/// <summary>
/// The outline a label is masked to when its clip is not an axis-aligned rectangle.
/// </summary>
/// <remarks>
/// RectMask2D can only cut to a rectangle, so a label inside a rounded card, an ellipse or a
/// concave clip used to spill over the corners the artwork around it was cut to. This graphic
/// draws the clip outline into the stencil through a UGUI <see cref="Mask"/> on the same
/// object, with the graphic itself hidden. Costs the stencil pass and a draw call per masked
/// label, so it is only switched on for labels that need it.
/// </remarks>
internal sealed class ClipShape : MaskableGraphic
{
    // Serialised so the copy a screen capture takes still has its outline; an empty one
    // masks everything inside it away.
    [SerializeField] private List<Vector2> _points = new();

    /// <summary>Sets the outline, given in the parent's space, relative to this object's centre.</summary>
    internal void SetOutline(List<Vector2> outline, Vector2 centre)
    {
        var same = outline.Count == _points.Count;
        for (var i = 0; same && i < outline.Count; i++)
            same = (outline[i] - centre - _points[i]).sqrMagnitude < 1e-6f;

        if (same)
            return;

        _points.Clear();
        foreach (var point in outline)
            _points.Add(point - centre);

        SetVerticesDirty();
    }

    // A screen capture builds the surface from inside UGUI's graphic rebuild loop, and a label
    // touched there asks the registry to queue this graphic -- which it refuses, logging one
    // error per request: 252 for one capture of a test console. VectorSlice hit the same wall.
    // Inside a rebuild, do the queued work here and now instead; outside one, UGUI's normal path.
    private Mesh? _inlineMesh;

    public override void SetVerticesDirty()
    {
        if (!CanvasUpdateRegistry.IsRebuildingGraphics())
        {
            base.SetVerticesDirty();
            return;
        }

        _inlineMesh ??= new Mesh { name = "ClipShape" };
        using (var helper = new VertexHelper())
        {
            OnPopulateMesh(helper);
            helper.FillMesh(_inlineMesh);
        }

        canvasRenderer.SetMesh(_inlineMesh);
    }

    public override void SetMaterialDirty()
    {
        if (!CanvasUpdateRegistry.IsRebuildingGraphics())
        {
            base.SetMaterialDirty();
            return;
        }

        UpdateMaterial();
    }

    public override void SetLayoutDirty()
    {
        if (!CanvasUpdateRegistry.IsRebuildingGraphics())
            base.SetLayoutDirty();
    }

    protected override void OnDestroy()
    {
        if (_inlineMesh != null)
            Destroy(_inlineMesh);

        base.OnDestroy();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        if (_points.Count < 3 || !Triangulator.Triangulate(_points, null, out var vertices, out var indices))
            return;

        var white = new Color32(255, 255, 255, 255);
        foreach (var vertex in vertices)
            vh.AddVert(vertex, white, Vector2.zero);

        for (var t = 0; t + 2 < indices.Count; t += 3)
            vh.AddTriangle(indices[t], indices[t + 1], indices[t + 2]);
    }
}
