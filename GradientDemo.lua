-- Gradients and clipping.
--
-- Gradients are baked into vertex colours at tessellation time, so they batch with the
-- rest of the ScriptedScreens UI and need no material of their own. Clipping is convex
-- only and geometric -- shapes are cut during tessellation, with no stencil buffer.

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
    props = { text = "GRADIENTS / CLIPPING" },
    style = { font_size = 15, color = "#5FD9A8", align = "left" },
})

local scene = {
    scene = "grad",
    w = 200, h = 200, fit = "stretch",
    -- IMPORTANT: gradient coordinates are in the same space as the geometry that uses
    -- them. A shape lying outside its gradient's extent clamps to an end stop and comes
    -- out a flat colour -- which looks exactly like a broken gradient but is correct
    -- behaviour. Each gradient below is placed over the shape that references it.
    defs = {
        -- Two stops: exact with no subdivision, because a linear ramp really is an
        -- affine function of position and vertex colours interpolate affinely.
        -- Spans the top-left rect: y 8 -> 60.
        { op = "GL", id = "sky", x1 = 0, y1 = 8, x2 = 0, y2 = 60,
          stops = { { 0, "#0B3B5C" }, { 1, "#5FD9A8" } } },

        -- Four stops: piecewise affine, so the tessellator subdivides to follow it.
        -- Spans the top-right rect: x 108 -> 192.
        { op = "GL", id = "heat", x1 = 108, y1 = 0, x2 = 192, y2 = 0,
          stops = { { 0, "#2E8B6E" }, { 0.4, "#5FD9A8" }, { 0.7, "#F59E0B" }, { 1, "#E23D3D" } } },

        -- Same four stops across the clip window: x 100 -> 195.
        { op = "GL", id = "bars", x1 = 100, y1 = 0, x2 = 195, y2 = 0,
          stops = { { 0, "#2E8B6E" }, { 0.4, "#5FD9A8" }, { 0.7, "#F59E0B" }, { 1, "#E23D3D" } } },

        -- Across the ring: x 8 -> 82.
        { op = "GL", id = "ring", x1 = 8, y1 = 0, x2 = 82, y2 = 0,
          stops = { { 0, "#2E8B6E" }, { 0.5, "#5FD9A8" }, { 1, "#F59E0B" } } },

        -- Radial, centred on the circle it fills, with the focus pulled up and left so
        -- the highlight is off-centre. Never affine, so always subdivided.
        { op = "GR", id = "orb", cx = 45, cy = 118, r = 36, fx = 33, fy = 106,
          stops = { { 0, "#FFFFFF" }, { 0.35, "#5FD9A8" }, { 1, "#08131F" } } },

        -- Clip shapes are convex only. A rounded rect qualifies.
        { op = "CP", id = "window",
          c = { { op = "R", x = 108, y = 108, w = 84, h = 84, rx = 14 } } },
    },
    root = {
        -- Two-stop linear, no subdivision needed.
        { op = "R", x = 8, y = 8, w = 84, h = 52, rx = 6, f = "@sky" },

        -- Multi-stop linear across a rounded rect.
        { op = "R", x = 108, y = 8, w = 84, h = 52, rx = 6, f = "@heat" },

        -- Radial fill on a circle, plus a gradient-stroked ring around it.
        { op = "C", cx = 45, cy = 118, rx = 34, ry = 34, f = "@orb" },
        { op = "C", cx = 45, cy = 118, rx = 37, ry = 37, s = "@ring", sw = 2 },

        -- Everything in this group is cut to the rounded-rect clip window. The shapes
        -- deliberately overhang it on every side, and the motes drift through it.
        --
        -- The group is TRANSLATED and the clip is declared in scene coordinates, which is
        -- the case no demo used to cover -- and the case that was broken. The children are
        -- written relative to the group, so if the clip silently moved with them nothing
        -- would line up.
        { op = "G", t = { 6, 0 }, clip = "window", c = {
            { op = "R", x = 90, y = 96, w = 108, h = 108, f = "#0E1B2A" },

            { op = "RP", n = 9, c = {
                { op = "R",
                  x = "=94+i*11",
                  y = "=96+18*sin(i*0.7+t*1.2)",
                  w = 7, h = 108,
                  f = "@bars", fo = 0.85 },
            } },

            -- A stroked line sweeping across: clipped strokes are cut into the pieces
            -- that fall inside, not bent along the boundary.
            { op = "LS", n = 2,
              x = "=84+i*120", y = "=150+46*sin(t*0.9)",
              s = "#FFFFFF", sw = 2, cap = "round" },
        } },

        -- The clip boundary itself, drawn unclipped so the cut is easy to judge.
        { op = "R", x = 108, y = 108, w = 84, h = 84, rx = 14,
          s = "#5FD9A8", sw = 1, dash = { 4, 3 }, dofs = "=t*6" },
    },
}

ui:element({
    id = "grad_structure",
    type = "vector",
    rect = { unit = "px", x = 14, y = 36, w = W - 28, h = H - 50 },
    props = scene,
})

ui:commit()
