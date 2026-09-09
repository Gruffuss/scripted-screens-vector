# Vector layer — reference

Every node, attribute and function the renderer accepts. Checked against the parser, not
against the design document — where the two disagreed, the code won.

For an introduction, read [README.md](README.md) first. For the reasoning behind the design,
see `vector-format-spec.md` in the project root.

---

## Elements

A scene is **two elements** sharing a `scene` name.

### Structure element

| Prop | Type | Meaning |
|------|------|---------|
| `scene` | string | scene id; pairs the two elements |
| `w`, `h` | number | viewbox size — the units all artwork is written in |
| `fit` | string | `stretch` (default), `contain`, `cover` |
| `defs` | array | gradient and clip-path declarations |
| `root` | array | the node list |

### Data element

| Prop | Type | Meaning |
|------|------|---------|
| `scene` | string | matching scene id |
| `data` | map | named numbers, arrays of numbers, and colour strings |
| `nodes` | map | geometry patches by node `id` — see below |

**Node patching.** Any node may carry an `id`, anywhere in the tree. The data element can then
change that node's attributes without resending the scene:

```lua
data:set_props({ nodes = { hv_bar = { w = 42, f = "#E23D3D" } } })
```

The patch is **merged onto the node's original props and the node re-parsed**, so keys the
patch does not mention keep their values — a partial apply would reset them to defaults.
Children are not patchable and are kept, so patching a group costs nothing for its subtree.

Use it for a change no expression can express — a different op, a new gradient reference, a
count. For anything that is only a *value*, prefer `data` and an expression: that path needs
no re-parse at all.

Give it a 1×1 rect at negative coordinates so it draws nothing.

### Scene as text — `src`

A scene may be given as one string instead of nested tables, in place of `root` and `defs`:

```lua
props = { scene = "tank", src = [[
SCENE w=200 h=200 fit=stretch
DEFS {
    GL id=liquid units=bbox x1=0 y1=0 x2=0 y2=1 stops=[[0,#5FD9A8],[1,#2E8B6E]]
    CP id=tank { R x=10 y=10 w=44 h=60 rx=8 }
}
R x=10 y=10 w=44 h=60 rx=8 f=#0B1622
G clip=tank {
    YS n=24 x==8+i*1.75 y==64-$fill*52 y2=72 f=@liquid
}
]] }
```

**Why it exists: a Lua string literal costs zero instructions to build.** Nested tables cost
roughly a dozen per node, and a page of a few hundred nodes is a serious fraction of the
50,000 per tick — which is what forces consoles to split their build across frames.

Grammar: one node per line, `OP` then `key=value`, `{` … `}` for children, `#` comments to
end of line. `SCENE` carries the viewbox; `DEFS { … }` holds gradients and clips. Same op
names, keys and expression syntax as the table form.

Values: a plain number is a number, `[a,b,c]` is an array and nests, anything else is a
string — which covers `#colours`, `@refs`, enum words and `=expressions`. A bare word is a
flag, so `lod` means `lod=1`. Double quotes wrap a string containing spaces.

**One rule the format imposes: an unquoted expression cannot contain a space.** Values are
read to whitespace, because expressions are full of `,` `[` `]` — `clamp($x,0,1)`,
`$name[i]` — and those cannot also be separators. Quote an expression that needs a space.

The text is converted to the same props the table form arrives as and parsed by the same
code, so the two cannot drift apart, and parsed scenes are cached by source text so a
structure element resent unchanged reparses nothing.

### Debug switches

Set on the **structure** element's props, alongside `root`. Each disables one stage, so
subtracting the reported cost isolates it.

| Prop | Effect |
|------|--------|
| `nofill` | skip fills |
| `nofeather` | skip feathering |
| `noeval` | skip expression evaluation |

---

## Nodes

A node is a table whose `op` key names its kind. Children live in `c`. Unknown keys are
ignored, so annotations are harmless.

| `op` | Kind |
|------|------|
| `G` | group — transform, opacity, clip |
| `RP` | repeat |
| `R` | rectangle |
| `C` | ellipse |
| `P` | SVG path |
| `L` | polyline (open) |
| `Y` | polygon (closed) |
| `YS` | sampled band — a filled strip |
| `LS` | sampled polyline — a stroked line |
| `SP` | spline through literal points |

