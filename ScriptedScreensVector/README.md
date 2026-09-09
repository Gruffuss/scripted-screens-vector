# ScriptedScreens Vector

A `vector` element type for ScriptedScreens surfaces. It draws resolution-independent
artwork as a real mesh instead of a pixel canvas, and **animates it on the client** from
expressions rather than from per-frame Lua.

You describe the picture once. Motion costs no ticks, no instructions and no network traffic.

---

## Why it exists

The canvas approach costs roughly **10 FPS per console**, and the cause is the number of
drawing operations per frame — not fill area, not resolution. Every op is recorded,
batched, synced, replayed, and then the texture is uploaded. Doing that sixty times a second
for a console with a few hundred shapes is what the frame time goes into.

This layer makes the per-frame op count **zero**. A scene is sent once as structured data;
the client tessellates it and re-evaluates only what moves.

What you get on top of that:

- **Real alpha.** Straight source-over blending, so no pre-composited opaque palettes.
- **Any resolution.** Coordinates are a viewbox, mapped onto the element. One scene fits a
  128 px console and a 760 px one.
- **Antialiased edges.** UGUI applies none; this layer feathers geometrically.
- **Curves, gradients, clipping, dashes, rounded corners** as single attributes rather than
  as loops.
- **No instruction budget pressure.** A repeat of 2,000 motes is one node.

---

## Getting started

Paste [`examples/01-hello.lua`](examples/01-hello.lua) into a chip attached to a console. If
you see a rounded panel with a green circle, everything works.

Then read the examples in order — each one introduces exactly one idea and runs on paste:

| file | teaches |
|------|---------|
| [`01-hello.lua`](examples/01-hello.lua) | the element, the viewbox, basic shapes |
| [`02-moving.lua`](examples/02-moving.lua) | expressions over `t` — motion with no `tick` |
| [`03-data.lua`](examples/03-data.lua) | live values, and why a scene is two elements |
| [`04-repeat.lua`](examples/04-repeat.lua) | `RP`, `i`, `hash`, arrays, LOD |
| [`05-curves.lua`](examples/05-curves.lua) | `YS` vs `RP`, line charts, splines, paths |
| [`06-paint.lua`](examples/06-paint.lua) | gradients, alpha, feathering, animated colour |
| [`07-clip.lua`](examples/07-clip.lua) | clip paths and their one restriction |
| [`08-console.lua`](examples/08-console.lua) | everything, assembled into a real console |

[`Patterns.lua`](Patterns.lua) holds the same building blocks as copy-paste functions.

[`REFERENCE.md`](REFERENCE.md) is the complete list of nodes, attributes and functions.

---

## The shape of a scene

Two elements, always:

```lua
-- STRUCTURE: sent once, at spawn.
ui:element({
    id = "tank_s", type = "vector",
    rect = { unit = "px", x = 10, y = 10, w = 60, h = 160 },
    props = {
        scene = "tank",              -- pairs the two elements
        w = 100, h = 200,            -- viewbox, mapped onto the rect
        root = { ... },              -- the nodes
    },
})

-- DATA: rewritten every tick, tiny.
local data = ui:element({
    id = "tank_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },   -- off-screen, draws nothing
    props = { scene = "tank", data = { fill = 0.5 } },
})

function tick(dt)
    data:set_props({ data = { fill = read_level() } })
    ui:commit()
end
```

**Why two elements and not two props.** `set_props` merges what you give it into the
element's existing props and then resends the *whole* element. A `data` prop living beside a
large `scene` prop would resend the entire scene tree every tick, forever. Splitting them is
the only way to make data updates cheap.

Coordinates are **top-left origin, +Y down**, like the old canvas.

---

## Making things move

Any numeric attribute can be an expression: a string starting with `=`, over `t` (seconds),
`i` (repeat index), and `$name` (data values).

```lua
{ op = "R", x = 10, y = "=100-$fill*100", w = 40, h = "=$fill*100", f = "#2E8B6E" }
```

The client evaluates that every frame. Lua sends `fill` twice a second and the bar moves
smoothly regardless.

