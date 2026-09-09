-- 13 - The same scene twice: as tables, and as text
--
-- Both halves of this file draw the same gauge. The left one is built from nested Lua tables;
-- the right one is a string literal. They go through the same parser, so the pictures are
-- identical -- what differs is what it costs the chip to hand them over.
--
--   nested tables    ~a dozen instructions per node, paid every time the structure is built
--   a string literal ZERO. It is already in the compiled chunk.
--
-- With 50,000 instructions per tick and a tick every half second, a page of a few hundred
-- nodes is a serious fraction of the budget -- which is what forces consoles to build their
-- screens across several frames. The text form removes that problem rather than pacing it.
--
-- THE GRAMMAR is one node per line: OP then key=value pairs, { } for children, # for a
-- comment. SCENE carries the viewbox, DEFS { } holds gradients and clips. Same op names,
-- same keys, same expressions as the table form. A bare word is a flag, so `lod` means
-- `lod=1`.
--
-- TWO RULES THAT WILL BITE YOU, both from Lua rather than from the format:
--
--   1. WRAP IT IN [==[ ... ]==], not [[ ... ]]. An array value ends in `]]`, and a Lua long
--      string closes at the FIRST one it meets -- so `stops=[[0,#5FD9A8],[1,#2E8B6E]]` cuts
--      your scene off there and everything below vanishes with no error.
--
--   2. AN UNQUOTED VALUE IS READ TO THE NEXT SPACE, so an expression cannot contain one.
--      `y==64-$fill*52` is fine. `y="=64 - $fill * 52"` needs the quotes. Expressions are
--      full of commas and brackets, so whitespace is the only separator left over.
--
-- Note `y==...` is not a typo: the first `=` separates key from value, the second begins the
-- expression.

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

-- ---------------------------------------------------------------- tables, on the left

ui:element({
    id = "src_tables", type = "vector",
    rect = { unit = "px", x = 12, y = 40, w = (W - 36) / 2, h = H - 80 },
    props = {
        scene = "src_a",
        w = 100, h = 200,

        defs = {
            { op = "GL", id = "liquid", units = "bbox", x1 = 0, y1 = 0, x2 = 0, y2 = 1,
              stops = { { 0, "#5FD9A8" }, { 1, "#2E8B6E" } } },
            { op = "CP", id = "tank", c = {
                { op = "R", x = 20, y = 20, w = 60, h = 160, rx = 10 },
            } },
        },

        root = {
            { op = "R", x = 20, y = 20, w = 60, h = 160, rx = 10, f = "#0B1622" },

            { op = "G", clip = "tank", c = {
                { op = "YS", n = 24,
                  x  = "=18+i*2.8",
                  y  = "=180-clamp($fill,0,1)*152+3*sin(i*0.5+t*1.6)",
                  y2 = 184,
                  f  = "@liquid", fea_edge = 0 },
            } },

            { op = "R", x = 20, y = 20, w = 60, h = 160, rx = 10,
              f = "none", s = "#1E3247", sw = 2 },

            { op = "T", x = 20, y = 190, w = 60, h = 10,
              text = "TABLES", size = 7, cspace = 2, align = "center", f = "#3A5570" },
        },
    },
})

-- ---------------------------------------------------------------- text, on the right
--
-- Identical picture. Note the DEFS block, the clip, the expressions and the flag-style
-- `fea_edge=0` are all here -- the format is not a reduced subset.

ui:element({
    id = "src_text", type = "vector",
    rect = { unit = "px", x = W / 2 + 6, y = 40, w = (W - 36) / 2, h = H - 80 },
    props = {
        scene = "src_b",
        src = [==[
# viewbox and fit live on SCENE, not on the element props
SCENE w=100 h=200 fit=stretch

DEFS {
    GL id=liquid units=bbox x1=0 y1=0 x2=0 y2=1 stops=[[0,#5FD9A8],[1,#2E8B6E]]
    CP id=tank { R x=20 y=20 w=60 h=160 rx=10 }
}

R x=20 y=20 w=60 h=160 rx=10 f=#0B1622

G clip=tank {
    YS n=24 x==18+i*2.8 y==180-clamp($fill,0,1)*152+3*sin(i*0.5+t*1.6) y2=184 f=@liquid fea_edge=0
}

R x=20 y=20 w=60 h=160 rx=10 f=none s=#1E3247 sw=2

T x=20 y=190 w=60 h=10 text=TEXT size=7 cspace=2 align=center f=#3A5570
]==],
    },
})

-- Both scenes read the same names, so one data element each with the same payload.
local a = ui:element({
    id = "src_da", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "src_a", data = { fill = 0.5 } },
})

local b = ui:element({
    id = "src_db", type = "vector",
    rect = { unit = "px", x = -6, y = -6, w = 1, h = 1 },
    props = { scene = "src_b", data = { fill = 0.5 } },
})

ui:element({
    id = "src_title", type = "label",
    rect = { unit = "px", x = 0, y = 8, w = W, h = 24 },
    props = { text = "one scene, two ways to say it" },
    style = { font_size = 14, color = "#5A7085", align = "center" },
})

local phase = 0

function tick(dt)
    phase = phase + (dt or 0.5)
    local fill = 0.5 + 0.45 * math.sin(phase * 0.3)

    a:set_props({ data = { fill = fill } })
    b:set_props({ data = { fill = fill } })

    ui:commit()
end
