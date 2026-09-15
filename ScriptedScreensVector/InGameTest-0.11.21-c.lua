-- In-game checks for 0.11.21, part C: combinations nothing else covers.
-- A 3x3 grid; each caption says what to look for.

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
    id = "tc_s", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = {
        scene = "tc",
        src = [==[
SCENE w=300 h=400 fit=contain

DEFS {
    GL id=fade units=bbox x1=0 y1=0 x2=1 y2=0 stops=[[0,#FFFFFFFF],[1,#FFFFFF00]]
    GL id=lin units=bbox x1=0 y1=0 x2=1 y2=0 stops=[[0,#E23D3D],[1,#5FD9A8]]
    GC id=wheel units=bbox cx=0.5 cy=0.5 a=0 stops=[[0,#E23D3D],[0.33,#F59E0B],[0.66,#3B82F6],[1,#E23D3D]]
    GC id=sweep units=bbox cx=0.5 cy=0.5 a=0 stops=[[0,#FFFFFF00],[1,#FFFFFFFF]]
}

# 1 SKEW + SHADOWS: leaning letters; the red and green shadow copies lean with them, the red inset band inside too.
G t=[50,40] m=[1,0,-0.5,1,0,0] {
    T x=-45 y=-12 w=90 h=24 text=LEAN size=20 weight=bold align=center valign=middle f=#EAF4F8 sh=[[2,2,0,0,#E23D3D],[4,4,0,0,#5FD9A8]]
    T x=-45 y=14 w=90 h=24 text=LEAN size=20 weight=bold align=center valign=middle f=#EAF4F8 sh=[[0,2,0,0,#E23D3DFF,inset]]
}
T x=5 y=84 w=90 h=12 text="1 skew + shadows" size=7 align=center f=#5A7085

# 2 STRETCH: a group scaled 1.6 wide, 0.7 tall; the letters and their shadow are wide and flat.
G t=[150,45] s=[1.6,0.7] {
    T x=-28 y=-14 w=56 h=28 text=WIDE size=20 weight=bold align=center valign=middle f=#F59E0B sh=[[2,2,1,0,#000000]]
}
T x=105 y=84 w=90 h=12 text="2 uneven scale" size=7 align=center f=#5A7085

# 3 FILTERS ON TEXT SHADOWS: red text with a red shadow, hue-rotated: text and shadow both turn cyan.
G hue=180 {
    T x=205 y=20 w=90 h=26 text=HUE size=20 weight=bold align=center valign=middle f=#E23D3D sh=[[3,3,0,0,#E23D3D]]
}
G gray=1 {
    T x=205 y=50 w=90 h=26 text=GREY size=20 weight=bold align=center valign=middle f=#5FD9A8 sh=[[0,2,0,0,#E23D3DFF,inset]]
}
T x=205 y=84 w=90 h=12 text="3 filters on shadows" size=7 align=center f=#5A7085

# 4 MASK ON TEXT WITH SHADOWS: the letters and their shadow copies fade out to the right together.
G mask=@fade {
    T x=5 y=112 w=90 h=26 text=FADING size=18 weight=bold align=center valign=middle f=#EAF4F8 sh=[[2,2,0,0,#E23D3D],[3,3,0,0,#3B82F6]]
    T x=5 y=142 w=90 h=26 text=FADING size=18 weight=bold align=center valign=middle f=#EAF4F8 sh=[[0,2,0,0,#E23D3DFF,inset]]
}
T x=5 y=184 w=90 h=12 text="4 mask on shadows" size=7 align=center f=#5A7085

# 5 LABEL vs IMG ORDER: UNDER is declared before the picture and is hidden where it overlaps;
#   OVER is declared after it and draws on top.
T x=105 y=112 w=90 h=20 text=UNDER size=16 weight=bold align=center f=#F59E0B
IMG x=115 y=118 w=70 h=50 src=https://raw.githubusercontent.com/github/explore/main/topics/lua/lua.png fit=cover rx=6
T x=105 y=148 w=90 h=20 text=OVER size=16 weight=bold align=center f=#F59E0B
T x=105 y=184 w=90 h=12 text="5 label vs picture order" size=7 align=center f=#5A7085

# 6 GRADIENT TEXT UNDER SKEW: the red-to-green ramp runs along the leaning word.
G t=[250,140] m=[1,0,-0.4,1,0,0] {
    T x=-45 y=-14 w=90 h=28 text=RAMPED size=18 weight=bold align=center valign=middle f=@lin
}
T x=205 y=184 w=90 h=12 text="6 gradient text + skew" size=7 align=center f=#5A7085

# 7 CONIC WITH FEATHERED EDGES: soft antialiased rims; no seam smear, no dark ring at the edge.
C cx=35 cy=240 rx=28 ry=28 f=@wheel
R x=68 y=212 w=28 h=56 rx=10 f=@wheel
T x=5 y=284 w=90 h=12 text="7 conic, default feather" size=7 align=center f=#5A7085

# 8 CONIC MASK OVER FEATHERED SHAPES: circles fading around the clock; soft edges stay soft, no specks.
G mask=@sweep {
    RP n=3 {
        C cx==125+i*25 cy=225 rx=11 ry=11 f=#5FD9A8
        C cx==125+i*25 cy=255 rx=11 ry=11 f=#F59E0B
    }
}
T x=105 y=284 w=90 h=12 text="8 conic mask, feathered" size=7 align=center f=#5A7085

# 9 INSET + CLIP + FILTER together: a rounded card in a concave clip, greyscaled, with an inset shadow.
DEFS {
    CP id=notch { Y p=[205,205,295,205,295,240,265,240,265,275,205,275] }
}
G clip=notch gray=1 {
    R x=205 y=205 w=90 h=70 rx=12 f=#E23D3D sh=[[0,5,8,0,#000000CC,inset]]
}
T x=205 y=284 w=90 h=12 text="9 inset + concave + gray" size=7 align=center f=#5A7085

# 10-13 INSET TEXT IN GROUPS: which of these shows the red band inside the top of the letters?
T x=5 y=310 w=70 h=26 text=TOP size=20 weight=bold align=center valign=middle f=#EAF4F8 sh=[[0,2,0,0,#E23D3DFF,inset]]
G {
    T x=80 y=310 w=70 h=26 text=G size=20 weight=bold align=center valign=middle f=#EAF4F8 sh=[[0,2,0,0,#E23D3DFF,inset]]
}
G hue=0 {
    T x=155 y=310 w=70 h=26 text=HUE0 size=20 weight=bold align=center valign=middle f=#EAF4F8 sh=[[0,2,0,0,#E23D3DFF,inset]]
}
G t=[260,323] r=5 {
    T x=-35 y=-13 w=70 h=26 text=ROT size=20 weight=bold align=center valign=middle f=#EAF4F8 sh=[[0,2,0,0,#E23D3DFF,inset]]
}
T x=5 y=345 w=290 h=12 text="10 top level / 11 plain group / 12 hue=0 / 13 rotated" size=7 align=center f=#5A7085
]==],
    },
})
