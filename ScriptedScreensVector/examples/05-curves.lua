-- 05 — Continuous edges: YS, LS, SP and P
--
-- This is the distinction that catches people out after they learn RP.
--
--   RP makes SEPARATE SHAPES.
--   YS makes ONE CONNECTED SURFACE.
--
-- A liquid surface built from RP rectangles is a staircase, however finely you sample it,
-- because each rectangle has its own flat top. The top two rows of this file draw exactly
-- the same wave both ways so you can see the difference on a real console.
--
--   YS  filled band   -- liquid, area charts, ribbons (and fo2, a fade from
--                        an edge that MOVES -- see item 6)
--   LS  stroked line  -- line charts, waveforms, needles
--   SP  spline        -- a smooth curve THROUGH a list of points
--   P   SVG path      -- icons, logos, anything with holes

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

-- The wave both rows share. Written once so the only difference is the node type.
local WAVE = "=22-6*sin(i*0.45+t*1.6)"

ui:element({
    id = "curves_s", type = "vector",
    rect = { unit = "px", x = 12, y = 12, w = W - 24, h = H - 24 },
    props = {
        scene = "curves",
        w = 120, h = 120,
        root = {

            -- 1. WRONG for a surface: 30 separate rectangles, 30 flat tops.
            --    Look closely and the top edge is visibly stepped.
            { op = "RP", n = 30, c = {
                { op = "R", x = "=6+i*3.6", y = WAVE, w = 3.6, h = "=34-(22-6*sin(i*0.45+t*1.6))",
                  f = "#8B4A4A" },
            } },

            -- 2. RIGHT: one strip, one continuous edge through the same sample points.
            --    `y` is the sampled edge, `y2` the opposite one. A constant y2 fills to a
            --    baseline; a second expression gives a ribbon of varying thickness.
            { op = "YS", n = 30,
              x  = "=6+i*3.6",
              y  = "=62-6*sin(i*0.45+t*1.6)",
              y2 = 74,
              f  = "#2E8B6E" },

            -- 3. LS is the stroked sibling of YS -- same sampling rule, drawn as a line.
            --    This is the line-chart and waveform primitive.
            { op = "LS", n = 40,
              x = "=6+i*2.7",
              y = "=88-clamp($history[i],0,1)*16",
              s = "#5FD9A8", sw = 1.5, cap = "round", join = "round" },

            -- 4. SP fits a Catmull-Rom spline THROUGH the points in `p` -- it passes
            --    through them rather than being pulled toward them like a bezier handle.
            --    `p` is flat: x, y, x, y, ...  `seg` is segments per span.
            { op = "SP", seg = 14, p = { 8, 108, 34, 96, 60, 114, 86, 100, 112, 110 },
              s = "#F59E0B", sw = 2, cap = "round" },

            -- 5. P takes SVG path data. Uppercase absolute, lowercase relative.
            --    Two subpaths make a hole: the largest is the outline, the rest are holes.
            { op = "P", d = "M 96 8 L 114 8 L 114 44 L 96 44 Z M 101 14 L 109 14 L 109 38 L 101 38 Z",
              f = "#1E3247" },

            -- 6. fo2: an opacity ramp ALONG EACH COLUMN, from `fo` at the
            --    sampled edge to `fo2` at the far one.
            --
            --    This is how you fade from a surface that MOVES. A gradient is
            --    straight and anchored to the shape's bounding box, so on a
            --    rippling edge it would reach only part-way at a crest and
            --    start part-way up in a trough -- a bright line exactly where
            --    the fade should disappear. `fea` ramps outward from a solid
            --    edge, which is the opposite direction.
            --
            --    LEFT: transparent at the wave, solid 18 units below it,
            --    following every ripple. Costs no extra geometry.
            { op = "YS", n = 16,
              x  = "=6+i*3.2",
              y  = "=96-4*sin(i*0.5+t*1.3)",
              y2 = "=114-4*sin(i*0.5+t*1.3)",
              f  = "#8FE8C8", fo = 0, fo2 = 0.7 },

            -- RIGHT: the same band with a flat `fo` and no ramp, for contrast.
            -- A slab with a hard top edge instead of a fade into the surface.
            { op = "YS", n = 16,
              x  = "=64+i*3.2",
              y  = "=96-4*sin(i*0.5+t*1.3)",
              y2 = "=114-4*sin(i*0.5+t*1.3)",
              f  = "#8FE8C8", fo = 0.35 },
        },
    },
})

local data = ui:element({
    id = "curves_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "curves", data = { history = {} } },
})

ui:commit()

-- ------------------------------------------------------------------------ rolling window
--
-- A chart wants a ROLLING WINDOW: shift every sample one slot left, append one new value.
--
-- This matters more than it looks. The renderer eases arrays between payloads, so blending
-- old[i] toward new[i] on a shifted window produces a real horizontal SCROLL. Regenerating
-- the whole curve each tick instead animates the array in place, which looks like the line
-- writhing rather than advancing.

local history = {}
for k = 1, 40 do history[k] = 0.5 end

local elapsed = 0

function tick(dt)
    elapsed = elapsed + (dt or 0.5)

    for k = 1, #history - 1 do
        history[k] = history[k + 1]
    end
    history[#history] = 0.5 + 0.4 * math.sin(elapsed * 0.5)

    data:set_props({ data = { history = history } })
    ui:commit()
end
