using UnityEngine;
using UnityEngine.UI;

namespace ScriptedScreensVector;

/// <summary>
/// One more mesh for a surface that does not fit in a single one.
/// </summary>
/// <remarks>
/// **A surface's mesh cannot exceed about 65,000 vertices**, and that is UGUI's limit rather
/// than a setting: its own <c>VertexHelper</c> throws at 65,000, because the
/// <c>CanvasRenderer</c> batcher works in 16-bit indices throughout. Asking a mesh for 32-bit
/// indices and handing it to a CanvasRenderer anyway takes the game down natively, with
/// nothing in the log — tried, and reverted.
///
/// The supported answer is more meshes, and it is what TextMeshPro does for exactly the same
/// reason: <c>TMP_SubMeshUI</c> is a MaskableGraphic child spawned when one mesh will not do.
/// This is the same idea with nothing in it but presentation.
///
/// Draw order is sibling order, and slices are created in emission order, so a later slice
/// draws over an earlier one — which is what the single mesh did with its triangle order.
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
internal sealed class VectorSlice : MaskableGraphic
{
    /// <summary>Serialised so a capture clone keeps it; see VectorGraphic for why.</summary>
    [SerializeField] private Mesh? _mesh;

    /// <summary>True on the instance that made the mesh, false on a capture clone.</summary>
    private bool _ownsMesh;

    /// <summary>Never raycast: the parent surface owns hit testing for the whole scene.</summary>
    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    /// <summary>Shows one slice of the surface's geometry.</summary>
    internal void Present(MeshBuilder builder, int slice)
    {
        if (_mesh == null)
        {
            _mesh = new Mesh { name = "VectorSlice" };
            _ownsMesh = true;
        }

        builder.Apply(_mesh, slice);
        canvasRenderer.SetMesh(_mesh);
    }

    /// <summary>
    /// Presents the current mesh rather than rebuilding.
    /// </summary>
    /// <remarks>
    /// UGUI calls this on any dirtying — a rect change, a material change, a canvas rebuild.
    /// The surface decides when geometry is rebuilt; a slice only ever shows what it was
    /// given, so this must not throw the mesh away.
    /// </remarks>
    protected override void UpdateGeometry()
    {
        if (_mesh != null)
            canvasRenderer.SetMesh(_mesh);
    }

    // A slice created during a screen capture is created INSIDE UGUI's graphic rebuild loop:
    // the capture's inline build runs from UpdateGeometry, and draw-order text now splits most
    // text-bearing scenes into several slices. Enabling a Graphic there asks the registry to
    // queue it, the registry refuses, and Unity logs an error per request -- 66 of them for one
    // capture of a seven-mesh page.
    //
    // Nothing is lost by it (the capture was checked and is complete), but a slice never needs
    // the queue anyway: its mesh is handed over directly by Present. So while a rebuild is in
    // progress, do the one job the queue would have done -- set the material -- here and now.
    // Outside a rebuild, UGUI's normal path is left alone.
    //
    // Deferring slice creation to the next Update would silence it too, and break the capture:
    // the clone is taken inside the same call, so it would have no slices past the first.

    public override void SetVerticesDirty()
    {
        if (!CanvasUpdateRegistry.IsRebuildingGraphics())
        {
            base.SetVerticesDirty();
            return;
        }

        if (_mesh != null)
            canvasRenderer.SetMesh(_mesh);
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
        // A slice stretches over its surface and has no layout of its own to recompute.
        if (!CanvasUpdateRegistry.IsRebuildingGraphics())
            base.SetLayoutDirty();
    }

    protected override void OnDestroy()
    {
        if (_mesh != null && _ownsMesh)
        {
            Destroy(_mesh);
            _mesh = null;
        }

        base.OnDestroy();
    }
}
