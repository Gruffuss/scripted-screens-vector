# Changelog

ScriptedScreens Vector, newest first.

## 0.11.101

### Fixed

- **A hex `fmt` that is not one conversion printed the missing text in a placeholder**, while the
  same `fmt` on a whole binding printed correctly — one value, one label, two answers. With
  `v = 7.6`: `"%x {0:D2}"` gave `8 08` bound and `<-->` in a placeholder; `"%x {0:F1}"` gave
  `8 8.0` and `<-->`; `"%X {0:X}"` gave `8 8` and `<-->`.

  Two causes, both fixed. `TextPart.Hex` was taken from the spec the SPLIT left behind even when
  the split had failed, so a format whose first conversion is hex but which is not one conversion
  set it anyway. And the slow path then handed a float to a format carrying an integer conversion,
  which `string.Format` refuses — it retries as a long now, which is what the whole-binding path
  already did and the only reading that can satisfy `{0:X}` and `{0:F1}` from one argument.
  All three now print identically either way.

### Docs

- **My own point-count paragraph from the previous commit was wrong twice**, and the console
  builder session caught both. An ODD value count is not an error: the last value is dropped and
  the rest are used, so `{10,10,60,10,60}` is a two-point line, while `{10,10,60}` is left with one
  point and draws nothing. And the minimum is per PAINT, not per op: a stroke needs two points, a
  fill and a shadow need three, so a two-point `Y` draws its outline and nothing inside it. My
  first measurement used stroke-only for `L` and fill-only for `Y` and generalised from each.
- **A two-point `SP close = 1` is filled, shadowed and stroked**, not left as an open curve.
- **A third copy of "a spec that cannot be read renders `missing`"** — the claim corrected in the
  `fmt` table two commits ago appears again in the `T` prose, and was still wrong there.

### Measured

Both `fmt` tables re-run whole: the fourteen specs of 0.11.100 still land correctly, and the hex
formats now agree between binding and placeholder. Fingerprint byte-identical, unit suite 359.

## 0.11.100

### Fixed

- **0.11.97's unreadable-`fmt` report was a false positive on .NET formats that work**, and it
  brought the magenta border with it. `fmt = "{0} kPa"` drew `1.25 kPa`, `"{0,6:0.0}"` drew
  `   1.3`, `"{{{0}}}"` drew `{1.25}` and `"{0:}"` drew `1.25` — every one correct, every one
  reported. It also fired where no number went through `fmt` at all, and it MISSED `{0:Z}`, which
  is well formed and then fails because a float has no `Z` conversion.

  The check asked the wrong question. It asked whether the fast path could split the spec, which
  is not the same as whether the spec works. It now **formats a sample number and looks at the
  result**: a spec is bad when the attempt throws, or when the output comes back as the spec
  itself, meaning no placeholder was consumed and the author's text printed where the number
  should have been. The two failures carry different messages, since one draws the `missing` text
  and the other prints the spec.

  **It is also scoped now.** Only a `fmt` a number actually goes through is checked, so a label of
  literal text with a leftover `fmt` formats nothing and is left alone.

- **A placeholder's own bad format was silent.** `text = "<{$v:%q}>"` printed `<%q>` and said
  nothing. Checking the template parts rather than the node's `fmt` string closes it, because each
  numeric part carries the spec it will actually use — its own, or the node's inherited into it.

### Measured

Fourteen specs, each checked against what it draws: the six that format correctly report nothing,
the two where `fmt` is unreachable report nothing, and the five unusable ones plus the previously
silent `{$v:%q}` all report. Fingerprint byte-identical to 0.11.99, unit suite 359.

**This is my own regression from 0.11.97, found by the console builder session within hours of
pinning it.** The 0.11.97 check was written with a guard against exactly this failure — a `{0:F1}`
test — and the guard was too narrow: `{0:F1}` is the one composite format the fast path happens to
split.

## 0.11.99

### Fixed

- **A format spec a float cannot take KILLED THE WHOLE SCENE** when it reached a placeholder.
  `text = "<{$v}>"` with `fmt = "{0:Z}"` — and the same for `{0:D3}` and `{0:B}` — threw a
  `FormatException` out of the tessellation and the console drew nothing at all, while the very
  same spec on a whole binding (`text = "$v" fmt = "{0:Z}"`) printed the missing text and carried
  on. `float.TryFormat` THROWS on a specifier the type cannot use rather than returning false; the
  code already knew that for `X` and handled only that one. Guarded now, so a spec this cannot use
  falls through to the same slow path the whole-binding route always used. Measured: the scene
  draws `<-->` where it used to fail, with `{0:F1}` unchanged.

  **This is a different fault from 0.11.97's unreadable-`fmt` report, and that check cannot catch
  it**: `{0:Z}` is a well-formed .NET composite format, so it splits cleanly. It is the float that
  refuses the `Z`.
- **A def op written inside a `CP` was dropped in silence.** 0.11.96 began reporting a second
  SHAPE in a clip, but a `GL`, `GR`, `GC`, `CP` or `SYM` there returns null from the node parser —
  `ParseDefs` owns those — so the shape count passed straight over it. It is reported now, and
  with its own message, since an author who wrote a gradient inside a clip needs telling something
  different from one who wrote two rectangles.

### Measured

Fingerprint byte-identical to 0.11.98, unit suite 359. Both faults were reported by the console
builder session from reading the source, and both reproduced exactly as described.

## 0.11.98

Six faults that move pixels or change behaviour, as against 0.11.96 and 0.11.97 which only turned
silences into reports. Two of them change a scene that is CORRECT today; both are called out below.

### Fixed

- **`^` is guarded like `/` and `%` now.** It was raw `Mathf.Pow`, so `0^-1` was infinity,
  `(-2)^0.5` was not a number and `2^999` overflowed — and nothing between the expression and
  `Mesh.SetVertices` rejects either, while a scene is normally ONE mesh whose bounds every vertex
  feeds. One poisoned coordinate therefore took the whole console rather than the shape that asked
  for it. All three answer `0` now, which is what `/`, `%`, `mod` and `sqrt` of a negative already
  answered. Measured: `2^3` = 8, `2^-3` = 0.125 and `-2^2` = -4 unchanged, and a scene with a
  poisoned `x` emits no non-finite vertex.
  **This makes the reference's old sentence false, and that sentence was written the same day** —
  it said `^` had no guard and told authors to keep the base positive. Corrected in the same commit.
- **An `IMG` ignored the per-corner `rx` list** and drew square corners in silence, where an `R`
  reads it. Measured: `rx = {12,12,12,12}` and `rx = 12` now both emit 52 vertices, against 4 for
  square corners.
