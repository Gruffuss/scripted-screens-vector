-- Does the single delivered payload carry the LAST CALL's props only, or the element's props
-- MERGED across all calls in that execution?
--
-- Settled already: an element's payloads collapse within one chip execution and only one is
-- delivered. The open question is what that one carries.
--
-- The discriminator needs no instrumentation. In one execution:
--
--     set_props { scene, keep, data = { v = "..." } }     -- carries the data
--     set_props { snap = 1 }                              -- carries NO data, NO scene
--
--   MERGED        the delivered element still has scene + data, so $v reads SET-BY-FIRST
--   LAST-ONLY     the delivered element is just { snap = 1 } -- no scene, no data -- so the
--                 payload never reaches this scene at all and $v stays MISSING
--
-- Row 2 is the control: one set_props per execution, which must always show its value.

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
        'T x=6 y=4 w=188 h=8 size=7 text="' .. title .. '"',
        'T x=6 y=15 w=188 h=12 size=5 f=#8FA6B8 wrap=1 text="' .. note .. '"',
        'T x=6 y=40 w=30 h=8 size=6 f=#53646F text="$v ="',
        'T x=36 y=36 w=158 h=14 size=11 f=#5FD9A8 text=$v missing="MISSING"',
    }, "\n")
end

ui:element({
    id = "p1_s", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = (H - 16) / 2 },
    props = { scene = "pm1", src = panel("1  set_props(data) then set_props(snap)",
        "MERGED -> SET-BY-FIRST.  LAST-ONLY -> MISSING") },
})

ui:element({
    id = "p2_s", type = "vector",
    rect = { unit = "px", x = 4, y = 8 + (H - 16) / 2, w = W - 8, h = (H - 16) / 2 },
    props = { scene = "pm2", src = panel("2  control: one set_props per execution",
        "must always show its value") },
})

local p1 = ui:element({
    id = "p1_d", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
    props = { scene = "pm1", keep = 1 },
})

local p2 = ui:element({
    id = "p2_d", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
    props = { scene = "pm2", keep = 1 },
})

function tick()
    -- The question. Second call names neither scene nor data.
    p1:set_props({ scene = "pm1", keep = 1, data = { v = "SET-BY-FIRST" } })
    p1:set_props({ snap = 1 })

    -- The control.
    p2:set_props({ scene = "pm2", keep = 1, data = { v = "CONTROL-OK" } })
end
