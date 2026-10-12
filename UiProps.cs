using System;
using SS = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem;

namespace ScriptedScreensVector;

internal static class UiProps
{
    internal static bool Equal(SS.UiProp[]? earlier, SS.UiProp[]? later)
    {
        if (ReferenceEquals(earlier, later))
            return true;
        if (earlier == null || later == null || earlier.Length != later.Length)
            return false;
        foreach (var prop in later)
        {
            var found = false;
            foreach (var before in earlier)
                if (string.Equals(before.Key, prop.Key, StringComparison.Ordinal) && Equal(before.Value, prop.Value))
                {
                    found = true;
                    break;
                }
            if (!found)
                return false;
        }
        return true;
    }

    private static bool Equal(SS.UiValue earlier, SS.UiValue later)
    {
        if (earlier.Type != later.Type)
            return false;
        switch (earlier.Type)
        {
            case SS.UiValueType.Nil: return true;
            case SS.UiValueType.Number: return earlier.Number.Equals(later.Number);
            case SS.UiValueType.Bool: return earlier.Bool == later.Bool;
            case SS.UiValueType.String: return string.Equals(earlier.String, later.String, StringComparison.Ordinal);
            case SS.UiValueType.Map: return Equal(earlier.Map, later.Map);
            case SS.UiValueType.Array:
                if (ReferenceEquals(earlier.Array, later.Array))
                    return true;
                if (earlier.Array == null || later.Array == null || earlier.Array.Length != later.Array.Length)
                    return false;
                for (var i = 0; i < earlier.Array.Length; i++)
                    if (!Equal(earlier.Array[i], later.Array[i]))
                        return false;
                return true;
            default: return false;
        }
    }
}
