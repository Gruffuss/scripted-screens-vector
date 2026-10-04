-- The five 0.11.74 fixes, on one page. Four are visible; one needs a click each.
--
-- NOTE the two `f=none`s. The root style carries `f=#EAF4F8` for the labels, and from 0.11.74
-- that reaches EVERY node, so a shape meant to be stroke-only inherits it and fills. That is
-- the same thing a `G style={f=...}` has always done to a stroke-only child; it is just much
-- easier to do by accident on the root. `f=none` is the way out.
--
-- 1 A `style` ON THE SCENE ROOT
--   `size=9 f=#EAF4F8` sit on the SCENE line and are read by nothing before 0.11.74. Every
--   label on this page relies on them: if the root style is ignored the whole page draws at
--   the 12-unit default size in white, which is obvious, and row 1's two words differ.
--   `fit` must NOT come down from the root -- the SCENE line says `fit=stretch`, and a label
--   that shrank to fit would prove it leaked.
--
-- 2 BLUR ON SHAPES THAT ARE NOT R OR C
--   Three shapes inside one `G blur=3`: a rectangle (blurred before 0.11.74), a polygon and
--   a path. ALL THREE must be equally soft. Before, the triangle and the path were sharp.
--
-- 3 A CLOSED SPLINE
--   `SP close=1` filled, beside the same points without `close`. The closed one must be a
--   filled rounded blob with no visible seam where the curve rejoins; the open one is a
--   stroked curve only. Before 0.11.74 `close` was not a key and both drew the same.
--
-- 4 CLICKS ON EVERY SHAPE
--   Four shapes, one click each: a polygon, a polyline, a closed spline and a path. Each must
--   turn its own tick green when clicked. Before 0.11.74 none of the four registered a hit at
--   all, so all four stayed grey however hard they were clicked, and nothing was reported.
--
-- 5 THE CLOCK SURVIVES AN IDENTICAL RESEND
--   The structure element is re-upserted with the SAME text every tick. The bar must sweep and
--   the seconds must count up. Before 0.11.74 `t` reset on every upsert, so the bar sat at its
--   left edge and the counter read 0.0 for ever.
--
-- Read `vector_stats` too: this page must report NO problems.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

-- So the four clickable shapes can be driven without standing at the console and aiming.
if ss.ui.allow_mcp_automation then ss.ui.allow_mcp_automation(true) end

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

local src = table.concat({
    -- 1 the root style: size and fill for every label below, and a `fit` that must not inherit
    "SCENE w=200 h=200 fit=stretch size=5 f=#EAF4F8",

    "R x=0 y=0 w=200 h=200 f=#0B1622 fea=0",

    'T x=4 y=3 w=192 h=6 text="1 root style: both words same size, same colour"',

    -- 2 one blur over three shapes of different kinds
    'T x=4 y=12 w=192 h=6 text="2 blur: all THREE equally soft"',
    "G blur=3 {",
    " R x=8 y=20 w=34 h=26 f=#5FD9A8 fea=0",
    " Y p=[55,46,72,20,89,46] f=#F5D76E fea=0",
    ' P d="M102 46 L112 20 L128 20 L138 46 Z" f=#7FB2F0 fea=0',
    "}",

    -- 3 closed spline against the same points open
    'T x=4 y=52 w=192 h=6 text="3 SP close=1 fills, no seam; open one is a line"',
    "SP p=[12,62,30,58,44,72,30,86,12,82] close=1 seg=12 f=#C98BE0 fea=0",
    "SP p=[72,62,90,58,104,72,90,86,72,82] seg=12 s=#C98BE0 sw=1.2 f=none",

    -- 4 a hit area on each of the four shapes that had none
    'T x=4 y=92 w=192 h=6 text="4 click each shape: its tick turns green"',
    "Y id=hitY p=[10,102,34,102,34,124,10,124] f=#3A4A5A press=1",
    "L id=hitL p=[44,102,76,102,76,124,44,124] s=#3A4A5A sw=6 press=1 f=none",
    "SP id=hitSP p=[88,104,112,100,120,112,112,124,88,120] close=1 seg=10 f=#3A4A5A press=1",
    'P id=hitP d="M132 102 L164 102 L164 124 L132 124 Z" f=#3A4A5A press=1',

    -- One label per tick, coloured by a data string. Two overlapping labels that differed
    -- only in colour was unreadable: whichever drew, the answer looked the same.
    'T x=10 y=128 w=24 h=7 text="Y" f=$tickY size=7',
    'T x=44 y=128 w=24 h=7 text="L" f=$tickL size=7',
    'T x=88 y=128 w=24 h=7 text="SP" f=$tickSP size=7',
    'T x=132 y=128 w=24 h=7 text="P" f=$tickP size=7',

    'T x=4 y=140 w=192 h=6 text=$last missing="(nothing clicked yet)" f=#8FA6B8',

    -- 5 the clock, under a structure resent identically every tick
    'T x=4 y=152 w=192 h=6 text="5 clock: bar sweeps, seconds count up"',
    "R x=8 y=160 w=184 h=10 f=#16222E fea=0",
    'R x=8 y=160 w="=184*(t%4)/4" h=10 f=#5FD9A8 fea=0',
    'T x=4 y=176 w=192 h=9 text="t = {=t:%.1f} s" size=8',
}, "\n")

local hits = {}
local data

local structure = ui:element({
    id = "v74_s", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = { scene = "v74", src = src },
    on_click = function(value)
        -- `down:hitY` and `hitY` both count; the tick only has to light.
        local id = value:match("hit(%a+)$")
        if id then hits[id] = 1 end

        local values = { last = value }
        for k, v in pairs(hits) do values["tick" .. k] = "#5FD9A8" end

        data:set_props({ scene = "v74", keep = 1, snap = 1, data = values })
        ui:commit()
    end,
})

-- The data element, so the ticks have names to read before anything is clicked.
data = ui:element({
    id = "v74_d", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
    props = { scene = "v74", keep = 1, data = {
        tickY = "#53646F", tickL = "#53646F", tickSP = "#53646F", tickP = "#53646F" } },
})

ui:commit()

-- 5: the same structure, re-upserted every tick. This is the shape that froze `t` at 0.
function tick()
    structure:set_props({ scene = "v74", src = src })
    ui:commit()
end