- **`fl weight = normal` could not un-bold a bold label's first line.** `weight` was a plain bool,
  so "absent" and "present and normal" were the same thing; it is nullable now. TMP ignores a
  closing bold tag for a style the component's own `fontStyle` carries, so the fix takes the bold
  off the label and lets the wrap bold the REST — `one <b>two` rather than an unclosed tag.
- **An expression could not hold a supplementary-plane name.** `$<astral>` reported `expected a
  name` and the attribute fell back to its default, while the same name worked as an `id` and as a
  label's `$binding`, both of which read the author's text as written. The name lexer tested
  `char.IsLetterOrDigit` on one UTF-16 unit and each half of a surrogate pair fails it; it reads a
  text element now. Measured: 1 problem -> 0, with `=plain` still reporting `unknown variable` so
  the lookup path is untouched.
- **The element swallowed clicks over its WHOLE rect once anything in it was clickable.** A click
  on an empty part of the scene hit no node AND never reached what was underneath, so a native
  `textinput` behind a vector element could not be focused at all. `VectorGraphic` is an
  `ICanvasRaycastFilter` now, answering for a pointer over a hit region **or over a scrollable
  container's viewport** — the latter decided by the same `Locate` test `OnScroll` and `OnDrag`
  use, so the filter can never take a pointer those two then ignore. Only the START of a gesture is
  filtered; Unity runs drag and release on the object stored at press time without raycasting
  again, which is why the drag handlers already re-test the position.
  **Changed, so worth knowing:** a clickable vector element used deliberately as a SHIELD over
  something behind it stops shielding.
- **A stroke `so` inherited into an `SC` became its scroll offset.** `so` is stroke opacity on
  every other op, so `G so=0.5 { SC sov=1 }` scrolled the container to 0.5 with nothing on the
  `SC` asking for it — the merged map cannot tell an inherited key from one the author wrote. The
  scroll offset is read from the node's OWN map now. 0.11.92 fixed the other half of the same
  collision. Measured: the fault 0.500 -> null through both the bare and the `style = {…}` route,
  while `SC so=0.5 sov=1` still reads 0.500 and an inherited `so` still dims a stroked child
  inside the container (30 vertices against 24 bare).
  **Changed, so worth knowing:** a scene relying on an ancestor's `so` to set a scroll position
  must write `so` on the `SC` itself.

### Held back

- **An `LS` evaluates its paint outside its sample frame**, so `i` there means the enclosing repeat
  while in a `YS` it means 0. The patch for it is written and NOT applied: moving the paint into
  the sample scope makes `Index(0)` the sample number, but `EvalContext.Pointer` compares that
  against the hit region's index, which is the enclosing repeat instance — so `hover` and `down`
  would stop matching on a clickable `LS`. Fixing an inconsistency by breaking a documented feature
  is not a trade worth making silently; it needs a design that keeps both.

### Measured

The 17-scene fingerprint is byte-identical to 0.11.97 at all three `t` values, and the unit suite
passes at 359.

## 0.11.97

The rest of the silence batch: the `src` reader and the text formatter. The pixel-moving changes
move to 0.11.98.

### Fixed

- **A malformed brace was accepted in silence, both ways.** A scene whose text ended with an
  unclosed `{` parsed into a complete, plausible tree — every node inside the block had already
  been attached — and read exactly like a closed one. A stray `}` was worse: it ENDED the scene
  and discarded every node after it, with nothing reported anywhere, so a page simply stopped
  halfway. Both are refused now, and the unclosed one names the line that OPENED the block rather
  than the innocent last line of the scene.
- **The unquoted-value error never named the character that broke it.** `T text=one,two` rejects
  the whole scene, and the message talked about spaces, `=` and `;` — never the comma. It now
  reads `expected an op, found `,``. Message text only; nothing changes what is accepted.
- **A `]` inside a `{$n:spec}` format spec was silently wrong, and wrong in two different ways.**
  The placeholder took the LAST `]` as the index's closing bracket, which it is only when the
  spec holds none. Without an index, `{$n:%.2f]}` made `n:%.2f]` the whole NAME and drew the
  missing text for a name nobody wrote. WITH an index, `{$arr[0]:%.2f]}` skipped the real colon,
  handed `0]:%.2f` to the expression parser — which reported a `]` it could not read, a message
  about nothing the author wrote — and **dropped the format entirely**, so the number printed
  through the default `0.##`. The colon is now the first one outside the index: both forms apply
  the format, and a `]` in a spec is the literal that `[` already was.
- **An unreadable `fmt` printed itself, in silence**, where the reference promised the `missing`
  text. `%q` drew `%q`, `nonsense` drew `nonsense`, `""` drew no label at all. Printing the
  author's own spec is the honest half — `missing` would hide the typo — so the silence is what
  is fixed. **A .NET composite format such as `{0:F1}` still works and still says nothing**: it
  also comes back unchanged from the printf translator, so "unchanged" alone is not the test, and
  the check asks whether any conversion reached the formatter at all.

### Measured

The 17-scene fingerprint is byte-identical, every one of the repo's 22 scene-text blocks is still
accepted, and the unit suite passes at 359. `{$n:%.2f]}` and `{$arr[0]:%.2f]}` both draw `254.60]`
where they drew `--` and `254.6`; `%q` and `""` report while `%.1f` and `{0:F1}` stay silent.

**`%.1f` would not have shown the format being dropped** — it happens to equal the `0.##` default
for the value under test. The decimals in the falsification are the whole point of it.

## 0.11.96

Nine silent failures become reported ones. None of them moves a pixel that was already correct;
each turns a scene that drew wrong, or drew clean over a typo, into one that says so.

**This changes what an existing scene looks like.** A scene carrying any of these faults has been
drawing without complaint and will now show the magenta problem border. That is the point, but it
is a visible change rather than a quiet fix.

### Fixed

- **A malformed `sh` was dropped in silence.** An `sh` that is not a list at all, an entry with
  fewer than five values, a fifth value that is not a string, and a colour that will not parse
  were each discarded without a word, so a mistyped `"#0000O01f"` left a card with no shadow and
  nothing to read. All four report now. A `USE`'s attributes are symbol PARAMETERS and may be
  called anything, `sh` included, so a `USE` is exempt — the same exemption the unknown-attribute
  check already makes.
- **A fault inside a clip was never reported at all.** `ClipOutline` parsed the `CP`'s shape
  against a throwaway scene that was dropped on the next line, so a typo'd attribute or an
  unsupported op inside a clip reached nothing: no problem, no border, and the clip quietly the
  wrong shape.
- **A `CP` holding several shapes used the first and ignored the rest, silently.** It reports the
  count now. Not unioned: `Frame.Pieces` can already draw a node once per clip region, but
  once-per-region equals a union only for DISJOINT regions, and overlapping ones double-composite
  — invisible on an opaque fill, obvious through transparency and feathering.
