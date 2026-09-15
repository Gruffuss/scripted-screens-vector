-- In-game checks for 0.11.21: the paths that must REPORT rather than draw wrongly.
-- Expected: a magenta border (the scene has problems) and, in vector_stats for scene "tp":
--   * P: inset shadow needs a convex shape; not drawn        (the star still fills)
--   * IMG "...no-such-file.png": HTTP 404                    (nothing drawn there)
--   * IMG "...lua.png": colour filters do not apply to images; drawn unfiltered (picture in colour)
--   * T fl: "colour" is not a first-line attribute           (text still draws)
--   * IMG: uv takes [u0,v0,u1,v1] ...                        (picture drawn whole)
--   * G: m takes six numbers, 3 given                        (box drawn untransformed)
--   * mask gradient "nope" is not declared in defs           (box drawn unmasked)
--   * T: text takes one inset shadow; later ones not drawn

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

ui:element({
    id = "bg", type = "panel",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    style = { bg = "#0A121C" },
})

ui:element({
    id = "tp_s", type = "vector",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = {
        scene = "tp",
        src = [==[
SCENE w=200 h=200 fit=contain

P d="M50 8 L60 35 L90 38 L67 55 L75 82 L50 66 L25 82 L33 55 L10 38 L40 35 Z" f=#5FD9A8 sh=[[0,3,4,0,#000000CC,inset]]
IMG x=110 y=10 w=80 h=70 src=https://raw.githubusercontent.com/github/explore/main/topics/lua/no-such-file.png
G gray=1 {
    IMG x=10 y=100 w=50 h=45 src=https://raw.githubusercontent.com/github/explore/main/topics/lua/lua.png
}
T x=70 y=100 w=60 h=20 text=FIRST fl="colour=#fff" size=10 f=#EAF4F8
IMG x=140 y=100 w=50 h=45 src=https://raw.githubusercontent.com/github/explore/main/topics/lua/lua.png uv=[0.5,0,0.2,1]
G m=[1,0,0] {
    R x=10 y=155 w=40 h=35 f=#F59E0B
}
G mask=@nope {
    R x=60 y=155 w=40 h=35 f=#3B82F6
}
T x=110 y=160 w=85 h=24 text=TWO size=16 weight=bold f=#EAF4F8 sh=[[0,2,0,0,#E23D3D,inset],[0,-2,0,0,#5FD9A8,inset]]
]==],
    },
})
