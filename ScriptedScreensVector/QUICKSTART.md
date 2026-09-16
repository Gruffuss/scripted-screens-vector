# ScriptedScreens Vector: brief for AI editors

Read this before writing or changing a `vector` element. It is the minimum needed to produce a
scene that works first time; every other resource is linked at the bottom by topic.

## What it is and when to use it

`vector` is a ScriptedScreens element type added by the ScriptedScreens Vector mod. It draws
resolution-independent shapes, gradients, text and pictures as a mesh, and animates them on
the client from expressions. Animation costs no Lua ticks, no instructions and no network.

Use it for: gauges, bars, tanks with liquid, waves, charts, particle fields, rounded or clipped
panels, scrolling lists, clickable tiles, animated indicators. Prefer it over ScriptedScreens'
`canvas` element in every case (canvas costs ~10 FPS per console). ScriptedScreens' own
elements (`label`, `button`, `panel`, ...) still work beside it on the same surface.

## Workflow

1. Build the scene as **two elements sharing one `scene` id** (template below).
2. Look up anything not covered here: `search_docs` with scope `vector`, or read the URI from
   the topic map at the bottom.
3. Push the script to the chip.
4. Verify: call `vector_stats` (optionally `{"scene":"<id>"}`). It must say `no problems` and
   list no missing data names. Then call `capture_scripted_screen` with the chip's `ref_id`
   and look at the PNG.
5. Fix whatever `vector_stats` names and repeat from 3. A capture of a console nobody is
   looking at may draw curves coarser than the game does; judge smoothness from the game.

## Template (runs as written)

```lua
local ui = ss.ui.surface("main")
ss.ui.activate("main")
local size = ui:size()
local W, H = 480, 480
if size then W, H = size.w, size.h end
ui:clear()

-- Background: a plain ScriptedScreens panel. Without one the surface is not dark.
ui:element({
    id = "bg", type = "panel",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    style = { bg = "#0A121C" },
})

-- STRUCTURE: sent once. Holds the nodes.
ui:element({
    id = "gauge_s", type = "vector",
    rect = { unit = "px", x = 12, y = 12, w = W - 24, h = H - 24 },
    props = {
        scene = "gauge",
        w = 100, h = 100,                      -- viewbox, mapped onto rect
        root = {
            { op = "R", x = 12, y = 15, w = 20, h = 70, rx = 3, f = "#12202F" },
            { op = "R", x = 12, y = "=85-clamp($level,0,1)*70",
              w = 20, h = "=clamp($level,0,1)*70", rx = 3, f = "#2E8B6E" },
            { op = "C", cx = 65, cy = 60, rx = 20, ry = 20, f = "none",
              s = "#5FD9A8", sw = 1, so = "=0.3+0.3*sin(t*2)" },
        },
    },
})

-- DATA: rewritten every tick. Off-screen, draws nothing.
local data = ui:element({
    id = "gauge_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "gauge", data = { level = 0.5 } },
})
ui:commit()

local elapsed = 0
function tick(dt)
    elapsed = elapsed + (dt or 0.5)
    local level = 0.5 + 0.4 * math.sin(elapsed)   -- replace with a real device reading
    data:set_props({ data = { level = level } })
    ui:commit()
end
```

Why two elements: `set_props` resends the whole element, so data beside the structure would
resend the entire scene every tick.

Equivalent structure as text (`src` replaces `root`/`defs`; one node per line; `key==expr` is
an expression; children in `{ }`):

```lua
props = { scene = "gauge", src = [==[
SCENE w=100 h=100
R x=12 y=15 w=20 h=70 rx=3 f=#12202F
R x=12 y==85-clamp($level,0,1)*70 w=20 h==clamp($level,0,1)*70 rx=3 f=#2E8B6E
]==] }
```

## Choosing a node

| need | node |
|------|------|
| rectangle, rounded panel, bar | `R` (`rx`/`ry`, per-corner radii) |
| circle, ellipse, dot, lamp | `C` (`cx cy rx ry`) |
| any outline, icon, arc | `P` (`d` = SVG path data) |
| open line / closed polygon from points | `L` / `Y` (`p` = flat x,y list) |
| smooth curve through data points | `SP` |
| liquid surface, filled wave, area chart | `YS` (sampled band: `n`, `x`, `y`, `y2` per sample `i`) |
| line chart, waveform | `LS` (sampled line) |
| many copies: ticks, rows, motes | `RP` (`n`, children use `i`) |
| move, rotate, scale, fade, clip, filter a subtree | `G` (`t r s a o clip m`, filters, `mask`) |
| text, live numbers | `T` (`text`, `fmt`, `size`, `font`, `align`) |
| scrolling list | `SC` (`id`, `ch` = content height) |
| picture from a URL | `IMG` (`src`, `fit`) |
| gradient, clip shape, reusable part | `defs`: `GL` `GR` `GC`, `CP`, `SYM` + `USE` |

