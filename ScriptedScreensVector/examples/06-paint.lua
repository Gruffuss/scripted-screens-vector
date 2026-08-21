-- 06 — Paint: gradients, alpha, feathering, and colour that follows a value
--
-- Four things worth knowing, two of which cost people real time.
--
--   1. Gradient coordinates live in the SAME SPACE as the geometry. Get this wrong and you
--      see a flat colour and assume the renderer is broken. units = "bbox" removes the
--      problem entirely.
--   2. Fading to transparent BLACK greys out on the way. Fade to the same colour instead.
--   3. `fea` softens edges. It is what makes vector output look like vector output.
--   4. A colour is not a number, so it cannot be an expression -- but sampling a gradient
--      AT an expression gives you real animated colour.

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
    id = "paint_s", type = "vector",
    rect = { unit = "px", x = 12, y = 12, w = W - 24, h = H - 24 },
    props = {
        scene = "paint",
        w = 120, h = 120,

        -- ------------------------------------------------------------------------- defs
        defs = {
            -- GL is linear: a ramp from (x1,y1) to (x2,y2), in SCENE UNITS by default.
            -- This one is placed deliberately over the shape that uses it, at x 6..54.
            { op = "GL", id = "heat", x1 = 6, y1 = 0, x2 = 54, y2 = 0,
              stops = { { 0, "#2E8B6E" }, { 0.5, "#F59E0B" }, { 1, "#E23D3D" } } },

            -- units = "bbox" spans whatever shape references it, 0..1 across its own
            -- bounding box. No coordinates to get wrong, and it tracks a shape that moves
            -- or resizes. Prefer this unless you specifically want a ramp fixed in space.
            { op = "GL", id = "sheen", units = "bbox", x1 = 0, y1 = 0, x2 = 0, y2 = 1,
              stops = { { 0, "#5FD9A8FF" }, { 1, "#2E8B6EFF" } } },

            -- GR is radial: centre (cx,cy), radius r, optional focus (fx,fy).
            { op = "GR", id = "orb", units = "bbox", cx = 0.5, cy = 0.5, r = 0.5,
              stops = { { 0, "#FFFFFFCC" }, { 1, "#FFFFFF00" } } },

            -- A ramp used as a LOOKUP TABLE rather than as a fill -- see item 6 below.
            { op = "GL", id = "status", units = "bbox", x1 = 0, y1 = 0, x2 = 1, y2 = 0,
              stops = { { 0, "#5FD9A8" }, { 0.5, "#F59E0B" }, { 1, "#E23D3D" } } },

            -- The fade-to-transparent pair, drawn side by side as item 4.
            -- CLEAN: same RGB at both ends, only the alpha changes. The hue holds.
            { op = "GL", id = "fade_clean", units = "bbox", x1 = 0, y1 = 0, x2 = 1, y2 = 0,
              stops = { { 0, "#5FD9A8FF" }, { 1, "#5FD9A800" } } },

            -- MUDDY: fades toward transparent BLACK, so it passes through grey at half
            -- alpha even though both ends look right on their own.
            { op = "GL", id = "fade_muddy", units = "bbox", x1 = 0, y1 = 0, x2 = 1, y2 = 0,
              stops = { { 0, "#5FD9A8FF" }, { 1, "#00000000" } } },
        },

        root = {
            -- 1. Linear gradient in scene coordinates. Declared at x 6..54, drawn at x
            --    6..54. If these disagree you get a flat end-stop colour and it looks
            --    exactly like a broken renderer. It is the single easiest mistake to make.
            { op = "R", x = 6, y = 6, w = 48, h = 18, rx = 3, f = "@heat" },

            -- 2. The same idea with units = "bbox": no coordinates at all, and it would
            --    still be correct if this rectangle moved or changed size.
            { op = "R", x = 66, y = 6, w = 48, h = 18, rx = 3, f = "@sheen" },

            -- 3. Radial, over a circle.
            { op = "C", cx = 30, cy = 50, rx = 16, ry = 16, f = "@orb" },

            -- 4. ALPHA: the fade-to-black trap, side by side.
            --    Blending is straight (not premultiplied), so RGB and alpha interpolate
            --    independently. Fading toward transparent BLACK passes through grey.
            { op = "R", x = 60, y = 36, w = 54, h = 10, rx = 2, f = "@fade_clean" },
            { op = "R", x = 60, y = 50, w = 54, h = 10, rx = 2, f = "@fade_muddy" },

            -- 5. FEATHERING. Without it every edge is hard -- UGUI applies no antialiasing
            --    of its own, so diagonals and curves show pixel steps.
            --
            --    The DEFAULT is automatic and resolves to about 1.3 SCREEN pixels, not a
            --    fixed number of scene units. That matters because the same scene is drawn
            --    at wildly different sizes; a fixed value is invisible when small and a
            --    halo when large. You normally want the default.
            --
            --    Set a number to override, 0 for deliberately hard edges, or something
            --    large for a glow.
            { op = "C", cx = 22, cy = 88, rx = 12, ry = 12, f = "#5FD9A8", fea = 0 },
            { op = "C", cx = 52, cy = 88, rx = 12, ry = 12, f = "#5FD9A8" },
            { op = "C", cx = 82, cy = 88, rx = 12, ry = 12, f = "#5FD9A8", fea = 6 },

            -- 6. ANIMATED COLOUR. The expression evaluator is scalar, so f = "=lerp(...)"
            --    has nothing to return -- a colour is not a number. Instead, sample a ramp
            --    AT an expression. This blends through every stop.
            --
            --    For alarms this is usually what you want: send a number, let the client
            --    decide it means amber. Cheaper and smoother than sending colour strings.
            { op = "C", cx = 105, cy = 88, rx = 9, ry = 9,
              f = { grad = "status", at = "=clamp($level,0,1)" },
              fo = "=0.4+0.6*tri(t*0.8)" },

            -- 7. STROKES. Same paint forms as fills, plus width, caps, joins and dashes.
            { op = "R", x = 6, y = 104, w = 108, h = 10, rx = 5,
              f = "none", s = "#1E3247", sw = 1, dash = { 4, 3 } },
        },
    },
})

local data = ui:element({
    id = "paint_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "paint", data = { level = 0.5 } },
})

ui:commit()

local elapsed = 0

function tick(dt)
    elapsed = elapsed + (dt or 0.5)
    data:set_props({ data = { level = 0.5 + 0.5 * math.sin(elapsed * 0.3) } })
    ui:commit()
end

-- THE FADE TRAP, IN ONE LINE
--
-- Both ramps in item 4 reach alpha 0. Only @fade_clean keeps its colour getting there,
-- because blending is straight rather than premultiplied and RGB interpolates independently
-- of alpha. Fade to the SAME colour at zero alpha, never to #00000000.
--
-- ALPHA MULTIPLIES FROM FOUR PLACES
--   the aa in a colour literal, fo / so, a group's o, and the feather ramp.
