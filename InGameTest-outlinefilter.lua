-- Does a group's colour filter reach a label's OUTLINE on a real console? (0.11.88)
--
-- This is the one fix in 0.11.88-0.11.90 that had only ever been checked offline, and the
-- a mirroring tool is dropping its own compensating outline filter on the strength of it. The
-- outline is a MATERIAL property on TMP rather than a vertex colour, so the headless harness
-- sees the value handed to the material, not what the shader finally draws -- which is exactly
-- the gap a console closes.
--
-- WHITE text with a thick RED outline, so fill and outline are told apart at a glance:
--
--   PASS   row 2 and row 3 outlines are GREY / INVERTED like their fills
--   FAIL   the outlines stay RED while the fills change -- the filter missed the outline

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

local function label(y, text)
    return 'T x=10 y=' .. y .. ' w=180 h=22 size=20 weight=bold f=#FFFFFF oc=#FF0000 ow=0.35 text="'
        .. text .. '"'
end

local src = table.concat({
    "SCENE w=200 h=200 fit=stretch size=5 f=#EAF4F8",
    "R x=0 y=0 w=200 h=200 f=#12202F fea=0",
    'T x=10 y=4 w=180 h=7 size=6 f=#8FA6B8 text="white fill, red outline -- filters must reach both"',

    -- 1  NO FILTER: the control. Red outline, white fill.
    'T x=10 y=16 w=180 h=6 size=5 f=#53646F text="1  no filter (control)"',
    label(24, "OUTLINE"),

    -- 2  GREYSCALE: both fill and outline must lose their colour.
    'T x=10 y=56 w=180 h=6 size=5 f=#53646F text="2  gray=1 -- outline must go grey"',
    "G gray=1 {",
    label(64, "OUTLINE"),
    "}",

    -- 3  INVERT: red inverts to cyan, white to black.
    'T x=10 y=96 w=180 h=6 size=5 f=#53646F text="3  inv=1 -- outline must go cyan"',
    "G inv=1 {",
    label(104, "OUTLINE"),
    "}",

    -- 4  NESTED: filters compose, so this is darker still.
    'T x=10 y=136 w=180 h=6 size=5 f=#53646F text="4  bri=0.5 then gray=1"',
    "G bri=0.5 {",
    "G gray=1 {",
    label(144, "OUTLINE"),
    "}",
    "}",

    'T x=10 y=180 w=180 h=14 size=5 f=#8FA6B8 wrap=1 text="FAIL looks like: rows 2-4 outlines still RED while their fills change."',
}, "\n")

ui:element({
    id = "of_s", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = { scene = "of", src = src },
})

ui:commit()
