-- 0.11.113: a text outline is as wide as `ow`, half outside the letters, as CSS
-- -webkit-text-stroke draws it. Up to 0.11.112 `ow = 8` at size 96 reached 7-8 units outside the
-- letters on LiberationSans and 6-7 on Barlow; CSS puts 4.
--
-- The viewbox matches a 460 x 460 surface, so a scene unit is a pixel in a capture of one.
-- Measure, do not eyeball: each outlined row's ink box should be 4 px larger on every side than
-- the plain row above it, which has the same text in the same place.
--
-- What you should see: in each outlined row, the white face of the letters still showing inside
-- a red band. Before 0.11.113 the band filled the stems and counters and no white was left.
-- The Barlow rows need the ScriptedScreens Fonts mod; without it they fall back and say so.
local ui = ss.ui.surface("main")
ss.ui.activate("main")
ui:clear()

local s = table.concat({
  "SCENE w=460 h=460 fit=stretch f=#EAF4F8",
  "R x=0 y=0 w=460 h=460 f=#0E1622 fea=0",
  'T x=20 y=0 w=440 h=110 size=96 text="PRBH"',
  'T x=20 y=110 w=440 h=110 size=96 ow=8 oc=#E04848 text="PRBH"',
  'T x=20 y=230 w=440 h=110 size=96 font=Barlow text="PRBH"',
  'T x=20 y=340 w=440 h=110 size=96 font=Barlow ow=8 oc=#E04848 text="PRBH"',
}, "\n")

ui:element({ id = "ow", type = "vector",
  rect = { unit = "px", x = 0, y = 0, w = 460, h = 460 },
  props = { scene = "outline", src = s } })
ui:commit()
