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

## Text cannot be covered by a shape — needed for CSS z-index

**Wanted by the HTML mod**, which is implementing the full CSS stack, so this is a
requirement rather than a nicety: `z-index` on a box that overlaps a label has to be able to
put the box on top, and today it cannot.

**Why.** Labels are TextMeshPro objects parented beside the geometry, not part of the mesh.
UGUI draws children in sibling order, so the whole text layer draws either entirely before or
entirely after the whole mesh. A label belonging to a node early in the scene still draws with
every other label. "On top" and "underneath" are the only two states, and they apply to all
text at once.

**The fix, and the machinery already exists.** The mesh is already cut into slices at shape
boundaries (`MeshBuilder.MarkShape`, `VectorSlice`), and cuts already fall between shapes,
which is exactly the constraint a text node needs. So:

1. A `T` node forces a cut at its position in draw order, the same call `MarkShape` makes.
2. `TextPlacement` records which cut it falls after.
3. `TextLayer` parents each label as a sibling at that index, so children read
   `slice 0, labels before cut 1, slice 1, labels before cut 2, …`.

Shapes then cover labels and labels cover shapes according to scene order, which is what the
scene already means everywhere else.

**Costs, stated honestly.** A scene interleaving text and shapes gets one mesh per run between
labels rather than one per 60,000 vertices, so a page alternating label and box ends up with
many small meshes and a draw call each. Worth measuring before shipping — a page of thirty
labelled tiles could go from one draw call to sixty. A mitigation exists if it bites: only cut
where a label actually **overlaps** a later shape's bounds, which is rare, and leave
non-overlapping text in one layer as now.

**Do not start this without the overlap test.** The naive version is simple and will be slow
on exactly the pages the HTML mod generates.

---

## Capture logging could say whether pixels arrived

The HTML mod's capture logs sample the result — centre and corner pixels, plus layout sizes —
so a blank capture says which half failed. This mod logs the vertex count it built, which
answers "did geometry exist" but not "did it reach the picture".

Not needed while captures work. Worth copying if one ever comes back blank with a healthy
vertex count, because that is exactly the case the current line cannot distinguish.
