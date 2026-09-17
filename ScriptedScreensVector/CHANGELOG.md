# Changelog

ScriptedScreens Vector, newest first.

## 0.11.27

- Fixed: a screen capture of a scene that fills named data slots could show the previous build's values in the rebuilt page, as stale text, wrong fonts and dark blocks.
- Fixed: data sent just before a new structure during a rebuild went to the outgoing surface and was lost.
- With Diagnostics on, each screen capture writes what it copied to a text file in the temp folder.

## 0.11.26

- `snap = 1` on a data payload applies its numbers at once instead of easing them in; other values keep easing.
- Fixed: a data patch (`keep = 1`) that arrived while an earlier one was still waiting to apply replaced it, losing the earlier patch's values.
- Fixed: a new payload made values still easing jump to their end before easing to the new value; they now continue from where they are on screen.
- Fixed: a linear gradient with more than two stops multiplied a shape's geometry up to a thousandfold; ten rounded boxes took about 231,000 vertices. It is now cut exactly at its stops: about 1,100 vertices, and exact.
- An inset shadow with no blur costs about half the vertices.
- Fixed: screen captures of a scene that had just been rebuilt drew the old and the new contents on top of each other, which showed as doubled or garbled text.
- Fixed: a reused label could show its previous text's glyphs as dark blocks in a screen capture.

## 0.11.25

- **MCP support for AI editors.** With StationeersLua's MCP server, the mod publishes a brief written for AI editors (stationeers://vector/index), the guide and reference split into searchable sections (scope **vector**), the changelog, Patterns.lua and all examples, next to the existing vector_stats tool.
- QUICKSTART.md ships in the mod folder: the same one-page brief, with a working template and a checklist.
- The published DLL no longer carries the build machine's folder paths in its debug information.

## 0.11.24

- Other mods on the same client can follow a scroll container's offset (ScrollChanged, TryGetScroll); with Diagnostics on, each change is logged.
- Fixed: a script-set scroll jump (so, sov) landed short of its target while data was easing, e.g. at 149.1 instead of 150.

## 0.11.23

- Fixed: a path with a hole inside a clipped group drew solid. Clipped fills keep their holes, including one the clip cuts through or one crossing a concave clip's pieces.

## 0.11.22

- Text follows a group's skew or uneven scale (m, s), as CSS transforms do; the viewbox fit still leaves letterforms alone.
- Radial and conic masks follow their gradient smoothly on any shape, with no specks, and a conic mask keeps a hard edge at its start angle.
- Fixed: a click landed anywhere in a node's bounding rectangle -- a circle's corners, a clipped shape's cut-away parts, list rows scrolled out of sight. Clicks now land only on what is drawn.
- Fixed: a label covered by the scene's very first shape stayed on top of it.
- Fixed: a gradient mask over a group holding only text hid the text entirely.
- Fixed: screen captures logged an error per masked label and lost masked text afterwards, and showed images white, text without its gradient or filter colours, and inset text shadows missing.
- Inset shadows, masks and colour filters cost less to rebuild.
- Examples: 05's path hole and layout, 13 and 14 no longer stretched, 15's clipped label.

## 0.11.21

- **Inset shadows** on shapes (sixth sh field "inset", convex shapes) and on text. Text takes several shadows.
- **Conic gradients** (GC), clockwise from a start angle, with a hard edge at the seam.
- **Concave clips.** Any polygon or path can clip; text is masked to rounded and concave outlines with a stencil.
- **IMG**: a picture from a URL, with object-fit, corner radii and opacity, drawn in scene order.
- Groups take CSS **filters** (bri con sat hue gray sep inv), a gradient **mask**, and a CSS **matrix** m.
- Fixed: text applied fo and group opacity twice, so a label at 50% drew at 25%.
- Fixed: shadows ignored group opacity and fo, so a faded shape kept a full-strength shadow.
- Text takes a gradient fill (f=@name) and first-line styling (fl). IMG takes uv, a crop of the picture.
- T align=justified. fat and sat sample a gradient in the text scene format. SC so and sov set the scroll offset once per version.

## 0.10.5

