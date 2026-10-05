# Reference sweep — working list

The 59 items from the VectorBuilder sweep against v0.11.91, plus what today's work left open.
Kept updated as each lands. Started 2026-10-05.

**Buckets:** `CODE` the mod is wrong and I fix it · `DOC` the reference is wrong and I correct it ·
`THEIRS` the report misread the source · `FEATURE` neither is wrong, it is new work.

**Not in scope here:** the intermittent push/load failure and the blank first capture after a
push. Being tested separately, at the owner's instruction.

---

## Status

| | done | left |
|---|---|---|
| Part A (code vs doc) — the CODE half | **14 of 14** | 0 |
| Part A — the DOC half | 0 | 7 |
| Part B (doc silent or vague) | 19 | 20 |
| New code faults the sweep itself found (C1–C11) | 0 | 11 |
| Carried over from today | 3 | 3 |

**Every code fault Part A named is fixed and measured.** What is left in Part A is wording.

---

## Part A — the code and the doc disagree

- [x] **A1** a `nodes` patch never rebuilds children — **DOC**. Children are never patched; a `c`
      in a patch is dropped. Reference rewritten, `ec13b92`.
- [x] **A2** `T` and `IMG` never register a hit area — **DOC** then **FEATURE**. The doc claim was
      wrong (they never did, not even at v0.11.66, and the list also omitted `LS`, which does);
      corrected in `ec13b92`, then the behaviour itself was built in 0.11.93.
- [x] **A3** `so` on an `SC` becomes the stroke opacity of everything inside — **CODE**. Fixed in
      0.11.92, measured before and after and seen on a console. Second half still open below.
- [x] **A4** in `src`, only `=` values are read to whitespace — **CODE**. One unquoted
      `text=$rows[i]` rejected the WHOLE scene; a `$` binding now reads to whitespace like an `=`
      expression, which is what the reference already promised. Inside an array the ordinary rule
      stays so `p=[$a,$b]` still splits on the comma. 0.11.93, measured both ways.
- [x] **A5** `weight` given as a number is ignored — **CODE**, 0.11.94. Bold `False` -> `True`.
- [x] **A6** `fmt` is not the printf spec, and an unreadable spec does not render `missing`
      — **CODE** for both halves, 0.11.94: `%%` collapses (`50%%` -> `50%`) and `%x` rounds and
      guards NaN (`fe` -> `ff`). The rest of A6 is the DOC half below.
- [ ] **A7** an open `L`, `LS` or `SP` with `f` is filled — **DOC**, wording pending.
- [x] **A8** a `P` with `sh` but no `f` casts no shadow — **CODE**, 0.11.94. 0 -> 1361 verts,
      identical to the `Y` control.
- [ ] **A9** `ry` never shapes a corner on `R`, `SC` or `IMG` — **DOC**, wording pending. See also
      **C4**: on `IMG` the per-corner list is a code omission, not just a doc one.
- [x] **A10** a broken colour-filter value falls back to 1, not to the filter's identity
      — **CODE**, 0.11.94. Amount 1.00 -> 0.00, still reported.
- [x] **A11** an enclosing group's `blur` is lost inside an `SC` — **CODE**, 0.11.94. 4 -> 3333
      verts.
- [ ] **A12** many numeric keys take a literal number only — **DOC**, wording pending.
- [ ] **A13** stroke width under an uneven `m` is not `sqrt(|ad - bc|)` — **DOC**, wording pending.
- [x] **A14** a missing numeric data array is not reported — **CODE**, 0.11.94. `Missing` `[]` ->
      `[nosuch]`, matching the label control.
- [x] **A15** a malformed gradient number falls back to 0, not to its default — **CODE**, 0.11.94.
      `r` and `y2` both 0.00 -> 1.00.
- [x] **A16** defs skip the unknown-attribute check — **CODE**, 0.11.94. Reported nothing ->
      `GL "g": unknown attribute "nosuchkey"`. **Changes an existing scene:** one carrying a typo
      on a def now shows the problem border.
- [ ] **A17** `unit` is ignored on a plain literal `text` — **DOC**, wording pending. (Hit this in
      passing building the 0.11.94 page: a numeric `text` renders no label at all, so `fmt` on a
      literal number cannot be demonstrated either.)
