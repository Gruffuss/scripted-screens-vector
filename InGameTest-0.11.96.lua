-- 0.11.96: nine silent failures become reported ones.
--
-- This page is DELIBERATELY FULL OF FAULTS. The magenta hatched border is the expected result,
-- not a failure. What is being tested is that `vector_stats` NAMES each one; before 0.11.96 every
-- row here drew exactly as it does now and said nothing at all.
--
-- WHAT SHOULD BE SEEN
--   the magenta problem border, and in `vector_stats` these SEVEN messages:
--     sh: shadows are a list, { dx, dy, blur, spread, colour } or a list of those
--     sh: a shadow takes dx, dy, blur, spread and a colour, 3 given
--     sh: "bleu" is not a colour
--     G: mask "fade" is not a gradient reference (must be "@fade")
--     CP "win": a clip uses one shape; 1 further shape was ignored
--     R: unknown attribute "nosuchkey"          <- this one is INSIDE the clip, which never
--                                                  reported anything before 0.11.96
--     SCENE "v96": unknown attribute "ztxt"
--
--   and NOT these, which are valid and must stay silent:
--     anything about the USE's `sh` parameter   (a symbol parameter may be named anything)
--     anything about mask=""                    (that is how a nodes patch turns a mask off)
--     anything about z_index                    (the HOST reads it; the README says to use it)
--
-- PASS   border shown, seven messages, none of the three silent cases named
-- FAIL   a message missing, or any of the three silent cases reported

local ui = ss.ui.surface("main")
ss.ui.activate("main")
ui:clear()

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

local src = table.concat({
    -- ztxt is the header typo; z_index is valid and must NOT be reported
    "SCENE w=200 h=200 fit=stretch size=5 f=#EAF4F8 ztxt=1 z_index=2",
    "DEFS {",
    "  SYM id=dot { C cx=4 cy=4 rx=4 ry=4 f=#5FD9A8 fea=0 }",
    -- two shapes in one clip: the second is ignored, and must now say so.
    -- nosuchkey is inside the clip, which reported nothing at all before 0.11.96.
    "  CP id=win { R x=0 y=0 w=90 h=60 nosuchkey=3",
    "              R x=100 y=0 w=90 h=60 }",
    "}",
    "R x=0 y=0 w=200 h=200 f=#0B1622 fea=0",
    'T x=5 y=3 w=190 h=7 size=6 text="0.11.96 -- the border is EXPECTED; read vector_stats"',

    -- 1  sh that is not a list at all
    'T x=5 y=16 w=190 h=6 size=5 f=#53646F text="1 sh not a list"',
    'R x=5 y=24 w=55 h=14 f=#5FD9A8 fea=0 sh="#0000001f"',

    -- 2  an entry one field short
    'T x=70 y=16 w=120 h=6 size=5 f=#53646F text="2 sh too short"',
    "R x=70 y=24 w=55 h=14 f=#5FD9A8 fea=0 sh=[[1,2,3]]",

    -- 3  a colour that will not parse
    'T x=135 y=16 w=60 h=6 size=5 f=#53646F text="3 sh bad colour"',
    'R x=135 y=24 w=55 h=14 f=#5FD9A8 fea=0 sh=[[1,2,3,0,"bleu"]]',

    -- 4  mask without its @
    'T x=5 y=44 w=60 h=6 size=5 f=#53646F text="4 mask without @"',
    "G mask=fade {",
    "  R x=5 y=52 w=55 h=14 f=#C98BE0 fea=0",
    "}",

    -- 5  the clip: declared above with two shapes and a typo inside
    'T x=70 y=44 w=125 h=6 size=5 f=#53646F text="5 clip: 2 shapes + a typo in it"',
    "R x=70 y=52 w=120 h=14 f=#7FB2F0 clip=win fea=0",

    -- VALID, must stay silent --------------------------------------------------------------
    'T x=5 y=74 w=190 h=6 size=5 f=#5FD9A8 text="below: valid, and must NOT be reported"',

    -- a symbol parameter that happens to be called sh
    'T x=5 y=84 w=60 h=6 size=5 f=#53646F text="USE, sh param"',
    "USE ref=dot x=5 y=92 sh=4",

    -- an empty mask: how a nodes patch turns one off
    'T x=70 y=84 w=125 h=6 size=5 f=#53646F text="mask= (empty) means no mask"',
    'G mask="" {',
    "  R x=70 y=92 w=55 h=10 f=#5FD9A8 fea=0",
    "}",

    -- a sound shadow, to prove the fix did not break the working form
    'T x=5 y=110 w=190 h=6 size=5 f=#53646F text="a sound shadow still casts"',
    'R x=5 y=118 w=55 h=14 f=#5FD9A8 fea=0 sh=[[2,2,4,0,"#000000cc"]]',

    'T x=5 y=145 w=190 h=20 size=4 f=#53646F wrap=1 text="Every row above drew exactly like this before 0.11.96. The difference is entirely in what vector_stats says."',
}, "\n")

ui:element({
    id = "v96", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = { scene = "v96", src = src },
})

ui:commit()
