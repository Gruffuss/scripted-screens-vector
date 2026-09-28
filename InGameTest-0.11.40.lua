-- 0.11.40 — a label with fit="ellipsis" in a box shorter than its own line.
--
-- Every row asks for `fit = "ellipsis"` on a long string. The box gets shorter down the page
-- while the text size stays the same, so rows 3 and 4 have less height than one line needs.
--
-- What to see:
--   * EVERY row shows text. Before 0.11.40 a row whose box was shorter than one line drew
--     nothing at all: the whole title vanished rather than being cut short.
--   * Each row is cut on the RIGHT with an ellipsis, not wrapped and not scaled down.
--   * Rows 5, 6 and 7 are the same short box with valign top, middle and bottom. The line must
--     grow away from the edge the valign pins: 5 sits against the top of its grey box, 6
--     centred in it, 7 against the bottom.
--   * The grey boxes show where each label was asked to go, so any drift is visible.
--
-- This one cannot be checked outside the game: the text engine measures the line, and it does
-- not run headless.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 480, 480
if size then W, H = size.w, size.h end

ui:clear()

local TEXT = "ATMOSPHERE REGULATOR OVERRIDE PANEL"

-- { label height, text size, valign }
local rows = {
    { 34, 20, "middle" },
    { 26, 20, "middle" },
    { 23, 20, "middle" },   -- shorter than one line at size 20
    { 18, 16, "middle" },   -- shorter than one line at size 16
    { 19, 16, "top" },
    { 19, 16, "middle" },
    { 19, 16, "bottom" },
}

local lines = { "SCENE w=200 h=280 fit=stretch", "R x=0 y=0 w=200 h=280 f=#070D16" }
local y = 6
for i, row in ipairs(rows) do
    local h, s, valign = row[1], row[2], row[3]
    lines[#lines + 1] = ("R x=6 y=%d w=188 h=%d f=#1E3247 fea=0"):format(y, h)
    lines[#lines + 1] = ("T x=6 y=%d w=188 h=%d text=\"%d %s\" size=%d valign=%s fit=ellipsis f=#EAF4F8")
        :format(y, h, i, TEXT, s, valign)
    lines[#lines + 1] = ("T x=6 y=%d w=188 h=7 text=\"row %d: box h=%d, size %d, valign %s\" size=5 f=#5FD9A8")
        :format(y + h + 1, i, h, s, valign)
    y = y + h + 12
end

ui:element({
    id = "t40_s", type = "vector",
    rect = { unit = "px", x = 6, y = 6, w = W - 12, h = H - 12 },
    props = { scene = "t40", src = table.concat(lines, "\n") },
})

ui:commit()