### `G` — group

| Key | Type | Meaning |
|-----|------|---------|
| `t` | `{x, y}` | translate |
| `r` | number/expr | rotate, degrees clockwise |
| `s` | `{sx, sy}` | scale |
| `a` | `{x, y}` | anchor the transform pivots about, default `{0, 0}` |
| `o` | number/expr | group opacity `0..1`, multiplied into all descendants |
| `clip` | string | id of a `CP` in `defs` |
| `c` | array | child nodes |

Applied scale → rotate → translate, about `a`. Nests without limit.

### `RP` — repeat

| Key | Type | Meaning |
|-----|------|---------|
| `n` | **literal number** | instance count — not an expression |
| `lod` | number | `1` allows count reduction at distance; omitted means never |
| `c` | array | children, instantiated `n` times |

Inside, `i` is the instance index `0..n-1` and `n` the count. Nested repeats shadow `i`; the
enclosing index is `i1`, the next out `i2`.

`n` is structural — changing it means resending the structure element.

### `R` — rectangle

`x`, `y`, `w`, `h`, plus optional `rx` / `ry` corner radii. `rx` alone gives circular
corners. Radii clamp to half the shorter side. Corners are true arcs.

**Per-corner radii:** `rx = { tl, tr, br, bl }`, CSS order. A zero corner is a sharp point.

```lua
{ op = "R", x = 0, y = 0, w = 60, h = 24, rx = { 12, 12, 0, 0 }, f = "#12202F" }
```

### `C` — ellipse

`cx`, `cy`, `rx`, `ry`.

Segments follow **on-screen radius** (6 at the floor, 48 at the cap, quantised so camera drift
does not retessellate), the same way rounded-rect corners do. A mote two pixels across costs
about 6 vertices against a rectangle's 4, so circles are affordable as particles — use them
wherever a round speck is what you actually mean.

### `P` — path

`d` is a string of SVG path commands. Uppercase absolute, lowercase relative.

| Cmd | Args | Meaning |
|-----|------|---------|
| `M` | x y | move to |
| `L` | x y | line to |
| `H` | x | horizontal line |
| `V` | y | vertical line |
| `Q` | cx cy x y | quadratic bézier |
| `T` | x y | smooth quadratic — reflects the previous control point |
| `C` | c1x c1y c2x c2y x y | cubic bézier |
| `S` | c2x c2y x y | smooth cubic — reflects the previous control point |
| `A` | rx ry rot large sweep x y | elliptical arc |
| `Z` | — | close subpath |

`d` cannot contain expressions — a path's command list is static. Curves are flattened
adaptively against on-screen size, cached in ~12% scale buckets so camera drift does not
retessellate.

Multiple subpaths make holes. The largest closed subpath is the outer contour; `fr` decides
what the rest are.

### `L`, `Y` — polyline, polygon

`p` is a flat array of alternating coordinates: `{x, y, x, y, ...}`. `Y` closes
automatically and can be filled; `L` is stroked only.

### `YS` — sampled band

| Key | Meaning |
|-----|---------|
| `n` | sample count |
| `x` | x of each sample, `i` bound as in a repeat |
| `y` | the sampled edge |
| `y2` | the opposite edge — a constant fills to a baseline |
| `fo2` | fill opacity at the `y2` edge, ramping from `fo` at the `y` edge |

**`fo2` is the ramp-from-a-moving-edge primitive.** Each column interpolates between its own
two sampled endpoints, so the ramp follows a rippling surface exactly, and it costs no extra
geometry — the strip already emits a vertex on each edge.

Neither obvious alternative can do this, which is why it exists:

- a **gradient** is linear in space and anchored to the bounding box, so on a surface with
  ripple amplitude `A` and a ramp of depth `D` it reaches only `D/(D+A)` of full opacity at a
  crest and *starts* at `A/(D+A)` in a trough — a bright line exactly where the fade should
  vanish;
- **`fea`** ramps outward from a solid edge, the opposite direction.

