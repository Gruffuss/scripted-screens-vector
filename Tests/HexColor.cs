using System;
using System.Collections.Generic;
using UnityEngine;
namespace ScriptedScreensVector;
// Managed stand-in for ColorUtility.TryParseHtmlString, which is a native call headless.
internal static class HexColor
{
    // Unity's measured name table, documented in REFERENCE.
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = "#FF0000", ["cyan"] = "#00FFFF", ["blue"] = "#0000FF", ["darkblue"] = "#00008B",
        ["lightblue"] = "#ADD8E6", ["purple"] = "#800080", ["yellow"] = "#FFFF00", ["lime"] = "#00FF00",
        ["fuchsia"] = "#FF00FF", ["white"] = "#FFFFFF", ["silver"] = "#C0C0C0", ["grey"] = "#808080",
        ["black"] = "#000000", ["orange"] = "#FFA500", ["brown"] = "#A52A2A", ["maroon"] = "#800000",
        ["green"] = "#008000", ["olive"] = "#808000", ["navy"] = "#000080", ["teal"] = "#008080",
        ["aqua"] = "#00FFFF", ["magenta"] = "#FF00FF", ["transparent"] = "#00000000",
    };

    internal static bool TryParse(string? s, out Color c)
    {
        c = Color.clear;
        if (string.IsNullOrEmpty(s)) return false;
        if (s[0] != '#' && !Names.TryGetValue(s, out s)) return false;
        var h = s.Substring(1);
        foreach (var ch in h) if (!Uri.IsHexDigit(ch)) return false;
        if (h.Length == 3 || h.Length == 4) { var e = ""; foreach (var ch in h) e += new string(ch, 2); h = e; }
        if (h.Length != 6 && h.Length != 8) return false;
        if (!uint.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out var v)) return false;
        if (h.Length == 6) v = (v << 8) | 0xFF;
        c = new Color(((v >> 24) & 255) / 255f, ((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f);
        return true;
    }
}