- Fixed: a console that ran out of room once kept its magenta border for ever, because running out was filed with the scene's parse-time faults, which are never cleared. It is a fault of the size you are standing at, so it now clears when it stops being true.
- **Feathered shapes cost a third less, and shadows nearly half**, with no change to how either looks. Both were emitting a contour twice: a feather ring wrote its inner edge at the same positions and colours the fill had just written, and each ring of a blurred shadow rewrote the contour the previous ring had already laid down. They index the shared edge now. Measured on 400 feathered circles: 60 vertices each became 40.

## 0.10.4

- **Running out of room says so.** Going over the ceiling used to drop geometry silently -- something on the console was simply missing, with nothing in the log and nothing on screen to say which part. It now raises a scene problem naming what was refused, so it gets the magenta border and a line in vector_stats like every other fault.
- Fixed: vector_stats reported a nonsense rebuild rate unless Diagnostics was switched on, because it divided by the reporting interval whether or not a report had happened.
- The 60,000-vertex ceiling stays. It is UGUI's own limit, not a setting -- raising it is only possible by splitting a surface across several meshes.

## 0.10.3

- **Radial gradients cost about half the mesh they used to**, and a small many-stop one far less. Band count now respects what the screen can resolve -- a thirteen-stop ramp on a six-pixel shape was drawing ninety-six bands of detail finer than a pixel -- and rings near the centre carry fewer points, since a ring at a tenth of the radius has a tenth of the circumference. This matters because a surface has a hard vertex ceiling that fails by dropping geometry silently.

## 0.10.2

- **Shadows work on every closed shape**, not only rectangles and ellipses. A filled polygon, spline or closed path parsed sh and silently drew nothing; the documentation claimed otherwise and was wrong.
- **Text takes a shadow too**, through the font's own underlay, since a glyph has no outline for the geometric shadow to trace. One shadow per label, and a request larger than the font's SDF padding allows is scaled down as a whole and reported rather than quietly shrunk.

## 0.10.1

- Text nodes take wrap=1 for multi-line paragraphs and lh for line height, as a CSS-style multiple of the font size. Single line stays the default, since a readout that silently becomes two lines shifts everything the scene placed around it.
- Backslash escapes inside quoted values in the text scene format, so a string can contain the quote that delimits it.

## 0.10.0

- **A whole list is one node.** Text and colours can be bound to an array slot -- text="$rows[i]" and f="$tints[i]" inside a repeat -- so thirty rows are one node and one array instead of thirty nodes. A clickable node in a repeat now reports which instance was hit.
- **Text can format a number itself**, with fmt="%.1f" and unit=" kPa", so the chip sends the number it already had and does no string work at all.
- **keep=1 on the data element** makes a payload a patch: names it does not mention hold their values. Without it every string on screen had to be resent every tick or it vanished.
- Groups in the text scene format can carry paint defaults directly, since that format has no map syntax for style.
- vector_stats reports the addon version on its first line.
- Fixed: a label with no font of its own could inherit the face of whichever label used that pooled object before it. Fixed: string and colour arrays were parsed out of the payload and then dropped before the renderer saw them.

## 0.9.1

- Text now fades with its group. A T node takes fo, and the enclosing group's o reaches it -- so a row's text fades at the edge of a scroll container along with the artwork around it, which it previously did not.
- Corrected the documentation's instruction-budget claims after a real console was ported and measured. Writing a scene as text is free when it is a literal and costs about what tables cost when it is generated; it is worth choosing for clipping, scrolling, click regions and live text, not for the budget.

## 0.9.0

- **Scroll containers.** SC is a box that clips to itself and slides its children inside it: wheel over it or drag it and a list longer than the console moves. The scroll position stays on the client, so a wheel notch costs one mesh rebuild and nothing else -- no tick, no network message, and none of the chip's instruction budget.
- Inside a container, the sy and vh expression variables report that container, so a pinned header, an edge fade or a scrollbar thumb is written exactly as it is for a scroll view.

## 0.8.0

- **Scenes report their own faults.** An unknown op or attribute, a malformed expression, a data name with no value, a missing gradient or clip — each is reported once with the node it came from, and the surface shows a magenta border rather than looking switched off. A colour bound to a name the payload never supplied now draws magenta instead of white, so an unresolved binding looks like a fault rather than a design decision.
- **vector_stats**, an MCP tool for StationeersLua: every live surface's node and shape counts, rebuild rate, tessellation and upload cost, on-screen size, and any problems or unresolved names. Bound by reflection, so nothing changes if StationeersLua is not installed.

## 0.7.0

