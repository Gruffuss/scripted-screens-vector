# ScriptedScreens Vector Layer — format specification

**Status:** revision 2, rewritten against structured props. The integration surface below is
verified against ScriptedScreens 0.9.5.0 and confirmed working in game; the node, paint and
expression model is design-complete but unimplemented.

**Scope:** a resolution-independent 2D scene format that replaces pixel-canvas drawing on
ScriptedScreens displays. Scenes are described once, tessellated into a UGUI mesh on the
client, and animated client-side from an expression evaluator. No per-frame Lua execution,
no per-frame network traffic, no texture uploads.

**What changed from revision 1.** Revision 1 encoded the scene as a string on a label's text
prop, with `§SV1|` sentinels, `|` separators, and a hard prohibition on `<` and `>`. All of
that is gone. `ScriptedScreenscriptableUiLibrary.ParseProps` iterates every string key in the
Lua `props` table with no whitelist, and `ParseValue` recurses into nested tables — so the
scene ships as native structured data. No wire format, no parser, no reserved characters.
Confirmed in game: a `vector` element carrying 4,000 nested nodes arrived with all 28,001
values intact.

---

## 1. Architecture

```
Lua (server, 2 ticks/sec)          sync                Client (display refresh)
-------------------------          ----                ------------------------
build scene table once      ->  vector element A  ->   parse once, tessellate
rewrite data table per tick ->  vector element B  ->   re-evaluate expressions
                                                       rebuild mesh only on change
                                                       animate from local clock
```

**Element A — structure.** The node tree, as a nested Lua table. Changes only when the
layout changes, which for most consoles means once, at spawn.

**Element B — data.** Named values the structure references. Rewritten every tick. Small and
flat regardless of how elaborate the artwork is.

They are paired by a scene id present in both. The renderer holds a structure until a
matching data element appears, and tolerates either arriving first.

### 1.1 Why two elements and not two props

This is not stylistic. `set_props` merges the given props into the element's existing props
and then calls `ops.Upsert` with the **whole element**:

```csharp
// ScriptedScreenscriptableUiLibrary, set_props
foreach (existing prop) dictionary[key] = value;
foreach (new prop)      dictionary[key] = value;   // merge
uiElement.Props = list.ToArray();
ops.Upsert(surfaceName, uiElement);                // whole element, every time
```

There is no partial-prop update on the wire. Rewriting a small `data` prop on an element
that also carries a large `scene` prop would resend the scene along with it, every tick,
forever. Splitting across two elements is the only way to guarantee that rewriting data does
not resend structure.

### 1.2 Rendering target

The renderer attaches a `MaskableGraphic` to a child of element A's `RectTransform`,
stretched to fill it. A child rather than the element itself because UGUI permits one
`Graphic` per GameObject and ScriptedScreens has already placed its fallback `Image` on the
host. That fallback is set transparent rather than destroyed, because it is re-added on
every upsert. Element B is given a zero-size off-screen rect and draws nothing.

Consequences: the scene composites with the rest of the ScriptedScreens UI, obeys their
masking and layout, batches with surrounding UGUI, and requires no render texture.

Verified in game: an unknown element type yields a correctly parented, correctly sized,
correctly layered host, and its stale-component cleanup destroys only `CircleGraphic`, so
the renderer component is never disturbed.

### 1.3 Text

Out of scope for v1. Text remains ScriptedScreens' own label elements, positioned by their
existing layout, using whatever fonts the font-registration plugin has made available.

This is a real limitation — text cannot sit inside a transformed or clipped group, and
z-order between text and artwork is element-level rather than per-node. Rendering text
inside the scene means spawning and managing child TMP objects from the renderer, which is a
larger piece of work and is deferred until the rest is proven.

---

## 2. Coordinate system

Scene coordinates are declared by the root and mapped onto the element rect.

- Origin top-left, **+X right, +Y down**. Matches the existing canvas and HTML mockups.
- The root declares a viewbox: `w`, `h`, and `fit`.
- `fit` is `stretch` (default), `contain`, or `cover`.

