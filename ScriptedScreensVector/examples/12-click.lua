-- 12 - Clicks, and changing a node without resending the scene
--
-- Two things that both hang off a node's `id`.
--
-- CLICKS. A node with an id and `click = 1` becomes a hit region. The click arrives at the
-- VECTOR ELEMENT's own on_click, and the node id comes through as the event's VALUE:
--
--     on_click = function(nodeId, player) ... end
--
-- That is not an oversight. Lua registers handlers per ELEMENT, and a vector node is not an
-- element, so one handler serves the whole scene rather than one per row. It replaces the
-- old workaround of laying a transparent `button` element over every clickable thing.
--
--   * `click = 1` is OPT-IN and separate from having an id -- an id is also a patch target,
--     and making every patch target swallow clicks would be a nasty surprise.
--   * A scene with nothing clickable stays transparent to the pointer, so decoration never
--     steals a click from a button underneath it.
--   * Hit testing is against the node's BOUNDING BOX, in draw order, last match wins. For
--     rows and tiles the bounds are the shape. A ring or a thin diagonal claims more than it
--     draws -- and an invisible R with fo = 0 makes a hit area of any size for no geometry.
--
-- PATCHING. The data element can rewrite a node's attributes by id:
--
--     data:set_props({ nodes = { sel = { y = 60 } } })
--
-- The patch MERGES onto the node's original props and re-parses that node, so keys you leave
-- out keep their values and children are untouched. Reach for it only when an expression
-- cannot say the thing -- a different op, a new gradient reference, a changed count. Anything
-- that is merely a value belongs in `data` with an expression reading it, which re-parses
-- nothing at all. Both are shown below so the difference is visible.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 480, 480
if size then W, H = size.w, size.h end

local ROWS = { "OXYGEN", "NITROGEN", "CARBON DIOXIDE", "VOLATILES", "POLLUTANT" }
local ROW_H, ROW_Y = 24, 40

ui:clear()

ui:element({
    id = "bg", type = "panel",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    style = { bg = "#0A121C" },
})

local rows = {}
for k = 1, #ROWS do
    local y = ROW_Y + (k - 1) * (ROW_H + 4)

    -- The plate. `click = 1` plus an id is the whole of it.
    rows[#rows + 1] = {
        op = "R", id = "row" .. k, click = 1,
        x = 16, y = y, w = 168, h = ROW_H, rx = 4, f = "#12202F",
    }

    rows[#rows + 1] = {
        op = "T", x = 26, y = y, w = 120, h = ROW_H,
        text = ROWS[k], size = 8, valign = "middle", f = "#8FA6B8",
    }

    -- A per-row value, driven by `data` -- no patch, no re-parse.
    rows[#rows + 1] = {
        op = "R", x = 150, y = y + 9, w = 26, h = 6, rx = 3, f = "#5FD9A8",
        fo = "=0.25+0.75*$v" .. (k - 1),
    }
end

local ACCENTS = { "#5FD9A8", "#4E8FD9", "#F59E0B", "#E23D3D", "#9B7FD9" }
local data
local picked = "nothing selected"

local function on_row(nodeId, player)
    local k = tonumber((nodeId or ""):match("^row(%d+)$"))
    if not k then return end

    local y = ROW_Y + (k - 1) * (ROW_H + 4) - 2
    picked = ROWS[k] .. "  //  " .. (player or "?")

    -- One set_props carrying both patches and the data payload. `nodes` and `data` are
    -- separate props, so a patch does not disturb the values and vice versa.
    data:set_props({
        nodes = {
            sel    = { y = y },
            accent = { y = y, f = ACCENTS[k] },
        },
        data = { picked = picked },
    })

    ui:commit()
end

ui:element({
    id = "click_s", type = "vector",
    on_click = on_row,
    rect = { unit = "px", x = 12, y = 12, w = W - 24, h = H - 24 },
    props = {
        scene = "click",
        w = 200, h = 200,

        root = {
            { op = "T", x = 16, y = 10, w = 168, h = 12,
              text = "TAP A ROW", size = 8, cspace = 4, f = "#5A7085" },

            -- The selection highlight, drawn UNDER the rows. Its `y` is patched when the
            -- selection changes: a value an expression could carry too, but patching is
            -- shown here because the SHAPE also changes -- see the accent bar below.
            { op = "R", id = "sel", x = 14, y = ROW_Y - 2, w = 172, h = ROW_H + 4, rx = 5,
              f = "#1E3247" },

            { op = "G", c = rows },

            -- The accent bar's colour is patched, because a colour is not a number and an
            -- expression has nothing to return for it. (A gradient sampled at an expression
            -- is the other way to animate colour -- see 06-paint.)
            { op = "R", id = "accent", x = 14, y = ROW_Y - 2, w = 3, h = ROW_H + 4, rx = 1.5,
              f = "#5FD9A8" },

            { op = "T", x = 16, y = 176, w = 168, h = 12,
              text = "$picked", size = 8, align = "center", f = "#5A7085" },
        },
    },
})

data = ui:element({
    id = "click_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "click", data = { picked = "nothing selected" } },
})

local phase = 0

function tick(dt)
    phase = phase + (dt or 0.5)

    -- `data` is ONE prop, so it is replaced wholesale rather than merged key by key.
    -- Anything the scene still needs has to be resent with it -- here, the picked line.
    local values = { picked = picked }
    for k = 1, #ROWS do
        values["v" .. (k - 1)] = 0.5 + 0.5 * math.sin(phase * (0.2 + k * 0.07))
    end

    data:set_props({ data = values })
    ui:commit()
end