**A scene with no `t` anywhere is tessellated once and then costs nothing per frame.** Worth
designing for: keep large static chrome in one scene and animated parts in another.

### Two clocks

`t` advances every frame. `$data` changes only when a payload arrives — about twice a second.
A needle driven purely by `$pressure` would step in quarter-second jumps beside motes that
glide, and the two together make the first look broken.

The renderer eases `$name` values across the gap between payloads, measuring that gap itself
so it adapts to any tick rate. You get this for free.

The cost: **a value that should snap will not.** A discrete mode flip arrives over roughly one
tick. `Renderer.SmoothData = false` restores snapping.

Arrays are smoothed too, which matters most for history charts — see the rolling-window
idiom in [`05-curves.lua`](examples/05-curves.lua).

### `RP` versus `YS`

`RP` makes **separate shapes**. `YS` makes **one connected surface**.

A liquid surface built from `RP` rectangles is a staircase however finely you sample it, because
each rectangle has its own flat top. `YS` samples `x`/`y`/`y2` and joins the samples into one
strip.

Rule of thumb: **`RP` for discrete things** (motes, ticks, bars), **`YS` for anything that
should read as a continuous edge** (liquid, waveforms, area charts). `LS` is the stroked
sibling, for line charts.

### Fading from a moving edge — `fo2`

A band can carry an opacity **ramp along each column**: `fo` at the sampled `y` edge, `fo2` at
the `y2` edge.

```lua
{ op = "YS", n = 24, x = "=10+i*4",
  y  = surface,                       -- the rippling edge
  y2 = "=" .. surface .. "+18",       -- 18 units below it
  f = "#5FD9A8", fo = 0, fo2 = 0.62 } -- transparent at the surface, solid below
```

This is the primitive for a gas fade, a depth cue, or anything measured **down from a surface
that moves**. Each column interpolates between its own two sample points, so the ramp follows
the ripple exactly, and it costs no extra geometry — the strip already has a vertex on each
edge.

Neither obvious alternative works, which is why it exists:

- a **gradient** is straight and anchored to the shape's bounding box, so on a rippling edge it
  reaches only part-way at a crest and starts part-way up in a trough — a bright line exactly
  where the fade should vanish;
- **`fea`** ramps *outward* from a solid edge, the opposite direction.

Before `fo2`, the only way to get this was a stack of ~20 abutting bands of rising opacity.
That worked, and it was two thirds of one real console's tessellation cost.

### Shadows

`sh` takes CSS `box-shadow` values, and several compose in order:

```lua
sh = { { 0, 3, 8, 0, "#0000001f" }, { 0, 3, 1, 0, "#0000000a" } }
```

`{ dx, dy, blur, spread, colour }`, sigma is half the blur, drawn beneath the shape.

**This is what `fea` cannot do.** Feather ramps *outward* from a solid edge; a blur softens
both sides of it, which is why a feathered knob reads as a ring rather than a shadow. `sh`
evaluates the real Gaussian and stacks contours across it.

Two limits: the shadow is not knocked out under the shape, so a **translucent** shape sits
over its own shadow and reads darker than a CSS mockup; and `inset` is not implemented.

### Pinning artwork inside a scroll view

A vector element inside a scroll view scrolls with the content. `sy` (offset) and `vh`
(viewport height), both in scene units, cancel that out so a header or fade stays put:

```lua
{ op = "R", x = 0, y = "=sy", w = W, h = 16, f = "@fadeTop", fo = "=step(1,sy)" },
{ op = "R", x = 0, y = "=sy+vh-16", w = W, h = 16, f = "@fadeBot" },
```

Both are `0` when there is no scroll view, so the same scene works either way. A scene using
them rebuilds as the offset changes even with no `t` anywhere.

Worth checking first: if the pinned artwork does not need to sit *under* scrolled content, a
second vector element **outside** the scroll view, layered with `z_index`, is pinned for free
and needs neither variable.

### Colour

Three mechanisms, and picking the right one matters:

