-- Which colour strings does Unity's parser actually accept?
--
-- A data string becomes a COLOUR only if `ColorUtility.TryParseHtmlString` takes it (plus
-- `transparent` and `none`, which the mod handles itself). Anything else stays text, so a shape
-- bound to it keeps no fill and the name is listed as unresolved.
--
-- This page is SELF-REPORTING: read the answer from `vector_stats`, not from the screen.
--   * a name listed under unresolved  -> Unity REJECTED that string
--   * a name not listed              -> Unity ACCEPTED it, and the swatch shows the colour
--
-- Since 0.11.64 a rejected binding also draws magenta rather than white, so the screen agrees
-- with the tool: every magenta swatch is a rejected string.
--
-- The labels are drawn beside each swatch so a capture alone is readable, but the tool is the
-- authority -- a magenta swatch and a magenta-ish accepted colour would look the same.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

-- Each entry is { key, the string sent as data, what it is probing }.
local CASES = {
    { "c01", "#FFFFFF",            "6-digit hex" },
    { "c02", "#ffffff",            "lowercase hex" },
    { "c03", "#FFFFFFFF",          "8-digit hex with alpha" },
    { "c04", "#FFF",               "3-digit shorthand" },
    { "c05", "#FFFF",              "4-digit shorthand" },
    { "c06", "FFFFFF",             "hex with NO leading hash" },
    { "c07", "#FFFFF",             "5 digits, malformed" },
    { "c08", "#GGGGGG",            "non-hex characters" },
    { "c09", "red",                "named colour" },
    { "c10", "Red",                "named, mixed case" },
    { "c11", "RED",                "named, upper case" },
    { "c12", "grey",               "British spelling" },
    { "c13", "gray",               "American spelling" },
    { "c14", "transparent",        "handled by the mod, not Unity" },
    { "c15", "none",               "handled by the mod, not Unity" },
    { "c16", "rgba(255,0,0,1)",    "CSS function" },
    { "c17", "hsl(0,100%,50%)",    "CSS function" },
    { "c18", " #FFFFFF ",          "leading/trailing spaces" },
    { "c19", "#FFFFFF00",          "fully transparent, still a colour" },
    { "c20", "white",              "named white" },
}

local s = { "SCENE w=200 h=220 fit=stretch", "R x=0 y=0 w=200 h=220 f=#0B1622 fea=0" }
for i, case in ipairs(CASES) do
    local row = i - 1
    local y = 4 + row * 10.6
    -- The swatch. No `fea` so an edge artefact cannot be mistaken for a colour difference.
    s[#s + 1] = ("R x=4 y=%.1f w=16 h=8 f=$%s fea=0"):format(y, case[1])
    s[#s + 1] = ('T x=24 y=%.1f w=172 h=7 text="%s  %s" size=4.5 f=#9FB3C8'):format(
        y + 0.5, case[2], case[3])
end

ui:element({
    id = "strict_structure", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = { scene = "strict", src = table.concat(s, "\n") },
})

-- Data goes in its OWN element: set_props re-upserts the whole element, so a payload sharing an
-- element with the structure would wipe the structure.
local data = {}
for _, case in ipairs(CASES) do
    data[case[1]] = case[2]
end

ui:element({
    id = "strict_data", type = "vector",
    props = { scene = "strict", data = data },
})
