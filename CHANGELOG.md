# Changelog

ScriptedScreens Vector, newest first.

## 0.11.58

- Fixed: **a soft glow seen from a distance rendered as a four-pointed star.** The exact-coverage path switched off below three screen pixels of sigma, and without it a rectangle's contour has vertices only at its four corners; the rings are mitred, so each one pushes a spike out along the diagonal by sqrt(2) times its offset. The threshold is now half a pixel, by which point the blur is sub-pixel and nothing is visible either way. A distant console is a few dozen pixels across, so the exact path costs a few hundred vertices there.
- Added: `Renderer.BlurLod`, on by default. Off builds every blur at full detail regardless of how large it is on screen. It costs vertices on distant consoles and buys nothing the eye can use, but it tells a distance artefact apart from a geometry one without moving the camera -- which is how long the star above took to find.
- Changed: a blur's corner runs never drop below 8 points (`MinCornerSamples`). The pixel-driven count bottomed out at 2, which cannot round a mitred corner at any size.

## 0.11.57

- Reverted 0.11.56. Cutting coverage below half an output level made glows visibly worse on a console -- it ends a glow on a hard edge rather than letting it fade, which is far more noticeable than the faint lift it was meant to remove. 0.11.55's behaviour is restored exactly. The reasoning in 0.11.56 was sound in isolation and wrong in practice: half a level is only invisible against a flat field, and the eye finds the resulting boundary immediately.

## 0.11.56 (withdrawn)

- Fixed: **the very end of a glow's tail showed a faint speckle instead of fading out.** Where coverage falls below half an output level the pixel would round to the background anyway, so dithering there recovers no detail -- it turns "invisible" into sparse single-level noise on a flat dark field, which is one of the easiest things for an eye to find. Coverage below half a level is now cut before the dither. The step this introduces is half a level, which is by definition below what the output can show. Reported from a console, confirmed by probing a screenshot: the far corner of a drawn area sat 0.15 of a level above the page background, about one pixel in seven.

## 0.11.55

- Fixed: **a glow's faint tail never reached black, settling in a faint haze instead.** The dither was added and then clamped with `saturate`, which clips the negative half of the noise while the positive half survives, so near zero the mean was lifted by about half the amplitude -- at 4/255 that is a permanent 2/255 of pedestal over the whole reach of every blur. The amplitude is now tapered to twice the alpha, so the noise stays symmetric where there is room for it and falls silent where there is not. Introduced in 0.11.49 and visible on a console as soon as 0.11.54 removed the banding that had been hiding it.

## 0.11.54

- Added: the log now records the render target's precision at startup -- colour space, whether the main camera is HDR, and what it draws into. Guessed at twice during the banding work and wrong both times; in linear space the right dither amplitude is not the one gamma wants, and against an HDR target there is no eight-bit step to dither at all.
- Added: **a blur's coverage rides in a UV rather than in vertex alpha**, and the shader multiplies it in. UVs interpolate as float32; the canvas's own vertex stream carries colour as `Color32`. Measured against the exact answer, on its own this is worth nothing -- the widest flat step in a wide glow's tail is 38 px with an 8-bit ramp and 37 px with a float one. It only pays with an output that has headroom: 8-bit ramp into a 16-bit output is 32 px, float ramp into a 16-bit output is **1 px**. Either quantiser alone produces the banding, so removing one can never show a difference on its own, which is why every single-variable test read as "no change". This half is ours. Falls back to the old behaviour when the shader is not loaded, since the stock UI material would ignore the stream and draw a blur at full strength.

## 0.11.52

- Changed: `Renderer.Dither` now defaults to 4/255 rather than 1. 1 was a guess, and measured against the exact answer it is about four times too weak where it matters: on a black page the widest flat step in a wide glow's tail is 38 px undithered, 12 px at 1/255 and 2 px at 4/255.

## 0.11.51