| want | use | cost |
|------|-----|------|
| pulse, breathe, blink | `fo = "=0.3+0.7*tri(t*0.5)"` | client-side, free |
| host picks an exact colour | `f = "$alarm"`, data sends `"#E23D3D"` | one string per tick |
| colour follows a value | `f = { grad = "status", at = "=$level" }` | one number per tick |

The third is usually what you want for alarms: send `0.7` and let a ramp decide it is amber.
It blends through every stop, unlike a two-state swap.

A colour is **not** an expression — the evaluator is scalar — so `f = "=lerp(...)"` cannot
work. Sampling a ramp is how you interpolate.

---

## Patterns worth knowing

These came out of porting real consoles, and none of them is obvious from the node list.

### Showing one of two things

There is no conditional node. Give each alternative an opacity expression and let one
evaluate to zero — a charge/discharge arrow is two triangles:

```lua
{ op = "Y", p = { x, y-s, x-s, y+s, x+s, y+s }, f = accent, fo = "=$charging" },
{ op = "Y", p = { x, y+s, x-s, y-s, x+s, y-s }, f = accent, fo = "=1-$charging" },
```

A shape whose paint resolves to zero alpha is **skipped entirely**, not drawn transparent, so
the hidden branch costs nothing per rebuild.

### Discrete colour bands

Sampling a ramp gives a smooth blend. For bands that must be *discrete* — 74% is "half", not
a blend of half and healthy — double each stop so the colour holds flat and swaps over a tiny
window:

```lua
stops = {
    { 0.000, RED   }, { 0.2495, RED   },
    { 0.2505, AMBER }, { 0.4995, AMBER },
    { 0.5005, GREEN }, { 1.000,  GREEN },
}
```

Keep the window small but non-zero. At exactly zero a value crossing the boundary pops; a
0.001 window plus the renderer's own easing between payloads turns it into a quick transition
with no per-frame work.

### Layering text over artwork

The vector layer draws no text, so labels are ScriptedScreens' own `label` elements on top.
Write the artwork in viewbox units and convert for the labels, so one set of numbers drives
both:

```lua
local VB = 460                       -- viewbox
local function px(v) return v * SW / VB end

ui:element({ id = "eta", type = "label",
    rect = { unit = "px", x = px(224), y = px(42), w = px(220), h = px(30) },
    props = { text = eta },
    style = { font_size = px(24), color = accent, align = "right" } })
```

### Varying size convincingly

Two things matter, and the second is the one that is easy to miss.

**Bias the spread, but not too hard.** A flat `hash(i)` gives an even mix of sizes, which reads
as *sorted* rather than scattered. Raising it to a power pushes the distribution toward the
floor so most instances are small and a few are larger. But overdo it and the small ones stop
reading as *small* and start reading as *absent*:

```lua
rx = "=0.45+hash(i+4)^2*1.5"        -- 4.3x smallest to largest: too far
rx = "=0.85+hash(i+4)^1.4*0.85"     -- 2.0x, floor high enough to see
```

Keep the ratio near 2x and the floor well above invisible. `^` is the power operator; there is
no `pow()`.

**Vary it over time, not just per instance.** A hash-only size is fixed forever, so the runt of
the field is *always* the runt. Give each instance its own rate and phase and size becomes
something the field does rather than something each member permanently is.

**Make that swing absolute, not proportional** — this is the part that catches people. A mote
is about one scene unit of radius; a console drawing ~1.2 screen pixels per unit turns a ±22%
breath into **±0.3 px**, so the diameter moves half a pixel and nothing is visible. Any
percentage of a two-pixel circle is sub-pixel by construction.

```lua
-- invisible: proportional swing on a tiny shape
rx = "=(0.85+hash(i+4)^1.4*0.85)*(1+0.22*sin(t*0.4))"

-- visible: an absolute swing worth a pixel or two, at a rate you can see
rx = "=0.75+hash(i+4)^1.4*0.55"
  .. "+(0.55+hash(i+13)*0.5)*(0.5+0.5*sin(t*(0.8+hash(i+15)*1.2)+hash(i+14)*6.283))"
```

`(0.5+0.5*sin(…))` instead of plain `sin` so the term only ever **adds** — the base is then the
smallest the shape gets rather than its midpoint, which keeps the floor predictable.

