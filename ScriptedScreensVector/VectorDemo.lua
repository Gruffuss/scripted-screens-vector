-- Vector layer demo: liquid tank with ripple and mote field, plus a rotating group.
--
-- The point of this script is what it does NOT do. There is no on_frame loop, no
-- per-frame drawing, and no canvas. Everything moves because the client evaluates
-- expressions over `t` at display refresh rate.
--
-- Lua construction cost: about 20 nodes, built once at spawn.
-- Client-side expansion: roughly 90 quads, re-evaluated per frame on the GPU-side mesh.
-- Per tick after that: one small data table.
--
-- Structure and data are SEPARATE elements on purpose. set_props merges and then
-- upserts the whole element, so putting them together would resend the entire scene
-- twice a second.

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
    props = { text = "VECTOR LAYER" },
    style = { font_size = 15, color = "#5FD9A8", align = "left" },
})

-- Scene coordinates are 200x200 and get mapped onto the element rect, so this
-- artwork is resolution independent -- the same table drives a 240px console and a
-- 760px one.
local scene = {
    scene = "demo",
    w = 200, h = 200, fit = "stretch",
    root = {
        -- Tank body.
        { op = "R", x = 10, y = 10, w = 70, h = 180, rx = 8, f = "#0E1B2A" },

        -- Liquid surface. YS samples the wave 40 times and joins the samples into one
        -- connected strip, so the top edge is a curve. Doing this with RP would emit 40
        -- separate rectangles, each with its own flat top -- a staircase, not a wave.
        { op = "YS", n = 40,
          x  = "=12+i*1.7",
          y  = "=188-$fill*176+3*sin(i*0.42+t*1.6)+2*sin(i*0.31-t*2.3)",
          y2 = 188,
          f  = "#2E8B6E" },

        -- Motes. hash(i) gives each instance stable pseudo-random constants, so the
        -- field looks scattered without a single random value being transmitted.
        { op = "RP", n = 30, c = {
            { op = "R",
              x = "=14+mod(hash(i)*64+3*sin(t*(0.5+hash(i+9)*0.9)+hash(i+1)*6.283),64)",
              y = "=mod(hash(i+2)*176+12-t*(4+hash(i+5)*10),176)+12",
              w = "=1+step(0.7,hash(i+4))",
              h = "=1+step(0.7,hash(i+4))",
              f = "#8FE8C8",
              fo = 0.6 },
        } },

        -- Tank rim, drawn over the liquid.
        { op = "R", x = 10, y = 10, w = 70, h = 4, rx = 2, f = "#5FD9A8" },

        -- A rotating group: proves transforms compose and that `a` anchors rotation.
        -- Twelve ticks around a ring, each an ellipse, opacity pulsing out of phase.
        { op = "G", t = { 145, 100 }, r = "=t*25", a = { 0, 0 }, c = {
            { op = "RP", n = 12, c = {
                { op = "C",
                  cx = "=cos(i*0.5236)*42",
                  cy = "=sin(i*0.5236)*42",
                  rx = "=3+2*tri(t*0.5+i/12)",
                  ry = "=3+2*tri(t*0.5+i/12)",
                  f = "#F59E0B",
                  fo = "=0.35+0.65*tri(t*0.7+i/12)" },
            } },
        } },

        -- Centre pip, static: no `t` anywhere, so it is evaluated once and cached.
        { op = "C", cx = 145, cy = 100, rx = 6, ry = 6, f = "#E23D3D" },

        -- Level readout bar driven purely by data, no time reference.
        { op = "R", x = 100, y = 176, w = 90, h = 6, rx = 3, f = "#16283C" },
        { op = "R", x = 100, y = 176, w = "=90*$fill", h = 6, rx = 3, f = "#5FD9A8" },
    },
}

ui:element({
    id = "demo_structure",
    type = "vector",
    rect = { unit = "px", x = 14, y = 36, w = W - 28, h = H - 50 },
    props = scene,
})

local data = ui:element({
    id = "demo_data",
    type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "demo", data = { fill = 0.5 } },
})

ui:commit()

-- The entire per-tick cost: one number. Everything visible animates from `t` on the
-- client regardless of how rarely this runs.
local elapsed = 0

function tick(dt)
    elapsed = elapsed + (dt or 0.5)
    local fill = 0.5 + 0.42 * math.sin(elapsed * 0.15)

    data:set_props({ data = { fill = fill } })
    ui:commit()
end