- Fixed: **a group's per-column opacity ramp snapped every vertex to fully transparent or fully opaque.** `Fade` still rounded alpha to an integer and cast it to a byte, left over from when it took a `Color32`; on a float colour, alpha is 0..1, so the only results it could produce were 0 and 1. Found while auditing the mesh path for 8-bit rounding.
- Changed: `Renderer.Dither` now applies on every rebuild rather than only when an element is enabled, so it can be tuned from the settings UI without a restart. It was documented as live in 0.11.49 and was not.
- Added: `Renderer.BlurDensity`, a multiplier on the number of contours and corner points a blur is built from. 1 is normal. It exists to test whether banding left in a wide glow's faint tail is contour spacing; raising it costs vertices in proportion.

## 0.11.50

- Fixed: **soft glows were faceted at the corners and stepped in the tail when seen up close.** Mesh density for a blur follows real screen pixels, so walking up to a console already asked for more rings and more points around each corner -- but both were capped at numbers tuned while 8-bit banding hid everything underneath them. Standing at a console, a glow's tail is a few hundred pixels wide, so 24 rings put a band every ten pixels and a corner was a twelve-segment polygon. Raised to 96 rings and 32 points per corner. A scene seen from across the room is unchanged: it never reaches those numbers, so this costs nothing at a distance.

## 0.11.49

- Fixed: **wide soft glows and slow gradients showed bands tens of pixels wide.** Alpha is quantised twice on the way to the screen -- UGUI packs vertex colour to `Color32`, and the framebuffer is 8-bit as well -- and at full glow contrast the two steps are the same size. A Gaussian tail is flat, so it crosses only about five levels over several hundred screen pixels, and each level is a band. Vector geometry is now drawn with its own shader: Unity's `UI/Default` with a per-pixel dither on the output alpha, which scatters the rounding into a grain the eye averages away. New setting `Renderer.Dither` (default 1, in 255ths) tunes it; 0 is the old behaviour. The shader ships as `vectorshaders.bundle` beside the mod; without it the mod draws with the stock UI material and bands as before, and says so in the log.

## 0.11.48 (withdrawn)

- Dithered vertex alpha instead. It made glows visibly worse -- vertices sit tens of pixels apart, so the offset moved whole interpolated ramps rather than scattering the pixels inside them, and band edges turned blocky. Reverted; the reason is recorded in `Shadow.cs` so it is not tried again.

## 0.11.47 (no change)

- Raised the shadow ring limit to 160 to test whether ring density was behind the banding. It was not: 24, 64, 96, 128 and 160 rings are indistinguishable. Back to 24.

## 0.11.46

- Fixed: **every soft fade was quantised to about thirteen shades.** Vertex colours were stored as 8-bit, which is what UGUI normally uses, and in the faint tail of a shadow or `blur` that leaves almost nothing to work with: over a dark page one step of 1/255 in alpha lands roughly nine levels apart on screen, so a smooth ramp could only show 0, 9, 16, 22, 26, 30 and so on. Measured from a console: 0, 12, 19, 24, 28, 32 -- the same ladder. The mesh is built here rather than by UGUI, so its colours are floats now. **Correction (0.11.49): this changed nothing on screen.** The canvas re-packs any mesh handed to `CanvasRenderer.SetMesh` into its own `Color32` vertex buffer, so the floats were rounded again immediately, and the framebuffer rounds a second time regardless. The measurement quoted here was taken offline, where neither step exists. The float colours are kept -- they cost nothing and the dither in 0.11.49 has slightly more to work with -- but the banding fix is the shader, not this.
- This is what made a large glow look stepped on a dark background while the game's own gradients looked smooth, and why a light background or a small blur hid it. It is not the same thing as the dark-colour limit in REFERENCE, which is about flat colours and remains.

## 0.11.45

- Fixed: a blur wider than the shape it blurs drew a **flat bright rectangle** in the middle of the glow. Everything inside the innermost ring was filled with one alpha, which is right only while the blur is small against the shape: blur a 30x24 box by 9 and the true coverage at its centre is 0.74, where it drew 0.89. The core now carries the same exact coverage the rings do, vertex by vertex. Reported from a console as "a bright square in the middle".
- The stepping that shows only against a dark background is the two faults together: the geometry's own error, and the small number of levels the dark end of the range has to land on. The same glow over a light background was clean throughout, which is what separated them.

