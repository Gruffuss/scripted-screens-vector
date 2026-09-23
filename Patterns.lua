-- Building blocks for vector consoles.
--
-- Each function returns a node (or a list of nodes) to drop into a scene's `root`. They are
-- written for copy-and-paste rather than as a library: take what you need and edit it.
--
-- Everything animates from `t` and `$data` on the client. None of these needs on_frame, and
-- none of them costs anything per tick beyond the data values they read.
--
-- Coordinates are top-left origin, +Y down, in whatever viewbox the scene declares.

-- HOW TO USE THIS FILE
--
-- Paste it into a chip as-is: it draws the worked console at the bottom so you can see the
-- pieces working. Then copy whichever functions you want into your own script and delete
-- the rest.
--
-- There is no module system in a chip -- no `require` -- so these are plain local functions
-- meant to be copied, not imported.

local P = {}

--------------------------------------------------------------------------------
-- Liquid tank: rounded shell, filled body, rippling surface.
--
-- `key` is the data name holding fill level 0..1. The ripple is client-side, so the host
-- only ever sends that one number.
--
-- Note YS, not RP: a repeat would emit separate rectangles and the surface would read as a
-- staircase. See README, "RP versus YS".
--------------------------------------------------------------------------------
function P.tank(x, y, w, h, key, colour, gradientId)
    local bottom = y + h
    return {
        -- Shell.
        { op = "R", x = x, y = y, w = w, h = h, rx = 5, f = "#0B1622" },

        -- Body plus rippling surface, in one connected strip.
        { op = "YS", n = 36,
          x  = string.format("=%f+i*%f", x + 2, (w - 4) / 35),
          -- Ripple amplitude is in scene units: on a 200-unit viewbox, 2 units is about a
          -- pixel and reads as static. Scale it to the tank so it is actually visible.
          y  = string.format("=%f-$%s*%f + %f*sin(i*0.42+t*1.6) + %f*sin(i*0.31-t*2.3)",
                             bottom - 2, key, h - 4, h * 0.03, h * 0.018),
          y2 = bottom - 2,
          f  = gradientId and ("@" .. gradientId) or (colour or "#2E8B6E") },

        -- Rim, drawn over the liquid.
        { op = "R", x = x, y = y, w = w, h = 3, rx = 1.5, f = "#5FD9A8" },
    }
end

--------------------------------------------------------------------------------
-- Mote field: drifting specks, for gas or particulate.
--
-- `lod = 1` lets the renderer thin the field when the console is drawn small, if Count LOD
-- is switched on in the mod settings (off by default). Opt in for
-- decoration; never for anything load-bearing like tick marks.
--------------------------------------------------------------------------------
function P.motes(x, y, w, h, count, colour, driftKey)
    local drift = driftKey and ("$" .. driftKey) or "1"
    return { op = "RP", n = count, lod = 1, c = {
        { op = "R",
          x = string.format("=%f+mod(hash(i)*%f + 2*sin(t*(0.4+hash(i+9)*0.9) + hash(i+1)*6.283), %f)",
                            x, w, w),
          y = string.format("=%f+mod(hash(i+2)*%f - t*%s*6, %f)", y, h, drift, h),
          w = "=1+step(0.72,hash(i+4))",
          h = "=1+step(0.72,hash(i+4))",
          f = colour or "#8FE8C8",
          fo = "=0.35+0.4*tri(t*0.4+hash(i+3))" },
    } }
end

--------------------------------------------------------------------------------
-- Horizontal bar with a colour that follows its value.
--
-- Needs a ramp in `defs`, e.g.
--   { op = "GL", id = "status", x1 = 0, y1 = 0, x2 = 1, y2 = 0,
--     stops = { {0,"#5FD9A8"}, {0.5,"#F59E0B"}, {1,"#E23D3D"} } }
--
-- The host sends a number; the client decides the colour. Cheaper and smoother than sending
-- colour strings, and it blends through every stop.
--------------------------------------------------------------------------------
function P.bar(x, y, w, h, key, rampId)
    return {
        { op = "R", x = x, y = y, w = w, h = h, rx = h / 2, f = "#16283C" },
        { op = "R", x = x, y = y, w = string.format("=%f*clamp($%s,0,1)", w, key), h = h,
          rx = h / 2,
          f = rampId and { grad = rampId, at = "=clamp($" .. key .. ",0,1)" } or "#5FD9A8" },
    }
end

--------------------------------------------------------------------------------
-- Dial: tick marks plus a needle.
--
-- The ticks are a repeat WITHOUT lod -- losing them would be a rendering bug, not an
-- optimisation. `sweep` is the arc in degrees, centred on straight up.
--------------------------------------------------------------------------------
function P.dial(cx, cy, radius, key, sweep, ticks)
    sweep = sweep or 240
    ticks = ticks or 9

    local half = sweep / 2
    local step = sweep / (ticks - 1)

    return {
        { op = "C", cx = cx, cy = cy, rx = radius, ry = radius, f = "#0B1622",
          s = "#1E3247", sw = 1 },

        -- Tick marks. No lod: structure, not decoration -- losing one would be a bug.
        --
        -- The rotation has to be INSIDE the repeat, on a group whose angle is an expression
        -- over `i`. A repeat instantiates its children unchanged; nothing rotates by itself,
        -- so without this every tick lands on top of the first one.
        { op = "G", t = { cx, cy }, c = {
            { op = "RP", n = ticks, c = {
                { op = "G", r = string.format("=%f+i*%f", -half, step), c = {
                    { op = "R", x = -0.7, y = -(radius - 2), w = 1.4, h = 5, f = "#5FD9A8" },
                } },
            } },
        } },

        -- Needle. The group rotates; the shape inside stays simple.
        { op = "G", t = { cx, cy },
          r = string.format("=%f + clamp($%s,0,1)*%f", -half, key, sweep),
          c = {
            { op = "LS", n = 2, x = 0, y = string.format("=i*%f", -(radius - 4)),
              s = "#F59E0B", sw = 2, cap = "round" },
          } },

        { op = "C", cx = cx, cy = cy, rx = 3, ry = 3, f = "#F59E0B" },
    }