Watch the rate too: a period of 9–25 s reads as static. Aim for **3–8 s** for anything meant to
look alive.

Costs one `sin` per instance and **no extra geometry** — vertex count follows on-screen radius.

Circles are cheap at this scale: an ellipse's segment count follows its on-screen radius, so a
mote costs about 6 vertices against a rectangle's 4. Use `C` when you mean a speck, not `R`.

### Scatter that stays even

`hash(i)` is random, not *evenly spread*, and over a handful of instances that difference is
visible. Measured, for `i = 0..8`:

```
min 0.405   max 0.988   only 2 of 9 below 0.5
```

— so `x = hash(i) * width` leaves the left 40% empty and looks like a bug. Stratify instead:
one instance per slot, with the hash only jittering it inside its own slot.

```lua
x = ("=%f+((i+hash(i))/%d)*%f"):format(left, n, width)   -- even at any n
```

Above ~40 instances plain `hash(i)` is fine. Below that, stratify.

### Seeding repeated blocks

Two copies of the same subtree run the same expressions over the same `i`, so they animate
**identically** — eight battery cells rippling in lockstep with bubbles in matching places
reads as a rendering fault. Nothing varies unless you vary it, so bake a per-copy seed in when
you build the nodes:

```lua
local SEED = (cell - 1) * 137
... ("=hash(i+%d)"):format(SEED) ...          -- different scatter per cell
... ("=sin(%f*t+%f)"):format(rate, cell*1.7)  -- different phase per cell
```

Baked at build time, so it stays deterministic and identical on every client.

### Stacked wave layers

Overlapping bands, each drawn from its own crest **down to the floor**, composite into a
layered liquid. Do not try to draw the slices *between* crests — that is what a canvas has to
do because its alpha overwrites rather than blends, and it means computing a pre-composited
colour for every combination of layers.

---

## Traps

**Gradient coordinates are in the same space as the geometry.** A gradient declared
`x1 = 10, x2 = 90` used on a shape at `x = 108..192` paints one flat end-stop colour, because
everything past 1 clamps. It looks exactly like a broken renderer and is the easiest mistake
to make.

Use **`units = "bbox"`** and the problem disappears — the ramp spans whatever shape references
it, `0..1`, and follows that shape if it moves or resizes.

**Fade to the same colour, not to black.** Blending is straight (non-premultiplied), so
`#5FD9A8FF → #00000000` greys out through the middle. Use `#5FD9A8FF → #5FD9A800`.

**Level of detail is automatic in time, opt-in in count.** Scenes drawn small rebuild less
often — free, nothing needed from you. Shedding *instances* from a repeat requires `lod = 1`
on that node and never happens otherwise, because dropping members of a gauge's tick marks
would be a bug while dropping motes is invisible:

```lua
{ op = "RP", n = 400, lod = 1, c = { ... } }
```

**`YS` and `LS` join their samples with straight lines.** The sample count is what decides
whether a wave reads as a curve or a polygon, and the tempting figure — the sampling theorem's
~8 per period — is the wrong test: it is ample to *reconstruct* a sine and nowhere near enough
to *draw* one. Aim for roughly 20 segments per period of the fastest term you use.

Sampling generously is cheap: a curve is one node however many points it has, and the renderer
now samples it more coarsely by itself when it is drawn small (Curve LOD, on by default). That
one is safe to leave on because nothing is dropped — `i` is a float, so a coarser step walks
the *same* curve.

**A stroke is centred on its path**, half inside and half out. An outline drawn on a shape's
exact bounds paints `sw/2` beyond them — and leaves a `sw/2` gap against anything clipped to
those bounds. Inset the outline by half the stroke width when they have to meet.

**Data arrays are 0-based in expressions, 1-based in Lua.** `$history[0]` is what your script
put in `history[1]`. Inside a repeat this lines up by itself, since `i` runs `0..n-1`; a
hand-written index does not. An off-by-one reads as `0`, so it looks like a stuck value rather
than an error.