There is no pixel count. The mesh rasterises at whatever resolution the GPU is drawing the
console at, from any distance.

---

## 3. Prop schema

Element A carries these props:

| Prop | Type | Meaning |
|------|------|---------|
| `scene` | string | scene id, pairs with element B |
| `w`, `h` | number | viewbox dimensions |
| `fit` | string | `stretch` \| `contain` \| `cover` |
| `defs` | array | gradient and clip-path declarations (§5.3, §7) |
| `root` | array | the node list |

Element B carries `scene` (matching id) and `data` (a map of named values, §8).

A node is a map whose `op` key names its kind. Child nodes live in an array under `c`.
Every other key is an attribute of that node.

```lua
{ op = "R", x = 0, y = 0, w = 50, h = 140, rx = 5, f = "#071019" }
```

Attribute names are exactly those in §4 and §5. Unknown keys are ignored, so a node may
carry annotations without breaking older renderers.

### 3.1 Numbers, strings, and expressions

A numeric attribute may be a Lua number, or a **string beginning with `=`**, which is an
expression (§6). Colours are `#rrggbb` / `#rrggbbaa` strings, a gradient reference `@name`,
or `none`.

There are no reserved characters. Revision 1's prohibition on `<` and `>` existed only
because the payload passed through TMP's rich-text parser, which structured props bypass
entirely. **The comparison functions `lt` / `gt` / `lte` / `gte` are nevertheless retained
rather than becoming operators** — they compose more predictably in a stack evaluator and
keep the expression grammar to a single parsing mode.

---

## 4. Nodes

| `op` | Meaning |
|------|---------|
| `G` | group — pushes transform, opacity, clip |
| `RP` | repeat — instantiates its children `n` times |
| `R` | rectangle, optionally rounded |
| `C` | ellipse |
| `P` | path |
| `L` | polyline (open) |
| `Y` | polygon (closed) |
| `YS` | sampled band — a connected strip generated by expressions over a sample index |
| `LS` | sampled polyline — the stroked sibling of `YS` |
| `SP` | spline — Catmull-Rom through a literal point list |

There is no `E` (end group) opcode. Nesting is expressed by the `c` array, so the tree
structure is carried by the data rather than by matched delimiters.

### 4.1 Group — `G`

| Key | Meaning |
|-----|---------|
| `t` | translate, `{x, y}` |
| `r` | rotate, degrees clockwise |
| `s` | scale, `{sx, sy}` |
| `a` | anchor the transform pivots about, in the group's own coordinates, default `{0, 0}` |
| `o` | group opacity `0..1`, multiplied into descendants |
| `clip` | names a clip path declared in `defs` |
| `c` | child nodes |

Order of application: scale, then rotate, then translate, all about `a`. Groups nest without
depth limit, though deep nesting costs transform composition per rebuild.

### 4.2 Repeat — `RP`

```lua
{ op = "RP", n = 26, c = { ... } }
```

Instantiates its children `n` times. Inside, `i` evaluates to the instance index (`0..n-1`)
and `n` to the count. Nested repeats shadow `i`; the outer index remains available as `i1`,
the next out as `i2`, and so on.

`n` must be a literal integer, not an expression — instance count is structural, and
changing it means resending structure.

**Level of detail is temporal, not spatial.** Mesh regeneration is capped at 30 Hz for every
animated scene and scales down to 8 Hz as an element is drawn smaller, rather than losing
instances. Rendering itself is unaffected and happens every frame; only the rebuild is
rate-limited. Every shape is still there, in the right place, just
resampled less frequently, and at that size the difference is invisible. Full rate returns
above ~400 px of on-screen width.

This is deliberate. Shedding instances is the obvious optimisation and it is the wrong one:
motes pop in and out, and apparent density falls with the count, so the field visibly dims as
you walk away. Rate reduction has neither problem and saves the same work — seven distant
consoles at 10 Hz cost a sixth of seven at 60 Hz, which is the arithmetic that actually
matters.

