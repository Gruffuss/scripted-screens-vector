-- 0.11.102: the values the parser used to read as nothing and say nothing about.
--
-- Every row is a DRAWN result, so most of the page reads off one capture. Each subject sits
-- beside the control that already worked, so a row that looks right for the wrong reason shows
-- itself. FIVE elements, and ONLY the one marked E may carry the magenta border.
--
-- WHAT SHOULD BE SEEN
--
--   A  numbers where free text belongs (the "text" element); left = unquoted, right = quoted,
--      both are NUMBERS in scene text and both must now draw:
--        text=700 / text="700"      ->  700          (drew NOTHING before 0.11.102)
--        missing=0 with no data     ->  0            (was the default --)
--        unit=5 over n=7.6          ->  7.65         (the unit was dropped)
--        the small square has id=1 and is patched red: GREY means the patch missed it
--
--   D  on the same element, the two format fixes:
--        fmt="100%% of %.0f" over 7.6  ->  100% of 8   (drew its own spec, and was wrongly
--                                                       reported as holding no conversion)
--        {=floor(t/60):%d:00}          ->  m:00, the seconds colon intact (drew "00")
--
--   B  Lua booleans (the "bool" element). The chip sends true/false, never 1/0:
--      This element is written in the LUA TABLE form, because a Lua boolean only reaches an
--      attribute that way -- in scene text `press=true` is the string "true".
--        v=false  ->  the RED square is GONE      (it stayed visible before)
--        v=true   ->  the GREEN square is THERE
--        CLICK the amber bar -- press=true must make it clickable at all, and the line
--        below it then reads down:bar / up:bar / bar
--        the alarm word: after two ticks it must read OFF. `keep = true` with
--        `alarm = false` used to leave it at ON, which is the payload that could not
--        turn an alarm off.
--
--   C  %name splicing (the "sym" element), one symbol with parameters i=3 and idx=7:
--        "i=%i idx=%idx"      ->  i=3 idx=7      (was "i=3 idx=3dx")
--        "%wide", undeclared  ->  draws %wide literally
--
--   E  the "problems" element is the ONLY one with a magenta border, and vector_stats on it
--      must list exactly these four and nothing else:
--        T: fit "elipsis" is not none, ellipsis or shrink
--        L: cap "rund" is not butt, round or square
--        sh: dy is read as a plain number and is not evaluated, so a shadow does not animate...
--        expression "=#5FD9A8" holds a colour, but this attribute takes a number
--      Its DRAWING is unchanged by all four: the label, the line and the shadowed box draw as
--      though the typos were the defaults, because that is what they fall back to.
--
--   F  a repeat past the old cap (the "rows" element): n=30000 hairlines fill the strip edge
--      to edge and evenly. Before 0.11.102 `n` was clamped to 20,000 AND `n` read 20,000 in
--      the expression, so the field spread 20,000 marks over the same width -- visibly
--      coarser, with nothing reported. vector_stats must report NO "TOO LARGE" for it.
--
-- PASS   A-D as marked, E exactly those four problems on that element alone, F fills the strip
-- FAIL   anything drawn that is not marked, a border on any other element, or TOO LARGE on F

local ui = ss.ui.surface("main")
ss.ui.activate("main")
ui:clear()

local boolData

