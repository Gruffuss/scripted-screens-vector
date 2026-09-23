-- 0.11.35 — picture tiling per axis, nine-slice edge tiling, gradients transparent past their ends.
--
-- What to look for on the console (the mod's own thumbnail throughout):
--   LEFT
--   1. REPEAT-X   one row of small copies across the top of its box, nothing below it.
--   2. AUTO H     copies 20 wide at the picture's own proportions (not squashed).
--   3. SPACE      whole copies only, first and last touching the box's sides, even gaps between.
--   4. ROUND      whole copies stretched slightly so exactly a whole number fills the width.
--   5. CONTAIN    one copy per box height, repeated across, none cut vertically.
--   RIGHT
--   6. SREP       three nine-slice frames: STRETCH (edges smeared long), REPEAT (edge detail
--                 repeated, centred, cut at both ends), ROUND (repeated, whole, no cuts).
--   7. NONE       a green band across the middle half of its box only, hard edges, and the
--                 dark box showing plainly on either side. With `pad` it would fill the box.
--
-- The magenta hatched border must NOT appear anywhere.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 480, 480
if size then W, H = size.w, size.h end

local PICTURE = "https://raw.githubusercontent.com/Gruffuss/scripted-screens-vector/09be911193cecfd8cc5a624c6bb55febad26f921/ScriptedScreensVector/About/thumb.png"

ui:clear()

local src = [==[
SCENE w=200 h=200 fit=stretch
DEFS {
  GL id=band x1=146 y1=0 x2=176 y2=0 spread=none stops=[[0,#5FD9A8],[1,#2E8B6E]]
}
R x=0 y=0 w=200 h=200 f=#070D16

T x=6 y=4 w=90 h=7 text="REPEAT-X" size=5 f=#5FD9A8
R x=6 y=12 w=90 h=24 f=#12202F
IMG x=6 y=12 w=90 h=24 src="PICTURE" tile=[16,9] at=[0,0] rep=["repeat","once"]

T x=6 y=40 w=90 h=7 text="AUTO H (20 WIDE)" size=5 f=#5FD9A8
IMG x=6 y=48 w=90 h=24 src="PICTURE" tile=[20,0] at=[0,0]

T x=6 y=76 w=90 h=7 text="SPACE" size=5 f=#5FD9A8
R x=6 y=84 w=90 h=16 f=#12202F
IMG x=6 y=84 w=90 h=16 src="PICTURE" tile=[26,14] rep=["space","once"] at=[0,0]

T x=6 y=104 w=90 h=7 text="ROUND" size=5 f=#5FD9A8
IMG x=6 y=112 w=90 h=16 src="PICTURE" tile=[26,0] rep=["round","once"] at=[0,0]

T x=6 y=132 w=90 h=7 text="CONTAIN" size=5 f=#5FD9A8
IMG x=6 y=140 w=90 h=20 src="PICTURE" tile="contain" rep=["repeat","once"] at=[0,0]

T x=104 y=4 w=90 h=7 text="SREP STRETCH | REPEAT | ROUND" size=5 f=#5FD9A8
IMG x=104 y=12 w=28 h=40 src="PICTURE" slice=[60,90,60,90] bw=[6,6,6,6]
IMG x=135 y=12 w=28 h=40 src="PICTURE" slice=[60,90,60,90] bw=[6,6,6,6] srep="repeat"
IMG x=166 y=12 w=28 h=40 src="PICTURE" slice=[60,90,60,90] bw=[6,6,6,6] srep="round"

T x=104 y=60 w=90 h=7 text="SPREAD NONE" size=5 f=#5FD9A8
R x=134 y=68 w=54 h=20 f=#12202F
R x=134 y=68 w=54 h=20 f=@band fea=0
]==]

src = src:gsub("PICTURE", PICTURE)

ui:element({
    id = "t35i_s", type = "vector",
    rect = { unit = "px", x = 6, y = 6, w = W - 12, h = H - 12 },
    props = { scene = "t35i", src = src },
})

ui:commit()