```lua
{ op = "YS", n = 12, x = "=10+i*4", y = surface, y2 = "=" .. surface .. "+18",
  f = "#5FD9A8", fo = 0, fo2 = 0.62 }
```

Produces **one connected strip**. This is the primitive `RP` cannot replace: a repeat emits
`n` separate shapes with `n` flat tops, which reads as a staircase at any sample density.

**Samples are joined by straight lines**, so `n` decides whether a curve reads as a curve.
Around **20 segments per period** of the fastest term is where the facets stop showing; the
sampling theorem's 8 per period is enough to reconstruct a sine and not to draw one. Sample
generously — a curve is one node whatever `n` is.

**Curve LOD** then samples it more coarsely when it is drawn small, quantised so camera drift
does not retessellate, and never above the authored `n`. This is automatic and safe to leave
on: `i` is a float and the geometry expressions are continuous in it, so a coarser step walks
the *same* curve. Nothing is dropped, unlike count LOD on a repeat.

### `LS` — sampled polyline

Same sampling rule as `YS` (`n`, `x`, `y`), stroked rather than filled. The line-chart and
waveform primitive.

### `T` — text

| Key | Meaning |
|-----|---------|
| `x`, `y`, `w`, `h` | the box the text is laid out in |
| `text` | a literal, or `"$name"` bound to a data string |
| `size` | font size in scene units; scales with the transform |
| `f` | colour, as on any shape |
| `align` | `left` (default), `center`, `right` |
| `valign` | `top` (default), `middle`, `bottom` |
| `font` | a registered TMP family, e.g. from the companion fonts mod |
| `weight` | `bold`, or a number ≥ 600 |
| `cspace` | character spacing |
| `fit` | `none` (default), `ellipsis`, `shrink` |
| `min_size` | floor for `shrink` |

```lua
{ op = "T", x = 8, y = 8, w = 120, h = 20, text = "$pressure",
  size = 14, f = "#EAF4F8", align = "right", fit = "ellipsis" }
```

**Text is not part of the mesh, and that shapes what it can do.** TMP builds its own geometry
on its own GameObject and is main-thread only, while tessellation runs on a worker — so the
walk records where each `T` landed and the labels are created and updated when the job lands.

| | |
|---|---|
| transform, scale, scroll with the group | **yes** — the rect goes through the frame matrix, rotation included |
| updates from `data` | **yes** — strings are data values now, so `text = "$name"` works like a number |
| rich text, registered fonts | **yes** — it is real TMP |
| clipping | **axis-aligned rect only**, via `RectMask2D`. A rounded or rotated clip does not cut text until stencil clipping lands |
| update rate | at the **rebuild** rate, not instantly |

`ellipsis` and `shrink` are TMP's own overflow modes, so fitting is done by the engine that
knows the glyph metrics rather than estimated.

Objects are pooled by index and reused across rebuilds; surplus labels are disabled rather
than destroyed, so a scene alternating between two pages does not churn objects.

**Why use it over a `label` element:** a ScriptedScreens label costs roughly 300 Lua
instructions to declare and must be re-declared to change its text, and it cannot move or
clip with the scene. A `T` node costs a string in the data payload.

### `SP` — spline

| Key | Meaning |
|-----|---------|
| `p` | flat point array `{x, y, x, y, ...}` |
| `seg` | segments per span |

Catmull-Rom, so the curve passes **through** its points rather than being pulled toward them.

---

## Paint

Applies to any shape node.

### Fill

| Key | Meaning |
|-----|---------|
| `f` | `#rrggbb`, `#rrggbbaa`, `@gradientId`, `$dataName`, `none`, or a gradient sample (below) |
| `fo` | fill opacity `0..1`, expression-capable |
| `fr` | `nonzero` (default) or `evenodd` |

**Gradient sample** — the way to animate a colour:

```lua
f = { grad = "status", at = "=clamp($level,0,1)" }
```

Samples the ramp at an expression and yields a flat colour. Needed because the expression
evaluator is scalar: `f = "=lerp(...)"` has nothing to return, since a colour is not a number.

**The same form works on `s`**, so an outline can follow a value exactly as a fill does.

**Fill rules.** `evenodd`: every further contour is a hole. `nonzero`: a contour is a hole
only when wound *against* the outer one. This is a winding comparison, exact for nested
non-overlapping contours, approximate where contours partially overlap.

