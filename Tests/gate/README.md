# Tessellation regression gate

Run `dotnet run` from `Tests/`. Every scene is parsed and tessellated at `t = 0`, `1.5`,
and `7.25`; each differing fingerprint line fails the suite and prints both values.
Rows record vertices, triangles, shapes, hit regions, labels, mesh bounds and parse problems.
The SHA-256 `mesh` checksum includes ordered vertex positions rounded to 0.001, RGBA colours
rounded to 1/255, texture and coverage UVs rounded to 0.0001, and triangle indices.

When a change intentionally moves geometry, run `dotnet run -- --rebaseline` from `Tests/`
and review both `gate/fingerprint.txt` and `gate/fingerprint-pixel.txt` before committing them.
The two `fingerprint-off-*-bytes.txt` files are immutable baselines from the original emitter:
one with stock coverage, one with shader UV coverage. They hash every float bit and index,
without rounding. Every one of their 78 rows must still match with pixel edges off.
The pixel fingerprint also uses unrounded hashes, with shader coverage enabled.

Pixel mode changes 21 rows relative to the original shader-coverage baseline. Each row below
covers all three times (`0`, `1.5`, `7.25`); rows not listed are byte-identical.

| Scene | Changed fields | Reason |
|---|---|---|
| `16-pointer` | triangles +4; mesh | one automatic open stroke gains two feathered end closures |
| `18-lines` | vertices +728; triangles +1004; mesh | round caps gain fringes, open butt/square strokes gain end closures, and automatic side bands carry edge distances |
| `InGameTest-0.11.74` | triangles +8; mesh | two automatic open strokes gain end closures; automatic fills carry edge distances |
| `InGameTest-handson` | mesh bounds, mesh | automatic bands grow from the old bisector offset to a two-ramp safety extent; solid bounds remain unchanged |
| `zz-clip-and-mask` | mesh | the clipped automatic ellipse ring carries solid colour, geometric coverage and edge distance; mask/clip geometry counts stay unchanged |
| `zz-groups` | mesh bounds, mesh | automatic bands use the full group transform and wider safety extent |
| `zz-repeat` | mesh bounds, mesh | repeated automatic bands use the wider safety extent |

Hole rings and root/group y scales are also covered by the dedicated pixel-edge tests.
Additional vertices/triangles on holes and stroke ends are deliberate; fill-only counts,
shape bounds, label placement, hits and slice cuts retain their regression checks.
The first 51 rows cover the corrected original 17-scene corpus. Additional scenes sort after them.

`zzz-dash` has both full and screen-window rows. `zzz-dash-window` contains a billion-unit
line in a 200-unit scene and runs only with the target rectangle as its visible window.
Windowed runs must produce between 1 and 1,024 vertices.

`zzz-nofill-control` is the same scene as `zzz-nofill` without `nofill=1`.
With `nofill`, the radial, blurred and flat fills each contribute zero vertices;
only the flat control's eight stroke vertices remain. Without it, all three fills draw.
Feathering is disabled to isolate the fill geometry.

The test runner installs a managed parser for Unity's hex and named colours before parsing.
The mod's whitespace trimming and `gray` normalization still run through `Colours`.
The installer is compiled only into the suite; the game retains Unity's parser.
FONT declarations are parsed and retained; the suite does not download fonts.

`InGameTest-handson` expands the Lua page's default square console: a 224-by-224 viewbox
and horizontal offset 12, with its actual embedded GIF and BMP URLs. Their decoded dimensions
are loaded into the image stub. `InGameTest-0.11.84` contains the actual 400-level expressions.

`17-pictures` uses `PIC` as a loaded 863-by-471 image, matching the shipped thumbnail's dimensions.
It exercises fit, alignment, cropping, rounded clipping, tiling, slicing, opacity, hit regions,
and emitted UVs. It does not exercise downloading, GPU texture upload, sampling or rendered pixels.
