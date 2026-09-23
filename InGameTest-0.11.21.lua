-- In-game checks for 0.11.21: the parts offline tests could not reach.
-- Each tile is numbered by requirement; what to look for is in the tile's comment.

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
    id = "t21_s", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = {
        scene = "t21",
        src = [==[
SCENE w=400 h=400 fit=contain

DEFS {
    GL id=rainbow units=bbox x1=0 y1=0 x2=1 y2=0 stops=[[0,#E23D3D],[0.5,#F59E0B],[1,#5FD9A8]]
    GL id=fade units=bbox x1=0 y1=0 x2=1 y2=0 stops=[[0,#FFFFFFFF],[1,#FFFFFF00]]
    CP id=round { R x=140 y=10 w=120 h=60 rx=26 }
    CP id=ell { Y p=[280,10,390,10,390,35,315,35,315,120,280,120] }
}

# 3 + 4: inset text shadow (dark inside the letters, top edge) and three stacked outset shadows
T x=10 y=10 w=120 h=30 text=INSET size=22 weight=bold f=#EAF4F8 sh=[[0,2,2,0,#000000CC,inset]]
T x=10 y=42 w=120 h=30 text=STACK size=22 weight=bold f=#EAF4F8 sh=[[2,2,0,0,#E23D3D],[4,4,0,0,#F59E0B],[6,6,0,0,#5FD9A8]]

# 6: text cut by the ROUNDED clip corners (letters clipped on the curve, not a rectangle)
G clip=round {
    R x=140 y=10 w=120 h=60 f=#1E3247
    T x=130 y=8 w=140 h=64 text=ROUNDED size=30 weight=bold valign=middle f=#F59E0B sh=[[0,3,1,0,#00000099,inset]]
}

# 8: concave L clip, text masked to the L (nothing in the notch at lower right)
G clip=ell {
    R x=280 y=10 w=110 h=110 f=#233A52
    T x=280 y=14 w=110 h=104 wrap=1 text="L CLIP L CLIP L CLIP L CLIP L CLIP" size=14 f=#EAF4F8
}

# 14: gradient text, red -> amber -> green across the word
T x=10 y=80 w=260 h=34 text=GRADIENT size=30 weight=bold f=@rainbow

# 10 + 12: mask fades the row to the right, text included; gray filter makes the green grey
G mask=@fade gray=1 {
    R x=10 y=125 w=260 h=30 rx=6 f=#2E8B6E
    T x=18 y=125 w=250 h=30 text="MASKED AND GREY" size=16 valign=middle f=#5FD9A8
}

# opacity fixes: each drawn at 50% over what is behind it (the game blends in linear light, so
# 50% looks lighter than a browser's 50%); the card's glow fades with the card
G o=0.5 {
    R x=10 y=165 w=125 h=40 rx=8 f=#EAF4F8 sh=[[0,0,12,0,#5FD9A8]]
    T x=10 y=165 w=125 h=40 text="o=0.5" size=14 align=center valign=middle f=#0A121C
}
T x=145 y=165 w=125 h=40 text="fo=0.5" size=14 align=center valign=middle f=#EAF4F8 fo=0.5

# 5 + 15: justified paragraph whose first line is bigger, bold and amber
T x=10 y=215 w=260 h=80 wrap=1 align=justified size=10 f=#8FA6B8 fl="size=13 weight=bold f=#F59E0B" text="The first line of this paragraph should be larger, bold and amber, and every full line should run flush to both edges of the box."

# 7: list jumps to row 12 every six seconds (sov bumps); wheel it in between and it must stay put
SC id=list x=280 y=130 w=110 h=120 ch=600 so==$jump sov==$ver rx=6 {
    RP n=20 {
        R x=284 y==134+i*30 w=102 h=26 rx=4 f=#12202F
        T x=290 y==134+i*30 w=90 h=26 text=row size=10 valign=middle f=#8FA6B8
        T x=340 y==134+i*30 w=40 h=26 text="$rows[i]" size=10 align=right valign=middle f=#EAF4F8
    }
}

# 9 + 16: the whole picture, and its top-left quarter cropped with uv (cover, rounded)
IMG x=10 y=305 w=120 h=85 src=https://raw.githubusercontent.com/github/explore/main/topics/lua/lua.png fit=contain
IMG x=145 y=305 w=120 h=85 src=https://raw.githubusercontent.com/github/explore/main/topics/lua/lua.png uv=[0,0,0.5,0.5] fit=cover rx=12
# Inset text, stencil version: red band inside the top of each letter (hard, soft, spread),
# and one inside the rounded clip, where it nests in a second stencil.
T x=280 y=300 w=110 h=22 text=HARD size=18 weight=bold f=#EAF4F8 sh=[[0,2,0,0,#E23D3DFF,inset]]
T x=280 y=322 w=110 h=22 text=SOFT size=18 weight=bold f=#EAF4F8 sh=[[0,2,2,0,#E23D3DFF,inset]]
T x=280 y=344 w=110 h=22 text=SPREAD size=18 weight=bold f=#EAF4F8 sh=[[0,0,0,1,#E23D3DFF,inset]]
T x=280 y=366 w=110 h=22 text=GRAD size=18 weight=bold f=@rainbow sh=[[1,2,1,0,#000000CC,inset]]
]==],
    },
})

local rows = {}
for i = 1, 20 do rows[i] = tostring(i - 1) end

local data = ui:element({
    id = "t21_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "t21", keep = 1, data = { rows = rows, jump = 330, ver = 1 } },
})

local elapsed, ver = 0, 1

function tick(dt)
    elapsed = elapsed + (dt or 0.5)
    local want = math.floor(elapsed / 6) + 1
    if want ~= ver then
        ver = want
        data:set_props({ data = { ver = ver } })
        ui:commit()
    end
end