**`lod = 1`** opts a repeat into count reduction as well, for cases where a thinning field is
acceptable and the saving is needed. It scales by area so density per pixel is constant, and
`i`/`n` still refer to the authored count so the field loses members rather than
redistributing. Off by default.

This is the only iteration construct, and it is the single most important node in the
format. See §10: the Lua instruction budget makes emitting a few thousand explicit nodes
impossible in one tick, while `RP n = 2000` costs one. Particle fields, tick marks, bar
arrays, and staggered animations are all repeats over expressions of `i`.

### 4.3 Rectangle — `R`

`x`, `y`, `w`, `h`, and optional `rx` / `ry` corner radii. If only `rx` is given, corners are
circular. Radii are clamped to half the shorter side. A rounded corner is one attribute,
tessellated as a true arc.

### 4.4 Ellipse — `C`

`cx`, `cy`, `rx`, `ry`.

### 4.5 Path — `P`

`d` is a string of SVG path commands, space-separated, uppercase absolute and lowercase
relative:

| Cmd | Args | Meaning |
|-----|------|---------|
| `M`/`m` | x y | move to |
| `L`/`l` | x y | line to |
| `H`/`h` | x | horizontal line |
| `V`/`v` | y | vertical line |
| `Q`/`q` | cx cy x y | quadratic bezier |
| `C`/`c` | c1x c1y c2x c2y x y | cubic bezier |
| `A`/`a` | rx ry rot large sweep x y | elliptical arc |
| `Z`/`z` | — | close subpath |

Multiple subpaths in one `d` participate in the fill rule together, which is how holes are
made. Beziers and arcs are flattened adaptively — tolerance is chosen from the on-screen
size of the node, so a curve is smooth when large and cheap when small.

**Implementation notes.** Parsing and flattening are separate: `d` is a string and cannot
contain expressions, so the command list is static and only the tolerance varies. Flattening
therefore happens at draw time against a **quantised** scale (~12% buckets), which both
caches the result and supplies the hysteresis §11.4 asks for — ordinary camera drift does not
cross a bucket boundary and so does not retessellate.

Fills go through an ear-clipping triangulator with hole bridging, not a centre fan. The
largest closed subpath is the outer contour, and `fr` decides what the others are:

- **`evenodd`** — every further contour flips inside/outside, so each one is a hole however
  it was wound.
- **`nonzero`** (default) — a contour is a hole only when it winds *against* the outer one.
  Wound the same way it adds to the winding number rather than cancelling it, and stays
  filled.

This is a winding comparison, not a scanline evaluation. It is exact for nested,
non-overlapping contours — which is what UI artwork is — and approximate for contours that
partially overlap. A faithful implementation needs scanline or trapezoidal decomposition, a
different algorithm class from ear clipping.

**Self-intersecting contours are not supported.** Ear clipping is undefined on them and the
output is visibly wrong rather than slightly off. Detecting the condition costs more than the
fill itself, so the contract is simple polygons.

Path data stays a string because SVG command syntax is more compact than any table encoding
of the same thing, and it is the one place where a parser is genuinely worth its cost.

### 4.6 Sampled band — `YS`

```lua
{ op = "YS", n = 40, x = "=12+i*1.7", y = "=188-$fill*176+3*sin(i*0.42+t*1.6)", y2 = 188 }
```

`n` samples of `x`, `y` and `y2`, evaluated with `i` bound exactly as in a repeat, joined
into a single triangle strip. `y` is the sampled edge, `y2` the opposite edge — a constant
for a fill-to-baseline, another expression for a ribbon of varying thickness.

**This is the primitive `RP` cannot replace, and the distinction matters.** `RP` instantiates
`n` *separate* shapes; a wave built that way is `n` rectangles with `n` flat tops, which
reads as a staircase however finely it is sampled. `YS` produces one connected surface whose
edge is a polyline through the sample points.

Use `RP` for repeated discrete things — motes, tick marks, bars. Use `YS` for anything that
should read as a continuous edge — liquid surfaces, waveforms, area charts, ribbons.

Tessellated as a strip rather than a fan, so a non-convex edge (any wave with more than one
crest) is handled correctly.

