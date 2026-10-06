-- 0.11.105, page 1 of 2: THE REPORTS. Every wrong thing here now names itself, and every
-- right thing beside it stays silent.
--
-- MOST of this release's thirteen faults were a SILENCE: a value the parser dropped, or read
-- and never used, with an empty problem list beside it. So this page is read off `vector_stats`
-- and the magenta problem border, not off the pixels -- almost nothing on it draws differently
-- from 0.11.104. Page 2 (`InGameTest-0.11.105-b.lua`) carries the rows where pixels or values
-- change.
--
-- EVERY ROW IS A PAIR: the deliberate mistake sits beside the valid twin that must NOT report.
-- A problem list is deduplicated and capped at 16 per scene, so each pair lives on its own
-- element and each element's list is "exactly these strings and nothing else". A twin that
-- started reporting would lengthen its own element's list, which is how these controls fail.
--
-- AT A GLANCE: on 0.11.105 SEVEN of the eight elements carry the magenta border. On 0.11.104
-- only A6 did. If this page comes up with one border, an old build is loaded.
--
-- WHAT SHOULD BE SEEN
--
--   A1  o OFF A GROUP -- 4 reports. `o` is group opacity and only G, USE, SC and IMG read it.
--       Top row: a green rect, a gradient rect and a green label, all three carrying o=0.5,
--       plus o=0.5 on the scene header. All draw at FULL strength -- `o` never did anything
--       here, which is the point -- and all four are reported.
--       Bottom row: the gold rect inside `G o=0.5` and the gold rect inside `SC o=0.5` ARE
--       half faded, and neither is reported.
--       vector_stats, exactly four, in this order:
--         SCENE "opacity": o is group opacity, read on G, USE, SC and IMG only; fo fades a
--            fill and so a stroke, or put this inside a G
--         GL "ramp": o is group opacity, ...
--         R "rect": o is group opacity, ...
--         T "lbl": o is group opacity, ...
--
--   A2  AN = EXPRESSION OR A $ BINDING IN A LITERAL-ONLY SLOT -- 4 reports. Thirty-odd
--       attributes are read by literal-only readers; twenty-four had no other check, so a
--       computed value was dropped with an EMPTY list.
--       Nothing drawn changes. The red squares and the three green/blue shapes below them look
--       the same on both builds; only the silence is gone.
--       vector_stats, exactly four, in this order:
--         RP "rep": n "=2" is read as a plain number and is not evaluated; write a number
--         R "ghost": click "=2" is read as a plain number ...
--         Y "poly": p "$pts" is read as a plain number ...
--         G "mat": m "=2" is read as a plain number ...
--       The twins that must stay silent: `x="=0*t+2"` (evaluated), a live gradient `x1`, and a
--       `n="=9+0*t"` passed to a USE as a symbol parameter.
--
--   A3  A CLICK KEY WITH NO id -- 2 reports. All four RecordHit sites skip a node with no id,
--       so the shape drew, registered no hit region and answered nothing.
--       All three carry `f="=if(down,...)"`, so a shape that DID register a hit would gold
--       while held. PRESS EACH OF THE THREE:
--         dark red rect (click=1, no id)  -> NOTHING happens: no hit region, no answer
--         red "T press" label (press=1)   -> the same
--         green "id=btn" rect             -> golds while held, and is silent in the list
--       vector_stats, exactly two:
--         R: click (or press, xy, hoverev, drag, drop) needs an id; the id is the value
--            on_click receives
--         T: click (or press, xy, hoverev, drag, drop) needs an id; ...
--       The blue bar at the right is a USE whose symbol PARAMETER is called `click`. That is
--       not a fault and must not be reported.
--
--   A4  A C WITH NO USABLE RADIUS -- 3 reports. Either radius at or below 0 draws nothing at
--       all, not even a counted shape, and nothing said so.
--       Top row: three EMPTY places -- `ry` with no `rx`, `rx=-6`, `ry=-6`.
--       Bottom row: `rx=6` and `rx="=6+0*t"` draw green dots; `rx=0` and a bare `C` with no
--       radius at all draw NOTHING and are SILENT (a zero datum writes exactly `rx=0`, and a
--       radius may arrive by patch).
--       vector_stats, exactly three:
--         C "dot": rx is missing, so nothing is drawn
--         C "neg": rx is negative, so nothing is drawn
--         C "negy": ry is negative, so nothing is drawn
--
--   A5  A UNIT WITH NO VALUE, AND A PLACEHOLDER NEVER CLOSED -- 2 reports. Both were silent,
--       and both still draw exactly what they drew.
--         red "OK" with unit=" kPa"   -> draws OK (no kPa), reported
--         red "a {$press"             -> draws `a {$press`, reported
--         green "Reactor" under G unit=" kPa"  -> silent: the unit was handed down
--         green "p {=1+2} k" with unit=" kPa"  -> draws `p 3 k kPa`, silent
--         the empty label bottom left (unit, no text)  -> silent
--       vector_stats, exactly two:
--         T: unit " kPa" follows a value, and this label prints no value, so nothing prints
--            the unit
--         T: text "{$press" opens a placeholder that is never closed, so it prints itself
--            instead of a value
--
--   A6  A PATCH RE-READS ONLY WHAT ITS AUTHOR WROTE -- 1 report, and this is the one element
--       that was bordered on 0.11.104 too, with TWO.
--       A node is re-parsed from its own props MERGED with its group's defaults, and the patch
--       path handed that merged map to the checks that ask "did the author write this key
--       HERE". So after ANY nodes patch an inherited key read as one the author had written.
--         green "inherited" inside `G fit=cover`, patched w=90  -> SILENT
--         red "in the patch", patched align=centerr             -> reported
--       vector_stats, exactly one:
--         T: align "centerr" is not left, center, centre, right, justified or justify
--       On 0.11.104 the list also held `T: fit "cover" is not none, ellipsis or shrink`, and
--       a problem list is never cleared, so that border stayed for the life of the scene.
--
--   A7  A NODES TABLE BUILT AS A LIST -- 1 report. Its payload is `{ { f = green } }`: a plain
--       Lua list, which carries no ids, never reached a node and was dropped without a word.
--       Recovered, its entries are looked up under the names 1 upwards, and this scene has no
--       node called 1, so THE SQUARE STAYS GREY on both builds and only the report differs.
--       vector_stats, exactly one:
--         patch for unknown node id "1"; the nodes table arrived as a list, so its patches
--         were named 1 upwards -- a patch is keyed by node id
--
--   A8  AN INHERITED so IS NOT A SCROLL JUMP -- 0 reports, and the ONLY element on this page
--       with no border. `so` is stroke opacity on every op but an `SC`, where it is a scroll
--       offset applied once per new `sov`. The `G so=30` here hands 30 down; nothing on the
--       container asked to scroll.
--       The list is patched `ch` on the first tick. ROW 1 MUST STAY AT THE TOP of the window.
--       On 0.11.104 the patch made the inherited 30 read as the container's own and the list
--       jumped by 30 -- row 1 slid out of the window and row 2 took its place.
--       Do not spin the mouse wheel over it before reading it: a wheel notch is the player's
--       own scroll and moves the list legitimately.
--
-- NOT ON THIS PAGE
--
--   `nofill` missing the radial-gradient fill and the flat fill under a `blur` is a MEASURING
--   switch: no shipped page, example or test scene sets it, and nothing on a console looks
--   different either way. It was measured offline only -- 18,265 / 1,361 / 1,361 vertices
--   under `nofill = 1` before, 0 after, flat-fill controls unmoved -- and it has no permanent
--   regression check, because the case needs colour literals and the headless suite cannot
--   parse those. A row here would be a row that cannot fail, so there is none.
--
--   The escaped brace, the booleans, the numeric-keyed patch, the sampled paint and the lazy
--   deep `if` are on page 2, with the ablation-switch leak.
--
-- PASS   seven magenta borders (A1-A7), none on A8; each element's vector_stats list is
--        exactly the strings above; row 1 of A8's list is at the top of its window; A3's
--        green button answers a click and the two shapes above it answer nothing
-- FAIL   any element with a longer or shorter list than the one named for it, a border on A8,
--        a red "OK"/"a {$press" that has changed what it draws, or row 2 at the top of A8

