-- WHERE does a payload get lost inside one chip execution?
--
-- Measured already: declaring an element and patching it in the same execution loses the
-- declaration's data. ScriptedScreens delivers BOTH ops, per op, with distinct payloads, and
-- `keep = 1` promises a payload is a patch -- so losing one is this mod's bug, not the host's.
-- Before guessing at a cause, narrow WHICH pair loses it.
--
--   A  declared at load, then TWO set_props in one tick, each naming a different key.
--      If keep works per op, $a $b $c all show. If the first patch dies, $b is missing.
--
--   B  re-declared with ui:element and then patched, both in one tick (the known-bad case),
--      kept here as the control so both run on the same build in the same frame.
--
-- Reading it:  A all three present + B missing  -> the loss is specific to RE-DECLARATION
--              A missing $b     + B missing     -> any two ops in one execution lose the first

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

local function panel(title, note, keys)
    local rows = {
        "SCENE w=200 h=100 fit=stretch size=5 f=#EAF4F8",
        "R x=0 y=0 w=200 h=100 f=#0B1622 fea=0",
        'T x=6 y=4 w=188 h=8 size=7 text="' .. title .. '"',
        'T x=6 y=15 w=188 h=12 size=5 f=#8FA6B8 wrap=1 text="' .. note .. '"',
    }
    for i, k in ipairs(keys) do
        local y = 34 + (i - 1) * 16
        rows[#rows + 1] = 'T x=6 y=' .. y .. ' w=40 h=8 size=6 f=#53646F text="$' .. k .. ' ="'
        rows[#rows + 1] = 'T x=46 y=' .. (y - 2) .. ' w=148 h=12 size=9 f=#5FD9A8 text=$'
            .. k .. ' missing="MISSING"'
    end
    return table.concat(rows, "\n")
end

ui:element({
    id = "a_s", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = (H - 16) / 2 },
    props = { scene = "fa", src = panel("A  two set_props in one tick",
        "declared at load; each patch names a different key", { "a", "b", "c" }) },
})

ui:element({
    id = "b_s", type = "vector",
    rect = { unit = "px", x = 4, y = 8 + (H - 16) / 2, w = W - 8, h = (H - 16) / 2 },
    props = { scene = "fb", src = panel("B  re-declare then patch, one tick",
        "the known-bad case, as a control on the same build", { "d", "e" }) },
})

-- A: declared ONCE at load, carrying `a`.
local a = ui:element({
    id = "a_d", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
    props = { scene = "fa", keep = 1, data = { a = "A-FROM-LOAD" } },
})

function tick()
    -- A: two patches in ONE execution, different keys, no commit between.
    a:set_props({ scene = "fa", keep = 1, data = { b = "B-FIRST-PATCH" } })
    a:set_props({ scene = "fa", keep = 1, data = { c = "C-SECOND-PATCH" } })

    -- B: the known-bad shape, for comparison in the same frame.
    local b = ui:element({
        id = "b_d", type = "vector",
        rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
        props = { scene = "fb", keep = 1, data = { d = "D-FROM-DECLARE" } },
    })
    b:set_props({ scene = "fb", keep = 1, data = { e = "E-FROM-PATCH" } })
end