### 4.7 Sampled polyline — `LS`

```lua
{ op = "LS", n = 60, x = "=8+i*3.1", y = "=95+18*sin(i*0.16+t*1.4)", s = "#8FE8C8", sw = 2.5 }
```

Same sampling rule as `YS`, but the result is stroked rather than filled. This is the
line-chart and waveform primitive.

### 4.8 Spline — `SP`

```lua
{ op = "SP", seg = 12, p = { 8, 150, 45, 128, 80, 165 }, s = "#E23D3D", sw = 2 }
```

Flattens a **Catmull-Rom** spline through the points in `p`, `seg` segments per span.

Catmull-Rom rather than bezier because it passes *through* its control points. A scene
author supplying data points wants a curve through them, not handles to tune. Bezier
control-handle curves belong in `P` when path support lands.

### 4.9 Polyline and polygon — `L`, `Y`

`p` is a flat array of alternating coordinates: `{x, y, x, y, ...}`. `Y` closes
automatically and can be filled. Present because generated geometry — a sampled wave, a
plotted series — is far more compact this way than as path commands, and as an array of
numbers it needs no parsing at all.

---

## 5. Paint

Paint attributes apply to any shape node.

### 5.1 Fill

| Key | Meaning |
|-----|---------|
| `f` | `#rrggbb`, `#rrggbbaa`, `@gradientName`, `$dataName`, or `none` |
| `fo` | fill opacity `0..1`, multiplied with any alpha in the colour |
| `fr` | `nonzero` (default) or `evenodd` |

Alpha composites correctly, with straight (non-premultiplied) source-over blending. There is
no need to pre-blend colours against a known backdrop — which is what forced the opaque
palette tables in the canvas-era `GasUI.lua`.

Alpha arrives from four places and they all multiply: the colour literal's `aa`, `fo`/`so`,
group opacity `o`, and feathering's edge ramp.

**Fading to transparent: keep the RGB.** Because blending is straight rather than
premultiplied, a gradient interpolates RGB and alpha independently. Fading
`#5FD9A8FF → #00000000` passes through **grey at half alpha** — the classic muddy midtone.
Fade to the *same* colour at zero alpha instead:

```lua
stops = { { 0, "#5FD9A8FF" }, { 1, "#5FD9A800" } }   -- clean: hue holds, alpha drops
stops = { { 0, "#5FD9A8FF" }, { 1, "#00000000" } }   -- muddy: greys out on the way
```

Both reach alpha 0; only the first keeps its colour getting there. This is also why
feathering sets alpha to zero on a copy of the fill colour rather than fading toward
transparent black.

### 5.2 Stroke

| Key | Meaning |
|-----|---------|
| `s` | stroke paint, same forms as `f` |
| `sw` | stroke width, in scene units, scales with the transform |
| `so` | stroke opacity |
| `cap` | `butt` (default), `round`, `square` |
| `join` | `miter` (default), `round`, `bevel` |
| `ml` | miter limit, default 4 |
| `dash` | array of on/off lengths |
| `dofs` | dash start offset |

### 5.3 Gradients

Declared in `defs`, referenced as `f = "@name"`:

```lua
{ op = "GL", id = "liq", x1 = 0, y1 = 0, x2 = 0, y2 = 140,
  stops = { { 0, "#5FD9A8CC" }, { 1, "#2E8B6EFF" } } }

{ op = "GR", id = "glow", cx = 25, cy = 70, r = 40, fx = 25, fy = 70,
  stops = { { 0, "#FFFFFFAA" }, { 1, "#FFFFFF00" } } }
```

`GL` is linear, `GR` radial. Stop positions are `0..1`. Gradients are baked into vertex
colours at tessellation time; the tessellator subdivides automatically based on stop count
and node size so a gradient across a large smooth area interpolates cleanly.

### 5.4 Feathering

`fea` expands the shape edge outward with an alpha ramp to zero. **`fea_edge`** applies to a
band's sampled edge only, so a liquid or gas surface can carry a wide soft ramp while the
walls beside it stay crisp — one attribute instead of a stack of faked strips.

