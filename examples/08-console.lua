-- 08 — A complete console
--
-- Everything from examples 01-07 assembled into something you would actually put on a wall:
-- four tanks with rippling liquid and drifting motes, a pressure dial, a status bar, a
-- scrolling history chart and an alarm lamp.
--
-- Paste it, then replace the four lines in `read_sensors()` with real device reads.
--
-- HOW IT IS PUT TOGETHER
--   * Builder functions return node LISTS, which get flattened into `root`. That keeps the
--     layout readable and lets you move a tank by changing two numbers.
--   * Structure is built ONCE, outside tick(). Only the data element is touched per tick.
--   * Every repeat that is decoration carries lod = 1; nothing structural does.
--
-- COST
--   Tessellation runs on a worker thread, so a console like this costs a fraction of a
--   millisecond of frame time regardless of how much is animating. Turn on
--   Diagnostics.Enabled in the mod config to see the numbers for yourself.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 480, 480
if size then W, H = size.w, size.h end

ui:clear()

-- Viewbox. All the artwork below is written in these units and scales to any console.
local VB = 200

-- ---------------------------------------------------------------------------- builders

-- A tank: shell, clipped liquid with a rippling surface, drifting motes, rim and label bar.
-- `key` names a PERCENTAGE in the data payload, 0..100, because the readout beside each tank
-- prints the same number with `fmt = "%.0f"` and a `%` unit. One value serves both; the
-- geometry divides by 100 where it needs a fraction.
local function tank(x, y, w, h, key, clipId)
    local inner_top    = y + 3
    local inner_bottom = y + h - 3
    local inner_left   = x + 3
    local inner_w      = w - 6

    return {
        { op = "R", x = x, y = y, w = w, h = h, rx = 4, f = "#0B1622",
          s = "#1E3247", sw = 1 },

        { op = "G", clip = clipId, c = {
            -- Liquid. YS, not RP: the surface has to read as one continuous edge.
            { op = "YS", n = 24,
              x  = string.format("=%f+i*%f", inner_left, inner_w / 23),
              y  = string.format("=%f-clamp($%s/100,0,1)*%f + %f*sin(i*0.5+t*1.5) + %f*sin(i*0.31-t*2.1)",
                                 inner_bottom, key, inner_bottom - inner_top,
                                 h * 0.02, h * 0.012),
              y2 = inner_bottom,
              f  = "@liquid" },

            -- Motes. Decoration, so lod = 1: with Count LOD on, the renderer may thin them at distance.
            { op = "RP", n = 40, lod = 1, c = {
                { op = "R",
                  x = string.format("=%f+mod(hash(i)*%f + 1.5*sin(t*(0.3+hash(i+9)*0.6) + hash(i+1)*6.283), %f)",
                                    inner_left, inner_w, inner_w),
                  y = string.format("=%f+mod(hash(i+2)*%f - t*4, %f)",
                                    inner_top, h - 6, h - 6),
                  w = "=0.8+step(0.8,hash(i+4))",
                  h = "=0.8+step(0.8,hash(i+4))",
                  f = "#8FE8C8",
                  fo = "=0.2+0.4*tri(t*0.4+hash(i+3))" },
            } },
        } },

        -- Rim, over the liquid.
        { op = "R", x = x, y = y, w = w, h = 2.5, rx = 1.25, f = "#5FD9A8" },
    }
end

-- A dial with tick marks and a needle. Ticks are structural: no lod.
local function dial(cx, cy, radius, key)
    return {
        { op = "C", cx = cx, cy = cy, rx = radius, ry = radius,
          f = "#0B1622", s = "#1E3247", sw = 1 },

        { op = "G", t = { cx, cy }, c = {
            { op = "RP", n = 9, c = {
                { op = "G", r = "=-120+i*30", c = {
                    { op = "R", x = -0.6, y = -(radius - 3), w = 1.2, h = 4.5,
                      f = "#5FD9A8" },
                } },
            } },
        } },

        { op = "G", t = { cx, cy },
          r = string.format("=-120+clamp($%s,0,1)*240", key), c = {
            { op = "R", x = -0.9, y = -(radius - 6), w = 1.8, h = radius - 6,
              rx = 0.9, f = "#F59E0B" },
        } },

        { op = "C", cx = cx, cy = cy, rx = 2.5, ry = 2.5, f = "#F59E0B" },
    }
end

-- A bar whose colour follows its own value through the @status ramp.
local function bar(x, y, w, h, key)
    return {
        { op = "R", x = x, y = y, w = w, h = h, rx = h / 2, f = "#12202F" },
        { op = "R", x = x, y = y,
          w = string.format("=%f*clamp($%s,0,1)", w, key), h = h, rx = h / 2,
          f = { grad = "status", at = string.format("=clamp($%s,0,1)", key) } },
    }
end

-- A history chart. `count` must match the array length the tick sends.
local function chart(x, y, w, h, key, count)
    return {
        { op = "R", x = x, y = y, w = w, h = h, rx = 2, f = "#0B1622" },
        { op = "G", clip = "chartwin", c = {
            { op = "LS", n = count,
              x = string.format("=%f+i*%f", x, w / (count - 1)),
              y = string.format("=%f-clamp($%s[i],0,1)*%f", y + h - 2, key, h - 4),
              s = "#5FD9A8", sw = 1.2, cap = "round", join = "round" },
        } },
    }
