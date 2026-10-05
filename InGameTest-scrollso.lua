-- A scroll container's `so` is a scroll offset, not stroke opacity (0.11.92).
--
-- Before 0.11.92 an `SC` passed its own `so` down as a paint default, so a list that asked to
-- jump to the top drew every outline inside it at alpha 0. Measured offline on this exact
-- shape: max stroke alpha 0.000 before the fix, 1.000 after, with a plain `G so=0` still
-- inheriting as it should.
--
--   PASS   row 1 shows its white outline, row 2 shows nothing
--   FAIL   row 1 is blank -- the SC is still passing `so` down to its children
--
-- Row 2 is the control that keeps the fix honest: `so` as a default on an ORDINARY group must
-- still be inherited, so its outline must stay invisible. A fix that made row 2 appear would
-- have broken a documented default.

local ui = ss.ui.surface("main")
ss.ui.activate("main")
ui:clear()

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:element({
    id = "so_s", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = { scene = "so", src = table.concat({
        "SCENE w=200 h=200 fit=stretch size=5 f=#EAF4F8",
        "R x=0 y=0 w=200 h=200 f=#12202F fea=0",
        'T x=6 y=4 w=188 h=7 size=6 text="an SC keeps its own so; a G still passes it down"',

        'T x=6 y=16 w=188 h=6 f=#8FA6B8 text="1  SC so=0 sov=1 -- outline must SHOW"',
        "SC id=l1 x=6 y=26 w=188 h=40 ch=80 so=0 sov=1 {",
        "  R x=4 y=4 w=80 h=30 rx=3 f=none s=#FFFFFF sw=2",
        "}",

        'T x=6 y=76 w=188 h=6 f=#8FA6B8 text="2  plain G so=0 -- must stay INVISIBLE"',
        "G so=0 {",
        "  R x=10 y=86 w=80 h=30 rx=3 f=none s=#FFFFFF sw=2",
        "}",
        'T x=100 y=94 w=94 h=16 size=5 f=#53646F wrap=1 text="nothing drawn here is correct"',
    }, "\n") },
})

ui:commit()
