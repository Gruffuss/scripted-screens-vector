-- Everything that shipped without ever being seen on a console. Nothing here is new; each row
-- is a thing that has only ever been checked offline, or not at all.
--
-- 1 `ver`   must read major*10000 + minor*100 + patch of the RUNNING build: 1176 on
--           0.11.76, 1180 on 0.11.80. The point of `ver` is
--           an exported scene that can tell it is running on a mod too old to draw it, so the
--           number has to be right, not merely present.
-- 2 x % 0   must print 0 and must not hang or blank the scene. A divide guard that is wrong
--           takes the whole surface with it.
-- 3 %x      the hex format: 255 must print `ff`.
-- 4 inset   a shadow marked inset by the BOOLEAN true, beside the same shadow marked by the
--           string "inset". The two boxes must look identical: shadow inside the shape, dark
--           at the top edge. If the boolean form is not read, that box gets an OUTER shadow
--           instead and the difference is obvious.
-- 5 bbox    a stroke painted by a gradient with units=bbox, on two shapes of different sizes.
--           Each stroke must run the full ramp over its OWN width -- the short box and the
--           long box both go green to blue end to end. If the binding is wrong the short one
--           shows only the first slice of the ramp.
-- 6 @ghost  a fill naming a gradient that was never declared must draw MAGENTA and be listed
--           by vector_stats. Silence here is the bug.
-- 7 sh      a CLOSED shape with a shadow and NO fill. New in 0.11.74: it now casts its shadow
--           like an R or a C always has. Before, a closed unfilled Y cast nothing.
--
-- Read vector_stats as well: it must list exactly ONE problem, the undeclared gradient in 6.

local ui = ss.ui.surface("main")
ss.ui.activate("main")
if ss.ui.allow_mcp_automation then ss.ui.allow_mcp_automation(true) end

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

ui:element({
    id = "arrears", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = {
        scene = "arrears",
        w = 200, h = 200, fit = "stretch",
        size = 5, f = "#EAF4F8",

        -- `defs` is an ARRAY of maps, each with its own `op` and `id`. Written as a map keyed
        -- by id it parses to nothing at all, silently, and every `@name` using it is then
        -- undeclared.
        defs = {
            { op = "GL", id = "ramp", units = "bbox",
              x1 = 0, y1 = 0, x2 = 1, y2 = 0,
              stops = { { 0, "#5FD9A8" }, { 1, "#7FB2F0" } } },
        },

        root = {
            { op = "R", x = 0, y = 0, w = 200, h = 200, f = "#0B1622", fea = 0 },

            { op = "T", x = 4, y = 3, w = 192, h = 6,
              text = "1 ver = {=ver}   (= major*10000+minor*100+patch)" },
            { op = "T", x = 4, y = 11, w = 192, h = 6,
              text = "2 5 % 0 = {=5%0}   3 255 as hex = {=255:%x}" },

            -- 4 inset by boolean vs by string: the two must match
            { op = "T", x = 4, y = 22, w = 192, h = 6,
              text = "4 inset: boolean (left) must match the string (right)" },
            { op = "R", x = 8, y = 30, w = 56, h = 26, f = "#2E8B6E", fea = 0,
              sh = { { 0, 3, 6, 0, "#000000cc", true } } },
            { op = "R", x = 76, y = 30, w = 56, h = 26, f = "#2E8B6E", fea = 0,
              sh = { { 0, 3, 6, 0, "#000000cc", "inset" } } },

            -- 5 a bbox gradient as a STROKE, on two different widths
            { op = "T", x = 4, y = 62, w = 192, h = 6,
              text = "5 units=bbox stroke: both run green to blue end to end" },
            { op = "R", x = 8, y = 70, w = 40, h = 20, s = "@ramp", sw = 3, f = "none" },
            { op = "R", x = 56, y = 70, w = 130, h = 20, s = "@ramp", sw = 3, f = "none" },

            -- 6 a gradient that does not exist
            { op = "T", x = 4, y = 96, w = 192, h = 6,
              text = "6 f=@ghost: MAGENTA, and one problem in vector_stats" },
            { op = "R", x = 8, y = 104, w = 56, h = 22, f = "@ghost", fea = 0 },

            -- 7 a closed shape with a shadow and no fill at all
            { op = "T", x = 4, y = 132, w = 192, h = 6,
              text = "7 closed Y, stroke only, must still cast a shadow" },
            { op = "Y", p = { 20, 150, 70, 150, 70, 180, 20, 180 },
              s = "#EAF4F8", sw = 1, f = "none",
              sh = { { 4, 4, 5, 0, "#000000dd" } } },

            -- the same thing filled, as the control: both shadows must look the same
            { op = "Y", p = { 110, 150, 160, 150, 160, 180, 110, 180 },
              f = "#2E8B6E", fea = 0,
              sh = { { 4, 4, 5, 0, "#000000dd" } } },
            { op = "T", x = 4, y = 186, w = 192, h = 6,
              text = "left is unfilled, right is the filled control" },
        },
    },
})

ui:commit()
