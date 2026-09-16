-- In-game check for ScrollChanged (0.11.24): with Diagnostics on, the log shows
--   scroll Ui:sr_s/list: 0.0 of 200.0, view 100.0      once, when the list appears
--   scroll Ui:sr_s/list: 150.0 of 200.0, view 100.0    then 150, 40, 400 in turn, every 3 s
--   scroll Ui:sr_s/list: 40.0 of 200.0, view 100.0
--   scroll Ui:sr_s/list: 200.0 of 200.0, view 100.0    (a jump to 400, clamped)
-- Values exact: a jump is not eased like data (0.11.24 fix; it landed at 149.1 before).

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
    id = "sr_s", type = "vector",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = {
        scene = "sr",
        src = [==[
SCENE w=200 h=200 fit=contain
SC id=list x=20 y=20 w=160 h=100 ch=300 so==$jump sov==$ver {
 RP n=15 {
  R x=24 y==24+i*20 w=152 h=16 f=#1E3247
 }
}
]==],
    },
})

local data = ui:element({
    id = "sr_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "sr", keep = 1, data = { jump = 0, ver = 0 } },
})

local targets = { 150, 40, 400 }
local elapsed, step = 0, 0

function tick(dt)
    elapsed = elapsed + (dt or 0.5)
    if elapsed < 3 then return end
    elapsed = 0
    step = step + 1
    local jump = targets[(step - 1) % #targets + 1]
    data:set_props({ data = { jump = jump, ver = step } })
    ui:commit()
end
