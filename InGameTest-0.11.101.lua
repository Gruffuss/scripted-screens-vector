-- Everything unseen since 0.11.97: the four versions 0.11.98, .99, .100 and .101.
--
-- The one thing here that CANNOT be checked offline is the raycast filter, so it gets its own
-- element and a native textinput underneath it. Everything else is a drawn result.
--
-- WHAT SHOULD BE SEEN, and what to do
--
--   A  CLICK THE PLACEHOLDER TEXT ("click the empty box above me"), which is empty vector
--      space: the dashed box covers it and the green square is well to its left.
--      0.11.98 made the element an ICanvasRaycastFilter, so the click should reach the TEXTINPUT
--      behind it and focus it (a caret appears). Before 0.11.98 the vector element swallowed it
--      and the input could never be focused at all.
--      Then CLICK THE GREEN SQUARE: it must still report `hit` on the line below.
--
--   B  SCROLL THE WHEEL over the EMPTY part of the list (below the last row).
--      The filter must still let the container have the pointer over its whole viewport, not
--      only over the drawn rows, or wheel-scrolling dies on short lists.
--
--   C  fmt row, all four must read as marked -- these are 0.11.100 and 0.11.101:
--        {0} kPa     -> 1.25 kPa     and NO problem border from this element
--        {0,6:0.0}   ->    1.3
--        %x {0:D2}   -> 8 08         (v=7.6; the placeholder must match the whole binding)
--        {$v:%q}     -> prints %q    AND is reported
--
--   D  points row -- 0.11.101's doc correction, drawn as proof:
--        a 2-point Y strokes but does NOT fill (outline only, hollow)
--        an odd 5-value L draws as a 2-point line
--        an odd 3-value L draws NOTHING
--
--   E  the scroll offset: this list does NOT scroll to 0.5 on load. 0.11.98 stopped an inherited
--      `so` becoming an SC's scroll offset, so it must start at the TOP.
--
-- PASS   A focuses the input and the square still reports; B scrolls; C and D as marked; E at top
-- FAIL   any of those, or a problem border on an element other than the fmt one

local ui = ss.ui.surface("main")
ss.ui.activate("main")
ss.ui.allow_mcp_automation(true)   -- lets the element-level control run; a real click is unaffected
ui:clear()

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

-- A native input UNDER the vector element: the thing the raycast filter must now let through.
ui:element({
    id = "behind", type = "textinput",
    rect = { unit = "px", x = 70, y = 40, w = 150, h = 28 },
    props = { value = "", placeholder = "click the empty box above me" },
})

local hit

-- A: one clickable node in a mostly empty element, drawn OVER the textinput
ui:element({
    id = "ray", type = "vector",
    rect = { unit = "px", x = 10, y = 10, w = 220, h = 90 },
    props = { scene = "ray", src = table.concat({
        "SCENE w=110 h=45 fit=stretch size=4 f=#EAF4F8",
        'T x=2 y=1 w=106 h=5 size=4 text="A  click the EMPTY space, then the square"',
        -- a dashed outline so the element's extent is visible; not clickable
        "R x=1 y=7 w=108 h=36 f=none s=#53646F sw=0.5 dash=[2,2]",
        'R id=sq x=4 y=12 w=16 h=16 f="=if(hover,#F5D76E,#5FD9A8)" click=1 fea=0',
    }, "\n") },
    on_click = function(value)
        hit:set_props({ scene = "out", keep = 1, data = { last = value } })
        ui:commit()
    end,
})

-- B and E: a scroll container. `so` comes from the GROUP, never from the SC -- after 0.11.98
-- that must NOT set the scroll offset, so the list starts at the top.
ui:element({
    id = "list", type = "vector",
    rect = { unit = "px", x = 240, y = 10, w = 210, h = 160 },
    props = { scene = "list", src = table.concat({
        "SCENE w=105 h=80 fit=stretch size=4 f=#EAF4F8",
        "R x=0 y=0 w=105 h=80 f=#0B1622 fea=0",
        'T x=2 y=1 w=101 h=5 size=4 text="B  wheel over the EMPTY part below"',
        "G so=0.5 {",
        "  SC id=sc x=2 y=8 w=101 h=70 ch=200 sov=1 {",
        "    RP n=6 {",
        '      R x=2 y="=10+i*14" w=60 h=10 f=#2E8B6E fea=0',
        '      T x=4 y="=12+i*14" w=50 h=6 size=4 text="row {=i}"',
        "    }",
        "  }",
        "}",
        'T x=2 y=74 w=101 h=5 size=3 f=#53646F text="E  must start at the TOP, not scrolled"',
    }, "\n") },
})

-- C and D: drawn results, no interaction
ui:element({
    id = "res", type = "vector",
    rect = { unit = "px", x = 10, y = 180, w = 440, h = 260 },
    props = { scene = "res", src = table.concat({
        "SCENE w=200 h=120 fit=stretch size=4 f=#EAF4F8",
        "R x=0 y=0 w=200 h=120 f=#0B1622 fea=0",
        'T x=3 y=2 w=194 h=6 size=5 text="C  fmt (v = 7.6 and 1.25)"',

        'T x=3 y=12 w=60 h=5 size=4 f=#53646F text="{0} kPa"',
        'T x=66 y=11 w=60 h=7 size=6 text=$w fmt="{0} kPa"',

        'T x=3 y=22 w=60 h=5 size=4 f=#53646F text="{0,6:0.0}"',
        'T x=66 y=21 w=60 h=7 size=6 text=$w fmt="{0,6:0.0}"',

        'T x=3 y=32 w=60 h=5 size=4 f=#53646F text="%x {0:D2} placeholder"',
        'T x=66 y=31 w=60 h=7 size=6 text="<{$v}>" fmt="%x {0:D2}"',

        'T x=3 y=42 w=60 h=5 size=4 f=#53646F text="spec %q on $v: reports"',
        'T x=66 y=41 w=60 h=7 size=6 text="<{$v:%q}>"',

        'T x=3 y=56 w=194 h=6 size=5 text="D  points"',
        'T x=3 y=66 w=60 h=5 size=4 f=#53646F text="2-point Y: outline only"',
        "Y p=[70,64,110,78] f=#E23D3D s=#FFFFFF sw=1",

        'T x=3 y=82 w=60 h=5 size=4 f=#53646F text="odd 5-value L: a 2-point line"',
        "L p=[70,80,110,94,999] f=none s=#5FD9A8 sw=1",

        'T x=3 y=98 w=60 h=5 size=4 f=#53646F text="odd 3-value L: nothing"',
        "L p=[70,96,110] f=none s=#C98BE0 sw=1",

        'T x=3 y=110 w=194 h=5 size=3 f=#53646F text="the border on THIS element is expected: the %q spec above"',
    }, "\n") },
})

hit = ui:element({
    id = "out", type = "vector",
    rect = { unit = "px", x = 10, y = 110, w = 220, h = 60 },
    props = { scene = "out", src = table.concat({
        'T x=0 y=0 w=110 h=6 size=5 f=#8FA6B8 text="last click:"',
        'T x=0 y=8 w=110 h=8 size=7 f=#5FD9A8 text=$last missing="(none yet)"',
    }, "\n") },
})

-- the data both result elements read
ui:element({
    id = "data", type = "vector",
    rect = { unit = "px", x = 0, y = 0, w = 1, h = 1 },
    props = { scene = "res", keep = 1, data = { v = 7.6, w = 1.25 } },
})

ui:commit()
