using UnityEngine;

namespace ScriptedScreensVector;

internal static class DashWindow
{
    internal static bool Finite(Rect rect) => float.IsFinite(rect.xMin) && float.IsFinite(rect.yMin)
        && float.IsFinite(rect.xMax) && float.IsFinite(rect.yMax);

    internal static Rect? Expand(Rect? screen)
    {
        if (screen is not Rect rect || !Finite(rect))
            return null;
        var expanded = Rect.MinMaxRect(rect.xMin - rect.width, rect.yMin - rect.height,
            rect.xMax + rect.width, rect.yMax + rect.height);
        return Finite(expanded) ? expanded : null;
    }

    internal static bool NeedsRebuild(Rect? builtWindow, Rect? screen)
    {
        if (builtWindow is not Rect window || !Finite(window))
            return false;
        return screen is not Rect current || !Finite(current)
            || current.xMin < window.xMin || current.yMin < window.yMin
            || current.xMax > window.xMax || current.yMax > window.yMax;
    }
}
