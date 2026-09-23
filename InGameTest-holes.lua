-- In-game check for clipped fills with holes (0.11.23).
-- Expected, four tiles on a dark ground, no magenta border, no hole warning in the log:
--   top left:     red frame clipped to its tile; the dark hole shows through (was solid)
--   top right:    gold ring clipped by a circle; hole shows, outer corners cut round
--   bottom left:  blue-to-cyan frame whose clip cuts through the hole: the left half of a frame,
--                 a notch where the hole was, smooth gradient, no cracks (was solid)
--   bottom right: green frame under an L-shaped clip; the hole sits across the L's inner corner,
--                 the notch (bottom right) is empty, no cracks along the L's internal cut

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
    id = "holes_s", type = "vector",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = {
        scene = "holes",
        src = [==[
SCENE w=200 h=200 fit=contain
DEFS {
 CP id=tl { R x=5 y=5 w=90 h=90 }
 CP id=tr { C cx=150 cy=50 rx=38 ry=38 }
 CP id=bl { R x=5 y=105 w=45 h=90 }
 CP id=br { Y p=[105,105,195,105,195,150,150,150,150,195,105,195] }
 GL id=sea units=bbox x1=0 y1=0 x2=1 y2=1 stops=[[0,#1E4FA8],[1,#40D0E0]]
}
G clip=tl {
 P d="M0 0 h100 v100 h-100 z M25 25 h50 v50 h-50 z" f=#B5352C fr=evenodd
}
G clip=tr {
 P d="M105 5 h90 v90 h-90 z M135 35 h30 v30 h-30 z" f=#E0B040 fr=evenodd
}
G clip=bl {
 P d="M10 110 h80 v80 h-80 z M30 130 h40 v40 h-40 z" f=@sea fr=evenodd
}
G clip=br {
 P d="M110 110 h80 v80 h-80 z M130 130 h40 v40 h-40 z" f=#4CB050 fr=evenodd
}
]==],
    },
})