## Syntax essentials

- Coordinates are viewbox units, **origin top-left, +Y down**.
- Paint: `f` fill, `s` stroke, `sw` stroke width, `fo`/`so` opacity 0..1, colours `#RRGGBB` or
  `#RRGGBBAA`, `"none"`, `@id` for a gradient, `$name` for a colour from data. `sh` shadows,
  `fea` edge feather, `click = 1` with an `id` for a hit region.
- Any number can be an expression: a string starting with `=`.
  - Variables: `t` seconds, `i` repeat index from 0, `n` repeat count, `i1 i2` outer repeat
    indices, `$name` data value, `$arr[k]` array element (0-based), `sy`/`vh` scroll offset
    and viewport height.
  - Functions: `sin cos tan atan2 abs sign sqrt floor ceil round min max clamp lerp mod saw
    tri pulse step smoothstep if eq lt gt lte gte and or not hash hash2 pi tau`. `^` is power;
    there is no `pow`. Unknown functions fail the scene.
- Data values ease between ticks by themselves; do not interpolate in Lua.

## Rules that fail silently: check every one

- [ ] Structure and data elements have the **same `scene`** value.
- [ ] Every tick sends **every** data value the scene uses (`data` replaces, it does not merge),
      or the data element has `keep = 1`.
- [ ] Hand-written array indices are **0-based** in expressions (`$h[0]` is Lua `h[1]`).
- [ ] `src` strings use `[==[ ... ]==]`, never `[[ ... ]]`.
- [ ] Unquoted `src` values contain no spaces (`y==64-$f*52`, or quote the whole value).
- [ ] Gradients on shapes that move or differ in size use `units=bbox`.
- [ ] Fades go to the **same colour at alpha 0**, not to black or `#00000000`.
- [ ] Repeated copies vary through `i` (and `hash(i)` for randomness); spread fewer than ~40
      with `(i + hash(i)) / n`, and seed per block with `hash(i + seed)`.
- [ ] Sensor data is clamped before it sets a size or position.
- [ ] `YS`/`LS` have about 20 samples per wave period.
- [ ] Rotation inside a repeat uses a `G` inside the `RP`.
- [ ] Chip Lua: `string.format("%d", x)` only on integers (`math.floor` first). For numbers on
      screen prefer `T text="$v" fmt="%.1f"` so the chip sends numbers.
- [ ] An outline stroke meant to meet a clipped fill is inset by `sw/2`.
- [ ] Two shapes sharing an edge set `fea_edge = 0` on it.

## Symptoms

| symptom | cause |
|---------|-------|
| magenta hatched border | scene problems: `vector_stats` lists them (unknown op or key, bad expression, missing id) |
| magenta fill | a colour bound to a data name that never arrived |
| nothing drawn | `scene` ids differ, or the structure element failed to parse |
| one flat colour instead of a gradient | gradient coordinates not over the shape; use `units=bbox` |
| value stuck at 0 | array index off by one |
| a shape missing | self-intersecting `Y` or `P` |
| picture missing | URL failed; `vector_stats` shows the HTTP error |
| click does nothing | node lacks `id` or `click = 1`, or the element has no `on_click` |

## Events

- Clicks: `on_click = function(nodeId, player)` on the **structure element**. Inside a repeat
  the id is `"id:i"`: `local id, k = nodeId:match("^(.-):(%d+)$")`.
- Scroll: `SC` scrolls on wheel and drag on the client; the chip is not told. Jump from the
  script with `so = "=$to", sov = "=$version"` (applied once per new version).
- Patch one node without resending the scene: `data:set_props({ nodes = { id = { w = 42 } } })`.

## Topic map

| topic | URI |
|-------|-----|
| structure and data elements, `keep`, `src`, size limits, `ztext` | `stationeers://vector/reference/elements` and its `elements-*` sections |
| one node's attributes | `stationeers://vector/reference/nodes-<op>-...` (e.g. `nodes-g-group`, `nodes-t-text`, `nodes-sc-scroll-container`) |
| fill, stroke, clicks, shadows, `style`, feathering | `stationeers://vector/reference/paint-*` |
| gradients, clip paths, symbols | `stationeers://vector/reference/defs-*` |
| expression variables, operators, functions | `stationeers://vector/reference/expressions-*` |
| animation idioms | `stationeers://vector/guide/making-things-move` |
| worked patterns (tanks, motes, dials, charts) | `stationeers://vector/guide/patterns-worth-knowing`, `stationeers://vector/patterns` |
| all traps, explained | `stationeers://vector/guide/traps` |
| debugging | `stationeers://vector/guide/when-something-looks-wrong` |
| cost and limits | `stationeers://vector/guide/how-much-a-console-can-hold`, `stationeers://vector/guide/performance` |
| runnable examples, one idea each | `stationeers://vector/examples/index` (01-hello to 15-effects) |
| what changed in which version | `stationeers://vector/changelog` |
