-- Two `nodes` patches queued BEFORE the structure arrives: do both land?
--
-- A reported repro. 0.11.89 merged the queued patches per TOP-LEVEL key, but `nodes`
-- is one key holding a map of node id -> patch, so the later `nodes` still replaced the earlier
-- one whole and the first patch vanished. 0.11.90 merges per node id and per node property.
--
-- The patch elements are declared BEFORE the structure, so when they arrive there is no graphic
-- yet and both go into the waiting queue. The structure then lands and the queue is applied.
--
--   PASS   the left box is RED   (patch 1 landed)  and the right box is WIDE (patch 2 landed)
--   FAIL   the left box is GREY  -- patch 1 was dropped, which is the 0.11.89 behaviour

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

-- PATCH 1, queued first: recolour node `a`.
ui:element({
    id = "p1", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
    props = { scene = "pm", nodes = { a = { f = "#FF0000" } } },
})

-- PATCH 2, queued second: widen node `b`. Before 0.11.90 this one alone survived.
ui:element({
    id = "p2", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = 1, h = 1 },
    props = { scene = "pm", nodes = { b = { w = 120 } } },
})

-- THE STRUCTURE, declared last, so both patches were waiting when it arrived.
ui:element({
    id = "pm_s", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = {
        scene = "pm",
        src = table.concat({
            "SCENE w=200 h=200 fit=stretch size=5 f=#EAF4F8",
            "R x=0 y=0 w=200 h=200 f=#0B1622 fea=0",
            'T x=6 y=6 w=188 h=8 size=7 text="two patches queued before the structure"',
            'T x=6 y=18 w=188 h=14 size=5 f=#8FA6B8 wrap=1 text="PASS = left box RED and right box WIDE. Left grey means patch 1 was dropped."',

            'T x=6 y=44 w=90 h=7 size=5 f=#53646F text="1  node a: f -> red"',
            "R id=a x=6 y=54 w=60 h=40 rx=4 f=#53646F fea=0",

            'T x=104 y=44 w=90 h=7 size=5 f=#53646F text="2  node b: w -> 120"',
            "R id=b x=104 y=54 w=20 h=40 rx=4 f=#7FB2F0 fea=0",

            'T x=6 y=110 w=188 h=7 size=5 f=#53646F text="for reference, unpatched:"',
            "R x=6 y=120 w=60 h=24 rx=4 f=#53646F fea=0",
            "R x=104 y=120 w=20 h=24 rx=4 f=#7FB2F0 fea=0",
        }, "\n"),
    },
})

ui:commit()
