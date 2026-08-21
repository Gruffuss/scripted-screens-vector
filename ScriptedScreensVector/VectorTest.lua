-- Scaffold test for the ScriptedScreens Vector mod.
--
-- Proves the whole integration path in one screen:
--   1. an unknown element type still gets a host GameObject from ScriptedScreens
--   2. the Harmony postfix sees it and attaches VectorGraphic to a child
--   3. props survive the Lua -> MessagePack -> client round trip as structured data
--
-- Expect: a green rounded rectangle on the left, an amber one with square-ish
-- corners on the right, and a panel between them for contrast. If you get flat
-- dark grey boxes instead, the mod did not patch -- check BepInEx/LogOutput.log
-- for "Patched ScriptedScreens".

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 480, 480
if size then W, H = size.w, size.h end

ui:clear()

ui:element({
    id = "bg",
    type = "panel",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    style = { bg = "#0F172A" },
})

ui:element({
    id = "title",
    type = "label",
    rect = { unit = "px", x = 12, y = 8, w = W - 24, h = 22 },
    props = { text = "VECTOR SCAFFOLD" },
    style = { font_size = 15, color = "#94A3B8", align = "left" },
})

local half = math.floor((W - 36) / 2)
local top, tall = 44, H - 60

-- Default fill and radius: props omitted entirely.
ui:element({
    id = "vec_default",
    type = "vector",
    rect = { unit = "px", x = 12, y = top, w = half, h = tall },
    -- style.bg is what ScriptedScreens tints its fallback Image with. The patch
    -- clears that Image, so this value should never be visible. If you see it,
    -- the postfix did not run.
    style = { bg = "#FF00FF" },
})

-- Both props supplied, to prove the structured prop path carries values through.
ui:element({
    id = "vec_props",
    type = "vector",
    rect = { unit = "px", x = 24 + half, y = top, w = half, h = tall },
    props = { fill = "#F59E0B", radius = 6 },
    style = { bg = "#FF00FF" },
})

ui:commit()
