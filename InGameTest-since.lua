-- ANSWERED: ANY payload restarts `since($name)`, a nodes-only patch included, because the
-- flushed payload carries the element's accumulated `data` along with it. Documented in
-- REFERENCE.md as the `since()` trap, with the remedy: send geometry patches from a DIFFERENT
-- element than the one carrying the animated data. Keep this page -- it is the regression test,
-- and a CAPTURE CANNOT REPLACE IT: a capture replays the stored elements and re-stamps every
-- data name, so `since()` reads 0 in every capture regardless of the truth. This has to be read
-- off the live screen.
--
-- The question it was written to settle:
--
-- What restarts `since($name)`?
--
-- `since` is Clock - ArrivedAt[name], and a name is stamped whenever a payload carrying it is
-- applied. The question is what counts as "carrying it", given that a flushed payload carries
-- the element's ACCUMULATED properties -- so a patch that mentions only `nodes` still brings
-- the element's existing `data` along with it.
--
--   1  data sent ONCE, element never touched again
--        -> nothing re-arrives, since($x) must CLIMB
--   2  data sent once, then a NODES patch on the same element every tick
--        -> the accumulated `data` rides along, so since($y) should stay near ZERO
--
-- A third readout proves the page is live, so a frozen number cannot be mistaken for a result.
-- Predicted by a tool mirroring this format; this is the measurement.

local ui = ss.ui.surface("main")
ss.ui.activate("main")
ui:clear()

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

local function panel(title, note, name)
    return table.concat({
        "SCENE w=200 h=100 fit=stretch size=5 f=#EAF4F8",
        "R x=0 y=0 w=200 h=100 f=#0B1622 fea=0",
        'T x=5 y=3 w=190 h=8 size=7 text="' .. title .. '"',
        'T x=5 y=14 w=190 h=12 size=5 f=#8FA6B8 wrap=1 text="' .. note .. '"',
        'T x=5 y=38 w=60 h=8 size=6 f=#53646F text="since($' .. name .. ') ="',
        'T x=62 y=34 w=80 h=14 size=11 f=#5FD9A8 text="{=since($' .. name .. '):%.1f} s"',
        -- Proof the panel is live: this must climb in both rows whatever since() does.
        'T x=146 y=38 w=50 h=8 size=5 f=#53646F text="t {=t:%.0f}"',
        "R id=mark x=5 y=56 w=20 h=10 rx=2 f=#53646F fea=0",
    }, "\n")
end

ui:element({
    id = "q1_s", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = (H - 16) / 2 },
    props = { scene = "q1", src = panel("1  data once, element never touched again",
        "nothing re-arrives -- since must CLIMB", "x") },
})

ui:element({
    id = "q2_s", type = "vector",
    rect = { unit = "px", x = 4, y = 8 + (H - 16) / 2, w = W - 8, h = (H - 16) / 2 },
    props = { scene = "q2", src = panel("2  data once, then nodes patches every tick",
        "accumulated data rides along -- since should stay ~0", "y") },
})

local d1 = ui:element({
    id = "q1_d", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
    props = { scene = "q1", keep = 1 },
})

local d2 = ui:element({
    id = "q2_d", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
    props = { scene = "q2", keep = 1 },
})

local n = 0

function tick()
    n = n + 1

    if n == 1 then
        d1:set_props({ scene = "q1", keep = 1, data = { x = 1 } })
        d2:set_props({ scene = "q2", keep = 1, data = { y = 1 } })
        return
    end

    -- Row 2 only: a nodes-bearing patch, carrying no data of its own.
    d2:set_props({ scene = "q2", nodes = { mark = { w = 20 + (n % 5) * 6 } } })
end