- **`mask` without its leading `@` was ignored without a word**, where the sibling `clip` takes a
  bare id, so `mask = "fade"` is the natural slip and drew an unmasked group. An EMPTY `mask`
  stays silent and still means "no mask": that is how a `nodes` patch turns one off, since a patch
  merge overrides a key and never deletes one.
- **A `src` that failed to parse, on an element that also carried `root`, silently drew the
  `root` scene** and sent the text error to the log alone.
- **Gradient stops were dropped in silence** — a stop that is not a pair, one whose colour is not
  a string, and one whose colour will not parse — so `{ { 0, "#fff" }, { 1, "bleu" } }` was a flat
  `#fff` with nothing said. A gradient left with no stops says so too.
- **The scene HEADER's own keys were never checked**, so `ztxt` for `ztext` was ignored while the
  same typo on a node was reported. The header has its own vocabulary, and the keys the HOST reads
  off the same props are exempt — `z_index`, `zIndex`, `visible`, `parent_id` and the visor
  anchors — because this mod's own README tells authors to layer two vector elements with
  `z_index`, and reporting that would mark a scene nobody mistyped.
- **An `IMG` whose picture had not downloaded recorded no hit area**, so a clickable picture was
  dead until it loaded, silently.
- **`sd` and `sdo` were accepted and read by nothing.** The only two such keys in the whole
  attribute list, added beside `dash` and `dofs` and never read by any commit. They are gone, so
  `sd = {4, 2}` reports an unknown attribute instead of silently drawing a solid line.

### Measured

Seven faults each report; seven valid forms that an adversarial review predicted would break stay
silent — a `USE` with an `sh` parameter, `mask = ""`, `z_index` and `visible` on a header, a sound
shadow, a one-shape `CP`, and a plain scene. The 17-scene fingerprint's problem counts are
unchanged, so no shipped example newly reports. Unit suite 359 passes.

Three of these nine would have broken a valid scene as first written. The review that caught them
is the reason this release does not ship a problem border on working consoles.

## 0.11.95

### Fixed

- **A deep number inside a colour expression ended the GAME PROCESS.** `f = "=mix(#a,#b,<deep>)"`
  and `f = "=if(<deep>,#a,#b)"` overflowed the stack, and a stack overflow cannot be caught in
  .NET -- it takes the process down, with nothing in the log.

  This was the **eighth** recursive walk, and the one 0.11.85-0.11.86 missed while converting the
  other seven to explicit stacks. `EvaluateColour` does walk the colour tree iteratively, but it
  reached `if`'s condition and `mix`'s blend factor through `Arg`, which calls `Evaluate` on the
  argument -- and only a ROOT is given a measured tree depth at parse, so an argument's depth
  reads 0, the deep-tree test is false, and it recursed at its subtree's full depth. `IsColour`
  never walks those arguments either, so the expression was accepted as a colour and then
  evaluated on the tessellation worker every rebuild.

  Measured out of process, before and after. `mix` and `if` both survived 7,968 levels of prefix
  `-` and died at 8,125 with `Stack overflow.`, exit 127; the same number written as an ordinary
  numeric attribute survived 60,000, which is what pinned the fault to the colour path rather
  than to depth in general. After the fix both survive 200,000. The 17-scene fingerprint is
  byte-identical at all three `t` values, the colour-expression probe's 20 checks are unchanged,
  and the unit suite passes.

  The memo is allocated only for a tree that is genuinely deep, so an ordinary `mix` keeps the
  single predictable branch it had.

  **This is why the reference's promise that any depth parses and draws was not true**: it held
  for every numeric attribute and failed for a colour one.

## 0.11.94

Ten bugs from the reference sweep, each watched failing on the previous build before the fix and
measured again after. The 17-scene fingerprint is byte-identical across all three `t` values, so
nothing that already drew has moved.

### Fixed

- **`weight` given as a number was ignored.** The documented "a number of 600 or more" went
  through a string-only accessor, which returns null for a number, so `weight = 700` from Lua
  left the label at its ordinary weight without a word. `weight = "bold"` always worked.
  Measured: `weight=700` bold `False` -> `True`.
- **`%%` did not collapse to one percent sign.** The guard that spots it only fired when the
  `%%` opened the spec, so `"{=50:%.0f%%}"` drew `50%%`. Measured: `50%%` -> `50%`.
- **`{$n:%x}` truncated where `fmt = "%x"` rounds**, so the same number printed differently
  through the two, against the code's own comment. A NaN also printed the long it cast to.
  Measured: `{=254.6:%x}` `fe` -> `ff`; a NaN now prints its missing text.
- **A `P` carrying `sh` but no `f` cast no shadow.** Both shadow calls sat inside the fill test,
  where every other shape emits them outside it. Measured against an identical `Y`: 0 vertices ->
  1361, the same as the control.
- **A malformed colour-filter value fell back to 1 for all seven filters.** One is identity for
  `bri`, `con` and `sat` and FULL EFFECT for `gray`, `sep` and `inv`, so a value that was
  already reported as broken turned a red rectangle fully grey. Each filter now falls back to
  its own identity. Measured: a broken `gray=` amount 1.00 -> 0.00, still reported.
- **An enclosing group's `blur` was lost inside an `SC`.** The scroll container's frame carried
  opacity and scale but not blur, so the field fell back to 0 and the content drew sharp.
  Measured: 4 vertices -> 3333.
- **A missing numeric data array was not reported.** `$arr[i]` in an expression read 0 in
  silence, while the same reference in a label's text was listed. It was the one accessor that
  did not record the name. Measured: `Missing` empty -> `[nosuch]`, matching the label control.
  An out-of-range index stays silent, as it does for a string or colour array: the array is
  there, the slot is not.
- **A malformed number in a gradient def fell back to 0 instead of that key's own default**, so
  a broken `y2` collapsed the ramp axis to no length rather than the documented 1, and a broken
  `r` gave a radius of 0. Measured: both 0.00 -> 1.00.
- **A def's keys were never checked.** `Validate` ran on nodes only, so a typo on a gradient or
  a clip was ignored in silence -- the exact fault that check exists to catch. Measured: a `GL`
  carrying `nosuchkey` reported nothing -> `GL "g": unknown attribute "nosuchkey"`, matching the
  node control, with a valid `GL`/`GR`/`GC`/`CP` set still reporting nothing.
