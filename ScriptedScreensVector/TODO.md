# Open items

Things found and measured but deliberately not done. Each says what was measured, what the
fix is, and why it was left — so picking one up does not mean rediscovering it.

---

## Shadow ring density is inherited from colour gradients and is probably twice what it needs

**Measured, 40 cards at 1395 px, one `sh` each:**

| | vertices per card |
|---|---|
| with one box-shadow | 640 |
| no shadow (control) | 64 |

So a single box-shadow is **576 vertices, nine times the card it sits under**, and on a page
of tiles it is where the whole budget goes. The 2x2 console was measured at 91 vertices per
shape for a page of boxes; a filled rectangle is four.

**Where the number comes from.** `Shadow.Emit` picks its ring count as roughly one ring per
two screen pixels of blur reach, clamped 4..24. At that size it resolved to 16 rings of the
card's 32 outline points.

**Why it is probably too fine.** That density was inherited from the radial gradient rule,
where the visible failure is banding across a saturated colour ramp. A shadow is a dark
translucent ramp against a background, where a step of the same size is far harder to see —
the eye is being asked to resolve a few percent of alpha, not a hue change. Halving it is one
constant.

**How to check it properly.** Push two scenes to a console from one spot: the same card grid
at the current density and at half, and look. The probe scenes used for the feather
measurement are the right shape for it. Do not decide this from arithmetic — the record on
predicting what is visible in this renderer is poor, and the feather turned out to matter
precisely where it looked like it did not (dense overlapping fields, not arranged grids).

**Why it was left.** Nothing is being dropped any more now that a surface can span several
meshes, so this is a cost question rather than a correctness one. It is worth doing, it is
just not urgent.

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

## Capture logging could say whether pixels arrived

The HTML mod's capture logs sample the result — centre and corner pixels, plus layout sizes —
so a blank capture says which half failed. This mod logs the vertex count it built, which
answers "did geometry exist" but not "did it reach the picture".

Not needed while captures work. Worth copying if one ever comes back blank with a healthy
vertex count, because that is exactly the case the current line cannot distinguish.

---

## `ztext` cuts more on HTML pages than on hand-authored ones, and nobody knows why

Measured in game on 0.11.12.0, with text in draw order on by default:

| scene | shapes | meshes |
|---|---|---|
| `12-click.lua` (hand-authored) | 19 | 1 |
| `html:page` (HTML mod) | 48 | **13** |
| `html:gas` (HTML mod) | 754 | **9** |

**This is not a performance problem and was wrongly presented as one.** The extra upload is
0.06 -> 0.13 ms on `html:page` and 0.34 -> 0.71 ms on gas — sub-millisecond, against a 16.7 ms
frame. Tessellation is unchanged, because it is the same geometry handed over in more pieces.

**What it does mean is that the prediction was wrong.** "A cut is close to never" held for
scenes a person placed and not for scenes a layout engine generated, which is exactly the
population the original design note warned about.

**The suspicion, untested:** the HTML emitter gives a label slack on the side its alignment
allows, so TextMeshPro does not wrap a shrink-wrapped box. That makes a label's rect wider than
its text, and the overlap test uses the rect. Spurious overlaps with neighbouring tiles would
follow, and would scale with page size rather than with real occlusion.

**How to settle it:** log which labels force a cut, and against which shape, under
`Diagnostics.Enabled`. One line per cut names the label text and both boxes, and the answer is
either "the slack" or "genuinely overlapping boxes" in a single look. Do that before changing
the overlap test — the record on guessing about this renderer is poor, and the cheap wrong fix
(shrinking the test box) would reintroduce labels drawing over things that really do cover them.
