-- A deep number inside a COLOUR expression used to end the game process (fixed in 0.11.95).
--
-- `f = "=mix(#a,#b,<deep>)"` and `f = "=if(<deep>,#a,#b)"` overflowed the stack. A stack overflow
-- cannot be caught in .NET, so it took the whole process down with nothing in the log -- the same
-- failure as the four crashes 0.11.77-0.11.84 capped against and 0.11.85-0.11.86 was supposed to
-- have removed. It was the EIGHTH recursive walk; seven were converted and this one was missed.
--
-- Measured out of process before the fix: both forms survived 7,968 levels of prefix `-` and died
-- at 8,125 with "Stack overflow.", exit 127. The same number as an ordinary numeric attribute
-- survived 60,000, which is what pinned the fault to the colour path rather than to depth itself.
-- After the fix both survive 200,000.
--
-- WHAT SHOULD BE SEEN: four coloured bars and the word OK. If the page is on the console at all,
-- the test has already passed -- the failure mode is that the GAME IS NOT RUNNING.
--
--   PASS   the page draws; rows 1 and 2 are mid-green, row 3 is green, row 4 is the control
--   FAIL   the game dies on push, or on the rebuild after it
--
-- DEPTH is deliberately past the old breaking point. Raise it if you want a harder test; the
-- fix is depth-independent, so there is no number that should bring it back.

local ui = ss.ui.surface("main")
ss.ui.activate("main")
ui:clear()

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

local DEPTH = 12000
local deep = string.rep("-", DEPTH) .. "1"       -- DEPTH unary minus nodes over a literal 1

local src = table.concat({
    "SCENE w=200 h=200 fit=stretch size=5 f=#EAF4F8",
    "R x=0 y=0 w=200 h=200 f=#0B1622 fea=0",
    'T x=6 y=4 w=188 h=7 size=6 text="a deep number inside a colour expression"',

    -- 1  mix's blend factor: the number reached through Arg, at DEPTH levels
    'T x=6 y=18 w=188 h=6 size=5 f=#53646F text="1  mix blend factor, ' .. DEPTH .. ' deep"',
    'R x=6 y=26 w=188 h=18 fea=0 f="=mix(#5FD9A8,#2E8B6E,' .. deep .. ')"',

    -- 2  the same, nested one level further inside another mix
    'T x=6 y=50 w=188 h=6 size=5 f=#53646F text="2  the same, inside another mix"',
    'R x=6 y=58 w=188 h=18 fea=0 f="=mix(#5FD9A8,mix(#2E8B6E,#1B5E4A,' .. deep .. '),0.5)"',

    -- 3  if's condition, the other unguarded argument
    'T x=6 y=82 w=188 h=6 size=5 f=#53646F text="3  if condition, ' .. DEPTH .. ' deep"',
    'R x=6 y=90 w=188 h=18 fea=0 f="=if(' .. deep .. ',#5FD9A8,#E23D3D)"',

    -- 4  the control: the same depth as an ordinary NUMERIC attribute, which always worked
    'T x=6 y=114 w=188 h=6 size=5 f=#53646F text="4  control: same depth as a number, not a colour"',
    'R x=6 y=122 w=188 h=18 f=#7FB2F0 fea=0 o="=1+0*(' .. deep .. ')"',

    'T x=6 y=150 w=188 h=10 size=8 f=#5FD9A8 text="OK -- the game is still running"',
    'T x=6 y=168 w=188 h=24 size=4 f=#53646F wrap=1 text="Before 0.11.95 rows 1-3 ended the process. Row 4 never did: the same number as an ordinary attribute took the iterative path."',
}, "\n")

ui:element({
    id = "cd_s", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = { scene = "cd", src = src },
})

ui:commit()
