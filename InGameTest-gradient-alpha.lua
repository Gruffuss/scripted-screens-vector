-- 0.11.60: gradient stops interpolate in premultiplied alpha, as CSS does.
--
-- A transparent stop contributes its alpha and NONE of its colour. So fading to #00000000 and
-- fading to the same hue at alpha 0 are now the same thing, and the old advice to "fade to the
-- same colour, not to black" is gone from the docs.
--
-- WHAT TO SEE
--
-- 1 THE PAIR. Two bars, same hue, over a light panel so a dark midtone would be obvious.
--     top    -> #00000000   (fade to transparent BLACK)
--     bottom -> #5FD9A800   (fade to the same hue at alpha 0)
--   They must now look IDENTICAL. Before this change the top one greyed through its middle;
--   that is the whole bug. If the top is still darker in the middle, the change did not land.
--
-- 2 OVER WHITE. The same pair on a near-white panel. This is the harsher test: a muddy midtone
--   hides against a dark background and cannot hide against a pale one. Both bars must fade
--   smoothly to the panel with no grey band and no colour shift.
--
-- 3 THE EXTREME. Magenta fading to transparent GREEN. Premultiplied, the green is weightless,
--   so this must read as magenta fading out -- never a muddy olive band in the middle. This is
--   the case that fails most visibly if the mix is straight.
--
-- 4 CONTROL. A ramp between two OPAQUE colours, magenta to green. Premultiplying changes
--   nothing when both stops are opaque, so this must look exactly as it always has: a clean
--   magenta-to-green ramp through grey-ish middle tones. If THIS one changed, the mix is wrong
--   for the ordinary case and that matters far more than the transparent one.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

local s = {
    "SCENE w=200 h=200 fit=stretch",
    "DEFS {",
    -- units=bbox so each ramp is bound to the bar that uses it, whatever its size.
    ' GL id=toBlack units=bbox x1=0 y1=0 x2=1 y2=0 stops=[[0,"#5FD9A8FF"],[1,"#00000000"]]',
    ' GL id=toSelf  units=bbox x1=0 y1=0 x2=1 y2=0 stops=[[0,"#5FD9A8FF"],[1,"#5FD9A800"]]',
    ' GL id=magGreen units=bbox x1=0 y1=0 x2=1 y2=0 stops=[[0,"#FF2FD0FF"],[1,"#2FFF6A00"]]',
    ' GL id=magGreenOpaque units=bbox x1=0 y1=0 x2=1 y2=0 stops=[[0,"#FF2FD0FF"],[1,"#2FFF6AFF"]]',
    "}",

    -- 1 THE PAIR, over a mid panel
    "R x=0 y=0 w=200 h=52 f=#243447 fea=0",
    'T x=4 y=2 w=192 h=6 text="1 THE PAIR  (both must match)" size=5 f=#EAF4F8',
    "R x=8 y=12 w=184 h=14 f=@toBlack fea=0",
    'T x=8 y=27 w=100 h=5 text="to #00000000" size=4 f=#9FB3C8',
    "R x=8 y=34 w=184 h=14 f=@toSelf fea=0",

    -- 2 OVER WHITE: a grey midtone cannot hide here
    "R x=0 y=52 w=200 h=52 f=#EDF2F7 fea=0",
    'T x=4 y=54 w=192 h=6 text="2 OVER WHITE  (no grey band)" size=5 f=#16202B',
    "R x=8 y=64 w=184 h=14 f=@toBlack fea=0",
    "R x=8 y=86 w=184 h=14 f=@toSelf fea=0",

    -- 3 THE EXTREME: the transparent stop is a different hue entirely
    "R x=0 y=104 w=200 h=48 f=#101820 fea=0",
    'T x=4 y=106 w=192 h=6 text="3 EXTREME  magenta -> transparent green" size=5 f=#EAF4F8',
    "R x=8 y=116 w=184 h=28 f=@magGreen fea=0",

    -- 4 CONTROL: both stops opaque, so nothing should have changed at all
    "R x=0 y=152 w=200 h=48 f=#101820 fea=0",
    'T x=4 y=154 w=192 h=6 text="4 CONTROL  both opaque, must be unchanged" size=5 f=#EAF4F8',
    "R x=8 y=164 w=184 h=28 f=@magGreenOpaque fea=0",
}

ui:element({
    id = "grad_alpha", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = { scene = "gradalpha", src = table.concat(s, "\n") },
})
