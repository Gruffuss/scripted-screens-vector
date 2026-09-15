-- Justified text check: the same paragraph left-aligned (top) and justified (middle), and
-- justified with a first-line style (bottom). Each sits in an outlined box exactly its text box,
-- so a justified full line should touch the box's right edge and a left-aligned one should not.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

ui:element({
    id = "bg", type = "panel",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    style = { bg = "#0A121C" },
})

ui:element({
    id = "just_s", type = "vector",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = {
        scene = "just",
        src = [==[
SCENE w=200 h=200 fit=contain

R x=10 y=6 w=180 h=58 f=none s=#E23D3D sw=0.4 fea=0
T x=10 y=6 w=180 h=58 wrap=1 align=left size=9 f=#EAF4F8 text="LEFT: the quick brown fox jumps over the lazy dog while a very long word like instrumentation arrives late."

R x=10 y=70 w=180 h=58 f=none s=#5FD9A8 sw=0.4 fea=0
T x=10 y=70 w=180 h=58 wrap=1 align=justified size=9 f=#EAF4F8 text="JUSTIFIED: the quick brown fox jumps over the lazy dog while a very long word like instrumentation arrives late."

R x=10 y=134 w=180 h=60 f=none s=#F59E0B sw=0.4 fea=0
T x=10 y=134 w=180 h=60 wrap=1 align=justified size=9 f=#EAF4F8 fl="size=11 weight=bold f=#F59E0B" text="WITH FL: the quick brown fox jumps over the lazy dog while a very long word like instrumentation arrives late."
]==],
    },
})
