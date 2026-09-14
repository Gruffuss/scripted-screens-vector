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
| [`09-scroll.lua`](examples/09-scroll.lua) | `SC`, and a whole list as one repeat over `$rows[i]` |
| [`10-text.lua`](examples/10-text.lua) | `T` nodes, fonts, fitting, and `fmt` so the chip ships numbers |
| [`11-symbols.lua`](examples/11-symbols.lua) | `SYM` / `USE`, inherited `style`, per-corner radii |
| [`12-click.lua`](examples/12-click.lua) | clickable rows in a repeat, the `id:i` index, node patching |
| [`13-src.lua`](examples/13-src.lua) | the same scene as tables and as text, and group defaults |

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

**`data` is one prop, so it is replaced wholesale, not merged key by key.** Send every value
the scene still needs on every tick. A name that quietly disappears from the payload is
reported as missing and, if a colour was bound to it, draws magenta.

Coordinates are **top-left origin, +Y down**, like the old canvas.

### Writing the scene as text instead of tables

A scene can be given as `src`, one node per line, in place of `root` and `defs`:

```lua
props = { scene = "tank", src = [==[
SCENE w=100 h=200 fit=stretch
DEFS {
    GL id=liquid units=bbox x1=0 y1=0 x2=0 y2=1 stops=[[0,#5FD9A8],[1,#2E8B6E]]
    CP id=tank { R x=10 y=10 w=44 h=60 rx=8 }
}
R x=10 y=10 w=44 h=60 rx=8 f=#0B1622
G clip=tank {
    YS n=24 x==8+i*1.75 y==64-$fill*52 y2=72 f=@liquid
}
]==] }
```

**Wrap the string in `[==[ … ]==]`, not `[[ … ]]`.** An array value ends in `]]`, and Lua's
long-string bracket closes at the first one it sees — so a scene containing
`stops=[[0,#5FD9A8],[1,#2E8B6E]]` is silently truncated at that point and everything after it
vanishes. A longer level of bracket has no such collision.

One node per line, `OP` then `key=value`, braces for children, `#` for comments. Same op
names, same keys, same expressions. A bare word is a flag, so `lod` means `lod=1`.

**Do not choose this for the instruction budget.** Measured on the Atmo Regulator's Apple
skin, three pages built both ways, counting VM instructions on the build tick:

| page | node tables + `label` elements | `src` + `T` + `click` |
|------|-------------------------------|-----------------------|
| atmo | 25.0k | 28.3k |
| alarms, 90 labels | 31.5k | 28.9k |
| devices | 40.1k | 41.9k |

Two out of three got *worse*. **A `src` line is only free when it is a literal** — then it is
already in the compiled chunk and costs nothing. A line built with `string.format` costs about
what the table would have, and serialising tables into text at build time is a straight loss.
The alarms page won because 90 labels became one `src`, not because text is cheaper.

Choose it for what it actually buys: clipping, scrolling, click regions, and text that changes
without re-declaring an element. Take the budget win where the layout is static enough to be a
literal, and do not expect it anywhere else.

**The one rule the format imposes:** an unquoted value is read to the next space, so an
expression cannot contain one. `y==64-$fill*52` is fine; `y="=64 - $fill * 52"` needs the
quotes. Expressions are full of commas and brackets, so whitespace is the only separator left.

One element takes either `src` or `root`, not both. When a layout depends on how many devices
turned up, build the string with `string.format` or `table.concat` — but see the measurement
above, and keep the generated part small. One `string.format` per node is roughly what a node
table costs; anything fancier is worse.

### Changing one node without resending the scene

Any node may carry an `id`, and the data element can patch it:

```lua
data:set_props({ nodes = { hv_bar = { w = 42, f = "#E23D3D" } } })
```

The patch merges onto the node's original props and re-parses that node, so keys you leave out
keep their values, and children are untouched.

Reach for this only when an expression cannot say it — a different op, a new gradient
reference, a changed count. Anything that is merely a *value* belongs in `data` with an
expression reading it, because that path re-parses nothing at all.

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

### Scrolling a list — `SC`

A list longer than its box goes in an `SC`. Children are written in content coordinates and
the container clips and slides them:

```lua
{ op = "SC", id = "log", x = 10, y = 30, w = 180, h = 130, ch = 24 * 22, rx = 6, c = rows }
```

**The scroll position never reaches the chip.** A wheel notch is one mesh rebuild: no tick, no
network, no instructions. The alternative — a ScriptedScreens `scrollview` full of `label`
elements — pays a **round trip per scroll**, so the list keeps up with the half-second tick
rather than with the mouse. Wheel is a fifth of the viewport per notch, drag moves content with the pointer, and
both clamp so a container whose content fits cannot move at all.

