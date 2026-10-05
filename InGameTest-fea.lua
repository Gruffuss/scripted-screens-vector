-- Does `fea` reach the stroke of a shape with no fill? (0.11.91)
--
-- Before 0.11.91 every read of `fea` sat inside a fill branch of the parser, and that branch
-- returns as soon as `f` is missing or `none` -- so a stroke-only shape kept the AUTOMATIC
-- feather whatever it asked for, and `fea=0` and `fea=8` drew the same stroke.
--
--   PASS   row 1 is crisp, row 2 is visibly soft, and the filled controls below match them
--   FAIL   rows 1 and 2 look identical
--
-- Measured offline on this exact shape: 28 verts both ways before, 12 vs 28 after.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

ui:element({
    id = "fea_s", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = { scene = "fea", src = table.concat({
        "SCENE w=200 h=200 fit=stretch size=5 f=#EAF4F8",
        "R x=0 y=0 w=200 h=200 f=#12202F fea=0",
        'T x=8 y=4 w=184 h=7 size=6 text="stroke-only: fea must change the edge"',

        'T x=8 y=16 w=90 h=6 f=#8FA6B8 text="1  f=none fea=0  (crisp)"',
        "R x=8 y=26 w=70 h=60 s=#FFFFFF sw=3 f=none fea=0",

        'T x=104 y=16 w=90 h=6 f=#8FA6B8 text="2  f=none fea=8  (soft)"',
        "R x=104 y=26 w=70 h=60 s=#FFFFFF sw=3 f=none fea=8",

        'T x=8 y=98 w=184 h=6 f=#8FA6B8 text="controls: the same strokes on a FILLED box"',
        "R x=8 y=108 w=70 h=60 s=#FFFFFF sw=3 f=#24405A fea=0",
        "R x=104 y=108 w=70 h=60 s=#FFFFFF sw=3 f=#24405A fea=8",

        'T x=8 y=178 w=184 h=14 size=5 f=#53646F wrap=1 text="FAIL looks like: 1 and 2 identical. The controls always differed."',
    }, "\n") },
})

ui:commit()
