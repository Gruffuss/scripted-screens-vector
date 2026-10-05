-- A table-form scene nested deep, which killed the game process before 0.11.81. The text form
-- was already bounded by the scene-text reader; this road was not.
--
-- SET `DEPTH` TO 100 FOR THE NORMAL TEST. The page then draws, the game keeps running, and
-- vector_stats says "nodes may not nest more than 64 deep". That is the mod's guard working.
--
-- AT 2000 THE GAME STILL DIES, AND NOT IN THIS MOD. The vector parser refuses at 64 whatever
-- it is handed, so it behaves identically at 100 and at 2000; the difference is below it, in
-- the conversion from the Lua table to props, which happens before any of this mod runs and
-- recurses per level. Measured 2026-10-05 on 0.11.81: 100 survives, 2000 kills the process
-- with nothing in the log. Nothing in this mod can guard that -- it is the host's recursion.
--
-- So: 100 tests the guard. 2000 tests the host, costs a restart, and is not worth running
-- unless someone is working on the host.
local ui = ss.ui.surface("main")
ss.ui.activate("main")
local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end
ui:clear()

local DEPTH = 100

local n = { op = "R", x = 0, y = 0, w = 40, h = 20, f = "#5FD9A8", fea = 0 }
for _ = 1, DEPTH do n = { op = "G", c = { n } } end

ui:element({
    id = "deep", type = "vector",
    rect = { unit = "px", x = 4, y = 4, w = W - 8, h = H - 8 },
    props = { scene = "deep", w = 200, h = 200, fit = "stretch", root = {
        { op = "R", x = 0, y = 0, w = 200, h = 200, f = "#0B1622", fea = 0 },
        { op = "T", x = 4, y = 4, w = 192, h = 8, size = 6, f = "#EAF4F8",
          text = "nested groups past the cap: the game is still running" },
        { op = "T", x = 4, y = 16, w = 192, h = 7, size = 5, f = "#8FA6B8",
          text = "one problem reported, no green box (it is past the cap)" },
        n,
    } },
})
ui:commit()