- **`d` rejected compact SVG numbers.** The reader was greedy over `.`, so `L60.5.5` -- two
  numbers, and what a minifier emits -- parsed as neither and the coordinate silently read 0;
  and the two arc flags were read as whole numbers, so `a20 20 0 011 40` took `011` as the
  large-arc flag, the endpoint's x as the sweep flag, and drew nothing at all. A second `.` now
  ends a number and a flag is one character. Measured against the same paths written out in
  full: `L60.5.5` box `(-1.87,78.13)-(121.87,201.87)` -> exactly the control's
  `(15.29,74.61)-(123.44,201.91)`; the packed arc 0 vertices -> the control's 138, same box.

### Changed

- **A typo on a `defs` entry now shows the magenta problem border.** A scene that carries one has
  been drawing clean and will now report it, which is the point, but it is a visible change to an
  existing scene rather than only to a broken one.

### Tests

- **The unit suite had been red since 0.11.86** and nobody noticed, because its one failing check
  asserted the array-depth cap's refusal message -- and that cap was deliberately removed in
  0.11.86. It now asserts what the removal was for: an array nested 2000 deep parses, while an
  unterminated one is still refused. 358 checks, exit 0.

## 0.11.93

- **A `T` or an `IMG` with `click = 1` now registers a hit area.** Neither ever has: a hit area is
  recorded where a shape's outline is built, and a label and a picture go through neither path, so
  `click`, `press`, `xy`, `drag` and `drop` on them did nothing and reported nothing. Each now
  takes clicks on its own box -- a picture through the same outline it draws, corner radii
  included; a label through its rect, the same region an `R` in that place would claim.

  **Changed, so worth knowing:** a label that sits over a clickable shape and carries both an `id`
  and `click` now takes the click itself rather than letting it through, because a click lands on
  what is drawn last. A label with no `click` registers nothing, exactly as before.

- **Fixed: one unquoted `$name[i]` rejected the whole scene.** Values are read to whitespace
  because expressions and bindings are full of `,` `[` `]`, but only a value starting with `=` was
  read that way. `text=$rows[i]` therefore ended at the `[`, the `[` was read as the next op, and
  the scene was refused with "expected an op" -- the whole page, for one unquoted binding, exactly
  the form the reference gives as an example. A `$` value now reads to whitespace too. Inside an
  array the ordinary rule stays, so `p=[$a,$b]` still splits on the comma.

## 0.11.92

- **Fixed: a scroll container's `so` made every outline inside it invisible.** `so` is stroke
  opacity on a shape and a **scroll offset** on an `SC`, and because a scroll container has
  children it was passing its own `so` down as a paint default. So `SC so=0 ...` asked the list
  to jump to the top and drew every stroke inside it at alpha 0; a larger offset escaped notice
  only because opacity clamps to 1. An `SC` now keeps its `so` to itself, the way a group already
  keeps `s` (which is scale there and stroke colour on a shape). `so` as a default on an ordinary
  group is unchanged.

## 0.11.91

- **Fixed: `fea` was ignored on a shape with no fill.** A stroke is an edge and takes a feather
  like any other, but every read of `fea` sat inside a fill branch, and that branch returns as
  soon as `f` is missing or `none`. A stroke-only shape therefore kept the automatic feather
  whatever it asked for, so `fea = 0` and `fea = 8` drew exactly the same stroke. Measured on a
  60x160 box stroked at 2: before, both forms produced 28 vertices; after, `fea = 0` produces 12
  and keeps its hard edge. `examples/11` and `13` draw stroke-only groups with `fea = 0` and were
  quietly getting a soft edge.

  **The same early return also dropped `fea` from a shape whose fill failed to parse** -- a
  colour expression with a branch that is not a colour, say. Those shapes now keep the edge they
  asked for too, which is the only change to any existing drawing: a hard edge where there had
  been an automatic one.

## 0.11.90

- **Fixed, properly this time: a geometry patch waiting for its structure still dropped the one
  before it.** 0.11.89 combined the waiting patches, but only at the top level -- and `nodes` is
  a single property holding a map of node id to that node's patch, so the later `nodes` replaced
  the earlier one whole and the first patch vanished exactly as before. Patches now combine per
  node id, and within a node per property, so two queued patches land exactly as two delivered
  ones would: a live patch merges into the node's current properties rather than replacing them,
  and a waiting pair now does the same.

## 0.11.89

- **Fixed: a geometry patch sent before the structure could drop an earlier one.** When a
  `nodes` patch arrives for a scene whose graphic does not exist yet -- the structure has not
  come through, or the surface is mid-rebuild -- it waits. Two patches waiting for the same
  scene overwrote rather than combined, so the first was lost with nothing reported. The data
  travelling beside it had always merged, with a comment explaining why; the patch beside it did
  not. They now both merge, later winning per key, which is what ScriptedScreens itself does
  when it combines properties.

  Two patches in one tick is ordinary rather than exotic: the host queues one operation per
  update and never combines them, and it delivers at the end of every chip execution whether or
  not the script asked it to.

## 0.11.88

- **Fixed: a group's colour filters skipped a label's outline.** `G gray=1 { T oc=... ow=... }`
  drew a grey fill with an outline still in its original colour. The fill is filtered through the
  glyph vertices and the shadow colours are filtered where they are placed, but the outline is the
  one text colour that is a material property rather than a vertex colour, so nothing downstream
  ever touched it. Measured before the fix: a red outline stayed at (1.000, 0.000, 0.000) while
  the fill it belonged to drew at (0.213, 0.213, 0.213). Nested filters compose correctly too.

  The reference said filters "reach the glyphs' vertex colours", which was true and misleading in
  the same breath; it now names the fill, the shadows and the outline.

## 0.11.87

Two things 0.11.85 broke and shipped. Its own offline suite had been reporting both; this is the
first version since where every test passes.

- **Fixed: a clickable shape inside a fully transparent group stopped being clickable.** When
  0.11.85 replaced the recursion that parses children with a queue, the step that gives a group
  its children's properties stayed where it was -- at the point where the group's child list is
  now still empty. It therefore did nothing at all. A group only skips drawing its subtree when
  nothing inside needs reaching, and "owns a click region" is one of those things, so a
  transparent group carrying a button was skipped whole and the button went dead. `o=0` keeps
  its click regions, the way a browser still sends a click to an `opacity: 0` element; `v=0`
  takes them with it, as before.
- The same fault stopped animation and scrolling in a child from marking its parent, and made a
  group holding text rebuild by the wrong path.
- **Fixed: `%x` printed the missing-value text instead of a number.** .NET's `x` is an integer
  format and refuses a float; the buffered formatter treated that refusal as a missing value, so
  `{=255:%x}` rendered as whatever `missing` was set to. Hex now formats from a rounded integer
  on the fast path, and anything else the fast path cannot format falls back to the original
  formatter rather than claiming the value is absent. `NaN` and the infinities still print their
  own names. No allocation added: a hex readout still costs 0 bytes per rebuild.

