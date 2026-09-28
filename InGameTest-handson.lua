-- Every vector check that needs a person at the PC, on one console (0.11.37, 0.11.38, 0.11.39).
--
-- Work down the page. Each answer is in the LOG at the bottom, newest first.
--
--  1 SLIDER   press anywhere along the bar. The fill jumps to where you pressed and the log
--             reads "down:level@0.xx,0.yy" -- x from the left, 0..1. Release: "up:level@...".
--  2 HOLD     hold the green button: the lamp lights, log "down:hold". Release on it: lamp out,
--             "up:hold" then a plain "hold". Hold it and DRAG OFF: "leave:hold" while still
--             held, no plain click on release.
--  3 TURN     hold the green button again and TURN YOUR VIEW away without moving the mouse:
--             "leave:hold" must arrive from the turn alone.
--  4 TAP      tap the amber button as fast as you can. EVERY tap must log its own down/up pair
--             (before 0.11.37 two within a quarter second merged into one).
--  5 DRAG     drag the yellow chip into the other bin and let go: "dragstart:chip",
--             "drop:chip>binB", "dragend:chip", and the chip is drawn in that bin.
--  6 SWAP     press SWAP to change which panel is shown. Only the SHOWN panel's button may
--             answer: pressing where the hidden one is must log nothing.
--
-- And four things to look at while you are here:
--  7 SPIN     the hand turns CLOCKWISE, like a clock.
--  8 BLUR     the two soft squares must look the same (left is blur, right is a shadow).
--  9 PILL     the thin bar has fully round ends and a clean edge.
-- 10 PICS     two small pictures: GIF (red/green over blue/transparent) and BMP (red/green over
--             blue/white), both embedded in the page, neither downloaded.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

-- Lets the MCP tools drive this screen, so clicks and drags can be tested without a person.
if ss.ui.allow_mcp_automation then ss.ui.allow_mcp_automation(true) end

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

-- Keep the artwork in a 200-wide band whatever the console's shape, centred.
local SH = 224
local SW = math.max(200, math.floor(SH * W / H))
local OFF = math.floor((SW - 200) / 2)

local GIF = "data:image/gif;base64,R0lGODlhBAAEAIEAAP8AAADIAABQ/////yH5BAEAAAMALAAAAAAEAAQAAAIKBJgQYEKJGyVuBQA7"
local BMP = "data:image/bmp;base64,Qk1GAAAAAAAAADYAAAAoAAAAAgAAAAIAAAABABgAAAAAABAAAAAAAAAAAAAAAAAAAAAAAAAA/wAA////AAAAAP8A/wAAAA=="

