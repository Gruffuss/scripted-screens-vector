-- 09 - Scrolling a list that is longer than the console
--
-- SC is a box that clips to itself and slides its children inside it. Wheel over it or drag
-- it and the content moves.
--
-- THE POINT IS WHAT DOES NOT HAPPEN. The scroll position lives on the client. A wheel notch
-- costs one mesh rebuild and nothing else: no tick, no network message, no instructions out
-- of the 50,000 the chip gets. Put the same list in a ScriptedScreens `scrollview` and every
-- scroll is a round trip, so it keeps up with the half-second tick rather than with the mouse.
--
-- WHAT YOU WRITE. Children are in CONTENT coordinates, measured from the container's own y.
-- A row at y = TOP + k*ROW sits where you would draw it if the box were tall enough to hold
-- everything, and the container works out what is visible.
--
--   ch    total content height. At or below h, nothing scrolls and the wheel is ignored.
--   id    REQUIRED -- it is the key the scroll position is stored under.
--
-- INSIDE A CONTAINER, `sy` AND `vh` REPORT THAT CONTAINER. `sy` is the scroll OFFSET and is
-- zero at rest; `vh` is the container's height. So the top of what is showing is the
-- container's own y PLUS sy, and pinned artwork is written that way:
--
--     y = "=" .. TOP .. "+sy"           the top edge, wherever it has scrolled to
--     y = "=" .. TOP .. "+sy+vh-10"     the bottom edge
--
-- Forgetting the TOP puts the artwork at the top of the VIEWBOX, above the container, where
-- the clip removes it and nothing appears to happen. `sy` is an offset in both places -- a
-- ScrollRect scrolls the whole element, so its content origin is already the scene origin and
-- no constant is needed there.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 480, 480
if size then W, H = size.w, size.h end

local ROWS    = 24         -- more rows than fit, which is the whole point
local ROW     = 22         -- scene units per row
local TOP     = 30         -- where the list box starts
local BOX     = 132        -- how tall the list box is
local CONTENT = ROWS * ROW + 8

-- Thumb geometry, worked out once here rather than inside three expressions. `sy` runs
-- 0..CONTENT-BOX and the thumb has BOX-THUMB of track, so it moves at TRAVEL times `sy` --
-- on top of the `sy` that keeps it on screen at all, since it is drawn inside the container.
local THUMB  = BOX * BOX / CONTENT
local TRAVEL = (BOX - THUMB) / (CONTENT - BOX)

ui:clear()

ui:element({
    id = "bg", type = "panel",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    style = { bg = "#0A121C" },
})

-- The container's children, built once. Stripes come from one RP, but the LABELS have to be
-- generated: a text node binds a data string by NAME, and there is no `$name[i]` for strings
-- the way there is for numbers. 24 small tables is a few hundred instructions, paid once --
-- the structure element is only resent when the list itself changes, not every tick.
local body = {
    { op = "RP", n = ROWS, c = {
        { op = "R", x = 16, y = "=" .. (TOP + 6) .. "+i*" .. ROW,
          w = 168, h = ROW - 4, rx = 3,
          f = "#12202F", fo = "=0.35+0.4*mod(i,2)" },

        -- A severity pip, lit for the ~18% of rows whose hash clears the step.
        { op = "C", cx = 24, cy = "=" .. (TOP + 6 + (ROW - 4) / 2) .. "+i*" .. ROW,
          rx = 2.5, ry = 2.5, f = "#5FD9A8",
          fo = "=0.2+0.8*step(0.82,hash(i))" },
    } },
}

for k = 0, ROWS - 1 do
    body[#body + 1] = {
        op = "T", x = 34, y = TOP + 6 + k * ROW, w = 130, h = ROW - 4,
        text = "$row" .. k, size = 8, valign = "middle", f = "#8FA6B8",
        fit = "ellipsis",
    }
end

-- Pinned last, so it draws over the rows.
local PIN = "=" .. TOP .. "+sy"

body[#body + 1] = { op = "R", x = 10, y = PIN, w = 180, h = 10, f = "@fadeDown" }
body[#body + 1] = { op = "R", x = 10, y = PIN .. "+vh-10", w = 180, h = 10, f = "@fadeUp" }
body[#body + 1] = { op = "R", x = 180, y = PIN, w = 3, h = BOX, rx = 1.5, f = "#12202F" }
body[#body + 1] = { op = "R", x = 180, w = 3, h = THUMB, rx = 1.5, f = "#3A5570",
                    y = PIN .. "+sy*" .. TRAVEL }

ui:element({
    id = "scroll_s", type = "vector",
    rect = { unit = "px", x = 12, y = 12, w = W - 24, h = H - 24 },
    props = {
        scene = "scroll",
        w = 200, h = 200,

        defs = {
            -- Fades to the well colour at both edges, so rows leave and arrive softly
            -- instead of being guillotined by the clip.
            { op = "GL", id = "fadeDown", units = "bbox", x1 = 0, y1 = 0, x2 = 0, y2 = 1,
              stops = { { 0, "#0B1622" }, { 1, "#0B162200" } } },
            { op = "GL", id = "fadeUp", units = "bbox", x1 = 0, y1 = 0, x2 = 0, y2 = 1,
              stops = { { 0, "#0B162200" }, { 1, "#0B1622" } } },
        },

        root = {
            { op = "T", x = 10, y = 8, w = 180, h = 14,
              text = "EVENT LOG", size = 9, cspace = 3, f = "#5A7085" },

            -- The well the list sits in. Drawn OUTSIDE the container, so the container's
            -- own clip does not cut its frame.
            { op = "R", x = 10, y = TOP, w = 180, h = BOX, rx = 6, f = "#0B1622" },

            { op = "SC", id = "log",
              x = 10, y = TOP, w = 180, h = BOX, ch = CONTENT, rx = 6, c = body },

            { op = "T", x = 10, y = TOP + BOX + 10, w = 180, h = 12,
              text = "wheel or drag the list", size = 7, align = "center", f = "#3A5570" },
        },
    },
})

-- Row text is one data element: 24 strings in one payload, and no element re-declared to
-- change a line. Strings live in `data` alongside the numbers.
local data = ui:element({
    id = "scroll_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "scroll", data = {} },
})

local sent = false

function tick(dt)
    -- A real log would resend when it changes. This one is fixed, so it goes out once.
    if sent then return end
    sent = true

    local payload = {}
    for k = 0, ROWS - 1 do
        payload["row" .. k] = string.format("%02d:%02d  event %d", (k * 7) % 24, (k * 13) % 60, k + 1)
    end

    data:set_props({ data = payload })
end
