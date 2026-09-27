-- Colour fidelity: is a flat colour drawn exactly, and does the vector layer differ from
-- ScriptedScreens' own elements?
--
-- Each row is one colour drawn twice: a vector `R` on the left, a ScriptedScreens `panel` of
-- the same colour on the right. Capture the console and read one pixel from the middle of each
-- patch. WHAT MATTERS IS WHETHER THE TWO HALVES OF A ROW AGREE, not whether either is exact.
--
-- Captures of a vector console came back a few levels off at the dark end, which is exactly
-- what one round trip through an 8-bit LINEAR buffer does: near black, 8-bit linear holds only
-- about one and a half levels. Predicted under that model:
--
--   asked        predicted
--   #080C10   8, 12, 16  ->  13, 13, 13    (the three channels collapse onto each other)
--   #12202F  18, 32, 47  ->  22, 34, 46
--   #1E3247  30, 50, 71  ->  28, 50, 71
--   #808080 128,128,128  -> 128,128,128    (mid grey is unaffected)
--   #FFFFFF 255,255,255  -> 255,255,255
--
-- Reading it:
--   * both halves shifted the same way -> it is the shared UI or capture path, not the vector
--     layer, and it applies to every ScriptedScreens console.
--   * only the vector half shifted -> it is ours, and worth chasing.
--   * neither shifted -> the earlier reading was something about that page, not colour.
-- Worth doing both ways: read the same patches from a screenshot of the live screen as well, to
-- tell a capture artefact from what is actually drawn.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 480, 480
if size then W, H = size.w, size.h end

ui:clear()

local rows = {
    { "#080C10", "8,12,16" },
    { "#12202F", "18,32,47" },
    { "#1E3247", "30,50,71" },
    { "#808080", "128,128,128" },
    { "#FFFFFF", "255,255,255" },
}

-- Left half: the vector layer. 200-unit scene stretched over the element.
local lines = { "SCENE w=200 h=200 fit=stretch", "R x=0 y=0 w=200 h=200 f=#2A2A2A" }
for i, row in ipairs(rows) do
    local y = 6 + (i - 1) * 38
    lines[#lines + 1] = ("R x=6 y=%d w=90 h=30 f=%s fea=0"):format(y, row[1])
    lines[#lines + 1] = ("T x=6 y=%d w=90 h=8 text=\"%s %s\" size=5 f=#EAF4F8"):format(y + 31, row[1], row[2])
end

ui:element({
    id = "col_s", type = "vector",
    rect = { unit = "px", x = 0, y = 0, w = math.floor(W / 2), h = H },
    props = { scene = "col", src = table.concat(lines, "\n") },
})

-- Right half: ScriptedScreens' own panels, same colours, same order.
local half = math.floor(W / 2)
local step = math.floor(H / #rows)
for i, row in ipairs(rows) do
    ui:element({
        id = "panel" .. i, type = "panel",
        rect = { unit = "px", x = half + 8, y = (i - 1) * step + 6, w = half - 16, h = step - 22 },
        style = { bg = row[1] },
    })
    ui:element({
        id = "plab" .. i, type = "label",
        rect = { unit = "px", x = half + 8, y = (i - 1) * step + step - 14, w = half - 16, h = 12 },
        props = { text = row[1] .. "  (ScriptedScreens panel)" },
        style = { font_size = 9, color = "#EAF4F8" },
    })
end

ui:commit()
