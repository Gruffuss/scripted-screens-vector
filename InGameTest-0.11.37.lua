-- 0.11.37 — hit positions, hover events, drag and drop, fast events, group blur, data: images.
--
-- What to do and what to see:
--   1. SLIDER    press anywhere on the bar: the fill jumps to that point and LAST reads
--                "down:level@0.xxx,0.500"-ish (x from the left, 0..1). Release: "up:level@...".
--   2. HOVER     move the pointer onto the blue box: LAST reads "enter:hov", the box lights.
--                Move off: "exit:hov", it goes dark again. Nothing while moving inside it.
--   3. DRAG      drag the yellow chip from bin A into bin B and let go there: the log shows
--                "dragstart:chip", "drop:chip>binB", "dragend:chip" (newest first) and the chip
--                is drawn in B. Drag it back to A likewise. Released outside both bins: only
--                dragstart and dragend, and the chip stays where it was.
--   4. FAST      tap the HOLD button as fast as you can: every tap shows its own "down:hold" /
--                "up:hold" pair in the log, where 0.11.36 merged taps under a quarter second.
--   5. BLUR      the left square sits in G blur=6, the right one has a box shadow with blur 12
--                and a transparent fill. The two soft squares should look the same.
--   6. IMAGES    both pictures come from data: URLs, no download. The GIF shows red and green
--                on top, blue bottom left, and the background grid through the bottom right.
--                The BMP beside it: red, green on top; blue, white below; hard pixels.
-- The event log keeps the last six values, newest first.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 480, 480
if size then W, H = size.w, size.h end

ui:clear()

local GIF = "data:image/gif;base64,R0lGODlhBAAEAIEAAP8AAADIAABQ/////yH5BAEAAAMALAAAAAAEAAQAAAIKBJgQYEKJGyVuBQA7"
local BMP = "data:image/bmp;base64,Qk1GAAAAAAAAADYAAAAoAAAAAgAAAAIAAAABABgAAAAAABAAAAAAAAAAAAAAAAAAAAAAAAAA/wAA////AAAAAP8A/wAAAA=="

local src = [==[
SCENE w=200 h=200 fit=stretch
R x=0 y=0 w=200 h=200 f=#070D16

T x=6 y=4 w=90 h=7 text="1 SLIDER (XY)" size=5 f=#5FD9A8
R id=level press=1 xy=1 x=6 y=12 w=90 h=10 rx=3 f=#12202F
R x=6 y=12 w="=90*$level" h=10 rx=3 f=#2E8B6E

T x=104 y=4 w=90 h=7 text="2 HOVER (HOVEREV)" size=5 f=#5FD9A8
R id=hov hoverev=1 x=104 y=12 w=90 h=10 rx=3 f=#1E3A5F
G o=$lit { R x=104 y=12 w=90 h=10 rx=3 f=#5FA8FF }

T x=6 y=28 w=120 h=7 text="3 DRAG CHIP INTO A BIN" size=5 f=#5FD9A8
R id=binA drop=1 x=6 y=36 w=60 h=28 rx=4 f=#12202F s=#3A5A7A sw=1
R id=binB drop=1 x=74 y=36 w=60 h=28 rx=4 f=#12202F s=#3A5A7A sw=1
T x=6 y=58 w=60 h=6 text="A" size=5 align=center f=#8FA6B8
T x=74 y=58 w=60 h=6 text="B" size=5 align=center f=#8FA6B8
R id=chip drag=1 x="=16+68*$inB" y=40 w=40 h=14 rx=7 f=#F5D76E

T x=142 y=28 w=52 h=7 text="4 FAST" size=5 f=#5FD9A8
R id=hold press=1 x=142 y=36 w=52 h=28 rx=4 f=#B7791F
T x=142 y=46 w=52 h=10 text="HOLD" size=7 align=center f=#EAF4F8

T x=6 y=70 w=120 h=7 text="5 BLUR: THE TWO SHOULD MATCH" size=5 f=#5FD9A8
G blur=6 { R x=20 y=86 w=30 h=30 f=#5FD9A8 }
R x=74 y=86 w=30 h=30 f=#5FD9A800 fea=0 sh=[[0,0,12,0,"#5FD9A8FF"]]

T x=130 y=70 w=64 h=7 text="6 DATA: IMAGES" size=5 f=#5FD9A8
RP n=4 { R x="=130+i*8" y=80 w=4 h=40 f=#26384A }
IMG x=130 y=80 w=30 h=30 src="]==] .. GIF .. [==[" smp=point
IMG x=164 y=80 w=30 h=30 src="]==] .. BMP .. [==[" smp=point

T x=6 y=140 w=180 h=7 text="LAST" size=5 f=#5FD9A8
T x=6 y=148 w=188 h=10 text=$last size=7 f=#EAF4F8 missing="(nothing yet)"
T x=6 y=162 w=188 h=34 text=$log size=5 f=#8FA6B8 wrap=1 missing=""
]==]

local log = {}
local data

ui:element({
    id = "t37_s", type = "vector",
    rect = { unit = "px", x = 6, y = 6, w = W - 12, h = H - 12 },
    props = { scene = "t37", src = src },
    on_click = function(value, player)
        table.insert(log, 1, value)
        if #log > 6 then table.remove(log) end

        local values = { last = value, log = table.concat(log, "  |  ") }

        local fx = value:match("^%a*:?level@([%d.]+),")
        if fx then values.level = tonumber(fx) end
        if value == "enter:hov" then values.lit = 1 elseif value == "exit:hov" then values.lit = 0 end
        if value == "drop:chip>binB" then values.inB = 1 elseif value == "drop:chip>binA" then values.inB = 0 end

        data:set_props({ keep = 1, snap = 1, data = values })
        ui:commit()
    end,
})

data = ui:element({
    id = "t37_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "t37", keep = 1, data = { level = 0.3, lit = 0, inB = 0 } },
})

ui:commit()
