-- In-game checks for 0.11.21, part B: everything part A did not put on a console.
-- A 4x4 grid; each tile's caption says what it tests. Expected looks are in the comments.
-- Hands needed: wheel the list in tile 11 between jumps, and click the star in tile 10.

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

local clicks = 0

local data

local function on_star(nodeId)
    clicks = clicks + 1
    print("star clicked: " .. tostring(nodeId) .. " (" .. clicks .. ")")
    if data then
        data:set_props({ data = { clickmsg = "CLICKS " .. clicks } })
        ui:commit()
    end
end

local LUA = "https://raw.githubusercontent.com/github/explore/main/topics/lua/lua.png"

ui:element({
    id = "tb_s", type = "vector",
    on_click = on_star,
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = {
        scene = "tb",
        src = [==[
SCENE w=400 h=400 fit=contain

DEFS {
    GL id=ramp units=bbox x1=0 y1=0 x2=1 y2=0 stops=[[0,#1E3247],[1,#5FD9A8]]
    GL id=lin units=bbox x1=0 y1=0 x2=1 y2=0 stops=[[0,#E23D3D],[1,#5FD9A8]]
    GC id=wheel cx=250 cy=45 a=0 stops=[[0,#E23D3D],[0.25,#F59E0B],[0.5,#5FD9A8],[0.75,#3B82F6],[1,#E23D3D]]
    GC id=wheelbox units=bbox cx=0.5 cy=0.5 a=0 stops=[[0,#E23D3D],[0.5,#3B82F6],[1,#E23D3D]]
    GR id=spot units=bbox cx=0.5 cy=0.5 r=0.5 stops=[[0,#FFFFFFFF],[1,#FFFFFF00]]
    GC id=sweep units=bbox cx=0.5 cy=0.5 a=0 stops=[[0,#FFFFFF00],[1,#FFFFFFFF]]
    GR id=rtxt units=bbox cx=0.5 cy=0.5 r=0.6 stops=[[0,#F59E0B],[1,#3B82F6]]
    GC id=ctxt units=bbox cx=0.5 cy=0.5 a=0 stops=[[0,#E23D3D],[0.5,#5FD9A8],[1,#E23D3D]]
    CP id=tall { R x=160 y=10 w=35 h=70 }
    CP id=star { P d="M150 305 L162 332 L193 334 L169 353 L177 385 L150 367 L123 385 L131 353 L107 334 L138 332 Z" }
    CP id=ell2 { Y p=[205,300,295,300,295,330,235,330,235,385,205,385] }
    CP id=ell3 { Y p=[305,300,395,300,395,330,335,330,335,385,305,385] }
}

# 1 FAT/SAT: the bar's fill and the text breathe from dark blue to green; the outline runs opposite.
R x=10 y=10 w=80 h=34 rx=6 f=@ramp fat==0.5+0.5*sin(t*2) s=@ramp sat==0.5-0.5*sin(t*2) sw=3
T x=10 y=48 w=80 h=24 text=FAT size=18 weight=bold align=center f=@ramp fat==0.5+0.5*sin(t*2)
T x=5 y=84 w=90 h=12 text="1 fat/sat" size=7 align=center f=#5A7085

# 2 INSET on an ellipse (dark lower right), and on a rect cut by a clip (only the left part shows, shadow clipped too).
C cx=130 cy=40 rx=24 ry=24 f=#5FD9A8 sh=[[3,3,6,0,#000000CC,inset]]
G clip=tall {
    R x=155 y=15 w=50 h=60 rx=8 f=#F59E0B sh=[[0,5,6,0,#000000CC,inset]]
}
T x=105 y=84 w=90 h=12 text="2 inset ellipse+clip" size=7 align=center f=#5A7085

# 3 CONIC in scene units (red at 12, amber at 3, green at 6, blue at 9); the hole has a conic stroke, a white ring inside it.
C cx=250 cy=45 rx=36 ry=36 f=@wheel
C cx=250 cy=45 rx=18 ry=18 f=#0A121C s=@wheel sw=3
C cx=250 cy=45 rx=12 ry=12 f=none s=#FFFFFF sw=1.5
T x=205 y=84 w=90 h=12 text="3 conic units+stroke" size=7 align=center f=#5A7085

# 4 CONIC on a concave star (falls back to subdivision; colours still sweep, seam at 12).
Y p=[350,8,360,35,390,38,367,55,375,82,350,66,325,82,333,55,310,38,340,35] f=@wheelbox
T x=305 y=84 w=90 h=12 text="4 conic concave" size=7 align=center f=#5A7085

# 5 MASK radial: a red grid fading out from the centre, text included.
G mask=@spot {
    RP n=4 {
        RP n=4 { R x==12+i*20 y==108+i1*18 w=17 h=15 f=#E23D3D }
    }
    T x=5 y=135 w=90 h=16 text=RADIAL size=12 weight=bold align=center f=#FFFFFF
}
T x=5 y=184 w=90 h=12 text="5 mask radial" size=7 align=center f=#5A7085

# 6 MASK conic: transparent at 12 o'clock, sweeping to solid just before it.
G mask=@sweep {
    R x=108 y=104 w=84 h=76 rx=6 f=#5FD9A8
    T x=108 y=134 w=84 h=16 text=SWEEP size=12 weight=bold align=center f=#0A121C
}
T x=105 y=184 w=90 h=12 text="6 mask conic" size=7 align=center f=#5A7085

# 7 FILTERS, one each over the same red/green/blue bars:
#   top row  bri=0.5 (darker), con=2 (punchier), sat=0 (greys)
#   bottom   hue=180 (cyan/magenta/yellow), sep=1 (browns), inv=1 (cyan/magenta/yellow, inverted)
G bri=0.5 {
    R x=205 y=104 w=8 h=30 f=#E23D3D
    R x=214 y=104 w=8 h=30 f=#5FD9A8
    R x=223 y=104 w=8 h=30 f=#3B82F6
}
G con=2 {
    R x=235 y=104 w=8 h=30 f=#E23D3D
    R x=244 y=104 w=8 h=30 f=#5FD9A8
    R x=253 y=104 w=8 h=30 f=#3B82F6
}
G sat=0 {
    R x=265 y=104 w=8 h=30 f=#E23D3D
    R x=274 y=104 w=8 h=30 f=#5FD9A8
    R x=283 y=104 w=8 h=30 f=#3B82F6
}
G hue=180 {
    R x=205 y=142 w=8 h=30 f=#E23D3D
    R x=214 y=142 w=8 h=30 f=#5FD9A8
    R x=223 y=142 w=8 h=30 f=#3B82F6
}
G sep=1 {
    R x=235 y=142 w=8 h=30 f=#E23D3D
    R x=244 y=142 w=8 h=30 f=#5FD9A8
    R x=253 y=142 w=8 h=30 f=#3B82F6
}
G inv=1 {
    R x=265 y=142 w=8 h=30 f=#E23D3D
    R x=274 y=142 w=8 h=30 f=#5FD9A8
    R x=283 y=142 w=8 h=30 f=#3B82F6
}
T x=205 y=184 w=90 h=12 text="7 bri con sat / hue sep inv" size=7 align=center f=#5A7085

# 8 NESTED + ANIMATED: hue spins through colours, brightness pulses; the text changes colour with them.
G bri==0.7+0.3*sin(t*2) {
    G hue==t*60 {
        R x=308 y=104 w=26 h=50 f=#E23D3D
        R x=337 y=104 w=26 h=50 f=#5FD9A8
        R x=366 y=104 w=26 h=50 f=#3B82F6
        T x=305 y=158 w=90 h=18 text=SPIN size=14 weight=bold align=center f=#F59E0B
    }
}
T x=305 y=184 w=90 h=12 text="8 nested animated" size=7 align=center f=#5A7085

# 9 MATRIX: skewed box with SKEWED LETTERS leaning right; below, scale(1.6,0.6) with wide flat letters.
G t=[50,228] m=[1,0,-0.5,1,0,0] {
    R x=-30 y=-18 w=60 h=36 f=#1E3247 s=#5FD9A8 sw=1
    T x=-30 y=-9 w=60 h=18 text=SKEW size=14 weight=bold align=center valign=middle f=#EAF4F8
}
G t=[50,268] m=[1.6,0,0,0.6,0,0] {
    T x=-28 y=-10 w=56 h=20 text=WIDE size=14 weight=bold align=center valign=middle f=#F59E0B
}
T x=5 y=284 w=90 h=12 text="9 matrix text" size=7 align=center f=#5A7085

# 10 GRADIENT TEXT: radial (amber centre, blue edge), conic (red/green around), linear in a turned group.
T x=105 y=204 w=90 h=22 text=RADIAL size=18 weight=bold align=center f=@rtxt
T x=105 y=228 w=90 h=22 text=CONIC size=18 weight=bold align=center f=@ctxt
G t=[150,262] r=-15 {
    T x=-45 y=-10 w=90 h=20 text=TURNED size=16 weight=bold align=center f=@lin
}
T x=105 y=284 w=90 h=12 text="10 gradient text" size=7 align=center f=#5A7085

# 11 FIRST LINE with a font switch (code, amber) and italic rich text further on.
T x=205 y=204 w=90 h=76 wrap=1 size=8 f=#8FA6B8 fl="font=code size=9 f=#F59E0B" text="first line in the code face, then <i>italic</i> and plain text wrapping on below it."
T x=205 y=284 w=90 h=12 text="11 fl font + rich" size=7 align=center f=#5A7085

# 12 UV: fill stretches the picture's centre half into a tall box; contain letterboxes its top half.
IMG x=305 y=204 w=40 h=76 src=https://raw.githubusercontent.com/github/explore/main/topics/lua/lua.png uv=[0.25,0.25,0.75,0.75] fit=fill
IMG x=350 y=204 w=45 h=76 src=https://raw.githubusercontent.com/github/explore/main/topics/lua/lua.png uv=[0,0,1,0.5] fit=contain
T x=305 y=284 w=90 h=12 text="12 uv fill/contain" size=7 align=center f=#5A7085

# 13 IMG IN A SCROLL LIST: three pictures; wheel it, the pictures scroll and clip to the rounded box.
SC id=pics x=5 y=300 w=90 h=84 ch=252 rx=8 {
    RP n=3 {
        IMG x=9 y==304+i*84 w=82 h=76 src=https://raw.githubusercontent.com/github/explore/main/topics/lua/lua.png fit=cover rx=6
    }
}
T x=5 y=386 w=90 h=12 text="13 wheel: images scroll" size=7 align=center f=#5A7085

# 14 PATH CLIP (star) with text inside and a click region: click the star, the chip log counts.
G clip=star {
    R id=star click=1 x=100 y=300 w=100 h=90 f=#2E8B6E
    T x=105 y=336 w=90 h=16 text="$clickmsg" missing="CLICK" size=11 weight=bold align=center f=#EAF4F8
}
T x=105 y=386 w=90 h=12 text="14 star counts, corners don't" size=7 align=center f=#5A7085

# 15 SCROLL INSIDE A CONCAVE CLIP, jumping to row 6 every 15 s; wheel it between jumps and it stays.
G clip=ell2 {
    SC id=lrows x=205 y=300 w=90 h=85 ch=400 so==$jump sov==$ver {
        RP n=20 {
            R x=207 y==302+i*20 w=86 h=17 rx=3 f=#12202F
            T x=210 y==302+i*20 w=80 h=17 text="$rows[i]" size=8 valign=middle f=#EAF4F8
        }
    }
}
T x=205 y=386 w=90 h=12 text="$countdown" missing="15 wheel between jumps" size=7 align=center f=#F59E0B

# 16 IMG UNDER A CONCAVE CLIP: the picture shows only inside the L.
G clip=ell3 {
    IMG x=305 y=300 w=90 h=85 src=https://raw.githubusercontent.com/github/explore/main/topics/lua/lua.png fit=cover
}
T x=305 y=386 w=90 h=12 text="16 img concave clip" size=7 align=center f=#5A7085
]==],
    },
})

local rows = {}
for i = 1, 20 do rows[i] = "row " .. (i - 1) end

data = ui:element({
    id = "tb_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "tb", keep = 1, data = { rows = rows, jump = 120, ver = 1 } },
})

local elapsed, ver = 0, 1

function tick(dt)
    elapsed = elapsed + (dt or 0.5)
    local want = math.floor(elapsed / 15) + 1
    local left = 15 - math.floor(elapsed % 15)
    local update = { countdown = "jump to row 6 in " .. left .. " s" }
    if want ~= ver then
        ver = want
        update.ver = ver
    end
    data:set_props({ data = update })
    ui:commit()
end
