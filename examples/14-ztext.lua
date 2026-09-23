-- 14 - Text and draw order
--
-- A label is covered by anything declared after it and covers anything declared before it --
-- the same rule every other node follows. That is the DEFAULT; there is nothing to switch on.
--
-- It is worth a whole example because it was not always so, and the reason still shapes the
-- cost. Labels are TextMeshPro objects parented beside the mesh rather than part of it, and
-- UGUI draws children in sibling order -- so the whole text layer drew either entirely before
-- or entirely after the whole surface. "On top" and "underneath" were the only two states and
-- they applied to every label at once, which made `z-index` impossible to express.
--
-- WHAT IT COSTS. A label a later shape actually covers forces the mesh to be cut there, and
-- each extra mesh is a draw call. The cut is forced ONLY on a genuine overlap of the two
-- boxes, which a well-placed scene mostly avoids by construction: 12-click.lua has seven
-- labels over nineteen shapes and forces none, because its rows, pills and accent bars were
-- already positioned not to collide. Deciding that costs ~0.19 ms per 40,000 vertices, on the
-- tessellation worker rather than the frame.
--
-- `ztext = 0` restores the old rule, for a scene that deliberately wants a readout floating
-- above artwork drawn after it.
--
-- One case neither setting can serve: a label declared before ANY shape. The surface's own
-- renderer always draws before its children, so there is nothing to put such a label behind.
-- It stays on top rather than vanishing.
--
-- What to look for: two identical scenes, one flag apart. The red shutter slides UNDER the
-- word on the left (the default) and OVER it on the right (`ztext = 0`).

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
        w = 200, h = 240, fit = "contain",   -- even scale: the box is taller than the scene
        -- No ztext here: draw order is what you get without asking.
        root = column("default"),
    },
})

ui:element({
    id = "z_off", type = "vector",
    rect = { unit = "px", x = W / 2 + 5, y = 20, w = W / 2 - 15, h = H - 40 },
    props = {
        scene = "ztext_off",
        w = 200, h = 240, fit = "contain",   -- even scale: the box is taller than the scene
        ztext = 0,
        root = column("ztext = 0"),
    },
})

ui:commit()

-- No tick. Both scenes reference `t`, so they animate on the client with nothing sent.
