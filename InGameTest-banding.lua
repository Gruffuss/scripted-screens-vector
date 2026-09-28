-- Where does the stair-stepping in a soft glow come from: our geometry, or the screen's
-- handling of dark colours?
--
-- The SAME glow four times, over four backgrounds from black to light. The geometry is
-- identical in each; only what it fades into changes.
--
-- What to see, standing close:
--   * if the steps are worst on the BLACK panel and fade away as the background lightens,
--     they are the display's: dark values have far fewer levels to land on, so a slow fade
--     into near-black has to jump. Nothing in the renderer can change that.
--   * if all four step the same, the fault is in the mesh and I keep looking.
--
-- The right-hand column repeats it with a SMALL blur, where the fade is short and steep. Steps
-- need a long slow ramp to show, so these should look clean even on black.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

local BG = { "#000000", "#0B1622", "#243447", "#4A5A6E" }

local lines = { "SCENE w=200 h=200 fit=stretch" }
for i, bg in ipairs(BG) do
    local y = (i - 1) * 50
    lines[#lines + 1] = ("R x=0 y=%d w=200 h=50 f=%s fea=0"):format(y, bg)
    lines[#lines + 1] = ("G blur=9 { R x=30 y=%d w=30 h=24 f=#5FD9A8 }"):format(y + 13)
    lines[#lines + 1] = ("G blur=2 { R x=130 y=%d w=30 h=24 f=#5FD9A8 }"):format(y + 13)
    lines[#lines + 1] = ('T x=70 y=%d w=56 h=8 text="%s" size=6 align=center f=#EAF4F8'):format(y + 21, bg)
end

ui:element({
    id = "band_s", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = { scene = "band", src = table.concat(lines, "\n") },
})

ui:commit()
