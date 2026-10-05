-- 0.11.97: the src reader and the text formatter stop failing silently.
--
-- THREE vector elements, each with its own scene, because two of the four faults REJECT the scene
-- outright and so cannot share a page with anything that has to draw.
--
-- WHAT SHOULD BE SEEN, read in `vector_stats`:
--
--   scene "brace1"  -- its src ends with an unclosed `{`
--     nothing drawn, and the problem:
--       src: a { block was never closed, line 3: G t=[6,6] {
--     BEFORE 0.11.97 this parsed into a complete, plausible tree and drew the rect, silently.
--
--   scene "brace2"  -- its src carries a stray `}` with nothing open
--     nothing drawn, and the problem:
--       src: a } closes a block that was never opened, line 4: }
--     BEFORE 0.11.97 this ENDED the scene at the `}` and silently threw away every node after it,
--     so the second rect simply never existed and nothing said so.
--
--   scene "fmt"  -- draws, and reports two unreadable specs
--     drawn: row 1 "%q", row 2 nothing, row 3 "254.6", row 4 "254.6"
--     problems, exactly two:
--       T: fmt "%q" is not a printf conversion, so it prints instead of the number
--       T: fmt "" is not a printf conversion, so it prints instead of the number
--     Rows 3 and 4 are the CONTROLS and must stay silent: `%.1f` is a readable printf spec, and
--     `{0:F1}` is a .NET composite format, which also comes back unchanged from the printf
--     translator and formats correctly -- that is the false positive this check must not raise.
--     Row 5 is C19: both `{$n:%.2f]}` and `{$arr[0]:%.2f]}` must draw "254.60]". Before 0.11.97
--     the first drew "--" and the second drew "254.6" with the format silently DROPPED, plus a
--     spurious `trailing input at ']'` problem about text the author never wrote.
--
-- PASS   three scenes, two rejected by name, two fmt problems, row 5 reading 254.60] twice
-- FAIL   a brace scene draws, a control reports, or row 5 shows "--" or "254.6"

local ui = ss.ui.surface("main")
ss.ui.activate("main")
ui:clear()

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

-- 1  an unclosed { : the block is never closed before the text ends
ui:element({
    id = "b1", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = 100 },
    props = { scene = "brace1", src = table.concat({
        "SCENE w=100 h=40 fit=stretch",
        "R x=0 y=0 w=100 h=40 f=#0B1622 fea=0",
        "G t=[6,6] {",
        "  R x=0 y=0 w=30 h=12 f=#5FD9A8 fea=0",
    }, "\n") },
})

-- 2  a stray } with nothing open, and a node after it that used to vanish
ui:element({
    id = "b2", type = "vector",
    rect = { unit = "px", x = 4, y = 110, w = W - 8, h = 100 },
    props = { scene = "brace2", src = table.concat({
        "SCENE w=100 h=40 fit=stretch",
        "R x=0 y=0 w=100 h=40 f=#0B1622 fea=0",
        "R x=6 y=6 w=30 h=12 f=#5FD9A8 fea=0",
        "}",
        "R x=46 y=6 w=30 h=12 f=#E23D3D fea=0",
    }, "\n") },
})

-- 3  the formatter: two faults and three controls, all in one drawing scene
ui:element({
    id = "f1", type = "vector",
    rect = { unit = "px", x = 4, y = 216, w = W - 8, h = 236 },
    props = { scene = "fmt", src = table.concat({
        "SCENE w=200 h=100 fit=stretch size=5 f=#EAF4F8",
        "R x=0 y=0 w=200 h=100 f=#0B1622 fea=0",
        'T x=4 y=3 w=190 h=7 size=6 text="0.11.97 -- fmt and the ] in a spec"',

        'T x=4 y=14 w=60 h=6 size=5 f=#53646F text="1 fmt=%q (reports)"',
        'T x=70 y=13 w=120 h=9 size=8 text=$n fmt="%q"',

        'T x=4 y=26 w=60 h=6 size=5 f=#53646F text="2 fmt= empty (reports)"',
        'T x=70 y=25 w=120 h=9 size=8 text=$n fmt=""',

        'T x=4 y=38 w=60 h=6 size=5 f=#53646F text="3 fmt=%.1f  CONTROL"',
        'T x=70 y=37 w=120 h=9 size=8 text=$n fmt="%.1f"',

        'T x=4 y=50 w=60 h=6 size=5 f=#53646F text="4 fmt={0:F1} CONTROL"',
        'T x=70 y=49 w=120 h=9 size=8 text=$n fmt="{0:F1}"',

        'T x=4 y=62 w=60 h=6 size=5 f=#53646F text="5 a ] inside the spec"',
        'T x=70 y=61 w=120 h=9 size=8 text="{$n:%.2f]} {$arr[0]:%.2f]}"',

        'T x=4 y=80 w=190 h=14 size=4 f=#53646F wrap=1 text="row 5 must read 254.60] twice. Before 0.11.97 it read -- and 254.6, the format silently dropped."',
    }, "\n") },
})

-- The DATA element must be its own element: an element holding `data` is the data element
-- whatever else it carries, so putting the payload on the element above would make the scene's
-- `src` be ignored and nothing would draw at all.
ui:element({
    id = "f1d", type = "vector",
    rect = { unit = "px", x = 4, y = 216, w = 1, h = 1 },
    props = { scene = "fmt", keep = 1, data = { n = 254.6, arr = { 254.6 } } },
})

ui:commit()
