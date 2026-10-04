-- 17 - Pictures: IMG, and the five ways to place one
--
-- `IMG` draws a PNG, JPEG, BMP or the first frame of a GIF, from a URL or a `data:` URI. The
-- picture is fetched once per source per client and cached, so twenty nodes showing the same
-- logo cost one download.
--
-- THE BOX AND THE PICTURE ARE TWO DIFFERENT RECTANGLES, and `fit` says how one meets the
-- other. This is the thing to understand; everything else here is detail.
--
--     fit=fill         stretch to the box, aspect ignored (the default)
--     fit=contain      fit inside, whole picture visible, bars on the long axis
--     fit=cover        fill the box, aspect kept, the overflow cropped
--     fit=none         natural size, cropped by the box
--     fit=scale-down   `none`, or `contain` when the picture is larger than the box
--
-- `at = [ax, ay]` then says WHERE in the box the picture sits, `0..1` from the top left, which
-- only shows when the two rectangles differ: `cover` aligned `at=[0,0.5]` keeps the left of a
-- wide photo, `at=[1,0.5]` keeps the right.
--
-- `uv = [x1, y1, x2, y2]` crops the SOURCE before any of that, in `0..1` of the texture, top
-- down. Crop first, then fit the crop. A sprite sheet is a `uv` per icon.
--
-- `tile = [w, h]` repeats one picture of that size across the box instead of stretching one
-- copy -- a texture, a hatch, a scanline. `slice = [l, t, r, b]` with `bw = [...]` is CSS
-- nine-slice: the corners keep their size, the edges stretch, so one small PNG makes a panel
-- border at any size. `smp = point` turns off smoothing, for pixel art that should stay crisp.
--
-- A FAILED DOWNLOAD IS REPORTED, not silent: `vector_stats` shows the HTTP error against the
-- node. A picture that has not arrived yet simply is not drawn, so put a plain `R` behind it
-- if the gap would look like a fault.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

-- Any reachable image URL works. This one ships with the mod, pinned to a commit so the link
-- keeps working when the repository is rearranged.
local PIC = "https://raw.githubusercontent.com/Gruffuss/scripted-screens-vector/"
         .. "09be911193cecfd8cc5a624c6bb55febad26f921/ScriptedScreensVector/About/thumb.png"

local src = table.concat({
    "SCENE w=200 h=200 fit=stretch size=5 f=#8FA6B8",
    "R x=0 y=0 w=200 h=200 f=#0B1622 fea=0",

    -- 1 THE FIT MODES, same box, same picture
    'T x=4 y=3 w=192 h=6 text="fit: fill / contain / cover -- same box, same picture"',
    "R x=4 y=11 w=60 h=34 f=#12202F fea=0",
    "R x=70 y=11 w=60 h=34 f=#12202F fea=0",
    "R x=136 y=11 w=60 h=34 f=#12202F fea=0",
    'IMG x=4 y=11 w=60 h=34 src="PIC" fit=fill',
    'IMG x=70 y=11 w=60 h=34 src="PIC" fit=contain',
    'IMG x=136 y=11 w=60 h=34 src="PIC" fit=cover',

    -- 2 WHERE THE PICTURE SITS WHEN IT DOES NOT FILL  (at)
    'T x=4 y=50 w=192 h=6 text="cover + at: keep the left edge, the middle, the right"',
    'IMG x=4 y=58 w=60 h=26 src="PIC" fit=cover at=[0,0.5] rx=3',
    'IMG x=70 y=58 w=60 h=26 src="PIC" fit=cover at=[0.5,0.5] rx=3',
    'IMG x=136 y=58 w=60 h=26 src="PIC" fit=cover at=[1,0.5] rx=3',

    -- 3 CROPPING THE SOURCE  (uv)
    'T x=4 y=89 w=192 h=6 text="uv: the whole picture, then a quarter of it, magnified"',
    'IMG x=4 y=97 w=60 h=30 src="PIC" uv=[0,0,1,1]',
    'IMG x=70 y=97 w=60 h=30 src="PIC" uv=[0.25,0.25,0.75,0.75]',
    --   a small crop blown up: smoothed, then with smoothing off
    'IMG x=136 y=97 w=28 h=30 src="PIC" uv=[0.3,0.3,0.4,0.42]',
    'IMG x=168 y=97 w=28 h=30 src="PIC" uv=[0.3,0.3,0.4,0.42] smp=point',

    -- 4 REPEATING ONE PICTURE  (tile)
    'T x=4 y=132 w=192 h=6 text="tile: one copy, repeated"',
    'IMG x=4 y=140 w=92 h=26 rx=4 src="PIC" tile=[22,12]',

    -- 5 NINE-SLICE: one PNG, any size, corners intact
    'T x=102 y=132 w=94 h=6 text="slice: corners keep their size"',
    'IMG x=102 y=140 w=40 h=26 src="PIC" slice=[80,120,80,120] bw=[5,7,5,7]',
    'IMG x=148 y=140 w=48 h=26 src="PIC" slice=[80,120,80,120] bw=[5,7,5,7]',

    -- A picture is a node like any other: it clips, rounds and takes clicks.
    'T x=4 y=172 w=192 h=6 text="an IMG rounds, fades and clicks like any other node"',
    'IMG id=pic x=4 y=180 w=52 h=16 rx=8 src="PIC" fit=cover fo=0.55 press=1',
    'T x=62 y=183 w=134 h=8 size=5 f=#5FD9A8 text=$tap missing="(click the rounded one)"',
}, "\n"):gsub("PIC", PIC)

local data

ui:element({
    id = "pic_s", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = { scene = "pic", src = src },
    on_click = function(value)
        data:set_props({ scene = "pic", keep = 1, snap = 1, data = { tap = "clicked: " .. value } })
        ui:commit()
    end,
})

data = ui:element({
    id = "pic_d", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
    props = { scene = "pic", keep = 1, data = {} },
})

ui:commit()