- **Clickable nodes.** A node marked click=1 becomes a hit region, and the click arrives at the vector element's own on_click with the node id as its value. A list of tappable rows no longer needs a transparent button element over every one of them.

## 0.6.0

- **Symbols.** Declare a subtree once with SYM and stamp it with USE, passing parameters — a row, an LED, a duct segment written once and placed twenty times. Substitution happens at parse time, so an instance costs exactly what writing the nodes out would have.
- **Inherited defaults.** A style map on a group or the scene root supplies defaults for everything under it that does not set the key itself.
- **Per-corner radii** on rectangles: rx = { tl, tr, br, bl }, CSS order, and a zero corner is a sharp point.

## 0.5.0

- New **T** node: text inside the scene, drawn with TextMeshPro so registered fonts and rich text work. It moves, scales and scrolls with its group like any other node, updates from the data payload with text = "$name", and supports ellipsis and shrink-to-fit. Text was previously label elements laid over the scene, which cost about 300 Lua instructions each, had to be re-declared to change a word, and could not move with the artwork.
- Strings are now data values, so a data payload can carry text as well as numbers, arrays and colours.
- Known limit: text clips to an axis-aligned rectangle only. A rounded or rotated clip does not cut it until stencil clipping lands.

## 0.4.1

- Fixed: a fill written as **f = "$name"** never worked. The colour was parsed out of the data payload correctly and then dropped before it reached the renderer, so every data-bound fill fell back to white. Colours now arrive like numbers and arrays do.
- New **nodes** prop on the data element: patch any node that carries an id, without resending the scene — nodes = { hv_bar = { w = 42, f = "#E23D3D" } }.

## 0.4.0

- A scene can now be written as one string in a **src** prop instead of nested tables — one node per line, braces for children. A static layout written this way is a literal, already in the compiled script, so it costs nothing to build; a generated one costs about what the tables cost. Same ops, keys and expressions as the table form, and it goes through the same parser so the two cannot drift.

## 0.3.0

- New **sh** attribute: CSS box-shadow, several composing in order — { dx, dy, blur, spread, colour }. Drawn as real geometry from the closed form for a blurred edge, so it needs no offscreen pass and no shader. This is the thing feathering could not do: feather ramps outward from a solid edge while a blur softens both sides of it, which is why a feathered knob reads as a ring instead of a shadow.
- New expression variables **sy** and **vh** — the scroll offset and viewport height of an enclosing scroll view, in scene units. A vector element inside a scroll view moves with the content; adding sy cancels that out, so a header, fade or rule can stay pinned to the viewport while the content scrolls underneath. Both read 0 when there is no scroll view, so the same scene works either way, and a scene that uses them redraws as you scroll even if it never mentions time.

## 0.2.1

- The eight worked examples, the authoring guide and the full reference now install with the mod, in the mod's own folder. Previously only the DLL was copied, so there was nothing to read or paste.
- Fixed: two consoles running the same script fought over one scene registration. Every console names its surface "main", so both hashed to the same key and one console's data reached the other's artwork — seen as clipping breaking on whichever console was started second, and swapping over on restart. Scenes are now keyed per board.

## 0.2.0

- Tessellation moved to a worker thread. A console with 800 animated shapes at 30 Hz now costs about 0.4% of a frame on the main thread — switching one on is no longer measurably different from switching it off.
- New **fo2** on sampled bands: an opacity ramp along each column, from the sampled edge to the far one. This is how you fade from a surface that moves; a gradient cannot, because it is anchored to the bounding box rather than the wave.
- Gradients gained **units = "bbox"** — a ramp that spans whatever shape references it, and follows that shape when it moves or resizes.
- Curve level of detail: sampled bands and lines are walked with a longer step when drawn small. Nothing is dropped, the same curve is simply traced with fewer segments.
- Circles now tessellate by on-screen radius instead of a flat 32 segments, so particles are cheap and large dials are smooth.
- Shapes that resolve to zero opacity are skipped rather than tessellated, which makes opacity usable as a visibility switch.
- Clipping no longer allocates per boundary edge, and gained bounds fast paths. Much faster on clipped scenes.
- Feathering survives a clip and can ramp one edge only (**fea_edge**).
- Clip paths are interpreted in scene coordinates, so a clip keeps clipping the same area however the groups under it are transformed.
- A scene that draws nothing now shows a magenta hatched border instead of an empty console, with the reason in the log.
