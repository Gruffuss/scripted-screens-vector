-- ANSWERED: (a). A flushed payload carries the element's WHOLE accumulated props, so `keep = 1`
-- written once stays on the element and every later payload is a patch. The host clones the
-- element per change and `set_props` merges per prop against that clone; it also COALESCES an
-- element's successive upserts inside one chip execution into a single delivery carrying only
-- the last payload. Keep this page: it is the regression test for that, and it needs no clock.
--
-- The question it was written to settle:
--
-- Does a flushed payload carry the element's WHOLE accumulated props, or only what was written
-- since the last flush? Discriminated through `keep`.
--
--   tick 1   set_props { scene, keep = 1, data = { p = "P-FIRST" } }
--   tick 2+  set_props { scene,           data = { q = "Q-LATER" } }    -- NO keep
--
--   (a) whole accumulated state -> keep = 1 is still on the element, so the later payload is a
--       PATCH and $p survives beside $q
--   (b) only what was written   -> keep is absent, so the later payload is the whole truth and
--       $p is WIPED
--
-- Row 2 is the control: it repeats keep = 1 every tick, so $p must survive there whatever the
-- answer. If row 2 loses $p, the test is wrong rather than the mod.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

local function panel(title, note)
    return table.concat({
        "SCENE w=200 h=100 fit=stretch size=5 f=#EAF4F8",
        "R x=0 y=0 w=200 h=100 f=#0B1622 fea=0",
        'T x=5 y=3 w=190 h=8 size=7 text="' .. title .. '"',
        'T x=5 y=14 w=190 h=12 size=5 f=#8FA6B8 wrap=1 text="' .. note .. '"',
        'T x=5 y=38 w=26 h=8 size=6 f=#53646F text="$p ="',
        'T x=33 y=35 w=162 h=12 size=9 f=#5FD9A8 text=$p missing="WIPED"',
        'T x=5 y=58 w=26 h=8 size=6 f=#53646F text="$q ="',
        'T x=33 y=55 w=162 h=12 size=9 f=#7FB2F0 text=$q missing="(none yet)"',
    }, "\n")
end

ui:element({
    id = "k1_s", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = (H - 16) / 2 },
    props = { scene = "k1", src = panel("1  keep sent ONCE, later patches omit it",
        "(a) $p survives -- keep persisted.  (b) $p WIPED") },
})

ui:element({
    id = "k2_s", type = "vector",
    rect = { unit = "px", x = 4, y = 8 + (H - 16) / 2, w = W - 8, h = (H - 16) / 2 },
    props = { scene = "k2", src = panel("2  control: keep repeated every tick",
        "$p must survive here either way") },
})

local k1 = ui:element({
    id = "k1_d", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
    props = { scene = "k1" },
})

local k2 = ui:element({
    id = "k2_d", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
    props = { scene = "k2" },
})

local ticks = 0

function tick()
    ticks = ticks + 1

    if ticks == 1 then
        k1:set_props({ scene = "k1", keep = 1, data = { p = "P-FIRST" } })
        k2:set_props({ scene = "k2", keep = 1, data = { p = "P-FIRST" } })
        return
    end

    -- Row 1 omits keep from here on; row 2 keeps repeating it.
    k1:set_props({ scene = "k1", data = { q = "Q-LATER" } })
    k2:set_props({ scene = "k2", keep = 1, data = { q = "Q-LATER" } })
end