## 0.11.44

- Fixed: a soft shadow or `blur` seen from close up broke into large polygonal facets -- reported as stair-stepping down the side of a glow, on a console standing 3000 pixels tall. The points that shape a blurred corner were placed at fixed fractions of the blur (0.5, 1.1, 1.9 and 3 sigma), which is scene units; on that screen they sat 40 to 90 pixels apart, and the eye measures pixels. They now follow the on-screen size, one about every ten pixels up to a limit.
- That also makes an ordinary console **cheaper**: a shadow at a normal viewing distance went from 280 vertices to 200, because the old fixed count was more than a small shadow needed. A shadow you are standing at costs about three times what it did, which is where the detail is wanted.
- Worst error against the exact Gaussian improves to 0.025 (6 of 255).

## 0.11.43

- Reverted the `fit = "ellipsis"` growth again, and this time for good. 0.11.40 measured the line through the text engine and 0.11.42 took it from the font's own metrics, measuring nothing; both drew every such label a whole row away from its box, identically, so the fault is in re-placing the label's rect rather than in either measurement. A box shorter than one line shows nothing, as it did in 0.11.39 and before; REFERENCE now says to give an `ellipsis` label about 1.4 times its `size` in height.
- A blurred corner is smoother again: four sample points around each corner instead of two, which took the worst error against the exact Gaussian from 0.044 to 0.027 (7 of 255) at about 1.4 times the vertices. Reported from a console as "kinda jagged" after 0.11.42, which had fixed the corner's brightness but left its shape coarse.
- Denser rings were tried for the same complaint and measured as no help at all -- the biggest jump between neighbouring pixels sits where the Gaussian is genuinely steepest, at the shape's own edge, and the true image steps by as much there. The ring spacing is unchanged and the note is in the code so it is not tried a third time.
- Confirmed on a console: the blurred corners of 0.11.42, and a scene that will not parse drawing its magenta border and naming the line in `vector_stats` while a good scene beside it still draws.

## 0.11.42

- Fixed: **every box shadow and group blur was wrong at its corners.** A shadow is built as contours whose alpha comes from their distance to the shape, which is right along a straight edge -- the blur there is a one-dimensional problem -- and wrong at a corner, where two edges act at once and the true coverage is their product. Measured against the exact Gaussian: 0.512 drawn at a corner where 0.250 was due, a quarter of full brightness too much, seen on a console as a bright four-pointed star. A rectangle now carries the exact product at every contour vertex, with extra points near each corner so the profile has somewhere to live. Worst error anywhere on the shape is 0.044, from 0.262. It costs about five times the vertices of the old corners, only while the blur is at least three screen pixels; round and other shapes are unchanged.
- Fixed: a scene that would not parse drew **nothing**, with no marker on the console and no entry in `vector_stats` -- only a log line, which is the last place an author looks. It now draws the same magenta border every other scene problem does, and the tool names the reason.
- Parse errors say **where**: the line number and the line's text, plus a note that a value holding spaces, `=` or `;` (a `data:` URL, say) must be quoted. "expected an op" on its own sent a session hunting through a whole page.
- `fit = "ellipsis"` again gives a label the one line it needs when its box is shorter than that, so a title in a slightly short box is cut rather than vanishing. 0.11.40's attempt measured the text through TMP and moved labels a row out of place; the line height now comes from the font's own metrics, which touches nothing.

## 0.11.41

