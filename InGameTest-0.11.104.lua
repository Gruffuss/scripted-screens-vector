-- 0.11.104: a payload carrying NO `data` prop no longer touches evaluator data.
--
-- Both halves of this were measured FAILING on 0.11.103, on a console:
--   - two elements naming one scene: the second wiped the first's data, so `$n` read `--`
--   - `since($n)` restarted whenever a nodes-only patch arrived
-- They are the same fault: `ReadData` clears its maps and returns early when there is no `data`
-- prop, and that empty context still replaced the scene's data.
--
-- WHAT SHOULD BE SEEN
--
--   A  TWO ELEMENTS, ONE SCENE. The structure is one element; `data = {n = 7.6}` is a second;
--      `nodes = {...}` is a THIRD, carrying no data at all.
--        n          -> 7.6        (was `--` on 0.11.103: the nodes element wiped it)
--        left box   -> GREEN      patched by `a`
--        right box  -> GOLD       patched by `b`
--      Both patch keys are words on purpose: a nodes table keyed ONLY by numbers is dropped by
--      a separate, still-open fault, which would confuse this test.
--
--   B  since($n) SURVIVES A NODES PATCH. The chip sends a nodes-only payload on EVERY TICK,
--      for ever. `since($n)` counts the seconds since `n` last ARRIVED, and `n` arrives exactly
--      once, at load. So the number must CLIMB PAST 2 and keep climbing -- 10, 30, 60.
--      On 0.11.103 it reset to about 0 on every tick and never got anywhere near 2.
--      The bar under it is the same number as a width, so the behaviour is readable at a glance:
--      it must sweep right and stay right, not saw back to nothing.
--
-- PASS   n reads 7.6, both boxes are coloured, and the since number climbs past 2 and keeps going
-- FAIL   n reads `--`, a box stays grey, or the since number keeps dropping back near 0

local ui = ss.ui.surface("main")
ss.ui.activate("main")
ui:clear()

ui:element({
    id = "struct", type = "vector",
    rect = { unit = "px", x = 10, y = 10, w = 440, h = 300 },
    props = { scene = "two", src = table.concat({
        "SCENE w=220 h=150 fit=stretch size=4 f=#EAF4F8",
        "R x=0 y=0 w=220 h=150 f=#0B1622 fea=0",

        'T x=3 y=2 w=214 h=6 size=5 text="A  two elements, one scene"',
        'T x=3 y=12 w=60 h=6 size=4 f=#53646F text="n ="',
        'T x=24 y=12 w=60 h=6 size=6 f=#5FD9A8 text=$n missing="-- WIPED"',
        'T x=90 y=12 w=127 h=5 size=4 f=#8FA6B8 text="must read 7.6, not -- WIPED"',

        "R id=a x=3 y=24 w=16 h=16 f=#53646F fea=0",
        "R id=b x=23 y=24 w=16 h=16 f=#53646F fea=0",
        'T x=44 y=28 w=173 h=5 size=4 f=#8FA6B8 text="both patched from a THIRD element: green, gold"',

        'T x=3 y=48 w=214 h=6 size=5 text="B  since($n) survives a nodes patch"',
        'T x=3 y=58 w=214 h=5 size=4 f=#8FA6B8 text="a nodes-only payload arrives on EVERY tick, for ever"',
        'T x=3 y=66 w=214 h=5 size=4 f=#8FA6B8 text="n arrives ONCE, at load, so this must only climb"',

        'T x=3 y=78 w=60 h=6 size=4 f=#53646F text="since(n) ="',
        'T x=40 y=76 w=70 h=9 size=8 f="=if(gt(since($n),2),#5FD9A8,#E23D3D)" text="{=since($n)}" fmt="{0:F1}"',
        'T x=110 y=79 w=107 h=5 size=4 f=#8FA6B8 text="red until 2 s, then green for ever"',

        "R x=3 y=92 w=214 h=10 f=#16222E fea=0",
        'R x=3 y=92 w="=min(214,since($n)*7)" h=10 f=#5FD9A8 fea=0',
        'T x=3 y=106 w=214 h=5 size=4 f=#8FA6B8 text="the bar sweeps right and STAYS. sawing back = failed"',

        'T x=3 y=118 w=214 h=5 size=4 f=#53646F text="on 0.11.103 this never passed 2.0 and the bar sawed"',
    }, "\n") },
})

-- the DATA element: carries `n` and nothing else
ui:element({
    id = "two-data", type = "vector",
    rect = { unit = "px", x = 0, y = 0, w = 1, h = 1 },
    props = { scene = "two", keep = 1, data = { n = 7.6 } },
})

-- the NODES element: names the same scene and carries NO data at all. Before 0.11.104 this
-- wiped the element above, which is exactly what this page exists to prove.
ui:element({
    id = "two-patch", type = "vector",
    rect = { unit = "px", x = 1, y = 0, w = 1, h = 1 },
    props = { scene = "two", nodes = { a = { f = "#5FD9A8" }, b = { f = "#F5D76E" } } },
})

ui:commit()

-- A nodes-only payload on EVERY tick, for ever. No clock: `os.clock` may not exist here, and a
-- guard built on a missing clock would never fire, so B would pass without testing anything.
-- Every tick is also the STRONGER test: if since($n) survives a patch at the tick rate, it
-- survives one every 2 s.
function tick()
    ui:element({
        id = "two-patch", type = "vector",
        rect = { unit = "px", x = 1, y = 0, w = 1, h = 1 },
        props = { scene = "two", nodes = { a = { f = "#5FD9A8" }, b = { f = "#F5D76E" } } },
    })
    ui:commit()
end
