-- 0.11.118: gradients blend the way a browser does, and very dark colours keep their value.
-- Row 1: #5FD9A8 -> #2E8B6E. Its middle must read about (70,178,139), a browser's value;
--        0.11.117 drew (75,183,142), lighter.
-- Row 2: the same red -> blue ramp on a solid stroke (top) and a dashed one (bottom). At the
--        same x they must match; at 45% across, blue reads about 115 on both (0.11.117: 178 solid).
-- Row 3: #050505, #0A0A0A and #141414 on black. All three must be visible and read 5, 10 and 20;
--        0.11.117 drew the first two as pure black.
local ui = ss.ui.surface("main")
ss.ui.activate("main")
local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end
ui:clear()

local s = table.concat({
  "SCENE w=400 h=300 fit=stretch size=10 f=#EAF4F8",
  "DEFS {",
  "  GL id=teal units=bbox x1=0 y1=0 x2=1 y2=0 stops=[[0,#5FD9A8],[1,#2E8B6E]]",
  "  GL id=rb units=bbox x1=0 y1=0 x2=1 y2=0 stops=[[0,#FF0000],[1,#0000FF]]",
  "}",
  "R x=0 y=0 w=400 h=300 f=#000000 fea=0",
  "R x=0 y=10 w=400 h=60 f=@teal fea=0",
  "L p=[0,100,400,100] s=@rb sw=20 cap=butt",
  "L p=[0,140,400,140] s=@rb sw=20 cap=butt dash=[10,10]",
  "R x=20 y=180 w=80 h=80 f=#050505 fea=0",
  "R x=120 y=180 w=80 h=80 f=#0A0A0A fea=0",
  "R x=220 y=180 w=80 h=80 f=#141414 fea=0",
  'T x=310 y=180 w=90 h=12 text="5 / 10 / 20"',
  'T x=0 y=270 w=400 h=14 text="browser midpoint 70,178,139; dashed = solid"',
}, "\n")

ui:element({ id = "srgb", type = "vector",
  rect = { unit = "px", x = 0, y = 0, w = W, h = H },
  props = { scene = "srgb", src = s } })
ui:commit()
