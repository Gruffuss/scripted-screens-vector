using System.Collections.Generic;
using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>
/// Works out which labels have to be drawn underneath later geometry, and where to cut.
/// </summary>
/// <remarks>
/// Labels are TextMeshPro children, not part of the mesh, and UGUI draws children in sibling
/// order — so without this the whole text layer draws either entirely before or entirely
/// after the whole surface. "On top" and "underneath" are the only two states and they apply
/// to every label at once, which is why a box declared after a label cannot cover it.
///
/// The mesh is already cut into slices at shape boundaries for the 60,000-vertex limit, and
/// those cuts already fall between shapes — exactly the constraint a label needs. So the fix
/// is to force a cut where a label has to interleave, and parent the label between the two
/// slices.
///
/// **The whole design turns on cutting rarely**, because every cut is another mesh and
/// another draw call. Cutting at every label would take a page of thirty labelled tiles from
/// one draw call to sixty. So a cut is forced only where a label is genuinely covered by a
/// shape drawn after it, which on a real page is close to never: tiles do not overlap their
/// neighbours, and a label sits inside its own tile.
/// </remarks>
internal static class TextOrder
{
    /// <summary>
    /// Marks every label that a later shape overlaps, forcing a mesh cut before that shape.
    /// </summary>
    /// <remarks>
    /// Runs on the tessellation worker, between emitting the geometry and asking how many
    /// meshes it needs. Cost is labels x shapes-after in the worst case; see the note on the
    /// scan below for when that matters.
    /// </remarks>
    internal static void Assign(List<TextPlacement> placements, MeshBuilder builder)
    {
        var shapes = builder.ShapeCount;

        for (var i = 0; i < placements.Count; i++)
        {
            var placement = placements[i];
            var cutBefore = -1;

            // ponytail: linear scan of the shapes after this label. Worst case is
            // labels x shapes -- 100 labels over 800 shapes is ~80,000 rect tests, well
            // under a millisecond and off the frame. If a page ever carries thousands of
            // both, put a uniform grid over the shape bounds and query that instead.
            for (var shape = placement.ShapeIndex; shape < shapes; shape++)
            {
                if (!Overlaps(placement.Rect, builder.ShapeBounds(shape)))
                    continue;

                cutBefore = shape;
                break;
            }

            // Nothing covers it: leave it in the top layer, where every label is today. This
            // is the common case and it is what keeps the mesh in one piece.
            if (cutBefore < 0)
            {
                placement.SliceDepth = -1;
                placements[i] = placement;
                continue;
            }

            // The very first shape covers it. Slice 0 is the surface's own renderer, which
            // always draws before any child, so the geometry moves to slice 1 behind an empty
            // slice 0 and the label goes between them.
            if (cutBefore == 0)
            {
                builder.LeadingEmpty = true;
                placement.CutVertex = 0;
                placement.SliceDepth = 0;
                placements[i] = placement;
                continue;
            }

            builder.ForceCutBefore(cutBefore);
            placement.CutVertex = builder.ShapeStart(cutBefore);
            placement.SliceDepth = 0;
            placements[i] = placement;
        }
    }

    /// <summary>
    /// Turns each marked label's cut position into the number of slices drawn before it.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Assign"/> because it can only run once the slice boundaries
    /// are settled, and settling them is what <see cref="Assign"/>'s forced cuts feed into.
    /// </remarks>
    internal static void Resolve(List<TextPlacement> placements, MeshBuilder builder, int sliceCount)
    {
        for (var i = 0; i < placements.Count; i++)
        {
            var placement = placements[i];

            placement.SliceDepth = placement.SliceDepth < 0
                ? sliceCount
                : Mathf.Clamp(builder.SlicesBefore(placement.CutVertex), 1, sliceCount);

            placements[i] = placement;
        }
    }

    /// <summary>
    /// Do two canvas-space boxes share real area? Empty boxes, and boxes that merely touch,
    /// overlap nothing.
    /// </summary>
    /// <remarks>
    /// Layout puts boxes flush against each other constantly -- a button whose bottom is the
    /// next row's top -- and after the transform the shared edge differs in the last float
    /// digit. Counted as an overlap it forced a mesh cut for a label nothing covers; the
    /// "click me" label on the HTML test page was one. A quarter of a canvas unit is well under
    /// a pixel at any viewing distance, so nothing that genuinely covers text is missed.
    /// </remarks>
    internal static bool Overlaps(Rect a, Rect b)
    {
        const float Touch = 0.25f;

        if (b.width <= 0f || b.height <= 0f || a.width <= 0f || a.height <= 0f)
            return false;

        return a.xMin < b.xMax - Touch && b.xMin < a.xMax - Touch
            && a.yMin < b.yMax - Touch && b.yMin < a.yMax - Touch;
    }
}