end

--------------------------------------------------------------------------------
-- Line chart from a data array.
--
-- `key` names an array in the data payload; `count` is how many points to plot. Out-of-range
-- array reads yield 0, so a short array degrades rather than failing.
--------------------------------------------------------------------------------
function P.chart(x, y, w, h, key, count, colour)
    return {
        { op = "R", x = x, y = y, w = w, h = h, f = "#0B1622" },
        { op = "LS", n = count,
          x = string.format("=%f+i*%f", x, w / (count - 1)),
          y = string.format("=%f-clamp($%s[i],0,1)*%f", y + h, key, h),
          s = colour or "#5FD9A8", sw = 1.5, cap = "round", join = "round" },
    }
end

--------------------------------------------------------------------------------
-- Alarm lamp: colour from data, pulse rate from data.
--
-- Both are numbers the host already knows. The pulse is client-side.
--------------------------------------------------------------------------------
function P.lamp(cx, cy, radius, rampId, levelKey, rateKey)
    return { op = "C", cx = cx, cy = cy, rx = radius, ry = radius,
             f = { grad = rampId, at = "=clamp($" .. levelKey .. ",0,1)" },
             fo = string.format("=0.35+0.65*tri(t*$%s)", rateKey) }
end

--------------------------------------------------------------------------------
-- A worked console using the above.
--------------------------------------------------------------------------------
local function example()
    local ui = ss.ui.surface("main")
    ss.ui.activate("main")

    local size = ui:size()
    local W, H = 480, 480
    if size then W, H = size.w, size.h end

    ui:clear()

    ui:element({
        id = "bg", type = "panel",
        rect = { unit = "px", x = 0, y = 0, w = W, h = H },
        style = { bg = "#070D16" },
    })

    local root = {}
    local function add(nodes)
        if nodes.op then
            root[#root + 1] = nodes
        else
            for _, n in ipairs(nodes) do root[#root + 1] = n end
        end
    end

    -- Gradient placed over the tank it fills: see README, "Traps".
    local defs = {
        { op = "GL", id = "liquid", x1 = 0, y1 = 20, x2 = 0, y2 = 170,
          stops = { { 0, "#5FD9A8" }, { 1, "#2E8B6E" } } },
        { op = "GL", id = "status", x1 = 0, y1 = 0, x2 = 1, y2 = 0,
          stops = { { 0, "#5FD9A8" }, { 0.5, "#F59E0B" }, { 1, "#E23D3D" } } },
    }

    add(P.tank(10, 20, 46, 150, "o2", nil, "liquid"))
    add(P.motes(12, 22, 42, 146, 60, "#8FE8C8", "flow"))
    add(P.dial(120, 70, 40, "pressure"))
    add(P.bar(70, 150, 120, 10, "pressure", "status"))
    add(P.chart(70, 170, 120, 24, "history", 16))
    add(P.lamp(178, 30, 7, "status", "alarm", "rate"))

    ui:element({
        id = "console_s", type = "vector",
        rect = { unit = "px", x = 8, y = 8, w = W - 16, h = H - 16 },
        props = { scene = "console", w = 200, h = 200, fit = "stretch",
                  defs = defs, root = root },
    })

    local data = ui:element({
        id = "console_d", type = "vector",
        rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
        props = { scene = "console", data = {
            o2 = 0.5, flow = 1, pressure = 0.4, alarm = 0, rate = 0.5,
            history = { 0.2, 0.3, 0.25, 0.4, 0.5, 0.45, 0.6, 0.55 },
        } },
    })

    ui:commit()
    return data
end

--------------------------------------------------------------------------------
-- Runs on paste. Delete from here down once you have copied what you need.
--------------------------------------------------------------------------------
local data = example()
local elapsed = 0

-- Rolling window, seeded flat so the chart starts sensible rather than empty.
local history = {}
for i = 1, 16 do history[i] = 0.5 end

function tick(dt)
    elapsed = elapsed + (dt or 0.5)

    -- Stand-in values. Replace with real device reads.
    local pressure = 0.5 + 0.45 * math.sin(elapsed * 0.20)
    local o2 = 0.5 + 0.40 * math.sin(elapsed * 0.13)

    -- A real history: shift left, append one new sample. Regenerating the whole curve
    -- every tick (which an earlier version did) is not what a history chart does, and it
    -- animates the array itself rather than scrolling it.
    for i = 1, #history - 1 do
        history[i] = history[i + 1]
    end
    history[#history] = pressure

    data:set_props({ data = {
        o2 = o2,
        flow = 1,
        pressure = pressure,
        alarm = pressure,          -- drives lamp colour through the status ramp
        rate = 0.4 + pressure * 2, -- and its pulse rate
        history = history,
    } })

    ss.ui.surface("main"):commit()
end