Inside one, `sy` and `vh` report **that container**, which is what pins a header, an edge fade
or a scrollbar thumb. `sy` is the offset and is **zero at rest**, so pinned artwork goes at the
container's own `y` plus `sy` -- `y = "=30+sy"` for a container at 30. Writing `y = "=sy"`
puts it at the top of the viewbox instead, where the container clips it away.

Vertical only, one level deep, and no scrollbar is drawn for you — see
[`09-scroll.lua`](examples/09-scroll.lua), where the thumb is two expressions and the whole
list, backgrounds and labels alike, is a single repeat over `$rows[i]`.

### Pinning artwork inside a scroll view

The same two variables work for a ScriptedScreens scroll view holding the whole element. A
vector element inside one scrolls with the content; `sy` (offset) and `vh` (viewport height),
both in scene units, cancel that out so a header or fade stays put:

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

### Text in the scene — `T`

Text is a node like any other, so it moves, scales, rotates and scrolls with the group it is
in, and its content comes from the data payload:

```lua
{ op = "T", x = 8, y = 8, w = 120, h = 20, text = "$eta",
  size = 14, f = "#EAF4F8", align = "right", valign = "middle", fit = "ellipsis" }
```

```lua
data:set_props({ data = { eta = "4h 12m" } })
```

**Prefer this to a `label` element over the artwork** — but not for the reason you might
expect. `ui:element` is a C call and costs the chip very little; the per-label cost is the Lua
around it, and that is the same either way. Measured, a page of 90 labels moved from 31.5k
instructions to 28.9k, which is a real saving and not a large one.

What you actually gain: a `T` node **changes its text without re-declaring an element**, sits
in viewbox units rather than console pixels so no number is converted twice, and clips, scrolls
and rotates with the artwork it belongs to. A readout attached to a moving needle is a sane
thing to draw; as a label element it is not.

Two things do cut real Lua work, though:

```lua
-- a whole list as one node and one array
{ op = "RP", n = 30, c = {
    { op = "T", y = "=6+i*20", text = "$lines[i]", f = "$tints[i]", ... },
} }

-- a number formatted here, so the chip does no string work at all
{ op = "T", text = "$press", fmt = "%.1f", unit = " kPa", ... }
```

And on the data element, `keep = 1` makes a payload a patch rather than the whole truth, so a
string sent once stays until it is changed. Without it every string on screen has to be resent
every tick or it disappears.

Three things it cannot do, because TMP builds its own geometry on its own object:

- It updates at the **rebuild rate**, not instantly. In practice that is 30 Hz.
- It clips to an **axis-aligned rectangle** only. A rounded or rotated clip cuts the geometry
  around it but not the text.
- It cannot be part of a gradient fill — `f` is a flat colour sampled at the node's origin.

`font` takes any family TMP knows, which is what the companion fonts mod registers. `fit` is
`ellipsis` or `shrink`, and both are TMP's own overflow modes, so the fitting is done by the
engine that has the glyph metrics rather than estimated in Lua.

A label element is still the right answer for text that must update the instant a value
changes, and for anything the player has to select or copy.

### Text and draw order

A label is covered by anything declared after it and covers anything declared before it — the
same rule the rest of the scene follows. Nothing to switch on.

It is worth knowing *why* this is worth mentioning at all: labels are TMP objects beside the
mesh rather than in it, so until recently the whole text layer drew above the whole surface and
a panel could never be slid over a readout.

The cost is one extra mesh, so one draw call, per label a later shape **actually overlaps** —
not per label. A page of tiles where each label sits inside its own tile stays in a single mesh;
`12-click.lua` has seven labels over nineteen shapes and stays in one. Pages generated by a
layout engine cut more, because boxes land where the engine puts them — one measured at 13
meshes over 48 shapes, which cost 0.07 ms of extra upload and is not worth avoiding.

If you want the old rule back for one scene:

```lua
props = { scene = "panel", w = 200, h = 240, ztext = 0, root = { ... } }
```

`examples/14-ztext.lua` draws the same scene both ways side by side.

### Reusing a subtree — `SYM` and `USE`

A row, an LED, a duct segment: written once, stamped anywhere.

```lua
defs = {
    { op = "SYM", id = "led", params = { r = 4, col = "#5FD9A8" }, c = {
        { op = "C", cx = 0, cy = 0, rx = "%r", ry = "%r", f = "%col" },
        { op = "C", cx = 0, cy = 0, rx = "=%r*1.8", ry = "=%r*1.8", f = "%col", fo = 0.2 },
    } },
},
root = {
    { op = "USE", ref = "led", x = 20, y = 20 },
    { op = "USE", ref = "led", x = 40, y = 20, r = 6, col = "#E23D3D" },
},
```

