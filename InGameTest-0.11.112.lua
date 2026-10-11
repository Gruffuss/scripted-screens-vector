-- 0.11.112: a label that fell back picks up a face that registers by ANOTHER route.
-- Scene "fallen" names Bangers and declares NO FONT link of its own, so nothing tells it when
-- Bangers arrives except the fonts mod's registration count. Scene "provider", a separate
-- element, declares the Bangers link. Needs the ScriptedScreens Fonts mod with its registration
-- count, and the family NOT already in its download cache (a cached face loads at startup and
-- never arrives late: pick another Google family if this one has been fetched before).
--
-- PUSH WHILE LOOKING AT THE CONSOLE. A label that has never been laid out is re-applied on any
-- rebuild and would recover for the wrong reason; one already drawn is not.
--
-- What you should see within a second or two of the push, with no re-push:
--   A1 "font= Bangers" switches from the game's font to Bangers (a comic display face).
--   A2 the inline-tag line: first the tag itself as literal text, then Bangers with the tag gone.
--      Only the forced re-layout fixes this one, and in this scene only the registration count
--      can trigger it. Before 0.11.112 both A rows stayed in the game's font.
--   B  "own link" in Bangers.
-- vector_stats: scene "fallen" has NO FONTS section and, once Bangers has arrived, no TEXT
-- warning; scene "provider" lists the link as loaded: Bangers.
local ui = ss.ui.surface("main")
ss.ui.activate("main")
local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end
ui:clear()

local fallen = table.concat({
  "SCENE w=240 h=80 fit=stretch size=13 f=#EAF4F8",
  "R x=0 y=0 w=240 h=80 f=#0B1622 fea=0",
  'T x=6 y=4 w=228 h=24 font=Bangers text="A1 font= Bangers"',
  'T x=6 y=32 w=228 h=24 text="A2 <font=Bangers>inline Bangers</font>"',
  'T x=6 y=64 w=228 h=12 size=6 text="no FONT def here: recovers by the registration count alone"',
}, "\n")

local provider = table.concat({
  "SCENE w=240 h=80 fit=stretch size=13 f=#EAF4F8",
  "DEFS {",
  '  FONT src="https://fonts.googleapis.com/css2?family=Bangers"',
  "}",
  "R x=0 y=0 w=240 h=80 f=#0B1622 fea=0",
  'T x=6 y=8 w=228 h=26 font=Bangers text="B own link"',
}, "\n")

local half = math.floor((H - 12) / 2)
ui:element({ id = "fallen", type = "vector",
  rect = { unit = "px", x = 4, y = 4, w = W - 8, h = half },
  props = { scene = "fallen", src = fallen } })
ui:element({ id = "provider", type = "vector",
  rect = { unit = "px", x = 4, y = 8 + half, w = W - 8, h = half },
  props = { scene = "provider", src = provider } })
ui:commit()
