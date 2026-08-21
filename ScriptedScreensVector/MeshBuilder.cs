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

    /// <summary>Vertices emitted so far. Named to match the old call sites.</summary>
    internal int currentVertCount => _positions.Count;

    internal void Clear()
    {
        _positions.Clear();
        _colours.Clear();
        _uv0.Clear();
        _indices.Clear();
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

    /// <summary>Uploads to a mesh, reusing its buffers.</summary>
    internal void Apply(Mesh mesh)
    {
        // Clear indices before vertices: a rebuild that shrinks the vertex count would
        // otherwise leave triangles referring to vertices that no longer exist, which Unity
        // rejects outright.
        mesh.Clear(keepVertexLayout: true);

        if (_positions.Count == 0)
            return;

        mesh.SetVertices(_positions);
        mesh.SetColors(_colours);
        mesh.SetUVs(0, _uv0);
        mesh.SetTriangles(_indices, 0, calculateBounds: true);
    }
}