## 0.11.86

The 0.11.85 sweep removed the nesting limits from the scene parser, the expression parser and
the tessellator, but five recursions survived it -- two still capped at 32, three uncapped and
able to end the game's process. All five are explicit stacks now, so the claim that nothing
nests too deep is true of the text format and of colour expressions as well.

- **Scene text no longer stops at 32 levels.** Blocks (`G { G { ... } }`) and arrays
  (`p=[[[...]]]`) each had a 32-deep cap that refused the scene. A scene built by a tool nests
  further than one written by hand, and the text form was the only export path a deep scene
  had: the table form dies inside ScriptedScreens' own Lua-table conversion at about 2000
  levels. 5000 levels of each now parse.
- **Fixed: a deep scene could end the game's process.** Under the block parser sat a further
  recursion -- the step that turns parsed nodes into properties -- which the 32-deep cap had
  been hiding, since no scene could get deep enough to reach it. Without it a deep scene stopped being refused and started
  overflowing the stack instead, which is not catchable in .NET and takes the process with it.
  Measured at 1527 levels before the fix.
- **Fixed: a deeply nested colour expression could end the game's process.** Both the check
  that decides whether an expression yields a colour and the evaluator that produces it walked
  the expression tree recursively, while the parser that builds that tree does not. So
  `mix(mix(mix(...)))` parsed happily and then overflowed. 5000 levels now evaluate.

No change to what any existing scene draws: every scene in the repo's corpus produces an
identical mesh, vertex for vertex, except the one that was being refused outright.

## 0.11.85

**The depth limits are gone, because the recursion is gone.** 0.11.77-0.11.84 capped expression
nesting, array nesting and node nesting after four separate stack overflows ended the game's
process. Those caps refused input that was only DEEP, not wrong -- and a scene generated from a
document nests further than a person would type -- so they were a mitigation presented as a fix.
Every one of the seven recursions behind them now uses an explicit stack instead.

- The expression parser: an operand/operator stack for precedence and a group stack for `(`,
  `$name[]` and call arguments. Precedence and associativity are untouched -- `2^3^2` is 512,
  `-2^2` is -4, `2^-3` works, `---1` is -1 -- and 200,000 levels now parse.
- Expression evaluation, which is where the overflow merely moved to once parsing was fixed. A
  deep tree is walked post-order with an explicit stack and memoised; `Arg()` reads the memo
  rather than descending. Shallow trees, which is every real scene, keep the original path and
  pay one predictable branch.
- Node parsing: children are queued rather than descended into.
- The tessellator's whole traversal -- groups, repeats, scroll containers, concave-clip pieces,
  id scopes and node spans -- is one work stack with explicit cleanup items, so a `finally` that
  used to ride on the call stack is now an item on the work stack and still runs when something
  throws.
- `Reindex`, `HoldsText` and `CollectForcedScrolls` likewise.

A scene nested 100,000 deep now parses and draws. Verified by a deterministic fingerprint of 17
scenes at three `t` values -- vertex, triangle, shape, hit-region and label counts, mesh bounds
and problems -- which is byte-identical between the recursive and iterative versions, with
scenes written for masks, concave clips, scroll, repeats, symbols and `ztext` ordering.

- Kept, and correctly: the **symbol cycle** guard. A symbol that reaches itself has no finite
  expansion at all, so that input is wrong rather than deep.
- Fixed on the way: `MeshBuilder.Clear()` does not reset `Tint`, so a throw inside a group with
  `filters` or a `mask` left the tint set, and every later rebuild of that console drew through
  it. The traversal now restores it while unwinding.

## 0.11.84

Both reported from reading v0.11.80, both
confirmed here by running them first.

- Fixed: **two more recursions that ended the game process.** The depth cap counted parentheses,
  prefix `-` and `^`, but a function call's ARGUMENTS and a `$name[...]` INDEX recurse through
  the same parser and reached neither. `abs(abs(abs(...)))` and `$a[$a[$a[...]]]` at 20,000 deep
  both printed "Stack overflow." and exited 127; they now fall back to the attribute's default
  like any other over-nested expression. `abs(abs(abs(1)))` and `$a[$a[0]]` are untouched.
- Fixed: **a colour expression with one non-colour branch drew white, silently.** `IsColour`
  checked only the top level, so `f = "=if(c,#A,5)"` and `f = "=mix(#A,1,t)"` claimed to be
  colours, failed at evaluation, and fell through to the default paint. Every branch that can be
  returned is now checked -- both outcomes of an `if`, both ends of a `mix`, recursively -- so
  these are reported as a scene problem and drawn magenta, like any other colour that will not
  parse.
- Workshop description refreshed: it still advertised 0.11.21-0.11.30 and "fifteen examples".

## 0.11.83

- Fixed: **a scene with no text captured blank.** The dither shader carries a blur's coverage in
  TEXCOORD1 and multiplies alpha by it, but a canvas uploads only the vertex channels named in
  `additionalShaderChannels` -- everything else reaches the shader as zero. ScriptedScreens'
  screen capture builds its own Canvas (`[ScriptedScreens-McpCaptureCanvas]`), which starts at
  `None`, so every vector pixel on it drew at alpha 0. Text scenes escaped it because
  TextMeshPro turns TexCoord1 on for whatever canvas its labels sit on; the mod had been
  free-riding on that since the dither material shipped. `VectorGraphic` now asks for the
  channel itself, once per canvas, from `UpdateGeometry`. Examples 01-07 went from 7,327 bytes
  (the bare panel) to 28-135 KB; 08-18 are unchanged. Live consoles are unaffected -- their
  canvas already had the channel, which is why they always drew correctly.

## 0.11.82

Three faults found from reading the source, all confirmed here and
all invisible in normal use, which is why they lasted.

- Fixed: **a table-form scene inherited the previous run's data and clock.** The carry-over guard
  compared the structure text of the old graphic against the new one, and for a `root` scene both
  are null -- so `string.Equals(null, null)` was true and any table-form page pushed with a scene
  id the board had used before took the earlier run's values and `t`. `Scenes` is never pruned, so
  "before" could be an hour ago. The carry-over now applies when a capture is running, where the
  structure is identical by construction, or when a non-empty `src` matches.
- Fixed: **a `nodes` patch was dropped when its data arrived before the structure.** `PendingData`
  held the payload's values and nothing else, so a script that sent a geometry patch ahead of the
  structure lost it silently. Patches now wait beside the data and are applied first.
- Fixed: **`Snapshot()` left out data still in flight.** Values that arrived while a rebuild was
  running had not reached the context yet, so a capture's replacement graphic was handed a console
  missing whatever was sent in that window.
