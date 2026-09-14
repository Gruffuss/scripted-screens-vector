-- 14 - Text in draw order
--
-- Labels are TextMeshPro objects parented beside the geometry, not part of the mesh, and
-- UGUI draws children in sibling order. So by default the WHOLE text layer draws after the
-- WHOLE surface: a shape declared after a label still cannot cover it. "On top" and
-- "underneath" are the only two states and they apply to every label at once.
--
-- `ztext = 1` on the scene makes labels obey scene order instead, like everything else.
--
-- IT IS OPT-IN, and deliberately so. Text on top is what every scene written before this
-- existed relies on -- often correctly, since it was the only behaviour available. Turning
-- it on changes only the cases where a shape genuinely covers a label.
--
-- WHAT IT COSTS. A label that has to sit under later geometry forces the mesh to be cut
-- there, and each extra mesh is a draw call. So a cut is forced ONLY where a later shape
-- actually overlaps the label's box -- which on a normal page is close to never, because
-- tiles do not overlap their neighbours and a label sits inside its own tile. A page of
-- thirty labelled tiles stays one mesh. A page that really does slide panels over text pays
-- one draw call per such label, which is the price of asking for it.
--
-- One case it cannot serve: a label declared before ANY shape. The surface's own renderer
-- always draws before its children, so there is nothing to put such a label behind. It stays
-- on top rather than vanishing.
--
-- What to look for: two identical scenes, one flag apart. The red shutter slides UNDER the
-- word on the left, and OVER it on the right.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 480, 480
if size then W, H = size.w, size.h end

ui:clear()

ui:element({
    id = "bg", type = "panel",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    style = { bg = "#0A121C" },
})

-- The two columns are built from one function so nothing but `ztext` can differ between
-- them. A demo that proves a flag has to hold everything else still.
local function column(caption)
    return {
        { op = "T", x = 0, y = 4, w = 200, h = 14,
          text = caption, size = 9, align = "center", f = "#5A7085" },

        -- A card, a label ON the card, then a shutter declared AFTER the label.
        { op = "R", x = 20, y = 40, w = 160, h = 90, rx = 8, f = "#182230" },
        { op = "T", x = 20, y = 70, w = 160, h = 30,
          text = "COVERED?", size = 17, align = "center", f = "#7EE0B8" },
        { op = "R", x = "=20+160*(0.5+0.5*sin(t*0.9))", y = 40, w = 160, h = 90, rx = 8,
          f = "#E2563D", fo = 0.85 },

        -- A control: here the label is declared LAST, so it is on top either way and the
        -- two columns must look identical. If this one differs, something else changed.
        { op = "R", x = 20, y = 160, w = 160, h = 60, rx = 8, f = "#182230" },
        { op = "T", x = 20, y = 178, w = 160, h = 24,
          text = "always on top", size = 12, align = "center", f = "#5B8FB0" },
    }
end

ui:element({
    id = "z_on", type = "vector",
    rect = { unit = "px", x = 10, y = 20, w = W / 2 - 15, h = H - 40 },
    props = {
        scene = "ztext_on",
        w = 200, h = 240,
        ztext = 1,
        root = column("ztext = 1"),
    },
})

ui:element({
    id = "z_off", type = "vector",
    rect = { unit = "px", x = W / 2 + 5, y = 20, w = W / 2 - 15, h = H - 40 },
    props = {
        scene = "ztext_off",
        w = 200, h = 240,
        root = column("default"),
    },
})

ui:commit()

-- No tick. Both scenes reference `t`, so they animate on the client with nothing sent.
