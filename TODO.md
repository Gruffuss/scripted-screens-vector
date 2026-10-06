## A page cannot ASK for a font, only name one that is already there

`font=` on a `T` looks a face up by name, so a page can only use faces the fonts mod has already
registered — the ones a player dropped in the fonts folder, plus the game's own. The fonts mod can
also fetch a font on request, and that capability is reachable only from mod code today, not from
a page.

What is wanted: a page declares the font it needs **in its Lua props, the way it declares
everything else here**, and the mod does the asking — no C# on the author's side. Something of the
shape `fonts = { "<a font link>" }` on the element or in the scene header, with the labels drawing
in the fallback face until the faces arrive and a rebuild when they do, since the fetch is
asynchronous and returns the face names it registered.

Constraints that are not negotiable when this is built:
- The fonts mod decides which hosts may be fetched from, and its default is deliberately narrow.
  This must go through that setting and must not widen it — a font file is parsed by native code on
  every player's machine, so "any CDN" is not an option.
- A font that never arrives, a link that is refused, and a face name that does not match what
  arrived must each be REPORTED, not silently fall back. Everything else in this format reports a
  mistake the author made.
- It is an addition: a page that names no font behaves exactly as it does now.

**The fonts mod's side is answered, from its source (2026-10-06). Do not re-derive this:**

- `RequestFont(link, done)` — **the signature will not change, and it will never gain an
  overload.** Both consumers resolve it with `GetMethod("RequestFont")` by name alone, so a second
  overload would throw `AmbiguousMatchException` at reflection time, breaking both at once with no
  compile error anywhere. A richer call would get a DIFFERENT NAME instead.
- **The two failures arrive differently, and a page must handle both.** A refused request —
  malformed, not http(s), a host outside the allowed set, loader not running — returns **`false`
  synchronously and never calls back**, so the return value must be read; waiting for a callback
  there waits for ever. An accepted request that loads nothing — download failed, file switched
  off, face cap reached — **calls back with an EMPTY array**. That is the "never arrived" signal.
- **Accepted means exactly one callback, always.** Asking this question found a hole on their side:
  the loader waited without limit for conditions a request cannot influence, so a callback could
  simply never arrive and a page could not tell "still loading" from "never coming". That wait is
  bounded now and gives up through the same delivery path. Fixed on their side the same day.
- **The callback runs on the main thread**, from a coroutine, so a scene may be rebuilt directly
  inside it.
- **A label already drawn keeps the face it resolved to**, so rebuilding when the callback lands is
  not an optimisation, it is the mechanism that makes the font appear at all.
- **The names that come back are the font's own** (`Manrope`, `Manrope Bold`), NOT derived from the
  link — so an author cannot know what to write in `font=` without loading it once. Checking the
  author's name against the array the callback delivers, and reporting a mismatch, is the answer to
  that, and is why that report is on the list above rather than optional.

Still unanswered, and deliberately not asked yet: an empty array does not say WHY nothing loaded
(the reason is only in the log). The fonts mod offered to add a reason if this needs to tell a
failed download from a disabled file — worth asking when this is actually built, not before, since
it is their work for a feature that is not scheduled.

Unanswered here: whether a request belongs per element or per scene, and what a page should do
about a face that arrives after a capture has already been taken.

## The work budget's figure is measured on .NET 8, not on Mono

0.11.102 replaced the silent 20,000 cap on a repeat's `n` with a per-rebuild work budget of about
a million units (one per repeat iteration, one per `YS`/`LS` sample, 25 per label). Every figure
behind that number was measured in the offline probe, which is .NET 8; the game runs Mono and is
slower, so the budget may be looser there than intended. What to measure in game: a repeat of a
hidden leaf at `n = 1000000` and a nest at `n = 900` x `900`, both of which should draw, against
the frame time -- and whether 38,000 labels is a figure TextMeshPro can actually realise, which
nothing outside the player can answer.

## `so` inherited INTO an SC still becomes its scroll offset

