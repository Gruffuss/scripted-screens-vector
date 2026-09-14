using System.Collections.Generic;
using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>
/// Accumulates geometry into plain lists and uploads it as a <see cref="Mesh"/>.
/// </summary>
/// <remarks>
/// Replaces UGUI's <c>VertexHelper</c>, which was measured at ~259 ns per vertex in game —
/// essentially the entire rebuild cost once expressions, triangulation and allocation had
/// been dealt with. It writes **seven** parallel lists per vertex (position, colour, uv0-uv3,
/// normal, tangent) and the UI shader consumes three of them, so four sevenths of that work
/// was thrown away.
///
/// Here it is three lists plus indices, appended directly and handed to
/// <c>Mesh.SetVertices</c> in bulk rather than element by element.
///
/// The lists persist between rebuilds and are only cleared, so steady-state allocation is
/// zero once a scene reaches its high-water mark.
/// </remarks>
internal sealed class MeshBuilder
{
    private readonly List<Vector3> _positions = new(4096);
    private readonly List<Color32> _colours = new(4096);
    private readonly List<Vector2> _uv0 = new(4096);
    private readonly List<int> _indices = new(8192);

    /// <summary>Vertex counts at which the geometry may be cut into a separate mesh.</summary>
    /// <remarks>
    /// A triangle's three vertices must end up in the same mesh, so a cut can only fall
    /// between shapes. The tessellator marks a boundary after each one it finishes; the marks
    /// are candidates, not commitments, and <see cref="Slices"/> picks from them.
    ///
    /// Within a shape this is not merely convention: a feather ring indexes the fill's
    /// vertices and each ring of a shadow indexes the previous ring's, so those triangles
    /// genuinely reach backwards -- but never past the start of their own shape.
    /// </remarks>
    private readonly List<int> _cuts = new(256);

    /// <summary>Index-list position at each of those boundaries, kept in step with <see cref="_cuts"/>.</summary>
    /// <remarks>
    /// A shape writes its vertices and then its triangles, and never references anything
    /// outside itself, so a shape boundary cuts BOTH lists at once. Recording the index
    /// position too is what makes a slice a contiguous range of each -- without it, finding a
    /// slice's triangles meant scanning every index in the surface, once per slice.
    /// </remarks>
    private readonly List<int> _cutIndices = new(256);

    /// <summary>Where each slice starts, rebuilt on demand. Index i covers [_slices[i], _slices[i+1]).</summary>
    private readonly List<int> _slices = new(4);

    /// <summary>The matching index-list starts, one per entry in <see cref="_slices"/>.</summary>
    private readonly List<int> _sliceIndexStart = new(4);

    /// <summary>Vertices one mesh may hold. UGUI's own limit is 65,000; this leaves room.</summary>
    internal const int PerMesh = 60000;

    /// <summary>Vertices emitted so far. Named to match the old call sites.</summary>
    internal int currentVertCount => _positions.Count;

    internal void Clear()
    {
        _positions.Clear();
        _colours.Clear();
        _uv0.Clear();
        _indices.Clear();
        _cuts.Clear();
        _cutIndices.Clear();
        _slices.Clear();
        _sliceIndexStart.Clear();
    }

    /// <summary>Marks the end of a shape, where the geometry may be cut.</summary>
    internal void MarkShape()
    {
        var at = _positions.Count;

        if (_cuts.Count > 0 && _cuts[^1] == at)
            return;

        _cuts.Add(at);
        _cutIndices.Add(_indices.Count);
    }

