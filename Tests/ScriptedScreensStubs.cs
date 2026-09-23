// ---------------------------------------------------------------------------
// ScriptedScreens value types, stubbed so SceneText can be tested headless.
//
// Field-for-field with the real ones (verified by compiling a construction
// against the publicised assembly), because SceneText's whole job is producing
// these. Testing it against a different shape would prove nothing.
// ---------------------------------------------------------------------------
namespace ScriptedScreens.ScriptableUi
{
    internal static class ScriptedScreensScriptableUiSystem
    {
        internal enum UiValueType { Nil, Number, Bool, String, Array, Map }

        internal struct UiValue
        {
            internal UiValueType Type;
            internal float Number;
            internal string? String;
            internal UiValue[]? Array;
            internal UiProp[]? Map;
        }

        internal struct UiProp
        {
            internal string Key;
            internal UiValue Value;
        }
    }
}