- Fixed in `examples/10-text.lua` and `12-click.lua`: **both declared a `keep = 1` data element and
  never committed before the first `tick`.** ScriptedScreens merges successive upserts of one
  element into a single one, with `data` replaced whole, so the declaration's values never arrived
  and every name only it set read `--`. Measured, one variable apart: without a commit the declared
  value reads `-- LOST --`, with one it reads `SURVIVED`. Examples 08, 16 and 17 always committed.

## 0.11.81

Both reported from reading the source, and both
confirmed here by running them first.

- Fixed: **a deeply nested scene in the TABLE form killed the game process** -- as far as this mod can fix it; see below. The text form is bounded before the parser ever runs -- `SceneText` caps `{` nesting -- but a table arrives as a tree and reaches the node parser directly, so `c = { { op = "G", c = { ... } } }` built by a Lua loop recursed with nothing to stop it. Measured: 10 levels fine, 1,000 levels "Stack overflow." and exit 127. Nodes are now capped at 64 deep and the scene reports it. The tessellator recurses per level as well, so one cap covers both.
  - **A deep enough table still ends the process, and not in this mod.** Measured on 0.11.81: 100 levels survives and is reported, 2,000 levels kills the game with nothing in the log. The parser refuses at 64 whatever it is handed, so it behaves identically at both -- the difference is below it, in the conversion from the Lua table to props, which runs before any of this mod and recurses per level. Nothing here can guard that. `InGameTest-deepnest.lua` carries the 100 case; its header says why 2,000 is not worth running.
- Fixed: **state stranded by a throw was carried into every later rebuild.** Three places saved and restored around a recursive call without a `finally`: the repeat stack, the `[ThreadStatic]` concave-clip piece flag, and the scroll offsets. A build is also reset before it starts.
  - **This was made reachable by 0.11.76.** Before it, a throw inside the tessellator faulted the task and the graphic stopped, so nothing used the stranded state; now the throw is caught and reported and the graphic goes on rebuilding -- with `i` and `n` wrong in every repeat, the scroll offsets wrong inside `SC`, and the piece flag suppressing text, hit regions and scroll containers in **every other graphic on that worker thread**. Making a failure survivable is what made its side effects matter.

## 0.11.80

- Fixed: **two more recursions that killed the game process.** 0.11.77 capped nested `(`, but the expression parser descends once per prefix `-` and once per `^` as well, and neither was counted: `---...-1` and `1^1^1^...` at 100,000 both ended the process. Measured out of process before and after -- "Stack overflow.", exit 127, then a clean fall back to the attribute's default. All three recursions now share one depth counter at 64, so an ordinary `---1` and `2^3^2` are untouched.
  - **These arrive through any numeric attribute.** A value needs no leading `=` to be parsed as an expression, so `w = "---...-1"` is as dangerous as `w = "=---...-1"` was. That is what made it worth fixing rather than noting.
  - Reported from reading the source, and confirmed here by running it. The tool that reported it already caps prefix runs and `^` counts before generating a scene, so it was never going to send one.

## 0.11.79

- Added: **a colour can be an expression.** `f = "=if(down,#2E8B6E,if(hover,#6FE3B6,#5FD9A8))"` is a button with three states in one node and nothing sent to the chip; `s = "=mix(#24405A,#3A6E8B,hover)"` blends between two. Works on `f` and `s`, with `hover`, `down`, `t`, `$data` or anything else an expression reads.
  - `mix(a, b, t)` blends **premultiplied**, as gradient stops do, so mixing towards a transparent colour fades out instead of drifting through the other colour's hue -- `mix(#FF0000, #00000000, 0.5)` and `mix(#FF0000, #FF000000, 0.5)` are the same colour.
  - Colours are not a type in the evaluator: every other value is a float and a 32-bit RGBA does not survive one. They exist only where a colour is asked for. `if(hover, 1, 2)` is a number; a colour read as a number is 0, and an expression that does not give a colour is reported as a scene problem and drawn magenta rather than guessed at.
  - A `#` literal becomes a `Color` **at parse time**, never later: Unity's colour parser is a native ECall and tessellation runs on a worker thread, where an ECall throws.
  - Why it was added: it is what both the author of this mod and its docs reached for by instinct, twice in one session, before noticing it was not supported. A format whose natural spelling is an error has the bug.
- Added: **three examples**, `16-pointer.lua` (hold, hover, `xy`, drag and drop, and hover styling with no chip round trip), `17-pictures.lua` (`IMG`: the five `fit` modes, `at`, `uv` crops, tiling, nine-slice) and `18-lines.lua` (`cap`, `join`, `ml`, `dash`, and open versus closed curves). Between them they cover what had no example at all: the pointer flags, pictures as a subject, and line ends.

## 0.11.78

- Fixed: **an open shape answered clicks in its empty middle.** A hit region is a polygon, and for an open `L`, `SP` or `P` that polygon was the path's own points, which close implicitly: three sides of a box therefore claimed the whole box, including the middle it had never drawn. A stroked open shape is now clickable along its stroke, as SVG's `visiblePainted` has it. Seen in game on `InGameTest-0.11.74.lua` the moment those shapes became clickable at all (0.11.74).
- Unchanged on purpose: a **closed** shape still answers inside its outline, whether or not it is filled -- that is the region it encloses, and an unfilled closed shape is a way scenes make an invisible hit area. An open shape with neither fill nor stroke also keeps the old behaviour, for the same reason: there is no stroke to take as its area.

## 0.11.77

Three inputs that ended the PROCESS rather than the parse. A .NET stack overflow cannot be
caught, so where the earlier parser guards turned a hang into a message, these turned a crash
of the whole game into one. Confirmed out of process against the unguarded parser: "Stack
overflow.", exit 127, nothing catchable and nothing logged.

- Fixed: **a symbol that uses itself, or a cycle of symbols, crashed the game.** `SYM` expansion
  recursed with nothing to stop it, so a `SYM` whose body contains a `USE` of itself expanded for
  ever. It is now reported like any other scene fault, naming the chain (`a -> b -> a`), and that
  branch is simply not expanded. The same symbol used twice side by side is not a cycle and still
  works. This is the one of the three a person writes by accident.
- Fixed: **arrays nested past 32 deep** in scene text, which recursed through the value reader.
  Refused with a message; the deepest array in any example here is two.
- Fixed: **expressions nested past 64 parentheses**, which recursed through the expression
  parser. Refused as a malformed expression, so the attribute falls back to its default as it
  does for any other.

Reported by the ScriptedScreens console builder, whose own validator already rejected all three
before generating a scene; these close them for scripts it did not write.