-- A and D ---------------------------------------------------------------------------------
ui:element({
    id = "text", type = "vector",
    rect = { unit = "px", x = 8, y = 8, w = 300, h = 160 },
    props = { scene = "text", src = table.concat({
        "SCENE w=150 h=80 fit=stretch size=4 f=#EAF4F8",
        "R x=0 y=0 w=150 h=80 f=#0B1622 fea=0",
        'T x=2 y=1 w=146 h=5 size=4 text="A  a number where free text belongs"',

        "T x=4  y=8 w=40 h=7 size=5 text=700",
        'T x=46 y=8 w=40 h=7 size=5 text="700"',
        'T x=90 y=9 w=58 h=5 size=3 f=#5A7085 text="both must read 700"',

        'T x=4  y=17 w=40 h=7 size=5 text="$gone" missing=0',
        'T x=46 y=17 w=40 h=7 size=5 text="$gone" missing="0"',
        'T x=90 y=18 w=58 h=5 size=3 f=#5A7085 text="both must read 0"',

        'T x=4  y=26 w=40 h=7 size=5 text="$n" unit=5',
        'T x=46 y=26 w=40 h=7 size=5 text="$n" unit="5"',
        'T x=90 y=27 w=58 h=5 size=3 f=#5A7085 text="both must read 7.65"',

        "R id=1 x=4 y=36 w=10 h=10 f=#53646F fea=0",
        "R id=two x=16 y=36 w=10 h=10 f=#53646F fea=0",
        'T x=30 y=38 w=118 h=5 size=3 f=#5A7085 text="both patched: LEFT id=1 (number), RIGHT id=two"',

        'T x=2 y=50 w=146 h=5 size=4 text="D  fmt and placeholders"',
        'T x=4  y=57 w=66 h=7 size=5 text="$n" fmt="100%% of %.0f"',
        'T x=74 y=58 w=74 h=5 size=3 f=#5A7085 text="must read 100% of 8"',
        'T x=4  y=67 w=66 h=7 size=5 text="{=floor(t/60):%d:00}"',
        'T x=74 y=68 w=74 h=5 size=3 f=#5A7085 text="m:00, the colon kept"',
    }, "\n") },
})

-- B ---------------------------------------------------------------------------------------
-- TABLE form, not `src`: a Lua boolean only reaches an attribute this way. In scene text
-- `press=true` is the STRING "true", which is not a number and never was one.
ui:element({
    id = "bool", type = "vector",
    rect = { unit = "px", x = 316, y = 8, w = 136, h = 160 },
    props = {
        scene = "bool",
        w = 68, h = 80, fit = "stretch",
        root = {
            { op = "R", x = 0, y = 0, w = 68, h = 80, f = "#0B1622", fea = 0 },
            { op = "T", x = 2, y = 1, w = 64, h = 5, size = 3, f = "#EAF4F8",
              text = "B  Lua booleans" },
            { op = "T", x = 2, y = 7, w = 64, h = 4, size = 2.5, f = "#5A7085",
              text = "left GONE, right THERE" },

            -- `v` straight from Lua. `false` used to leave the node VISIBLE, because the
            -- attribute fell back to its default of 1.
            { op = "R", x = 8, y = 13, w = 18, h = 18, f = "#E23D3D", fea = 0, v = false },
            { op = "R", x = 42, y = 13, w = 18, h = 18, f = "#5FD9A8", fea = 0, v = true },

            -- `press = true` has to make this clickable at all.
            { op = "R", id = "bar", x = 6, y = 36, w = 56, h = 10, rx = 2, fea = 0,
              press = true, f = "=if(down,#F5D76E,#B8903A)" },
            { op = "T", x = 6, y = 38, w = 56, h = 6, size = 3.5, f = "#0B1622",
              align = "center", text = "press me" },
            { op = "T", x = 6, y = 48, w = 56, h = 6, size = 4, f = "#EAF4F8",
              text = "$last", missing = "(no click yet)" },

            { op = "T", x = 6, y = 58, w = 56, h = 4, size = 2.5, f = "#5A7085",
              text = "alarm, must read OFF" },
            { op = "T", x = 6, y = 64, w = 56, h = 9, size = 7, f = "#E23D3D",
              text = "ON", v = "$alarm" },
            { op = "T", x = 6, y = 64, w = 56, h = 9, size = 7, f = "#5FD9A8",
              text = "OFF", v = "=1-$alarm" },
        },
    },
    on_click = function(value)
        boolData:set_props({ scene = "bool", keep = true, data = { last = tostring(value) } })
        ui:commit()
    end,
})

