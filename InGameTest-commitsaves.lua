-- Does ui:commit() between two data-bearing writes actually save the first?
--
-- REFERENCE has claimed this since 0.11.82 ("add ui:commit() and both arrive"), and the
-- VectorBuilder exporter's guard now exempts a pair that has a commit between them. But that
-- claim comes from the same paragraph that has been wrong twice today, and it was measured in
-- an earlier session under assumptions since disproven. If a commit does NOT save the first
-- write, that guard is silent on a real data loss.
--
--   1  commit between the two writes   -> both $a and $b must show
--   2  no commit (the control)         -> $a MISSING, $b shows
--
-- If row 1 shows $a MISSING too, the commit exemption is false and the guard needs removing.

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
        'T x=6 y=38 w=26 h=8 size=6 f=#53646F text="$a ="',
        'T x=34 y=35 w=160 h=12 size=9 f=#5FD9A8 text=$a missing="MISSING"',
        'T x=6 y=58 w=26 h=8 size=6 f=#53646F text="$b ="',
        'T x=34 y=55 w=160 h=12 size=9 f=#7FB2F0 text=$b missing="MISSING"',
    }, "\n")
end

ui:element({
    id = "c1_s", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = (H - 16) / 2 },
    props = { scene = "cm1", src = panel("1  commit BETWEEN the two data writes",
        "the exemption under test: both must show") },
})

ui:element({
    id = "c2_s", type = "vector",
    rect = { unit = "px", x = 4, y = 8 + (H - 16) / 2, w = W - 8, h = (H - 16) / 2 },
    props = { scene = "cm2", src = panel("2  control: no commit between",
        "known: $a is lost") },
})

local c1 = ui:element({
    id = "c1_d", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
    props = { scene = "cm1", keep = 1 },
})

local c2 = ui:element({
    id = "c2_d", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
    props = { scene = "cm2", keep = 1 },
})

function tick()
    -- 1: a commit separates the two data-bearing writes.
    c1:set_props({ scene = "cm1", keep = 1, data = { a = "A-SURVIVED" } })
    ui:commit()
    c1:set_props({ scene = "cm1", keep = 1, data = { b = "B-SECOND" } })

    -- 2: the control, no commit.
    c2:set_props({ scene = "cm2", keep = 1, data = { a = "A-SURVIVED" } })
    c2:set_props({ scene = "cm2", keep = 1, data = { b = "B-SECOND" } })
end
