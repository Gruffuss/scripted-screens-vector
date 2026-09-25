-- 0.11.38 — rotation direction, corner radii, a held press when the view turns, captures.
--
-- What to see and do:
--   1. SPIN      the amber hand turns CLOCKWISE (r = t*90), like a clock hand.
--   2. MATCH     the two short white bars point the same way, straight DOWN: the left one is
--                `r = 90`, the right one CSS rotate(90deg) as `m`.
--   3. PILL      the wide, short bar has fully round ends and a clean outline, though it asks
--                for rx = 20 on a box 8 high.
--   4. HOLD      press and hold the green button, then turn your view away without moving the
--                mouse: LAST reads "leave:hold" while you still hold. Release: "up:hold".
--   5. CAPTURE   capture this console twice, a few seconds apart, from across the room: the
--                clock hand is at a different angle in each (a capture no longer restarts it),
--                and edges are as sharp as a close-up capture.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 480, 480
if size then W, H = size.w, size.h end

ui:clear()

local src = [==[
SCENE w=200 h=200 fit=stretch
R x=0 y=0 w=200 h=200 f=#070D16

T x=6 y=4 w=90 h=7 text="1 SPIN: CLOCKWISE" size=5 f=#5FD9A8
C cx=40 cy=45 rx=28 ry=28 f=#12202F
G t=[40,45] r="=t*90" { R x=-1.5 y=-25 w=3 h=25 rx=1.5 f=#F5D76E }
C cx=40 cy=45 rx=3 ry=3 f=#F5D76E

T x=104 y=4 w=90 h=7 text="2 BOTH POINT DOWN" size=5 f=#5FD9A8
G r=90 a=[125,25] { R x=125 y=23 w=20 h=4 f=#FFFFFF fea=0 }
G m=[0,1,-1,0,0,0] a=[165,25] { R x=165 y=23 w=20 h=4 f=#FFFFFF fea=0 }

T x=104 y=60 w=90 h=7 text="3 PILL, CLEAN OUTLINE" size=5 f=#5FD9A8
R x=104 y=70 w=90 h=8 rx=20 f=#2E8B6E s=#EAF4F8 sw=0.8

T x=6 y=90 w=120 h=7 text="4 HOLD, THEN TURN AWAY" size=5 f=#5FD9A8
R id=hold press=1 x=6 y=98 w=90 h=28 rx=4 f=#2E8B6E
T x=6 y=106 w=90 h=12 text="HOLD" size=8 align=center f=#EAF4F8

T x=6 y=140 w=180 h=7 text="LAST" size=5 f=#5FD9A8
T x=6 y=148 w=188 h=10 text=$last size=7 f=#EAF4F8 missing="(nothing yet)"
T x=6 y=162 w=188 h=34 text=$log size=5 f=#8FA6B8 wrap=1 missing=""
]==]

local log = {}
local data

ui:element({
    id = "t38_s", type = "vector",
    rect = { unit = "px", x = 6, y = 6, w = W - 12, h = H - 12 },
    props = { scene = "t38", src = src },
    on_click = function(value, player)
        table.insert(log, 1, value)
        if #log > 6 then table.remove(log) end
        data:set_props({ keep = 1, snap = 1, data = { last = value, log = table.concat(log, "  |  ") } })
        ui:commit()
    end,
})

data = ui:element({
    id = "t38_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "t38", keep = 1, data = {} },
})

ui:commit()
