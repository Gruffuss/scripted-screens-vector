# Vector layer — reference

Every node, attribute and function the renderer accepts. Checked against the parser, not
against the design document — where the two disagreed, the code won.

For an introduction, read [README.md](README.md) first. For the reasoning behind the design,
see [`vector-format-spec.md`](https://github.com/Gruffuss/scripted-screens-vector/blob/main/vector-format-spec.md)
in the source repository.

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
| `keep` | number | `1` makes the payload a patch: names it omits keep their values |
| `snap` | number | `1` applies this payload's numbers and number arrays at once instead of easing them in |
| `ease` | map | per-name glide timing: `{ name = seconds }` or `{ name = { seconds, "curve", delay } }` |

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

**`keep = 1` turns a payload into a patch.** By default `data` is the whole truth and a name
the payload omits is gone — which means every string a scene displays has to be resent every
tick or it vanishes. A real console was shipping about a hundred strings a tick for that
reason alone. With `keep = 1`, send a name once and then send only what changed:

```lua
props = { scene = "log", keep = 1, data = { … } }
```

Off by default, deliberately. With merging always on there would be no way to clear a value,
and the missing-name diagnostic would go quiet for any name ever sent once.

**`snap = 1` turns easing off for one payload.** Numbers normally glide from the value on
screen to the new one over the gap between payloads, which is right for a gauge and wrong for
a value that must change at once: a mode switch, a jump to a new item, or the constants
inside a running animation expression, which would otherwise drift mid-animation. Snap is
recorded per name: the latest payload that mentions a name decides, so a later payload without
`snap` eases that name again, and names a snapped payload leaves out keep easing. Colours and
strings never ease anyway. Several data elements may write to one scene, so eased and snapped
values can live on separate elements, each with `keep = 1`:

```lua
props = { scene = "log", keep = 1, snap = 1, data = { mode = 2 } }
```

**`ease` gives one value its own glide time and curve.** By default a number glides to its new
value across the measured gap to the next payload, in a straight line. That is right for a
gauge fed at a steady tick and wrong whenever a change should have its own pace — a bar that
should settle in 0.6 s whatever the tick rate, a needle that should ease out, a readout that
should step:

```lua
data:set_props({
    keep = 1,
    data = { bar = 62, needle = 0.8 },
    ease = { bar = { 0.6, "ease-out" }, needle = 0.25 },
})
```

A name with an entry glides for exactly that long, from the moment the payload applies,
whatever the gap to the next one; give it seconds alone for a straight line. Curves are CSS's:
`linear`, `ease`, `ease-in`, `ease-out`, `ease-in-out`, `cubic-bezier(a,b,c,d)` and
`steps(n)`. A duration of 0 or less snaps, and so does `snap = 1`, which wins over any timing
in the same payload.

**A third element delays the start**: `{ 0.3, "ease-in", 0.15 }` holds the value where it is
for 0.15 s and then glides for 0.3 s. Stagger a row of bars by giving each a delay of its
own — `i * 0.05` — and they set off in sequence from one payload.

**Colours glide too, when asked.** A colour sent as data (`f = "$state"`) changes at once by
default. Give its name an `ease` entry and it glides from the colour on screen to the new one
over that time and curve, exactly as a number would: `ease = { state = { 0.4, "ease-out" } }`.

**Motion driven by an event costs one payload.** `since($name)` in an expression is the
seconds since `name` last arrived on this client, so the chip sends a value once, at the event,
and the scene draws the rest:

```lua
-- one send per jump; the arc is drawn here at the frame rate
data:set_props({ keep = 1, snap = 1, data = { jump = 24 } })
-- in the scene
{ op = "G", t = { 0, "=-max(0, $jump*since($jump) - 45*since($jump)^2)" }, c = { ... } }
```

Resending the same value restarts the count, so every send is an event. Send such values with
`snap = 1` so the value itself does not glide. A name never sent reads as a very long time ago.

Timing describes a **change**, not a value: it applies to the glide that payload starts. Send
it again with the next value if that one should glide the same way — a payload that restates a
name without timing glides it the ordinary way, exactly as one that restates a name without
`snap` eases it again.

Restating a name mid-glide restarts it **from what is on screen**, so a value that changes
faster than it can glide keeps moving smoothly instead of jumping back. Number arrays take
timing the same way. Colours and strings never glide at all.

A payload that arrives while the previous one is still waiting to apply is merged into it
(`keep = 1`) or replaces it (a full payload), so no patch is lost.

Give it a 1×1 rect at negative coordinates so it draws nothing.

### Scene as text — `src`

A scene may be given as one string instead of nested tables, in place of `root` and `defs`:

```lua
props = { scene = "tank", src = [==[
SCENE w=200 h=200 fit=stretch
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

**Why it exists: clipping, scrolling, click regions, and text that changes without
re-declaring an element.** A `T` inside an `SC` is not expressible as `label` elements over
the artwork at any price.

**Not for the instruction budget.** Measured on a real console, three pages built both ways,
two got worse: 25.0k → 28.3k, 31.5k → 28.9k, 40.1k → 41.9k. A `src` line is free only when it
is a **literal**, already in the compiled chunk. One built with `string.format` costs about
what the node table costs, and serialising tables into text at build time is a straight loss.

**Wrap the string in `[==[ … ]==]`, not `[[ … ]]`.** An array value ends in `]]`, and Lua's
long-string bracket closes at the first one it sees — so a scene containing
`stops=[[0,#5FD9A8],[1,#2E8B6E]]` is silently truncated at that point and everything after it
vanishes. A longer level of bracket has no such collision.

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

### Diagnostics

A scene reports its own faults rather than drawing nothing and leaving you to guess:

| fault | what happens |
|-------|--------------|
| a single shape too large for one mesh | reported, naming what was dropped |
| unknown op | reported; the node is skipped |
| unknown attribute name | reported, with the op and the node id |
| malformed expression | reported; that attribute falls back to its default |
| `$name` with no data value | reported; a bound **colour** draws **magenta** |
| missing gradient or clip id | reported; the reference is ignored |

Anything reported also puts a magenta hatched border around the surface, so a broken scene
looks broken instead of looking switched off. Detail goes to `BepInEx/LogOutput.log`.

Magenta rather than white for an unresolved colour is deliberate: white is a colour somebody
meant to use, and magenta is not, so an unresolved binding reads as a fault rather than a
design decision.

**The unknown-attribute check is a union of every key the renderer reads**, so it catches a
typo — `fille`, `strke` — which otherwise vanishes silently because unknown keys are ignored
by design. It does not catch a real key on the wrong op. `USE` and `SYM` are exempt, since
symbol parameters are arbitrary by definition.

### `vector_stats` — the MCP tool

If [StationeersLua](https://steamcommunity.com/workshop/) is installed, the mod registers a
`vector_stats` tool with its MCP server. It reports every live surface: node and shape counts,
vertices, rebuild rate, tessellation and upload cost, on-screen size, whether the scene is
animated or scroll-driven, and **the problems and unresolved data names above**.

Two more sections appear when they apply, and "no problems" is printed only when every section
is empty:

- `TEXT` — anything the text layer had to compromise on, such as a shadow reduced to fit the
  font's padding. These used to reach the BepInEx log only.
- `DROPPED` — a mesh piece too large to upload, which means everything drawn in it is missing
  from the console. A single shape past 60,000 vertices is the only way to get one.

```
vector_stats            -- all surfaces
vector_stats scene=gas  -- one
```

The first line is the addon's version, so "is the build I just made the one that is running"
is answerable from inside the editor.

The same server also carries this documentation. Search it with scope `vector`, or start
from `stationeers://vector/index`:

| URI | content |
|-----|---------|
| `stationeers://vector/index` | QUICKSTART.md, a brief written for AI editors, then every URI below |
| `stationeers://vector/guide/<section>` | README.md, one resource per `##` section |
| `stationeers://vector/reference/<section>` | this file, one resource per `##` and `###` section |
| `stationeers://vector/changelog` | CHANGELOG.md |
| `stationeers://vector/patterns` | Patterns.lua |
| `stationeers://vector/examples/index` | the examples, each at `stationeers://vector/examples/<file>` |

Sections rather than whole files, because a search returns at most two hits per resource.
Everything is read from the mod folder when requested, so it always matches the installed build.

Bound by reflection, so the mod loads normally without StationeersLua and simply does not
register the tool or the documentation.

### Size limits

**One mesh holds 60,000 vertices, and a surface uses as many meshes as it needs.** Extra
meshes are created automatically; `vector_stats` says `across N meshes` when there is more
than one. Measured: 150 shadowed cards at 148,000 vertices across three meshes drew in full
with no change to frame time.

That per-mesh figure is **UGUI's limit, not a setting** — its own vertex helper throws at
65,000, because the `CanvasRenderer` batcher works in 16-bit indices. Asking a mesh for 32-bit
indices and handing it to a CanvasRenderer anyway takes the game down natively, with nothing
in the log. More meshes is the supported answer and the one TextMeshPro uses.

Cuts fall **between shapes**, never inside one, so a single shape has to fit one mesh on its
own. One that does not is refused and reported. Nothing else is capped: a page can be as dense
as you like, and the cost you will feel first is the mesh upload, which is on the frame and
grows with vertex count.

**What is expensive, measured at 1395 px:**

| | vertices |
|---|---|
| a filled rectangle | 4 |
| the same rectangle with a feather | 64 |
| the same rectangle with one `sh` | 640 before 0.11.19, up to half that since |
| a small circle with a feather | 40 |

A box-shadow is the most expensive ordinary thing a scene can ask for, because its ring count
follows the blur's on-screen size. Since 0.11.19 it lays one ring per four screen pixels of
reach rather than two — checked against a 400-ring reference, the difference is a few isolated
pixels at 12/255 — so a shadow below the 24-ring cap costs half what it did, and one at the cap
about a quarter less. Reducing the blur radius is still the direct lever.

### Screen capture

`capture_scripted_screen` works on vector consoles, artwork and text alike. It is not free:
the capture rebuilds the surface and renders a clone inside one call, so the geometry is
tessellated **inline on the main thread** rather than on a worker. A capture of a dense
console costs a frame; ordinary rendering is unaffected.

The capture rebuilds the scene from its elements, so a `keep = 1` scene keeps the values it was
sent only if its structure is unchanged; a scene that sends a different structure on rebuild
must send its values with it. With Diagnostics on, every capture writes what it copied (each
object, whether it is active, each label's text and font) to
`ScriptedScreensVector-captures` in the system temp folder, and the log names the file.

### Text and draw order — `ztext`

**Labels obey scene order by default.** A shape declared after a label covers it; one declared
before it does not. `ztext = 0` on the structure element restores the older behaviour, where all
text painted above all geometry.

That older behaviour existed because labels are TextMeshPro objects parented beside the mesh
rather than part of it, and UGUI draws children in sibling order — so the whole text layer drew
either entirely before or entirely after the whole surface. "On top" and "underneath" were the
only two states and they applied to every label at once.

**What it costs.** A label a later shape actually covers forces the mesh to be cut there, and
each extra mesh is a draw call and a little more upload. A cut is forced only on genuine overlap
of the two boxes.

How often that happens depends on how the scene is laid out, not on how many labels it has.
Measured in game:

| | shapes | meshes |
|---|---|---|
| `examples/12-click.lua` — labels beside the shapes after them | 19 | 1 |
| a dense page of nested panels, label boxes touching later boxes | 48 | 11 |

A label whose box stays clear of everything declared after it costs nothing. What produces cuts
is a label **box** reaching over a later shape: nested panels, and label boxes made wider than
their text so it never wraps. Boxes that merely touch do not count, and neither does anything
transparent — a feather or the fading edge of a shadow cannot cover text — which took that page
from 13 meshes to 11. **Neither case is worth worrying about**: on the 48-shape page the whole difference was
0.06 → 0.13 ms of mesh upload, against a 16.7 ms frame, and tessellation is unchanged because it
is the same geometry handed over in more pieces.

Deciding costs about 0.19 ms per 40,000 vertices — measured on .NET 8, so read it as a shape
rather than a figure, since the game runs Mono and the two do not scale together. It is on the
tessellation worker, not the frame.

**A label covered by the very first shape** goes under it too. The surface's own renderer
draws before its children, so in that one case the first mesh is left empty and the geometry
starts at the second, with the label between them: one extra draw call, only when it happens.
Before 0.11.21 such a label stayed on top -- a caption drawn over the picture declared after
it.

**When to reach for `ztext = 0`:** a scene that deliberately floats a readout above artwork
drawn after it, and wants that regardless of declaration order.

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
| `IMG` | a picture from a URL, in scene order |

### `G` — group

| Key | Type | Meaning |
|-----|------|---------|
| `t` | `{x, y}` | translate |
| `r` | number/expr | rotate, degrees clockwise |
| `s` | `{sx, sy}` | scale |
| `a` | `{x, y}` | anchor the transform pivots about, default `{0, 0}` |
| `o` | number/expr | group opacity `0..1`, multiplied into all descendants |
| `v` | number/expr | `0` removes the subtree entirely, clicks included (CSS `visibility`) |
| `clip` | string | id of a `CP` in `defs` |
| `m` | `{a, b, c, d, e, f}` | CSS `matrix()`, applied after `t r s` (innermost) |
| `bri` `con` `sat` `hue` `gray` `sep` `inv` | number/expr | colour filters, CSS `filter()` semantics |
| `mask` | `"@gradient"` | multiplies every colour under the group by the gradient's alpha |
| `c` | array | child nodes |

Applied scale → rotate → translate, about `a`. Nests without limit.

**`o = 0` is free, not merely cheap.** A group at zero opacity is skipped whole rather than
node by node, so keeping an alternative layout in the scene and showing it with `o` costs
nothing while it is hidden. Clicks, scroll containers and pictures under it still register,
as `opacity: 0` does in a browser; use `o = 0` on those only when you mean them to stay live.

**Animation inside a hidden group costs nothing.** A scene redraws for `t` only when the last
redraw reached something that reads it, so a blink or pulse inside a group hidden by `v = 0`,
by `o = 0`, or by data stops the redraws while it is hidden and resumes when it is shown.

**A group that only fades is redrawn by nothing.** When a group's own `o` is its only
animation -- an expression of `t` alone, over content that does not move and holds no text,
outside any `RP` -- it is drawn once at full opacity in a mesh of its own, and the renderer
sets its opacity every frame. A blinking status dot then costs no redraws at all, at one extra
draw call per such group. An `o` that also reads data, a group that moves, or one holding a `T`
redraws as usual.

**`v = 0` is the other half**, and it is CSS `visibility: hidden`: the subtree is not there
at all, so a button under it cannot be clicked and a scroll container under it reports
nothing. It is an expression like anything else, so one scene can carry several states and
show one:

```lua
{ op = "G", v = "$night", c = { --[[ dark skin ]] } },
{ op = "G", v = "=1-$night", c = { --[[ light skin ]] } },
```

Which to reach for: `o` to fade something out that should stay live, `v` to switch between
states that must not overlap.

**`m` is a CSS matrix**, `x' = a·x + c·y + e`, `y' = b·x + d·y + f`, and it is the innermost
factor, as in `transform: translate() rotate() scale() matrix()`: points go through the
matrix first, then the scale, rotation and translation. Stroke widths scale by
`sqrt(|ad − bc|)`. A matrix of any other length is a problem, not a silent identity.

**Text follows a skew or an uneven scale** from `m` or `s = {sx, sy}`: the letters lean and
stretch with the group, as CSS transforms them. The viewbox `fit` is not part of that -- with
`fit = "stretch"` into a box of another shape, shapes stretch to fill it and text keeps its
letterforms, as it always has.

**Filters** take CSS's amounts: `bri=1` `con=1` `sat=1` `hue=0` `gray=0` `sep=0` `inv=0` change
nothing; `gray`, `sep` and `inv` clamp to `0..1`, `hue` is in degrees. Several on one group
apply **in the order written**, as a CSS filter list does, and a nested group's filters apply
before its parent's. They reach fills, strokes, feathers, shadows and text, including text
shadows. Two limits:

- They act on **vertex colours**, so a gradient is filtered at its vertices and interpolated
  between them. Exact for flat colours; within a triangle, contrast clamping and hue rotation
  can differ slightly from filtering every pixel.
- **An `IMG` is not filtered.** Its colour is in the texture, and filtering the vertex would
  tint the picture flat instead. It draws unfiltered and the scene reports it.

**`mask`** is `mask-image` with a gradient: every vertex under the group takes the gradient's
alpha at its position, and text takes it at its glyph corners. With `units = "bbox"` the
gradient spans **the group's content** -- its shapes and its labels' boxes -- measured after it
is drawn. A two-stop linear mask is exact on any geometry. A radial, conic or many-stop one cuts
the group's shapes on a grid of cells a few screen pixels across, so the alpha follows the ramp
wherever it changes; the cost follows the masked area on screen, and shapes the mask does not
vary across are left alone. A conic mask is also cut along its start angle, so it keeps the
hard edge CSS draws there. Text cannot be cut: a letter straddling a conic's start angle fades
across it rather than being split. An undeclared gradient is a problem.

```lua
{ op = "G", mask = "@fade_right", gray = 0.6, c = { ... } }
```

### `RP` — repeat

| Key | Type | Meaning |
|-----|------|---------|
| `n` | **literal number** | instance count — not an expression |
| `lod` | number | `1` allows count reduction at distance when Count LOD is switched on (off by default); omitted means never |
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

**Curve LOD**, when a player switches it on in the settings (it is off by default), samples it
more coarsely when it is drawn small, quantised so camera drift does not retessellate, and
never above the authored `n`. It is safe to switch on: `i` is a float and the geometry expressions are continuous in it, so a coarser step walks
the *same* curve. Nothing is dropped, unlike count LOD on a repeat.

### `LS` — sampled polyline

Same sampling rule as `YS` (`n`, `x`, `y`), stroked rather than filled. The line-chart and
waveform primitive.

### `SC` — scroll container

| Key | Type | Meaning |
|-----|------|---------|
| `id` | string | **required** — the scroll position is stored under it |
| `x`, `y`, `w`, `h` | number/expr | the viewport box, in the enclosing coordinates |
| `ch` | number/expr | total content height; at or below `h` nothing scrolls |
| `rx` / `ry` | number/expr | corner radii, same rules and per-corner form as `R` |
| `o` | number/expr | container opacity, multiplied into all descendants |
| `so` | number/expr | a scroll offset to jump to — applied only when `sov` changes |
| `sov` | number/expr | version for `so`; required with it |
| `c` | array | children, in the enclosing coordinates, translated by the scroll offset |

A group that clips to its own box and slides its children inside it. **Children are written in
the same coordinates as everything around them** -- put the first row at the container's own
`y` and the last `ch` below it -- and the container translates them by the offset.

```lua
{ op = "SC", id = "log", x = 4, y = 20, w = 192, h = 120, ch = 480, rx = 6, c = {
    { op = "RP", n = 12, c = {
        { op = "R", x = 8, y = "=20 + i*40", w = 176, h = 34, rx = 4, f = "#12202F" },
        { op = "T", x = 16, y = "=28 + i*40", w = 160, h = 20,
          text = "$line", size = 11, f = "#8FA6B8" },
    } },
} }
```

**The scroll position never leaves the client.** A wheel notch moves the offset and triggers
one rebuild — no tick, no network, no instructions. That is the whole reason this exists as a
node rather than as a ScriptedScreens `scrollview` with a scene inside it: routing a scroll
through the chip makes it cost half a second and a slice of the instruction budget.

Wheel is a fifth of the viewport per notch; drag moves content with the pointer. Both clamp to
`0 .. ch - h`, so a container whose content fits cannot be moved at all.

**Setting the offset from a script: `so` and `sov`.** `so` alone would pin the list, fighting
every wheel notch. Instead the offset is applied **once per new `sov`**: bump the version when
the script wants to jump (to the newest log line, to a selected row), and the player's wheel
and drag own the offset again straight after. Both may be data bindings:

```lua
{ op = "SC", id = "log", x = 4, y = 20, w = 192, h = 120, ch = 480,
  so = "=$jump_to", sov = "=$jump_version", c = { ... } }
```

**Reading the offset from another mod.** The chip never learns the offset: wheel and drag stay
on the client and nothing is sent. A mod running on the same client can follow it, and only
an `SC` with an `id` is reported:

```csharp
// host: the vector element's GameObject ("Ui:<element id>"); all values in scene units
VectorGraphic.ScrollChanged += (host, scId, offset, max, view) => { ... };
VectorGraphic.TryGetScroll(host, "log", out var offset, out var max, out var view);
```

`ScrollChanged` is raised on the main thread after the rebuild that shows the change, once
per container per rebuild (MaximumHz at most, 60 by default), and once when a container first appears. `max` is
`ch - h`. The type is internal to this mod, so reach it by reflection
(`ScriptedScreensVector.VectorGraphic, ScriptedScreensVector`). With Diagnostics on, every
report is logged as `scroll Ui:<element>/<id>: offset of max, view h`.

**`sy` and `vh` report *this* container inside it.** `sy` is the scroll offset, **zero at
rest**, and `vh` is the container's height -- so pinned artwork is written at the container's
own `y` plus `sy`:

```lua
-- container at y = 20, height 120
{ op = "R", x = 4, y = "=20+sy",         w = 192, h = 18, f = "#0B1622" }  -- pinned header
{ op = "R", x = 4, y = "=20+sy+vh-12",   w = 192, h = 12, f = "@fade" }    -- bottom fade
```

**The same holds in a gradient that a shape or a `mask` inside the container uses.** A def
reading `sy` or `vh` is resolved where it is used, so one `@fade` def serves a list fade in
every container that uses it:

```lua
-- container at y = 20, as above: the last 16 units of its window fade out
{ op = "GL", id = "fade", x1 = 0, y1 = "=20+sy+vh-16", x2 = 0, y2 = "=20+sy+vh",
  stops = { { 0, "#FFFFFFFF" }, { 1, "#FFFFFF00" } } }
{ op = "SC", id = "list", y = 20, ..., c = { { op = "G", mask = "@fade", c = { ... } } } }
```

**`sy` is an offset, not a position**, exactly as it is for a ScrollRect. The difference is
that a ScrollRect scrolls the whole element, so its content origin *is* the scene origin and
`y = "=sy"` lands on the viewport top by itself. An `SC` sits somewhere inside a scene, so its
own `y` has to be added. Leaving it out puts the artwork at the top of the viewbox, above the
container, where it will be clipped away and look like nothing happened.

**Limits worth knowing:**

- **Vertical only.** Horizontal scrolling has no console in this repo asking for it, and it
  doubles the state, the clamping and the input handling.
- **Containers do not nest usefully.** An inner one would need its own wheel target inside the
  outer one's and the pointer cannot be in both; the inner container clips and draws correctly
  but the wheel always finds the innermost box under the pointer.
- **No scrollbar is drawn.** Draw one: with the container at `y`, a thumb of height
  `h*h/ch` sits at `y + sy + sy*(h - h*h/ch)/(ch - h)`. Two expressions, and a scene that
  wants a different indicator is not fighting a built-in one.
- **A text node inside is clipped by the container's box.** A square box uses RectMask2D; a
  rounded one masks text to its real outline with a stencil. See `T`.

### `T` — text

| Key | Meaning |
|-----|---------|
| `x`, `y`, `w`, `h` | the box the text is laid out in |
| `text` | a literal, `"$name"`, `"$rows[i]"` for one slot of a data array, or a literal holding several `{$name}` placeholders |
| `fmt` | printf spec for a bound **number**, e.g. `"%.1f"` |
| `unit` | literal suffix appended after the text |
| `missing` | what to draw when the name has no value; default `"--"`. An empty string is a value and draws nothing |
| `size` | font size in scene units; scales with the transform |
| `f` | colour, as on any shape |
| `fo` | opacity `0..1`, as on any shape; multiplied by the enclosing group's `o` |
| `align` | `left` (default), `center`, `right`, `justified` (extra width goes between words, as CSS) |
| `valign` | `top` (default), `middle`, `bottom` |
| `font` | a registered TMP family, e.g. from the companion fonts mod |
| `weight` | `bold`, or a number ≥ 600 |
| `cspace` | character spacing |
| `wrap` | `1` lets the text run to more than one line inside its box |
| `lh` | line height as a multiple of the font size, CSS style; omitted uses the font's own |
| `sh` | text shadows, several allowed, one of them `inset` — see Shadows |
| `ow` | outline width in scene units, centred on the glyph edge (CSS `-webkit-text-stroke`); scales with the transform |
| `oc` | outline colour, `#rrggbb` or `#rrggbbaa`; default black |
| `fl` | first-line overrides, `"f=#fff size=12 weight=bold font='Name'"` — see below |
| `fit` | `none` (default), `ellipsis`, `shrink` |
| `min_size` | floor for `shrink` |

```lua
{ op = "T", x = 8, y = 8, w = 120, h = 20, text = "$pressure",
  size = 14, f = "#EAF4F8", align = "right", fit = "ellipsis" }
```

**Single line unless you ask.** A readout that quietly becomes two lines pushes its own
baseline and shunts everything the scene placed around it, which reads as a rendering fault
rather than as a long string. A paragraph says so:

```lua
{ op = "T", x = 8, y = 8, w = 180, h = 60, text = "$body",
  wrap = 1, lh = 1.4, size = 9, f = "#8FA6B8" }
```

`lh` is a multiple, as in CSS `line-height: 1.4`, not an absolute.

**Several values in one label.** A literal `text` may hold placeholders, each with its own
format; one without a format takes the node's `fmt`:

```lua
{ op = "T", x = 8, y = 8, w = 220, h = 14, size = 9,
  text = "set {$press:%.1f} kPa · trip {$trip:%.0f} · {$rows[i]}" }
```

Each placeholder resolves as `text = "$name"` would: a string as it is, a number through its
format, and `missing` in its place when the name has no value, while the rest of the text still
shows. `unit` still goes after the whole text. A `{` not followed by `$` or `=` is ordinary text,
and a label whose printed characters did not change makes no new string, so a line of readouts
costs the same as one.

**A placeholder may be an expression:** `{=expr}` or `{=expr:%.1f}`, over `t`, data, `since()`
or anything else an expression reads. A clock or a counter then needs no payload at all:
`text = "uptime {=floor(t):%d} s"`.

**`f` may be a gradient.** `f = "@name"` samples the gradient at every glyph's corners, so a
linear ramp is exact within each glyph and continuous across the label; radial and conic are
exact at the corners and interpolated between them. With `units = "bbox"` the ramp spans the
text's box (`x y w h`), not the glyphs' ink. Rich-text `<color>` tags multiply into it. It is the
face only: text shadows and the outline keep their own colours.

**`ow` outlines the glyphs** inside the text engine's own shader, half inside the letter's edge
and half outside, as CSS `-webkit-text-stroke` does. How wide it can go depends on the font's
atlas padding; past it the outline is capped at the widest the font allows and the surface's
TEXT warnings say so. A text shadow is cast by the letters, not by their outline.

**`fl` styles the first line**, like CSS `::first-line`: a string of `f`, `size` (scene units,
scaled like `size`), `weight` and `font`, quoted where a value has spaces. Only the text engine
knows where a line breaks, so the label is laid out once, the break read, and rich tags placed
around that span; if the tags move the break, it is placed once more. Recomputed only when the
text, box or style changes.

```lua
{ op = "T", x = 8, y = 8, w = 180, h = 60, wrap = 1, text = "$body",
  size = 9, fl = "size=12 weight=bold f=#EAF4F8" }
```

**Text is not part of the mesh, and that shapes what it can do.** TMP builds its own geometry
on its own GameObject and is main-thread only, while tessellation runs on a worker — so the
walk records where each `T` landed and the labels are created and updated when the job lands.

| | |
|---|---|
| transform, scale, scroll with the group | **yes** — the rect goes through the frame matrix, rotation included |
| fade with the group | **yes** — `o` and `fo` reach the label as its own alpha |
| updates from `data` | **yes** — strings are data values now, so `text = "$name"` works like a number |
| rich text, registered fonts | **yes** — it is real TMP |
| clipping | **to the clip's real outline.** An axis-aligned rectangle uses `RectMask2D`; a rounded, elliptical or concave clip masks through the stencil, at one extra draw call per masked label |
| filters and masks | **yes** — a group's filters and `mask` reach the glyphs' vertex colours |
| update rate | at the **rebuild** rate, not instantly |

`ellipsis` and `shrink` are TMP's own overflow modes, so fitting is done by the engine that
knows the glyph metrics rather than estimated.

Group opacity has to arrive this way because it cannot arrive any other way: a TMP child draws
**above** the mesh, so nothing in the geometry can fade it. Without it a `G o = 0.3` would fade
all its artwork and leave the readable part at full strength.

This is what lets a row fade at the edge of a scroll container:

```lua
{ op = "G", o = "=clamp((20+sy+vh-Y)/16,0,1)", c = { { op = "T", ... } } }
```

Objects are pooled by index and reused across rebuilds; surplus labels are disabled rather
than destroyed, so a scene alternating between two pages does not churn objects. **A fully
transparent `T` is still placed**, deliberately: the pool is keyed by placement order, so
skipping one would hand every later label the wrong text.

**One node for a whole list.** `text = "$rows[i]"` inside a repeat takes its string from a
data array, so thirty rows are one `T` and one array rather than thirty nodes:

```lua
{ op = "RP", n = 30, c = {
    { op = "T", x = 8, y = "=6+i*20", w = 160, h = 16, text = "$lines[i]", f = "$tints[i]" },
} }
```

```lua
data:set_props({ data = {
    lines = { "O2 low", "pump 3 offline", ... },
    tints = { "#F59E0B", "#E23D3D", ... },
} })
```

The index is a full expression, not just `i` — `$rows[n-1-i]` reverses a list and
`$cols[mod(i,4)]` cycles a palette. Out of range draws `missing` rather than failing.

**Formatting a number, so the chip does no string work.** Most console text is a number with a
unit, and formatting it in Lua costs a `string.format` per label per tick:

```lua
{ op = "T", x = 8, y = 8, w = 90, h = 16, text = "$press", fmt = "%.1f", unit = " kPa" }
```

The chip then sends the number it already had. `fmt` takes the printf spec you would have
passed to `string.format`: `f`, `e`, `g`, `d`, `i`, `x`, `X`, with precision. Width and flags
are ignored — lay text out with `align` and a box instead. A spec that cannot be read renders
`missing`.

A name may hold a string or a number. The string wins, so a payload that deliberately sends
`"OFFLINE"` for a numeric readout shows that word rather than a formatted zero.

**Why use it over a `label` element:** it changes its text without re-declaring an element,
it is written in viewbox units rather than console pixels, and it moves, clips and scrolls with
the scene. `ui:element` itself is a C call and cheap — measured, 90 labels became one `src` and
the page went from 31.5k instructions to 28.9k, a real saving and a modest one. The reason to
reach for `T` is what a label cannot do at all, not the budget.

### `SP` — spline

| Key | Meaning |
|-----|---------|
| `p` | flat point array `{x, y, x, y, ...}` |
| `seg` | segments per span |

Catmull-Rom, so the curve passes **through** its points rather than being pulled toward them.

---

### `IMG` — image

| Key | Meaning |
|-----|---------|
| `x`, `y`, `w`, `h` | the box |
| `src` | URL (`https://`, `file://`) |
| `fit` | `fill` (default, stretch), `contain`, `cover`, `none` (one scene unit per texel), `scale-down` (`contain` if the picture is larger than the box, else `none`) — CSS `object-fit` |
| `rx` / `ry` | corner radii, as `R` |
| `o` | opacity `0..1`, multiplied by the enclosing group's |
| `uv` | `{u0, v0, u1, v1}`: the part of the picture shown, fractions of the texture, **v from the top**; default `{0, 0, 1, 1}` |
| `at` | `{ax, ay}`: where the picture sits in the room `fit` leaves it, fractions of the free space; default `{0.5, 0.5}` (centred), CSS `object-position` |
| `off` | `{ox, oy}`: scene units added after `at` has placed the picture, under every `fit`; default `{0, 0}` |
| `tile` | `{tw, th}`: repeat the picture at that size across the box, from where `at`/`off` place one. Both `0` is the natural size (one scene unit per texel); one `0` keeps the picture's aspect. `"contain"` or `"cover"` sizes it to the box. `fit` does not apply |
| `rep` | with `tile`: `repeat` (default), `once`, `round` or `space`, one word for both axes or `{x, y}` -- CSS `background-repeat` |
| `smp` | `point` for hard-edged pixels (CSS `image-rendering: pixelated`); default smooth |
| `slice` | `{t, r, b, l}`: nine-slice insets in texels of the picture (of the `uv` crop, if any) |
| `bw` | `{t, r, b, l}`: how wide those borders are drawn, in scene units; default the `slice` numbers |
| `mid` | `0` leaves a nine-slice's middle undrawn; default `1` |
| `srep` | how a nine-slice's edges and middle fill their length: `stretch` (default), `repeat`, `round` or `space`, one word or `{across, down}` -- CSS `border-image-repeat` |

```
IMG x=10 y=10 w=80 h=45 src=https://example.com/map.png fit=cover rx=6
```

**Nothing is drawn until the picture has loaded**, then the surface rebuilds once. Each source is
fetched once per session and shared by every console that uses it. A failed load is a scene
problem naming the error.

An image draws **in scene order**: shapes declared after it cover it and ones before it are
underneath. That needs a mesh of its own, so each image is one more draw call. Clips, masks and
group opacity apply; colour filters do not — see `G`.

`contain` draws only where the picture is, leaving the rest of the box empty; `cover` fills
the box and crops the picture's long side. Corner radii cut the box in every fit. A picture
that does not cover its box — `contain`, a small one under `none`, or one moved by `off` —
draws only where it is.

**`at` places the picture** within whatever `fit` leaves free: `{0, 0}` is flush top-left,
`{1, 1}` flush bottom-right. Under `cover` the free space is negative, so the same numbers
choose which part of the picture is kept — `{0.5, 0}` keeps the top. It has no effect under
`fill`, which leaves nothing free. Both values take expressions, so a slow pan across a
cropped picture needs no data at all.

**`off` shifts it by a length** after `at` has placed it, the way CSS writes an edge offset:
`object-position: right 10px bottom 4px` is `at = {1, 1}, off = {-10, -4}`. It applies under
`fill` too, moving the whole picture and leaving the uncovered strip empty.

**`tile` repeats it**, CSS `background-repeat`: one copy is placed by `at` and `off` exactly as
under `none`, then copies are laid edge to edge in every direction until the box is covered,
and the edge ones are cut by the box and its corner radii. With `uv` the cropped part is what
repeats. Each copy is a quad, so a box of small tiles costs vertices; past 4096 tiles the
picture is drawn once and the scene reports why.

**`rep` chooses how each axis repeats**, as CSS `background-repeat` does. `once` is the anchored
copy alone, so `rep = { "repeat", "once" }` is a single row (`repeat-x`), and `tile` with `once`
on both axes is simply a picture at an explicit size. `round` resizes the tiles so a whole number
fills the box, a `0` axis following in proportion. `space` lays as many whole tiles as fit,
first and last against the box's edges, with even gaps between; where fewer than two fit it
behaves as `once`.

**`smp = point`** keeps pixel art square at any size. The pixelated picture is loaded as a
texture of its own, once per session, so the same source drawn smooth elsewhere is unaffected.

With `uv`, the cropped part is the picture: `fit` works from its size, not the texture's. That is
what a sprite sheet or canvas `drawImage` with a source rectangle needs.

**`slice` draws a nine-slice frame**, CSS `border-image`: the picture is cut `t r b l` texels in
from its edges, the corners drawn `bw` wide, the edges stretched between them and the middle
stretched both ways. It covers the box itself, so `fit`, `at`, `off` and `tile` do not apply.
Borders that add up to more than the box are scaled down together, as in CSS. CSS draws the
middle only with `fill`; here it is drawn unless `mid = 0`.

**`srep` tiles the edges instead of stretching them**, CSS `border-image-repeat`. An edge's
tiles keep the source part's proportions at the border's width; the middle takes the top
edge's scale across and the left edge's down. `repeat` centres the tiles and cuts the ones at
the ends, `round` resizes them so whole tiles fit exactly, and `space` spreads whole tiles with
even gaps. The first word is the top and bottom edges and the middle across; the second the
left and right edges and the middle down.

```
IMG x=10 y=10 w=180 h=60 src=file:///panel.png slice=[12,12,12,12] bw=[6,6,6,6]
```


## Paint

Applies to any shape node.

### Fill

| Key | Meaning |
|-----|---------|
| `f` | `#rrggbb`, `#rrggbbaa`, `@gradientId`, `$dataName`, `none`, or a gradient sample (below) |
| `fo` | fill opacity `0..1`, expression-capable |

**Opacity blends in linear light**, the way the game's UI does, not in the sRGB numbers a browser
averages. Half-opaque `#EAF4F8` over `#0D161C` shows as about `#ACB5B7`, where a browser shows
`#7B858A`, so anything faded reads lighter than the same value in a CSS mockup. Applies to every
alpha: `fo`, `so`, `o`, colour alpha, gradients, masks and shadows.
| `fr` | `nonzero` (default) or `evenodd` |

**Gradient sample** — the way to animate a colour:

```lua
f = { grad = "status", at = "=clamp($level,0,1)" }
```

Samples the ramp at an expression and yields a flat colour. Needed because the expression
evaluator is scalar: `f = "=lerp(...)"` has nothing to return, since a colour is not a number.

**The same form works on `s`**, so an outline can follow a value exactly as a fill does.

**In the text scene format**, which has no map syntax, write it as `fat` and `sat`:

```
R x=10 y=10 w=80 h=20 f=@status fat==clamp($level,0,1)
L p=[10,40,90,40] s=@status sat==$level sw=2
```

`fat` samples the fill's gradient and `sat` the stroke's; both need `f` / `s` to be a
`@gradient`, and both work on `T`.

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

### Clicks — `click`

A node with an `id` and `click = 1` becomes a hit region. The click arrives at the **vector
element's own `on_click`**, with the node id as the value:

```lua
ui:element({
    id = "menu", type = "vector",
    props = { scene = "menu", src = [==[
        R id=row1 click=1 x=0 y=0  w=200 h=24 f=#12202F
        R id=row2 click=1 x=0 y=26 w=200 h=24 f=#12202F
    ]==] },
    on_click = function(nodeId, player) ... end,
})
```

Lua registers handlers **per element**, and a vector node is not an element — so the node id
travels as the event's *value* rather than its id. One handler serves the whole scene.

`click = 1` is opt-in and separate from `id`, because an id is also how a node is patched and
patch targets are common; making all of them swallow clicks would be a surprise. A scene with
no clickable node stays transparent to the pointer exactly as before.

**A click lands on what is drawn:** inside the node's own outline and inside any clip it is
drawn through. Where regions overlap, the one drawn last wins. Before 0.11.21 it was the outline's bounding rectangle, so a circle took clicks
in its corners, a turned shape across its upright bounds, and a clipped one where the clip had
cut it away -- including list rows scrolled out of sight.

**A clickable node inside a repeat reports its index.** One node stands for n rows, so the id
alone cannot say which was hit; the value becomes `id:i`:

```lua
on_click = function(nodeId, player)
    local id, index = nodeId:match("^(.-):(%d+)$")
    ...
end
```

Outside a repeat it is the bare id, unchanged.

**`press = 1` reports holding as well as clicking**, for a press-and-hold button or a
hold-to-act control. The node is clickable as with `click = 1`, and the same `on_click` also
receives:

| value | when |
|---|---|
| `down:id` | the pointer goes down on the node |
| `up:id` | that pointer comes up, **wherever it is** by then, as a browser's `pointerup` does |
| `leave:id` | that pointer, still held, moves off the node (once per press) |

A full press and release on the node therefore arrives as `down:id`, `up:id`, then `id` (the
click). Inside a repeat the id carries its index as above: `down:row:3`. Split on the first
colon:

```lua
on_click = function(value, player)
    local kind, id = value:match("^(%a+):(.+)$")
    if kind == "down" then ... elseif kind == "up" then ... elseif kind == "leave" then ...
    else --[[ a plain click on `value` ]] end
end
```

Nodes without `press` never send these, so an existing handler sees exactly the clicks it did.
They travel as clicks because the click is the one event ScriptedScreens carries from every
player to the chip. Two presses of the same kind less than a quarter of a second apart arrive
as one: ScriptedScreens drops repeats that close together.

### Shadows

| Key | Meaning |
|-----|---------|
| `sh` | list of shadows, `{ { dx, dy, blur, spread, "#rrggbbaa" [, "inset"] }, ... }` |

CSS `box-shadow` semantics and order: the shape offset by `dx`/`dy`, grown by `spread`,
filled with the colour, blurred with a Gaussian whose sigma is **half** the blur radius,
drawn beneath the shape. Several compose in declaration order. A single shadow may be
written unwrapped.

**Shadows fade with their shape**: the group's `o` and the shape's `fo` multiply into the shadow,
as CSS `opacity` takes a box-shadow with its box. Before 0.11.21 they did not, and a faded card
kept a full-strength halo. The shadow colour's own alpha is separate and unaffected.

```lua
{ op = "R", x = 8, y = 8, w = 60, h = 28, rx = 14, f = "#FFFFFF",
  sh = { { 0, 3, 8, 0, "#0000001f" }, { 0, 3, 1, 0, "#0000000a" } } }
```

Available on **`R`, `C`, a filled closed `Y` or `SP`, a closed `P`, and `T`.** Everything but
text is drawn as geometry — no offscreen pass, no shader — by stacking contours from `-3σ` to
`+3σ` carrying the closed-form coverage of a blurred edge, `0.5·erfc(d / (σ√2))`. Ring count
follows the blur's on-screen size.

A path with holes casts its shadow from the **outer contour only**: shadowing each closed
subpath would put a solid shadow behind every hole, and punching one out needs the polygon
boolean this renderer does not have.

**Text is the exception and works differently.** A glyph has no contour here — the text engine
builds its own mesh above ours — so a `T` shadow is the SDF shader's underlay instead. Same
`sh` syntax, three differences:

- **Several are allowed.** The first outset shadow uses the label's own underlay; every further
  one is drawn by another copy of the label beneath it, the last written lowest as CSS stacks
  them. Each copy is a TMP object, so a label with three shadows costs three labels.
- **`inset` works on text too.** Inside the glyphs (a copy of the label used as a stencil
  mask), the letters are painted in the shadow colour and the face is painted back over them
  moved by the offset, so the shadow shows along the edges facing away from it. Blur softens and
  spread shrinks that moved face, so those two share the padding budget below; the offset is a
  position and does not. One inset shadow per label; a second is reported. Costs three more TMP
  objects and a stencil pass. CSS has no inset text shadow.
  TMP's own inner underlay (`UNDERLAY_INNER`) is not used: the game's text shaders declare it
  but draw nothing with it, checked four ways on a console.
- **Blur and spread are capped by the font's SDF padding.** They share one budget of roughly
  `gradientScale` atlas texels, which for the game's LiberationSans works out to about
  `fontSize / 8.65` canvas units. A request past it is scaled down *as a whole*, so the shadow
  keeps its shape, and the reduction is listed under `TEXT` in `vector_stats`.
- **Offset is not capped.** A shadow with an offset that would not fit is drawn by an offset
  copy of the label instead of inside the label's own quad, so the offset stops counting
  against the budget. A `2 2 3` shadow on 13-point text fits whole this way; inside the quad it
  was shrunk to 43%.
- **A glow is the case that stays capped.** With no offset there is nothing to take out: a
  `0 0 6` glow on 14-point LiberationSans needs about twice the padding the font has and draws
  at 54%. Use a smaller blur, or a font with more padding.
- `spread` maps to dilate and `blur` to softness, both approximations of the geometric
  version rather than the same arithmetic.

**Three differences from CSS**, worth knowing rather than discovering:

- The coverage is measured along each vertex's normal rather than by solving the 2-D
  convolution. Exact on a straight edge, very slightly tight at a sharp corner; under a
  pixel at UI corner radii.
- **The shadow is not knocked out under the shape.** CSS clips it to outside the border box so
  a translucent shape does not darken over its own shadow. That needs a polygon boolean here.
  Opaque shapes are unaffected; a translucent one will read darker than the mockup. Geometry
  only — the text underlay draws strictly behind its glyphs and has no such problem.
- **`inset`** — a sixth field `"inset"` (or `1`) — draws inside the shape, over the fill and
  under the stroke: the shape moved by `dx`/`dy` and shrunk by `spread`, inverted, blurred and
  clipped to the shape, as CSS draws it. Needs a **convex** outline (`R`, `C`, a convex `Y`, `SP`
  or `P`); a concave one is refused with a problem rather than leaking past its edges. Costs
  roughly three times the vertices of an outset shadow of the same blur, since its rings are cut
  to the shape triangle by triangle.

  ```lua
  { op = "R", x = 8, y = 8, w = 120, h = 40, rx = 8, f = "#0B1622",
    sh = { { 0, 2, 6, 0, "#00000099", "inset" } } }
  ```

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
| `spread` | past the ends of the ramp: `pad` (default, hold the end colours), `repeat`, `reflect`, `none` (transparent) |

### `GR` — radial gradient

| Key | Meaning |
|-----|---------|
| `id` | name |
| `cx`, `cy`, `r` | centre and radius |
| `fx`, `fy` | optional focus, default the centre |
| `units` | as above |
| `stops` | as above |
| `spread` | as above |

**`units = "bbox"`** spans the referencing shape's own bounding box, `0..1`. Prefer it: no
coordinates to get wrong, and it tracks a shape that moves or resizes. Without it,
coordinates are in scene units and must be placed over the shape that uses them.

**Geometry and stops can be values.** Anywhere a gradient takes a number — `x1 y1 x2 y2`,
`cx cy r fx fy`, `a`, and a stop's position — an expression works instead, and a stop's
colour may be `$name` from the data payload. They are re-read every rebuild, so a ramp can
follow a level, a threshold or a theme colour without the structure being resent:

```lua
{ op = "GL", id = "fade", units = "bbox", x1 = 0, y1 = 0, x2 = "$edge", y2 = 0,
  stops = { { 0, "#E0A44F" }, { "=clamp($level,0,1)", "$sky" } } }
```

A gradient of plain numbers is resolved once, as before, and costs nothing per rebuild.
Outside its stops a ramp holds its end colour, so a two-stop ramp ending at `0.64` is flat
from there on.

**`spread` repeats the ramp** past its ends, SVG `spreadMethod` and CSS
`repeating-linear-gradient`: `repeat` starts it again at every period, `reflect` runs it back
and forth. A linear one is exact, the seams of `repeat` included, because the shape is cut at
every stop of every period; a radial one draws more rings the more periods it covers. Stripes
cost vertices in proportion to how many fit in the shape. A `mask` takes it too. A conic
gradient already goes all the way round and ignores it.

**`spread = none`** is transparent past the ramp's ends, with a hard edge at each: a sized
gradient that covers only part of the shape, CSS `mask-repeat: no-repeat`. It keeps the end
colour at zero alpha rather than fading to black, so no dark fringe appears at the edge.

Gradients are baked into vertex colours. A two-stop linear gradient is exact; multi-stop and
radial are subdivided automatically, and radial fills as concentric bands so vertices land at
even gradient parameters.

**A radial fill is the most expensive thing in the format**, so it is worth knowing what it
costs. Band count follows the shape's on-screen radius, its stop count, and — since 0.10.3 —
what the screen can actually resolve, which is the one that was missing: a many-stop ramp used
to force its band count with no regard to size, so a six-pixel badge with a thirteen-stop
gradient drew ninety-six bands. Rings nearer the focus also carry fewer outline points now,
because a ring at a tenth of the radius has a tenth of the circumference.

Together those roughly halve a typical fill and cut a small multi-stop one by an order of
magnitude — worth knowing, though no longer a wall. See **Size limits** below.

**Since 0.11.18, `units = "bbox"` radials use the band fill too**, and bands follow the gradient
on any shape. Two faults meant neither was true before: the band fill tested the gradient's
0..1 focus against the shape's real coordinates, so no bounding-box radial ever qualified and
all of them fell back to a subdivided fill that reached 60,000 vertices on one box; and a ring
only carried colour at the outline's own points, which on a rounded rect are all in the
corners, so the bands took the box's shape. Outlines now get a point every 2.5 screen pixels
before rings are built. Measured on a 90x50 box: about 16,000–21,000 vertices at any size,
where the subdivided fill grew from 43,000 to 99,000 with size.

### `GC` — conic gradient

| Key | Meaning |
|-----|---------|
| `id` | name |
| `cx`, `cy` | centre |
| `a` | start angle, degrees **clockwise from twelve o'clock**, as CSS `conic-gradient(from a)` |
| `units` | as above; with `bbox` the centre is a fraction of the shape's box |
| `stops` | as above, positions `0..1` around the turn |

```lua
{ op = "GC", id = "dial", cx = 0.5, cy = 0.5, a = -120, units = "bbox",
  stops = { { 0, "#5FD9A8" }, { 0.66, "#F59E0B" }, { 1, "#E23D3D" } } }
```

The angle is measured in the **shape's own space**, so in `bbox` units a wide box does not bend
the angles. A convex shape is filled as wedges from the centre, one per ~2.5 screen pixels of
rim, aligned so no triangle crosses the start angle — which is what gives the hard edge CSS
draws there when the ends of the ramp differ. A concave shape falls back to subdivision, where
the seam is resolved to the subdivision's depth rather than exactly.

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

### Inherited defaults in the text format

`style` is a map and the text format has no map syntax, so a text-form `G` carries its
defaults as ordinary attributes instead — the same shape as `SYM`'s parameter defaults:

```
G fea=0 f=#c6c6c8 {
    R x=0  y=0 w=40 h=12
    R x=44 y=0 w=40 h=12
}
```

Only **paint** keys are inherited this way: `f fo fea fea_edge fr sh so sw cap join ml dash
dofs` and the text keys `size font weight cspace align valign fit min_size`. A group's own
`t r s a o clip` are not, because it uses those itself and inheriting them would apply every
transform twice.

**Stroke colour is `s_`, not `s`, when written as a group default.** On a shape `s` is the
stroke colour; on a group it is the scale. `s_` says the former without breaking the latter.

Both forms work in the table form too, and an explicit `style` wins over a bare attribute on
the same group.

### `CP` — clip path

| Key | Meaning |
|-----|---------|
| `id` | name, referenced from a group's `clip` |
| `c` | one shape |

Rectangle, rounded rectangle, ellipse, polygon or path — **convex or not**. Outlines are in
**scene coordinates** and stay put when the referencing group is transformed. A path clips to
its outer contour.

**A clip's geometry can be a value.** `CP id=track { R x=20 y=20 w="$w" h=16 }` is re-cut
every rebuild, so a clipped bar, a masked gauge or a list window follows the data payload
with no new structure. A clip that evaluates to nothing — a width of zero — hides what it
clips rather than releasing it, which is what an empty window means. A clip written with
plain numbers is cut once at parse, as before.

**A concave clip costs more than a convex one.** Clipping stays geometric: the outline is split
into convex pieces and every shape under the group is emitted once per piece, so an L-shaped
clip that splits in two doubles the vertices of what it clips. Pieces meet exactly, so there is
no seam and no double coverage. Text under it is masked to the true outline with a stencil,
and hit regions and labels are recorded once.

A clipped fill keeps its holes, including one the clip boundary cuts through. A hole wholly
inside the clip costs nothing extra. One crossing the boundary, or the cut between two pieces
of a concave clip, makes that shape triangulate before clipping, so it draws with unshared
vertices: a few more than usual, only for that shape.

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
| `sy` | scroll offset of the enclosing scroll view or `SC`, in scene units; `0` when there is none |
| `vh` | viewport height of that scroll view or `SC`, in scene units; `0` when there is none |
| `hover` | `1` while the pointer is over a clickable node inside the nearest node with an `id` around this expression (the node itself, or a group), else `0` |
| `down` | the same, while a pointer is held down on it |

**`hover` and `down` style a control from the pointer**, CSS `:hover` and `:active`, with nothing
sent: each client tracks its own pointer, and a scene redraws on a change only if it reads them.
They answer to the nearest node carrying an `id`, so a button written as a group lets its label
answer too:

```
G id=save {
  R click=1 id=save_box x=10 y=10 w=60 h=18 rx=4 f=#2E8B6E fo="=0.8+0.2*hover"
  T x=10 y=13 w=60 h=12 text=SAVE align=center size=8 f=#EAF4F8 fo="=1-0.3*down"
}
```

The label has no `id` of its own, so its scope is the group; a node with its own `id` answers
only for itself. Inside a repeat the instance has to match as well.
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
| `since($name)` | seconds since data `name` last arrived on this client; a name never sent reads as a very long time |
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
| blur and backdrop effects | need an offscreen pass or a custom shader. Drop shadows **are** supported — see `sh` |
| horizontal scrolling | `SC` is vertical only |
| self-intersecting fills | ear clipping is undefined on them; detection costs more than the fill |
| holes inside a clipped fill | needs boolean subtraction |
| expressions in a path's `d` | the command string is parsed once; move or scale the path with its group's `t`/`r`/`s`, or draw it as `YS`/`LS` samples |
