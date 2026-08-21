-- 04 — Repeats: the most important node in the format
--
-- RP instantiates its children `n` times, with `i` bound to 0..n-1 inside.
--
-- WHY IT MATTERS MORE THAN IT LOOKS
--   A chip gets about 50,000 Lua instructions per tick, and building one node costs roughly
--   a dozen. So you can construct around 4,000 nodes per tick, total. Writing 400 motes out
--   as 400 tables spends a tenth of your entire budget on one piece of decoration.
--
--   RP with n = 400 costs ONE node.
--
-- THE TRAP THAT CATCHES EVERYONE
--   A repeat instantiates its children UNCHANGED. Nothing varies by itself. If no attribute
--   mentions `i`, all n copies land exactly on top of each other and you see one shape.
--   Vary something with `i`, or nothing happens.

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
    id = "rep_s", type = "vector",
    rect = { unit = "px", x = 12, y = 12, w = W - 24, h = H - 24 },
    props = {
        scene = "rep",
        w = 100, h = 100,
        root = {

            -- 1. A plain row. `i` spaces them; nothing else changes.
            { op = "RP", n = 12, c = {
                { op = "R", x = "=6+i*7.5", y = 8, w = 5, h = 5, rx = 1, f = "#2E8B6E" },
            } },

            -- 2. The same row, animated as a travelling wave. Offsetting the PHASE by `i`
            --    is what turns twelve independent bobs into one ripple.
            { op = "RP", n = 12, c = {
                { op = "R", x = "=6+i*7.5", y = "=20+4*sin(t*2+i*0.5)",
                  w = 5, h = 5, rx = 1, f = "#5FD9A8" },
            } },

            -- 3. A bar chart from an array. $name[expr] indexes data, and an out-of-range
            --    read gives 0 rather than failing, so a short array degrades quietly.
            { op = "RP", n = 10, c = {
                { op = "R", x = "=6+i*9", y = "=56-clamp($bars[i],0,1)*18",
                  w = 6, h = "=clamp($bars[i],0,1)*18", f = "#F59E0B" },
            } },

            -- 4. Radial layout. tau() is 2*pi, so i/n*tau() walks a full circle. This is
            --    the pattern for anything arranged around a centre.
            { op = "RP", n = 16, c = {
                { op = "C", cx = "=78+14*cos(i/n*tau()-pi()/2)",
                  cy = "=78+14*sin(i/n*tau()-pi()/2)",
                  rx = 1.6, ry = 1.6, f = "#8FE8C8",
                  fo = "=0.3+0.7*tri(t*0.5+i/n)" },
            } },

            -- 5. Tick marks around a dial. The rotation has to be a GROUP INSIDE the
            --    repeat: the group angle varies with `i` and the shape inside stays simple.
            --    Rotating the repeat itself would rotate every copy together.
            { op = "G", t = { 28, 78 }, c = {
                { op = "RP", n = 9, c = {
                    { op = "G", r = "=-120+i*30", c = {
                        { op = "R", x = -0.6, y = -16, w = 1.2, h = 4, f = "#5FD9A8" },
                    } },
                } },
            } },

            -- 6. Scatter with hash(). hash(x) is a deterministic pseudo-random 0..1 -- the
            --    same on every client and every frame, with nothing transmitted. Feed it
            --    different offsets to get independent values per instance.
            --
            --    lod = 1 lets the renderer thin this field when the console is drawn small.
            --    Opt in for DECORATION ONLY: dropping tick marks or chart bars would be a
            --    bug, which is why it never happens unless you ask for it.
            { op = "RP", n = 120, lod = 1, c = {
                { op = "R",
                  x = "=mod(hash(i)*100 + 2*sin(t*(0.3+hash(i+9)*0.7) + hash(i+1)*6.283), 100)",
                  y = "=88+mod(hash(i+2)*10 - t*3, 10)",
                  w = "=1+step(0.8,hash(i+4))",
                  h = "=1+step(0.8,hash(i+4))",
                  f = "#8FE8C8",
                  fo = "=0.25+0.45*tri(t*0.4+hash(i+3))" },
            } },
        },
    },
})

local data = ui:element({
    id = "rep_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "rep",
              data = { bars = { 0.2, 0.5, 0.3, 0.8, 0.6, 0.4, 0.9, 0.5, 0.3, 0.7 } } },
})

ui:commit()

local elapsed = 0

function tick(dt)
    elapsed = elapsed + (dt or 0.5)

    local bars = {}
    for k = 1, 10 do
        bars[k] = 0.5 + 0.45 * math.sin(elapsed * 0.4 + k * 0.6)
    end

    data:set_props({ data = { bars = bars } })
    ui:commit()
end

-- NESTED REPEATS
--
-- Inside a nested repeat `i` is the inner index; the enclosing one is `i1`, the next out
-- `i2`, and so on. A grid is two repeats:
--
--   { op = "RP", n = 8, c = {
--       { op = "RP", n = 8, c = {
--           { op = "R", x = "=i*12", y = "=i1*12", w = 10, h = 10, f = "#2E8B6E" },
--       } },
--   } }
--
-- `n` must be a literal number, not an expression. Instance count is structural, so
-- changing it means resending the structure element.
