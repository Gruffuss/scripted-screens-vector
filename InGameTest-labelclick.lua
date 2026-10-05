-- A `T` and an `IMG` with click=1 register a hit area (0.11.93).
--
-- Neither ever did: a hit area is recorded where a shape's outline is built, and a label and a
-- picture go through neither path. Measured offline: T click=1 went from 0 hits to 1, T press=1
-- from 0 to 1, and a T with no click still registers nothing.
--
-- AIM AT each row and click. The line at the bottom shows what was reported.
--
--   PASS   rows 1, 2 and 3 each report their own id; row 4 reports NOTHING
--   FAIL   a row reports nothing, or row 4 reports something

local ui = ss.ui.surface("main")
ss.ui.activate("main")
ui:clear()

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

local data

ui:element({
    id = "lc_s", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = { scene = "lc", src = table.concat({
        "SCENE w=200 h=200 fit=stretch size=5 f=#EAF4F8",
        "R x=0 y=0 w=200 h=200 f=#0B1622 fea=0",
        'T x=6 y=4 w=188 h=7 size=6 text="a label takes clicks now -- aim at each row"',

        -- 1  a plain clickable label
        'T x=6 y=18 w=90 h=6 size=5 f=#53646F text="1  T click=1"',
        'T id=lab x=6 y=26 w=90 h=16 size=11 f=#5FD9A8 click=1 text="CLICK ME"',

        -- 2  press, which implies clickable, and styles itself from `down`
        'T x=104 y=18 w=90 h=6 size=5 f=#53646F text="2  T press=1"',
        'T id=lab2 x=104 y=26 w=90 h=16 size=11 press=1 text="HOLD ME" f="=if(down,#F5D76E,#7FB2F0)"',

        -- 3  a label inside a group, to prove the frame matrix is applied to its box
        'T x=6 y=50 w=188 h=6 size=5 f=#53646F text="3  T click=1 inside a translated group"',
        "G t=[20,0] {",
        '  T id=lab3 x=6 y=58 w=90 h=16 size=11 f=#C98BE0 click=1 text="MOVED"',
        "}",

        -- 4  the control: no click, must stay silent
        'T x=6 y=82 w=188 h=6 size=5 f=#53646F text="4  T with NO click -- must report nothing"',
        'T id=lab4 x=6 y=90 w=120 h=16 size=11 f=#53646F text="SILENT"',

        'T x=6 y=120 w=188 h=6 f=#8FA6B8 text="last event:"',
        'T x=6 y=130 w=188 h=20 size=9 f=#5FD9A8 text=$last wrap=1 missing="(nothing yet)"',
        'T x=6 y=160 w=188 h=30 size=5 f=#53646F wrap=1 text="PASS = rows 1, 2 and 3 each report their own id and row 4 reports nothing."',
    }, "\n") },

    on_click = function(value)
        data:set_props({ scene = "lc", keep = 1, data = { last = value } })
        ui:commit()
    end,
})

data = ui:element({
    id = "lc_d", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
    props = { scene = "lc", keep = 1 },
})

ui:commit()
