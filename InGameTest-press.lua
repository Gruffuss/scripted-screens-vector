-- 0.11.36 — press, release and leave on a `press = 1` node.
--
-- What to do and what to see:
--   1. HOLD      press and hold the green button: the lamp beside it lights and LAST reads
--                "down:hold". Release on the button: the lamp goes out, LAST reads "hold"
--                (the click, after "up:hold").
--   2. DRAG OFF  press the button, drag off it, then release anywhere: LAST shows "leave:hold"
--                while you are still holding, the lamp goes out on release ("up:hold"), and no
--                click follows.
--   3. TAP       the amber button is a plain `click = 1`: it only ever shows "tap", never
--                down/up/leave.
--   4. ROWS      the three rows are one `press = 1` node in a repeat: holding row 2 shows
--                "down:row:1" (indices count from 0).
-- The event log keeps the last five values, newest first.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 480, 480
if size then W, H = size.w, size.h end

ui:clear()

local src = [==[
SCENE w=200 h=200 fit=stretch
R x=0 y=0 w=200 h=200 f=#070D16

T x=10 y=8 w=120 h=8 text="HOLD (PRESS=1)" size=6 f=#5FD9A8
R id=hold press=1 x=10 y=18 w=80 h=24 rx=4 f=#2E8B6E
T x=10 y=24 w=80 h=12 text="HOLD" size=8 align=center f=#EAF4F8
C cx=110 cy=30 rx=7 ry=7 f=#1B2A3A
G o=$held { C cx=110 cy=30 rx=7 ry=7 f=#F5D76E }

T x=130 y=8 w=60 h=8 text="TAP (CLICK=1)" size=6 f=#5FD9A8
R id=tap click=1 x=130 y=18 w=60 h=24 rx=4 f=#B7791F
T x=130 y=24 w=60 h=12 text="TAP" size=8 align=center f=#EAF4F8

T x=10 y=52 w=120 h=8 text="ROWS (PRESS=1 IN A REPEAT)" size=6 f=#5FD9A8
RP n=3 { R id=row press=1 x=10 y==62+i*16 w=180 h=14 rx=3 f=#12202F }
RP n=3 { T x=16 y==64+i*16 w=160 h=10 text="row" size=6 f=#8FA6B8 }

T x=10 y=120 w=180 h=8 text="LAST" size=6 f=#5FD9A8
T x=10 y=130 w=180 h=12 text=$last size=9 f=#EAF4F8 missing="(nothing yet)"
T x=10 y=148 w=180 h=48 text=$log size=6 f=#8FA6B8 wrap=1 missing=""
]==]

local log = {}

local data

ui:element({
    id = "press_s", type = "vector",
    rect = { unit = "px", x = 6, y = 6, w = W - 12, h = H - 12 },
    props = { scene = "press", src = src },
    on_click = function(value, player)
        table.insert(log, 1, value)
        if #log > 5 then table.remove(log) end

        local held
        if value == "down:hold" then held = 1 elseif value == "up:hold" or value == "leave:hold" then held = 0 end

        local values = { last = value, log = table.concat(log, "  |  ") }
        if held ~= nil then values.held = held end
        data:set_props({ keep = 1, snap = 1, data = values })
        ui:commit()
    end,
})

data = ui:element({
    id = "press_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "press", keep = 1, data = { held = 0 } },
})

ui:commit()
