using System;
using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>
/// The one place a colour string is turned into a colour.
/// </summary>
/// <remarks>
/// Unity's own parser is the authority on what is a colour, and this does not try to replace it.
/// What it does is forgive two things the author plainly did not mean, before asking:
///
/// * **surrounding whitespace**, which arrives from a concatenation, a text field or a paste;
/// * **`gray`**, which is a legitimate CSS colour and not a Unity one -- Unity's table carries only
///   the British `grey`, so a page written in American English drew magenta with no other clue.
///
/// Both were found by measuring Unity's real behaviour on a console, and both were first written
/// into the docs as traps for the author to avoid. That was the wrong answer: where the intent is
/// unambiguous, accepting it is a fix and documenting it is not.
///
/// Everything else Unity rejects is genuinely not a colour -- a bare `FFFFFF` is not a hex
/// literal, `rgba(...)` is a function this format does not have -- and those still fail, loudly.
///
/// `none` is this mod's, meaning "nothing here". `transparent` is Unity's own name and would
/// resolve anyway; it is handled here too so both spellings of the idea behave identically.
/// </remarks>
internal static class Colours
{
    internal static bool TryParse(string? text, out Color colour)
    {
        colour = default;
        if (string.IsNullOrEmpty(text))
            return false;

        var trimmed = text!.Trim();
        if (trimmed.Length == 0)
            return false;

        if (string.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "transparent", StringComparison.OrdinalIgnoreCase))
        {
            colour = new Color(0f, 0f, 0f, 0f);
            return true;
        }

        if (string.Equals(trimmed, "gray", StringComparison.OrdinalIgnoreCase))
            trimmed = "grey";

        return ColorUtility.TryParseHtmlString(trimmed, out colour);
    }
}
