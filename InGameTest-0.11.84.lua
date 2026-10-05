-- 0.11.84: a colour expression with ONE non-colour branch must be REPORTED and drawn magenta,
-- not drawn white in silence. And the two new recursion caps must refuse, not crash.
local ui = ss.ui.surface("main")
ss.ui.activate("main")
local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end
ui:clear()

local deepcall = "=" .. string.rep("abs(", 400) .. "1" .. string.rep(")", 400)
local deepindex = "=" .. string.rep("$a[", 400) .. "0" .. string.rep("]", 400)

local s = table.concat({
  "SCENE w=200 h=150 fit=stretch size=5 f=#EAF4F8",
  "R x=0 y=0 w=200 h=150 f=#0B1622 fea=0",
  'T x=4 y=3 w=192 h=7 text="1 good colour expr: GREEN"',
  'R x=8 y=12 w=60 h=16 f="=if(1,#5FD9A8,#2E8B6E)" fea=0',
  'T x=4 y=32 w=192 h=7 text="2 if with a number branch: MAGENTA + reported"',
  'R x=8 y=41 w=60 h=16 f="=if(1,#5FD9A8,5)" fea=0',
  'T x=4 y=61 w=192 h=7 text="3 mix with a number end: MAGENTA + reported"',
  'R x=8 y=70 w=60 h=16 f="=mix(#5FD9A8,1,0.5)" fea=0',
  'T x=4 y=90 w=192 h=7 text="4,5 deep call and deep index: no bar, game alive"',
  'R x=8 y=99 w="' .. deepcall .. '" h=12 f=#F5D76E fea=0',
  'R x=8 y=115 w="' .. deepindex .. '" h=12 f=#F5D76E fea=0',
  'T x=4 y=132 w=192 h=7 text="vector_stats: 4 problems, none fatal"',
}, "\n")

ui:element({ id = "mc", type = "vector",
  rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
  props = { scene = "mixedcolour", src = s } })
ui:commit()
