-- A table-form scene nested deep, which killed the game process before 0.11.81.
--
-- THE 64-DEEP GUARD THIS PAGE WAS WRITTEN FOR IS GONE. 0.11.81 added it; 0.11.86 removed it and
-- every other depth cap, because a cap that refuses merely DEEP input is a mitigation, not a fix.
-- All the recursions underneath are explicit stacks now. So `vector_stats` no longer says
-- "nodes may not nest more than 64 deep" at any depth, and a page expecting that message is
-- reading a build older than 0.11.86.
--
-- WHAT THE PAGE TESTS NOW: that deep nesting simply works. Measured offline on 0.11.94, the
-- table form through the real parser and tessellator -- 10, 64, 65, 100, 2000 and 20,000 levels
-- each parse, draw, and report NO problems, with the rect's x offset tracking the nesting
-- exactly at every depth (20,000 levels of `t = {1, 0}` put it at x = 40,000). Nothing refuses
-- and nothing is dropped.
--
-- THE HOST'S OWN RECURSION IS A SEPARATE QUESTION AND IS UNTESTED SINCE 0.11.86. On 0.11.81,
-- DEPTH = 2000 killed the process with nothing in the log, and that was attributed to the
-- conversion from the Lua table to props, which happens in the host BEFORE this mod runs. That
-- attribution has not been re-checked since the mod's own caps came out, and it cannot be
-- checked offline -- the probe never runs the host's converter. Treat 2000 as unproven in
-- either direction: if it dies, the log and the stack say whether it died above this mod.
--
-- So: any DEPTH tests that the mod handles it. 2000 tests the HOST, costs a restart, and is
-- worth running only when someone is asking about the host.
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
