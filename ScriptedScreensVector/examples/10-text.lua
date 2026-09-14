-- 10 - Text in the scene
--
-- Text used to be out of scope here, and the advice was to layer ScriptedScreens `label`
-- elements over the artwork. It is a node now.
--
-- NOT MAINLY A BUDGET WIN. `ui:element` is a C call and costs the chip very little; the
-- per-label cost is the Lua around it, which is the same either way. Measured on a real
-- console, a page of 90 labels became one src and went from 31.5k instructions to 28.9k --
-- a real saving, and a modest one.
--
-- What a T node actually buys:
--
--   * its text changes WITHOUT re-declaring an element
--   * it is written in viewbox units, so no number is converted into console pixels twice
--   * it moves, scales, rotates and scrolls with the group it is in
--   * it can FORMAT A NUMBER itself, so the chip sends the value it already had and does no
--     string work at all -- see section 2 below
--
-- T MOVES WITH ITS GROUP. Rotate the group and the text rotates. Scale it and the type
-- scales. Put it in a scroll container and it scrolls. That is the part a label cannot do at
-- any price, and it is why a readout attached to a moving needle is now a sane thing to draw.
--
-- WHAT IT CANNOT DO, because TMP builds its own geometry on its own object:
--   * it updates at the REBUILD rate, ~30 Hz, not the instant a value changes
--   * it clips to an AXIS-ALIGNED RECTANGLE only -- a rounded clip cuts the shapes around
--     the text but not the text
--   * `f` is a flat colour, sampled at the node's origin. No gradient fills on type.
--   * a group's opacity reaches it as the LABEL's alpha, not by fading it with the mesh --
--     which is the right answer, but it means `o` cannot tint type the way it tints a fill
--
-- DRAW ORDER IS ORDINARY, though it took work to make it so: a shape declared after a `T`
-- covers it, one declared before it does not. See 14-ztext.
--
-- Fonts come from the companion fonts mod: any family TMP knows can be named here.

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

ui:element({
    id = "text_s", type = "vector",
    rect = { unit = "px", x = 12, y = 12, w = W - 24, h = H - 24 },
    props = {
        scene = "text",
        w = 200, h = 200,

        root = {

            -- 1. THE PLAIN CASE. `text` takes a literal, or "$name" to bind a data string.
            { op = "T", x = 10, y = 8, w = 180, h = 12,
              text = "TEXT NODES", size = 8, cspace = 4, f = "#5A7085" },

            { op = "R", x = 10, y = 24, w = 180, h = 34, rx = 4, f = "#0B1622" },

            { op = "T", x = 18, y = 30, w = 80, h = 10,
              text = "PRESSURE", size = 7, cspace = 2, f = "#5A7085" },

            -- Alignment is horizontal (align) and vertical (valign), both optional.
            { op = "T", x = 100, y = 28, w = 84, h = 24,
              text = "$pressure", size = 18, align = "right", valign = "middle",
              f = "#EAF4F8" },

            -- 2. THE SAME READOUT, WITH NO STRING WORK ON THE CHIP. `fmt` takes the printf
            --    spec you would have passed to string.format -- because that is the one you
            --    already know -- and `unit` is a literal suffix. The payload then carries the
            --    NUMBER, which the script had anyway, instead of a formatted string.
            --
            --    Supported: f e g d i x X, with precision. Width and flags are ignored: lay
            --    text out with `align` inside a box, not by padding with spaces.
            { op = "R", x = 10, y = 114, w = 180, h = 30, rx = 4, f = "#0B1622" },

            { op = "T", x = 18, y = 120, w = 60, h = 18,
              text = "RAW", size = 7, valign = "middle", f = "#5A7085" },

            { op = "T", x = 78, y = 118, w = 104, h = 22,
              text = "$kpa", fmt = "%.1f", unit = " kPa",
              size = 14, align = "right", valign = "middle", f = "#5FD9A8" },

            --    `missing` is what a name with no value renders. It defaults to "--", which
            --    is why a console that has not had its first payload yet reads as pending
            --    rather than as an empty box that looks like a layout mistake.
            { op = "T", x = 78, y = 140, w = 104, h = 12,
              text = "$never_sent", fmt = "%.0f", unit = " kPa", missing = "no data",
              size = 7, align = "right", f = "#3A5570" },

            -- 3. FITTING. `ellipsis` truncates, `shrink` scales down to `min_size`. Both are
            --    TMP's own overflow modes, so the engine that owns the glyph metrics does
            --    the work rather than Lua estimating it.
            { op = "R", x = 10, y = 88, w = 180, h = 22, rx = 4, f = "#0B1622" },

            { op = "T", x = 18, y = 92, w = 164, h = 14,
              text = "$long", size = 9, valign = "middle", f = "#8FA6B8", fit = "ellipsis" },

            -- 4. TEXT THAT MOVES WITH THE ARTWORK. The group carries both the needle and its
            --    readout, so one rotation expression places both -- and the label stays put
            --    relative to the needle however it swings.
            { op = "C", cx = 100, cy = 168, rx = 26, ry = 26, f = "#0B1622" },
            { op = "C", cx = 100, cy = 168, rx = 26, ry = 26,
              f = "none", s = "#1E3247", sw = 1.5 },

            { op = "G", t = { 100, 168 }, r = "=-120+240*clamp($fill,0,1)", c = {
                { op = "R", x = -1, y = -23, w = 2, h = 23, rx = 1, f = "#F59E0B" },

                -- Written upright at the tip; the group's rotation carries it round.
                { op = "T", x = -20, y = -35, w = 40, h = 10,
                  text = "$pct", size = 7, align = "center", f = "#F59E0B" },
            } },

            { op = "C", cx = 100, cy = 168, rx = 3, ry = 3, f = "#F59E0B" },

            -- 5. A REGISTERED FONT, and rich text. Both are real TMP, so anything TMP
            --    understands works -- but an unknown family silently keeps the current face
            --    rather than blanking the label, so check the fonts mod's log if a name has
            --    no effect.
            { op = "T", x = 10, y = 76, w = 180, h = 10,
              text = "code face: <b>0123</b> 456", size = 7, font = "code",
              align = "center", f = "#3A5570" },
        },
    },
})

-- Strings live in `data` beside the numbers. A colour string is stored as both -- the scene
-- may want to print "#FF0000" as well as paint with it.
local data = ui:element({
    id = "text_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "text", keep = 1, data = {
        fill = 0.5, pressure = "--", pct = "--",
        long = "a line long enough to need trimming in a narrow box",
    } },
})

local phase = 0

function tick(dt)
    phase = phase + (dt or 0.5)
    local fill = 0.5 + 0.5 * math.sin(phase * 0.4)

    data:set_props({ data = {
        fill     = fill,

        -- The old way: the chip formats and ships a string, every tick, per label.
        pressure = string.format("%.1f kPa", 40 + fill * 60),
        pct      = string.format("%d%%", math.floor(fill * 100 + 0.5)),

        -- The new way: ship the number. `fmt` and `unit` on the node do the rest.
        kpa      = 40 + fill * 60,

        -- `long` is not resent: `keep = 1` means it holds its value from the declaration.
    } })

    ui:commit()
end
