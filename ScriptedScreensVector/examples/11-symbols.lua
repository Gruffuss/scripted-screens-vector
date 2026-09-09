-- 11 - Writing a thing once: SYM, USE, inherited style, per-corner radii
--
-- A console is mostly the same row, cell or lamp repeated with different numbers. RP handles
-- the case where every copy is identical apart from `i`. SYM handles the other case: copies
-- that differ in COLOUR, SIZE or LABEL, where an expression over `i` would be a lookup table
-- written as arithmetic.
--
--   SYM   declares a subtree in `defs`, with `params` giving defaults
--   USE   stamps it, and EVERY attribute on the USE is a parameter
--
-- %name substitution is TEXTUAL and happens ONCE, at parse time. So a symbol costs exactly
-- what writing the nodes out would have cost -- this saves authoring, not drawing. It is the
-- opposite trade from RP, which saves both but cannot vary anything except by expression.
--
-- %name ALONE keeps the parameter's type, so a number stays a number. Inside a longer string
-- it splices as text, which is what makes it work in expressions: y = "=%top+i*4".
--
-- Two smaller conveniences are shown alongside, because they solve the same problem:
--   style   inherited defaults on a group, so twenty shapes say `fea = 0` once
--   rx = {} per-corner radii in CSS order, tl tr br bl, so a tab is one node

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
    id = "sym_s", type = "vector",
    rect = { unit = "px", x = 12, y = 12, w = W - 24, h = H - 24 },
    props = {
        scene = "sym",
        w = 200, h = 200,

        defs = {
            -- A lamp: a lit core with a soft halo. `r` and `col` have defaults, so a plain
            -- USE needs neither.
            { op = "SYM", id = "lamp", params = { r = 4, col = "#5FD9A8" }, c = {
                { op = "C", cx = 0, cy = 0, rx = "=%r*2.2", ry = "=%r*2.2", f = "%col", fo = 0.18 },
                { op = "C", cx = 0, cy = 0, rx = "%r", ry = "%r", f = "%col" },
            } },

            -- A meter row: label plate, track, and a bar whose width is an expression the
            -- INSTANCE supplies. A parameter can be an expression, which is what makes one
            -- symbol serve four different data names.
            { op = "SYM", id = "row",
              params = { w = 150, col = "#5FD9A8", v = 0.5, name = "?" }, c = {
                { op = "R", x = 0, y = 0, w = "%w", h = 14, rx = 3, f = "#0B1622" },
                { op = "T", x = 6, y = 0, w = 46, h = 14,
                  text = "%name", size = 7, valign = "middle", f = "#5A7085" },
                { op = "R", x = 54, y = 4, w = "=(%w-60)", h = 6, rx = 3, f = "#12202F" },
                { op = "R", x = 54, y = 4, w = "=(%w-60)*clamp(%v,0,1)", h = 6, rx = 3, f = "%col" },
            } },
        },

        root = {

            { op = "T", x = 10, y = 8, w = 180, h = 12,
              text = "SYMBOLS", size = 8, cspace = 4, f = "#5A7085" },

            -- 1. Four rows, four data names, one symbol. Note `v` is an expression string:
            --    it splices as TEXT into `clamp(%v,0,1)`, so it is written WITHOUT the
            --    leading `=`. The attribute it lands in already has one, and two would be a
            --    parse error -- splicing is textual, not nested evaluation.
            { op = "USE", ref = "row", x = 20, y = 26, name = "O2",  v = "$o2" },
            { op = "USE", ref = "row", x = 20, y = 46, name = "N2",  v = "$n2",  col = "#4E8FD9" },
            { op = "USE", ref = "row", x = 20, y = 66, name = "CO2", v = "$co2", col = "#F59E0B" },
            { op = "USE", ref = "row", x = 20, y = 86, name = "VOL", v = "$vol", col = "#E23D3D" },

            -- 2. Lamps, taking their defaults or overriding them. A USE is a group, so `x`,
            --    `y`, `o` and `clip` mean what they mean on a G.
            { op = "USE", ref = "lamp", x = 40, y = 124 },
            { op = "USE", ref = "lamp", x = 70, y = 124, col = "#F59E0B" },
            { op = "USE", ref = "lamp", x = 100, y = 124, r = 6, col = "#E23D3D" },
            { op = "USE", ref = "lamp", x = 135, y = 124, r = 3, col = "#4E8FD9", o = 0.4 },

            -- 3. INHERITED STYLE. Everything in this group gets the same stroke and no
            --    feather, stated once. A child that states its own wins.
            { op = "G", style = { f = "none", s = "#1E3247", sw = 1, fea = 0 }, c = {
                { op = "R", x = 20, y = 150, w = 36, h = 20, rx = 3 },
                { op = "R", x = 62, y = 150, w = 36, h = 20, rx = 3 },
                { op = "R", x = 104, y = 150, w = 36, h = 20, rx = 3, s = "#5FD9A8" },
                { op = "R", x = 146, y = 150, w = 36, h = 20, rx = 3 },
            } },

            -- 4. PER-CORNER RADII, CSS order tl tr br bl. A zero corner is a sharp point,
            --    and emits exactly one vertex rather than a degenerate arc.
            { op = "R", x = 20, y = 178, w = 60, h = 16, rx = { 8, 8, 0, 0 }, f = "#12202F" },
            { op = "R", x = 84, y = 178, w = 60, h = 16, rx = { 0, 8, 8, 0 }, f = "#12202F" },
        },
    },
})

local data = ui:element({
    id = "sym_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "sym", data = { o2 = 0.2, n2 = 0.7, co2 = 0.05, vol = 0.5 } },
})

local phase = 0

function tick(dt)
    phase = phase + (dt or 0.5)

    data:set_props({ data = {
        o2  = 0.5 + 0.4 * math.sin(phase * 0.30),
        n2  = 0.5 + 0.4 * math.sin(phase * 0.21 + 1.0),
        co2 = 0.5 + 0.4 * math.sin(phase * 0.44 + 2.0),
        vol = 0.5 + 0.4 * math.sin(phase * 0.17 + 3.0),
    } })

    ui:commit()
end
