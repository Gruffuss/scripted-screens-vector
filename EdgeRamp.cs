using System;
using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>One shape's scene-normal to screen-normal map and automatic band extent.</summary>
internal readonly struct EdgeRamp
{
    private readonly SurfaceProjection _projection;
    private readonly float _a, _b, _c, _d, _scale, _maxExtent;
    internal readonly bool Enabled;

    internal EdgeRamp(SurfaceProjection projection, Matrix4x4 matrix, float scale, float surfaceSize)
    {
        _projection = projection; _scale = scale;
        var det = matrix.m00 * matrix.m11 - matrix.m01 * matrix.m10;
        Enabled = Mathf.Abs(det) > 1e-20f;
        _a = Enabled ? matrix.m11 / det : 0f;
        _b = Enabled ? -matrix.m10 / det : 0f;
        _c = Enabled ? -matrix.m01 / det : 0f;
        _d = Enabled ? matrix.m00 / det : 0f;
        Enabled &= !float.IsNaN(_a) && !float.IsInfinity(_a) && !float.IsNaN(_b) && !float.IsInfinity(_b)
            && !float.IsNaN(_c) && !float.IsInfinity(_c) && !float.IsNaN(_d) && !float.IsInfinity(_d);
        var norm = Math.Sqrt((double)matrix.m00 * matrix.m00 + (double)matrix.m01 * matrix.m01
            + (double)matrix.m10 * matrix.m10 + (double)matrix.m11 * matrix.m11);
        // A nearly singular camera must not send band geometry billions of units off the surface.
        _maxExtent = norm > 0d ? (float)(4d * surfaceSize / norm) : 0f;
    }

    internal float Length(Vector2 canvas, Vector2 normal) => _projection.InverseTransposeLength(canvas,
        new Vector2(_a * normal.x + _b * normal.y, _c * normal.x + _d * normal.y), _scale);

    internal float Width(Vector2 canvas, Vector2 direction, Vector2 na, Vector2 nb)
    {
        var reach = Mathf.Max(Length(canvas, na), Length(canvas, nb));
        var cosine = Mathf.Max(0.25f, Mathf.Min(Mathf.Abs(Vector2.Dot(direction, na)), Mathf.Abs(Vector2.Dot(direction, nb))));
        return Mathf.Min(_maxExtent, Tessellator.EdgeBandSafety * Tessellator.AutoFeatherPixels * reach / cosine);
    }

    // Mirror of the coverage VECTOR block in shader/VectorDither.shader.
    internal static float Coverage(float y, float gradient, float x, float k, float band = 1f)
    {
        if (gradient <= 1e-20f) return Mathf.Clamp01(x);
        var feature = 1f / gradient;
        var distance = Mathf.Abs(y) / gradient;
        var t = Mathf.Clamp01((feature / k - 1f) * 0.5f);
        var effective = y < 0f ? Mathf.Min(k, feature) : k * t * t * (3f - 2f * t);
        var ramp = effective > 0f ? Mathf.Clamp01(1f - distance / effective) : distance <= 0f ? 1f : 0f;
        return Mathf.Min(Mathf.Clamp01(band), ramp);
    }
}
