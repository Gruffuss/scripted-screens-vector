-- Payload ceiling probe for the vector layer.
--
-- Sends a ladder of increasingly large nested prop tables through `vector` elements.
-- The client-side patch logs what actually arrived:
--
--   probe "probe_N": claimed=<what Lua sent> nodes=<what arrived> depth=.. strChars=..
--
-- Read the log in BepInEx/LogOutput.log. claimed == nodes means the rung survived
-- intact. A missing line means that rung never made it. A short nodes count means
-- truncation.
--
-- IMPORTANT: in a solo session ScriptedScreens skips the network sync entirely
-- (it requires IsActive && IsServer && HasRemoteClients), so this measures the Lua
-- build cost and MessagePack round trip but NOT the 600-char chunking. For the
-- network ceiling this has to run with a second client connected.
--
-- Each rung is a separate element so one failure does not hide the others. Rungs are
-- built one per tick to stay clear of the Lua instruction budget -- if the whole
-- ladder were built in one pass, a budget overrun would look like a payload failure.

local RUNGS = { 50, 200, 1000, 4000, 12000 }

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

local status = ui:element({
    id = "status",
    type = "label",
    rect = { unit = "px", x = 10, y = 8, w = W - 20, h = 22 },
    props = { text = "payload ceiling: starting" },
    style = { font_size = 14, color = "#94A3B8", align = "left" },
})

ui:commit()

-- One node per entry, shaped like a real scene node so the measurement reflects the
-- actual format rather than a flat array of numbers.
local function build_nodes(count)
    local nodes = {}
    for i = 1, count do
        nodes[i] = {
            op = "R",
            x = i % 480,
            y = (i * 7) % 480,
            w = 4,
            h = 4,
            f = "#8FE8C8",
        }
    end
    return nodes
end

local rung = 0

function tick(dt)
    rung = rung + 1
    if rung > #RUNGS then
        return
    end

    local n = RUNGS[rung]
    local ok, err = pcall(function()
        ui:element({
            id = "probe_" .. n,
            type = "vector",
            -- Off-screen: this test is about payload, not pixels.
            rect = { unit = "px", x = -10, y = -10, w = 4, h = 4 },
            props = {
                n = n,
                probe = build_nodes(n),
            },
        })
        ui:commit()
    end)

    status:set_props({
        text = string.format("rung %d/%d  n=%d  %s",
            rung, #RUNGS, n, ok and "sent" or ("FAILED: " .. tostring(err))),
    })
end