local ui = ss.ui.surface("main")
ss.ui.activate("main")
ui:clear()

-- A 2 x 4 grid on a 684 x 460 console. Each cell holds a 165 x 54 viewbox, so one scene unit
-- is two pixels and a size=3.5 line is seven pixels tall.
local function cell(col, row)
    return { unit = "px", x = 6 + col * 342, y = 6 + row * 112, w = 330, h = 108 }
end

-- A1 -------------------------------------------------------------------------------------
ui:element({
    id = "opacity", type = "vector",
    rect = cell(0, 0),
    props = { scene = "opacity", src = table.concat({
        "SCENE w=165 h=54 fit=stretch size=3.5 f=#EAF4F8 o=0.5",
        "R x=0 y=0 w=165 h=54 f=#0B1622 fea=0",
        "DEFS {",
        " GL id=ramp x1=0 y1=0 x2=1 y2=0 units=bbox stops=[[0,#5FD9A8],[1,#2E8B6E]] o=0.5",
        "}",
        'T x=2 y=1 w=161 h=5 size=4 text="A1  o off a group: 4 reports"',

        "R id=rect x=2 y=9 w=20 h=11 f=#5FD9A8 fea=0 o=0.5",
        "R x=24 y=9 w=20 h=11 f=@ramp fea=0",
        'T id=lbl x=46 y=10 w=38 h=6 size=4 f=#5FD9A8 text="o on a T" o=0.5',
        'T x=2 y=22 w=161 h=4 size=3 text="those three and the header carry o=0.5 and"',
        'T x=2 y=26 w=161 h=4 size=3 text="draw at FULL strength: all four reported"',

        "G o=0.5 { R x=2 y=32 w=20 h=11 f=#F5D76E fea=0 }",
        "SC id=win x=24 y=32 w=20 h=11 ch=30 o=0.5 {",
        " R x=24 y=32 w=20 h=24 f=#F5D76E fea=0",
        "}",
        'T x=46 y=33 w=117 h=4 size=3 text="G and SC read o: faded, silent"',
        'T x=46 y=38 w=117 h=4 size=3 text="(the SC fades its own child)"',

        'T x=2 y=46 w=161 h=4 size=3 f=#53646F text="0.11.104 said nothing about any of the"',
        'T x=2 y=50 w=161 h=4 size=3 f=#53646F text="four and drew exactly this picture"',
    }, "\n") },
})

