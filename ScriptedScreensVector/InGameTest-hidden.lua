-- 0.11.31 / 0.11.33 — hidden groups (`o = 0`, `v = 0`) and picture placement (`at`).
--
-- Left column, top to bottom. Every dashed outline marks where something invisible is:
--   1. VISIBLE   a green bar. The control: if this is missing, nothing below means anything.
--   2. O = 0     a red bar inside `G o=0`. Must NOT be seen.
--   3. O = 0     the same, clickable. Invisible, but a click inside the outline MUST register
--                (opacity 0 stays clickable, as in a browser). The readout says "ghost".
--   4. V = 0     the same with `G v=0`. Invisible AND not there: a click inside the outline
--                must register NOTHING. The readout keeps its previous value.
--   5. V = $show a bar that appears and disappears every two seconds, driven by data alone.
--
-- Right column, the same picture four times (the mod's own thumbnail, a wide image):
--   6. COVER at {0, 0.5} and {1, 0.5}  -- square boxes; the left one shows the LEFT end of
--      the picture and the right one the RIGHT end. If both show the middle, `at` is ignored.
--   7. CONTAIN at {0.5, 0} and {0.5, 1} -- tall boxes; the picture sits at the TOP of the left
--      one and the BOTTOM of the right one.
--
-- The readout at the bottom shows the id of the last click, so a capture answers the click
-- question without the chip log.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 480, 480
if size then W, H = size.w, size.h end

local PICTURE = "https://raw.githubusercontent.com/Gruffuss/scripted-screens-vector/main/ScriptedScreensVector/About/thumb.png"

local data

local function on_hit(nodeId, player)
    data:set_props({ keep = 1, data = { last = tostring(nodeId) } })
    ui:commit()
end

ui:clear()

ui:element({
    id = "bg", type = "panel",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    style = { bg = "#070D16" },
})

local function label(y, text)
    return { op = "T", x = 8, y = y, w = 90, h = 8, text = text, size = 5.5, f = "#5FD9A8" }
end

local function outline(y)
    return { op = "R", x = 8, y = y, w = 84, h = 14, rx = 3, f = "none",
             s = "#5A7085", sw = 0.6, dash = { 2, 1.5 } }
end

ui:element({
    id = "hid_s", type = "vector",
    on_click = on_hit,
    rect = { unit = "px", x = 6, y = 6, w = W - 12, h = H - 12 },
    props = {
        scene = "hidden",
        w = 200, h = 200,
        root = {
            -- 1. the control
            label(4, "VISIBLE"),
            { op = "R", x = 8, y = 13, w = 84, h = 14, rx = 3, f = "#5FD9A8" },

            -- 2. o = 0, not clickable: nothing to see
            label(32, "O = 0"),
            outline(41),
            { op = "G", o = 0, c = {
                { op = "R", x = 8, y = 41, w = 84, h = 14, rx = 3, f = "#E23D3D" },
            } },

            -- 3. o = 0, clickable: nothing to see, but a click lands
            label(60, "O = 0, CLICK INSIDE"),
            outline(69),
            { op = "G", o = 0, c = {
                { op = "R", id = "ghost", click = 1, x = 8, y = 69, w = 84, h = 14, rx = 3, f = "#E23D3D" },
            } },

            -- 4. v = 0, clickable: nothing to see, and nothing to click
            label(88, "V = 0, CLICK INSIDE"),
            outline(97),
            { op = "G", v = 0, c = {
                { op = "R", id = "gone", click = 1, x = 8, y = 97, w = 84, h = 14, rx = 3, f = "#E23D3D" },
            } },

            -- 5. v from the payload
            label(116, "V = $show (BLINKS)"),
            outline(125),
            { op = "G", v = "$show", c = {
                { op = "R", x = 8, y = 125, w = 84, h = 14, rx = 3, f = "#F59E0B" },
            } },

            -- the click readout
            { op = "T", x = 8, y = 150, w = 184, h = 10, text = "$last",
              size = 7, f = "#EAF4F8", missing = "no click yet" },

            -- 6. cover: which end of the picture is kept
            { op = "T", x = 104, y = 4, w = 90, h = 8, text = "COVER  AT 0 | AT 1", size = 5.5, f = "#5FD9A8" },
            { op = "IMG", x = 104, y = 13, w = 42, h = 42, src = PICTURE, fit = "cover", at = { 0, 0.5 }, rx = 3 },
            { op = "IMG", x = 150, y = 13, w = 42, h = 42, src = PICTURE, fit = "cover", at = { 1, 0.5 }, rx = 3 },

            -- 7. contain: where the picture sits in a tall box
            { op = "T", x = 104, y = 60, w = 90, h = 8, text = "CONTAIN  TOP | BOTTOM", size = 5.5, f = "#5FD9A8" },
            { op = "R", x = 104, y = 69, w = 42, h = 76, rx = 3, f = "#12202F" },
            { op = "R", x = 150, y = 69, w = 42, h = 76, rx = 3, f = "#12202F" },
            { op = "IMG", x = 104, y = 69, w = 42, h = 76, src = PICTURE, fit = "contain", at = { 0.5, 0 } },
            { op = "IMG", x = 150, y = 69, w = 42, h = 76, src = PICTURE, fit = "contain", at = { 0.5, 1 } },
        },
    },
})

data = ui:element({
    id = "hid_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "hidden", keep = 1, data = { show = 1 } },
})

ui:commit()

local elapsed = 0

function tick(dt)
    elapsed = elapsed + (dt or 0.5)

    -- snap, so the blink is a clean on/off rather than a v that eases through 0.5
    data:set_props({ keep = 1, snap = 1, data = { show = (math.floor(elapsed / 2) % 2 == 0) and 1 or 0 } })
    ui:commit()
end
