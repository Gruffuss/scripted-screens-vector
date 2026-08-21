-- 01 — Hello, vector
--
-- The smallest thing that draws. Paste into a chip attached to a console and it runs.
--
-- WHAT TO NOTICE
--   * One element, type = "vector".
--   * `props.w` / `props.h` declare a VIEWBOX -- an imaginary drawing area. Everything
--     inside `root` is written in viewbox units, and the renderer maps that onto whatever
--     size the element actually is. Change the console size and the picture scales; you
--     never write pixel coordinates.
--   * Coordinates are TOP-LEFT origin, +Y DOWN. Same as the old canvas, opposite of maths.
--
-- Nothing here animates and nothing is sent per tick, so after the first frame this console
-- costs literally nothing.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

-- Consoles come in different sizes. Ask, and fall back to a sensible default.
-- The artwork itself does not care: it is written in viewbox units and scales.
local size = ui:size()
local W, H = 480, 480
if size then W, H = size.w, size.h end

ui:clear()

-- A plain dark backdrop, using ScriptedScreens' own panel element. The vector layer draws
-- on top of it. You could equally draw a full-size rectangle in the scene.
ui:element({
    id = "bg", type = "panel",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    style = { bg = "#0A121C" },
})

ui:element({
    id = "hello", type = "vector",
    rect = { unit = "px", x = 12, y = 12, w = W - 24, h = H - 24 },

    props = {
        scene = "hello",        -- a name for this scene; see example 03 for why it matters
        w = 100, h = 100,       -- viewbox: this artwork is 100x100 units

        root = {
            -- A rectangle. `rx` rounds the corners -- a real arc, not a fake.
            { op = "R", x = 5, y = 5, w = 90, h = 90, rx = 6,
              f = "#12202F", s = "#1E3247", sw = 1 },

            -- An ellipse. `C` takes a centre and two radii; equal radii give a circle.
            { op = "C", cx = 50, cy = 38, rx = 22, ry = 22, f = "#2E8B6E" },

            -- A rectangle with no stroke, filled with a semi-transparent colour.
            -- Colours are "#rrggbb" or "#rrggbbaa".
            { op = "R", x = 25, y = 66, w = 50, h = 10, rx = 5, f = "#5FD9A8CC" },
        },
    },
})

ui:commit()

-- No tick() function at all. A scene with no data and no animation never needs one.