-- A2 -------------------------------------------------------------------------------------
ui:element({
    id = "literal", type = "vector",
    rect = cell(1, 0),
    props = { scene = "literal", src = table.concat({
        "SCENE w=165 h=54 fit=stretch size=3.5 f=#EAF4F8",
        "R x=0 y=0 w=165 h=54 f=#0B1622 fea=0",
        "DEFS {",
        " GL id=live x1=\"=0*t\" y1=0 x2=1 y2=0 units=bbox stops=[[0,#5FD9A8],[1,#2E8B6E]]",
        " SYM id=tag n=18 { R x=0 y=0 w=%n h=9 f=#7FB2F0 fea=0 }",
        "}",
        'T x=2 y=1 w=161 h=5 size=4 text="A2  = in a literal-only slot: 4"',
        'T x=2 y=8 w=161 h=4 size=3 text="RP n, click, Y p and one entry of G m: each"',
        'T x=2 y=12 w=161 h=4 size=3 text="one dropped, and each one now reported"',

        'RP id=rep n="=2" { R x=2 y=18 w=9 h=9 f=#E23D3D fea=0 }',
        'R id=ghost x=13 y=18 w=9 h=9 f=#E23D3D fea=0 click="=2"',
        "Y id=poly p=$pts f=#E23D3D",
        'G id=mat m=["=2",0,0,1,0,0] { R x=35 y=18 w=9 h=9 f=#E23D3D fea=0 }',
        'T x=48 y=20 w=115 h=4 size=3 text="red: where a value was dropped"',

        'R x="=0*t+2" y=30 w=9 h=9 f=#5FD9A8 fea=0',
        "R x=13 y=30 w=9 h=9 f=@live fea=0",
        'USE ref=tag n="=9+0*t" x=24 y=30',
        'T x=48 y=30 w=115 h=4 size=3 text="an x, a live gradient and a"',
        'T x=48 y=34 w=115 h=4 size=3 text="USE parameter: all evaluate,"',
        'T x=48 y=38 w=115 h=4 size=3 text="and all three stay silent"',

        'T x=2 y=47 w=161 h=4 size=3 f=#53646F text="the drawing is unchanged; only the silence went"',
    }, "\n") },
})

