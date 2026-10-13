# Visible-surface scale cost

Run from the test directory with `dotnet run -c Release -- --scale-cost`.

The page has 221 rounded, stroked cards, including 13 sigma-5 shadows, in a
684.3-by-400 viewbox. It follows the card and shadow geometry in the shipped examples.
Curve, count and blur LOD are off; PixelEdges is on; no visible-window culling is supplied.
Each pair uses the same projection, page, context and mesh storage. Parsing is excluded;
mesh clearing and emission are included. All 221 shapes complete without exhausting a budget.

Measured with the managed renderer, Release, .NET 8.0.29: median of 15 paired samples,
with alternating order, after six paired warmups. These are offline relative costs,
not game frame timings.

| Geometry | Original scale (px/unit) | Visible scale (px/unit) | Original ms | Visible ms | Original vertices | Visible vertices |
|---|---:|---:|---:|---:|---:|---:|
| Side, corner depths 1.014 / 0.020, original bottom span 83,456 px | 121.958313 | 5.900846 | 40.047 | 23.894 | 205,972 | 121,160 |
| Close, centred, all corners off screen and in front | 40.000000 | 40.000000 | 40.475 | 40.587 | 205,972 | 205,972 |
| Close, roughly centred, crossing the near plane | 2.000000 fallback | 49.167950 | 18.397 | 43.111 | 84,952 | 205,972 |

The crossing case's original scalar measurement declines (`-1`); its original chosen
scale is the last-known 2 px/unit, using the production fallback. The visible measurement
comes from a horizontal chord inside the frustum. The close views remain magnified.
These numbers do not establish the cause of the previously recorded 171–188 ms game rebuilds.
