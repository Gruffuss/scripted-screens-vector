-- 0.11.33 — per-name glide timing on a data payload.
--
-- Four bars take the same value at the same moment, every two seconds. They differ only in
-- what the payload says about how each should get there.
--
-- What to look for on the console:
--   1. DEFAULT  — glides the whole two seconds, straight. This is the old behaviour.
--   2. 0.6 EASE-OUT — leaves at once, arrives after 0.6 s, and waits. Fast start, soft finish.
--   3. 0.6 EASE-IN  — starts slowly, arrives at the same moment as the one above.
--   4. STEPS(4) — four visible jumps, landing on the value at the last one.
--   5. SNAP     — jumps immediately, for comparison.
--   6. DELAYED  — holds still for half a second after the others start, then glides 0.4 s.
--
-- The amber tick marks where the bar is heading, and it jumps the moment the payload lands,
-- so each bar can be read as early or late against its own target. The whole point is that
-- all six are fed the same number at the same moment and reach it differently.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 480, 480
if size then W, H = size.w, size.h end

ui:clear()

local rows = {
    { key = "a", label = "DEFAULT (payload gap)" },
    { key = "b", label = "0.6s EASE-OUT" },
    { key = "c", label = "0.6s EASE-IN" },
    { key = "d", label = "0.6s STEPS(4)" },
    { key = "e", label = "SNAP" },
    { key = "f", label = "0.4s AFTER A 0.5s DELAY" },
}

local src = "SCENE w=200 h=200 fit=stretch\nR x=0 y=0 w=200 h=200 f=#070D16\n"

for i, row in ipairs(rows) do
    local y = 10 + (i - 1) * 30
    src = src
        .. string.format("T x=14 y=%d w=172 h=9 text=\"%s\" size=6 f=#5FD9A8\n", y, row.label)
        .. string.format("R x=14 y=%d w=160 h=12 rx=6 f=#12202F\n", y + 11)
        .. string.format("R x=14 y=%d w=\"=clamp($%s,0,1)*160\" h=12 rx=6 f=#5FD9A8\n", y + 11, row.key)
        -- A marker at the value the bar is heading for, so early and late are visible.
        .. string.format("R x=\"=12+clamp($%s_target,0,1)*160\" y=%d w=2 h=16 f=#F59E0B\n", row.key, y + 9)
end

src = src .. "T x=14 y=190 w=172 h=8 text=\"same value, same moment, different timing\" size=5 f=#A89C8F\n"

ui:element({
    id = "ease_s", type = "vector",
    rect = { unit = "px", x = 6, y = 6, w = W - 12, h = H - 12 },
    props = { scene = "ease", src = src },
})

local data = ui:element({
    id = "ease_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "ease", keep = 1, data = {
        a = 0, b = 0, c = 0, d = 0, e = 0, f = 0,
        a_target = 0, b_target = 0, c_target = 0, d_target = 0, e_target = 0, f_target = 0,
    } },
})

ui:commit()

local elapsed = 0
local step = 0

function tick(dt)
    elapsed = elapsed + (dt or 0.5)

    -- A new destination every two seconds, alternating between two levels so the glide is
    -- long enough to watch in both directions.
    if elapsed < step * 2 then
        ui:commit()
        return
    end

    step = step + 1
    local value = (step % 2 == 1) and 0.85 or 0.15

    data:set_props({ data = {
        a = value, b = value, c = value, d = value, e = value, f = value,
        a_target = value, b_target = value, c_target = value, d_target = value, e_target = value,
        f_target = value,
    }, ease = {
        -- a: no entry, so it keeps the default glide across the payload gap.
        b = { 0.6, "ease-out" },
        c = { 0.6, "ease-in" },
        d = { 0.6, "steps(4)" },
        e = 0,                      -- zero seconds is a snap
        f = { 0.4, "ease-out", 0.5 },   -- waits half a second, then glides
        -- The markers jump, so each bar can be seen arriving late or early against its target.
        a_target = 0, b_target = 0, c_target = 0, d_target = 0, e_target = 0, f_target = 0,
    } })

    ui:commit()
end
