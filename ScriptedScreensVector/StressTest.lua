-- Rebuild-cost benchmark for the vector layer.
--
-- Answers two of the brief's open questions with numbers instead of expectation:
--
--   "Rebuild cost at scale" -- roughly 1,700 animated quads across a console was
--                              *assumed* trivial. This measures it.
--
-- It does NOT answer "is on_frame still needed", and an earlier version of this comment
-- wrongly implied it did. That question is really two:
--
--   Does animation still need per-frame Lua?  Already answered, by this file: there is
--     no tick and no on_frame below, and the motes move anyway.
--   Is it cheaper than canvas + on_frame?     Needs the same visual built both ways and
--     compared. Watching the FPS counter with nothing to compare against cannot tell you.
--
-- The mod logs a line every 5 seconds to BepInEx/LogOutput.log:
--
--   vector: N surface(s) @ H Hz each, M ms per rebuild, L% of a core total (E% each),
--           peak V verts, P px wide
--
-- Read E first -- the share of one CPU core each surface costs. That is main-thread time
-- competing with everything else the game does. Multiply by how many consoles are in view.
--
-- L is aggregate across surfaces, so divide before comparing runs. Note also that the
-- rebuild rate can only land on frame boundaries, so an effective 21 Hz on a 42 FPS game
-- is a 28 Hz request landing every second frame, not the cap misbehaving.
--
-- Change QUADS and re-run to find where it stops being free. Set ANIMATE = false to
-- confirm the other half of the claim: a scene with no `t` reference must log nothing
-- at all, because it never rebuilds.

local QUADS = 1700       -- try 400 / 1700 / 5000
local ANIMATE = true     -- false = static scene, should produce zero rebuilds
local LOD = false        -- true = ALSO shed instances when small (pops; off by default)
local SIMPLE = false     -- true = same geometry, trivial expressions

-- Ablation. Change one at a time, note `tessellate` in the log, subtract.
-- These stay in Lua so a measurement needs no game restart.
local NO_EVAL    = 0     -- 1 = fixed geometry; removes attribute evaluation
local NO_FILL    = 0     -- 1 = build outlines but emit no fill vertices
local NO_FEATHER = 0     -- 1 = no feather ring (12 verts/shape -> 4)

-- SIMPLE isolates expression cost. It draws the SAME number of quads at the SAME vertex
-- count, but each attribute is a short expression instead of a deep one with hash() and
-- sin() calls. Run both and compare `ms per rebuild` in the log:
--
--   big difference  -> expression evaluation dominates, and the fix is to make the
--                      evaluator cheaper (hoist i-invariant subtrees, flatten the tree)
--   small difference -> the cost is in geometry emission and the evaluator is not worth
--                      optimising
--
-- Worth measuring rather than reasoning about: the .NET 8 benchmark in the test project
-- says expressions are ~11% of the cost, but the game runs Mono, which penalises a
-- recursive tree-walking interpreter far more than it penalises list appends. Ratios
-- measured in one runtime do not transfer to the other.

-- Level of detail is temporal by default and needs no opting in: a console drawn small
-- rebuilds less often, down to 8 Hz, while still drawing every mote in the right place.
-- Walk away and watch `rebuilds/s` fall in the log while `peak verts` stays put.
--
-- LOD = true additionally sheds instances by area. That saves more, but motes pop in and
-- out and the field dims as it thins, which is why it is not the default.

local W, H = 480, 480

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
if size then W, H = size.w, size.h end

ui:clear()

ui:element({
    id = "bg",
    type = "panel",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    style = { bg = "#070D16" },
})

ui:element({
    id = "title",
    type = "label",
    rect = { unit = "px", x = 14, y = 8, w = W - 28, h = 22 },
    props = { text = string.format("STRESS %d  simple=%s  eval=%d fill=%d feather=%d",
                                   QUADS, tostring(SIMPLE),
                                   1 - NO_EVAL, 1 - NO_FILL, 1 - NO_FEATHER) },
    style = { font_size = 14, color = "#5FD9A8", align = "left" },
})

-- One RP node in Lua expands to QUADS quads on the client. That asymmetry is the whole
-- design: building this many nodes explicitly would blow the 50,000-instruction budget
-- at around 4,000 nodes, and this costs a single node.
local mote
if ANIMATE and SIMPLE then
    -- Same shape count and vertex count, minimal expression depth.
    mote = {
        op = "R",
        x = "=mod(i*7+t*10,200)",
        y = "=mod(i*13,200)",
        w = 2,
        h = 2,
        f = "#8FE8C8",
        fo = 0.6,
    }
elseif ANIMATE then
    mote = {
        op = "R",
        x = "=mod(hash(i)*200+9*sin(t*(0.4+hash(i+9)*1.1)+hash(i+1)*6.283),200)",
        y = "=mod(hash(i+2)*200-t*(6+hash(i+5)*14),200)",
        w = "=1+step(0.7,hash(i+4))",
        h = "=1+step(0.7,hash(i+4))",
        f = "#8FE8C8",
        fo = "=0.35+0.45*tri(t*0.5+hash(i+3))",
    }
else
    -- Identical geometry, no `t` anywhere: tessellated once, then genuinely free.
    mote = {
        op = "R",
        x = "=mod(hash(i)*200,200)",
        y = "=mod(hash(i+2)*200,200)",
        w = "=1+step(0.7,hash(i+4))",
        h = "=1+step(0.7,hash(i+4))",
        f = "#8FE8C8",
        fo = 0.6,
    }
end

ui:element({
    id = "stress",
    type = "vector",
    rect = { unit = "px", x = 14, y = 36, w = W - 28, h = H - 50 },
    props = {
        scene = "stress",
        w = 200, h = 200, fit = "stretch",
        nofill = NO_FILL, nofeather = NO_FEATHER, noeval = NO_EVAL,
        root = {
            { op = "R", x = 0, y = 0, w = 200, h = 200, f = "#0B1622" },
            { op = "RP", n = QUADS, lod = LOD and 1 or 0, c = { mote } },
        },
    },
})

ui:commit()

-- No tick, no on_frame. Deliberately. If the scene moves, it moves entirely on the
-- client, and that is the claim being tested.
