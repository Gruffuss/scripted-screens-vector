-- 0.11.117 automatic pixel edges. Pair PixelEdges On/Off with the camera untouched,
-- then re-push this page to rebuild. Expected On: 1.3 px ramp, 10-90% about 1.04 px
-- before the game's AA/tonemap. Off at depth ratio about 3.4: near edges soft, far edges
-- under one pixel. Record the game's AA setting. Decode sRGB to linear, normalize the
-- plateaus, and average at least 50 rows/columns per bar edge. Count far hairline dropouts.
-- Frontal captures check regressions; live screenshots check foreshortening. vector_stats
-- reports corner depth ratio and sigma_min spread. Move around the console to test drift.
-- Controls fea=0 and fea=4 must stay the same. Also run the dither page's clipping check.
local ui = ss.ui.surface("main")
ss.ui.activate("main")
local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end
ui:clear()
local scene = {
    "SCENE w=1000 h=1000 fit=stretch size=18",
    "R x=0 y=0 w=1000 h=1000 f=#000 fea=0",
    "T x=85 y=10 w=830 h=30 text=\"PixelEdges On: 1.3 px ramp; 10-90% ~1.04 px\" f=#fff",
    "L p=[0,120,1000,120] sw=1 s=#fff cap=butt",
    "L p=[0,155,1000,155] sw=0.3 s=#fff cap=butt",
    "T x=85 y=175 w=830 h=25 text=\"sw=1 / sw=0.3; far end should have no dropouts\" f=#fff",
    "R x=100 y=220 w=12 h=90 f=#fff fea=0",
    "R x=165 y=220 w=12 h=90 f=#fff fea=4",
    "G m=[3,0,0,1,0,0] { R x=160 y=220 w=4 h=90 f=#fff }",
    "T x=85 y=315 w=830 h=25 text=\"controls: fea=0 / fea=4 / m=[3,0,0,1,0,0]\" f=#fff",
    "R x=100 y=870 w=200 h=45 f=#fff",
    "R x=500 y=870 w=200 h=45 rx=18 f=#fff",
    "T x=85 y=970 w=830 h=25 text=\"sharp / rounded; sharp sides On=1.3px, Off~0.92px\" f=#fff",
}
for _, percent in ipairs({5,35,65,95}) do
    local at = percent * 10
    scene[#scene+1] = string.format("R x=%d y=360 w=8 h=480 f=#fff", at)
    scene[#scene+1] = string.format("R x=0 y=%d w=1000 h=8 f=#fff", at)
end
ui:element({ id="pixel_edges", type="vector", rect={unit="px",x=0,y=0,w=W,h=H},
    props={scene="pixel_edges",src=table.concat(scene,"\n")} })
ui:commit()
