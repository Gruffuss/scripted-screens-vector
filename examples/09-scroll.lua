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
-- WHAT YOU WRITE. Children are in the same coordinates as everything around them -- put the
-- first row at the container's own y and the last `ch` below it -- and the container
-- translates them by the offset.
--
--   ch    total content height. At or below h, nothing scrolls and the wheel is ignored.
--   id    REQUIRED -- it is the key the scroll position is stored under.
--
-- THE WHOLE LIST IS ONE REPEAT. Backgrounds, pips and labels: one RP, one T, one array of
-- strings in the payload. `text = "$rows[i]"` takes the string for this instance, so 24 rows
-- cost 3 nodes rather than 72. The index is a full expression, so "$rows[n-1-i]" would show
-- the newest entry at the top without touching the data.
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

local PIN = "=" .. TOP .. "+sy"

ui:clear()

ui:element({
    id = "bg", type = "panel",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    style = { bg = "#0A121C" },
})

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
              x = 10, y = TOP, w = 180, h = BOX, ch = CONTENT, rx = 6, c = {

                { op = "RP", n = ROWS, c = {

                    -- Stripe.
                    { op = "R", x = 16, y = "=" .. (TOP + 6) .. "+i*" .. ROW,
                      w = 168, h = ROW - 4, rx = 3,
                      f = "#12202F", fo = "=0.35+0.4*mod(i,2)" },

                    -- Severity pip, coloured from an array. A colour is not a number, so an
                    -- expression has nothing to return for it -- but "$tints[i]" does.
                    { op = "C", cx = 24, cy = "=" .. (TOP + 6 + (ROW - 4) / 2) .. "+i*" .. ROW,
                      rx = 2.5, ry = 2.5, f = "$tints[i]" },

                    -- ONE text node for the whole list. Out of range draws `missing`, so a
                    -- payload shorter than n leaves blanks rather than repeating the last row.
                    { op = "T", x = 34, y = "=" .. (TOP + 6) .. "+i*" .. ROW,
                      w = 130, h = ROW - 4,
                      text = "$rows[i]", size = 8, valign = "middle", f = "#8FA6B8",
                      fit = "ellipsis", missing = "" },
                } },

                -- PINNED INSIDE THE CONTAINER, and drawn last so it sits over the rows.
                { op = "R", x = 10, y = PIN, w = 180, h = 10, f = "@fadeDown" },
                { op = "R", x = 10, y = PIN .. "+vh-10", w = 180, h = 10, f = "@fadeUp" },

                -- A scrollbar is not built in, because it is four numbers and everybody
                -- wants a different one. The track is pinned; the thumb rides `sy` twice.
                { op = "R", x = 180, y = PIN, w = 3, h = BOX, rx = 1.5, f = "#12202F" },
                { op = "R", x = 180, w = 3, h = THUMB, rx = 1.5, f = "#3A5570",
                  y = PIN .. "+sy*" .. TRAVEL },
            } },

            { op = "T", x = 10, y = TOP + BOX + 10, w = 180, h = 12,
              text = "wheel or drag the list", size = 7, align = "center", f = "#3A5570" },
        },
    },
})

-- `keep = 1` makes a payload a PATCH: names it does not mention hold their last value. Without
-- it, `data` is the whole truth, so this list would have to resend all 24 strings on every
-- tick or watch them vanish. With it, they go out once and only changes cost anything.
local data = ui:element({
    id = "scroll_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "scroll", keep = 1, data = {} },
})

local SEVERITY = { "#3A5570", "#5FD9A8", "#F59E0B", "#E23D3D" }

local sent = false

function tick(dt)
    -- A real log resends when it changes. This one is fixed, so it goes out once -- which is
    -- only safe because of `keep = 1` above.
    if sent then return end
    sent = true

    local rows, tints = {}, {}
    for k = 1, ROWS do
        rows[k]  = string.format("%02d:%02d  event %d", (k * 7) % 24, (k * 13) % 60, k)
        tints[k] = SEVERITY[(k % #SEVERITY) + 1]
    end

    -- Arrays are 0-based in expressions and 1-based in Lua, but that lines up by itself
    -- here: a repeat's `i` runs 0..n-1, so $rows[i] is rows[i+1].
    data:set_props({ data = { rows = rows, tints = tints } })
end