    /// <summary>
    /// How many meshes this geometry needs, and where each begins.
    /// </summary>
    /// <remarks>
    /// Greedy: take the furthest shape boundary that still fits one mesh. A single shape
    /// larger than a whole mesh cannot be split at all -- it is emitted anyway and will be
    /// rejected by Unity, which is why the tessellator still refuses to start one that big.
    /// </remarks>
    internal int Slices()
    {
        if (_slices.Count > 0)
            return _slices.Count - 1;

        _slices.Add(0);
        _sliceIndexStart.Add(0);

        var start = 0;
        while (start < _positions.Count)
        {
            var limit = start + PerMesh;
            var cut = -1;
            var cutIndex = _indices.Count;

            for (var k = 0; k < _cuts.Count; k++)
            {
                if (_cuts[k] > start && _cuts[k] <= limit)
                {
                    cut = _cuts[k];
                    cutIndex = _cutIndices[k];
                }
            }

            // No boundary fit: one shape is bigger than a mesh. Take the whole remainder
            // rather than looping for ever; the tessellator guards against this case.
            if (cut < 0)
            {
                cut = _positions.Count;
                cutIndex = _indices.Count;
            }

            _slices.Add(cut);
            _sliceIndexStart.Add(cutIndex);
            start = cut;
        }

        return _slices.Count - 1;
    }

    internal void AddVert(Vector3 position, Color32 colour, Vector2 uv)
    {
        _positions.Add(position);
        _colours.Add(colour);
        _uv0.Add(uv);
    }

    internal void AddTriangle(int a, int b, int c)
    {
        _indices.Add(a);
        _indices.Add(b);
        _indices.Add(c);
    }

    /// <summary>Rebased indices for one slice. The only copy a split upload makes.</summary>
    private readonly List<int> _sliceI = new(8192);

    /// <summary>Uploads one slice of the geometry to a mesh, reusing its buffers.</summary>
    /// <remarks>
    /// **Vertices are not copied at all.** Unity takes a range of a list directly, so the
    /// position, colour and UV buffers are handed over in place. Only the indices are copied,
    /// because they have to be rebased onto the slice's own vertex numbering, and only this
    /// slice's own range is touched.
    ///
    /// The first version walked the WHOLE index list once per slice, testing each triangle for
    /// membership. On a surface of 150 shadowed cards that is roughly 900,000 indices scanned
    /// three times, on the main thread, every upload -- measured at 6.17 ms against 0.14 ms for
    /// a single-mesh surface a ninth the size. Nine times the geometry cannot cost forty times
    /// the upload; the difference was all scanning.
    /// </remarks>
    internal void Apply(Mesh mesh, int slice = 0)
    {
        // Clear indices before vertices: a rebuild that shrinks the vertex count would
        // otherwise leave triangles referring to vertices that no longer exist, which Unity
        // rejects outright.
        mesh.Clear(keepVertexLayout: true);

        if (_positions.Count == 0)
            return;

        if (Slices() <= 1)
        {
            if (slice != 0)
                return;

            mesh.SetVertices(_positions);
            mesh.SetColors(_colours);
            mesh.SetUVs(0, _uv0);
            mesh.SetTriangles(_indices, 0, calculateBounds: true);
            return;
        }

        if (slice < 0 || slice + 1 >= _slices.Count)
            return;

        var from = _slices[slice];
        var to = _slices[slice + 1];

        // HARD GUARD. Handing a CanvasRenderer a mesh past UGUI's limit does not throw, it
        // takes the game down natively with nothing in the log -- that is exactly what
        // happened when this ceiling was raised the wrong way. A slice can only exceed the
        // limit if a single shape did, which the tessellator refuses, so reaching here means
        // something upstream is wrong. Drop the slice and say so rather than crash.
        if (to - from > PerMesh)
        {
            ScriptedScreensVectorPlugin.Log?.LogError(
                $"vector: slice {slice} is {to - from} vertices, past the {PerMesh} one mesh can hold; "
                + "dropped. A single shape too large to split is the only way this happens.");
            return;
        }

        var indexFrom = _sliceIndexStart[slice];
        var indexTo = _sliceIndexStart[slice + 1];

        _sliceI.Clear();
        for (var t = indexFrom; t < indexTo; t++)
            _sliceI.Add(_indices[t] - from);

        mesh.SetVertices(_positions, from, to - from);
        mesh.SetColors(_colours, from, to - from);
        mesh.SetUVs(0, _uv0, from, to - from);
        mesh.SetTriangles(_sliceI, 0, calculateBounds: true);
    }
}
