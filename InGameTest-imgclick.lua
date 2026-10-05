-- An `IMG` with click=1 registers a hit area (0.11.93).
--
-- The `T` half of this was confirmed on a console on 2026-10-05. The `IMG` half has only ever
-- been measured offline, because no page had a clickable picture: a hit area is recorded where a
-- shape's outline is built, and a picture goes through neither that path nor the label one.
-- Measured offline, 0.11.93: an `IMG click=1` went from 0 hit regions to 1, and the picture's
-- own outline is used, corner radii included.
--
-- AIM AT each picture and click. The line at the bottom shows what was reported.
--
--   PASS   rows 1, 2 and 3 each report their own id; row 4 reports NOTHING
--   FAIL   a picture reports nothing, or row 4 reports something
--
-- Row 2 is the one a capture could never settle: a ROUNDED picture must answer inside its
-- corners and not outside them, so the corner is the place to aim.

local ui = ss.ui.surface("main")
ss.ui.activate("main")
ui:clear()

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

local PICTURE = "https://raw.githubusercontent.com/Gruffuss/scripted-screens-vector/09be911193cecfd8cc5a624c6bb55febad26f921/ScriptedScreensVector/About/thumb.png"

local data

local src = [==[
SCENE w=200 h=200 fit=stretch size=5 f=#EAF4F8
R x=0 y=0 w=200 h=200 f=#0B1622 fea=0
T x=6 y=4 w=188 h=7 size=6 text="a picture takes clicks now -- aim at each one"

# 1  a plain clickable picture
T x=6 y=18 w=90 h=6 size=5 f=#53646F text="1  IMG click=1"
IMG id=pic x=6 y=26 w=56 h=32 src="PICTURE" click=1

# 2  rounded, to prove the hit area is the picture's OWN outline and not its bounding box
T x=104 y=18 w=90 h=6 size=5 f=#53646F text="2  IMG click=1 rx=14"
IMG id=pic2 x=104 y=26 w=56 h=32 src="PICTURE" rx=14 click=1

# 3  inside a translated group, to prove the frame matrix reaches the box
T x=6 y=64 w=188 h=6 size=5 f=#53646F text="3  IMG click=1 inside a translated group"
G t=[24,0] {
  IMG id=pic3 x=6 y=72 w=56 h=32 src="PICTURE" click=1
}

# 4  the control: no click, must stay silent
T x=6 y=110 w=188 h=6 size=5 f=#53646F text="4  IMG with NO click -- must report nothing"
IMG id=pic4 x=6 y=118 w=56 h=32 src="PICTURE"

T x=6 y=158 w=188 h=6 f=#8FA6B8 text="last event:"
T x=6 y=168 w=188 h=20 size=9 f=#5FD9A8 text=$last wrap=1 missing="(nothing yet)"
T x=6 y=188 w=188 h=10 size=4 f=#53646F wrap=1 text="PASS = 1, 2 and 3 each report their own id and 4 reports nothing."
]==]

src = src:gsub("PICTURE", PICTURE)

ui:element({
    id = "ic_s", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = { scene = "ic", src = src },

    on_click = function(value)
        data:set_props({ scene = "ic", keep = 1, data = { last = value } })
        ui:commit()
    end,
})

data = ui:element({
    id = "ic_d", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
    props = { scene = "ic", keep = 1 },
})

ui:commit()
