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

    /// <summary>Canvas-space bounds of each shape, one per entry in <see cref="_cuts"/>.</summary>
    /// <remarks>
    /// Only filled when the scene asks for text in draw order, because it is the only thing
    /// that reads them and tracking a running box costs four comparisons per vertex.
    /// </remarks>
    private readonly List<Rect> _shapeBounds = new(256);

    /// <summary>True while shape bounds are being tracked; see <see cref="_shapeBounds"/>.</summary>
    private bool _trackBounds;

    /// <summary>Running box of the shape being built, valid while <see cref="_trackBounds"/>.</summary>
    private float _minX, _minY, _maxX, _maxY;

    /// <summary>Vertex count at which the current shape began.</summary>
    private int _shapeStart;

    /// <summary>Cuts that must be taken whatever the vertex budget says.</summary>
    /// <remarks>
    /// The ordinary cuts are candidates chosen to fit 60,000 vertices in a mesh. These are
    /// the opposite: a label has to draw between two shapes, so the mesh has to end there
    /// regardless of how little is in it. Held as shape indices rather than vertex counts so
    /// the caller can name a boundary without knowing where it landed.
    /// </remarks>
    private readonly List<int> _forced = new(8);

    /// <summary>Where each slice starts, rebuilt on demand. Index i covers [_slices[i], _slices[i+1]).</summary>
    private readonly List<int> _slices = new(4);

    /// <summary>The matching index-list starts, one per entry in <see cref="_slices"/>.</summary>
    private readonly List<int> _sliceIndexStart = new(4);

    /// <summary>Vertices one mesh may hold. UGUI's own limit is 65,000; this leaves room.</summary>
    internal const int PerMesh = 60000;

    /// <summary>Vertices emitted so far. Named to match the old call sites.</summary>
    internal int currentVertCount => _positions.Count;

    /// <summary>The last slice refused as too large to upload, or null.</summary>
    internal string? Dropped;

    internal void Clear()
    {
        Dropped = null;
        _positions.Clear();
        _colours.Clear();
        _uv0.Clear();
        _indices.Clear();
        _cuts.Clear();
        _cutIndices.Clear();
        _slices.Clear();
        _sliceIndexStart.Clear();
        _shapeBounds.Clear();
        _forced.Clear();
        _shapeStart = 0;
        ResetShapeBox();
    }

    /// <summary>Starts recording each shape's bounds, for putting text in draw order.</summary>
    internal void TrackBounds(bool on)
    {
        _trackBounds = on;
    }

    /// <summary>How many shape boundaries have been marked.</summary>
    internal int ShapeCount => _cuts.Count;

    /// <summary>Bounds of shape <paramref name="shape"/>; empty when nothing was tracked.</summary>
    internal Rect ShapeBounds(int shape)
    {
        return shape >= 0 && shape < _shapeBounds.Count ? _shapeBounds[shape] : default;
    }

    /// <summary>Vertex count at which <paramref name="shape"/> begins.</summary>
    internal int ShapeStart(int shape)
    {
        if (shape <= 0)
            return 0;

        return shape <= _cuts.Count ? _cuts[shape - 1] : _positions.Count;
    }

    /// <summary>How many slices end at or before <paramref name="vertex"/>.</summary>
    /// <remarks>
    /// Text in draw order asks this: a label cut in before vertex V draws after exactly this
    /// many meshes. Only meaningful once <see cref="Slices"/> has run.
    /// </remarks>
    internal int SlicesBefore(int vertex)
    {
        var before = 0;

        // _slices[0] is always 0 and is the start of the first mesh, not the end of one.
        for (var i = 1; i < _slices.Count; i++)
        {
            if (_slices[i] <= vertex)
                before++;
        }

        return before;
    }

    /// <summary>Requires a mesh boundary immediately before <paramref name="shape"/>.</summary>
    internal void ForceCutBefore(int shape)
    {
        if (shape <= 0 || shape > _cuts.Count)
            return;

        // The cut sits at the START of that shape, which is the END of the one before it.
        var at = shape - 1;
        if (_forced.Contains(at))
            return;

        // Kept sorted: Slices walks it once, in step with its own forward scan.
        var insert = _forced.Count;
        while (insert > 0 && _forced[insert - 1] > at)
            insert--;

        _forced.Insert(insert, at);
    }

    private void ResetShapeBox()
    {
        _minX = float.MaxValue;
        _minY = float.MaxValue;
        _maxX = float.MinValue;
        _maxY = float.MinValue;
    }

    /// <summary>Marks the end of a shape, where the geometry may be cut.</summary>
    internal void MarkShape()
    {
        var at = _positions.Count;

        if (_cuts.Count > 0 && _cuts[^1] == at)
            return;

        _cuts.Add(at);
        _cutIndices.Add(_indices.Count);

        if (_trackBounds)
        {
            // A shape that emitted no vertices still gets an entry, so that shape indices and
            // bounds stay in step. An empty box overlaps nothing, which is the right answer.
            _shapeBounds.Add(_positions.Count > _shapeStart
                ? Rect.MinMaxRect(_minX, _minY, _maxX, _maxY)
                : default);

            _shapeStart = _positions.Count;
            ResetShapeBox();
        }
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

        // A forced cut outranks the vertex budget: text has to draw between two shapes, so
        // the mesh ends there however little is in it. Taking the NEAREST one ahead keeps the
        // greedy rule below for every stretch between them.
        var nextForced = 0;

        var start = 0;
        while (start < _positions.Count)
        {
            var limit = start + PerMesh;
            var cut = -1;
            var cutIndex = _indices.Count;

            while (nextForced < _forced.Count && _cuts[_forced[nextForced]] <= start)
                nextForced++;

            if (nextForced < _forced.Count && _cuts[_forced[nextForced]] <= limit)
            {
                cut = _cuts[_forced[nextForced]];
                cutIndex = _cutIndices[_forced[nextForced]];
                nextForced++;
            }
            else
            {
                for (var k = 0; k < _cuts.Count; k++)
                {
                    if (_cuts[k] > start && _cuts[k] <= limit)
                    {
                        cut = _cuts[k];
                        cutIndex = _cutIndices[k];
                    }
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

        // Transparent vertices do not count: a feather ring or the fading edge of a shadow
        // cannot cover a label. Counted, they made every pair of flush boxes overlap by a
        // feather's width -- which grows with viewing distance -- and forced mesh cuts for
        // labels nothing covers.
        if (!_trackBounds || colour.a <= 2)
            return;

        if (position.x < _minX) _minX = position.x;
        if (position.x > _maxX) _maxX = position.x;
        if (position.y < _minY) _minY = position.y;
        if (position.y > _maxY) _maxY = position.y;
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
            // Kept for vector_stats as well as logged: the log alone let a dropped slice read as
            // "no problems" there while its shapes vanished from the console.
            Dropped = $"mesh {slice} ({to - from} vertices, past the {PerMesh} one mesh holds)";

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