0.11.92 stopped an `SC` passing its own `so` down as stroke opacity. The reverse is still open: a
stroke `so` inherited from an enclosing group lands on an `SC` that declares `sov`, and is taken
as the scroll offset. Reaching it needs the parse to tell a node's OWN keys from its inherited
ones. **That part is already available**: `ParseNode` takes an `own` map -- the author's own
props, before the inherited defaults were merged in -- which the `SC` scroll offset and, since
0.11.102, the enum-word check both read. What remains is to read `so` from it in the `SC` arm.
Reported from a source reading; not yet reproduced in game.

## CLOSED: a scene with no `T` captured blank (2026-10-05, fixed in 0.11.83)

**Cause: the capture canvas does not upload `TEXCOORD1`, and the dither shader multiplies alpha
by it.** `UIDither` carries a blur's coverage in TEXCOORD1 and the fragment does
`color.a *= IN.coverage.x`. A UGUI canvas uploads ONLY the vertex streams named in
`Canvas.additionalShaderChannels`; everything else arrives as zero. ScriptedScreens' capture
builds its own `[ScriptedScreens-McpCaptureCanvas]` and never sets that property, so coverage
arrived as 0 and every vector pixel drew at alpha 0. The clone was never wrong -- it rendered
exactly what it was told, fully transparent.

**Why text looked like the discriminator:** TextMeshPro sets `TexCoord1 | Normal | Tangent` on
whatever canvas its labels sit on. Any `T` in the scene turned the channel on for the capture
canvas and the geometry rode along. The mod had been free-riding on TMP since the dither
material shipped. Live consoles were never affected: the game's own surface canvas already has
the channel.

**The fix** is `VectorGraphic.EnsureCoverageChannel()`, called from `UpdateGeometry`: it ORs
`TexCoord1` into its canvas's channels once per canvas. It closes a class rather than a symptom
-- any canvas this mod ever lands on without a TMP label would have drawn nothing, silently.

**The lesson, which cost nine wrong hypotheses:** every one of them asked "why is the geometry
missing?" The geometry was never missing. A blank clone and a working one printed IDENTICAL
component state -- mesh, material, cull, clip, alpha, layer, canvas -- and the only field that
differed was the canvas's channel mask. When two cases are indistinguishable in everything you
have looked at, the answer is in something you have not looked at; dump both sides and diff them
rather than forming another hypothesis about the side you can see.

# Open items

Things found and measured but deliberately not done. Each says what was measured, what the
fix is, and why it was left — so picking one up does not mean rediscovering it.

---

## Circle clip coarse in a capture of an unwatched console (seen 2026-09-15)

`InGameTest-holes.lua` on a 1x1 console: captured while stats said `on screen size unknown`, the
`CP { C rx=38 }` clip came out a hexagon; in the player's view it is round. Unknown size should
mean full quality. Cause not traced: arithmetic with the fallback scale of 1 gives 48 segments,
not 6, so something else supplies the scale on that path. Capture-only; play is unaffected.

---

## Shadow ring density — halved in 0.11.19.0

One ring per four screen pixels of reach instead of two. Checked offline, not by arithmetic:
the page's `4 6 10` box shadow rendered through the real tessellator at its viewed size
(4.76 px per unit) against a 400-ring reference.

| rings | vertices (incl. box) | max error | mean error |
|---|---|---|---|
| per 2 px (old) | 1,240 | 5.3/255 | 0.011/255 |
| per 4 px (now) | 976 | 12.0/255 | 0.016/255 |

The max errors are isolated edge pixels; the error map is black even amplified sixteen times.
That shadow sits at the 24-ring cap, so it saved ~25%; below the cap the saving is half.
Still wants one look in game at close range, since the reference is the model, not a screen.

---

## `vector_stats` mixes two moments in one line

The vertex figure is a peak over the reporting window while the pixel figure is from the last
rebuild, so a line can read "212,784 verts ... 497 px" after walking away from a console —
smaller on screen but more geometry, which is backwards and makes the table read wrong.

Either report both from the same rebuild, or label the vertex figure as a peak.

---

## Text in draw order shipped as `ztext`; the overlap test is the part to watch

