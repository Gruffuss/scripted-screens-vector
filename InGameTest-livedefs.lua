-- 0.11.31 — values inside defs, and the hidden-group early-out.
--
-- Structure is sent ONCE. Everything that moves afterwards is a number in the data payload:
--   * the clip that cuts the top bar (a clipped track, the ordinary layout case)
--   * the axis of the middle ramp
--   * the stop position and the stop colour of the bottom ramp
--   * which of two skins is showing, by group opacity alone
--
-- What to look for on the console:
--   1. TOP  — the green bar grows and shrinks INSIDE the dark track, never past its ends,
--             and at its narrowest the bar disappears rather than filling the track.
--   2. MID  — the ramp's white end slides left and right. Past the end it stays white:
--             no second ramp, no bright edge.
--   3. LOW  — the ramp's stop walks across the box and its colour cycles amber to blue.
--   4. SKIN — the two labelled panels alternate (`v`, not opacity); exactly one at a time.
--   5. The magenta hatched border must NOT appear, and the chip log must stay clean.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 480, 480
if size then W, H = size.w, size.h end

ui:clear()

local src = [==[
SCENE w=200 h=200 fit=stretch
DEFS {
  CP id=track { R x=20 y=22 w=$barw h=16 rx=8 }
  GL id=axis units=bbox x1=0 y1=0 x2=$edge y2=0 stops=[[0,#0B1622],[1,#FFFFFF]]
  GL id=stop units=bbox x1=0 y1=0 x2=1 y2=0 stops=[[0,#0B1622],[$at,$hot]]
}
R x=0 y=0 w=200 h=200 f=#070D16

T x=20 y=8 w=160 h=10 text="clip follows the value" size=7 f=#5FD9A8
R x=20 y=22 w=160 h=16 rx=8 f=#12202F
G clip=track { R x=20 y=22 w=160 h=16 rx=8 f=#5FD9A8 }

T x=20 y=48 w=160 h=10 text="gradient axis follows the value" size=7 f=#5FD9A8
R x=20 y=62 w=160 h=30 rx=4 f=@axis fea=0

T x=20 y=100 w=160 h=10 text="stop position and colour" size=7 f=#5FD9A8
R x=20 y=114 w=160 h=30 rx=4 f=@stop fea=0

G v=$skin_a {
  R x=20 y=156 w=76 h=30 rx=4 f=#12202F s=#5FD9A8 sw=1
  T x=20 y=164 w=76 h=14 text="SKIN A" size=9 align=center f=#5FD9A8
}
G v=$skin_b {
  R x=104 y=156 w=76 h=30 rx=4 f=#2A1B12 s=#F59E0B sw=1
  T x=104 y=164 w=76 h=14 text="SKIN B" size=9 align=center f=#F59E0B
}
]==]

ui:element({
    id = "live_s", type = "vector",
    rect = { unit = "px", x = 6, y = 6, w = W - 12, h = H - 12 },
    props = { scene = "live", src = src },
})

local data = ui:element({
    id = "live_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "live", keep = 1, data = {
        barw = 80, edge = 1, at = 0.5, hot = "#F59E0B", skin_a = 1, skin_b = 0,
    } },
})

ui:commit()

local elapsed = 0

function tick(dt)
    elapsed = elapsed + (dt or 0.5)

    local wave = 0.5 + 0.5 * math.sin(elapsed * 0.6)
    local flip = math.floor(elapsed / 3) % 2

    data:set_props({ data = {
        -- 0 .. 160: at the bottom the track must go empty, not solid.
        barw = 160 * wave,
        -- The ramp axis ends inside the box for most of the cycle.
        edge = 0.25 + 0.75 * wave,
        at = 0.15 + 0.7 * wave,
        hot = flip == 0 and "#F59E0B" or "#3B82F6",
        skin_a = flip == 0 and 1 or 0,
        skin_b = flip == 0 and 0 or 1,
    } })

    ui:commit()
end
