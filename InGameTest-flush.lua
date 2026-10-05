-- What actually happens to a declaration's `data` when a later set_props follows it?
--
-- REFERENCE.md says: "Commit between declaring the element and the first set_props, or the
-- declaration is lost", and explains it as ScriptedScreens merging successive upserts into one.
-- Reading the decompile, that explanation is wrong -- the host CLONES the element, APPENDS one
-- op per upsert and never coalesces them -- so the rule needs re-measuring rather than
-- re-wording from a second guess.
--
-- Two cases, because the documented example may not test what it claims:
--
--   A  declare at LOAD, set_props in TICK, no explicit commit.
--      These are different Execute calls, and the host flushes as a postfix on EVERY Execute,
--      so a flush already happened between them. `long` should SURVIVE.
--
--   B  declare and set_props in the SAME tick, no commit between.
--      This is the real "no flush between" case. If anything is lost, it is lost here.
--
-- Each case shows its own `long`. "LOST" means the declaration's data never arrived.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

local function panel(title, note)
    return table.concat({
        "SCENE w=200 h=120 fit=stretch size=5 f=#EAF4F8",
        "R x=0 y=0 w=200 h=120 f=#0B1622 fea=0",
        'T x=6 y=4 w=188 h=8 size=7 f=#EAF4F8 text="' .. title .. '"',
        'T x=6 y=15 w=188 h=14 size=5 f=#8FA6B8 wrap=1 text="' .. note .. '"',
        'T x=6 y=36 w=60 h=8 size=5 f=#53646F text="long ="',
        'T x=50 y=34 w=144 h=10 size=8 f=$lcol text=$long missing="LOST"',
        'T x=6 y=54 w=60 h=8 size=5 f=#53646F text="pct ="',
        'T x=50 y=52 w=144 h=10 size=8 f=#7FB2F0 text=$pct missing="(none)"',
        'T x=6 y=72 w=188 h=16 size=5 f=#53646F wrap=1 text=$verdict missing="waiting"',
    }, "\n")
end

ui:element({
    id = "a_s", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = (H - 16) / 2 },
    props = { scene = "ca", src = panel("A  declare at load, set_props in tick",
        "a flush happens between them, so long should survive") },
})

ui:element({
    id = "b_s", type = "vector",
    rect = { unit = "px", x = 4, y = 8 + (H - 16) / 2, w = W - 8, h = (H - 16) / 2 },
    props = { scene = "cb", src = panel("B  declare and set_props in ONE tick",
        "no flush between -- the real test of the documented rule") },
})

-- CASE A: the data element is declared here, at load. `long` is sent once and never again.
local a = ui:element({
    id = "a_d", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
    props = { scene = "ca", keep = 1,
              data = { long = "SURVIVED", lcol = "#5FD9A8" } },
})

local ticks = 0

function tick()
    ticks = ticks + 1

    -- CASE A: only `pct` from here on. No commit anywhere in this function.
    a:set_props({ scene = "ca", keep = 1, data = { pct = ticks } })

    -- CASE B: declared and then patched within this same Execute, so nothing can flush
    -- between the two. If the documented rule is real, this is where it bites.
    local b = ui:element({
        id = "b_d", type = "vector",
        rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
        props = { scene = "cb", keep = 1,
                  data = { long = "SURVIVED", lcol = "#5FD9A8" } },
    })

    b:set_props({ scene = "cb", keep = 1, data = { pct = ticks } })
end
