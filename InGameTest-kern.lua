-- 0.11.59: `kern` on a T node.
--
-- Kerning is on by default, like a browser: a page opts OUT with `kern=0`, never in.
--
-- WHAT TO SEE
--
-- 1 PAIRS. The same string twice in a face that carries kerning pairs. The top line is the
--   default (kerned) and the bottom has `kern=0`. The strings are chosen for pairs that kern
--   hard -- AV, AW, To, Ye, P. -- so the kerned line should be visibly SHORTER and the gaps
--   after A, T, P and V visibly tighter. If the two lines are identical, either the face has no
--   pair records or the attribute is not reaching the label.
--
-- 2 INHERITED. `kern=0` set once on the group, not on the labels. Both lines below it must be
--   unkerned, the same as row 1's bottom line. This is the `style` inheritance path, which is a
--   different code path from an attribute on the node itself.
--
-- 3 THE POOL. Labels alternate kerned, unkerned, kerned, unkerned down the column. Labels are
--   pooled BY INDEX and reused between rebuilds, so a label that does not ask for `kern` must
--   not inherit the setting of whatever used that slot last. If the alternation is clean, the
--   reset is unconditional as intended. If two neighbours ever match, the pool is leaking --
--   the same fault `font` had when placement order changed.
--
-- 4 DEFAULT FACE. The same pair of lines on the game's own font. Both must look IDENTICAL:
--   the stock font assets carry no kerning pairs, so there is nothing for `kern` to switch off.
--   A difference here would mean the attribute is doing something other than kerning.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

-- A face the companion fonts mod builds. If it is not installed the text falls back to the
-- default face and rows 1 to 3 will simply look like row 4 -- which is worth knowing rather
-- than mistaking for the attribute failing.
local FACE = "Barlow"
local PAIRS = "AVATAR Wave To Yesterday P."

local s = { "SCENE w=200 h=200 fit=stretch" }
local function line(txt) s[#s + 1] = txt end

line("R x=0 y=0 w=200 h=200 f=#0B1622 fea=0")

-- 1 PAIRS
line('T x=4 y=4 w=192 h=6 text="1 PAIRS  (top kerned, bottom kern=0)" size=5 f=#5FD9A8')
line(('T x=4 y=12 w=192 h=10 text="%s" size=9 font=%s f=#EAF4F8'):format(PAIRS, FACE))
line(('T x=4 y=24 w=192 h=10 text="%s" size=9 font=%s kern=0 f=#EAF4F8'):format(PAIRS, FACE))

-- 2 INHERITED from the group's style
line('T x=4 y=40 w=192 h=6 text="2 INHERITED  (kern=0 on the group)" size=5 f=#5FD9A8')
-- The text form has no map syntax, so a group carries its defaults as bare attributes.
line(('G font=%s kern=0 size=9 {'):format(FACE))
line(('  T x=4 y=48 w=192 h=10 text="%s" f=#EAF4F8'):format(PAIRS))
line(('  T x=4 y=60 w=192 h=10 text="%s" f=#EAF4F8'):format(PAIRS))
line("}")

-- 3 THE POOL: alternating, to catch a label inheriting its slot's last setting
line('T x=4 y=76 w=192 h=6 text="3 POOL  (must alternate cleanly)" size=5 f=#5FD9A8')
for i = 0, 3 do
    local y = 84 + i * 12
    local off = (i % 2 == 1) and " kern=0" or ""
    local tag = (i % 2 == 1) and "off" or "ON "
    line(('T x=4 y=%d w=192 h=10 text="%s  %s" size=9 font=%s%s f=#EAF4F8')
        :format(y, tag, PAIRS, FACE, off))
end

-- 4 DEFAULT FACE: no pair records, so kern must make no difference at all
line('T x=4 y=136 w=192 h=6 text="4 DEFAULT FACE  (both must match)" size=5 f=#5FD9A8')
line(('T x=4 y=144 w=192 h=10 text="%s" size=9 f=#EAF4F8'):format(PAIRS))
line(('T x=4 y=156 w=192 h=10 text="%s" size=9 kern=0 f=#EAF4F8'):format(PAIRS))

ui:element({
    id = "kern_test", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = { scene = "kern", src = table.concat(s, "\n") },
})
