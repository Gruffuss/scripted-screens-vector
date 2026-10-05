-- The ten bugs fixed in 0.11.94, one row each, each beside its own control.
--
-- A row is right when its two halves MATCH; nothing has to be remembered about the old build.
-- The left half is the form that was broken, the right half the form that always worked.
--
--   1  weight       "700" and "bold" in the same heavy weight. Was: the left one thin.
--   2  percent      "50%" on both sides, not "50%%".
--   3  hex          "ff" on both sides. Was: the left one "fe".
--   4  P shadow     two stroked boxes, the same shadow under each. Was: the left one had none.
--   5  gray         two RED bars. Was: the left one fully grey, from a value already reported
--                   as broken.
--   6  blur         two equally soft blobs. Was: the left one sharp.
--   7  array        read in `vector_stats`, not on the screen: it must list BOTH `exprname`
--                   and `labelname` as unresolved. Was: only `labelname`, because a label
--                   always reported an absent name and an expression never did. The left
--                   label draws "0" and the right one "--"; that part is unchanged.
--   8  gradient     two bars running white to dark over their full width. Was: the left one
--                   flat white, its ramp axis collapsed to no length.
--   9  defs typo    `vector_stats` naming `GL "bad": unknown attribute "nosuchkey"`. Was:
--                   silence.
--  10  compact d    two identical arcs and two identical triangles. Was: the left arc drew
--                   nothing and the left triangle's corner sat in the wrong place.
--
-- PROBLEMS ARE EXPECTED HERE: exactly two, the `gray=` value and the defs typo, so the magenta
-- hatched border is correct. A third problem is a defect.

local ui = ss.ui.surface("main")
ss.ui.activate("main")
ui:clear()

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:element({
    id = "v94", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = { scene = "v94", src = table.concat({
        "SCENE w=200 h=200 fit=stretch size=5 f=#EAF4F8",
        "DEFS {",
        "  GL id=good units=bbox x1=0 y1=0 x2=1 y2=1 stops=[[0,#FFFFFF],[1,#101418]]",
        "  GL id=bad units=bbox x1=0 y1=0 x2=1 y2==nosuchfn(1) nosuchkey=3 stops=[[0,#FFFFFF],[1,#101418]]",
        "}",
        "R x=0 y=0 w=200 h=200 f=#0B1622 fea=0",
        'T x=5 y=3 w=190 h=7 size=6 text="0.11.94 -- each row: the fixed form, then its control"',

        -- 1  a numeric weight is read, not only the word
        'T x=5 y=14 w=22 h=6 size=5 f=#53646F text="1 weight"',
        'T x=40 y=13 w=60 h=9 size=8 text="w=700" weight=700',
        'T x=110 y=13 w=60 h=9 size=8 text="w=bold" weight=bold',

        -- 2  %% collapses to one percent sign
        'T x=5 y=26 w=30 h=6 size=5 f=#53646F text="2 percent"',
        'T x=40 y=25 w=60 h=9 size=8 text="{=50:%.0f%%}"',
        'T x=110 y=25 w=60 h=9 size=8 text="50%"',

        -- 3  {$n:%x} rounds, the way fmt="%x" always did
        'T x=5 y=38 w=22 h=6 size=5 f=#53646F text="3 hex"',
        'T x=40 y=37 w=60 h=9 size=8 text="{=254.6:%x}"',
        'T x=110 y=37 w=60 h=9 size=8 text=ff',

        -- 4  a P carrying sh but no f casts its shadow, as every other shape does
        'T x=5 y=50 w=30 h=6 size=5 f=#53646F text="4 P shadow"',
        'P d="M40 52 L95 52 L95 70 L40 70 Z" f=none s=#5FD9A8 sw=1 sh=[[1,2,3,0,"#000000CC"]]',
        "Y p=[110,52,165,52,165,70,110,70] f=none s=#5FD9A8 sw=1 sh=[[1,2,3,0,\"#000000CC\"]]",

        -- 5  a broken filter value falls back to that filter's identity, not to full effect
        'T x=5 y=76 w=22 h=6 size=5 f=#53646F text="5 gray"',
        "G gray==nosuchfn(1) {",
        "  R x=40 y=75 w=55 h=12 f=#E23D3D fea=0",
        "}",
        "R x=110 y=75 w=55 h=12 f=#E23D3D fea=0",

        -- 6  an enclosing group's blur reaches inside a scroll container
        'T x=5 y=94 w=22 h=6 size=5 f=#53646F text="6 blur"',
        "G blur=3 {",
        "  SC id=box x=40 y=92 w=55 h=16 ch=32 {",
        "    C cx=67 cy=100 rx=14 ry=6 f=#C98BE0 fea=0",
        "  }",
        "}",
        "G blur=3 {",
        "  C cx=137 cy=100 rx=14 ry=6 f=#C98BE0 fea=0",
        "}",

        -- 7  an absent array read by an EXPRESSION is reported, as a label's always was
        'T x=5 y=114 w=22 h=6 size=5 f=#53646F text="7 array"',
        'T x=40 y=113 w=60 h=9 size=8 text="{=$exprname[0]:%.0f}"',
        'T x=110 y=113 w=60 h=9 size=8 text="$labelname[0]"',

        -- 8  a malformed gradient number falls back to that key's own default
        'T x=5 y=128 w=30 h=6 size=5 f=#53646F text="8 gradient"',
        "R x=40 y=126 w=55 h=12 f=@bad fea=0",
        "R x=110 y=126 w=55 h=12 f=@good fea=0",

        -- 9  the stray key on `bad` above is reported
        'T x=5 y=145 w=190 h=6 size=5 f=#53646F text="9 defs typo: vector_stats must name GL \\"bad\\" nosuchkey"',

        -- 10  minified path numbers: a second dot ends one, and an arc flag is one character
        'T x=5 y=156 w=30 h=6 size=5 f=#53646F text="10 compact d"',
        'P d="M45 160 a10 10 0 011 20" f=none s=#5FD9A8 sw=1.5',
        'P d="M115 160 a10 10 0 1 1 1 20" f=none s=#5FD9A8 sw=1.5',
        'P d="M45 184 l30.5.5 l-30 10 Z" f=none s=#7FB2F0 sw=1',
        'P d="M115 184 l30.5 0.5 l-30 10 Z" f=none s=#7FB2F0 sw=1',

        'T x=5 y=194 w=190 h=5 size=4 f=#53646F text="two problems expected: the gray= value and the defs typo"',
    }, "\n") },
})

ui:commit()