Done in 0.11.11.0. A `T` forces a mesh cut only where a later shape actually overlaps its box,
so the common page keeps one mesh and one draw call. `TextOrderTests` pins that: thirty
labelled tiles stay in one mesh, and reverting the overlap test to "always true" turns them
into thirty, which is the failure this design existed to avoid.

**What is still worth measuring in game**, and was not: a page that genuinely does overlap —
the HTML mod's `z-index` cases — has never been looked at for draw-call count. The theory says
one extra draw call per covered label. If a real page ever feels heavy with `ztext = 1` on,
`vector_stats` reports `across N meshes` and that is the number to read.

**Resolved in 0.11.21:** a label covered by the scene's first shape now goes under it. The
surface gets an empty slice 0 only in that case, so other scenes pay nothing.

---

## Capture logging could say whether pixels arrived

The HTML mod's capture logs sample the result — centre and corner pixels, plus layout sizes —
so a blank capture says which half failed. This mod logs the vertex count it built, which
answers "did geometry exist" but not "did it reach the picture".

Not needed while captures work. Worth copying if one ever comes back blank with a healthy
vertex count, because that is exactly the case the current line cannot distinguish.

---

## Why dense pages cut more than hand-built ones — answered

Found from the HTML mod's scene dump (`scenes/page.txt`) run offline through the real
tessellator and `TextOrder`. The test page had 12 cuts (13 meshes):

| cuts | cause |
|---|---|
| `z 1`, `z 2` | **genuine** — z-index boxes really overlap those labels |
| `one`, `two`, `five`, `t33`..`t37` | the label box is **wider than its text** (the HTML emitter's slack, e.g. `one` is 279 wide on a 201 tile) and runs into the next tile |
| `PAINT` | the 845-wide heading box touched the **faint outer edge of a box shadow** |
| `click me` | flush boxes: shape bounds included the **feather ring**, so touching boxes overlapped by a feather's width |

Fixed in 0.11.19.0: vertices at alpha <= 2 no longer count toward shape bounds (feathers and
shadow tails cannot cover text), and boxes touching within 0.25 canvas units do not overlap.
The page is now 10 cuts, 11 meshes.

**Left alone deliberately:** the eight slack cuts. They change nothing on screen and cost a
fraction of a millisecond of upload. Removing them needs the text's real width, which only
TextMeshPro knows and only on the main thread after the label is built — a two-pass design
(measure, then rebuild once) that is not worth it for an invisible cost. Revisit only if mesh
count ever matters.

---

## Text shadows larger than the font's padding

Since 0.11.19.0 a shadow with an OFFSET that does not fit the glyph quad is drawn by an offset
copy of the label, so only blur and spread count against the SDF padding. On the game's
LiberationSans (sampling / gradient scale measured at 8.65 from the in-game warnings) the test
page's `2 2 3` headings went from 43% to whole.

**Still capped: glows.** A `0 0 6` glow on 14-point text needs 3.0 units of reach against a
1.62-unit budget and stays at 54%, reported under TEXT in `vector_stats`. Nothing on the vector
side changes what the distance field holds. The fixes are a smaller blur on the page, or a font
with more padding: the fonts mod builds its atlases at padding 5 (`AtlasPadding`), which is
*less* than LiberationSans, so raising it would cost atlas space the 272-character set nearly
fills at 48pt. Not attempted.

## One dark pixel inside a solid fill, in captures (seen 2026-09-23, not chased)

`InGameTest-hidden.lua` on the 2x2 console: the control bar (`R x=8 y=13 w=84 h=14 rx=3
f=#5FD9A8`) has exactly **one** off-colour pixel, at capture (166, 47), in both of two captures
1.5 s apart. It is **(3, 3, 2, 255)**: opaque and near black. Not the page background showing
through a crack (that reads (13, 13, 22)), and not a transparent hole (alpha 255), so something
**drew** it. Nothing in the scene is declared at that point (scene ~(71, 18)).

Not yet known whether it is capture-only or also on the console, or whether it appears on other
fills. First checks: capture the same bar on another console; move the bar by a pixel and see
whether the speck moves with it (geometry) or stays (capture or overlay).
