-- Vertex ceiling: does going over it SAY so?
--
-- A surface's mesh holds at most 250,000 vertices. That is not a rate and it cannot be spread
-- over frames or ticks: every vertex has to be in the same mesh at the same instant for the
-- picture to be complete, so a vertex deferred is a hole, not a delay.
--
-- Until 0.10.4 the ceiling was 60,000 -- a margin under what a 16-bit index buffer can address
-- -- and going over it dropped geometry SILENTLY. Something on the console was simply missing
-- and nothing said which part or why, which reads as a mistake in the scene.
--
-- WHAT TO LOOK FOR, in order:
--
--   MOTES = 2000   under the ceiling. A full field, no border, no complaint.
--   MOTES = 9000   over it. A MAGENTA HATCHED BORDER round the surface, and in the log or in
--                  `vector_stats`:
--
--                      problems: scene is too large: a fill was dropped at 250000 vertices
--
--                  The field is still mostly there -- what was refused is whatever came after
--                  the ceiling, which is the point: you can see how much is missing.
--
-- Each mote is a circle, and a circle's segment count follows its ON-SCREEN radius, so the
-- count that tips it over depends on how close you stand. Walk toward the console with 9000
-- and watch the border appear.
--
-- The other thing to check is that frame time does not move. Tessellation is off-thread, so a
-- scene this size should cost the main thread almost nothing however much mesh it builds.
-- Turn Diagnostics on in the mod settings and read the `frame:` line.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 480, 480
if size then W, H = size.w, size.h end

local MOTES = 9000        -- try 2000 (under) and 9000 (over)

ui:clear()

ui:element({
    id = "bg", type = "panel",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    style = { bg = "#0A121C" },
})

ui:element({
    id = "cap_s", type = "vector",
    rect = { unit = "px", x = 8, y = 8, w = W - 16, h = H - 16 },
    props = {
        scene = "cap",
        w = 200, h = 200,

        root = {
            { op = "T", x = 10, y = 6, w = 180, h = 12,
              text = "VERTEX CEILING", size = 8, cspace = 4, f = "#5A7085" },

            { op = "T", x = 10, y = 182, w = 180, h = 12,
              text = MOTES .. " motes -- magenta border = over the ceiling",
              size = 7, align = "center", f = "#3A5570" },

            -- Circles rather than rects on purpose: a circle's segment count follows its
            -- on-screen radius, so this scene gets hungrier as you walk toward it. That is
            -- exactly the case that used to lose geometry without saying anything.
            { op = "RP", n = MOTES, c = {
                { op = "C",
                  cx = "=10+mod(hash(i)*1000,180)",
                  cy = "=20+mod(hash(i+7)*1000,150)",
                  rx = "=0.6+hash(i+3)*1.4",
                  ry = "=0.6+hash(i+3)*1.4",
                  f = "#5FD9A8",
                  fo = "=0.25+0.5*hash(i+5)" },
            } },
        },
    },
})

-- No data element and no `t` anywhere: the scene is static, so it tessellates once and then
-- costs nothing per frame. If `vector_stats` shows it rebuilding, something else is wrong.
