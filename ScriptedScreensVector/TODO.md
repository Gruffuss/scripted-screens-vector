# Open items

Things found and measured but deliberately not done. Each says what was measured, what the
fix is, and why it was left — so picking one up does not mean rediscovering it.

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

**Known limit, by design:** a label declared before any shape cannot be put underneath,
because the surface's own renderer always draws before its children. It stays on top. Fixing
that means giving the surface an empty slice 0, which costs a draw call on every scene to
serve a case nothing has asked for yet.

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
