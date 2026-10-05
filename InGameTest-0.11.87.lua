-- In-game verification for 0.11.86 and 0.11.87.
--
-- Each row FAILS VISIBLY on the version before it, so the page says which build is loaded:
--
--   1  60 nested groups        0.11.85 refuses the whole scene ("nesting too deep")
--   2  o=0 with a click        0.11.85/86 never registers it (the fold ran on an empty child
--                              list, so the transparent group was skipped whole)
--   3  v=0 with a click        must still take the click region with it -- the counterpart
--   4  %x                      0.11.85/86 prints the `missing` text instead of a number
--   5  colour expressions      the hover and down BRANCHES, never checked on a console
--
-- Row 1 is the loudest: on 0.11.85 there is no scene at all, only the parse error.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

-- Lets the MCP tooling drive the pointer, so rows 2, 3 and 5 can be tested without a player
-- standing at the console. A test page only -- no shipped example turns this on.
ss.ui.allow_mcp_automation(true)

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

-- 60 levels of group, far past the 32 that 0.11.85 allowed. Each nudges by a fixed amount, so
-- the box lands at 60 x (0.5, 0.2) plus the outer offset. If the nesting were being dropped
-- rather than applied, it would sit at the outer offset instead -- the position is the proof,
-- not merely the fact that something drew.
local deep = {}
for _ = 1, 60 do
    deep[#deep + 1] = "G t=[0.5,0.2] {"
end
deep[#deep + 1] = 'R x=0 y=0 w=46 h=16 rx=2 f=#5FD9A8 fea=0'
deep[#deep + 1] = 'T x=0 y=4 w=46 h=10 align=center size=8 f=#0B1622 text="60 deep"'
for _ = 1, 60 do
    deep[#deep + 1] = "}"
end

local src = table.concat({
    "SCENE w=200 h=200 fit=stretch size=5 f=#EAF4F8",
    "R x=0 y=0 w=200 h=200 f=#0B1622 fea=0",
    'T x=6 y=4 w=188 h=6 text="0.11.87 verification -- all five rows must pass"',

    -- 1  DEEP NESTING. 60 x (0.5, 0.2) = (30, 12), plus this group's own (6, 12), so the
    --    box lands at (36, 24) and occupies y 24..40 -- clear of row 2's label at y 44.
    'T x=6 y=13 w=188 h=6 f=#8FA6B8 text="1  60 nested groups (0.11.85 refuses the scene)"',
    "G t=[6,12] {",
    table.concat(deep, "\n"),
    "}",

    -- 2  o=0 KEEPS ITS CLICK REGION
    'T x=6 y=44 w=188 h=6 f=#8FA6B8 text="2  o=0: invisible, still clickable"',
    "R x=6 y=52 w=90 h=18 rx=3 f=#16222E s=#3A4A5A sw=1 fea=0",
    'T x=6 y=56 w=90 h=8 align=center size=5 f=#53646F text="aim here"',
    "G o=0 {",
    "  R id=ghost press=1 x=6 y=52 w=90 h=18 rx=3 f=#FF0000 fea=0",
    "}",

    -- 3  v=0 TAKES ITS CLICK REGION WITH IT
    'T x=104 y=44 w=90 h=6 f=#8FA6B8 text="3  v=0: must NOT fire"',
    "R x=104 y=52 w=90 h=18 rx=3 f=#16222E s=#3A4A5A sw=1 fea=0",
    'T x=104 y=56 w=90 h=8 align=center size=5 f=#53646F text="aim here too"',
    "G v=0 {",
    "  R id=gone press=1 x=104 y=52 w=90 h=18 rx=3 f=#0000FF fea=0",
    "}",

    -- 4  %x   255 must read ff. `missing` is deliberately loud so a value that never arrives
    --         cannot be mistaken for a formatting failure.
    'T x=6 y=76 w=188 h=6 f=#8FA6B8 text="4  %x of 255 must read ff"',
    'T x=6 y=84 w=50 h=10 size=8 f=#F5D76E text=$hex fmt=%x missing="NO DATA"',
    'T x=60 y=86 w=134 h=8 size=5 f=#53646F text="hex    (plain: "',
    'T x=112 y=86 w=40 h=8 size=5 f=#53646F text=$hex missing="NO DATA"',
    'T x=150 y=86 w=20 h=8 size=5 f=#53646F text=")"',

    -- 5  COLOUR EXPRESSION BRANCHES
    'T x=6 y=100 w=188 h=6 f=#8FA6B8 text="5  colour expressions: idle / hover / press"',
    "G id=c1 {",
    '  R id=c1hit press=1 x=6 y=108 w=58 h=22 rx=4 fea=0 f="=if(down,#2E8B6E,if(hover,#6FE3B6,#5FD9A8))"',
    '  T x=6 y=115 w=58 h=8 align=center size=5 f=#0B1622 text="if()"',
    "}",
    "G id=c2 {",
    '  R id=c2hit press=1 x=70 y=108 w=58 h=22 rx=4 fea=0 f="=mix(#24405A,#6FE3B6,hover)"',
    '  T x=70 y=115 w=58 h=8 align=center size=5 f=#EAF4F8 text="mix()"',
    "}",
    "G id=c3 {",
    --   A nested mix, so the converted walk is exercised rather than a single level.
    '  R id=c3hit press=1 x=134 y=108 w=60 h=22 rx=4 fea=0 f="=mix(mix(#C98BE0,#F5D76E,hover),#24405A,down)"',
    '  T x=134 y=115 w=60 h=8 align=center size=5 f=#0B1622 text="mix(mix())"',
    "}",

    'T x=6 y=138 w=188 h=6 f=#8FA6B8 text="last event:"',
    'T x=6 y=146 w=188 h=18 size=6 f=#5FD9A8 text=$last wrap=1 missing="(nothing yet)"',
    'T x=6 y=170 w=188 h=24 size=6 f=#F5D76E wrap=1 text=$verdict missing="aim at boxes 2 and 3"',
}, "\n")

local data

ui:element({
    id = "v87_s", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = { scene = "v87", src = src },

    on_click = function(value)
        local values = { last = value }

        -- `press=1` also sends up: and leave:; only the down matters here.
        local id = value:match("^down:(.+)$") or value

        if id == "ghost" then
            values.sawGhost = 1
        elseif id == "gone" then
            values.sawGone = 1
        end

        if id == "gone" then
            values.verdict = "FAIL row 3: v=0 fired -- it must take its click region with it"
        elseif id == "ghost" then
            values.verdict = "PASS row 2: o=0 kept its click region (the 0.11.87 fix)"
        end

        data:set_props({ scene = "v87", keep = 1, snap = 1, data = values })
        ui:commit()
    end,
})

-- The data element, as the examples do it: same scene, tiny rect, `keep = 1` so each payload
-- is a patch rather than a replacement.
data = ui:element({
    id = "v87_d", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
    props = { scene = "v87", keep = 1, data = { hex = 255 } },
})

ui:commit()
