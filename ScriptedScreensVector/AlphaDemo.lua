-- Alpha and blending.
--
-- Everything composites with straight (non-premultiplied) source-over alpha, which is
-- what UGUI's UI shader does. There is no pre-blending against a known backdrop -- the
-- thing the canvas-era GasUI.lua had to build whole palette tables for.
--
-- Four places alpha comes from, and they all multiply together:
--   1. the colour itself, as #rrggbbaa
--   2. fo / so, which are expressions
--   3. group opacity `o`, which multiplies into every descendant
--   4. feathering, which ramps the edge to zero

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
    props = { text = "ALPHA / BLENDING" },
    style = { font_size = 15, color = "#5FD9A8", align = "left" },
})

local scene = {
    scene = "alpha",
    w = 200, h = 200, fit = "stretch",
    defs = {
        -- CLEAN fade: RGB is held constant and only alpha drops. The hue stays true all
        -- the way out.
        -- Amber over white, deliberately. A green fade on a greenish ground hides the
        -- difference: the muddy midtone is only ~54/255 away and the eye has no reference.
        -- Amber to transparent-black over white is ~61/255 AND shifts hue -- warm cream
        -- against dull khaki -- which is what makes it readable rather than theoretical.
        { op = "GL", id = "clean", x1 = 12, y1 = 0, x2 = 94, y2 = 0,  -- left pair
          stops = { { 0, "#F59E0BFF" }, { 1, "#F59E0B00" } } },

        -- MUDDY fade: fading to transparent BLACK. Straight alpha blends RGB and A
        -- independently, so the midpoint is grey at half alpha and the ramp looks dirty.
        -- Same alpha curve, visibly worse colour.
        { op = "GL", id = "muddy", x1 = 106, y1 = 0, x2 = 188, y2 = 0,  -- right pair
          stops = { { 0, "#F59E0BFF" }, { 1, "#00000000" } } },
    },
    root = {
        -- 1. The two fades, each drawn over BOTH a dark and a light backing.
        --
        --    The light half is the whole point. Over dark, "fade to transparent green"
        --    and "fade to transparent black" look identical -- the muddy midtone is grey
        --    against a dark ground and hides. Over a light backing the muddy ramp goes
        --    visibly dirty through the middle while the clean one just thins out.
        { op = "R", x = 12, y = 22, w = 82, h = 18, f = "#0A1520" },
        { op = "R", x = 12, y = 40, w = 82, h = 18, f = "#FFFFFF" },
        { op = "R", x = 12, y = 22, w = 82, h = 36, f = "@clean" },

        { op = "R", x = 106, y = 22, w = 82, h = 18, f = "#0A1520" },
        { op = "R", x = 106, y = 40, w = 82, h = 18, f = "#FFFFFF" },
        { op = "R", x = 106, y = 22, w = 82, h = 36, f = "@muddy" },

        -- 2. Overlapping translucent discs. Where they cross, the colours genuinely
        --    composite -- no pre-blending, no palette tables.
        { op = "C", cx = 58, cy = 100, rx = 26, ry = 26, f = "#5FD9A8", fo = 0.55 },
        { op = "C", cx = 80, cy = 100, rx = 26, ry = 26, f = "#F59E0B", fo = 0.55 },
        { op = "C", cx = 69, cy = 118, rx = 26, ry = 26, f = "#E23D3D", fo = 0.55 },

        -- 3. Alpha straight from the colour literal, as #rrggbbaa. Stepped so the
        --    compositing is easy to judge against the background.
        { op = "RP", n = 8, c = {
            { op = "R",
              x = "=118+i*9",
              y = 84, w = 8, h = 52,
              f = "#5FD9A8" ,
              fo = "=0.12+i*0.125" },
        } },

        -- 4. Group opacity multiplies into everything below it, including children that
        --    already have their own fo. This whole group breathes as one.
        { op = "G", o = "=0.25+0.75*tri(t*0.35)", c = {
            { op = "R", x = 12, y = 150, w = 82, h = 34, rx = 6, f = "#2E8B6E" },
            { op = "C", cx = 34, cy = 167, rx = 11, ry = 11, f = "#FFFFFF", fo = 0.7 },
            { op = "C", cx = 62, cy = 167, rx = 11, ry = 11, f = "#E23D3D", fo = 0.7 },
        } },

        -- 5. A heavy feather. Feathering is alpha ramped to zero along the edge, so it is
        --    the same mechanism -- and it is why the default edge does not look hard.
        { op = "C", cx = 147, cy = 167, rx = 22, ry = 22, f = "#F59E0B", fea = 10 },
    },
}

ui:element({
    id = "alpha_structure",
    type = "vector",
    rect = { unit = "px", x = 14, y = 36, w = W - 28, h = H - 50 },
    props = scene,
})

ui:commit()
