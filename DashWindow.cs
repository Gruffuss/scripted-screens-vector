using UnityEngine;

namespace ScriptedScreensVector;

internal static class DashWindow
{
    internal static bool Finite(Rect rect) => float.IsFinite(rect.xMin) && float.IsFinite(rect.yMin)
        && float.IsFinite(rect.xMax) && float.IsFinite(rect.yMax);

    internal static Rect? Expand(Rect? screen)
    {
        if (screen is not Rect rect)
            return null;
        if (!Finite(rect))
            return rect;
        var expanded = Rect.MinMaxRect(rect.xMin - rect.width, rect.yMin - rect.height,
            rect.xMax + rect.width, rect.yMax + rect.height);
        return Finite(expanded) ? expanded : null;
    }

    internal static bool NeedsRebuild(Rect? builtWindow, Rect? screen)
    {
        if (builtWindow is not Rect window)
            return false;
        if (!Finite(window))
            return screen is not Rect next || Finite(next);
        return screen is not Rect current || !Finite(current)
            || current.xMin < window.xMin || current.yMin < window.yMin
            || current.xMax > window.xMax || current.yMax > window.yMax;
    }

    internal static bool CullAll(Rect? window, Matrix4x4 matrix)
    {
        if (window is Rect rect && !Finite(rect))
            return true;
        for (var i = 0; i < 16; i++)
            if (!float.IsFinite(matrix[i]))
                return true;
        var determinant = matrix.m00 * matrix.m11 - matrix.m01 * matrix.m10;
        return !float.IsFinite(determinant) || System.Math.Abs(determinant) < 1e-12f;
    }
}
