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
| Part A (code vs doc) | 4 | 16 |
| Part B (doc silent or vague) | 0 | 39 |
| Carried over from today | 2 | 3 |

---

## Part A — the code and the doc disagree

- [x] **A1** a `nodes` patch never rebuilds children — **DOC**. Children are never patched; a `c`
      in a patch is dropped. Reference rewritten, `ec13b92`.
- [x] **A2** `T` and `IMG` never register a hit area — **DOC**. Never did, not even at v0.11.66;
      the list also omitted `LS`, which does. Reference rewritten, `ec13b92`. Making them
      clickable is tracked below as a feature.
- [x] **A3** `so` on an `SC` becomes the stroke opacity of everything inside — **CODE**. Fixed in
      0.11.92, measured before and after and seen on a console. Second half still open below.
- [x] **A4** in `src`, only `=` values are read to whitespace — **CODE**. One unquoted
      `text=$rows[i]` rejected the WHOLE scene; a `$` binding now reads to whitespace like an `=`
      expression, which is what the reference already promised. Inside an array the ordinary rule
      stays so `p=[$a,$b]` still splits on the comma. Measured before and after.
- [ ] **A5** `weight` given as a number is ignored
- [ ] **A6** `fmt` is not the printf spec, and an unreadable spec does not render `missing`
- [ ] **A7** an open `L`, `LS` or `SP` with `f` is filled
- [ ] **A8** a `P` with `sh` but no `f` casts no shadow
- [ ] **A9** `ry` never shapes a corner on `R`, `SC` or `IMG`
- [ ] **A10** a broken colour-filter value falls back to 1, not to the filter's identity
- [ ] **A11** an enclosing group's `blur` is lost inside an `SC`
- [ ] **A12** many numeric keys take a literal number only
- [ ] **A13** stroke width under an uneven `m` is not `sqrt(|ad - bc|)`
- [ ] **A14** a missing numeric data array is not reported
- [ ] **A15** a malformed gradient number falls back to 0, not to its default
- [ ] **A16** defs skip the unknown-attribute check
- [ ] **A17** `unit` is ignored on a plain literal `text`
- [ ] **A18** `if()` is not lazy in a deep expression
- [ ] **A19** `noeval` and `nofill` do less, or more, than the table says
- [ ] **A20** `d` rejects compact SVG numbers

## Part B — the doc is silent, vague or contradicts itself

Questions rather than findings; several will be "intentional, here is why". Answering each in the
reference where it belongs.

- [ ] **B1** a quoted number is still a number in `src`
- [ ] **B2** group defaults: which nodes pass them, and case
- [ ] **B3** shadow entries are dropped silently
- [ ] **B4** with `src`, the element's own props are dropped
- [ ] **B5** an unknown function: does the scene draw?
- [ ] **B6** text colour with no `f`, and the text defaults
- [ ] **B7** `src` grammar details
- [ ] **B8** data values the doc does not list
- [ ] **B9** symbol substitution has no name boundary
- [ ] **B10** `mix` in a number slot is 0, unreported
- [ ] **B11** unknown commands in `d`, and open subpaths
- [ ] **B12** `font` with an unknown name
- [ ] **B13** out-of-range array reads: `0` or `missing`?
- [ ] **B14** the magenta border means "no shape reached", not "nothing drawn"
- [ ] **B15** `v` and Lua booleans
- [ ] **B16** effective clamps the doc does not give
- [ ] **B17** `fo`, `sw` and `fat` without the key they modify
- [ ] **B18** enum words: silent fallbacks and accepted spellings
- [ ] **B19** `=` is optional on numeric expressions
- [ ] **B20** expression grammar
- [ ] **B21** function details
- [ ] **B22** repeat variables
- [ ] **B23** gradient defaults and silent fallbacks
- [ ] **B24** clip paths and masks
- [ ] **B25** two doc contradictions about colours
- [ ] **B26** stale rows
- [ ] **B27** pair keys given a scalar, and corner arrays
- [ ] **B28** paint keys on `IMG`
- [ ] **B29** uneven group scale and text
- [ ] **B30** colour-filter ranges
- [ ] **B31** pointer details
- [ ] **B32** text outline and first-line keys
- [ ] **B33** stroke details
- [ ] **B34** `fea` on a band
- [ ] **B35** `G m` with `a`
- [ ] **B36** element pairing
- [ ] **B37** `ease` details
- [ ] **B38** problems are capped at 16
- [ ] **B39** `sd` and `sdo` are known keys that nothing reads

## Carried over from today

- [x] `since()` semantics — **not a defect.** Measured on the live screen: a name that stops
      arriving counts up (35.0 s), one that keeps arriving sits near zero (0.3 s). A capture
      cannot measure it, because the capture replays the stored elements and re-stamps every data
      name, so `since()` reads 0 in every capture regardless.
- [ ] **`FEATURE` make `T` and `IMG` clickable.** IN PROGRESS. They never have been; the reference now says so.
      A hit area is recorded where a shape's outline is built, and neither a label nor a picture
      goes through that path. Owner asked for this on 2026-10-05.
- [ ] **A3 second half** — a stroke `so` inherited INTO an `SC` that declares `sov` becomes its
      scroll offset. Needs the parse to tell a node's own keys from its inherited ones.
- [x] document the `since()` trap — done, `f45b169`, with the measurement and the remedy (send
      geometry patches from a different element than the one carrying the animated data).
- [ ] audit the conclusions drawn from the offline probe BEFORE `sync_src.py` existed — the
      snapshot could have been stale, and several of today's decisions rest on them.
- [ ] commit the scratch test pages that are worth keeping, delete the rest.

## Out of scope here

- the intermittent push/load failure and the blank first capture (owner is testing separately)
