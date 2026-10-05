-- Inputs that used to END THE GAME PROCESS, not the scene.
--
-- Each of the three below recursed with nothing to stop it, and the result was a stack
-- overflow, which .NET cannot catch: no exception, no log line, no magenta border -- the
-- process simply goes. Out of process against the unguarded parser the symbol cases printed
-- "Stack overflow." and exited 127.
--
-- WHAT MUST HAPPEN: this page draws, the game keeps running, and vector_stats lists FIVE
-- problems naming the five faults. If the game is gone, the guards are not there.
--
-- 1 a symbol whose body uses itself              -> "symbol "selfie" uses itself: selfie -> selfie"
-- 2 two symbols that use each other              -> "... uses itself: ping -> pong -> ping"
-- 3 an expression nested past 64 parentheses     -> a malformed expression; `x` falls back to its default
-- 4 a run of 400 prefix minus signs               -> the same, through a DIFFERENT recursion
-- 5 a chain of 400 `^`                            -> the same again
--
-- 4 and 5 were open until 0.11.80: the 0.11.77 cap counted parentheses only. Both arrive
-- through any numeric attribute, since a value needs no leading `=` to be read as an
-- expression -- the `w` below has none.
--
-- The fourth case, an array nested past 32 deep, cannot be written here: it is refused while
-- the scene TEXT is read, so the whole scene is rejected rather than this one node, and the
-- page would not draw at all. Its guard is covered by the unit suite.
--
-- A symbol used normally, and the same symbol used twice, must still work -- the two green
-- boxes at the bottom. A guard that refused those would be worse than the crash.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

ui:element({
    id = "crashers", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = {
        scene = "crashers",
        w = 200, h = 200, fit = "stretch",
        size = 5, f = "#EAF4F8",

        defs = {
            -- 1 uses itself
            { op = "SYM", id = "selfie", c = { { op = "USE", ref = "selfie" } } },

            -- 2 a pair that use each other
            { op = "SYM", id = "ping", c = { { op = "USE", ref = "pong" } } },
            { op = "SYM", id = "pong", c = { { op = "USE", ref = "ping" } } },

            -- and one that behaves, as the control
            { op = "SYM", id = "box", c = {
                { op = "R", x = 0, y = 0, w = 30, h = 16, f = "#5FD9A8", fea = 0 } } },
        },

        root = {
            { op = "R", x = 0, y = 0, w = 200, h = 200, f = "#0B1622", fea = 0 },

            { op = "T", x = 4, y = 4, w = 192, h = 7,
              text = "The game is still running. That is the test." },
            { op = "T", x = 4, y = 14, w = 192, h = 6,
              text = "vector_stats must list FIVE problems, named." },

            -- 1, 2: each draws nothing, and says why
            { op = "USE", ref = "selfie", x = 8, y = 26 },
            { op = "USE", ref = "ping", x = 8, y = 26 },

            -- 3 an expression nested far past the cap: x falls back to its default, 8
            { op = "T", x = 4, y = 30, w = 192, h = 6,
              text = "3 over-nested expression: the bar sits at its default x" },
            { op = "R", x = "=((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((8",
              y = 40, w = 60, h = 12, f = "#F5D76E", fea = 0 },

            -- 4 and 5: two recursions that are not parentheses. Both bars fall back to their
            -- default width, which for an `R` is 0, so neither draws -- and the game lives.
            { op = "T", x = 4, y = 58, w = 192, h = 6,
              text = "4,5 minus-run and ^-chain: no bar, and still running" },
            { op = "R", x = 8, y = 66, w = string.rep("-", 400) .. "1", h = 10, f = "#F5D76E" },
            { op = "R", x = 8, y = 78, w = table.concat({ string.rep("1^", 400), "1" }), h = 10, f = "#F5D76E" },

            -- the controls: a symbol used once, and the same symbol used twice
            { op = "T", x = 4, y = 92, w = 192, h = 6,
              text = "controls: three green boxes, from one symbol used 3x" },
            { op = "USE", ref = "box", x = 8, y = 102 },
            { op = "USE", ref = "box", x = 44, y = 102 },
            { op = "USE", ref = "box", x = 80, y = 102 },
        },
    },
})

ui:commit()