- [ ] **A18** `if()` is not lazy in a deep expression — **DOC**, wording pending.
- [ ] **A19** `noeval` and `nofill` do less, or more, than the table says — **DOC**, pending.
- [x] **A20** `d` rejects compact SVG numbers — **CODE**, 0.11.94. A second `.` ends a number
      (`L60.5.5` box `(-1.87,78.13)-(121.87,201.87)` -> the control's exactly) and an arc flag is
      one character (`a20 20 0 011 40` 0 verts -> the control's 138, same box).

## Part B — the doc is silent, vague or contradicts itself

**All 39 are answered**, with exact replacement wording, in
`scratchpad/b21-b39-answers.txt` (B21–B39) and the earlier agent reports (B1–B20). What is left is
applying that wording to `REFERENCE.md` and `README.md`. None of the 39 is itself a code defect;
the code faults the answering turned up are C1–C11 below.

- [ ] **B1–B20** — the agents' exact wording was NOT persisted and is lost to compaction. Must be
      regenerated before it can be applied. The questions themselves are listed below.
- [x] **B21–B39 APPLIED**, `76ccbdc`: 31 corrections to REFERENCE.md and one to README.md. Five
      of the agent's quoted anchors did not exist as quoted (the file moved under it), so every
      anchor was re-read from the live file first, and the applier asserts a single match before
      writing anything. Every positive claim the new text makes was measured first: all nine
      clickable ops register one hit and `YS` registers none, `GR r=0` stores 0.0001, and an
      `IMG` carrying `f`/`s`/`sw`/`sh` draws the same 4 verts as a bare one.
- [x] drop from the B31 sweep: `LS` is already in the clickable list, and `T`/`IMG` are documented
      as clickable from 0.11.93

## New code faults the sweep itself found

Not in the builder's 59. Each is mine, found while answering their questions; none is fixed.
**Planned order, told to the builder 2026-10-05:** 0.11.95 takes C2, C5, C6, C7, C8, C9 (silence
becomes a report -- same character as A16: a scene that drew clean starts showing the problem
border, no correct pixel moves); 0.11.96 takes C1, C3, C4, C10 (pixels move). C11 rides along.
**C3 is the only one that can change a scene that is currently correct**, so it gets its own
before/after measurement and its own changelog note.

- [ ] **C1** `^` is raw `Mathf.Pow`, so `0^-1` is infinity and a negative base with a fractional
      exponent is NaN, while `/` and `%` are both guarded for the stated reason
- [ ] **C2** `sd` and `sdo` are in `KnownKeys` and **nothing reads them** — the only two in the
      whole list; removing them makes `sd=[4,2]` report instead of silently drawing solid
- [ ] **C3** an `LS` evaluates `s` and `sw` outside any repeat frame, so `i` there means the
      enclosing repeat while in a `YS` it means 0 — against `EmitBand`'s own stated aim
- [ ] **C4** `IMG` ignores the per-corner `rx` list and draws square corners in silence
- [ ] **C5** `ParseShadow` drops a shadow with a bad colour, or under 5 parts, silently
- [ ] **C6** `ClipOutline` parses a `CP`'s shape against a throwaway scene, so a typo inside a
      clip is never reported
- [ ] **C7** `mask` without a leading `@` is ignored silently, where `clip` takes the bare id
- [ ] **C8** `src` present but rejected, with `root` also present, silently draws `root`
- [ ] **C9** malformed gradient stops are dropped silently, a no-stop gradient paints white with
      only a log line, and a `$name` stop colour the payload lacks is not listed as unresolved
- [ ] **C10** `fl weight=normal` cannot un-bold a bold label's first line
- [ ] **C11** four stale code comments naming limits that no longer exist
- [ ] **C12** unknown keys in the **SCENE header** are ignored without a report, where the same
      key on a node is reported. Measured: header `[]`, node `R: unknown attribute "..."`.
      (The clip-shape half of the same report is already **C6** — the throwaway scene.)
- [ ] **C13** a `CP` holding several shapes uses only the first and drops the rest in silence.
      Measured: a two-shape `CP` draws identically to a one-shape one, verts 4 and 4, 0 problems.
      **A union is not free**: `Frame.Pieces` already draws a node once per clip region, and a
      scroll viewport split uses it, but drawing once per region equals a union only for
      DISJOINT regions — overlapping ones would double-composite, which shows through
      transparency and feathering. Report the extras; leave a true union unbuilt.

- [ ] **C14** an `IMG` whose picture has NOT downloaded yet records no hit area, because the hit
      is recorded after the texture check in `EmitImage`. A clickable picture is therefore dead
      until it loads, and silently so. Measured: the same page gives `hits=0` with no texture and
      `hits=3` (`pic`, `pic2`, `pic3`) with one. Found while building `InGameTest-imgclick.lua` —
      the page's own first offline run read 0 hits and the probe, not the fix, was at fault.

### Refuted and answered, not defects

- **a clickable node inside an `RP` works.** The builder read `!_repeatPiece` as "inside a
  repeat". It is not: `_repeatPiece` is set in `PieceStep`, the CLIP-SPLITTING path, as
  `saved || k > 0`, so the first piece of a split node keeps its hit and the later pieces do not
  — one hit region per node, not per fragment. Measured: `RP n=3` with a clickable `R` gives
  **3** hits, ids `btn:0 btn:1 btn:2`; the same `R` clipped AND repeated still gives one per
  instance. Nothing to fix; the NAME is misleading and that is worth a comment.
- **non-BMP characters are fine in an id and a data name.** Measured: an id of one surrogate
  pair round-trips intact through the hit region, and `$<astral>name` is listed as unresolved
  under its own name. 0 problems. Their validator accepting any Unicode letter is correct.

## Carried over from today

- [x] `since()` semantics — **not a defect.** Measured on the live screen: a name that stops
      arriving counts up (35.0 s), one that keeps arriving sits near zero (0.3 s). A capture
      cannot measure it, because the capture replays the stored elements and re-stamps every data
      name, so `since()` reads 0 in every capture regardless.
- [x] **`FEATURE` make `T` and `IMG` clickable** — 0.11.93. **BOTH halves are now seen on a
      console** (owner's own clicks, 2026-10-05): `InGameTest-labelclick.lua` for `T`, and
      `InGameTest-imgclick.lua` for `IMG` — all four rows behaved, including the rounded `rx=14`
      picture answering inside its corners and the no-`click` control staying silent.
- [x] **0.11.94 IS SEEN on a console** (563, 2026-10-05). Tagged `v0.11.94` at `c52bbef` and
      pushed; remote `untested` moved f118b05 -> c52bbef, and `v0.11.93` was pushed too (it had
      only ever been local, so the builder could not have pinned it). `vector_stats` reports
      0.11.94.0 and the two deliberate problems including `GL "bad": unknown attribute
      "nosuchkey"`. Capture measured: every row's halves within 1% mean brightness.
- [x] **the `IMG` half of 0.11.93's clicks is CONFIRMED** (563, owner's clicks, 2026-10-05).
      `InGameTest-imgclick.lua` is the page; keep it, it is the only clickable-picture coverage.
- [x] document the `since()` trap — done, `f45b169`, with the measurement and the remedy (send
      geometry patches from a different element than the one carrying the animated data).
- [ ] **A3 second half — CONFIRMED by measurement 2026-10-05, not yet fixed.** A stroke `so`
      inherited into an `SC` that also declares `sov` becomes its scroll offset:

      | scene | SC's ScrollSet |
      |---|---|
      | `G so=0.5 { SC }` | (none) — does NOT scroll |
      | `G so=0.5 { SC sov=1 }` | **0.500** — the fault |
      | `SC so=0.5 sov=1` | 0.500 — correct, the author asked |
      | `SC sov=1`, no `so` | (none) — correct |

      `so` is read as a scroll offset only when `sov` is present too (`HasKey(map,"so") &&
      HasKey(map,"sov")`), so a repro WITHOUT `sov` cannot show it — the builder's first repro
      had none and would have measured "absent". Cause: `Merge(inherited, item.Map)` at
      SceneModel.cs:1065 puts the inherited `so` into the node's map before the `SC` case reads
      it, and nothing downstream can tell an inherited key from the node's own. 0.11.92 fixed the
      OTHER direction with a per-op exclusion in `ParseStyle`; this direction needs the node's own
      map to survive the merge. Must not break what an inherited `so` is for: a stroked `R` inside
      an `SC` under `G so=0.25` emits 30 verts against 24 bare, both stroked. **Planned for
      0.11.96** (it changes what an existing scroll container does).
- [ ] audit the conclusions drawn from the offline probe BEFORE `sync_src.py` existed — the
      snapshot could have been stale, and several of today's decisions rest on them.
- [ ] commit the scratch test pages that are worth keeping, delete the rest
      (`InGameTest-accum.lua`, `-accum2`, `-since`, `-tickcheck` are untracked).

## Also fixed today, outside the sweep

- [x] **the unit suite had been red since 0.11.86** (`dotnet run` in `Tests/` exited 1) and nobody
      noticed. Its one failing check asserted the array-depth cap's refusal message — and that cap
      was deliberately removed in 0.11.86 as a mitigation, not a fix. It now asserts what the
      removal was for: 2000 levels parse, an unterminated array is still refused. 358 checks,
      exit 0.
- [x] three rows of `InGameTest-0.11.94.lua` were wrong on the first write (a quoted number in
      `src` is still a number, so `text="700"`, `text="254.6"` and `missing="0"` drew nothing) and
      row 7's control reported the same name as its subject, so it could not have failed. Caught
      by running the page through the probe both ways before pushing it.

## Out of scope here

- the intermittent push/load failure and the blank first capture (owner is testing separately)

## Where this stopped (2026-10-05, usage limit)

Two workflows were running and were STOPPED mid-flight, not failed. Both are resumable with
their cached agent results intact, so re-running costs only the agents that had not finished:

- `vector-c-faults`, run `wf_8dc77015-a4b` — one agent per C1-C14 writing the exact patch, then
  two adversaries per patch. Resume:
  `Workflow({scriptPath: "<session>/workflows/scripts/vector-c-faults-wf_8dc77015-a4b.js",
  resumeFromRunId: "wf_8dc77015-a4b"})`
- `vector-docs-b1-b20`, run `wf_57f0371e-b9e` — the 20 lost B1-B20 answers with exact
  REFERENCE.md patches, each checked against the live file. Resume the same way with
  `vector-docs-b1-b20-wf_57f0371e-b9e.js` and `resumeFromRunId: "wf_57f0371e-b9e"`.

Read `<transcript dir>/journal.jsonl` for what each agent actually returned before assuming a
cached result is non-empty.

**Deliberately not started**, so nothing is half-applied: no C-item code change, and no A3 fix.
`SceneModel.cs` and `Tessellator.cs` are clean at `76ccbdc` — which matters, because fourteen
agents were quoting anchors out of those two files and an edit would have invalidated their work.
