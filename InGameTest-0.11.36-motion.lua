-- 0.11.36 — motion and state without per-frame sends: since(), {=expr} text, hover/down, colour ease.
--
-- The chip sends only every two to three seconds; everything else moves on the client.
--   1. JUMP      the amber ball leaps in a smooth arc every three seconds and lands. One payload
--                per jump.
--   2. CLOCK     "uptime N s" counts up once a second. No payloads at all.
--   3. BUTTON    point at the green button: it brightens (hover). Hold it down: the label dims
--                (down). Move away: it returns. Nothing is sent.
--   4. COLOUR    the wide bar changes colour every two seconds, gliding over half a second
--                rather than jumping.
-- With Diagnostics on, this surface should show only a few payloads, not one a frame.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 480, 480
if size then W, H = size.w, size.h end

ui:clear()

local src = [==[
SCENE w=200 h=200 fit=stretch
R x=0 y=0 w=200 h=200 f=#070D16

T x=10 y=8 w=80 h=8 text="JUMP" size=6 f=#5FD9A8
R x=10 y=62 w=80 h=2 f=#2A3B4E
G t=["0","=-max(0, $jump*since($jump) - 45*since($jump)^2)"] {
  C cx=50 cy=56 rx=6 ry=6 f=#F59E0B
}

T x=110 y=8 w=80 h=8 text="CLOCK" size=6 f=#5FD9A8
T x=110 y=24 w=80 h=14 text="uptime {=floor(t):%d} s" size=9 f=#EAF4F8

T x=10 y=80 w=80 h=8 text="BUTTON (HOVER, HOLD)" size=6 f=#5FD9A8
G id=btn {
  R click=1 id=btn_box x=10 y=90 w=80 h=24 rx=4 f=#2E8B6E fo="=0.55+0.45*hover"
  T x=10 y=96 w=80 h=12 text=PRESS align=center size=8 f=#EAF4F8 fo="=1-0.6*down"
}

T x=10 y=130 w=180 h=8 text="COLOUR EASE" size=6 f=#5FD9A8
R x=10 y=140 w=180 h=30 rx=4 f=$state
]==]

local colours = { "#2E8B6E", "#B7791F", "#8AB4F8", "#E23D3D" }

ui:element({
    id = "motion_s", type = "vector",
    rect = { unit = "px", x = 6, y = 6, w = W - 12, h = H - 12 },
    props = { scene = "motion", src = src },
    on_click = function(value, player) end,
})

local data = ui:element({
    id = "motion_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "motion", keep = 1, data = { state = colours[1] } },
})

ui:commit()

local elapsed, nextJump, nextColour, k = 0, 1, 2, 1

function tick(dt)
    elapsed = elapsed + (dt or 0.5)

    if elapsed >= nextJump then
        nextJump = nextJump + 3
        data:set_props({ keep = 1, snap = 1, data = { jump = 24 } })
        ui:commit()
    end

    if elapsed >= nextColour then
        nextColour = nextColour + 2
        k = k % #colours + 1
        data:set_props({ keep = 1, data = { state = colours[k] }, ease = { state = { 0.5, "ease-in-out" } } })
        ui:commit()
    end
end
