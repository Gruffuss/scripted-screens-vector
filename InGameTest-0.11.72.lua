-- Everything fixed between 0.11.69 and 0.11.72, on one page.
--
-- Read the answer from `vector_stats` as well as the screen: three of these are about a fault
-- being REPORTED, and a page that merely looks right would hide that.
--
-- 1 A COLOUR THAT IS NOT A COLOUR  (0.11.69)
--   `f=notacolour` used to leave the shape unfilled and report nothing -- a missing shape with
--   no entry anywhere, which is the hardest kind of mistake to find. It must now draw MAGENTA
--   and appear in vector_stats.
--
-- 2 gray AND WHITESPACE  (0.11.70)
--   Unity's table holds only the British `grey`, and its parser does not trim. Both are now
--   forgiven, everywhere rather than only in `data`: all four swatches must be plain grey.
--   A bare `FFFFFF` with no hash must still be magenta -- that one is genuinely not a colour.
--
-- 3 PER-CORNER RADII  (0.11.70)
--   `rx=[0,24,24,24]` used to draw a plain rectangle, because the square-shape test read only
--   the top-left corner. The box must show ONE sharp corner (top-left) and three round ones.
--
-- 4 A MALFORMED PATH  (0.11.72 and 0.11.70)
--   `d="M10 10 L50 10 Z 5"` hung the renderer: a number after `Z` re-entered the close command,
--   which reads no input. It must now draw the part before the fault and report where it
--   stopped. If this page fails to appear at all, the guard did not hold and the worker is
--   wedged -- that is the one outcome worth stopping for.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

local s = {
    "SCENE w=200 h=200 fit=stretch",
    "R x=0 y=0 w=200 h=200 f=#0B1622 fea=0",

    -- 1 an unparseable colour must be loud
    'T x=4 y=4 w=192 h=6 text="1 f=notacolour must be MAGENTA and reported" size=5 f=#EAF4F8',
    "R x=8 y=12 w=40 h=14 f=notacolour fea=0",

    -- 2 gray, grey, and whitespace around a hex value, in scene text
    'T x=4 y=32 w=192 h=6 text="2 all four GREY; the fifth magenta" size=5 f=#EAF4F8',
    "R x=8 y=40 w=30 h=14 f=grey fea=0",
    "R x=42 y=40 w=30 h=14 f=gray fea=0",
    'R x=76 y=40 w=30 h=14 f=" #808080 " fea=0',
    'R x=110 y=40 w=30 h=14 f=" GRAY " fea=0',
    "R x=144 y=40 w=30 h=14 f=FFFFFF fea=0",

    -- 3 one sharp corner, three round
    'T x=4 y=60 w=192 h=6 text="3 rx=[0,24,24,24]: top-left sharp, three round" size=5 f=#EAF4F8',
    "R x=8 y=68 w=80 h=50 rx=[0,24,24,24] f=#5FD9A8 fea=0",

    -- 4 a path that used to hang
    'T x=4 y=124 w=192 h=6 text="4 malformed path: draws, then reports" size=5 f=#EAF4F8',
    'P d="M10 140 L60 140 L60 170 Z 5" f=#F5D76E fea=0',
}

ui:element({
    id = "fixes", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = { scene = "fixes", src = table.concat(s, "\n") },
})
