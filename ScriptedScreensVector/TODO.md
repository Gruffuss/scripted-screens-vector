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

## Capture logging could say whether pixels arrived

The HTML mod's capture logs sample the result — centre and corner pixels, plus layout sizes —
so a blank capture says which half failed. This mod logs the vertex count it built, which
answers "did geometry exist" but not "did it reach the picture".

Not needed while captures work. Worth copying if one ever comes back blank with a healthy
vertex count, because that is exactly the case the current line cannot distinguish.
