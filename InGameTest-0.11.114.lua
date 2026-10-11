-- 0.11.114: dashed strokes always finish, and walk only the stretch that can reach the screen.
-- Up to 0.11.113 a dashed line 2e9 units long never finished building: the console stopped
-- redrawing and a core spun until the game closed.
--
-- The vector element is 200 x 200 in the middle of a larger surface, so what overflows it shows.
-- What you should see:
--   1 a dashed white line across the WHOLE screen, past both sides of the dark element. It is
--     2e9 units long; only the part near the screen is ever walked, and none of it may be cut
--     short at the element's edge, since vector geometry outside its element is not masked.
--   2 two yellow lines, both pattern [10] (10 on, 10 off). With dofs=0 the upper starts with a
--     dash; with dofs=10 the lower starts with a GAP. Up to 0.11.113 both started with a dash.
--   3 a dashed rectangle around the element, drawn as before.
-- vector_stats lists scene "dashes" with shapes emitted and no problems; a hang would leave it at 0.
local ui = ss.ui.surface("main")
ss.ui.activate("main")
local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end
ui:clear()

local s = table.concat({
  "SCENE w=200 h=200 fit=stretch",
  "R x=0 y=0 w=200 h=200 f=#0E1622 fea=0",
  "L p=[-1000000000,100,1000000000,100] s=#EAF4F8 sw=4 dash=[11,11]",
  "L p=[0,40,200,40] s=#F2C14E sw=6 dash=[10]",
  "L p=[0,60,200,60] s=#F2C14E sw=6 dash=[10] dofs=10",
  "R x=4 y=150 w=192 h=40 f=none s=#8FB8D8 sw=2 dash=[8,4]",
}, "\n")

ui:element({ id = "dashes", type = "vector",
  rect = { unit = "px", x = math.floor((W - 200) / 2), y = math.floor((H - 200) / 2), w = 200, h = 200 },
  props = { scene = "dashes", src = s } })
ui:commit()
