-- 16 - The pointer: hold, hover, where you hit, and drag and drop
--
-- `12-click.lua` covers the plain click. This is everything else the pointer can say, and the
-- rule that decides all of it:
--
--     A CLICK LANDS ON WHAT IS DRAWN.
--
-- Inside the node's own outline, inside any clip it is drawn through, in draw order, last
-- match wins. A circle takes no clicks in its corners. A row scrolled out of sight takes
-- nothing. An OPEN shape -- an `L`, an `SP` or a `P` that never closes -- is clickable along
-- its STROKE, not across the area it would enclose, because that area was never drawn.
--
-- THE CONSOLE'S POINTER IS THE CROSSHAIR. There is no free mouse while you are standing at a
-- console: you aim the camera, and the centre of the screen is the pointer. Hold Alt to detach
-- a real cursor. Everything here works both ways, but it is worth designing for the crosshair:
-- make targets big, and never require two things to be pointed at once.
--
-- FOUR OPT-IN FLAGS, ALL ARRIVING THROUGH on_click. ScriptedScreens carries exactly one event
-- from every player to the chip -- the click -- so these ride on it rather than inventing
-- handlers the game would drop:
--
--     press = 1      down:id   up:id   leave:id
--     xy = 1         appends @fx,fy -- where in the node, 0..1 from its top-left
--     hoverev = 1    enter:id  exit:id
--     drag = 1 / drop = 1    dragstart:id   drop:source>target   dragend:id
--
-- A COLOUR CAN BE AN EXPRESSION. `f = "=if(down,#A,if(hover,#B,#C))"` is one node with three
-- states, and `f = "=mix(#A,#B,hover)"` blends between two. `mix` blends the way a gradient
-- stop does -- premultiplied -- so mixing towards a transparent colour fades out rather than
-- drifting through the other one's hue. Colour literals are the only strings an expression
-- takes; everything else in it is a number, so `if(hover, 1, 2)` is a number, not a colour.
--
-- AND ONE THING THAT NEEDS NO EVENT AT ALL. For hover and press STYLING, send nothing: the
-- expression variables `hover` and `down` are 1 while the pointer is over, or held on, a
-- clickable node inside the nearest enclosing `id`. The highlight is then drawn on the client
-- with no round trip to the chip, which is both instant and free. Reach for `hoverev` only
-- when the CHIP needs to know -- to start a pump, to log, to arm something.
--
-- Read the line at the bottom of the console: it shows the last raw value received, so you can
-- watch the strings arrive as you use the page.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

-- `hover` and `down` are read INSIDE the group that carries the id, so each button styles
-- itself. No data, no tick, no chip involvement.
local src = table.concat({
    "SCENE w=200 h=200 fit=stretch size=5 f=#EAF4F8",
    "R x=0 y=0 w=200 h=200 f=#0B1622 fea=0",

    -- 1 STYLING WITH NO EVENTS
    'T x=6 y=5 w=188 h=6 text="hover and press me: no chip involved"',
    "G id=btn {",
    --   The colour IS the expression: one node, three states, nothing sent to the chip.
    '  R id=btnhit press=1 x=6 y=13 w=86 h=22 rx=4 fea=0 f="=if(down,#2E8B6E,if(hover,#6FE3B6,#5FD9A8))"',
    '  T x=6 y=20 w=86 h=8 align=center size=7 f=#0B1622 text="PRESS"',
    "}",

    -- 2 THE SAME BUTTON, BUT THE CHIP IS TOLD
    'T x=104 y=13 w=90 h=6 text="hoverev tells the chip"',
    "G id=watch {",
    '  R id=watchhit hoverev=1 press=1 x=104 y=21 w=90 h=14 rx=4 fea=0 f="=mix(#24405A,#3A6E8B,hover)"',
    '  T x=104 y=24 w=90 h=7 align=center size=5 f=#EAF4F8 text="WATCH"',
    "}",

    -- 3 WHERE IN THE NODE  (xy = 1)
    'T x=6 y=44 w=188 h=6 text="xy: aim anywhere in the pad -- the dot follows"',
    "R id=pad xy=1 press=1 x=6 y=52 w=188 h=44 rx=4 f=#12202F fea=0",
    --   $padx/$pady are whatever the handler below chose to store, 0..1 across the pad
    'C cx="=6+188*$padx" cy="=52+44*$pady" rx=4 ry=4 f=#F5D76E missing=0',

    -- 4 DRAG AND DROP
    'T x=6 y=104 w=188 h=6 text="drag the chip into either bay"',
    "R id=bayA drop=1 x=6 y=112 w=60 h=34 rx=4 f=#16222E s=#3A4A5A sw=1 fea=0",
    "R id=bayB drop=1 x=74 y=112 w=60 h=34 rx=4 f=#16222E s=#3A4A5A sw=1 fea=0",
    'T x=6 y=139 w=60 h=7 align=center size=5 f=#53646F text="BAY A"',
    'T x=74 y=139 w=60 h=7 align=center size=5 f=#53646F text="BAY B"',
    --   the chip sits in whichever bay the data says, so the drop MOVES it
    'R id=chip drag=1 x="=if($inB,86,18)" y=120 w=36 h=18 rx=3 f=#C98BE0 fea=0 missing=0',

    -- 5 AN OPEN SHAPE IS CLICKABLE ON ITS STROKE
    'T x=6 y=154 w=188 h=6 text="this bracket answers on the LINE, not inside it"',
    "L id=bracket press=1 p=[20,164,80,164,80,186,20,186] s=#8FA6B8 sw=5 f=none",

    'T x=100 y=162 w=94 h=6 text="last event:"',
    'T x=100 y=170 w=94 h=18 size=5 f=#5FD9A8 text=$last wrap=1 missing="(nothing yet)"',
}, "\n")

local data

ui:element({
    id = "ptr_s", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = { scene = "ptr", src = src },

    -- ONE handler for the whole scene. Every flag above arrives here as a string.
    on_click = function(value, player)
        local values = { last = value }

        -- `xy = 1` appends "@fx,fy" to whatever the id would have been.
        local id, fx, fy = value:match("^([^@]+)@([%d.]+),([%d.]+)$")
        if id == "pad" or id == "down:pad" then
            values.padx = tonumber(fx)
            values.pady = tonumber(fy)
        end

        -- `drop = 1` reports "drop:source>target".
        local source, target = value:match("^drop:(.+)>(.+)$")
        if source == "chip" then
            values.inB = target == "bayB" and 1 or 0
        end

        -- Everything else is already visible in `last`; a real page would act on it here.
        data:set_props({ scene = "ptr", keep = 1, snap = 1, data = values })
        ui:commit()
    end,
})

-- The data element. `keep = 1` means each payload is a patch, so the handler above sends only
-- what changed; `missing = 0` on the nodes covers the moment before anything has been sent.
data = ui:element({
    id = "ptr_d", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
    props = { scene = "ptr", keep = 1, data = { padx = 0.5, pady = 0.5, inB = 0 } },
})

ui:commit()
