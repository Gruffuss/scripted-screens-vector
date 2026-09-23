-- 0.11.34 — multi-value labels, empty strings, outlines, picture placement, tiling,
-- nine-slice, pixelated pictures, and repeating gradients.
--
-- What to look for on the console, column by column:
--
-- LEFT (text)
--   1. TEMPLATE   "P 101.3 kPa  T 23C" — both numbers change every tick, each keeping its own
--                 decimals (one after the point for P, none for T).
--   2. EMPTY      "HELLO" for two seconds, then NOTHING for two seconds, repeating. Before the
--                 fix the word never went away.
--   3. KIND       "42" and "OFF" alternating every two seconds. Before the fix it stuck on "OFF".
--   4. OUTLINE    "STROKE" in white with a thin red outline round every letter. If the face has
--                 no outline in its shader, the surface's TEXT warnings say so instead.
--
-- MIDDLE (pictures; the mod's own thumbnail)
--   5. OFF        the picture filling its box but pushed 10 right: a clean dark strip on the
--                 LEFT, the picture's right end cut off, and NO smeared pixels in the strip.
--   6. NONE | S-D a small crop of the picture: left at its natural size in the middle of its
--                 box, right shrunk to fit its smaller box.
--   7. TILE       a grid of little copies, the edge ones cut by the rounded box.
--   8. SLICE      the picture as a frame: sharp corners, stretched edges. Right: no middle.
--   9. SMOOTH | POINT  the same tiny crop blown up: left blurred, right in square blocks.
--
-- RIGHT (gradients)
--  10. REPEAT     black-to-green sawtooth stripes with HARD edges where green meets black.
--  11. REFLECT    the same ramp running back and forth: soft zigzag, no hard edges.
--  12. RADIAL     concentric rings, repeating out to the box's corners.
--  13. MASK       an amber bar faded by a repeating mask: soft stripes of transparency.
--
-- The magenta hatched border must NOT appear anywhere.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 480, 480
if size then W, H = size.w, size.h end

local PICTURE = "https://raw.githubusercontent.com/Gruffuss/scripted-screens-vector/09be911193cecfd8cc5a624c6bb55febad26f921/ScriptedScreensVector/About/thumb.png"

ui:clear()

local src = [==[
SCENE w=200 h=200 fit=stretch
DEFS {
  GL id=saw x1=0 y1=0 x2=10 y2=0 spread=repeat stops=[[0,#000000],[1,#5FD9A8]]
  GL id=zig x1=0 y1=0 x2=10 y2=0 spread=reflect stops=[[0,#000000],[1,#5FD9A8]]
  GR id=rings cx=165 cy=110 r=6 spread=repeat stops=[[0,#0B1622],[1,#8AB4F8]]
  GL id=fade x1=0 y1=0 x2=8 y2=0 spread=reflect stops=[[0,#00000000],[1,#000000FF]]
}
R x=0 y=0 w=200 h=200 f=#070D16

T x=6 y=4 w=58 h=7 text="TEMPLATE" size=5 f=#5FD9A8
T x=6 y=12 w=60 h=9 text="P {$p:%.1f} kPa  T {$t:%.0f}C" size=6 f=#EAF4F8

T x=6 y=28 w=58 h=7 text="EMPTY CLEARS" size=5 f=#5FD9A8
R x=6 y=36 w=58 h=10 rx=2 f=#12202F
T x=8 y=37 w=54 h=9 text=$note size=6 f=#EAF4F8

T x=6 y=52 w=58 h=7 text="KIND SWITCH" size=5 f=#5FD9A8
R x=6 y=60 w=58 h=10 rx=2 f=#12202F
T x=8 y=61 w=54 h=9 text=$kind size=6 f=#EAF4F8

T x=6 y=76 w=58 h=7 text="OUTLINE" size=5 f=#5FD9A8
T x=6 y=84 w=60 h=18 text="STROKE" size=13 f=#FFFFFF ow=0.6 oc=#E23D3D

T x=70 y=4 w=60 h=7 text="OFF 10 (FILL)" size=5 f=#5FD9A8
R x=70 y=12 w=60 h=24 f=#12202F
IMG x=70 y=12 w=60 h=24 src="PICTURE" off=[10,0]

T x=70 y=40 w=60 h=7 text="NONE | SCALE-DOWN" size=5 f=#5FD9A8
R x=70 y=48 w=36 h=30 f=#12202F
IMG x=70 y=48 w=36 h=30 src="PICTURE" uv=[0.4,0.3,0.43,0.35] fit=none
R x=110 y=48 w=20 h=16 f=#12202F
IMG x=110 y=48 w=20 h=16 src="PICTURE" uv=[0.4,0.3,0.43,0.35] fit=scale-down

T x=70 y=82 w=60 h=7 text="TILE" size=5 f=#5FD9A8
IMG x=70 y=90 w=60 h=26 rx=5 src="PICTURE" tile=[14,8]

T x=70 y=120 w=60 h=7 text="SLICE | NO MIDDLE" size=5 f=#5FD9A8
IMG x=70 y=128 w=28 h=24 src="PICTURE" slice=[80,120,80,120] bw=[5,7,5,7]
IMG x=102 y=128 w=28 h=24 src="PICTURE" slice=[80,120,80,120] bw=[5,7,5,7] mid=0

T x=70 y=156 w=60 h=7 text="SMOOTH | POINT" size=5 f=#5FD9A8
IMG x=70 y=164 w=28 h=28 src="PICTURE" uv=[0.5,0.5,0.515,0.525]
IMG x=102 y=164 w=28 h=28 src="PICTURE" uv=[0.5,0.5,0.515,0.525] smp=point

T x=136 y=4 w=58 h=7 text="REPEAT" size=5 f=#5FD9A8
R x=136 y=12 w=58 h=18 f=@saw fea=0

T x=136 y=36 w=58 h=7 text="REFLECT" size=5 f=#5FD9A8
R x=136 y=44 w=58 h=18 f=@zig fea=0

T x=136 y=68 w=58 h=7 text="RADIAL REPEAT" size=5 f=#5FD9A8
R x=136 y=78 w=58 h=64 rx=4 f=@rings fea=0

T x=136 y=148 w=58 h=7 text="MASK REFLECT" size=5 f=#5FD9A8
G mask=@fade { R x=136 y=156 w=58 h=18 rx=3 f=#F59E0B }
]==]

src = src:gsub("PICTURE", PICTURE)

ui:element({
    id = "t34_s", type = "vector",
    rect = { unit = "px", x = 6, y = 6, w = W - 12, h = H - 12 },
    props = { scene = "t34", src = src },
})

local data = ui:element({
    id = "t34_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "t34", keep = 1, data = { p = 101.3, t = 23, note = "HELLO", kind = 42 } },
})

ui:commit()

local elapsed = 0

function tick(dt)
    elapsed = elapsed + (dt or 0.5)
    local phase = math.floor(elapsed / 2) % 2

    -- `keep`: only what is sent changes, so an empty string and a change of kind are exactly
    -- the two cases that used to stick.
    local values = {
        p = 101.3 + 4 * math.sin(elapsed * 0.7),
        t = 23 + 3 * math.sin(elapsed * 0.4),
        note = (phase == 0) and "HELLO" or "",
    }
    if phase == 0 then values.kind = 42 else values.kind = "OFF" end

    data:set_props({ keep = 1, snap = 1, data = values })
    ui:commit()
end