local src = table.concat({
"SCENE w=" .. SW .. " h=" .. SH .. " fit=stretch",
"R x=0 y=0 w=" .. SW .. " h=" .. SH .. " f=#070D16",
"G t=[" .. OFF .. ",0] {",

' T x=0 y=2 w=110 h=6 text="1 SLIDER: PRESS ALONG IT (XY)" size=5 f=#5FD9A8',
" R id=level press=1 xy=1 x=0 y=10 w=110 h=13 rx=3 f=#12202F",
' R x=0 y=10 w="=110*$level" h=13 rx=3 f=#2E8B6E',
' T x=0 y=12 w=110 h=9 text="{=$level*100:%.0f}%" size=6 align=center f=#EAF4F8',

' T x=118 y=2 w=82 h=6 text="2+3 HOLD, DRAG OFF, TURN" size=5 f=#5FD9A8',
" R id=hold press=1 x=118 y=10 w=64 h=26 rx=4 f=#2E8B6E",
' T x=118 y=17 w=64 h=11 text="HOLD" size=8 align=center f=#EAF4F8',
" C cx=192 cy=23 rx=6 ry=6 f=#1B2A3A",
" G o=$held { C cx=192 cy=23 rx=6 ry=6 f=#F5D76E }",

' T x=0 y=30 w=110 h=6 text="4 TAP FAST: EVERY TAP LOGS" size=5 f=#5FD9A8',
" R id=tap click=1 press=1 x=0 y=38 w=52 h=22 rx=4 f=#B7791F",
' T x=0 y=44 w=52 h=10 text="TAP" size=7 align=center f=#EAF4F8',

' T x=60 y=30 w=140 h=6 text="5 DRAG THE CHIP TO THE OTHER BIN" size=5 f=#5FD9A8',
" R id=binA drop=1 x=60 y=38 w=66 h=22 rx=4 f=#12202F s=#3A5A7A sw=1",
" R id=binB drop=1 x=134 y=38 w=66 h=22 rx=4 f=#12202F s=#3A5A7A sw=1",
' R id=chip drag=1 x="=72+74*$inB" y=43 w=42 h=12 rx=6 f=#F5D76E',

' T x=0 y=64 w=200 h=6 text="6 SWAP: ONLY THE SHOWN PANEL MAY ANSWER" size=5 f=#5FD9A8',
" R id=swap click=1 x=0 y=72 w=52 h=20 rx=4 f=#3A5A7A",
' T x=0 y=77 w=52 h=10 text="SWAP" size=6 align=center f=#EAF4F8',
-- `v`, not `o`: a group at o=0 keeps its clicks (as CSS opacity: 0 does), and since the hidden
-- panel is drawn last it would take every click meant for the visible one. `v` removes it.
" G v=$showA {",
"  R id=panelA click=1 x=60 y=72 w=140 h=20 rx=4 f=#1E3A5F",
'  T x=60 y=77 w=140 h=10 text="PANEL A (press me)" size=6 align=center f=#EAF4F8',
" }",
' G v="=1-$showA" {',
"  R id=panelB click=1 x=60 y=72 w=140 h=20 rx=4 f=#2E8B6E",
'  T x=60 y=77 w=140 h=10 text="PANEL B (press me)" size=6 align=center f=#EAF4F8',
" }",

' T x=0 y=96 w=60 h=6 text="7 CLOCKWISE" size=5 f=#5FD9A8',
" C cx=28 cy=124 rx=22 ry=22 f=#12202F",
' G t=[28,124] r="=t*90" { R x=-1.5 y=-19 w=3 h=19 rx=1.5 f=#F5D76E }',
" C cx=28 cy=124 rx=2.5 ry=2.5 f=#F5D76E",

' T x=58 y=96 w=90 h=6 text="8 THESE TWO MUST MATCH" size=5 f=#5FD9A8',
" G blur=5 { R x=64 y=112 w=24 h=24 f=#5FD9A8 }",
' R x=106 y=112 w=24 h=24 f=#5FD9A800 fea=0 sh=[[0,0,10,0,"#5FD9A8FF"]]',

' T x=146 y=96 w=54 h=6 text="10 GIF + BMP" size=5 f=#5FD9A8',
' RP n=4 { R x="=146+i*7" y=106 w=3 h=34 f=#26384A }',
-- The URL must be QUOTED: unquoted, the `=` and `;` inside a data: URL break the attribute split.
' IMG x=146 y=106 w=24 h=24 src="' .. GIF .. '" smp=point',
' IMG x=174 y=106 w=24 h=24 src="' .. BMP .. '" smp=point',

' T x=0 y=146 w=200 h=6 text="9 ROUND ENDS, CLEAN EDGE" size=5 f=#5FD9A8',
" R x=0 y=154 w=200 h=9 rx=20 f=#2E8B6E s=#EAF4F8 sw=0.8",

' T x=0 y=170 w=200 h=6 text="LAST" size=5 f=#5FD9A8',
' T x=0 y=178 w=200 h=11 text=$last size=8 f=#EAF4F8 missing="(nothing yet)"',
' T x=0 y=192 w=200 h=28 text=$log size=5 wrap=1 f=#8FA6B8 missing=""',
"}",
}, "\n")

local log = {}
local showA = 1
local data

ui:element({
    id = "hands_s", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = { scene = "hands", src = src },
    on_click = function(value, player)
        table.insert(log, 1, value)
        if #log > 7 then table.remove(log) end

        local values = { last = value, log = table.concat(log, "  |  ") }

        local fx = value:match("level@([%d.]+),")
        if fx then values.level = tonumber(fx) end

        if value == "down:hold" then values.held = 1
        elseif value == "up:hold" or value == "leave:hold" then values.held = 0 end

        if value == "swap" then
            showA = 1 - showA
            values.showA = showA
        end

        if value == "drop:chip>binB" then values.inB = 1
        elseif value == "drop:chip>binA" then values.inB = 0 end

        data:set_props({ keep = 1, snap = 1, data = values })
        ui:commit()
    end,
})

data = ui:element({
    id = "hands_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "hands", keep = 1, data = { level = 0.35, held = 0, inB = 0, showA = 1 } },
})

ui:commit()
