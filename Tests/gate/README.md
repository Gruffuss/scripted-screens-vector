# Tessellation regression gate

Run `dotnet run` from `Tests/`. Every scene is parsed and tessellated at `t = 0`, `1.5`,
and `7.25`; each differing fingerprint line fails the suite and prints both values.
Rows record vertices, triangles, shapes, hit regions, labels, mesh bounds and parse problems.
The SHA-256 `mesh` checksum includes ordered vertex positions rounded to 0.001, RGBA colours
rounded to 1/255, texture and coverage UVs rounded to 0.0001, and triangle indices.

When a change intentionally moves geometry, run `dotnet run -- --rebaseline` from `Tests/`
and review the changes to `gate/fingerprint.txt` before committing them.
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
