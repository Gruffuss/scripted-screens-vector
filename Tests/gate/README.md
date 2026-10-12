# Tessellation regression gate

Run `dotnet run` from `Tests/`. Every scene is parsed and tessellated at `t = 0`, `1.5`,
and `7.25`; each differing fingerprint line fails the suite and prints both values.
Rows record vertices, triangles, shapes, hit regions, labels, mesh bounds and parse problems.

When a change intentionally moves geometry, run `dotnet run -- --rebaseline` from `Tests/`
and review the changes to `gate/fingerprint.txt` before committing them.
The first 51 rows preserve the original 17-scene corpus. Additional scenes sort after them.

`zzz-dash` has both full and screen-window rows. `zzz-dash-window` contains a billion-unit
line in a 200-unit scene and runs only with the target rectangle as its visible window.
Windowed runs must produce between 1 and 1,024 vertices.

`zzz-nofill-control` is the same scene as `zzz-nofill` without `nofill=1`.
With `nofill`, the radial, blurred and flat fills each contribute zero vertices;
only the flat control's eight stroke vertices remain. Without it, all three fills draw.
Feathering is disabled to isolate the fill geometry.

The test runner installs a managed hex colour parser before parsing. The game retains
Unity's parser. This stand-in supports hex literals rather than Unity's named colours.
FONT declarations are parsed and retained; the suite does not download fonts.