### Stroke

| Key | Meaning |
|-----|---------|
| `s` | stroke paint, same forms as `f` |
| `sw` | width in scene units, **centred on the path**; scales with the transform |
| `so` | stroke opacity |
| `cap` | `butt` (default), `round`, `square` |
| `join` | `miter` (default), `round`, `bevel` |
| `ml` | miter limit, default 4 |
| `dash` | array of on/off lengths |
| `dofs` | dash start offset |

**A stroke straddles its path**, half inside and half out. Outlining a shape on its exact
bounds therefore paints `sw/2` beyond them, and leaves a gap of `sw/2` between the stroke and
anything clipped to those same bounds. Inset the outline by half the width when the two have
to meet:

```lua
{ op = "R", x = x + sw/2, y = y + sw/2, w = w - sw, h = h - sw, rx = r - sw/2,
  f = "none", s = "#5FD9A8", sw = sw }
```

Joins are a **clamped miter** rather than inserted bevel or round geometry — invisible at UI
stroke widths, visible on very wide strokes at sharp corners. `join` is closer to a hint than
a guarantee.

### Shadows

| Key | Meaning |
|-----|---------|
| `sh` | list of drop shadows, `{ { dx, dy, blur, spread, "#rrggbbaa" }, ... }` |

CSS `box-shadow` semantics and order: the shape offset by `dx`/`dy`, grown by `spread`,
filled with the colour, blurred with a Gaussian whose sigma is **half** the blur radius,
drawn beneath the shape. Several compose in declaration order. A single shadow may be
written unwrapped.

```lua
{ op = "R", x = 8, y = 8, w = 60, h = 28, rx = 14, f = "#FFFFFF",
  sh = { { 0, 3, 8, 0, "#0000001f" }, { 0, 3, 1, 0, "#0000000a" } } }
```

Available on `R`, `C`, and any closed shape. Drawn as geometry — no offscreen pass, no
shader — by stacking contours from `-3σ` to `+3σ` carrying the closed-form coverage of a
blurred edge, `0.5·erfc(d / (σ√2))`. Ring count follows the blur's on-screen size.

**Three differences from CSS**, worth knowing rather than discovering:

- The coverage is measured along each vertex's normal rather than by solving the 2-D
  convolution. Exact on a straight edge, very slightly tight at a sharp corner; under a
  pixel at UI corner radii.
- **The shadow is not knocked out under the shape.** CSS clips it to outside the border box
  so a translucent shape does not darken over its own shadow. That needs a polygon boolean
  here. Opaque shapes are unaffected; a translucent one will read darker than the mockup.
- `inset` is not implemented.

Blending is straight source-over on sRGB bytes — the space the colours are written in — so a
shadow composites at the value the design specifies.

### Inherited defaults — `style`

A `style` map on a `G`, or on the scene root, supplies defaults for descendants that do not
set the key themselves:

```lua
{ op = "G", style = { f = "#5FD9A8", fea = 0, sw = 1 }, c = { ... } }
```

Anything a node states itself wins, which is what makes it a default rather than an override.
Defaults nest: a child group's `style` is merged onto what it inherited.

Use it for the key repeated on every node in a section — `fea = 0` across a stack of abutting
bands, one accent colour across a control.

### Feathering

| Key | Meaning |
|-----|---------|
| `fea` | edge softness in scene units; omitted means automatic |
| `fea_edge` | softness for a band's sampled edge only |

The default resolves to roughly **1.3 screen pixels**, not a fixed number of scene units,
because the same scene draws at very different sizes. `0` gives deliberately hard edges; a
large value gives a glow.

`fea_edge` lets a liquid or gas surface carry a wide soft ramp while the walls beside it stay
crisp.

A clipped shape's feather is clipped too: where the clip cut the outline the ramp collapses
to nothing, so no halo escapes, while untouched edges keep their full feather.

**Set `fea_edge = 0` on a band edge that ABUTS another shape.** Feathering exists to soften a
silhouette. An edge with a neighbour flush against it has no silhouette, and the ramp
double-composites with what is already there: two shapes at opacity `a` meeting under a
feather give `1-(1-a)²` — for `a = 0.62`, **0.86** — a bright rule a pixel or two tall exactly
where the join should be invisible. The renderer cannot detect abutment; the scene has to say
so.