A clipped shape's feather is clipped too: where the clip cut the outline the ramp collapses
to nothing, so no halo escapes, while edges the clip never touched keep their full feather.

**The default is automatic, and resolves to roughly 1.3 screen pixels rather than a fixed
number of scene units.** This is not a detail: the same scene is drawn at wildly different
sizes depending on console resolution and camera distance, so a fixed scene-unit feather is
invisible when the console is small and a blurry halo when it is large. Set an explicit
value in scene units to override, `0` for hard edges, or something large for a glow.

Without feathering, mesh edges are hard — UGUI applies no antialiasing of its own — and a
diagonal or curved edge shows visible pixel steps. Feathering is what makes vector output
look like vector output.

This is geometric antialiasing, and it is also the honest substitute for blur and drop
shadow, neither of which the vector layer supports — both need an offscreen pass or a custom
shader, which is a separate piece of work.

---

## 6. Expressions

**Any numeric attribute may be an expression**, written as a string with a leading `=`:

```lua
{ op = "R", x = 0, y = "=$level", w = 50, h = "=100-$level", f = "@liquid" }
```

Expressions are evaluated on the client, every frame, at display refresh rate.

### 6.1 Variables

| Name | Meaning |
|------|---------|
| `t` | seconds since the scene was first shown, floating point, monotonic |
| `i` | current repeat index, `0` outside a repeat |
| `i1`, `i2`, … | enclosing repeat indices, outward |
| `n` | current repeat count |
| `$name` | scalar from the data payload |
| `$name[expr]` | element of an array from the data payload |

Out-of-range array access yields `0` rather than failing.

### 6.2 Operators

`+` `-` `*` `/` `%` `^`, unary `-`, and parentheses. Standard precedence. Comparisons are
functions, not operators (§3.1).

### 6.3 Functions

| Function | Meaning |
|----------|---------|
| `sin(x)` `cos(x)` `tan(x)` | radians |
| `atan2(y,x)` | |
| `abs(x)` `sign(x)` `sqrt(x)` | |
| `floor(x)` `ceil(x)` `round(x)` | |
| `min(a,b)` `max(a,b)` `clamp(x,lo,hi)` | |
| `lerp(a,b,t)` | unclamped linear interpolation |
| `mod(a,b)` | always positive result, unlike `%` |
| `saw(x)` | rising ramp, period 1, range `0..1` |
| `tri(x)` | triangle wave, period 1, range `0..1` |
| `pulse(x,duty)` | `1` for the first `duty` of each period, else `0` |
| `step(edge,x)` | `0` below the edge, `1` at or above |
| `smoothstep(a,b,x)` | smooth `0..1` ramp between the edges |
| `if(c,a,b)` | `a` when `c` is non-zero, else `b` |
| `eq(a,b)` `lt(a,b)` `gt(a,b)` `lte(a,b)` `gte(a,b)` | comparisons returning `0` or `1` |
| `and(a,b)` `or(a,b)` `not(a)` | logical, on `0`/non-zero |
| `hash(x)` | deterministic pseudo-random `0..1` from `x` |
| `hash2(x,y)` | two-argument variant |
| `pi()` `tau()` | constants |

`hash` is what makes particle fields work: per-instance constants that look random, are
stable across frames and clients, and cost nothing to transmit. `hash(i)`, `hash(i+100)`,
`hash(i+200)` give an instance three independent stable values.

### 6.4 Evaluation and cost

Expressions compile once, at parse time, to a small stack program. Nodes whose attributes
contain no `t` reference are evaluated once and cached; only genuinely time-varying nodes are
re-evaluated per frame. A scene where nothing references `t` costs zero per frame after its
first build.

Vertex positions are recomputed on the CPU per frame for time-varying nodes. Topology is only
rebuilt when something structural changes — a repeat count, a path's command list, a stroke
width crossing a tessellation threshold. This is the difference between updating a few
hundred floats and re-running the tessellator.

---

## 7. Clipping