- Reverted 0.11.40's `fit = "ellipsis"` change. Growing a too-short label's box to one line drew labels a whole row away from where they belonged, which is worse than the bug it fixed, so `ellipsis` behaves as it did in 0.11.39: a box shorter than one line shows nothing. The cause is under investigation and the next attempt will be seen on a console before it ships.
- Fixed: `drag = 1` and `drop = 1` never started a drag in ordinary play. A console's pointer is the crosshair, fixed at the centre of the view, so it never moves across the screen and Unity's drag threshold is never crossed -- only detaching the mouse with Alt gave a real pointer movement, and a scene's drag looked dead without it. Holding on the node and turning until the crosshair leaves it now starts the drag, and releasing over a `drop` target reports it as before. The free-cursor path is unchanged, and a drag is still reported once whichever way it began.

## 0.11.40

- Fixed: a label with `fit = "ellipsis"` drew **nothing** when its box was even slightly shorter than one line -- a 26-unit title in a 31-unit box vanished instead of being cut short. `ellipsis` truncates vertically as well, and a box that cannot hold one line left nothing to show. The label now gets the one line it needs, growing away from the edge its `valign` pins, and the text is cut horizontally as asked.
- Fixed: `vector_stats` reported a rebuild rate a fraction of the truth (0.1/s for a scene rebuilding twice a second) whenever Diagnostics was on. The five-second log reset the counting window with its own stopwatch while the rate was worked out from the game clock, so the window was the whole session.
- Fixed: the five-second log called a scene `idle (off screen, paused, or static)` when its only motion was a group fading over `t`. That is not idle and not static: the renderer fades it every frame without a rebuild, and the line now says so.
- `vector_stats` says the on-screen size is measured at the last rebuild, and that an unknown size means nothing has been drawn in view yet -- a console behind the camera cannot be measured, which is the normal case when the tool is read from an editor.

## 0.11.39

- Fixed: two stops at the same offset -- a hard stop, which is what every CSS stripe is made of -- drew as a smooth ramp from the cut to the next stop instead of a step. The band either side of the cut was coloured from the stop position itself, which reads as the colour *before* it. Nudging one stop by a thousandth was the workaround and is no longer needed.
- Fixed: a capture drew a group faded by its `o` at full opacity -- a pulsing dot came out solid however faint it was on screen. A renderer fade is live state that the clone ScriptedScreens photographs does not carry, so a capture now multiplies the group's `o` into its colours instead.
- Fixed: the diagnostics said `animated False` about a scene whose only motion is a group fading over `t`. That scene does animate; what it does not do is redraw, because its renderer applies the fade. The line now says so and counts the fading groups.
- Fixed: a label with its own `missing = "..."` text was still listed under unresolved data names, so a console showing exactly what its author asked for reported two problems. A declared fallback means absence is expected. The heading also no longer says every unresolved name draws magenta -- that is colours; a label draws its `missing` text, or `--`.
- Fixed: `hover`, `down` and the `hoverev` events only noticed a change when the pointer itself moved. Turning the view moves the console under a still crosshair, so a card stayed lit and `enter`/`exit` did not arrive until the mouse was nudged. What is under the pointer is re-tested every frame now, for as long as the pointer is over the surface.

## 0.11.38

- Fixed: `r` on a group turned counter-clockwise, although it is documented as degrees clockwise and CSS `rotate()` (and `m` here) turn clockwise. **Changed:** every `r` now turns the other way. A scene written to the docs -- a gauge needle sweeping from `-120` to `+120` -- now sweeps left to right as intended; one tuned by eye to the old direction needs its angle negated.
- Fixed: a single `rx` was limited to half the box's width only, so on a wide, short box the corners overran each other and the outline crossed itself. Radii now shrink by CSS's rule: one `rx` to half the shorter side, per-corner radii all by one factor where two along a side exceed it. **Changed:** a per-corner radius may now reach the full side where its neighbours leave room, as in CSS; it was cut to half the shorter side.
- Fixed: capturing a console restarted its animations: the rebuilt surface started its clock, and every `since($name)`, from zero. They carry on now.
- Fixed: turning the view off a held `press = 1` node sent no `leave:id`; only the pointer moving did. A held press is now re-tested every frame.
- Fixed: a capture of a distant console came out blurred, with coarse curves and shadow rings, because it was built for the console's small size on screen rather than for the capture's resolution.

