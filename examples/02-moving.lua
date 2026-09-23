-- 02 — Motion without a tick
--
-- Any numeric attribute can be an EXPRESSION: a string starting with "=". The client
-- evaluates it every frame against `t`, the number of seconds since the scene appeared.
--
-- This file has no tick() and sends nothing after the first commit, yet everything moves.
-- That is the whole point of the vector layer: animation is described, not driven.
--
-- WHAT TO NOTICE
--   * sin/cos for smooth back-and-forth, tri for a linear there-and-back, saw for a
--     repeating ramp that snaps back, pulse for a hard on/off.
--   * A group `G` with an expression for `r` rotates everything inside it.
--   * Motion is in viewbox units, so it scales with the console like everything else.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

-- Consoles come in different sizes. Ask, and fall back to a sensible default.
-- The artwork itself does not care: it is written in viewbox units and scales.
local size = ui:size()
local W, H = 480, 480
if size then W, H = size.w, size.h end

ui:clear()

ui:element({
    id = "bg", type = "panel",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    style = { bg = "#0A121C" },
})

ui:element({
    id = "moving", type = "vector",
    rect = { unit = "px", x = 12, y = 12, w = W - 24, h = H - 24 },
    props = {
        scene = "moving",
        w = 100, h = 100,
        root = {
            -- 1. Slide. sin(t) runs -1..1, so this sweeps 30 units either side of centre.
            --    The multiplier on t is the speed in radians per second.
            { op = "R", x = "=42+30*sin(t*1.5)", y = 10, w = 16, h = 8, rx = 4,
              f = "#5FD9A8" },

            -- 2. Breathe. `fo` is fill opacity and is an expression like anything else.
            --    tri() is a triangle wave over 0..1, which reads as a steadier pulse than
            --    sin because it has no lingering at the extremes.
            { op = "C", cx = 25, cy = 40, rx = 12, ry = 12, f = "#F59E0B",
              fo = "=0.25+0.75*tri(t*0.5)" },

            -- 3. Grow. Radii driven by the same clock, a quarter-phase apart, which makes
            --    the circle bulge in one axis then the other.
            { op = "C", cx = 75, cy = 40,
              rx = "=8+5*sin(t*2)", ry = "=8+5*cos(t*2)", f = "#E23D3D" },

            -- 4. Rotate. The GROUP carries the rotation; the shape inside stays simple.
            --    `t` is the translate {x, y} and `r` the angle in degrees, clockwise.
            --    Everything inside rotates about the group's anchor, default {0, 0}, which
            --    after the translate means about (50, 70).
            { op = "G", t = { 50, 70 }, r = "=t*60", c = {
                { op = "R", x = -1.5, y = -20, w = 3, h = 20, rx = 1.5, f = "#8FE8C8" },
                { op = "C", cx = 0, cy = 0, rx = 3, ry = 3, f = "#8FE8C8" },
            } },

            -- 5. Blink. pulse(x, duty) is 1 for the first `duty` fraction of each period
            --    and 0 for the rest -- a hard state change, no easing.
            { op = "R", x = 8, y = 88, w = 8, h = 6, rx = 1,
              f = "#E23D3D", fo = "=pulse(t*1.2, 0.5)" },

            -- 6. Travel and wrap. mod() keeps the result positive, unlike the % operator,
            --    so this marches right and jumps back cleanly forever.
            { op = "R", x = "=mod(t*14, 100)", y = 70, w = 4, h = 4, f = "#5FD9A8" },
        },
    },
})

ui:commit()
