using System;
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

    /// <summary>
    /// The first mesh is left empty and the geometry starts at the second, so a label can draw
    /// underneath the very first shape.
    /// </summary>
    /// <remarks>
    /// The first mesh is the surface's own renderer, which draws before any child, so nothing
    /// can be placed in front of it. A label declared before the scene's first shape and covered
    /// by it stayed on top instead -- a caption drawn over the picture it was meant to sit under,
    /// seen in game 2026-09-15. An empty first mesh costs one draw call, and only then.
    /// </remarks>
    internal bool LeadingEmpty;

    /// <summary>The last slice refused as too large to upload, or null.</summary>
    internal string? Dropped;

    internal void Clear()
    {
        Dropped = null;
        LeadingEmpty = false;
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

        if (LeadingEmpty && _positions.Count > 0)
        {
            _slices.Add(0);
            _sliceIndexStart.Add(0);
        }

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

    /// <summary>Filters and mask of the groups currently being emitted, or null.</summary>
    internal VertexTint? Tint;

    internal void AddVert(Vector3 position, Color32 colour, Vector2 uv)
    {
        // Filters only: a group's mask is multiplied in once its content is known, MaskRange.
        if (Tint != null)
            colour = Tint.ApplyFilters(colour);

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

    /// <summary>
    /// Multiplies a group mask's alpha into every vertex from <paramref name="from"/> on,
    /// subdividing triangles first where the mask is not affine across them.
    /// </summary>
    /// <remarks>
    /// Subdivision rebuilds the range shape by shape: each shape's own vertices go back in the
    /// same order, new midpoints are appended after them, and its cut moves to the new end.
    /// A shape never references vertices outside itself, so nothing else has to be renumbered.
    /// The range can only be rebuilt from a shape boundary; a group always starts on one.
    /// </remarks>
    internal void MaskRange(int from, MaskInfo mask, bool refine, float screenScale = 1f, List<Rect>? labels = null)
    {
        var to = _positions.Count;

        if (mask.Gradient.BoundingBox)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            // Labels count towards the group's box. A group holding only text has no vertices at
            // all, and its mask kept a default box that put every glyph outside the gradient:
            // the text vanished entirely (seen in game 2026-09-15).
            if (labels != null)
            {
                foreach (var box in labels)
                {
                    foreach (var corner in new[] { box.min, box.max, new Vector2(box.xMin, box.yMax), new Vector2(box.xMax, box.yMin) })
                    {
                        var local = (Vector2)mask.CanvasToLocal.MultiplyPoint3x4(corner);
                        min = Vector2.Min(min, local);
                        max = Vector2.Max(max, local);
                    }
                }
            }

            for (var v = from; v < to; v++)
            {
                if (_colours[v].a <= 2)
                    continue;

                var local = (Vector2)mask.CanvasToLocal.MultiplyPoint3x4(_positions[v]);
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }

            if (min.x <= max.x)
            {
                mask.Min = min;
                mask.Size = max - min;
            }
        }

        if (to <= from)
            return;

        var firstCut = 0;
        while (firstCut < _cuts.Count && _cuts[firstCut] <= from)
            firstCut++;

        _alphaAt.Clear();
        var aligned = from == 0 || (firstCut > 0 && _cuts[firstCut - 1] == from);
        if (refine && aligned && firstCut < _cuts.Count && _cuts[^1] == to)
            Refine(from, firstCut, mask, screenScale);

        for (var v = from; v < _positions.Count; v++)
        {
            var at = _alphaAt.TryGetValue(v, out var sided) ? sided : _positions[v];
            var c = _colours[v];
            c.a = (byte)Mathf.RoundToInt(c.a * Mathf.Clamp01(mask.AlphaAt(at)));
            _colours[v] = c;
        }

        _alphaAt.Clear();
    }

    private const float MaskTolerance = 0.02f;

    /// <summary>Grid cell size, in screen pixels, where a mask changes.</summary>
    private const float MaskCellPixels = 5f;

    /// <summary>Most cells one shape may be cut into; past it the cells grow.</summary>
    private const int MaskMaxCells = 8000;

    private readonly List<Vector3> _oldP = new();
    private readonly List<Color32> _oldC = new();
    private readonly List<Vector2> _oldU = new();
    private readonly List<int> _oldI = new();
    private readonly List<int> _pieces = new();
    private readonly List<int> _cutScratch = new();
    private readonly Dictionary<long, int> _edgeCache = new();
    private readonly Dictionary<(long, long), int> _gridCache = new();
    private readonly HashSet<int> _onCut = new();

    /// <summary>
    /// Where a vertex on the conic seam reads its mask value: a hair into its own side. The
    /// vertex stays exactly on the seam, so the two sides meet with no gap.
    /// </summary>
    private readonly Dictionary<int, Vector3> _alphaAt = new();

    /// <summary>
    /// Rebuilds the masked range cut on a grid of small cells, so vertex alpha can follow the mask.
    /// </summary>
    /// <remarks>
    /// Each approach before this was seen failing on a console on 2026-09-15:
    ///
    /// - Subdividing each triangle as far as it needed left pinholes where a split triangle met
    ///   an unsplit neighbour: dark specks over a masked card.
    /// - Subdividing a whole shape to one depth fixed the specks, but a rounded card is a fan of
    ///   long thin triangles, and halving those runs out of vertex budget long before the cells
    ///   are small: a soft wedge by a conic's centre, rays at every cut, a hatched band of
    ///   slivers along the fan's diagonal.
    ///
    /// So a masked shape is cut on a regular grid instead, a few screen pixels a cell, whatever
    /// its triangles look like. Every point is named by what makes it -- a grid corner, a
    /// triangle edge crossing a grid line, an original vertex -- and made once, so neighbouring
    /// triangles share exactly the same vertices on their common edges and nothing cracks. The
    /// cost follows the shape's area on screen, not its triangulation.
    ///
    /// A conic mask is first cut along its start angle, the one place its value jumps; each side
    /// of that cut reads its own value, which is the hard edge CSS draws.
    /// </remarks>
    private void Refine(int from, int firstCut, MaskInfo mask, float screenScale)
    {
        var indexFrom = firstCut == 0 ? 0 : _cutIndices[firstCut - 1];

        _oldP.Clear(); _oldC.Clear(); _oldU.Clear(); _oldI.Clear();
        for (var v = from; v < _positions.Count; v++)
        {
            _oldP.Add(_positions[v]);
            _oldC.Add(_colours[v]);
            _oldU.Add(_uv0[v]);
        }

        for (var t = indexFrom; t < _indices.Count; t++)
            _oldI.Add(_indices[t]);

        _positions.RemoveRange(from, _positions.Count - from);
        _colours.RemoveRange(from, _colours.Count - from);
        _uv0.RemoveRange(from, _uv0.Count - from);
        _indices.RemoveRange(indexFrom, _indices.Count - indexFrom);

        var conic = mask.Seam(out var centre, out var start);

        var oldShapeStart = from;
        var oldIndexStart = indexFrom;

        for (var k = firstCut; k < _cuts.Count; k++)
        {
            var oldShapeEnd = _cuts[k];
            var oldIndexEnd = _cutIndices[k];
            var newShapeStart = _positions.Count;

            for (var v = oldShapeStart; v < oldShapeEnd; v++)
            {
                _positions.Add(_oldP[v - from]);
                _colours.Add(_oldC[v - from]);
                _uv0.Add(_oldU[v - from]);
            }

            _pieces.Clear();
            _edgeCache.Clear();
            _gridCache.Clear();
            _onCut.Clear();

            for (var t = oldIndexStart; t + 2 < oldIndexEnd; t += 3)
            {
                AddPiece(
                    _oldI[t - indexFrom] - oldShapeStart + newShapeStart,
                    _oldI[t + 1 - indexFrom] - oldShapeStart + newShapeStart,
                    _oldI[t + 2 - indexFrom] - oldShapeStart + newShapeStart);
            }

            if (conic)
            {
                _cutScratch.Clear();
                _cutScratch.AddRange(_pieces);
                _pieces.Clear();

                for (var q = 0; q + 2 < _cutScratch.Count; q += 3)
                    CutAlongLine(_cutScratch[q], _cutScratch[q + 1], _cutScratch[q + 2], centre, start, 0);

                for (var q = 0; q + 2 < _pieces.Count; q += 3)
                {
                    var centroid = (_positions[_pieces[q]] + _positions[_pieces[q + 1]] + _positions[_pieces[q + 2]]) / 3f;
                    for (var n = 0; n < 3; n++)
                    {
                        var v = _pieces[q + n];
                        if (_onCut.Contains(v) && !_alphaAt.ContainsKey(v))
                        {
                            var towards = centroid - _positions[v];
                            _alphaAt[v] = _positions[v] + (towards.sqrMagnitude > 1e-12f ? towards.normalized * 0.01f : Vector3.zero);
                        }
                    }
                }
            }

            // Does this shape need cutting at all, and over what area?
            var needs = false;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (var q = 0; q + 2 < _pieces.Count; q += 3)
            {
                var a = _positions[_pieces[q]];
                var b = _positions[_pieces[q + 1]];
                var c = _positions[_pieces[q + 2]];
                needs |= mask.SpreadOver(a, b, c) > MaskTolerance;
                min = Vector2.Min(min, Vector2.Min(a, Vector2.Min(b, c)));
                max = Vector2.Max(max, Vector2.Max(a, Vector2.Max(b, c)));
            }

            if (!needs)
            {
                _indices.AddRange(_pieces);
            }
            else
            {
                var cell = MaskCellPixels / Mathf.Max(0.0001f, screenScale);
                var size = max - min;
                var cells = (size.x / cell) * (size.y / cell);
                if (cells > MaskMaxCells)
                    cell *= Mathf.Sqrt(cells / MaskMaxCells);

                _cell = cell;
                for (var q = 0; q + 2 < _pieces.Count; q += 3)
                    Grid(_pieces[q], _pieces[q + 1], _pieces[q + 2], cell, newShapeStart);
            }

            _cuts[k] = _positions.Count;
            _cutIndices[k] = _indices.Count;
            oldShapeStart = oldShapeEnd;
            oldIndexStart = oldIndexEnd;
        }

        _edgeCache.Clear();
        _gridCache.Clear();
        _onCut.Clear();
        _shapeStart = _positions.Count;
    }

    private void AddPiece(int a, int b, int c)
    {
        _pieces.Add(a);
        _pieces.Add(b);
        _pieces.Add(c);
    }

    /// <summary>Cuts a triangle along a full line through the centre, when the line crosses it.</summary>
    private void CutAlongLine(int a, int b, int c, Vector2 origin, Vector2 direction, int line)
    {
        var tri = new[] { a, b, c };
        var side = new float[3];
        var hasLeft = false;
        var hasRight = false;

        for (var i = 0; i < 3; i++)
        {
            var p = (Vector2)_positions[tri[i]] - origin;
            side[i] = direction.x * p.y - direction.y * p.x;
            if (Mathf.Abs(side[i]) < 1e-5f)
                side[i] = 0f;

            hasLeft |= side[i] > 0f;
            hasRight |= side[i] < 0f;
        }

        if (!hasLeft || !hasRight)
        {
            AddPiece(a, b, c);
            return;
        }

        var left = new List<int>(4);
        var right = new List<int>(4);
        for (var i = 0; i < 3; i++)
        {
            var j = (i + 1) % 3;

            if (side[i] > 0f)
            {
                left.Add(tri[i]);
            }
            else if (side[i] < 0f)
            {
                right.Add(tri[i]);
            }
            else
            {
                left.Add(CutCopy(tri[i], tri[i], 0f, line, +1));
                right.Add(CutCopy(tri[i], tri[i], 0f, line, -1));
            }

            if ((side[i] > 0f && side[j] < 0f) || (side[i] < 0f && side[j] > 0f))
            {
                var lo = Mathf.Min(tri[i], tri[j]);
                var hi = Mathf.Max(tri[i], tri[j]);
                var slo = lo == tri[i] ? side[i] : side[j];
                var shi = lo == tri[i] ? side[j] : side[i];
                var t = slo / (slo - shi);

                left.Add(CutCopy(lo, hi, t, line, +1));
                right.Add(CutCopy(lo, hi, t, line, -1));
            }
        }

        for (var i = 1; i + 1 < left.Count; i++)
            AddPiece(left[0], left[i], left[i + 1]);

        for (var i = 1; i + 1 < right.Count; i++)
            AddPiece(right[0], right[i], right[i + 1]);
    }

    /// <summary>A vertex at <paramref name="t"/> along (lo, hi) on one side of a cut line, shared by every piece there.</summary>
    private int CutCopy(int lo, int hi, float t, int line, int towards)
    {
        var key = (1L << 62) | ((long)lo << 38) | ((long)hi << 8) | ((long)line << 2) | (towards > 0 ? 1L : 2L);
        if (_edgeCache.TryGetValue(key, out var cached))
            return cached;

        _positions.Add(Vector3.Lerp(_positions[lo], _positions[hi], t));
        _colours.Add(Color32.Lerp(_colours[lo], _colours[hi], t));
        _uv0.Add(Vector2.Lerp(_uv0[lo], _uv0[hi], t));

        var index = _positions.Count - 1;
        _onCut.Add(index);
        _edgeCache[key] = index;
        return index;
    }

    /// <summary>A point of a clipped cell, named by the two things it lies on.</summary>
    private struct GridPoint
    {
        internal Vector2 Position;

        /// <summary>An original vertex, or -1.</summary>
        internal int Vertex;

        /// <summary>What it lies on: triangle edges (1, lo, hi) and grid lines (2 + axis, index, 0).</summary>
        internal (int Kind, int A, int B) C1;
        internal (int Kind, int A, int B) C2;

        /// <summary>A third, when a point already on two things lies exactly on a grid line too.</summary>
        internal (int Kind, int A, int B) C3;
    }

    [ThreadStatic] private static List<GridPoint>? _polyA;
    [ThreadStatic] private static List<GridPoint>? _polyB;
    [ThreadStatic] private static List<GridPoint>? _polyStrip;

    /// <summary>Clips one triangle to every grid cell it covers and emits the pieces.</summary>
    private void Grid(int a, int b, int c, float cell, int shapeStart)
    {
        var pa = (Vector2)_positions[a];
        var pb = (Vector2)_positions[b];
        var pc = (Vector2)_positions[c];

        var i0 = Mathf.FloorToInt(Mathf.Min(pa.x, Mathf.Min(pb.x, pc.x)) / cell);
        var i1 = Mathf.FloorToInt(Mathf.Max(pa.x, Mathf.Max(pb.x, pc.x)) / cell);
        var j0 = Mathf.FloorToInt(Mathf.Min(pa.y, Mathf.Min(pb.y, pc.y)) / cell);
        var j1 = Mathf.FloorToInt(Mathf.Max(pa.y, Mathf.Max(pb.y, pc.y)) / cell);

        // A triangle inside one cell needs nothing.
        if (i0 == i1 && j0 == j1)
        {
            _indices.Add(a);
            _indices.Add(b);
            _indices.Add(c);
            return;
        }

        var src = _polyA ??= new List<GridPoint>(12);
        var dst = _polyB ??= new List<GridPoint>(12);

        var strip = _polyStrip ??= new List<GridPoint>(12);

        // Row by row: the triangle cut to the row's strip first, so only the cells the strip
        // actually spans are visited. A long thin triangle's bounding box is mostly empty cells.
        for (var j = j0; j <= j1; j++)
        {
            src.Clear();
            src.Add(new GridPoint { Position = pa, Vertex = a, C1 = Edge(a, b), C2 = Edge(c, a) });
            src.Add(new GridPoint { Position = pb, Vertex = b, C1 = Edge(a, b), C2 = Edge(b, c) });
            src.Add(new GridPoint { Position = pc, Vertex = c, C1 = Edge(b, c), C2 = Edge(c, a) });

            ClipAxis(src, dst, 1, j, j * cell, true);
            ClipAxis(dst, strip, 1, j + 1, (j + 1) * cell, false);
            if (strip.Count < 3)
                continue;

            var left = float.MaxValue;
            var right = float.MinValue;
            foreach (var point in strip)
            {
                left = Mathf.Min(left, point.Position.x);
                right = Mathf.Max(right, point.Position.x);
            }

            var from = Mathf.Max(i0, Mathf.FloorToInt(left / cell));
            var to = Mathf.Min(i1, Mathf.FloorToInt(right / cell));

            for (var i = from; i <= to; i++)
            {
                ClipAxis(strip, dst, 0, i, i * cell, true);
                ClipAxis(dst, src, 0, i + 1, (i + 1) * cell, false);

                if (src.Count < 3)
                    continue;

                var first = PointVertex(src[0], a, b, c, cell);
                var previous = PointVertex(src[1], a, b, c, cell);
                for (var n = 2; n < src.Count; n++)
                {
                    var current = PointVertex(src[n], a, b, c, cell);
                    if (first != previous && previous != current && current != first)
                    {
                        _indices.Add(first);
                        _indices.Add(previous);
                        _indices.Add(current);
                    }

                    previous = current;
                }
            }
        }
    }

    private static (int, int, int) Edge(int u, int v)
    {
        return (1, Mathf.Min(u, v), Mathf.Max(u, v));
    }

    /// <summary>One Sutherland-Hodgman pass against a grid line, keeping names on every point.</summary>
    private void ClipAxis(List<GridPoint> input, List<GridPoint> output, int axis, int index, float value, bool keepGreater)
    {
        output.Clear();
        var count = input.Count;
        if (count == 0)
            return;

        for (var n = 0; n < count; n++)
        {
            var p = input[n];
            var q = input[(n + 1) % count];
            var cp = axis == 0 ? p.Position.x : p.Position.y;
            var cq = axis == 0 ? q.Position.x : q.Position.y;
            var inP = keepGreater ? cp >= value : cp <= value;
            var inQ = keepGreater ? cq >= value : cq <= value;

            if (inP)
            {
                // A point exactly on this line lies on it: say so, or the segment from it along
                // the line has no carrier in common with the next point, and the next pass
                // computes its crossing on the wrong one. Seen as triangular gaps in every
                // cell of the row through a conic mask's centre, which sat on a grid line.
                if (cp == value && p.C3.Kind == 0 && !Carries(p, (2 + axis, index, 0)))
                    p.C3 = (2 + axis, index, 0);

                output.Add(p);
            }

            // A point exactly on the line is already in; it is not crossed again.
            if (inP == inQ || cp == value || cq == value)
                continue;

            var carrier = Common(p, q);
            var line = (2 + axis, index, 0);
            output.Add(Cross(carrier, line, value, axis));
        }
    }

    private static bool Carries(GridPoint p, (int, int, int) carrier)
    {
        return p.C1 == carrier || p.C2 == carrier || (p.C3.Item1 != 0 && p.C3 == carrier);
    }

    private static (int, int, int) Common(GridPoint p, GridPoint q)
    {
        if (Carries(q, p.C1))
            return p.C1;

        if (Carries(q, p.C2))
            return p.C2;

        return p.C3;
    }

    /// <summary>Where a carrier meets a grid line, computed the same way from either side.</summary>
    private GridPoint Cross((int Kind, int A, int B) carrier, (int, int, int) line, float value, int axis)
    {
        Vector2 position;
        if (carrier.Kind == 1)
        {
            var lo = (Vector2)_positions[carrier.A];
            var hi = (Vector2)_positions[carrier.B];
            var clo = axis == 0 ? lo.x : lo.y;
            var chi = axis == 0 ? hi.x : hi.y;
            var t = Mathf.Abs(chi - clo) < 1e-12f ? 0f : (value - clo) / (chi - clo);
            position = lo + (hi - lo) * t;
            if (axis == 0) position.x = value; else position.y = value;
        }
        else
        {
            // The other axis's grid line: a corner, exact.
            position = axis == 0 ? new Vector2(value, carrier.A * _cell) : new Vector2(carrier.A * _cell, value);
        }

        return new GridPoint { Position = position, Vertex = -1, C1 = carrier, C2 = line };
    }

    private float _cell;

    /// <summary>The mesh vertex for a clipped point, made once per name.</summary>
    private int PointVertex(GridPoint point, int a, int b, int c, float cell)
    {
        if (point.Vertex >= 0)
            return point.Vertex;

        (long, long) key;
        var edge = point.C1.Kind == 1 ? point.C1 : point.C2.Kind == 1 ? point.C2 : default;
        if (edge.Kind == 1)
        {
            var line = point.C1.Kind == 1 ? point.C2 : point.C1;
            key = (((long)edge.A << 32) | (uint)edge.B, ((long)line.Kind << 40) ^ (uint)line.A);
            if (_gridCache.TryGetValue(key, out var cached))
                return cached;

            var lo = edge.A;
            var hi = edge.B;
            var span = (Vector2)_positions[hi] - (Vector2)_positions[lo];
            var along = (point.Position - (Vector2)_positions[lo]);
            var t = span.sqrMagnitude < 1e-12f ? 0f : Mathf.Clamp01(Vector2.Dot(along, span) / span.sqrMagnitude);

            _positions.Add(new Vector3(point.Position.x, point.Position.y, Mathf.Lerp(_positions[lo].z, _positions[hi].z, t)));
            _colours.Add(Color32.Lerp(_colours[lo], _colours[hi], t));
            _uv0.Add(Vector2.Lerp(_uv0[lo], _uv0[hi], t));

            var index = _positions.Count - 1;
            var loSided = _alphaAt.TryGetValue(lo, out var loAt);
            var hiSided = _alphaAt.TryGetValue(hi, out var hiAt);
            if (loSided || hiSided)
                _alphaAt[index] = Vector3.Lerp(loSided ? loAt : _positions[lo], hiSided ? hiAt : _positions[hi], t);

            _gridCache[key] = index;
            return index;
        }

        // A grid corner inside the triangle: colour from the triangle's own corners.
        var x = point.C1.Kind == 2 ? point.C1.A : point.C2.A;
        var y = point.C1.Kind == 3 ? point.C1.A : point.C2.A;
        key = ((3L << 60) | ((long)(x + 1000000) << 24) | (uint)(y + 1000000), 0L);
        if (_gridCache.TryGetValue(key, out var corner))
            return corner;

        var w = Barycentric(point.Position, _positions[a], _positions[b], _positions[c]);
        _positions.Add(new Vector3(point.Position.x, point.Position.y, _positions[a].z * w.x + _positions[b].z * w.y + _positions[c].z * w.z));
        Color ca = _colours[a], cb = _colours[b], cc = _colours[c];
        _colours.Add(ca * w.x + cb * w.y + cc * w.z);
        _uv0.Add(_uv0[a] * w.x + _uv0[b] * w.y + _uv0[c] * w.z);

        var made = _positions.Count - 1;
        _gridCache[key] = made;
        return made;
    }

    private static Vector3 Barycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        var v0 = b - a;
        var v1 = c - a;
        var v2 = p - a;
        var d = v0.x * v1.y - v1.x * v0.y;
        if (Mathf.Abs(d) < 1e-12f)
            return new Vector3(1f, 0f, 0f);

        var v = (v2.x * v1.y - v1.x * v2.y) / d;
        var w = (v0.x * v2.y - v2.x * v0.y) / d;
        return new Vector3(1f - v - w, v, w);
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
