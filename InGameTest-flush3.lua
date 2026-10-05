-- Three precision questions about what survives the collapse within one chip execution.
-- (A fourth -- whether commit flushes every surface -- is not observable in game; see the note
-- at the end of this header.)
--
--   1  `nodes` is ONE property, like `data`. Two nodes-bearing writes in one execution:
--      does the first node patch survive?
--        set_props{ nodes = { n1 = {f=red} } } then set_props{ nodes = { n2 = {w=120} } }
--        REPLACED WHOLE -> n1 stays grey, n2 is wide
--        MERGED         -> n1 red AND n2 wide
--
--   2  Two ui:element{} DECLARATIONS of one id in one execution. The second omits `scene`.
--        REPLACED WHOLE -> no scene survives, the payload never reaches q1 -> MISSING
--        MERGED         -> scene survives -> DECL-TWO
--
--   3  An ordinary prop set in the declaration and NOT repeated in the patch.
--      Declared at load with scene=r1; the tick patch names only keep and data.
--        ARRIVES -> PATCH-ONLY       LOST -> MISSING
--
-- Q4 (does commit on one surface flush them all) cannot be measured here: the host flushes at
-- the end of EVERY execution regardless, so a commit's scope has no observable consequence at
-- tick granularity. Answered from the decompile instead.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

local third = (H - 16) / 3

-- 1  NODES
ui:element({
    id = "qn_s", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = third },
    props = { scene = "qn", src = table.concat({
        "SCENE w=200 h=66 fit=stretch size=5 f=#EAF4F8",
        "R x=0 y=0 w=200 h=66 f=#0B1622 fea=0",
        'T x=6 y=3 w=188 h=7 size=6 text="1  two nodes writes: does the first survive?"',
        'T x=6 y=13 w=90 h=6 f=#8FA6B8 text="n1 -> red?"',
        "R id=n1 x=6 y=22 w=60 h=34 rx=3 f=#53646F fea=0",
        'T x=104 y=13 w=90 h=6 f=#8FA6B8 text="n2 -> wide?"',
        "R id=n2 x=104 y=22 w=20 h=34 rx=3 f=#7FB2F0 fea=0",
    }, "\n") },
})

-- 2  TWO DECLARATIONS
ui:element({
    id = "q1_s", type = "vector",
    rect = { unit = "px", x = 4, y = 8 + third, w = W - 8, h = third },
    props = { scene = "q1", src = table.concat({
        "SCENE w=200 h=66 fit=stretch size=5 f=#EAF4F8",
        "R x=0 y=0 w=200 h=66 f=#0B1622 fea=0",
        'T x=6 y=3 w=188 h=7 size=6 text="2  second declaration omits scene"',
        'T x=6 y=26 w=188 h=14 size=10 f=#5FD9A8 text=$q missing="MISSING"',
    }, "\n") },
})

-- 3  DECLARED PROP NOT REPEATED
ui:element({
    id = "r1_s", type = "vector",
    rect = { unit = "px", x = 4, y = 12 + third * 2, w = W - 8, h = third },
    props = { scene = "r1", src = table.concat({
        "SCENE w=200 h=66 fit=stretch size=5 f=#EAF4F8",
        "R x=0 y=0 w=200 h=66 f=#0B1622 fea=0",
        'T x=6 y=3 w=188 h=7 size=6 text="3  scene set at declare, patch omits it"',
        'T x=6 y=26 w=188 h=14 size=10 f=#5FD9A8 text=$r missing="MISSING"',
    }, "\n") },
})

local qn = ui:element({
    id = "qn_d", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
    props = { scene = "qn" },
})

-- 3: declared ONCE at load carrying `scene`, never repeated afterwards.
local r = ui:element({
    id = "r1_d", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
    props = { scene = "r1", keep = 1 },
})

function tick()
    -- 1: two nodes-bearing writes, different node ids, one execution.
    qn:set_props({ scene = "qn", nodes = { n1 = { f = "#FF0000" } } })
    qn:set_props({ scene = "qn", nodes = { n2 = { w = 120 } } })

    -- 2: two DECLARATIONS of one id; the second names no scene.
    ui:element({ id = "q1_d", type = "vector",
        rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
        props = { scene = "q1", keep = 1, data = { q = "DECL-ONE" } } })
    ui:element({ id = "q1_d", type = "vector",
        rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
        props = { keep = 1, data = { q = "DECL-TWO" } } })

    -- 3: the patch names neither scene nor anything else from the declaration.
    r:set_props({ keep = 1, data = { r = "PATCH-ONLY" } })
end