## 0.11.76

Both of these were found by one test page: a label containing `{=255:%x}` blanked a console.

- Fixed: **`%x` threw, and the throw blanked the whole surface.** .NET's `X` is an integer format, so handing it a float raises `FormatException` rather than declining, and `float.TryFormat` throws it instead of returning false. A hex value now rounds to an integer first. Also `%x` is lower case and `%X` upper, as printf has them; both used to give upper.
- Fixed: **a scene that threw while being drawn went blank and said nothing.** The tessellation runs in a `Task`, so the exception faulted the task and stopped there: no geometry reached the surface, the scene listed no problem, and the log had no line -- `vector_stats` said "25 nodes, 0 shapes emitted, no problems" while the console showed the room behind it. A throw is now reported as a scene problem, like every other fault, and whatever was built before it is still drawn. The capture path reports it the same way.

## 0.11.75

- Fixed: **`v = 0` did nothing on anything but a `G`.** It was read in the group case of the parser and checked in the group case of the tessellator, so on a shape or a label it was accepted as a known attribute, reported no fault, and was then never looked at again: the node drew as though it had not been written. It now hides any node, and takes that node's hit region with it, as it always has for a group. Found by a test page of my own that used `v` on a `T` and silently showed both states at once.

## 0.11.74

Five things the docs described as limits. Each was a choice or an unfilled detail rather than
something the renderer could not do, so each is now done.

- Fixed: **`press`, `xy`, `drag` and plain clicks did nothing at all on `L`, `Y`, `SP` and `P`.** A hit area was recorded on the one code path a rectangle and an ellipse take, so a clickable polyline, polygon, spline or path registered nothing, fired nothing and reported nothing -- the silent kind of failure. All four now register exactly one region each, like every other shape; a `P` is clickable over its outer contour, holes included.
- Fixed: **`blur` left every shape but `R` and `C` sharp.** The polyline path carried its own copy of fill-and-stroke, which is why it missed both the blur and the hit area; it now calls the same one. A `P` blurs its outer contour, the approximation its shadow already made. A closed shape with `sh` but no fill now also casts its shadow, as `R` and `C` always have.
- Fixed: **a `style` on the scene root was read by nothing.** It is now inherited by every node in the scene, in the table form (`style = { ... }`) and the text form (bare attributes on the `SCENE` line), exactly as a `G`'s is. `fit` is excluded, because on the root it means how the viewBox meets the surface while on a `T` it means shrink-to-fit and on an `IMG` how the picture fills its box -- put a text or image `fit` default on a `G`.
- Fixed: **re-sending the same structure restarted every animation.** `set_props` re-upserts the whole element, so a script holding its structure in an element it touches each tick reset `t` to 0 every tick and dropped every `nodes` patch with it: animation never advanced. An identical `src` to the same surface is now nothing to do -- same text, same geometry, same scene -- and the parse and rebuild are skipped with it. A changed structure still restarts the clock, as it must. The table form has no cheap comparison and is unchanged.
- Added: **`SP close = 1`**, a closed spline. The curve wraps through one further span back to its first point with its tangents taken around the ring, so the seam is as smooth as any other point, and the ring takes a fill, a shadow and a `blur` like any other closed shape. Needs three points or more. Before it, a rounded blob had to be written as a `P`.
- Fixed: **three unit tests had been added to a list the runner indexed by hand**, `All[0]` to `All[3]`, so they compiled, ran never, and passed by being absent. The runner iterates the list.

Left as it is: **`sat` on a `T` still does nothing.** That one is not a missing detail -- a `T` is a
TextMeshPro label whose outline (`ow`, `oc`) is a single uniform colour, so there is no gradient
along it for `sat` to pick a point on.

## 0.11.73

- Fixed: **`Tessellator.NodeSpans` could report vertex ranges that no longer existed.** A masked group that refines removes everything from its start onward and rebuilds it with a different vertex count, so spans already recorded for the nodes inside it described nothing. Those are now discarded; the group keeps its own span, which is recorded afterwards and is still correct. A tool mapping a click to the wrong node is worse than one mapping it to the enclosing group. `TagNodes` is off in game, so this affects editors and previews only.

## 0.11.72

- Fixed: **an array spanning a newline exhausted memory instead of parsing or failing.** Inside `[ ]` the reader steps over space and tab only, so a newline -- or a `{` or `}` -- was neither skipped, nor `]`, nor `,`, and the value reader returned an empty token without advancing. The loop then appended empty values until the process died. `R p=[1,` newline `2]` is an ordinary thing for an author to write, so this was reachable by hand. An array must be closed on the line it opens, and saying so is now the error rather than a hang: letting a line break through would make a missing `]` swallow the rest of the scene instead of being reported where it happened.

## 0.11.71

- Fixed: **a repeating fault grew the problem list without bound.** `Problems` is never cleared, and four call sites appended to it directly instead of going through `Problem()`, bypassing its dedupe and its 16-entry cap. The worst was in the tessellator: an undeclared gradient id appended a problem and logged a warning on **every rebuild**, so an animated scene at 60 Hz added sixty entries a second. All four now go through `Problem()`, which also stops logging a fault it has already reported -- repeating one now costs a list scan and nothing else.
- Docs: `REFERENCE.md` said a fully transparent `T` is **still placed, deliberately**. It is not: a label whose final alpha is at or below `0.002` is skipped, because TextMeshPro work is main-thread work and the carry-both-states-and-fade-one idiom would otherwise pay for both every frame. Skipping is safe because a pooled label is reassigned every property when taken. Do not rely on an invisible label holding a pool slot.

## 0.11.70

- Fixed: **a malformed `d` could hang the renderer.** Two inputs consumed nothing per pass and so looped for ever: a number after `Z`, which re-enters the close command through the repeated-command rule while reading no input, and any character `Number()` cannot read -- a stray `)`, `;` or `:` -- which it reports as 0 without advancing. Exactly what a half-typed path looks like. The parser now requires every pass to consume something and otherwise stops and reports where, as SVG specifies for a malformed path. Reported by a reader of the source; confirmed by running the unguarded parser with a timeout and watching it not return.
- Fixed: **`rx=[0,8,8,8]` made every corner sharp instead of one.** The square-shape early-out read `node.Rx`, which the parser sets to the TOP-LEFT corner when per-corner radii are given, so a single zero discarded the other three. A rectangle is now only drawn square when every corner is zero.
- Changed: **`gray` is accepted as `grey`.** Unity's table carries only the British spelling, so the American one drew magenta with no other clue. It was documented as a trap in 0.11.65; documenting an unambiguous mistake is not fixing it.
- Changed: **every colour now goes through one parser**, so the whitespace trim added in 0.11.66 applies everywhere rather than only to `data`. `f=" #FFFFFF "` in scene text used to fail while the same value in a payload worked. Six call sites -- fill, stroke, outline, shadow, gradient stops and text -- previously called Unity's parser directly.