An edge already at opacity 0 needs no such guard — feathering is skipped there automatically.

Without feathering every edge is hard — UGUI applies no antialiasing of its own.

---

## `defs`

### `GL` — linear gradient

| Key | Meaning |
|-----|---------|
| `id` | name, referenced as `@id` |
| `x1`, `y1`, `x2`, `y2` | the ramp axis |
| `units` | omitted for scene coordinates, `"bbox"` for shape-relative |
| `stops` | array of `{ position, colour }`, position `0..1` |

### `GR` — radial gradient

| Key | Meaning |
|-----|---------|
| `id` | name |
| `cx`, `cy`, `r` | centre and radius |
| `fx`, `fy` | optional focus, default the centre |
| `units` | as above |
| `stops` | as above |

**`units = "bbox"`** spans the referencing shape's own bounding box, `0..1`. Prefer it: no
coordinates to get wrong, and it tracks a shape that moves or resizes. Without it,
coordinates are in scene units and must be placed over the shape that uses them.

Gradient coordinates are **static** — `PropNumber`, not expressions. `units = "bbox"` is what
makes a gradient follow a moving shape.

Gradients are baked into vertex colours. A two-stop linear gradient is exact; multi-stop and
radial are subdivided automatically, and radial fills as concentric bands so vertices land at
even gradient parameters.

### `SYM` / `USE` — symbols

Declare a reusable subtree in `defs`, instantiate it anywhere:

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

`USE` becomes a group carrying `x`, `y`, `o` and `clip`; the symbol's body becomes its
children. **Every attribute on the `USE` is a parameter**, so a symbol takes `r` and `col`
without declaring them specially; `params` supplies defaults for the ones an instance omits.
In the text format the `SYM`'s own attributes serve as those defaults, since that format has
no map syntax:

```
DEFS { SYM id=led r=4 col=#5FD9A8 { C cx=0 cy=0 rx=%r ry=%r f=%col } }
USE ref=led x=20 y=20
USE ref=led x=40 y=20 r=6 col=#E23D3D
```

`%name` alone as a value **keeps the parameter's type**, so a number stays a number. Inside a
longer string it splices textually, which is what makes it work in expressions —
`y = "=%top+i*4"`.

Substitution happens once, at parse time, not through a scope in the evaluator: symbol
parameters are structure, not animation, so an instance costs exactly what writing the nodes
out would have.

### `CP` — clip path

| Key | Meaning |
|-----|---------|
| `id` | name, referenced from a group's `clip` |
| `c` | one shape |

**Must be convex** — rectangle, rounded rectangle, ellipse, convex polygon. Outlines are in
**scene coordinates** and stay put when the referencing group is transformed. Clip outlines
are static; `t` inside one is silently constant.

A clipped fill cannot carry holes; they are dropped with a warning.

---

## Expressions

Any numeric attribute may be a string beginning with `=`.

### Variables

| Name | Meaning |
|------|---------|
| `t` | seconds since the scene first appeared |
| `i` | current repeat index, `0` outside a repeat |
| `i1`, `i2`, … | enclosing repeat indices, outward |
| `n` | current repeat count |
| `$name` | scalar from the data payload |
| `$name[expr]` | array element, **0-based**; out of range yields `0` |
| `sy` | scroll offset of the enclosing scroll view, in scene units; `0` when there is none |
| `vh` | viewport height of that scroll view, in scene units; `0` when there is none |

**Arrays are 0-based in expressions and 1-based in Lua.** `$history[0]` is the value your
script stored at `history[1]`. A repeat's `i` runs `0..n-1`, so `$history[i]` lines up with a
Lua array naturally; a hand-written index does not. When emitting per-item nodes from a Lua
loop, subtract one:

```lua
for k = 1, #cells do
    local node = { op = "R", h = ("=%.1f*$level[%d]"):format(H, k - 1), ... }
end
```

Out-of-range reads yield `0` rather than failing, so an off-by-one shows up as a shape stuck
at zero — not as an error.

