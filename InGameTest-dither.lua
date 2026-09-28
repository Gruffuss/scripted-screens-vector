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
-- BOTTOM HALF -- the clip check, and it is the one that catches a bad shader. The scene's
-- viewbox is 200x100, and the red bar is drawn from x=-400 to x=600, far outside it, inside an
-- element that only covers the lower strip of the console.
--   * the red bar stops at the edges of its own strip -> clipping survived.
--   * the red bar runs across the whole console, or past it into the world -> the shader lost
--     _ClipRect, UNITY_UI_CLIP_RECT or the stencil block. Stop and re-diff it against
--     UI-Default.shader before anything else; every scene on every console is drawing outside
--     its bounds.

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

-- Deliberately out of bounds in both directions, so a lost clip rect is unmistakable.
local clip = {
    "SCENE w=200 h=100 fit=stretch",
    "R x=0 y=0 w=200 h=100 f=#101820 fea=0",
    "R x=-400 y=30 w=1000 h=18 f=#FF3B30 fea=0",
    "R x=60 y=-300 w=18 h=1000 f=#FF9500 fea=0",
    'T x=20 y=64 w=160 h=10 text="both bars must stop at this panel" size=7 align=center f=#EAF4F8',
}

ui:element({
    id = "dither_clip", type = "vector",
    rect = { unit = "px", x = 40, y = math.floor(H * 0.74), w = W - 80, h = math.floor(H * 0.24) },
    props = { scene = "dither_clip", src = table.concat(clip, "\n") },
})
