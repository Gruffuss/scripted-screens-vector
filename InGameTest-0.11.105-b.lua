-- 0.11.105, page 2 of 2: THE PIXELS AND THE VALUES. Page 1 is read off `vector_stats`; every
-- row here changes what is DRAWN or what a name resolves to, so most of it reads off one
-- capture. Page 1 is `InGameTest-0.11.105.lua`: eight elements, seven of which must report.
--
-- Each row still pairs the deliberate mistake with the valid twin beside it.
--
-- WHAT SHOULD BE SEEN
--
--   B1  AN ESCAPED BRACE INSIDE A PLACEHOLDER. 0.11.103 found a placeholder's closing `}` by
--       COUNTING braces, which reads `{{` as two opens, so `{$v:x{{y}` never terminated: the
--       whole label stayed literal text and said nothing, while the same spec as a node `fmt`
--       drew `x{y` and WAS reported.
--         green  "<{$v:x{{y}>"        -> draws <x{y>      (0.11.104 drew <{$v:x{{y}>)
--         blue   text=$v fmt="y{{z"   -> draws y{z        (the same on both builds)
--         grey   "{$v}}"              -> draws 1.25}      unchanged, and silent
--         grey   "{{{$v}}}"           -> draws {{1.25}}   unchanged, and silent
--       The two grey rows are the narrowing: a `}` straight after a closer is a brace the
--       LABEL wanted, not an escape inside the spec. They read the same on both builds on
--       purpose -- they are there to catch the fix over-reaching.
--       vector_stats: exactly two, one per route, and they are DIFFERENT specs so neither can
--       stand in for the other --
--         T: fmt "x{{y" holds no conversion, so it prints itself instead of the number
--         T: fmt "y{{z" holds no conversion, so it prints itself instead of the number
--       On 0.11.104 only the `y{{z` one was listed.
--
--   B2  true AND false AS WORDS. 0.11.102 made a LUA boolean read as 1 and 0 wherever a number
--       is read and left the expression grammar behind, where `false` was an identifier: so
--       `R v=false` fell back to `v`'s default of 1 and drew VISIBLE, the opposite of what its
--       author wrote. Five squares, left to right:
--         v=false  -> GONE        (0.11.104 drew it, in red)
--         v=true   -> green, there
--         v=0      -> GONE        (worked before; the control for the pair above)
--         v=1      -> green, there
--         v=False  -> GOLD, there, and reported: lower case only, as Lua spells them
--       NO RED ANYWHERE. Red is the colour of both squares that must be hidden.
--       vector_stats, exactly one:
--         expression "False": unknown variable 'False'
--       On 0.11.104 it listed three -- 'false' and 'true' as well, both of them correct input.
--
--   B3  A PATCH, AND A DATA PAYLOAD, KEYED ONLY BY NUMBERS. ScriptedScreens serialises such a
--       Lua table as a LIST, so `{ ["1"] = ... }` arrived with its keys destroyed and the
--       readers that expect a map returned without a word. THIS IS THE ROW THAT NEEDED A
--       CONSOLE TO FIND AT ALL: an offline test never crosses the Lua-to-host bridge.
--         left square, id=1   -> GREEN
--         right square, id=2  -> GOLD
--         the big number      -> 7.5, from `data = { ["1"] = 7.5 }` read back as $1
--       On 0.11.104 both squares stayed GREY, the number read `--`, and nothing was reported.
--       vector_stats: NO problems on this element, on either build.
--
--   B4  i1 IN AN LS's PAINT KEY. An `LS`'s `f`, `fo`, `s`, `sw` and `fea_edge` were evaluated
--       outside the sample frame, so `i` there meant the enclosing repeat while `i` in the same
--       node's `x`/`y` meant the sample -- one node reading one name two ways.
--       Three lines, one per repeat instance, `sw="=1.5+1.5*i1"`:
--         EACH LINE IS THICKER THAN THE ONE ABOVE IT -- 1.5, 3 and 4.5 units of stroke.
--       On 0.11.104 all three drew at 1.5: `i1` in a paint key reached past the repeat and
--       found nothing. The three lines carry `click` only so that each stroke band is recorded
--       as a hit area whose HEIGHT is the width `sw` evaluated to -- an open stroked shape
--       answers along its stroke -- which is how the widths are measured rather than eyeballed.
--       Clicking them does nothing. Measured that way: 11.1 / 22.2 / 33.3 on 0.11.105 against
--       11.1 / 11.1 / 11.1 on 0.11.104.
--       vector_stats: no problems.
--
--   B5  hover IN AN LS's GEOMETRY. NEEDS A PERSON AT THE CONSOLE -- a capture only ever shows
--       the resting state. Three clickable lines, `y` carrying `-hover*5` and `s` a hover
--       colour.
--         AIM AT ONE LINE: it turns GOLD and lifts AS A WHOLE, and the other two do not move.
--       On 0.11.104 the hover test compared against the SAMPLE number, so pointing at one line
--       lifted one sample of EVERY line: all three kinked at once. Measured offline as the
--       stroke bands, with the crosshair on the middle line: 0.11.105 gives three bands of
--       height 22.2 at y 285.2 / 240.7 / 122.2 against an idle 285.2 / 203.7 / 122.2, so ONLY
--       the middle one moved and none deformed; 0.11.104 gives three bands of height 60.1,
--       every one of them kinked.
--
--   B6  if IS LAZY PAST EXPRESSION DEPTH 256. Above that depth evaluation leaves recursion for
--       an explicit stack, and that walk evaluated every node in the tree. The VALUE was never
--       wrong -- every operator here is total -- but a `$name` read only in an untaken branch
--       was recorded as unresolved, so a scene correct at depth 256 reported an unresolved data
--       name at 257.
--         green bar, if(1, <deep>1, <deep>$lazy)   -> 140 wide. `lazy` must NOT be listed.
--         gold bar,  if(0, <deep>1, <deep>$eager)  -> 20 wide.  `eager` MUST be listed.
--       Both bars are the same width on both builds. THE ROW IS READ OFF THE UNRESOLVED-NAMES
--       LINE OF vector_stats, nowhere else: it must say `eager` and only `eager`. On 0.11.104
--       it said `eager, lazy`. The two names are different on purpose -- a control naming the
--       same name as its subject could never have failed.
--       vector_stats: no problems on this element, on either build.
--
--   B7  AN ABLATION SWITCH THAT OUTLIVED ITS REBUILD. TWO ELEMENTS, AND THE ORDER MATTERS.
--       `NoEval` is written at the top of the emit and was never cleared, and it is also read
--       on the PARSE path by the code that cuts a static clip rectangle. Both are [ThreadStatic]
--       and a CAPTURE rebuilds inline on the main thread, which is the thread that parses -- so
--       after one capture of a scene whose root carries `noeval = 1`, every clip rectangle
--       parsed afterwards, on any scene and any surface, for the rest of the session, was cut
--       as a fixed 2x2 box at (40,40), in silence.
--       That is why this needs the two in sequence and cannot be read from a single picture:
--         B7a, `noeval = 1`, is the TRIGGER. It is MEANT to look nearly empty -- under the
--              switch every rectangle becomes that 2x2 box, its own backdrop included, so only
--              its labels and one speck draw. Nothing about it is the test.
--         B7b is the TEST. It holds `CP id=win { R x=4 y=12 w=90 h=30 }` and a green rect
--              clipped to it, and its structure is re-pushed on every tick with its parse
--              counter in the corner, so it is RE-PARSED after whatever has just been emitted.
--       SO: TAKE A CAPTURE, THEN TAKE A SECOND ONE. B7b must hold the SAME SOLID GREEN BLOCK,
--       90 x 30 scene units in its top left, in both -- and keep it however many captures are
--       taken. On 0.11.104 the first capture looks right and every capture after it shows the
--       block shrunk to a 2x2 speck near the middle of the cell, because the first capture set
--       the switch and the next tick's re-parse read it. Measured offline, with the noeval
--       scene emitted first and the clip scene parsed after it: this page's own clip outline
--       reads 90 x 30 at 4,12 on 0.11.105 and 2 x 2 at 40,40 on 0.11.104.
--       The parse counter in B7b's corner is there to prove the re-parse is happening: if it
--       stops climbing, the row is not being exercised and proves nothing.
--       vector_stats: no problems on either element.
--
-- NOT ON EITHER PAGE
--
--   `nofill` missing the radial-gradient fill and the flat fill under a `blur`: a measuring
--   switch, offline-measurable only, with no shipped scene that sets it and nothing visible on
--   a console either way. See page 1 for the numbers. A row for it would be a row that cannot
--   fail, so there is none.
--
-- PASS   B1 draws <x{y> and lists both specs; no red square in B2 and exactly one report;
--        B3 green + gold + 7.5; B4's three lines get thicker downwards; B6 lists `eager` and
--        not `lazy`; B7b's green block is unchanged across two or more captures; and, with a
--        person at the console, B5 lifts and golds ONLY the line aimed at
-- FAIL   a literal <{$v:x{{y}> in B1, any red in B2, a grey square or `--` in B3, three equal
--        lines in B4, `lazy` in the unresolved list, a 2x2 speck in B7b on any capture, or B5
--        kinking all three lines at once

local ui = ss.ui.surface("main")
ss.ui.activate("main")
ui:clear()

-- A 2 x 4 grid on a 684 x 460 console; one scene unit is two pixels.
local function cell(col, row)
    return { unit = "px", x = 6 + col * 342, y = 6 + row * 112, w = 330, h = 108 }
end

-- 256 prefix minus signs put the tree past depth 256 while keeping the branch VALUE at +1:
-- the fault is a laziness fault, not an arithmetic one, so the two bars must stay the widths
-- they always had and only the unresolved list may move.
local deep = string.rep("-", 256)

-- B1 -------------------------------------------------------------------------------------
ui:element({
    id = "brace", type = "vector",
    rect = cell(0, 0),
    props = { scene = "brace", src = table.concat({
        "SCENE w=165 h=54 fit=stretch size=3.5 f=#EAF4F8",
        "R x=0 y=0 w=165 h=54 f=#0B1622 fea=0",
        'T x=2 y=1 w=161 h=5 size=4 text="B1  an escaped brace in a slot"',

        'T x=2 y=9 w=70 h=7 size=5 f=#5FD9A8 text="<{$v:x{{y}>"',
        'T x=74 y=10 w=89 h=4 size=3 text="must draw <x{y>, and say so"',

        'T x=2 y=18 w=70 h=7 size=5 f=#7FB2F0 text="$v" fmt="y{{z"',
        'T x=74 y=19 w=89 h=4 size=3 text="the node fmt route: y{z,"',
        'T x=74 y=23 w=89 h=4 size=3 text="which both builds report"',

        'T x=2 y=27 w=70 h=7 size=5 f=#8FA6B8 text="{$v}}"',
        'T x=2 y=36 w=70 h=7 size=5 f=#8FA6B8 text="{{{$v}}}"',
        'T x=74 y=32 w=89 h=4 size=3 text="unchanged and silent: a"',
        'T x=74 y=36 w=89 h=4 size=3 text="brace after a closer is"',
        'T x=74 y=40 w=89 h=4 size=3 text="one the LABEL asked for"',

        'T x=2 y=47 w=161 h=4 size=3 f=#53646F text="0.11.104 drew the first row literally"',
    }, "\n") },
})

-- B2 -------------------------------------------------------------------------------------
ui:element({
    id = "bool", type = "vector",
    rect = cell(1, 0),
    props = { scene = "bool", src = table.concat({
        "SCENE w=165 h=54 fit=stretch size=3.5 f=#EAF4F8",
        "R x=0 y=0 w=165 h=54 f=#0B1622 fea=0",
        'T x=2 y=1 w=161 h=5 size=4 text="B2  true and false as words: 1"',

        "R x=2 y=9 w=26 h=19 f=#E23D3D fea=0 v=false",
        'T x=2 y=30 w=28 h=4 size=3 text="v=false"',
        "R x=32 y=9 w=26 h=19 f=#5FD9A8 fea=0 v=true",
        'T x=32 y=30 w=28 h=4 size=3 text="v=true"',
        "R x=62 y=9 w=26 h=19 f=#E23D3D fea=0 v=0",
        'T x=62 y=30 w=28 h=4 size=3 text="v=0"',
        "R x=92 y=9 w=26 h=19 f=#5FD9A8 fea=0 v=1",
        'T x=92 y=30 w=28 h=4 size=3 text="v=1"',
        "R x=122 y=9 w=26 h=19 f=#F5D76E fea=0 v=False",
        'T x=122 y=30 w=28 h=4 size=3 text="v=False"',

        'T x=2 y=38 w=161 h=4 size=3 text="NO RED ANYWHERE: red is the colour of both"',
        'T x=2 y=42 w=161 h=4 size=3 text="squares that must be hidden. the gold one"',
        'T x=2 y=46 w=161 h=4 size=3 text="stays: upper case is not a word here, and"',
        'T x=2 y=50 w=161 h=4 size=3 text="now says so"',
    }, "\n") },
})

-- B3 -------------------------------------------------------------------------------------
ui:element({
    id = "numeric", type = "vector",
    rect = cell(0, 1),
    props = { scene = "numeric", src = table.concat({
        "SCENE w=165 h=54 fit=stretch size=3.5 f=#EAF4F8",
        "R x=0 y=0 w=165 h=54 f=#0B1622 fea=0",
        'T x=2 y=1 w=161 h=5 size=4 text="B3  a patch keyed only by numbers"',

        "R id=1 x=2 y=9 w=28 h=17 f=#53646F fea=0",
        "R id=2 x=34 y=9 w=28 h=17 f=#53646F fea=0",
        'T x=2 y=28 w=64 h=4 size=3 text="1 GREEN, 2 GOLD"',
        'T x=68 y=10 w=95 h=4 size=3 text="the payload is written"',
        'T x=68 y=14 w=95 h=4 size=3 text="{ [1] = ..., [2] = ... },"',
        'T x=68 y=18 w=95 h=4 size=3 text="which the host sends"',
        'T x=68 y=22 w=95 h=4 size=3 text="as a LIST"',

        'T x=2 y=37 w=62 h=8 size=7 f=#5FD9A8 text="{$1}" fmt="{0:F1}" missing="--"',
        'T x=68 y=38 w=95 h=4 size=3 text="data keyed [1] as well:"',
        'T x=68 y=42 w=95 h=4 size=3 text="must read 7.5, never --"',

        'T x=2 y=48 w=64 h=4 size=3 f=#53646F text="0.11.104: both grey"',
    }, "\n") },
})

-- B4 -------------------------------------------------------------------------------------
ui:element({
    id = "sample", type = "vector",
    rect = cell(1, 1),
    props = { scene = "sample", src = table.concat({
        "SCENE w=165 h=54 fit=stretch size=3.5 f=#EAF4F8",
        "R x=0 y=0 w=165 h=54 f=#0B1622 fea=0",
        'T x=2 y=1 w=161 h=5 size=4 text="B4  i1 in an LS paint key"',

        -- `click` is here only so the stroke band is recorded as a hit area: an open stroked
        -- shape answers along its stroke, so the band's HEIGHT is what `sw` evaluated to, and
        -- the three widths can be measured as well as seen. Clicking does nothing.
        'RP n=3 { LS id=wide n=12 click=1 x="=8+i*13" y="=14+i1*11" sw="=1.5+1.5*i1" s=#5FD9A8 }',

        'T x=2 y=42 w=161 h=4 size=3 text="three lines, each THICKER than the one above"',
        'T x=2 y=47 w=161 h=4 size=3 f=#53646F text="0.11.104 drew all three at the same width"',
    }, "\n") },
})

-- B5 -------------------------------------------------------------------------------------
ui:element({
    id = "pointer", type = "vector",
    rect = cell(0, 2),
    props = { scene = "pointer", src = table.concat({
        "SCENE w=165 h=54 fit=stretch size=3.5 f=#EAF4F8",
        "R x=0 y=0 w=165 h=54 f=#0B1622 fea=0",
        'T x=2 y=1 w=161 h=5 size=4 text="B5  hover in an LS geometry key"',

        "RP n=3 {",
        ' LS id=ln n=12 click=1 x="=8+i*13" y="=14+i1*11-hover*5" sw=3'
            .. ' s="=if(hover,#F5D76E,#5FD9A8)"',
        "}",

        'T x=2 y=42 w=161 h=4 size=3 text="AIM at one line: it golds and lifts AS A WHOLE"',
        'T x=2 y=47 w=161 h=4 size=3 f=#53646F text="0.11.104 kinked all three lines at once"',
    }, "\n") },
})

-- B6 -------------------------------------------------------------------------------------
ui:element({
    id = "lazy", type = "vector",
    rect = cell(1, 2),
    props = { scene = "lazy", src = table.concat({
        "SCENE w=165 h=54 fit=stretch size=3.5 f=#EAF4F8",
        "R x=0 y=0 w=165 h=54 f=#0B1622 fea=0",
        'T x=2 y=1 w=161 h=5 size=4 text="B6  if is lazy past depth 256"',

        'R id=lz x=2 y=9 w="=20+120*if(1,' .. deep .. "1," .. deep .. '$lazy)" h=9 f=#5FD9A8 fea=0',
        'T x=2 y=20 w=161 h=4 size=3 text="140 wide. $lazy is in the branch NOT taken,"',
        'T x=2 y=24 w=161 h=4 size=3 text="so it must NOT be listed as unresolved"',

        'R id=eg x=2 y=32 w="=20+120*if(0,' .. deep .. "1," .. deep .. '$eager)" h=9 f=#F5D76E fea=0',
        'T x=2 y=43 w=161 h=4 size=3 text="20 wide: $eager IS read, and IS listed"',
        'T x=2 y=48 w=161 h=4 size=3 f=#53646F text="0.11.104 listed both names"',
    }, "\n") },
})

-- B7a ------------------------------------------------------------------------------------
-- Declared BEFORE the clipped scene, and it is the trigger: a capture emits it inline on the
-- main thread, which is also the thread that parses, and before 0.11.105 the switch it sets
-- stayed set.
ui:element({
    id = "ablate", type = "vector",
    rect = cell(0, 3),
    props = { scene = "ablate", src = table.concat({
        "SCENE w=165 h=54 fit=stretch size=3.5 f=#EAF4F8 noeval=1",
        "R x=0 y=0 w=165 h=54 f=#0B1622 fea=0",
        'T x=2 y=1 w=161 h=5 size=4 text="B7a  noeval=1: the TRIGGER"',
        "R x=2 y=12 w=80 h=20 f=#7FB2F0 fea=0",
        'T x=2 y=38 w=161 h=4 size=3 text="under the switch every rect becomes a 2x2 box,"',
        'T x=2 y=42 w=161 h=4 size=3 text="this cell\'s own backdrop included, so"',
        'T x=2 y=46 w=161 h=4 size=3 text="THIS CELL IS MEANT TO LOOK EMPTY"',
    }, "\n") },
})

-- B7b ------------------------------------------------------------------------------------
-- Re-pushed from `tick` so that it is RE-PARSED after whatever was last emitted. The counter
-- in the corner is the proof that the re-parse happens.
local function clipped(parses)
    return table.concat({
        "SCENE w=165 h=54 fit=stretch size=3.5 f=#EAF4F8",
        "R x=0 y=0 w=165 h=54 f=#0B1622 fea=0",
        "DEFS { CP id=win { R x=4 y=12 w=90 h=30 } }",
        'T x=2 y=1 w=161 h=5 size=4 text="B7b  the clip, parsed after it"',
        "G clip=win { R x=0 y=0 w=165 h=54 f=#5FD9A8 fea=0 }",
        'T x=98 y=13 w=65 h=4 size=3 text="a SOLID green block,"',
        'T x=98 y=17 w=65 h=4 size=3 text="90 x 30, in EVERY"',
        'T x=98 y=21 w=65 h=4 size=3 text="capture and for ever"',
        'T x=98 y=29 w=65 h=4 size=3 f=#53646F text="0.11.104: a 2x2"',
        'T x=98 y=33 w=65 h=4 size=3 f=#53646F text="speck, from the"',
        'T x=98 y=37 w=65 h=4 size=3 f=#53646F text="second capture on"',
        'T x=98 y=47 w=65 h=4 size=3 f=#53646F text="parses ' .. parses .. '"',
    }, "\n")
end

ui:element({
    id = "clip", type = "vector",
    rect = cell(1, 3),
    props = { scene = "clip", src = clipped(1) },
})

ui:commit()

-- The payloads, sent from a LATER chip execution than the declarations: declaring an element
-- and patching it inside ONE execution loses the declaration's data.
local parses = 1
local sent = false

function tick()
    -- B7b. A fresh parse every tick, with a new counter so the structure really is new and
    -- cannot be skipped as unchanged. This is what puts a parse AFTER a capture's emit.
    parses = parses + 1
    ui:element({
        id = "clip", type = "vector",
        rect = cell(1, 3),
        props = { scene = "clip", src = clipped(parses) },
    })

    if not sent then
        sent = true

        -- B1. `v` for the brace routes.
        ui:element({
            id = "brace-data", type = "vector",
            rect = { unit = "px", x = 0, y = 0, w = 1, h = 1 },
            props = { scene = "brace", keep = 1, data = { v = 1.25 } },
        })

        -- B3. BOTH tables are keyed only by numbers, which is the whole row: the host sends
        -- each of them as a LIST, and slot i is the name i + 1.
        ui:element({
            id = "numeric-data", type = "vector",
            rect = { unit = "px", x = 1, y = 0, w = 1, h = 1 },
            props = { scene = "numeric", keep = 1,
                      data = { ["1"] = 7.5 },
                      nodes = { ["1"] = { f = "#5FD9A8" }, ["2"] = { f = "#F5D76E" } } },
        })
    end

    ui:commit()
end
