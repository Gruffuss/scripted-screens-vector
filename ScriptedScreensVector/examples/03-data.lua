-- 03 — Live data, and why a scene is TWO elements
--
-- Expressions can read values your script sends, written $name. That needs a second
-- element, and the reason is worth understanding because getting it wrong is slow rather
-- than broken -- everything still works, the console just costs what the old canvas cost.
--
-- THE RULE
--   set_props MERGES what you give it into the element's existing props and then resends
--   the WHOLE element. So if `data` lived beside `root`, every tick would resend the entire
--   scene tree, forever.
--
--   Structure element : sent once. Big. Never touched again.
--   Data element      : sent every tick. A handful of numbers.
--
--   They find each other by having the same `scene` name.
--
-- The data element still has to exist on the surface, so park it off-screen at 1x1 where
-- it draws nothing.

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

-- ---------------------------------------------------------------- structure (sent once)
ui:element({
    id = "gauge_s", type = "vector",
    rect = { unit = "px", x = 12, y = 12, w = W - 24, h = H - 24 },
    props = {
        scene = "gauge",
        w = 100, h = 100,
        root = {
            -- Track and fill. Because y is measured downward, a taller bar also has to
            -- start higher up -- both attributes move together.
            { op = "R", x = 12, y = 15, w = 20, h = 70, rx = 3, f = "#12202F" },
            { op = "R", x = 12, y = "=85-clamp($level,0,1)*70",
              w = 20, h = "=clamp($level,0,1)*70", rx = 3, f = "#2E8B6E" },

            -- A needle: -120..+120 degrees across the same 0..1 range.
            { op = "G", t = { 65, 60 }, r = "=-120+clamp($level,0,1)*240", c = {
                { op = "R", x = -1, y = -28, w = 2, h = 28, f = "#F59E0B" },
            } },
            { op = "C", cx = 65, cy = 60, rx = 3, ry = 3, f = "#F59E0B" },

            -- Data and time mix freely in one expression. This ring pulses faster when the
            -- level is high -- no extra data, the client does the arithmetic.
            { op = "C", cx = 65, cy = 60, rx = 32, ry = 32, f = "none",
              s = "#5FD9A8", sw = 1,
              so = "=0.15+0.35*tri(t*(0.3+$level))" },

            -- ALWAYS clamp data you did not generate yourself. A sensor reading outside the
            -- range you designed for will happily draw a bar off the top of the console.
            { op = "R", x = 12, y = 90, w = "=20*clamp($level,0,1)", h = 3, f = "#5FD9A8" },
        },
    },
})

-- ---------------------------------------------------------------- data (sent every tick)
local data = ui:element({
    id = "gauge_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },   -- off-screen, draws nothing
    props = { scene = "gauge", data = { level = 0.5 } },
})

ui:commit()

-- ---------------------------------------------------------------------------------- tick
local elapsed = 0

function tick(dt)
    elapsed = elapsed + (dt or 0.5)

    -- Replace with a real reading, e.g. local level = tank.Pressure / 60000
    local level = 0.5 + 0.45 * math.sin(elapsed * 0.25)

    data:set_props({ data = { level = level } })
    ui:commit()
end

-- A NOTE ON SMOOTHNESS
--
-- tick() runs about twice a second; the scene draws at up to 30 Hz. A value read straight
-- from the payload would therefore step visibly, and a stepping needle beside a smoothly
-- pulsing ring looks broken.
--
-- The renderer eases $name values across the gap between payloads, and measures that gap
-- itself, so it adapts to whatever rate you tick at. You get this for free.
--
-- The cost: a value that should snap does not. A discrete mode flip eases over about one
-- tick. If that matters, set Renderer.SmoothData = false in the mod config.