## 0.11.69

- Fixed: **a colour that would not parse made the shape silently invisible.** `f="notacolour"` left the fill unset and reported nothing, so a typo produced a missing shape with no entry in `vector_stats` and no log line -- the hardest kind of mistake to find. A fill or stroke whose colour will not parse now reports a problem and draws the same magenta an unresolved data colour and an undeclared gradient id already draw. Found by the control row of a test page that was meant to prove the opposite.

## 0.11.68

- Docs: `REFERENCE.md` now lists **all 23 named colours with their values**, read from the engine binary and derived twice by different routes. Every value is the W3C one; `darkblue` is `#00008B`. Two traps worth knowing: `green` is `#008000`, not `#00FF00` (that is `lime`), and `aqua`/`cyan` and `fuchsia`/`magenta` are each the same colour under two names.

## 0.11.67

- Docs: corrected the colour-name table. **`transparent` is one of Unity's own 23 names**, not this mod's addition -- only `none` is ours. `InGameTest-colourstrict.lua` could not have shown this, because the data path short-circuits both names before Unity's parser sees them; the engine binary settles it. The full list is now in `REFERENCE.md`, and it confirms from a second source that `gray` is absent and only `grey` exists.

## 0.11.66

- Fixed: **a data colour with leading or trailing whitespace was rejected and drew magenta.** Unity's parser does not trim, so `" #FFFFFF "` failed where `"#FFFFFF"` worked. A value arriving with stray space -- from a concatenation, a text field or a copy-paste -- plainly means the colour inside it. `transparent` and `none` are trimmed on the same path.

## 0.11.65

- Docs: `REFERENCE.md` now lists **which colour strings a `data` value may be**, measured on a console rather than taken from Unity's documentation. The one that catches people: `grey` is accepted and **`gray` is not** -- Unity carries only the British spelling, so a page written in American English gets a magenta shape with no other clue. Also rejected: a bare `FFFFFF` with no `#`, a malformed length, non-hex digits, `rgba()`/`hsl()`, and anything with surrounding whitespace.

## 0.11.64

- **Changed: a reference to an undeclared gradient now draws magenta instead of white.** It was already reported as a problem and still is; what changed is that the shape now carries the same signal an unresolved data colour does, because white is a colour somebody meant to use and magenta is not. A scene with a dangling `@id` is not working, so this reads as the fault it is rather than as a design decision. An undeclared **clip** id is unchanged: the reference is ignored and the content draws unclipped.

## 0.11.63

- Fixed: **a `units=bbox` gradient on a stroke was never bound to the shape's box.** Fills, bands and text bound theirs; strokes did not, so the ramp was sampled in scene space instead -- and a shape whose fill and stroke shared one gradient drew them in two different spaces. The box used is the contour's, not the stroked band's, so a fill and a stroke with the same gradient line up. Confirmed by disabling the fix and watching a red-to-blue bbox ramp on a shape at x=120..180 come out uniformly blue.
- Docs: four more claims corrected against the parser. `if()` is **lazy** -- only the branch taken is evaluated, so the other may safely divide by zero or read a missing name. A `nodes` patch re-parses the **whole merged subtree**, a patch carrying `c` replaces children rather than merging, and a descendant re-parsed that way keeps only whitelisted inherited keys; patching a group is not free for what it holds. `sat` does nothing on a `T`, since text is never stroked.

## 0.11.62

- Added: **`ver`**, an expression variable carrying the running mod's version as `major*10000 + minor*100 + patch` (0.11.62 is `1162`). It is usable anywhere an expression is. The degradation is the feature: on a mod too old to know the name, the expression fails to parse and the attribute falls back to **its own default**, so `G v="=lt(ver,1162)" { ...banner... }` shows an "update the mod" notice on exactly the versions that cannot draw what follows, and hides itself on the ones that can. The parse failure is also reported as a problem, which is a diagnostic rather than a fault.

## 0.11.61

- Added: **`Tessellator.TagNodes`**, off by default, records which node produced which vertices (`Tessellator.NodeSpans`, a `First`/`Count` range per node). A group's span encloses its children's, so the smallest span containing a point is the innermost node under it. It exists for editors and previews that need click-to-select over a built mesh; in game it is never switched on and costs one bool test per node.
- Fixed: **`x % 0` returned NaN instead of 0.** The guard sat on the constant divisor of an inner `Divide(a % b, 1f)` rather than on `b`, so a modulo by zero produced NaN -- and a NaN reaching a coordinate silently poisons every vertex derived from it. Now 0, matching `/` and what the docs promise.
- Fixed: **`fmt` with `%x` or `%X` rendered the `missing` text.** .NET refuses hex formatting on a float and the `FormatException` was swallowed. A value destined for hex is an integer by intent, so it is now rounded and retried before giving up.
- Docs: several claims in `REFERENCE.md` and `QUICKSTART.md` over-promised and have been corrected against the parser -- `SP` is stroke-only (no fill, no shadow, no inset), `blur` on a group covers `R` and `C` only, a `style` on the scene ROOT is not read (use an outermost `G`), the shadow ring cap is 96 x `BlurDensity` rather than 24, and `pi`/`tau` are zero-argument functions needing brackets. The clip problem message now lists `L`, which the parser has always accepted.

## 0.11.60

- **Changed: gradient stops interpolate in premultiplied alpha, as CSS does.** A fully transparent stop now contributes its alpha and none of its colour, so `#5FD9A8FF → #00000000` fades exactly like `#5FD9A8FF → #5FD9A800` instead of greying through the middle. This is a visible change to any scene that deliberately ramped to transparent black: those fades keep their hue now and will look lighter in the middle. The workaround the docs used to prescribe -- fade to the same colour -- is no longer needed and has been removed from `README.md` and `QUICKSTART.md`.
- Fixed: **`inset` written as a Lua boolean in a shadow's 6th field was silently ignored.** ScriptedScreens' `UiValue.FromBool` sets its `Bool` field and leaves `Number` at 0, and the test read `Number`, so only the string `"inset"` or a number ever worked.
- Fixed: a `weight` of `"700"` was parsed with the current culture rather than the invariant one. Author text is not culture text.

## 0.11.59

- Added: **`kern` on a `T` node.** `kern=0` turns pair kerning off for that label; absent or `1` leaves it on, which is how a browser behaves -- a page opts out, never in. It is inheritable through a group's `style` like the other text keys. Only a font carrying kerning pairs is affected, so a label on the game's own font renders the same either way.

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
