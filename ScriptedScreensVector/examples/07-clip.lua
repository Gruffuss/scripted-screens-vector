-- 07 — Clipping
--
-- A clip path confines everything inside a group to a shape. This is how a liquid stays in
-- its tank, how a progress bar keeps rounded ends while its fill is square, and how a
-- scrolling chart stops at its window edge instead of running across the console.
--
-- THE ONE RESTRICTION: clip shapes must be CONVEX.
--   Allowed  : rectangle, rounded rectangle, ellipse, convex polygon.
--   Not      : an L-shape, a star, a crescent, anything with a dent in it.
--
--   That covers essentially every real console layout, and it is what makes clipping free
--   at draw time -- a convex region is an intersection of half-planes, so shapes are cut
--   geometrically while the mesh is built. There is no stencil buffer and no per-frame cost.
--
-- Clip outlines are in SCENE COORDINATES, where you declare them, and they stay put when
-- the group using them is transformed. Declare the window once, in the same coordinates as
-- everything else, and move the contents freely underneath it.

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
    id = "clip_s", type = "vector",
    rect = { unit = "px", x = 12, y = 12, w = W - 24, h = H - 24 },
    props = {
        scene = "clip",
        w = 120, h = 120,

        defs = {
            -- A clip path is declared with CP and holds one shape in `c`.
            { op = "CP", id = "tank", c = {
                { op = "R", x = 10, y = 10, w = 44, h = 60, rx = 8 },
            } },

            { op = "CP", id = "window", c = {
                { op = "R", x = 66, y = 10, w = 44, h = 60, rx = 4 },
            } },

            { op = "CP", id = "porthole", c = {
                { op = "C", cx = 34, cy = 96, rx = 18, ry = 14 },
            } },

            { op = "GL", id = "liquid", units = "bbox", x1 = 0, y1 = 0, x2 = 0, y2 = 1,
              stops = { { 0, "#5FD9A8" }, { 1, "#2E8B6E" } } },
        },

        root = {

            -- 1. A liquid tank. The band is drawn as a full-width rectangle of liquid with
            --    a rippling top; the clip is what gives it rounded corners and stops it
            --    spilling out of the sides.
            { op = "R", x = 10, y = 10, w = 44, h = 60, rx = 8, f = "#0B1622" },

            { op = "G", clip = "tank", c = {
                { op = "YS", n = 28,
                  x  = "=8+i*1.75",
                  y  = "=64-clamp($fill,0,1)*52 + 2*sin(i*0.5+t*1.7)",
                  y2 = 72,
                  f  = "@liquid" },
            } },

            -- The rim goes OUTSIDE the clipped group, so it draws over the liquid.
            { op = "R", x = 10, y = 10, w = 44, h = 60, rx = 8,
              f = "none", s = "#5FD9A8", sw = 1.2 },

            -- 2. A scrolling chart clipped to its window. The line is generated well past
            --    the window on both sides and simply cut. Strokes are SEVERED by a clip --
            --    cut into the runs that fall inside, not redirected along the boundary.
            { op = "R", x = 66, y = 10, w = 44, h = 60, rx = 4, f = "#0B1622" },

            { op = "G", clip = "window", c = {
                { op = "LS", n = 60,
                  x = "=56+i*1.4",
                  y = "=40+16*sin(i*0.3+t*1.2)",
                  s = "#F59E0B", sw = 1.5, cap = "round" },
            } },

            -- 3. A clip plus a MOVING group. The window stays where it was declared while
            --    the contents slide underneath -- which is the whole point of clip outlines
            --    being in scene coordinates rather than the group's own space.
            { op = "C", cx = 34, cy = 96, rx = 18, ry = 14, f = "#0B1622" },

            { op = "G", clip = "porthole", c = {
                { op = "G", t = { "=mod(t*8, 40)-20", 0 }, c = {
                    { op = "RP", n = 10, c = {
                        { op = "R", x = "=8+i*6", y = 84, w = 3, h = 24, f = "#2E8B6E" },
                    } },
                } },
            } },

            { op = "C", cx = 34, cy = 96, rx = 18, ry = 14,
              f = "none", s = "#1E3247", sw = 1.5 },

            -- 4. Feathering survives a clip, and does the right thing. Where the clip cut
            --    the outline the soft ramp collapses to nothing, so no halo escapes the
            --    window; edges the clip never touched keep their full feather.
            { op = "G", clip = "window", c = {
                { op = "C", cx = 110, cy = 66, rx = 14, ry = 14, f = "#E23D3D", fea = 4 },
            } },
        },
    },
})

local data = ui:element({
    id = "clip_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "clip", data = { fill = 0.6 } },
})

ui:commit()

local elapsed = 0

function tick(dt)
    elapsed = elapsed + (dt or 0.5)
    data:set_props({ data = { fill = 0.5 + 0.4 * math.sin(elapsed * 0.2) } })
    ui:commit()
end

-- OTHER THINGS TO KNOW
--
--   * A clipped fill cannot carry HOLES. A hole straddling the clip boundary needs boolean
--     subtraction rather than a convex clip, so holes are dropped with a warning.
--
--   * Clip outlines are STATIC. An expression using `t` inside a CP is silently constant.
--     Clips are layout, not animation -- animate the contents instead, as item 3 does.
--
--   * If a clip is rejected (not convex, or a name that does not exist) the console draws a
--     magenta hatched border rather than silently showing you the wrong picture. The reason
--     is in BepInEx/LogOutput.log.