**`sy` and `vh` pin artwork to a scroll viewport.** A vector element inside a scroll view
moves with the content, so anything drawn at a fixed `y` scrolls away with it. Adding `sy`
cancels that out, which is how you keep a header, a fade or a rule stuck to the viewport
while the content moves underneath:

```lua
-- a fade pinned to the top of the viewport, absent until scrolled
{ op = "R", x = 0, y = "=sy", w = W, h = 16, f = "@fadeTop", fo = "=step(1,sy)" },

-- and to the bottom, gone once the content ends
{ op = "R", x = 0, y = "=sy+vh-16", w = W, h = 16, f = "@fadeBot" },
```

Both are read on the main thread when the rebuild is dispatched and converted from canvas
pixels to scene units through the viewbox, so they mean the same thing at any console size.
A scene that uses them rebuilds when the offset moves even if it never mentions `t` —
otherwise it would be treated as static and freeze.

If the artwork does not need to sit *underneath* scrolled content, the simpler answer is a
second vector element outside the scroll view, layered over it with `z_index`: it is pinned
because it never scrolls, and needs neither of these.

### Operators

`+` `-` `*` `/` `%` `^`, unary `-`, parentheses. Standard precedence.

**`^` is the power operator and there is no `pow()`** — reaching for one is an easy mistake
when scanning the function table. An unknown function name throws at parse time rather than
evaluating to zero, so it fails loudly, but the scene it is in will not draw.

Division by zero yields `0`, not infinity — an infinity would poison vertex positions and produce an invisible mesh
rather than a visible glitch.

### Functions

| Function | Meaning |
|----------|---------|
| `sin(x)` `cos(x)` `tan(x)` | radians |
| `atan2(y,x)` | |
| `abs(x)` `sign(x)` `sqrt(x)` | |
| `floor(x)` `ceil(x)` `round(x)` | |
| `min(a,b)` `max(a,b)` `clamp(x,lo,hi)` | |
| `lerp(a,b,t)` | unclamped linear interpolation |
| `mod(a,b)` | always positive, unlike `%` |
| `saw(x)` | rising ramp, period 1, range `0..1` |
| `tri(x)` | triangle wave, period 1, range `0..1` |
| `pulse(x,duty)` | `1` for the first `duty` of each period, else `0` |
| `step(edge,x)` | `0` below the edge, `1` at or above |
| `smoothstep(a,b,x)` | smooth `0..1` ramp between the edges |
| `if(c,a,b)` | `a` when `c` is non-zero, else `b` |
| `eq(a,b)` `lt(a,b)` `gt(a,b)` `lte(a,b)` `gte(a,b)` | comparisons returning `0`/`1` |
| `and(a,b)` `or(a,b)` `not(a)` | logical, on `0`/non-zero |
| `hash(x)` | deterministic pseudo-random `0..1` |
| `hash2(x,y)` | two-argument variant |
| `pi()` `tau()` | constants |

`hash` is an integer avalanche over fixed-point input, **not** `fract(sin(x)*k)` —
transcendentals are not bit-identical across platforms and every client must agree, or a
particle field looks different to each player.

**It is random, not evenly spread.** Over few instances that shows: for `i = 0..8` it returns
`0.405 .. 0.988`, so `hash(i) * width` leaves the left 40% empty. Below ~40 instances,
stratify — `(i + hash(i)) / n` puts one per slot and jitters it within — and add a per-copy
offset (`hash(i + seed)`) when the same subtree is built more than once, or every copy scatters
identically.

---

## Coordinates

Top-left origin, **+Y down**, in viewbox units. The viewbox maps onto the element rect
according to `fit`.

---

## Not supported

| | Why |
|---|---|
| text | needs child TMP objects; use ScriptedScreens' own `label` elements over the artwork |
| blur, drop shadow, glow | need an offscreen pass or custom shader; `fea` approximates them |
| non-convex clipping | needs a stencil buffer |
| self-intersecting fills | ear clipping is undefined on them; detection costs more than the fill |
| holes inside a clipped fill | needs boolean subtraction |
| expressions in `d` or in gradient coordinates | both are static; use `units = "bbox"` |
