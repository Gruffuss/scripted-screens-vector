-- Does `snap` stay on an element after the one payload that carried it?
--
-- REFERENCE promises snap applies "for one payload" and that "a later payload without snap eases
-- that name again". But a flushed payload carries the element's accumulated properties, so a
-- `snap = 1` written once may still be on the element for ever -- in which case every later
-- value snaps and the promise is false.
--
-- Two rows, identical but for one thing: row 1 sent `snap = 1` with its FIRST payload and never
-- again; row 2 never sent it at all. Both ease `w` over 20 seconds and both step `w` from 10 to
-- 190 on the same tick, so they can be compared inside a single frame and no capture timing is
-- needed.
--
--   SNAP STICKS   row 1 reads 190 immediately while row 2 is still climbing
--   SNAP IS ONE   both rows climb together and read the same
--
-- The numbers are printed as well as drawn, so this is read rather than eyeballed.

local ui = ss.ui.surface("main")
ss.ui.activate("main")
ui:clear()

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

local function panel(title, note)
    return table.concat({
        "SCENE w=200 h=100 fit=stretch size=5 f=#EAF4F8",
        "R x=0 y=0 w=200 h=100 f=#0B1622 fea=0",
        'T x=5 y=3 w=190 h=8 size=7 text="' .. title .. '"',
        'T x=5 y=14 w=190 h=10 size=5 f=#8FA6B8 wrap=1 text="' .. note .. '"',
        "R x=5 y=30 w=190 h=20 rx=2 f=#16222E fea=0",
        'R x=5 y=30 w="=$w" h=20 rx=2 f=#5FD9A8 fea=0 missing=0',
        'T x=5 y=56 w=40 h=8 size=6 f=#53646F text="w ="',
        'T x=40 y=53 w=80 h=12 size=10 f=#F5D76E text=$w fmt=%.0f missing="-"',
        'T x=120 y=56 w=75 h=8 size=5 f=#53646F text="tick: {=$tk:%.0f}"',
    }, "\n")
end

ui:element({
    id = "sa_s", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = (H - 16) / 2 },
    props = { scene = "sa", src = panel("1  snap sent ONCE, with the first payload",
        "if snap stuck, this jumps straight to 190") },
})

ui:element({
    id = "sb_s", type = "vector",
    rect = { unit = "px", x = 4, y = 8 + (H - 16) / 2, w = W - 8, h = (H - 16) / 2 },
    props = { scene = "sb", src = panel("2  control: snap never sent",
        "this must always ease") },
})

local a = ui:element({
    id = "sa_d", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
    props = { scene = "sa", keep = 1 },
})

local b = ui:element({
    id = "sb_d", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
    props = { scene = "sb", keep = 1 },
})


local tk = 0

function tick()
    tk = tk + 1

    -- A long ease so the climb is wide open and no capture has to be timed.
    local w = (tk < 15) and 10 or 190

    -- `ease` rides on EVERY payload. Sent once it would apply to that payload only, and the
    -- default glide is the measured gap between payloads -- about half a second here, far too
    -- fast to sample, which is what made the first version of this test blind.
    local ease = { w = 20 }

    if tk == 1 then
        -- The ONLY payload row 1 ever marks as snapped.
        a:set_props({ scene = "sa", keep = 1, snap = 1, ease = ease,
                      data = { w = w, tk = tk } })
        b:set_props({ scene = "sb", keep = 1, ease = ease,
                      data = { w = w, tk = tk } })
        return
    end

    -- Neither row mentions snap from here on.
    if tk < 30 then
        -- Still carrying the snap written at tick 1, if it persists at all.
        a:set_props({ scene = "sa", keep = 1, ease = ease, data = { w = w, tk = tk } })
    else
        -- The remedy under test: write it back off explicitly.
        a:set_props({ scene = "sa", keep = 1, snap = 0, ease = ease, data = { w = w, tk = tk } })
    end
    b:set_props({ scene = "sb", keep = 1, ease = ease, data = { w = w, tk = tk } })
end
