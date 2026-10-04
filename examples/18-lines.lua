-- 18 - Lines and outlines: caps, joins, dashes, and closing a curve
--
-- Everything here is about the STROKE -- the part of a shape that is drawn along its edge
-- rather than inside it. `06-paint.lua` covers fills; this covers the line.
--
--     sw        width, in scene units, centred on the path
--     cap       butt (default) / round / square -- how a line ENDS
--     join      miter (default) / round / bevel -- how a corner TURNS
--     ml        miter limit: past this angle a miter becomes a bevel, as SVG does it
--     dash      [on, off, on, off, ...] in scene units; `dofs` slides the pattern along
--
-- CLOSING A CURVE. `L` is an open polyline and `Y` is the same points closed; `SP` is a smooth
-- curve through its points, and `SP close = 1` wraps it into a ring. Only a CLOSED shape has
-- an inside, so only a closed shape takes a fill, a shadow or a group's `blur`.
--
-- THAT ALSO DECIDES WHERE IT IS CLICKABLE. A click lands on what is drawn: a closed shape
-- answers anywhere inside its outline, an open one only along its stroke. An `L` drawn as
-- three sides of a box does not answer in the middle, because it never drew the middle.
--
-- DASHES COST VERTICES. Each dash is its own little quad strip, so a dashed outline is many
-- times the geometry of a solid one. On a long path or inside a repeat, check `vector_stats`
-- rather than assuming; a dashed border around twenty rows adds up.
--
-- A STROKE SCALES WITH THE GROUP. `sw` is in the group's own units, so a `G s=[2,2]` draws a
-- `sw=1` line two units wide. That is usually what you want; when it is not, put the stroked
-- node outside the scaled group.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

local src = table.concat({
    -- Defaults for the whole scene go on the SCENE line and reach every node, like a group's
    -- `style`. Every label below inherits this size and colour.
    "SCENE w=200 h=200 fit=stretch size=5 f=#8FA6B8",
    "R x=0 y=0 w=200 h=200 f=#0B1622 fea=0",

    -- 1 CAPS: how a line ends. Same two points, three endings.
    'T x=4 y=3 w=192 h=6 text="cap: butt / round / square -- the dots mark the real ends"',
    "L p=[14,16,60,16] s=#5FD9A8 sw=7 cap=butt",
    "L p=[76,16,122,16] s=#5FD9A8 sw=7 cap=round",
    "L p=[138,16,184,16] s=#5FD9A8 sw=7 cap=square",
    --   the true endpoints, so you can see butt stop short and square overhang
    "C cx=14 cy=16 rx=1 ry=1 f=#F5D76E",
    "C cx=60 cy=16 rx=1 ry=1 f=#F5D76E",
    "C cx=184 cy=16 rx=1 ry=1 f=#F5D76E",

    -- 2 JOINS: how a corner turns. A sharp angle is where they differ.
    'T x=4 y=28 w=192 h=6 text="join: miter / round / bevel, on a sharp corner"',
    "L p=[20,56,34,38,48,56] s=#7FB2F0 sw=7 join=miter ml=10",
    "L p=[82,56,96,38,110,56] s=#7FB2F0 sw=7 join=round",
    "L p=[144,56,158,38,172,56] s=#7FB2F0 sw=7 join=bevel",

    -- 3 THE MITER LIMIT. The miter runs 1/sin(angle/2) times the stroke width, so a limit of
    -- 4 is only reached below about 29 degrees: this spike is 28, which just crosses it.
    'T x=4 y=64 w=192 h=6 text="ml: a 19-degree spike. left keeps its point, right is cut off"',
    "L p=[19,104,24,74,29,104] s=#C98BE0 sw=4 join=miter ml=10",
    "L p=[103,104,108,74,113,104] s=#C98BE0 sw=4 join=miter ml=4",

    -- 4 DASHES
    'T x=4 y=110 w=192 h=6 text="dash: [on,off], and a dotted line is round caps on a short dash"',
    "L p=[14,120,186,120] s=#EAF4F8 sw=2 dash=[8,4]",
    "L p=[14,128,186,128] s=#EAF4F8 sw=2 dash=[1,6] cap=round",
    --   `dofs` slides the pattern: over `t` it becomes a marching-ants outline
    'R x=14 y=136 w=80 h=18 rx=3 f=none s=#F5D76E sw=1.5 dash=[6,4] dofs="=t*10"',
    'T x=100 y=142 w=96 h=8 size=5 text="dofs = t*10: it marches"',

    -- 5 OPEN VERSUS CLOSED
    'T x=4 y=158 w=192 h=6 text="open SP, then SP close=1 (fills), then Y: the same 5 points"',
    "SP p=[14,168,34,162,50,178,34,194,14,188] seg=12 s=#5FD9A8 sw=2 f=none",
    "SP p=[70,168,90,162,106,178,90,194,70,188] seg=12 close=1 f=#2E8B6E s=#5FD9A8 sw=2",
    --   the same points as a polygon, for comparison: straight edges, same corners
    "Y p=[126,168,146,162,162,178,146,194,126,188] f=#24405A s=#7FB2F0 sw=2",
}, "\n")

ui:element({
    id = "lines_s", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = { scene = "lines", src = src },
})

ui:commit()
