-- In-game check for `snap = 1` (0.11.26). Two readouts, both flipped between 0 and 100 on
-- every tick:
--   left  (eased):   captures catch values in between, like 37 or 82
--   right (snapped): captures only ever show 0 or 100
-- The bars under them do the same: the left one glides, the right one jumps.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

ui:element({
    id = "bg", type = "panel",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    style = { bg = "#0A121C" },
})

ui:element({
    id = "snap_s", type = "vector",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = {
        scene = "snap",
        src = [==[
SCENE w=200 h=200 fit=contain
T x=10 y=30 w=85 h=30 text="$a" fmt="%.0f" size=20 align=center f=#EAF4F8
T x=105 y=30 w=85 h=30 text="$b" fmt="%.0f" size=20 align=center f=#EAF4F8
T x=10 y=64 w=85 h=12 text="eased" size=8 align=center f=#5A7085
T x=105 y=64 w=85 h=12 text="snapped" size=8 align=center f=#5A7085
R x=10 y=90 w==0.85*$a h=10 f=#2E8B6E
R x=105 y=90 w==0.85*$b h=10 f=#F59E0B
# Log probes (Diagnostics on): a scroll container reports its range each rebuild, and the
# range here is the value itself. "scroll Ui:snap_s/ease" shows in-between numbers,
# "scroll Ui:snap_s/snap" only 0.0 and 100.0.
SC id=ease x=10 y=150 w=10 h=10 ch==10+$a {
}
SC id=snap x=30 y=150 w=10 h=10 ch==10+$b {
}
]==],
    },
})

-- Both elements write into the same scene. The eased and snapped values travel in
-- separate payloads, both `keep = 1`, so each leaves the other's value alone.
local eased = ui:element({
    id = "snap_a", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "snap", keep = 1, data = { a = 0 } },
})

local snapped = ui:element({
    id = "snap_b", type = "vector",
    rect = { unit = "px", x = -6, y = -6, w = 1, h = 1 },
    props = { scene = "snap", keep = 1, snap = 1, data = { b = 0 } },
})

ui:commit()

local high = false

function tick(dt)
    high = not high
    local v = high and 100 or 0
    eased:set_props({ data = { a = v } })
    snapped:set_props({ data = { b = v } })
    ui:commit()
end
