## Open: a scene with no `T` captures blank (2026-10-05, 0.11.81)

**Reproduction, deterministic.** Push any of examples 01-07 to a console and capture: 7,327 bytes,
one flat image of the ScriptedScreens panel behind the scene, byte-identical across all seven.
Push 08-18 and the capture has content. Add ONE `T` node to `01-hello` and its capture goes from
7,327 to 72,329 bytes. The split is exactly "has a text node" -- 01-07 have none.

Nothing is wrong with the scenes. They draw correctly on a console, `vector_stats` reports the
shapes and verts, and the mod's own log says `vector capture: "hello" built inline, 568 verts
across 1 mesh(es)` for the blank ones. The geometry is built; it does not reach the picture.

**What the capture-dump tree shows.** A page with text has `VectorSlice` children under
`VectorSurface`; a page without has none, because `ApplySlices` only creates children for slices
1..n and slice 0 rides on the graphic's own `CanvasRenderer`. Text is what splits a scene across
slices, which is why it correlates.

**THE DECISIVE FACT (2026-10-05, logged):** the capture clone's `UpdateGeometry` DOES run, and it
DOES present the right mesh -- `vector capture: clone presented "?" with 568 verts`, matching the
`built inline, 568 verts` from the same capture. So the clone exists, shares the mesh (`_mesh` is
already `[SerializeField]`), and hands over correct geometry. The picture is still blank. Whatever
is wrong is NOT that the geometry is missing from the clone.

**Five fixes tried, none worked** -- so the structural explanation above is not sufficient on
its own, and the cause is more likely to be WHEN the clone is taken than WHERE the mesh sits:

1. Marking every live surface dirty in the capture prefix, so `UpdateGeometry` is called.
2. Leaving the clone's renderer untouched when it has no scene (`_mesh` is already
   `[SerializeField]`, so the clone does share the mesh -- that was not the gap).
3. Putting every slice on a `VectorSlice` child during a capture, including slice 0.

4. Setting the clone's MATERIAL as well as its mesh (Instantiate copies neither). The clone had
   no material; giving it one changed nothing.
5. `maskable = false` on the clone, in case the surface's `RectMask2D` was clipping a correct
   mesh away because the mask's clip rect is pushed during a canvas update the clone misses.
6. Presenting mesh and material from `OnEnable` instead of `UpdateGeometry`, in case the
   presentation was simply landing after the capture had read its picture.

**What that leaves.** The clone we can see and touch is given the right geometry, a material and
no mask, at enable time, and the render still shows nothing -- while a scene with one text node in
it renders. The remaining explanations are about ScriptedScreens' own capture, not about this
graphic: either the object that is rendered is not the one we are presenting to, or the render
happens before any of our hooks. Both are answerable only by reading what the host actually does.

**THE HOST'S CAPTURE, read from the decompile (`ScriptedScreensScriptableUiSystem.cs`).** This is
the sequence, and it rules out most of what was guessed:

```
RebuildSurfaceFromModel(...)          // recreates the hosts: new graphics
Canvas.ForceUpdateCanvases()          // our inline build runs here
clone = Instantiate(surfaceRoot)      // the clone
StripCaptureOnlyComponents(clone)     // destroys forwarders, raycasters, audio, colliders --
                                      //   NOTHING of ours
ConfigureCaptureClone(clone, camera)  // stretches the root, repoints canvases at the capture
                                      //   camera, then ForceRebuildLayoutImmediate on EVERY
                                      //   child RectTransform
Canvas.ForceUpdateCanvases()          // a SECOND rebuild pass, clone included
camera.Render()                       // ARGB32, no HDR, no MSAA
ReadPixels -> PNG
```

So the clone DOES get two rebuild passes before it renders, `Capturing` is still true through
both, and nothing of ours is stripped. A capture is a camera render of a cloned tree, not a
read-back of the live screen.

**EIGHT hypotheses, eight wrong** (1-3 above, then):

4. Setting the clone's MATERIAL as well as its mesh -- Instantiate copies neither. No change.
5. `maskable = false` on the clone, in case the surface's `RectMask2D` clipped a correct mesh.
6. Presenting from `OnEnable` rather than `UpdateGeometry`, in case presentation landed late.
7. Giving the clone its OWN copy of the mesh, since `_mesh` is `[SerializeField]` and the clone
   shares the instance -- the live graphic builds again during those two extra passes and could
   have been emptying the very mesh the clone renders. No change.
8. The `CanvasUpdateRegistry` refusing the clone's material request inside the rebuild loop,
   which is exactly why `VectorSlice` overrides `SetVerticesDirty`/`SetMaterialDirty`. The log
   has ZERO such refusals, so it is not happening.

**What is still true and unexplained:** the clone is handed the right mesh (logged, right vertex
count), with a material, unmasked, at enable time, through the same `canvasRenderer.SetMesh` call
that `VectorSlice` uses successfully in the same clone -- and renders nothing, unless the scene
contains a text node.

**The one experiment not yet run:** make `VectorGraphic` present through UGUI's own
`OnPopulateMesh`/`VertexHelper` path for a clone, the way the `Image` that DOES render in the
same clone does, instead of `SetMesh`. It is the only mechanism left that differs between the
thing that works and the thing that does not.

**Superseded, kept for the record:** read
`TryCaptureSurfaceShared` in `decompiled/ScriptedScreens/` and establish, from its code, what it
clones, what it renders and in what order. Every hypothesis above was formed from the outside and
five of five were wrong; the host's source will say in minutes what a day of probing has not.

**Workaround for anyone needing a capture now:** put a text node in the scene, even an empty one.

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
