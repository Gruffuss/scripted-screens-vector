-- 0.11.35 — fading groups, animation under hidden groups, scroll-aware gradient defs.
--
-- What to look for on the console:
--   1. DOTS     three status dots blinking smoothly at different rates. They are faded by the
--               renderer, so with Diagnostics on this surface reads "idle" between data ticks
--               rather than rebuilding every frame.
--   2. HIDDEN   an amber pulse that is shown for three seconds and hidden for three. While it is
--               hidden the surface must go idle (Diagnostics: "its `t` is all under hidden
--               groups"); when it is shown it pulses again.
--   3. SIZE     the word "SIZE" swelling and shrinking. It is a surface of its own whose only
--               animation is its text size, and before this version it froze on its first frame.
--   4. LIST     scroll the list with the mouse wheel. The bottom 16 units of the list's window
--               fade out wherever you scroll to; before, the fade stayed at the top of the list
--               and scrolled away with it.
--
-- The magenta hatched border must NOT appear anywhere.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 480, 480
if size then W, H = size.w, size.h end

ui:clear()

local rows = {}
for i = 0, 19 do
    rows[#rows + 1] = string.format('T x=10 y=%d w=160 h=10 text="row %d" size=7 f=#EAF4F8', 104 + i * 14, i + 1)
end

local src = [==[
SCENE w=200 h=200 fit=stretch
DEFS {
  GL id=fade x1=0 y1==88+sy+vh-16 x2=0 y2==88+sy+vh stops=[[0,#FFFFFFFF],[1,#FFFFFF00]]
}
R x=0 y=0 w=200 h=200 f=#070D16

T x=8 y=6 w=80 h=8 text="DOTS" size=6 f=#5FD9A8
G o==0.5+0.5*sin(t*6) { C cx=100 cy=10 rx=4 ry=4 f=#5FD9A8 }
G o==0.5+0.5*sin(t*3) { C cx=115 cy=10 rx=4 ry=4 f=#F59E0B }
G o==step(0.5,mod(t,1)) { C cx=130 cy=10 rx=4 ry=4 f=#E23D3D }

T x=8 y=24 w=80 h=8 text="HIDDEN" size=6 f=#5FD9A8
G v=$show { R x=100 y=22 w==20+10*sin(t*4) h=10 rx=3 f=#F59E0B }

T x=8 y=78 w=80 h=8 text="LIST (SCROLL IT)" size=6 f=#5FD9A8
R x=8 y=88 w=184 h=100 rx=4 f=#12202F
SC id=list x=8 y=88 w=184 h=100 ch=300 {
  G mask=@fade {
ROWS
  }
}
]==]

src = src:gsub("ROWS", table.concat(rows, "\n"))

ui:element({
    id = "t35_s", type = "vector",
    rect = { unit = "px", x = 6, y = 6, w = W - 12, h = H - 12 },
    props = { scene = "t35", src = src },
})

-- Its own surface, so the main one above can go idle while the pulse is hidden.
ui:element({
    id = "t35_size", type = "vector",
    rect = { unit = "px", x = 6, y = 6 + math.floor((H - 12) * 40 / 200), w = math.floor((W - 12) / 2), h = math.floor((H - 12) * 16 / 200) },
    props = { scene = "t35size", src = 'SCENE w=100 h=16 fit=stretch\nT x=8 y=2 w=80 h=12 text="SIZE" size==7+2*sin(t*2) f=#EAF4F8' },
})

local data = ui:element({
    id = "t35_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "t35", keep = 1, data = { show = 1 } },
})

ui:commit()

local elapsed = 0
local shown = 1

function tick(dt)
    elapsed = elapsed + (dt or 0.5)
    local now = (math.floor(elapsed / 3) % 2 == 0) and 1 or 0
    if now ~= shown then
        shown = now
        data:set_props({ keep = 1, snap = 1, data = { show = shown } })
        ui:commit()
    end
end