**Every attribute on the `USE` is a parameter.** `params` only supplies defaults for the ones
an instance leaves out. `%name` on its own keeps the parameter's type, so a number stays a
number; inside a longer string it splices textually, which is what makes `y = "=%top+i*4"`
work.

Substitution happens once, at parse time. An instance therefore costs exactly what writing the
nodes out would have cost — this saves *authoring*, not drawing.

Two smaller conveniences worth knowing in the same breath:

```lua
{ op = "G", style = { f = "#5FD9A8", fea = 0, sw = 1 }, c = { ... } }   -- inherited defaults
{ op = "R", x = 0, y = 0, w = 60, h = 24, rx = { 12, 12, 0, 0 } }       -- per-corner radii
```

`style` nests and anything a node states itself wins, so a group of twenty shapes that all
want `fea = 0` says it once. Corner radii are CSS order, `tl tr br bl`, and a zero corner is a
sharp point.

### Making a node clickable

```lua
ui:element({
    id = "menu", type = "vector",
    props = { scene = "menu", src = [==[
        R id=row1 click=1 x=0 y=0  w=200 h=24 f=#12202F
        R id=row2 click=1 x=0 y=26 w=200 h=24 f=#12202F
    ]==] },
    on_click = function(nodeId, player)
        -- nodeId is "row1" or "row2"
    end,
})
```

The node id arrives as the event's **value**, because Lua registers handlers per element and a
node is not an element. One handler serves the whole scene.

`click = 1` is opt-in and separate from having an `id`, since an id is also a patch target.
A scene with no clickable node stays transparent to the pointer exactly as before, so
decoration never steals a click from a button underneath it.

Hit testing is against the node's **bounding box**, in draw order, last match wins. For the
rows and tiles that carry `click` the bounds are the shape; a thin diagonal or a ring will
claim more than it draws. An invisible `R` with `fo = 0` and `click = 1` makes a hit area of
any size and costs no geometry.

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

**A scene that draws wrong says so.** An unknown op or attribute name, a malformed expression,
a missing clip or gradient id, or a `$name` the data never supplied — each puts a magenta
hatched border around the surface, one stripe per distinct problem, and a colour bound to a
missing name draws **magenta** rather than white. See "When something looks wrong" below.

---

## How much a console can hold

**One mesh holds 60,000 vertices and a surface uses as many as it needs**, so density is not
something to budget against any more. 150 cards each carrying a drop shadow — 148,000
vertices — drew in full with frame time unchanged.

What is worth knowing is *what* costs. Measured on a 1395 px console:

| | vertices |
|---|---|
| a filled rectangle | 4 |
| with a feather | 64 |
| with one `sh` shadow | 640 |

A shadow is about nine cards' worth of geometry, because its ring count follows the blur's
on-screen size. If a design puts one on every tile in a grid, that is where the geometry goes,
and a smaller blur radius is the direct lever. Nothing breaks if you ignore this — it is a
cost, not a limit.

## When something looks wrong

The surface tells you first. A magenta hatched border means the scene has faults; magenta
*fill* on a shape means a colour was bound to a data name that never arrived.

| symptom | usual cause |
|---------|-------------|
| magenta border | unknown op or attribute, bad expression, missing clip or gradient id |
| magenta fill | `f = "$name"` and no `name` in the data payload |
| a flat end-stop colour | gradient coordinates in a different place from the shape |
| one shape missing | a non-convex clip, or a self-intersecting `Y` |
| a value stuck at zero | array index off by one — expressions are 0-based |
| nothing at all | the two elements' `scene` ids do not match |

The detail goes to `BepInEx/LogOutput.log`. **A typo in an attribute name is the one worth
knowing about**, because unknown keys are ignored by design — `fille` used to vanish silently
and now names itself and the node it is on.

If [StationeersLua](https://github.com/OrbitalFoundryModTeam) is installed, ask the editor
instead:

```
vector_stats            -- every live surface
vector_stats scene=gas  -- one of them
```

It reports node and shape counts, vertices, rebuild rate, tessellation and upload cost,
on-screen size, whether the scene is animated or scroll-driven, and the same problems and
unresolved names. It is bound by reflection, so the mod is perfectly happy without it.

For measuring rather than debugging, turn on `Diagnostics` in the mod settings and read the
log line each surface prints every five seconds.

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
