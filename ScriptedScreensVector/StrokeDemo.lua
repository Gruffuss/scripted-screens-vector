-- Stroke demo: lines with real width, joins, caps, dashes, and splines.
--
-- Everything here animates from `t` on the client. Lua sends the structure once and
-- then writes two numbers per tick.

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
    props = { text = "STROKES / SPLINES" },
    style = { font_size = 15, color = "#5FD9A8", align = "left" },
})

local scene = {
    scene = "strokes",
    w = 200, h = 200, fit = "stretch",
    root = {
        -- Outlined rounded rect: fill and stroke on the same node.
        { op = "R", x = 8, y = 8, w = 84, h = 54, rx = 8,
          f = "#0E1B2A", s = "#5FD9A8", sw = 1.5 },

        -- Dashed outline, offset animated so the dashes crawl.
        { op = "R", x = 108, y = 8, w = 84, h = 54, rx = 8,
          s = "#F59E0B", sw = 1.2, dash = { 5, 3 }, dofs = "=t*8" },

        -- A stroked sine, sampled 60 times. Round caps, round joins.
        { op = "LS", n = 60,
          x = "=8+i*3.1",
          y = "=95+18*sin(i*0.16+t*1.4)",
          s = "#8FE8C8", sw = 2.5, cap = "round", join = "round" },

        -- Catmull-Rom spline through six fixed points: passes through each one.
        { op = "SP", seg = 12,
          p = { 8, 150, 45, 128, 80, 165, 118, 132, 155, 158, 192, 138 },
          s = "#E23D3D", sw = 2, cap = "round" },

        -- The spline's control points, so you can see it passes through them.
        { op = "RP", n = 6, c = {
            { op = "C",
              cx = "=8+i*36.8",
              cy = "=$pts[i]",
              rx = 2.2, ry = 2.2, f = "#E23D3D" },
        } },

        -- Closed polygon, stroked and filled. Convex, so the fan is valid.
        { op = "Y", p = { 100, 176, 124, 168, 140, 188, 112, 196 },
          f = "#16283C", s = "#5FD9A8", sw = 1 },

        -- A rotating needle: stroke width animates too.
        { op = "G", t = { 45, 182 }, r = "=t*60", c = {
            { op = "LS", n = 2, x = "=i*22", y = 0,
              s = "#F59E0B", sw = "=1.5+1.5*tri(t*0.8)", cap = "round" },
        } },
        { op = "C", cx = 45, cy = 182, rx = 3, ry = 3, f = "#F59E0B" },
    },
}

ui:element({
    id = "stroke_structure",
    type = "vector",
    rect = { unit = "px", x = 14, y = 36, w = W - 28, h = H - 50 },
    props = scene,
})

local data = ui:element({
    id = "stroke_data",
    type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "strokes", data = { pts = { 150, 128, 165, 132, 158, 138 } } },
})

ui:commit()

function tick(dt)
    -- Static data here; the animation is entirely client-side from `t`.
    data:set_props({ data = { pts = { 150, 128, 165, 132, 158, 138 } } })
    ui:commit()
end