Declared in `defs`, referenced from a group as `clip = "name"`:

```lua
{ op = "CP", id = "tankclip", c = { { op = "R", x = 0, y = 0, w = 50, h = 140, rx = 5 } } }
```

**v1 restriction: clip shapes must be convex.** Rectangles, rounded rectangles, ellipses, and
convex polygons all qualify, which covers essentially every real console layout. Clipping is
performed geometrically during tessellation against the convex boundary, which needs no
stencil buffer and costs nothing at draw time.

Convexity is what makes this work: a convex region is an intersection of half-planes, so
clipping is a sequence of independent passes. Fills are clipped with Sutherland–Hodgman,
which reshapes the boundary before triangulation. Strokes are cut parametrically into the
runs that fall inside — a stroke must be *severed* by a clip, not redirected along the
boundary the way a filled contour is.

Clip outlines are evaluated once, as static geometry: an expression referencing `t` inside a
clip is silently constant. Clips are layout, not animation.

**Clip outlines are in scene coordinates**, matching where they are declared. A referencing
group brings the outline into its own space, and a nested group re-expresses an inherited
clip through the inverse of its transform — so a clip keeps clipping the same part of the
picture however the groups under it are transformed.

This was previously implemented as "the local space of the referencing group", which is the
same thing only when that group has no transform. Every demo happened to satisfy that, so
the documented behaviour was the untested one.

**A clipped fill cannot carry holes.** A hole straddling the clip boundary needs a boolean
subtraction rather than a convex clip; holes are dropped with a warning in that case.

Non-convex clipping needs stencil work and is deferred.

---

## 8. Data payload

Element B's `data` prop is a map of named values. Numbers, arrays of numbers, and colour
strings:

```lua
{ scene = "tank1", data = { fill = 0.31, drift = -1, alarm = "#E23D3D",
                            history = { 12, 14, 19, 22, 21 } } }
```

Names match the `$name` references in the structure. Colours supplied as data let
state-driven recolouring — an alarm going red — happen without resending structure.

Rewriting this element is the entire per-tick cost of an animated console.

---

## 9. Worked example

A liquid gauge with a rippling surface and drifting motes, 50×140, clipped to a rounded
tank. Fill level and drift come from data.

```lua
local scene = {
    scene = "tank1", w = 50, h = 140, fit = "stretch",
    defs = {
        { op = "CP", id = "tankclip",
          c = { { op = "R", x = 0, y = 0, w = 50, h = 140, rx = 5 } } },
        { op = "GL", id = "liq", x1 = 0, y1 = 0, x2 = 0, y2 = 140,
          stops = { { 0, "#5FD9A8CC" }, { 1, "#2E8B6EFF" } } },
    },
    root = {
        { op = "R", x = 0, y = 0, w = 50, h = 140, rx = 5, f = "#071019" },
        { op = "G", clip = "tankclip", c = {
            -- surface ripple: one polyline, sampled by a repeat over x
            { op = "RP", n = 26, c = {
                { op = "R",
                  x  = "=i*2",
                  y  = "=140-$fill*140+3*sin(i*0.19+t*1.0)+2*sin(i*0.29-t*1.4)",
                  w  = 2,
                  h  = "=$fill*140",
                  f  = "@liq" },
            } },
            -- motes: 26 instances, each with its own stable pseudo-random constants
            { op = "RP", n = 26, c = {
                { op = "R",
                  x  = "=mod(hash(i)*50+2*sin(t*(0.6+hash(i+9)*1.1)+hash(i+1)*6.283),50)",
                  y  = "=mod(hash(i+2)*140-$drift*t*26+2.4*sin(t*(0.5+hash(i+7)*1.0)+hash(i+3)*6.283),140)",
                  w  = "=1+step(0.62,hash(i+4))",
                  h  = "=1+step(0.62,hash(i+4))",
                  f  = "#8FE8C8", fo = 0.55 },
            } },
        } },
    },
}

surface:element({ id = "tank1_s", type = "vector",
                  rect = { unit = "px", x = 10, y = 10, w = 50, h = 140 },
                  props = scene })

surface:element({ id = "tank1_d", type = "vector",
                  rect = { unit = "px", x = -10, y = -10, w = 1, h = 1 },
                  props = { scene = "tank1", data = { fill = 0.31, drift = -1 } } })
```

