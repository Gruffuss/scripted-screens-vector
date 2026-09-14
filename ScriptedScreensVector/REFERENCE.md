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
| `keep` | number | `1` makes the payload a patch: names it omits keep their values |

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

```
vector_stats            -- all surfaces
vector_stats scene=gas  -- one
```

The first line is the addon's version, so "is the build I just made the one that is running"
is answerable from inside the editor.

Bound by reflection, so the mod loads normally without StationeersLua and simply does not
register the tool.

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
| the same rectangle with one `sh` | 640 |
| a small circle with a feather | 40 |

A box-shadow is far and away the most expensive thing a scene can ask for — roughly nine
cards' worth each — because its ring count follows the blur's on-screen size. Reducing the
blur radius is the direct lever.

### Screen capture

`capture_scripted_screen` works on vector consoles, artwork and text alike. It is not free:
the capture rebuilds the surface and renders a clone inside one call, so the geometry is
tessellated **inline on the main thread** rather than on a worker. A capture of a dense
console costs a frame; ordinary rendering is unaffected.

### Text in draw order — `ztext`

Set `ztext = 1` on the **structure** element's props (or `SCENE ztext=1` in the text form).

Labels are TextMeshPro objects parented beside the geometry rather than part of the mesh, and
UGUI draws children in sibling order. So by default the whole text layer draws after the whole
surface: **a shape declared after a label cannot cover it**, and that applies to every label at
once. `ztext = 1` makes labels obey scene order like everything else.

Opt-in, because text-on-top is what scenes written before it rely on. Turning it on changes
only the cases where a shape genuinely overlaps a label.

**What it costs.** A label that has to sit under later geometry forces the mesh to be cut
there, and each extra mesh is a draw call. A cut is forced *only* where a later shape actually
overlaps the label's box, which on a normal page is close to never — tiles do not overlap
their neighbours and a label sits inside its own tile. Measured: thirty labelled tiles stay in
one mesh. A page that really does slide panels over text pays one draw call per such label.

**One case it cannot serve:** a label declared before any shape. The surface's own renderer
draws before its children, so there is nothing to put such a label behind; it stays on top
rather than disappearing.

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

### `SC` — scroll container

| Key | Type | Meaning |
|-----|------|---------|
| `id` | string | **required** — the scroll position is stored under it |
| `x`, `y`, `w`, `h` | number/expr | the viewport box, in the enclosing coordinates |
| `ch` | number/expr | total content height; at or below `h` nothing scrolls |
| `rx` / `ry` | number/expr | corner radii, same rules and per-corner form as `R` |
| `o` | number/expr | container opacity, multiplied into all descendants |
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

**`sy` and `vh` report *this* container inside it.** `sy` is the scroll offset, **zero at
rest**, and `vh` is the container's height -- so pinned artwork is written at the container's
own `y` plus `sy`:

```lua
-- container at y = 20, height 120
{ op = "R", x = 4, y = "=20+sy",         w = 192, h = 18, f = "#0B1622" }  -- pinned header
{ op = "R", x = 4, y = "=20+sy+vh-12",   w = 192, h = 12, f = "@fade" }    -- bottom fade
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
- **A text node inside is clipped by the container's box**, which is the case RectMask2D
  handles. Rounded corners cut geometry but not text; see `T`.

### `T` — text

| Key | Meaning |
|-----|---------|
| `x`, `y`, `w`, `h` | the box the text is laid out in |
| `text` | a literal, `"$name"`, or `"$rows[i]"` for one slot of a data array |
| `fmt` | printf spec for a bound **number**, e.g. `"%.1f"` |
| `unit` | literal suffix appended after the text |
| `missing` | what to draw when the name has no value; default `"--"` |
| `size` | font size in scene units; scales with the transform |
| `f` | colour, as on any shape |
| `fo` | opacity `0..1`, as on any shape; multiplied by the enclosing group's `o` |
| `align` | `left` (default), `center`, `right` |
| `valign` | `top` (default), `middle`, `bottom` |
| `font` | a registered TMP family, e.g. from the companion fonts mod |
| `weight` | `bold`, or a number ≥ 600 |
| `cspace` | character spacing |
| `wrap` | `1` lets the text run to more than one line inside its box |
| `lh` | line height as a multiple of the font size, CSS style; omitted uses the font's own |
| `sh` | one drop shadow, drawn by the font's underlay — see Shadows |
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

**Text is not part of the mesh, and that shapes what it can do.** TMP builds its own geometry
on its own GameObject and is main-thread only, while tessellation runs on a worker — so the
walk records where each `T` landed and the labels are created and updated when the job lands.

| | |
|---|---|
| transform, scale, scroll with the group | **yes** — the rect goes through the frame matrix, rotation included |
| fade with the group | **yes** — `o` and `fo` reach the label as its own alpha |
| updates from `data` | **yes** — strings are data values now, so `text = "$name"` works like a number |
| rich text, registered fonts | **yes** — it is real TMP |
| clipping | **axis-aligned rect only**, via `RectMask2D`. A rounded or rotated clip does not cut text until stencil clipping lands |
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

**A clickable node inside a repeat reports its index.** One node stands for n rows, so the id
alone cannot say which was hit; the value becomes `id:i`:

```lua
on_click = function(nodeId, player)
    local id, index = nodeId:match("^(.-):(%d+)$")
    ...
end
```

Outside a repeat it is the bare id, unchanged.

**Hit testing is against the node's bounding box**, in draw order, last match wins. For a row,
a tile or a button — what carries `click` — the bounds are the shape. A thin diagonal or a
ring will claim more than it draws.

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

- **One shadow only.** The underlay is a single layer. A second is reported, not drawn.
- **Size is capped by the font's SDF padding.** Offset, spread and blur share one budget of
  roughly `gradientScale` atlas texels. A request past it is scaled down *as a whole*, so the
  shadow keeps its shape, and the reduction is logged with its percentage rather than left to
  look like the numbers were ignored.
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

**A radial fill is the most expensive thing in the format**, so it is worth knowing what it
costs. Band count follows the shape's on-screen radius, its stop count, and — since 0.10.3 —
what the screen can actually resolve, which is the one that was missing: a many-stop ramp used
to force its band count with no regard to size, so a six-pixel badge with a thirteen-stop
gradient drew ninety-six bands. Rings nearer the focus also carry fewer outline points now,
because a ring at a tenth of the radius has a tenth of the circumference.

Together those roughly halve a typical fill and cut a small multi-stop one by an order of
magnitude — worth knowing, though no longer a wall. See **Size limits** below.

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
| `sy` | scroll offset of the enclosing scroll view or `SC`, in scene units; `0` when there is none |
| `vh` | viewport height of that scroll view or `SC`, in scene units; `0` when there is none |

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
| blur and backdrop effects | need an offscreen pass or a custom shader. Drop shadows **are** supported — see `sh` |
| text in a gradient, or clipped to a rounded shape | `T` is a real TMP child, so its fill is flat and its clip is an axis-aligned rect |
| horizontal scrolling | `SC` is vertical only |
| non-convex clipping | needs a stencil buffer; convex covers every layout so far |
| self-intersecting fills | ear clipping is undefined on them; detection costs more than the fill |
| holes inside a clipped fill | needs boolean subtraction |
| expressions in `d` or in gradient coordinates | both are static; use `units = "bbox"` |