end

-- ------------------------------------------------------------------------------ assembly

local root = {}

local function add(nodes)
    if nodes.op then
        root[#root + 1] = nodes
    else
        for _, node in ipairs(nodes) do root[#root + 1] = node end
    end
end

local TANK_KEYS = { "o2", "n2", "co2", "vol" }
local TANK_X    = { 10, 54, 98, 142 }

for k = 1, 4 do
    add(tank(TANK_X[k], 14, 38, 92, TANK_KEYS[k], "tank" .. k))

    -- Caption and readout are SCENE nodes, in viewbox units like everything else. They used
    -- to be `label` elements laid over the artwork, which meant converting every coordinate
    -- into console pixels and re-declaring an element to change a number.
    add({ op = "T", x = TANK_X[k], y = 108, w = 38, h = 12,
          text = string.upper(TANK_KEYS[k]),
          size = 8, cspace = 1, align = "center", f = "#5FD9A8" })

    -- `fmt` formats a bound NUMBER here, so the payload below carries the value the script
    -- already had and does no string work per tank per tick.
    add({ op = "T", x = TANK_X[k], y = 96, w = 38, h = 11,
          text = "$" .. TANK_KEYS[k], fmt = "%.0f", unit = "%",
          size = 9, align = "center", valign = "middle", f = "#EAF4F8" })
end

add(dial(48, 152, 34, "pressure"))
add(bar(94, 122, 96, 7, "pressure"))
add(chart(94, 138, 96, 46, "history", 24))

-- Alarm lamp: colour from a value, pulse rate from another.
add({ op = "C", cx = 182, cy = 126, rx = 5, ry = 5,
      f = { grad = "status", at = "=clamp($alarm,0,1)" },
      fo = "=0.3+0.7*tri(t*$rate)" })

-- ---------------------------------------------------------------------------------- defs

local defs = {
    { op = "GL", id = "liquid", units = "bbox", x1 = 0, y1 = 0, x2 = 0, y2 = 1,
      stops = { { 0, "#5FD9A8" }, { 1, "#2E8B6E" } } },

    { op = "GL", id = "status", units = "bbox", x1 = 0, y1 = 0, x2 = 1, y2 = 0,
      stops = { { 0, "#5FD9A8" }, { 0.5, "#F59E0B" }, { 1, "#E23D3D" } } },

    { op = "CP", id = "chartwin", c = {
        { op = "R", x = 94, y = 138, w = 96, h = 46, rx = 2 },
    } },
}

for k = 1, 4 do
    defs[#defs + 1] = { op = "CP", id = "tank" .. k, c = {
        { op = "R", x = TANK_X[k], y = 14, w = 38, h = 92, rx = 4 },
    } }
end

-- --------------------------------------------------------------------------- elements

ui:element({
    id = "bg", type = "panel",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    style = { bg = "#070D16" },
})

ui:element({
    id = "console_s", type = "vector",
    rect = { unit = "px", x = 6, y = 6, w = W - 12, h = H - 12 },
    props = { scene = "console", w = VB, h = VB, fit = "stretch",
              defs = defs, root = root },
})

local data = ui:element({
    id = "console_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "console", keep = 1, data = {
        o2 = 50, n2 = 50, co2 = 20, vol = 70,
        pressure = 0.4, alarm = 0, rate = 0.5,
        history = {},
    } },
})

ui:commit()

-- -------------------------------------------------------------------------------- tick

local history = {}
for k = 1, 24 do history[k] = 0.5 end

local elapsed = 0

-- REPLACE THIS with real device reads, e.g.
--   local tank = ic.find("StructureTankSmall")
--   return tank.Pressure / 60000, ...
local function read_sensors()
    elapsed = elapsed + 0.5
    return {
        o2  = 0.5 + 0.40 * math.sin(elapsed * 0.13),
        n2  = 0.5 + 0.35 * math.sin(elapsed * 0.09 + 1.0),
        co2 = 0.3 + 0.25 * math.sin(elapsed * 0.17 + 2.0),
        vol = 0.6 + 0.30 * math.sin(elapsed * 0.11 + 3.0),
        pressure = 0.5 + 0.45 * math.sin(elapsed * 0.20),
    }
end

function tick(dt)
    local s = read_sensors()

    -- Rolling window: shift left, append one. This is what makes the chart SCROLL rather
    -- than writhe -- see example 05.
    for k = 1, #history - 1 do
        history[k] = history[k + 1]
    end
    history[#history] = s.pressure

    -- One number per gas serves both the liquid level and the readout: the tank geometry
    -- divides by 100 in its expressions, and `fmt = "%.0f"` prints it as it stands.
    data:set_props({ data = {
        o2 = s.o2 * 100, n2 = s.n2 * 100, co2 = s.co2 * 100, vol = s.vol * 100,
        pressure = s.pressure,
        alarm = s.pressure,             -- drives lamp colour through the status ramp
        rate  = 0.4 + s.pressure * 2,   -- and its pulse rate
        history = history,
    } })

    ui:commit()
end