-- C ---------------------------------------------------------------------------------------
ui:element({
    id = "sym", type = "vector",
    rect = { unit = "px", x = 8, y = 176, w = 300, h = 80 },
    props = { scene = "sym", src = table.concat({
        "SCENE w=150 h=40 fit=stretch size=4 f=#EAF4F8",
        "R x=0 y=0 w=150 h=40 f=#0B1622 fea=0",
        'T x=2 y=1 w=146 h=5 size=4 text="C  %name splicing"',
        "DEFS {",
        ' SYM id=pair i=3 idx=7 { T x=0 y=0 w=70 h=8 size=5 f=#EAF4F8 text="i=%i idx=%idx" }',
        ' SYM id=glued w=10 { T x=0 y=0 w=70 h=8 size=5 f=#EAF4F8 text="%wide" }',
        "}",
        "USE ref=pair x=4 y=10",
        'T x=78 y=11 w=70 h=5 size=3 f=#5A7085 text="must read i=3 idx=7"',
        "USE ref=glued x=4 y=24",
        'T x=78 y=25 w=70 h=5 size=3 f=#5A7085 text="must read %wide"',
    }, "\n") },
})

-- E ---------------------------------------------------------------------------------------
ui:element({
    id = "problems", type = "vector",
    rect = { unit = "px", x = 316, y = 176, w = 136, h = 80 },
    props = { scene = "problems", src = table.concat({
        "SCENE w=68 h=40 fit=stretch size=4 f=#EAF4F8",
        "R x=0 y=0 w=68 h=40 f=#0B1622 fea=0",
        'T x=2 y=1 w=64 h=5 size=3 text="E  four reports, border expected"',
        'T x=4 y=8 w=60 h=7 size=4 fit=elipsis text="still drawn"',
        "L p=[4,18,64,18] s=#5FD9A8 sw=2 cap=rund",
        'R x=4 y=23 w=26 h=13 rx=2 f=#2A3C50 fea=0 sh=[[0,"=2*t",6,0,#000000CC]]',
        "R x==#5FD9A8 y=23 w=1 h=13 f=#53646F fea=0",
    }, "\n") },
})

-- F ---------------------------------------------------------------------------------------
ui:element({
    id = "rows", type = "vector",
    rect = { unit = "px", x = 8, y = 264, w = 444, h = 60 },
    props = { scene = "rows", src = table.concat({
        "SCENE w=222 h=30 fit=stretch size=4 f=#EAF4F8",
        "R x=0 y=0 w=222 h=30 f=#0B1622 fea=0",
        'T x=2 y=1 w=218 h=5 size=4 text="F  RP n=30000: must fill the strip edge to edge"',
        'RP n=30000 { R x="=2+i*218/n" y=10 w=0.09 h=16 f=#5FD9A8 fea=0 }',
    }, "\n") },
})

-- The data elements. Declared here and patched only from `tick`: declaring an element and
-- patching it in the SAME chip execution loses the declaration's data.
-- `data` and `nodes` are DIFFERENT props, so they ride on ONE element. Two elements naming the
-- same scene do not both deliver: measured 2026-10-06, the second left `$n` UNRESOLVED and the
-- `id = 1` patch unapplied, and the mod said nothing about either.
ui:element({
    id = "text-data", type = "vector",
    rect = { unit = "px", x = 0, y = 0, w = 1, h = 1 },
    props = { scene = "text", keep = 1, data = { n = 7.6 },
              nodes = { ["1"] = { f = "#E23D3D" }, ["two"] = { f = "#5FD9A8" } } },
})

-- `alarm = 1` now, and `alarm = false` from the second tick: the payload that did nothing.
boolData = ui:element({
    id = "bool-data", type = "vector",
    rect = { unit = "px", x = 0, y = 0, w = 1, h = 1 },
    props = { scene = "bool", keep = true, data = { alarm = 1 } },
})

ui:commit()

local ticks = 0

function tick(dt)
    ticks = ticks + 1

    -- The payload that used to do nothing: under `keep` a boolean carried no value at all, so
    -- `alarm = false` left `alarm = 1` standing and the alarm could not be turned off. Sent
    -- from a LATER execution than the declaration, which is the only order that is safe.
    if ticks == 2 then
        boolData:set_props({ scene = "bool", keep = true, data = { alarm = false } })
        ui:commit()
    end
end