-- A3 -------------------------------------------------------------------------------------
ui:element({
    id = "clickid", type = "vector",
    rect = cell(0, 1),
    props = { scene = "clickid", src = table.concat({
        "SCENE w=165 h=54 fit=stretch size=3.5 f=#EAF4F8",
        "R x=0 y=0 w=165 h=54 f=#0B1622 fea=0",
        "DEFS { SYM id=knob click=20 { R x=0 y=0 w=%click h=9 f=#7FB2F0 fea=0 } }",
        'T x=2 y=1 w=161 h=5 size=4 text="A3  a click key with no id: 2"',

        'R x=2 y=9 w=34 h=11 f="=if(down,#F5D76E,#8C2B2B)" fea=0 click=1',
        'T x=38 y=10 w=125 h=4 size=3 text="R click=1, no id: it draws,"',
        'T x=38 y=14 w=125 h=4 size=3 text="and takes nothing"',

        'T x=2 y=22 w=34 h=6 size=4.5 f="=if(down,#F5D76E,#E23D3D)" press=1 text="T press"',
        'T x=38 y=23 w=125 h=4 size=3 text="T press=1, no id: the same"',

        'R id=btn x=2 y=32 w=34 h=11 f="=if(down,#F5D76E,#5FD9A8)" fea=0 click=1',
        'T x=38 y=33 w=78 h=4 size=3 text="id=btn: silent, and it"',
        'T x=38 y=37 w=78 h=4 size=3 text="DOES answer -- gold held"',
        "USE ref=knob click=20 x=120 y=34",

        'T x=2 y=47 w=161 h=4 size=3 f=#53646F text="the blue bar is a USE parameter named click"',
    }, "\n") },
})

-- A4 -------------------------------------------------------------------------------------
ui:element({
    id = "radius", type = "vector",
    rect = cell(1, 1),
    props = { scene = "radius", src = table.concat({
        "SCENE w=165 h=54 fit=stretch size=3.5 f=#EAF4F8",
        "R x=0 y=0 w=165 h=54 f=#0B1622 fea=0",
        'T x=2 y=1 w=161 h=5 size=4 text="A4  a C with no usable radius: 3"',

        "C id=dot cx=10 cy=14 ry=6 f=#E23D3D",
        "C id=neg cx=26 cy=14 rx=-6 f=#E23D3D",
        "C id=negy cx=42 cy=14 rx=6 ry=-6 f=#E23D3D",
        'T x=52 y=10 w=111 h=4 size=3 text="ry alone, rx=-6, ry=-6: three"',
        'T x=52 y=14 w=111 h=4 size=3 text="EMPTY places here, all said"',

        "C cx=10 cy=32 rx=6 f=#5FD9A8",
        "C cx=26 cy=32 rx=0 f=#5FD9A8",
        'C cx=42 cy=32 rx="=6+0*t" f=#5FD9A8',
        "C id=later cx=58 cy=32 f=#F5D76E",
        'T x=70 y=28 w=93 h=4 size=3 text="rx=6, rx=0, an expression"',
        'T x=70 y=32 w=93 h=4 size=3 text="and a bare C: four silent,"',
        'T x=70 y=36 w=93 h=4 size=3 text="and only two of them drawn"',

        'T x=2 y=47 w=161 h=4 size=3 f=#53646F text="rx=0 and the bare C draw nothing on purpose"',
    }, "\n") },
})

