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
    private Mesh? _mesh;

    /// <summary>Never raycast: the parent surface owns hit testing for the whole scene.</summary>
    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    /// <summary>Shows one slice of the surface's geometry.</summary>
    internal void Present(MeshBuilder builder, int slice)
    {
        _mesh ??= new Mesh { name = "VectorSlice" };

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

    protected override void OnDestroy()
    {
        if (_mesh != null)
        {
            Destroy(_mesh);
            _mesh = null;
        }

        base.OnDestroy();
    }
}
