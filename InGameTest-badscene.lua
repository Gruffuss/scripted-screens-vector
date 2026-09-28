-- A scene with a deliberate mistake, to check that the console SAYS so (0.11.42).
--
-- The IMG line carries an unquoted data: URL, whose `=` and `;` end the attribute early. That
-- is a real mistake an author makes, and before 0.11.42 it drew a blank console with nothing
-- in `vector_stats` and only a log line to say why.
--
-- What to see:
--   * a MAGENTA HATCHED BORDER around the element, the same one any other scene problem draws.
--   * `vector_stats` naming the line: `expected an op ... line 4: IMG ...`.
--   * the other console (the good copy of this page) still draws its green box, so one broken
--     scene does not take the rest with it.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

ui:element({
    id = "bad_s", type = "vector",
    rect = { unit = "px", x = 6, y = 6, w = W - 12, h = math.floor(H / 2) - 12 },
    props = { scene = "bad", src = [==[
SCENE w=200 h=100 fit=stretch
R x=0 y=0 w=200 h=100 f=#070D16
T x=6 y=6 w=188 h=10 text="THIS ONE IS BROKEN ON PURPOSE" size=8 f=#EAF4F8
IMG x=6 y=30 w=40 h=40 src=data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7
]==] },
})

ui:element({
    id = "good_s", type = "vector",
    rect = { unit = "px", x = 6, y = math.floor(H / 2) + 6, w = W - 12, h = math.floor(H / 2) - 12 },
    props = { scene = "good", src = [==[
SCENE w=200 h=100 fit=stretch
R x=0 y=0 w=200 h=100 f=#0B1622
R x=20 y=20 w=160 h=60 rx=8 f=#2E8B6E
T x=20 y=42 w=160 h=14 text="THIS ONE IS FINE" size=10 align=center f=#EAF4F8
]==] },
})

ui:commit()
