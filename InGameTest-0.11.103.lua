-- 0.11.103 on a console. Everything here is a DRAWN result, so none of it needs a click.
--
-- WHAT SHOULD BE SEEN
--
--   A  ver reads 11103.
--      The encoding changed from major*10000 + minor*100 + patch to
--      major*1000000 + minor*1000 + patch, because the old one had run out of patch digits:
--      0.11.100 and 0.12.0 both read 1200, and 0.12.0 sorted BELOW 0.11.101.
--      The row also shows that lt(ver, 1202) -- a stale literal on the OLD scale -- now reads
--      FALSE, so an un-updated "old mod" banner stays hidden rather than firing wrongly.
--
--   B  the fmt rule, which is the fix for a regression this mod shipped in 0.11.100.
--      LEFT column is the spec. RIGHT column is what it draws.
--      The FIRST SIX MUST BE SILENT -- no magenta border on this element from any of them:
--        (empty)   -> 1.25      an explicit empty spec means the default format
--        %%        -> %         one percent sign, not two
--        50%       -> 50%       literal text, written on purpose
--        ]         -> ]         same
--        {{0}}     -> {0}       escaped braces, no conversion, deliberate
--        {0:F1}    -> 1.2       in a PLACEHOLDER. Before 0.11.103 this drew <--}> because the
--                               placeholder ended at the first } , which is inside the spec.
--      The LAST TWO MUST BE REPORTED, and this element therefore carries a problem border:
--        %q        -> %q        prints itself where a number was meant
--      and ONE more, in column C ONLY:
--        x{{y      -> x{y       drops the number AND holds a letter, so it reads as a typo.
--                               It cannot be written inside a placeholder ON THIS BUILD: the
--                               brace count reads {{ as two opens, so `{$v:x{{y}` never
--                               terminates and stays literal. Found by the offline check before
--                               this page was pushed.
--                               SUPERSEDED after 0.11.104: the spec is read as a composite
--                               format now, so `{$v:x{{y}` draws x{y and IS reported, and the
--                               B-column row for it is on the page that carries that change.
--                               Everything this page DRAWS is unchanged by it, and its PASS
--                               line below still holds, which is why the drawn rows are left
--                               exactly as they were seen on a console. The three prose rows at
--                               y=102..116 describe this build, not the current one.
--
--   C  the same specs as a NODE fmt rather than a placeholder. They must agree with B, row for
--      row: one function serves both checks now.
--
-- PASS   ver reads 11103; B and C agree on all six silent rows and on %q; B's border comes
--        from %q alone and C's from %q and x{{y; no border on the ver element.
-- FAIL   any of the six silent rows differing between B and C, a border on the ver element,
--        or <--}> anywhere.

local ui = ss.ui.surface("main")
ss.ui.activate("main")
ui:clear()

ui:element({
    id = "verrow", type = "vector",
    rect = { unit = "px", x = 10, y = 10, w = 440, h = 86 },
    props = { scene = "verrow", src = table.concat({
        "SCENE w=220 h=43 fit=stretch size=4 f=#EAF4F8",
        "R x=0 y=0 w=220 h=43 f=#0B1622 fea=0",
        'T x=3 y=2 w=214 h=6 size=5 text="A  ver"',
        'T x=3 y=12 w=90 h=7 size=5 f=#53646F text="ver ="',
        'T x=40 y=12 w=90 h=7 size=6 f=#5FD9A8 text="{=ver}" fmt="{0:F0}"',
        'T x=100 y=12 w=118 h=6 size=4 f=#8FA6B8 text="must read 11103 on 0.11.103"',
        'T x=3 y=24 w=120 h=6 size=4 f=#53646F text="lt(ver,1202)  stale OLD-scale literal"',
        'T x=100 y=24 w=118 h=6 size=5 f="=if(lt(ver,1202),#E23D3D,#5FD9A8)" text="{=lt(ver,1202)}" fmt="{0:F0}"',
        'T x=130 y=24 w=88 h=6 size=4 f=#8FA6B8 text="must be 0 (banner stays hidden)"',
        'T x=3 y=34 w=214 h=5 size=4 f=#53646F text="0.12.0 would read 12000, which now sorts ABOVE this"',
    }, "\n") },
})

-- B: the spec inside a PLACEHOLDER
ui:element({
    id = "ph", type = "vector",
    rect = { unit = "px", x = 10, y = 104, w = 218, h = 330 },
    props = { scene = "ph", src = table.concat({
        "SCENE w=109 h=165 fit=stretch size=4 f=#EAF4F8",
        "R x=0 y=0 w=109 h=165 f=#0B1622 fea=0",
        'T x=3 y=2 w=103 h=6 size=5 text="B  spec in a PLACEHOLDER"',
        'T x=3 y=10 w=103 h=5 size=4 f=#8FA6B8 text="v = 1.25.  first six silent, last two reported"',

        'T x=3 y=20 w=46 h=6 size=4 f=#53646F text="(empty)"',
        'T x=54 y=20 w=52 h=6 size=5 text="{$v:}"',
        'T x=3 y=30 w=46 h=6 size=4 f=#53646F text="%%"',
        'T x=54 y=30 w=52 h=6 size=5 text="{$v:%%}"',
        'T x=3 y=40 w=46 h=6 size=4 f=#53646F text="50%"',
        'T x=54 y=40 w=52 h=6 size=5 text="{$v:50%}"',
        'T x=3 y=50 w=46 h=6 size=4 f=#53646F text="]"',
        'T x=54 y=50 w=52 h=6 size=5 text="{$v:]}"',
        'T x=3 y=60 w=46 h=6 size=4 f=#53646F text="{{0}}"',
        'T x=54 y=60 w=52 h=6 size=5 text="{$v:{{0}}}"',
        'T x=3 y=70 w=46 h=6 size=4 f=#53646F text="{0:F1}"',
        'T x=54 y=70 w=52 h=6 size=5 text="{$v:{0:F1}}"',

        'T x=3 y=84 w=103 h=5 size=4 f=#C98BE0 text="these two MUST be reported:"',
        'T x=3 y=92 w=46 h=6 size=4 f=#53646F text="%q"',
        'T x=54 y=92 w=52 h=6 size=5 text="{$v:%q}"',
        'T x=3 y=102 w=103 h=6 size=4 f=#C98BE0 text="x{{y is not writable INSIDE a placeholder:"',
        'T x=3 y=110 w=103 h=5 size=4 f=#8FA6B8 text="the brace count reads {{ as two opens, so it"',
        'T x=3 y=116 w=103 h=5 size=4 f=#8FA6B8 text="stays literal. Column C carries that row."',

        'T x=3 y=128 w=103 h=5 size=4 f=#8FA6B8 text="a border on THIS element is expected,"',
        'T x=3 y=136 w=103 h=5 size=4 f=#8FA6B8 text="from the %q row alone"',
    }, "\n") },
})

-- C: the same specs as a NODE fmt. Must match B row for row.
ui:element({
    id = "nodefmt", type = "vector",
    rect = { unit = "px", x = 236, y = 104, w = 214, h = 330 },
    props = { scene = "nodefmt", src = table.concat({
        "SCENE w=107 h=165 fit=stretch size=4 f=#EAF4F8",
        "R x=0 y=0 w=107 h=165 f=#0B1622 fea=0",
        'T x=3 y=2 w=101 h=6 size=5 text="C  same spec as a NODE fmt"',
        'T x=3 y=10 w=101 h=5 size=4 f=#8FA6B8 text="must agree with B, row for row"',

        'T x=3 y=20 w=46 h=6 size=4 f=#53646F text="(empty)"',
        'T x=54 y=20 w=50 h=6 size=5 text=$v fmt=""',
        'T x=3 y=30 w=46 h=6 size=4 f=#53646F text="%%"',
        'T x=54 y=30 w=50 h=6 size=5 text=$v fmt="%%"',
        'T x=3 y=40 w=46 h=6 size=4 f=#53646F text="50%"',
        'T x=54 y=40 w=50 h=6 size=5 text=$v fmt="50%"',
        'T x=3 y=50 w=46 h=6 size=4 f=#53646F text="]"',
        'T x=54 y=50 w=50 h=6 size=5 text=$v fmt="]"',
        'T x=3 y=60 w=46 h=6 size=4 f=#53646F text="{{0}}"',
        'T x=54 y=60 w=50 h=6 size=5 text=$v fmt="{{0}}"',
        'T x=3 y=70 w=46 h=6 size=4 f=#53646F text="{0:F1}"',
        'T x=54 y=70 w=50 h=6 size=5 text=$v fmt="{0:F1}"',

        'T x=3 y=84 w=101 h=5 size=4 f=#C98BE0 text="these two MUST be reported:"',
        'T x=3 y=92 w=46 h=6 size=4 f=#53646F text="%q"',
        'T x=54 y=92 w=50 h=6 size=5 text=$v fmt="%q"',
        'T x=3 y=102 w=46 h=6 size=4 f=#53646F text="x{{y"',
        'T x=54 y=102 w=50 h=6 size=5 text=$v fmt="x{{y"',

        'T x=3 y=118 w=101 h=5 size=4 f=#8FA6B8 text="a border here too, same two rows"',
    }, "\n") },
})

-- the value both result elements read
ui:element({
    id = "data", type = "vector",
    rect = { unit = "px", x = 0, y = 0, w = 1, h = 1 },
    props = { scene = "ph", keep = 1, data = { v = 1.25 } },
})
ui:element({
    id = "data2", type = "vector",
    rect = { unit = "px", x = 1, y = 0, w = 1, h = 1 },
    props = { scene = "nodefmt", keep = 1, data = { v = 1.25 } },
})

ui:commit()