Per tick, Lua rewrites one small table. The ripple and the motes animate at display refresh
rate from that alone. Sixteen of these on one console is one draw call and a few thousand
vertices.

**Note the node count.** Those two `RP` nodes expand to 52 quads on the client but cost
**two nodes** of Lua construction. Writing them out explicitly would cost 52, and sixteen
gauges would cost 832 — approaching the budget in §10 for a single console. This is why `RP`
exists.

---

## 10. Budgets and limits — measured

### 10.1 Payload size: not a constraint

Read from the 0.9.5.0 source. The sync path is `UiBatch` →
`MessagePackSerializer.Serialize` → `"MP_UI_SYNC_V1:" + Convert.ToBase64String(...)` → split
into 600-char chunks (`MaxUiSyncChunkChars = 600`) → one `SsCartridgeUiSyncMessage` per chunk
via `SendAll`.

Chunk count is `(text.Length + 599) / 600` with **no cap**, and nothing on this path rejects
or truncates an oversized payload. The `too large` guards that exist apply to the
client→server *input* path (`MaxReassembledInputChars`) and to canvas ops
(`CanvasCommandMaxOpsPerBatch`); neither touches element sync.

Cost is therefore linear and paid in messages. Base64 inflates by 4/3, so B bytes becomes
roughly `1.33 * B / 600` messages — 100 KB of MessagePack is about 222 messages per sync.
Bandwidth is a consideration; a ceiling is not.

The whole send path is gated on
`NetworkManager.IsActive && IsServer && HasRemoteClients()`. **In a solo session none of it
runs** — elements apply locally with no serialisation at all. Single-player measurements say
nothing about chunking.

Confirmed in game: 4,000 nodes (28,001 `UiValue`s, 60,000 string chars) arrived intact.

### 10.2 Lua instruction budget: the real constraint

A chip gets **50,000 instructions per tick, and a tick is 0.5 s**.

Measured with `PayloadCeilingTest.lua`: building 4,000 simple nodes succeeds; 12,000 fails
with `Instruction limit exceeded` inside the builder loop. A node of six attributes costs
roughly twelve instructions to construct, putting the practical ceiling near **4,000 nodes
constructed per tick**.

Three consequences that shape the format:

1. **`RP` is not a convenience, it is the mechanism.** Any repeated geometry must be a repeat
   node. Explicit emission of a few thousand nodes is not affordable.
2. **Structure must be built once**, at spawn, and may itself need spreading across ticks for
   a large console. This is why structure and data are separate elements (§1.1).
3. **Data must stay small.** A per-tick table of a few dozen values is free; a per-tick table
   of a few thousand is not, regardless of what the network would tolerate.

The budget also bounds the pure-Lua canvas shim mentioned in the project brief: it could give
old scripts alpha and unlimited resolution, but not framerate, because the cost was always
Lua issuing operations, not the operations themselves.

---

## 11. Open questions

1. ~~Payload ceiling.~~ **Answered, §10.1.** No hard limit; cost is linear in network
   messages and absent entirely in solo play.
2. **Rebuild cost at scale.** Sixteen gauges of motes is ~1,700 quads re-evaluated per frame
   on the CPU. Expected to be trivial, but worth measuring before committing to CPU-side
   evaluation rather than pushing time-varying transforms into a shader.
3. **Text in-scene.** Deferred, but the eventual design affects whether nodes need an
   identity concept for the renderer to reconcile child objects against.
4. **Distance-based tessellation tolerance.** Recomputing flattening as the player walks
   toward a console is correct but causes topology rebuilds. Likely needs hysteresis.
5. **Structure build spreading.** If a console's structure exceeds ~4,000 nodes of
   construction, Lua must emit it across several ticks. Whether the renderer should show a
   partial scene or wait for a completion marker is undecided.