## 0.11.37

- `xy = 1` on a clickable node reports where it was hit: the value becomes `id@fx,fy`, fractions of the node's box from its top-left, in its own space. For sliders, colour pickers and maps.
- `hoverev = 1` sends `enter:id` and `exit:id` as the pointer moves onto and off a node, once per change.
- `drag = 1` and `drop = 1`: `dragstart:id`, then `drop:src>dst` when released over a drop node, then `dragend:src`. A drag that starts on a drag node no longer scrolls the list it is in.
- `blur` on `G`: CSS `filter: blur()` for flat fills, each drawn as its exact Gaussian-blurred silhouette. Strokes, gradients, pictures and text under it stay sharp.
- `IMG` reads `data:` URLs (base64 or percent-encoded), uncompressed BMP, and the first frame of a GIF, as well as PNG and JPEG. The format is read from the bytes.
- Fixed: `down`, `up`, `leave` (and the new events) two of a kind within a quarter of a second arrived as one; each now arrives. A plain click is unchanged.
- Fixed: an outer box shadow (`sh`) on a shape with sharp corners -- any `R` without round corners, a triangle -- came out about 1.4 times too sharp along its whole edge, because its rings were moved along the corner bisectors without mitring. **Changed:** such shadows are now as soft as CSS draws them, which is visibly softer. Round shapes and inset shadows are unchanged.
- Fixed: every rebuild allocated a new traversal stack. A still label now costs nothing per rebuild.

## 0.11.36

- Fixed: several box shadows (`sh`) stacked in reverse, the last one on top. They now stack as CSS does and as text shadows here always did, the first on top. A scene that listed its shadows to suit the old order shows them the other way round.
- `since($name)` in expressions: seconds since that data name last arrived, so motion started by an event -- a jump, a flash, a slide-in -- is one payload and the scene draws the rest.
- Text placeholders may be expressions: `{=expr}` or `{=expr:%.1f}`, so a clock or a counter needs no payloads.
- `hover` and `down` in expressions: 1 while the pointer is over, or held on, a clickable node in the nearest node with an `id` around the expression, for CSS-style `:hover` and `:active` with nothing sent.
- Colours sent as data glide when their name has an `ease` entry, as numbers do. Without one they still change at once.
- `press = 1` on a node reports holding as well as clicking: the element's `on_click` also receives `down:id` when the pointer goes down on it, `up:id` when it comes up wherever it is, and `leave:id` when a held pointer moves off it. For press-and-hold buttons. Nodes without it are unchanged.
- A data colour may be `transparent` or `none`. They used to be stored as text only, so a `f = "$name"` sent either was reported unresolved and drawn magenta.
- Fixed: an element carrying only data kept ScriptedScreens' default grey background, which showed as a grey square wherever it was placed on screen.

## 0.11.35

- A group whose only animation is its own `o` over `t` -- a blinking status dot -- is drawn once and faded by its renderer every frame, so it costs no redraws at all. It takes one extra draw call per such group. Groups that move, hold text, sit inside a repeat, or whose `o` reads data redraw as before.
- A gradient def that reads `sy` or `vh` follows the scroll container where it is used, in fills and in a `mask`, so one fade def works in every list. Scrolling the host scroll view also redraws it now.
- `spread = "none"` on `GL` and `GR`: transparent past the ramp's ends with a hard edge, CSS `mask-repeat: no-repeat` on a sized gradient.
- `IMG` tiling: `tile` with one `0` keeps the picture's aspect (CSS `background-size: 50px auto`), `tile = "contain"` or `"cover"` sizes the tile to the box, and `rep` sets each axis to `repeat`, `once`, `round` or `space` -- CSS `background-repeat`, including `repeat-x` and a single picture at an explicit size.
- Nine-slice edges can tile instead of stretching: `srep` = `repeat`, `round`, `space` or `stretch`, per axis -- CSS `border-image-repeat`.
- Changed: `tile` with one `0` used to mean that axis's natural size; it now keeps the aspect, as CSS does. `0` on both axes is still the natural size.
- Fixed: a scene with a blink or pulse inside a hidden group (`v = 0`, `o = 0`, or hidden by data) redrew every frame although nothing moving was on screen. It now redraws for `t` only while something that reads it is shown.
- Fixed: a scene whose only animation was in `fo2`, a `$name[...]` index, text `size` or `min_size`, per-corner radii, a scroll container's `ch` or forced scroll, or a `{$name[...]}` placeholder index was treated as still and froze on its first frame.
- Fixed: a two-stop linear `mask` over a shape reaching past the ends of its ramp faded evenly from one end of the shape to the other, instead of holding its end values outside the ramp. A list longer than its fade was faded all the way down.

