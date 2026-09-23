-- Colour change and fading.
--
-- Two different mechanisms, and the difference matters:
--
--   FADE  -- `fo` / `so` are expressions, so opacity animates on the client at display
--            refresh rate. Lua sends nothing.
--   CHANGE -- `f = "$name"` binds the colour to the data payload. The host decides the
--            colour and sends it; the structure is never resent.
--   BLEND  -- `f = { grad = "name", at = "=expr" }` samples a colour ramp at an
--            expression. This is genuine colour interpolation: over `t` for animation,
--            or over `$data` so the host sends a number and the colour follows.
--
-- Use fading for anything continuous (pulsing, breathing, blinking). Use the data
-- binding for anything the host decides (a threshold crossing, an alarm, a mode).

local W, H = 480, 480

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
if size then W, H = size.w, size.h end

ui:clear()

ui:element({
    id = "bg",
    type = "panel",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    style = { bg = "#070D16" },
})

ui:element({
    id = "title",
    type = "label",
    rect = { unit = "px", x = 14, y = 8, w = W - 28, h = 22 },
    props = { text = "COLOUR CHANGE / FADE" },
    style = { font_size = 15, color = "#5FD9A8", align = "left" },
})

local scene = {
    scene = "colour",
    w = 200, h = 200, fit = "stretch",
    defs = {
        { op = "GL", id = "sweep", x1 = 12, y1 = 0, x2 = 188, y2 = 0,
          stops = { { 0, "#2E8B6E" }, { 0.5, "#5FD9A8" }, { 1, "#F59E0B" } } },

        -- A ramp used for colour rather than for position: nominal -> warning -> alarm.
        -- Its geometry is ignored when sampled with `at`.
        { op = "GL", id = "status", x1 = 0, y1 = 0, x2 = 1, y2 = 0,
          stops = { { 0, "#5FD9A8" }, { 0.5, "#F59E0B" }, { 1, "#E23D3D" } } },
    },
    root = {
        -- 1. Client-side fade. Opacity is an expression over t, so this breathes at
        --    display refresh rate with no traffic at all.
        { op = "R", x = 12, y = 26, w = 80, h = 34, rx = 6,
          f = "#5FD9A8", fo = "=0.15+0.85*tri(t*0.4)" },

        -- 2. Host-driven colour. $alarm is re-read from the data payload each tick, so
        --    the structure never has to be resent to change colour.
        { op = "R", x = 108, y = 26, w = 80, h = 34, rx = 6, f = "$alarm" },

        -- 3. Both at once: data-bound colour, client-side pulse. The pulse rate is
        --    also data-driven, so the host can make it urgent without resending anything.
        { op = "C", cx = 52, cy = 108, rx = 26, ry = 26,
          f = "$alarm", fo = "=0.3+0.7*tri(t*$rate)" },

        -- 4. Stroke colour is bindable too.
        { op = "C", cx = 148, cy = 108, rx = 26, ry = 26,
          s = "$alarm", sw = "=2+2*tri(t*$rate)" },

        -- 5. Real colour interpolation: sample a ramp at an expression instead of by
        --    position. One shape, and it blends through every stop rather than
        --    crossfading two stacked shapes.
        { op = "R", x = 12, y = 150, w = 84, h = 30, rx = 6,
          f = { grad = "status", at = "=tri(t*0.3)" } },

        -- 6. Same mechanism driven by data rather than time: the host sends a number and
        --    the colour follows the ramp. This is the alarm case, without sending a colour.
        { op = "R", x = 104, y = 150, w = 84, h = 30, rx = 6,
          f = { grad = "status", at = "=$level" } },

        -- 7. A gradient bar for reference: static colours, no animation.
        { op = "R", x = 12, y = 188, w = 176, h = 8, rx = 4, f = "@sweep" },
    },
}

ui:element({
    id = "colour_structure",
    type = "vector",
    rect = { unit = "px", x = 14, y = 36, w = W - 28, h = H - 50 },
    props = scene,
})

local data = ui:element({
    id = "colour_data",
    type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "colour", data = { alarm = "#5FD9A8", rate = 0.4, level = 0 } },
})

ui:commit()

-- Cycle through three states a few seconds apart. Each tick sends one colour string and
-- one number; everything else is already on the client.
local STATES = {
    { colour = "#5FD9A8", rate = 0.4, level = 0.0 },   -- nominal
    { colour = "#F59E0B", rate = 1.2, level = 0.5 },   -- warning, pulses faster
    { colour = "#E23D3D", rate = 2.6, level = 1.0 },   -- alarm, faster still
}

local elapsed = 0

function tick(dt)
    elapsed = elapsed + (dt or 0.5)

    local state = STATES[(math.floor(elapsed / 4) % #STATES) + 1]
    data:set_props({ data = { alarm = state.colour, rate = state.rate, level = state.level } })
    ui:commit()
end
