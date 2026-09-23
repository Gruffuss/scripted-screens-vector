-- Path and concave-fill demo.
--
-- Exercises the two things that were missing: SVG path data with real curves, and
-- filling shapes that are not convex (including shapes with holes).

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
    props = { text = "PATHS / CONCAVE FILL" },
    style = { font_size = 15, color = "#5FD9A8", align = "left" },
})

local scene = {
    scene = "paths",
    w = 200, h = 200, fit = "stretch",
    root = {
        -- A five-pointed star: heavily concave, and the case a centre fan cannot draw.
        -- If the fill is correct the points are sharp and nothing bleeds across the middle.
        { op = "Y",
          p = { 40, 12,  48, 34,  71, 34,  53, 48,  60, 70,
                40, 56,  20, 70,  27, 48,   9, 34,  32, 34 },
          f = "#F59E0B", s = "#FFD79A", sw = 0.8 },

        -- A ring: outer circle with an inner circle as a hole, via two closed subpaths.
        -- Bridging joins them into one contour, so the middle stays genuinely empty.
        { op = "P",
          d = "M 110 12 A 29 29 0 1 0 110 70 A 29 29 0 1 0 110 12 Z "
           .. "M 110 26 A 15 15 0 1 1 110 56 A 15 15 0 1 1 110 26 Z",
          f = "#5FD9A8" },

        -- Same two circles, both wound the SAME way, under each fill rule. Under
        -- evenodd the inner one is a hole; under nonzero it is not, and the disc is
        -- solid. This is the case where fr actually changes the picture.
        { op = "P", fr = "evenodd",
          d = "M 30 96 A 14 14 0 1 0 30 124 A 14 14 0 1 0 30 96 Z "
           .. "M 30 104 A 6 6 0 1 0 30 116 A 6 6 0 1 0 30 104 Z",
          f = "#F59E0B" },

        { op = "P", fr = "nonzero",
          d = "M 70 96 A 14 14 0 1 0 70 124 A 14 14 0 1 0 70 96 Z "
           .. "M 70 104 A 6 6 0 1 0 70 116 A 6 6 0 1 0 70 104 Z",
          f = "#F59E0B" },

        -- Cubic and quadratic beziers, stroked. Flattened at draw time, so walking up to
        -- the console subdivides these further rather than showing facets.
        { op = "P",
          d = "M 100 100 C 120 88, 140 118, 160 104 S 185 92, 195 108",
          s = "#8FE8C8", sw = 2, cap = "round" },

        { op = "P",
          d = "M 10 128 Q 55 100, 100 128 T 190 128",
          s = "#E23D3D", sw = 1.6, cap = "round" },

        -- A filled teardrop: curves plus a close, filled through the ear clipper.
        { op = "P",
          d = "M 40 150 C 40 140, 60 140, 60 155 C 60 172, 40 186, 40 186 C 40 186, 20 172, 20 155 C 20 140, 40 140, 40 150 Z",
          f = "#2E8B6E", s = "#5FD9A8", sw = 0.8 },

        -- Arc flags: the four large-arc/sweep combinations between the same endpoints.
        { op = "P", d = "M 100 160 A 20 20 0 0 0 140 160", s = "#F59E0B", sw = 1.4 },
        { op = "P", d = "M 100 170 A 20 20 0 0 1 140 170", s = "#8FE8C8", sw = 1.4 },
        { op = "P", d = "M 150 165 A 14 20 30 1 0 180 180", s = "#E23D3D", sw = 1.4 },

        -- A rotating concave arrow, to confirm the triangulation holds under transform.
        { op = "G", t = { 165, 40 }, r = "=t*45", c = {
            { op = "Y",
              p = { 0, -16,  6, -4,  16, -4,  8, 4,  11, 16,  0, 9,  -11, 16,  -8, 4,  -16, -4,  -6, -4 },
              f = "#5FD9A8", fo = 0.9 },
        } },
    },
}

ui:element({
    id = "path_structure",
    type = "vector",
    rect = { unit = "px", x = 14, y = 36, w = W - 28, h = H - 50 },
    props = scene,
})

ui:commit()
