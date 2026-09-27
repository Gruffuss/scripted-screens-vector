-- 0.11.39 — hard gradient stops, captures of a fade, hover without moving the mouse.
--
-- What to see and do:
--   1. STRIPES    the wide bar is striped, dark/light with hard edges, not a smooth ramp. The
--                 square beside it is a checker of the same two colours.
--   2. STEP       the small bar below is one hard step: left half teal, right half dark, with
--                 no gradient between them.
--   3. FADE       the disc pulses. CAPTURE the console while it is dim: the picture must show
--                 it dim too, not solid. Capture again a few seconds later and the pulse is at
--                 a different point (it does not restart).
--   4. HOVER      put the crosshair on card A: it lights and LAST reads "enter:cardA". Now TURN
--                 YOUR VIEW so the crosshair sits on card B, WITHOUT moving the mouse: A must
--                 go dark and B light, and the log must read "exit:cardA" then "enter:cardB".
--                 Turn away from the console entirely, still without moving the mouse: B goes
--                 dark and "exit:cardB" arrives.
--   The third card is styled by the client-side `hover` variable and sends nothing; it must
--   light and unlight on the same view turns.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 480, 480
if size then W, H = size.w, size.h end

ui:clear()

local src = [==[
SCENE w=200 h=200 fit=stretch
R x=0 y=0 w=200 h=200 f=#070D16

DEFS {
 GL id=stripes units=bbox x1=0 y1=0 x2=0.125 y2=0 spread=repeat stops=[[0,"#1E3247"],[0.4,"#1E3247"],[0.4,"#5FD9A8"],[1,"#5FD9A8"]]
 GL id=rows units=bbox x1=0 y1=0 x2=0 y2=0.125 spread=repeat stops=[[0,"#00000000"],[0.5,"#00000000"],[0.5,"#0B162288"],[1,"#0B162288"]]
 GL id=step units=bbox x1=0 y1=0 x2=1 y2=0 stops=[[0,"#5FD9A8"],[0.5,"#5FD9A8"],[0.5,"#12202F"],[1,"#12202F"]]
}

T x=6 y=4 w=120 h=7 text="1 STRIPES AND A CHECKER" size=5 f=#5FD9A8
R x=6 y=12 w=120 h=20 f=@stripes fea=0
R x=134 y=12 w=60 h=60 f=@stripes fea=0
R x=134 y=12 w=60 h=60 f=@rows fea=0

T x=6 y=38 w=120 h=7 text="2 ONE HARD STEP" size=5 f=#5FD9A8
R x=6 y=46 w=120 h=14 rx=3 f=@step fea=0

T x=6 y=68 w=120 h=7 text="3 PULSE: CAPTURE IT DIM" size=5 f=#5FD9A8
G o="=0.15+0.85*(0.5+0.5*sin(t*1.2))" { C cx=36 cy=94 rx=16 ry=16 f=#F5D76E }

T x=6 y=116 w=180 h=7 text="4 TURN THE VIEW ONTO EACH CARD" size=5 f=#5FD9A8
R id=cardA hoverev=1 x=6 y=124 w=56 h=22 rx=4 f=#12202F
G o=$onA { R x=6 y=124 w=56 h=22 rx=4 f=#2E8B6E }
T x=6 y=131 w=56 h=9 text="A" size=7 align=center f=#EAF4F8

R id=cardB hoverev=1 x=70 y=124 w=56 h=22 rx=4 f=#12202F
G o=$onB { R x=70 y=124 w=56 h=22 rx=4 f=#2E8B6E }
T x=70 y=131 w=56 h=9 text="B" size=7 align=center f=#EAF4F8

G id=cardC {
 R id=cardCbox click=1 x=134 y=124 w=60 h=22 rx=4 f=#12202F
 R x=134 y=124 w=60 h=22 rx=4 f=#5FA8FF fo="=hover"
 T x=134 y=131 w=60 h=9 text="C (HOVER)" size=6 align=center f=#EAF4F8
}

T x=6 y=156 w=188 h=7 text="LAST" size=5 f=#5FD9A8
T x=6 y=164 w=188 h=10 text=$last size=7 f=#EAF4F8 missing="(nothing yet)"
T x=6 y=178 w=188 h=18 text=$log size=5 f=#8FA6B8 wrap=1 missing=""
]==]

local log = {}
local data

ui:element({
    id = "t39_s", type = "vector",
    rect = { unit = "px", x = 6, y = 6, w = W - 12, h = H - 12 },
    props = { scene = "t39", src = src },
    on_click = function(value, player)
        table.insert(log, 1, value)
        if #log > 4 then table.remove(log) end

        local values = { last = value, log = table.concat(log, "  |  ") }
        if value == "enter:cardA" then values.onA = 1 elseif value == "exit:cardA" then values.onA = 0 end
        if value == "enter:cardB" then values.onB = 1 elseif value == "exit:cardB" then values.onB = 0 end

        data:set_props({ keep = 1, snap = 1, data = values })
        ui:commit()
    end,
})

data = ui:element({
    id = "t39_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "t39", keep = 1, data = { onA = 0, onB = 0 } },
})

ui:commit()