## 0.11.34

- `off = { ox, oy }` on `IMG` moves the picture by scene units after `at` has placed it, so CSS `object-position: right 10px` is `at = { 1, 0.5 }, off = { -10, 0 }`. It applies under every `fit`, `fill` included.
- `fit = "none"` draws a picture at one scene unit per texel, and `fit = "scale-down"` does that unless the picture is larger than the box, when it contains it — CSS `object-fit`.
- `tile = { tw, th }` on `IMG` repeats the picture across its box at that size, starting from where `at` and `off` place one copy — CSS `background-repeat`. `0` is the natural size; a `uv` crop repeats as the cropped part.
- `smp = "point"` on `IMG` draws hard-edged pixels, CSS `image-rendering: pixelated`. The same picture drawn smooth elsewhere is unaffected.
- `spread = "repeat"` or `"reflect"` on `GL` and `GR` repeats the ramp past its ends — SVG `spreadMethod`, CSS `repeating-linear-gradient` — in fills, strokes and masks. Linear ones are exact, hard seams included. The default `pad` is unchanged.
- `ow` and `oc` on `T` outline the text, centred on each letter's edge — CSS `-webkit-text-stroke`. An outline wider than the font's atlas can hold is capped, and the surface's text warnings say so.
- `slice = { t, r, b, l }` on `IMG` draws a nine-slice frame — CSS `border-image` — with corners `bw` scene units wide, edges and middle stretched. `mid = 0` leaves the middle out.
- A picture that does not cover its box now draws only where it is, under any `fit`. Only `contain` did before; a picture moved off its box would have stretched its edge pixels across the gap.
- A label may carry several values, each with its own format: `text = "set {$press:%.1f} kPa · trip {$trip:%.0f}"`. The chip sends the two numbers and the label is built here, so changing either one no longer means building the whole string in Lua. A value that is missing shows `missing` in its place and the rest of the label still shows.
- An empty string in a data payload is a value: `text = "$note"` with `note = ""` clears the label. It used to be skipped, so the label went on showing whatever it showed before.
- Fixed: every redraw re-applied every text label from scratch -- text, box, rotation and a dozen TextMeshPro settings -- even when nothing about the label had changed. Measured in game at about 100-150 bytes a label on every frame of an animating console. A label identical to last time is now left alone, and the text-ordering pass no longer re-sets a label's place in the hierarchy when it has not moved.
- Fixed: an `IMG` whose `at` depended on the scroll position did not follow the scroll.
- Fixed: a label bound to one slot of a number array, `text = "$rows[i]"`, was reported as missing data every rebuild although it drew correctly.
- Fixed: under `keep = 1`, a value sent as a number after it had been sent as a string (or the other way round) kept its old kind alongside the new one, and a label bound to it went on showing the string.

## 0.11.33

