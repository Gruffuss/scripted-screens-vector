-- Two colour-name questions the data-bound page could not answer.
--
-- `InGameTest-colourstrict.lua` sends its strings through `data`, and that path short-circuits
-- `transparent` and `none` before Unity's parser sees them. So it could never show whether Unity
-- itself knows `transparent`. These values are written in SCENE TEXT instead, which goes straight
-- to `ColorUtility.TryParseHtmlString`.
--
-- WHAT TO SEE
--
-- 1 transparent, in scene text. A bar filled `transparent` over a white panel.
--     * invisible (the panel shows through) -> Unity accepts the name.
--     * magenta                             -> it does not, and only the mod's shortcut did.
--
-- 2 transparent, as a GRADIENT STOP. Same question on a different call site (Gradient.cs), which
--   has no shortcut at all. A ramp from solid green to `transparent` must fade out. If the stop
--   were rejected the ramp would end in white, which is the fallback for an unparsed stop.
--
-- 3 darkblue. Two bars: the top filled by name, the bottom filled with the literal #00008B that
--   Unity's own table is said to hold. **They must match.** If the top is instead #0000A0 -- a
--   visibly lighter, more purple navy -- the name resolves through a different table than
--   expected, and anything reimplementing the parser needs to know which.
--
-- 4 A control, to prove a wrong name really does fail here: `notacolour` must draw magenta.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

local s = {
    "SCENE w=200 h=200 fit=stretch",
    "DEFS {",
    ' GL id=toClear units=bbox x1=0 y1=0 x2=1 y2=0 stops=[[0,"#2FD98A"],[1,"transparent"]]',
    "}",
    "R x=0 y=0 w=200 h=200 f=#0B1622 fea=0",

    -- 1 transparent in scene text, over white so anything opaque is obvious
    'T x=4 y=4 w=192 h=6 text="1 f=transparent  (invisible = Unity knows the name)" size=5 f=#EAF4F8',
    "R x=8 y=12 w=184 h=20 f=#EDF2F7 fea=0",
    "R x=8 y=12 w=184 h=20 f=transparent fea=0",

    -- 2 transparent as a gradient stop: a call site with no shortcut
    'T x=4 y=38 w=192 h=6 text="2 gradient stop to transparent  (must fade, not end white)" size=5 f=#EAF4F8',
    "R x=8 y=46 w=184 h=20 f=@toClear fea=0",

    -- 3 darkblue by name vs the literal it is said to be
    'T x=4 y=72 w=192 h=6 text="3 darkblue by name, then #00008B literal  (must match)" size=5 f=#EAF4F8',
    "R x=8 y=80 w=184 h=14 f=darkblue fea=0",
    "R x=8 y=96 w=184 h=14 f=#00008B fea=0",

    -- 4 control: a name that is definitely not a colour
    'T x=4 y=116 w=192 h=6 text="4 CONTROL  f=notacolour must be magenta" size=5 f=#EAF4F8',
    "R x=8 y=124 w=184 h=14 f=notacolour fea=0",
}

ui:element({
    id = "names", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = { scene = "names", src = table.concat(s, "\n") },
})