**Turn feathering off on an edge that touches another shape** — `fea_edge = 0`. Feather
softens a *silhouette*; an edge with a neighbour flush against it has none, and the ramp
composites on top of what is already drawn there. Two shapes at 0.62 meeting under a feather
read as 0.86: a bright rule where the join should be invisible.

**Clip shapes must be convex** — rectangle, rounded rectangle, ellipse, convex polygon. A
clipped fill cannot carry holes. Clip outlines are static: `t` inside one is silently
constant. They are in scene coordinates and stay put when the group using them is transformed.

**A repeat instantiates its children unchanged.** Nothing varies by itself; an expression over
`i` has to do it. For rotation that means a `G` *inside* the repeat.

**`Y` fills concave outlines but not self-intersecting ones.** A bow-tie renders wrong rather
than gracefully.

**A scene that draws nothing says so.** A missing clip id, a rejected non-convex clip or an
unknown op draws a magenta hatched border instead of an empty console, one stripe per distinct
problem. The detail is in `BepInEx/LogOutput.log`.

**Text is not supported.** Use ScriptedScreens' own `label` elements layered over the artwork
— they get the game's fonts, which the companion fonts mod makes available by name.

---

## Performance

Tessellation runs on a **worker thread**. The mesh upload — the only part on the main thread —
costs about **0.12 ms**, or a third of a percent of a frame, for a console with 800 shapes and
22,000 vertices animating at 30 Hz.

In practice that means you can stop budgeting. A console being on is not measurably different
from it being off.

What still matters, in order:

1. **Prefer fewer, larger shapes.** Cost is per shape emitted per rebuild — not per vertex,
   and not per expression complexity. Deep expressions are free; 800 tiny rectangles are not.
2. **Use `YS` instead of stacked strips.** Faking a gradient with a dozen bands per tank
   multiplies your shape count by a dozen. One band with a gradient fill does the same job.
3. **Keep static artwork in a scene with no `t`.** It is then built once and never again.
4. **Don't worry about expression depth.** Measured: trivial and deeply nested expressions
   cost the same.

Off-screen consoles and paused games rebuild nothing at all.

To measure, set `Diagnostics.Enabled = true` in `BepInEx/config/gruffuss.stationeers.scriptedscreens.vector.cfg`:

```
vector "gas": 30 Hz, 14.33 ms/rebuild (tessellate 14.21 off-thread + upload 0.12 on-thread),
              0.38% of a frame on the main thread, 45.3% of a worker core,
              21800 verts, 800 shapes, 1012 px
    by op: YS 10.07ms x184, R 3.92ms x616, G 0.10ms x16
```

Read **`% of a frame on the main thread`** first — that is what costs you FPS. The `off-thread`
figure is worker CPU and does not. `by op:` attributes the work to node types, which is how
you find out that 184 bands are two thirds of your scene.

`frame:` lines log actual frame time every five seconds, which is the only real ground truth.

---

## Configuration

`BepInEx/config/gruffuss.stationeers.scriptedscreens.vector.cfg`, also editable in
StationeersLaunchPad's settings UI.

| Section | Key | Meaning |
|---------|-----|---------|
| Renderer | `Enabled` | master switch; off for clean A/B measurements |
| Renderer | `SmoothData` | ease `$name` values between payloads |
| Renderer | `CullOffScreen` | skip rebuilds for consoles not in view |
| Renderer | `PauseWithGame` | freeze animation when the game is paused |
| Rate LOD | `Enabled`, `MaximumHz`, `MinimumHz`, `FullRatePixels` | rebuild rate versus on-screen size |
| Count LOD | `Enabled`, `MinimumFraction`, `FullDetailPixels` | thinning for `lod = 1` repeats |
| Curve LOD | `Enabled`, `PixelsPerSegment`, `MinimumSegments` | how finely `YS`/`LS` curves are sampled at distance |
| Diagnostics | `Enabled` | the logging above; off by default |

---

## Requirements

- [ScriptedScreens](https://steamcommunity.com/sharedfiles/filedetails/?id=3666779631) —
  required, load before this mod
- StationeersLaunchPad, BepInEx 5.x

Client-side only. Nothing on disk is modified; the element type is added by a single Harmony
postfix.
