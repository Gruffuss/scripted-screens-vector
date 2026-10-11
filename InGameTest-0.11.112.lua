-- 0.11.112: a label that fell back picks up a face that registers by ANOTHER route.
-- Scene "fallen" names font=Lobster and declares NO FONT link of its own, so nothing tells it
-- when Lobster arrives except the fonts mod's registration count. Scene "provider", a separate
-- element, declares the Lobster link. Needs the ScriptedScreens Fonts mod with its registration
-- count, and Lobster NOT already in its download cache.
--
-- What you should see, a few seconds after the push:
--   top    "A: Lobster via another page's link" in Lobster (a heavy script). It draws in the
--          game's font first, then switches with no re-push. Before 0.11.112 it stayed in the
--          game's font until the page was pushed again.
--   bottom "B: Lobster via its own link" in Lobster.
-- vector_stats: scene "fallen" has NO FONTS section and, once Lobster has arrived, no TEXT
-- warning; scene "provider" lists the link as loaded: Lobster.
local ui = ss.ui.surface("main")
ss.ui.activate("main")
local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end
ui:clear()

local fallen = table.concat({
  "SCENE w=240 h=80 fit=stretch size=13 f=#EAF4F8",
  "R x=0 y=0 w=240 h=80 f=#0B1622 fea=0",
  'T x=6 y=8 w=228 h=26 font=Lobster text="A: Lobster via another page\'s link"',
  'T x=6 y=52 w=228 h=12 size=6 text="no FONT def here: recovers by the registration count alone"',
}, "\n")

local provider = table.concat({
  "SCENE w=240 h=80 fit=stretch size=13 f=#EAF4F8",
  "DEFS {",
  '  FONT src="https://fonts.googleapis.com/css2?family=Lobster"',
  "}",
  "R x=0 y=0 w=240 h=80 f=#0B1622 fea=0",
  'T x=6 y=8 w=228 h=26 font=Lobster text="B: Lobster via its own link"',
}, "\n")

local half = math.floor((H - 12) / 2)
ui:element({ id = "fallen", type = "vector",
  rect = { unit = "px", x = 4, y = 4, w = W - 8, h = half },
  props = { scene = "fallen", src = fallen } })
ui:element({ id = "provider", type = "vector",
  rect = { unit = "px", x = 4, y = 8 + half, w = W - 8, h = half },
  props = { scene = "provider", src = provider } })
ui:commit()
