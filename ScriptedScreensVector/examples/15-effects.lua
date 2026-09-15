-- 15 - Effects: inset shadows, conic gradients, filters, masks, matrices, concave clips
--
-- Six CSS-shaped additions, one tile each. Everything here is geometry built while the mesh
-- is built, like the rest of the format: no offscreen pass, no shader.
--
--   1. INSET SHADOW  - sh's sixth field "inset". Over the fill, under the stroke, cut to the
--                      shape. Convex shapes only.
--   2. CONIC         - GC, a sweep around a centre, clockwise from `a` degrees past twelve.
--   3. FILTERS       - bri con sat hue gray sep inv on a group, in the order written.
--   4. MASK          - mask=@gradient on a group: its alpha fades everything inside, text too.
--   5. MATRIX        - m=[a,b,c,d,e,f], CSS matrix(), applied before t r s.
--   6. CONCAVE CLIP  - any outline clips. It costs a draw of the contents per convex piece.
--
-- And IMG, which needs a URL you trust, so it is left commented at the bottom of the scene.
--
-- Filters act on vertex colours: exact on flat colours, close on gradients, and not applied to
-- IMG pictures at all (the scene says so rather than tinting them wrongly).

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 480, 480
if size then W, H = size.w, size.h end

ui:clear()

ui:element({
    id = "bg", type = "panel",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    style = { bg = "#0A121C" },
})

ui:element({
    id = "fx_s", type = "vector",
    rect = { unit = "px", x = 12, y = 12, w = W - 24, h = H - 24 },
    props = {
        scene = "fx",
        src = [==[
SCENE w=300 h=300 fit=contain

DEFS {
    GC id=dial units=bbox cx=0.5 cy=0.5 a=-120 stops=[[0,#5FD9A8],[0.6,#F59E0B],[1,#E23D3D]]
    GL id=fade units=bbox x1=0 y1=0 x2=0 y2=1 stops=[[0,#FFFFFFFF],[0.6,#FFFFFFFF],[1,#FFFFFF00]]
    CP id=ell { Y p=[210,170,290,170,290,200,240,200,240,290,210,290] }
}

# 1. Inset shadow: a recessed well, lit from above.
R x=10 y=10 w=90 h=70 rx=10 f=#2A3C50 sh=[[0,5,10,0,#000000CC,inset]]
T x=10 y=84 w=90 h=10 text=INSET size=7 cspace=2 align=center f=#5A7085

# 2. Conic: a gauge sweep starting at a=-120, lower left, where red meets green in a hard edge.
C cx=155 cy=45 rx=35 ry=35 f=@dial
C cx=155 cy=45 rx=24 ry=24 f=#0A121C
T x=110 y=84 w=90 h=10 text=CONIC size=7 cspace=2 align=center f=#5A7085

# 3. Filters: the same badge, desaturated and dimmed when a value says it is offline.
G gray==$offline bri==1-0.5*$offline {
    R x=215 y=15 w=75 h=60 rx=8 f=#2E8B6E
    T x=215 y=35 w=75 h=20 text=ONLINE size=10 align=center valign=middle f=#EAF4F8 sh=[[0,1,2,0,#00000080]]
}
T x=210 y=84 w=90 h=10 text=FILTERS size=7 cspace=2 align=center f=#5A7085

# 4. Mask: a list fading out at the bottom, text included.
G mask=@fade {
    RP n=5 {
        R x=10 y==110+i*16 w=90 h=13 rx=3 f=#12202F
        T x=16 y==110+i*16 w=80 h=13 text=row size=8 valign=middle f=#8FA6B8
    }
}
T x=10 y=196 w=90 h=10 text=MASK size=7 cspace=2 align=center f=#5A7085

# 5. Matrix: a skew, which t r s cannot express.
G t=[155,150] m=[1,0,-0.4,1,0,0] {
    R x=-30 y=-25 w=60 h=50 rx=4 f=#1E3247 s=#5FD9A8 sw=1.5
    T x=-30 y=-8 w=60 h=16 text=SKEW size=9 align=center valign=middle f=#EAF4F8
}
T x=110 y=196 w=90 h=10 text=MATRIX size=7 cspace=2 align=center f=#5A7085

# 6. Concave clip: an L-shaped window. The label is masked to the L as well.
G clip=ell {
    R x=200 y=160 w=100 h=140 f=#233A52
    RP n=8 { R x==200+i*14 y=160 w=6 h=140 f=#2E4B69 }
    T x=205 y=175 w=90 h=14 text="L-SHAPED CLIP" size=8 f=#EAF4F8
}

# IMG: uncomment with a URL you trust. Nothing draws until it loads.
# IMG x=10 y=215 w=90 h=75 src=https://example.com/picture.png fit=cover rx=8
]==],
    },
})

local data = ui:element({
    id = "fx_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "fx", keep = 1, data = { offline = 0 } },
})

local elapsed = 0

function tick(dt)
    elapsed = elapsed + (dt or 0.5)

    -- Flip the badge every few seconds; the filter eases with the data.
    local offline = (math.floor(elapsed / 4) % 2 == 1) and 1 or 0
    data:set_props({ data = { offline = offline } })
    ui:commit()
end
