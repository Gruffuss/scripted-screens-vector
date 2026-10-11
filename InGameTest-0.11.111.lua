-- 0.11.111: page webfonts. A FONT def asks the ScriptedScreens Fonts mod for a Google Fonts
-- link, and every label using it must switch to the new face when it arrives WITHOUT a re-push.
-- Needs the ScriptedScreens Fonts mod, and Pacifico NOT already in its download cache, so the
-- face arrives after the page has drawn.
--
-- What you should see, a few seconds after the push:
--   1 "Pacifico arrived" in a brush script. For the first moment it is in the game's font.
--   2 the inline-tag line also in brush script. Before the font arrives TMP draws the tag
--     itself as literal text; after, the tag is gone.
--   3 "Barlow control" in Barlow, an installed face: NOT reported as a problem.
--   4 "wrong face name" stays in the game's font.
-- vector_stats, FONTS section:
--   the Pacifico link: loaded: Pacifico
--   the made-up family: failed: download-failed
--   the example.com link: refused (host not allowed)
--   font "Pacifico Bold" did not arrive; this page's fonts delivered: Pacifico
-- and no "Barlow" line anywhere.
local ui = ss.ui.surface("main")
ss.ui.activate("main")
local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end
ui:clear()

local s = table.concat({
  "SCENE w=240 h=160 fit=stretch size=11 f=#EAF4F8",
  "DEFS {",
  '  FONT src="https://fonts.googleapis.com/css2?family=Pacifico"',
  '  FONT src="https://fonts.googleapis.com/css2?family=NoSuchFamilyZq"',
  '  FONT src="https://example.com/font.ttf"',
  "}",
  "R x=0 y=0 w=240 h=160 f=#0B1622 fea=0",
  'T x=6 y=6 w=228 h=22 font=Pacifico text="1 Pacifico arrived"',
  'T x=6 y=34 w=228 h=22 text="2 <font=Pacifico>inline Pacifico</font>"',
  'T x=6 y=62 w=228 h=22 font=Barlow text="3 Barlow control"',
  'T x=6 y=90 w=228 h=22 font="Pacifico Bold" text="4 wrong face name"',
  'T x=6 y=124 w=228 h=12 size=6 text="vector_stats: FONTS section, see the page header"',
}, "\n")

ui:element({ id = "wf", type = "vector",
  rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
  props = { scene = "webfont", src = s } })
ui:commit()