- `ease` on a data payload gives a value its own glide time and curve: `ease = { bar = { 0.6, "ease-out" } }` settles that bar in 0.6 seconds whatever the tick rate, instead of gliding straight across the gap to the next payload. Curves are CSS's — `linear`, `ease`, `ease-in`, `ease-out`, `ease-in-out`, `cubic-bezier(a,b,c,d)`, `steps(n)` — and seconds alone means a straight line. An optional third element delays the start, so `{ 0.3, "ease-in", 0.15 }` waits a moment and then glides, and a row of bars given `i * 0.05` sets off in sequence from one payload. A value with no entry behaves exactly as before, `snap = 1` still wins, and restating a value mid-glide carries on from what is on screen.
- Labels that show a number no longer make a new string on every redraw. A readout like `text = "$fill", fmt = "%.0f"` cost about 90 bytes a label on every frame of an animating console, even while it printed the same digits; it now reuses the previous text unless the characters actually change. What it prints is unchanged, checked against the old formatter over a thousand format, unit and value combinations.
- `at = { ax, ay }` on `IMG` places the picture within the room `fit` leaves it — CSS `object-position`. Centred by default, as before; under `cover` it chooses which part of the picture is kept, and it takes expressions, so a picture can pan.

## 0.11.32

- Much less garbage while tessellating. A stroked shape allocated three buffers every time it was drawn; they are pooled now, and an undashed stroke no longer builds a list to hold its single run. Tessellating a page of 120 bordered boxes went from 86,136 to 5,496 bytes, 717 to 45 bytes a shape; an unstroked shape was already 5 bytes and is unchanged. In game, where a rebuild also uploads its mesh and updates its labels, a page animating at forty frames a second measured about **14%** less garbage overall — tessellation is a minority of what a rebuild costs there.
- A data payload no longer allocates a fresh evaluation context. Reading 26 values into the reused one allocates **nothing**; it was 3,176 bytes per payload, which is invisible at two payloads a second and 1.6 MB/s at forty.
- **New values now rebuild under the same ceiling as animation.** A payload used to force a rebuild immediately, so a page sending forty payloads a second rebuilt forty times a second on every console it owned, on screen or not, ignoring MaximumHz, Rate LOD and the off-screen cull. A new structure still draws at once; only values wait their turn.

## 0.11.31

- Clip paths take values: `CP id=track { R w = "$w" }` is re-cut every rebuild, so a clipped bar, a masked gauge or a list window follows the data payload with no new structure. A clip that shrinks to nothing hides what it clips.
- Gradients take values: `x1 y1 x2 y2`, `cx cy r fx fy`, `a`, stop positions and `$name` stop colours may all be expressions, re-read every rebuild.
- Fixed: a two-stop gradient whose ramp ended before the shape did kept ramping past its last stop instead of holding that colour, so a fade to transparent never finished fading.
- A group at zero opacity is skipped whole instead of node by node, so an alternative layout kept in the scene and hidden with `o = 0` costs nothing. Clicks, scrolling and pictures under it still register, as they do in a browser.
- Fixed: a screen capture of a console the player is not standing near came back as the terrain and sky behind it. The game switches a console's screen off while nobody is in the room with it, and a switched-off screen copies as a switched-off screen; the capture now switches it on for its own length and puts it back. Captures of a distant console work from now on, whatever is drawn on it.
- `v` on a group: `v = 0` removes the whole subtree, clicks and scrolling included, the way CSS `visibility: hidden` does. It takes an expression, so one scene can carry several states and show one of them.

## 0.11.30

- Level of detail is off by default: Rate, Curve and Count LOD all ship switched off, so every visible console rebuilds at full rate and full detail. Each can be switched on in the settings to save CPU with many consoles; an existing settings file is switched to the new defaults once.

## 0.11.29

- Fixed: a scene fed new data every frame rebuilt only on every second frame whenever a payload arrived while a rebuild was running (35 times a second at 71 FPS). It now keeps up with the frame rate.
- A finished rebuild is also picked up at the end of the frame, so new geometry reaches the screen a frame sooner.
- Diagnostics: each surface's line also reports the data payloads and structures it received and how often a rebuild was still running at frame start.

## 0.11.28

- Animated scenes rebuild up to 60 times a second by default (was 30), so script-driven motion can follow the frame rate. Rate LOD MaximumHz still sets the ceiling; an existing settings file still at the old default of 30 is moved to 60 once.

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
