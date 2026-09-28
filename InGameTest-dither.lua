-- 0.11.49: the per-pixel dither shader, and the clipping it must not have broken.
--
-- Two things to look at, and the second matters as much as the first.
--
-- TOP HALF -- the banding. The same wide glow on four backgrounds. Stand close, face on.
--   * smooth ramps with at most a fine grain  -> the dither works.
--   * the same wide bands as before           -> the bundle did not load. Check the log for
--                                                "Dither shader loaded"; without that line the
--                                                mod is drawing with plain UI/Default.
--   * visible grain, worse than the bands      -> the amplitude is too high for this colour
--                                                space. Lower Renderer.Dither (try 0.5); it
--                                                applies live, no restart.
--
-- BOTTOM HALF -- the clip check. Both bars are drawn far outside the box they are clipped to,
-- inside a `CP` clip def, which is the clipping that a custom material could actually break.
--   * both bars stop at the pale outline -> clip defs still cut. This is the check that matters.
--   * either bar runs the full width or height of the panel -> the clip was not applied. Stop and
--     re-diff the shader against UI-Default.shader before anything else.
--
-- Note what this does NOT test, because the first version of this page got it wrong: a vector
-- element does not clip to its own rect, and never did. `RectMask2D` sits on the SURFACE root --
-- the whole console -- so geometry that overflows an element but stays on the console is bounded
-- by nothing, and that is existing behaviour, not a regression. Only the console edge and an
-- explicit `CP` are clips.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

local BG = { "#000000", "#0B1622", "#243447", "#4A5A6E" }

local glow = { "SCENE w=200 h=200 fit=stretch" }
for i, bg in ipairs(BG) do
    local y = (i - 1) * 50
    glow[#glow + 1] = ("R x=0 y=%d w=200 h=50 f=%s fea=0"):format(y, bg)
    glow[#glow + 1] = ("G blur=9 { R x=40 y=%d w=40 h=24 f=#5FD9A8 }"):format(y + 13)
    glow[#glow + 1] = ("G blur=9 { R x=130 y=%d w=40 h=24 f=#D95F7A }"):format(y + 13)
    glow[#glow + 1] = ('T x=6 y=%d w=28 h=8 text="%s" size=5 align=left f=#EAF4F8'):format(y + 21, bg)
end

ui:element({
    id = "dither_glow", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = math.floor(H * 0.72) },
    props = { scene = "dither_glow", src = table.concat(glow, "\n") },
})

-- Both bars run far past the clip box in both directions, so a clip that stopped being applied
-- is unmistakable rather than subtle.
local clip = {
    "SCENE w=200 h=100 fit=stretch",
    "DEFS {",
    " CP id=window { R x=40 y=20 w=120 h=40 rx=6 }",
    "}",
    "R x=0 y=0 w=200 h=100 f=#101820 fea=0",
    -- The outline marks where the clip is, drawn unclipped so it shows even if the clip eats
    -- everything: the bars must stop exactly here.
    "R x=40 y=20 w=120 h=40 rx=6 f=#00000000 fea=0 s=#8FA6B8 sw=0.6",
    "G clip=window {",
    "  R x=-400 y=30 w=1000 h=8 f=#FF3B30 fea=0",
    "  R x=90 y=-300 w=8 h=1000 f=#FF9500 fea=0",
    "}",
    'T x=20 y=70 w=160 h=10 text="both bars must stop at the outline" size=7 align=center f=#EAF4F8',
}

ui:element({
    id = "dither_clip", type = "vector",
    rect = { unit = "px", x = 40, y = math.floor(H * 0.74), w = W - 80, h = math.floor(H * 0.24) },
    props = { scene = "dither_clip", src = table.concat(clip, "\n") },
})