-- A5 -------------------------------------------------------------------------------------
ui:element({
    id = "label", type = "vector",
    rect = cell(0, 2),
    props = { scene = "label", src = table.concat({
        "SCENE w=165 h=54 fit=stretch size=3.5 f=#EAF4F8",
        "R x=0 y=0 w=165 h=54 f=#0B1622 fea=0",
        'T x=2 y=1 w=161 h=5 size=4 text="A5  a unit with no value, and a"',
        'T x=2 y=6 w=161 h=5 size=4 text="placeholder never closed: 2"',

        'T x=2 y=14 w=48 h=6 size=4.5 f=#E23D3D text="OK" unit=" kPa"',
        'T x=52 y=15 w=111 h=4 size=3 text="draws OK; the unit is said"',

        'T x=2 y=22 w=60 h=6 size=4.5 f=#E23D3D text="a {$press"',
        'T x=64 y=23 w=99 h=4 size=3 text="draws its own text, said"',

        'G unit=" kPa" { T x=2 y=30 w=48 h=6 size=4.5 f=#5FD9A8 text="Reactor" }',
        'T x=52 y=31 w=111 h=4 size=3 text="a unit from a G: silent"',

        'T x=2 y=38 w=60 h=6 size=4.5 f=#5FD9A8 text="p {=1+2} k" unit=" kPa"',
        'T x=64 y=39 w=99 h=4 size=3 text="a placeholder takes it: silent"',

        'T id=empty x=2 y=47 w=20 h=4 unit=" kPa"',
        'T x=24 y=47 w=139 h=4 size=3 f=#53646F text="a label with no text at all: silent"',
    }, "\n") },
})

-- A6 -------------------------------------------------------------------------------------
ui:element({
    id = "patch", type = "vector",
    rect = cell(1, 2),
    props = { scene = "patch", src = table.concat({
        "SCENE w=165 h=54 fit=stretch size=3.5 f=#EAF4F8",
        "R x=0 y=0 w=165 h=54 f=#0B1622 fea=0",
        'T x=2 y=1 w=161 h=5 size=4 text="A6  a patch re-reads only what"',
        'T x=2 y=6 w=161 h=5 size=4 text="its author wrote HERE: 1"',

        'G fit=cover { T id=lbl x=2 y=14 w=58 h=7 size=4.5 f=#5FD9A8 text="inherited" }',
        'T x=62 y=14 w=101 h=4 size=3 text="a G hands fit=cover down."',
        'T x=62 y=18 w=101 h=4 size=3 text="a T cannot take cover, but"',
        'T x=62 y=22 w=101 h=4 size=3 text="its author wrote none, so"',
        'T x=62 y=26 w=101 h=4 size=3 text="the patch is SILENT"',

        'T id=typo x=2 y=34 w=58 h=7 size=4.5 f=#E23D3D text="in the patch"',
        'T x=62 y=34 w=101 h=4 size=3 text="patched align=centerr, a"',
        'T x=62 y=38 w=101 h=4 size=3 text="word its author did mistype"',

        'T x=2 y=47 w=161 h=4 size=3 f=#53646F text="0.11.104 reported the inherited fit as well"',
    }, "\n") },
})

