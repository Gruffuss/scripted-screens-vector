using UnityEngine;
namespace ScriptedScreensVector;
// Managed stand-in for ColorUtility.TryParseHtmlString, which is a native call headless.
internal static class HexColor
{
    internal static bool TryParse(string? s, out Color c)
    {
        c = Color.clear;
        if (string.IsNullOrEmpty(s) || s[0] != '#') return false;
        var h = s.Substring(1);
        if (h.Length == 3 || h.Length == 4) { var e = ""; foreach (var ch in h) e += new string(ch, 2); h = e; }
        if (h.Length != 6 && h.Length != 8) return false;
        if (!uint.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out var v)) return false;
        if (h.Length == 6) v = (v << 8) | 0xFF;
        c = new Color(((v >> 24) & 255) / 255f, ((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f);
        return true;
    }
}