-- A7 -------------------------------------------------------------------------------------
ui:element({
    id = "list", type = "vector",
    rect = cell(0, 3),
    props = { scene = "list", src = table.concat({
        "SCENE w=165 h=54 fit=stretch size=3.5 f=#EAF4F8",
        "R x=0 y=0 w=165 h=54 f=#0B1622 fea=0",
        'T x=2 y=1 w=161 h=5 size=4 text="A7  a nodes table built as a LIST: 1"',

        "R id=mark x=2 y=10 w=26 h=14 f=#53646F fea=0",
        'T x=32 y=11 w=131 h=4 size=3 text="the payload is { { f = green } }: a"',
        'T x=32 y=15 w=131 h=4 size=3 text="list, carrying no ids at all"',

        'T x=2 y=28 w=161 h=4 size=3 text="a list is looked up as 1, 2 ... and this"',
        'T x=2 y=32 w=161 h=4 size=3 text="scene has no node called 1, so the square"',
        'T x=2 y=36 w=161 h=4 size=3 text="STAYS GREY -- and now says why"',

        'T x=2 y=47 w=161 h=4 size=3 f=#53646F text="0.11.104 dropped the whole table silently"',
    }, "\n") },
})

-- A8 -------------------------------------------------------------------------------------
ui:element({
    id = "scroll", type = "vector",
    rect = cell(1, 3),
    props = { scene = "scroll", src = table.concat({
        "SCENE w=165 h=54 fit=stretch size=3.5 f=#EAF4F8",
        "R x=0 y=0 w=165 h=54 f=#0B1622 fea=0",
        'T x=2 y=1 w=161 h=5 size=4 text="A8  an inherited so is no jump"',

        "G so=30 {",
        " SC id=rows x=2 y=9 w=66 h=42 ch=130 sov=1 {",
        "  RP n=6 { R x=2 y=\"=9+i*22\" w=66 h=20 f=#2A3C50 fea=0 }",
        "  RP n=6 { T x=5 y=\"=14+i*22\" w=60 h=8 size=6 f=#5FD9A8 text=\"row {=i+1}\" }",
        " }",
        "}",

        'T x=72 y=9 w=91 h=4 size=3 text="so is stroke opacity on a"',
        'T x=72 y=13 w=91 h=4 size=3 text="G and a scroll offset on"',
        'T x=72 y=17 w=91 h=4 size=3 text="an SC, so the G hands 30"',
        'T x=72 y=21 w=91 h=4 size=3 text="down. nothing on the"',
        'T x=72 y=25 w=91 h=4 size=3 text="container asked to scroll."',
        'T x=72 y=32 w=91 h=4 size=3 text="patched ch=150 on tick 1:"',
        'T x=72 y=36 w=91 h=4 size=3 text="ROW 1 STAYS AT THE TOP"',
        'T x=72 y=44 w=91 h=4 size=3 f=#53646F text="0.11.104 jumped by 30 and"',
        'T x=72 y=48 w=91 h=4 size=3 f=#53646F text="showed row 2 first"',
    }, "\n") },
})

ui:commit()

-- The payloads. Declared in a LATER chip execution than the structures above: declaring an
-- element and patching it inside ONE execution loses the declaration's data, and a `nodes`
-- patch is read against the structure that is already parsed.
local sent = false

function tick()
    if sent then
        return
    end

    sent = true

    -- A6. Two patches in one table: `lbl` is the node whose group handed it `fit`, and `typo`
    -- carries a word its author really did mistype. Both keys are WORDS: a table keyed only by
    -- numbers is sent as a list, which is page 2's row B3.
    ui:element({
        id = "patch-data", type = "vector",
        rect = { unit = "px", x = 0, y = 0, w = 1, h = 1 },
        props = { scene = "patch", nodes = {
            lbl = { w = 90 },
            typo = { align = "centerr" },
        } },
    })

    -- A7. A plain Lua list: no ids, so nothing can be found under a node id.
    ui:element({
        id = "list-data", type = "vector",
        rect = { unit = "px", x = 1, y = 0, w = 1, h = 1 },
        props = { scene = "list", nodes = { { f = "#5FD9A8" } } },
    })

    -- A8. A patch naming neither `so` nor `sov`. That is the whole point: it must not move the
    -- list, and on 0.11.104 it moved it by the 30 the enclosing G had handed down.
    ui:element({
        id = "scroll-data", type = "vector",
        rect = { unit = "px", x = 2, y = 0, w = 1, h = 1 },
        props = { scene = "scroll", nodes = { rows = { ch = 150 } } },
    })

    ui:commit()
end
