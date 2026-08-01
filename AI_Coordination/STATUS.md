# Coordination Status

Last updated by: Codex - 2026-07-31 23:58

## Current phase: accepted EF&A K-guard orientation restored

The `SCollieDillsboro_7kkq` / `SDillsYard2_uvlz` diamond remains the live
14.79-degree fixture. Fresh runtime evidence proves the crossing-local acute
correction is exact: the source/target pairs are `14.782/15.282` and
`14.760/15.260`, both exactly +0.500 degrees.

The user's `23:33:15` screenshot rejects the preceding whole-rail location
swap. Translating by the vector between obtuse intersections put both guards
outside the trackwork. The required anatomy is now explicit: both guards stay
centered in their native K-frog locations, while the upper/lower guard SHAPES
are cross-assigned so their knuckles and wings face inward toward one another.
The current renderer builds both native centered guards, takes the shape from
the paired K, and recenters it on the target guard's original center station.

The user's latest K close-up also proves that preserving the source wing-curve
samples rounds the outside stock rail through the frog. The deployed stock
builder now emits exactly three stations: measured outside endpoint, the
compiler's frog-center knuckle, and measured outside endpoint. Both spans are
straight and all direction change occurs at that single center kink. Its offset
K guard extends a further 0.9 m at both ends. Following the existing
narrow-gauge K/check-rail construction, the outer 0.35 m on each end is a
10-degree wing flared away from the frog line. The result has five explicit
stations: wing tip, working heel, reverse center kink, working heel, wing tip.
The stock remains the required three-station straight-kink-straight rail.

The accepted dimensions remain unchanged: five stations, 3.043/3.056 m
length, 1.309 m transverse offset, 0.9 m extensions, and two 0.35 m/10-degree
wings. Guard curves remain in crossing-local coordinates to avoid large-world
float loss. New diagnostics report `guardCrossPaired=1` and the center-to-center
`guardShapeShift` rather than the rejected whole-frog `guardSwap`.

The user's accepted restart confirms the guards were centered correctly and
the signed kink diagnostics were exact (`10.244/-10.244` and
`10.268/-10.268`). The only requested change was the slightly excessive
guard-to-point-rail gap. The following direct-fit attempt was invalid: fresh
runtime records show it measured 1.164/1.162 m from each guard to a complete
route rail, then translated each guard to 0.126 m. That roughly 1.04 m move
crossed the frog and destroyed the already accepted orientation. Those are
not the generated K point rails and cannot calibrate this flangeway.

The invalid fitter and its misleading diagnostics are now removed. The exact
accepted centered, cross-paired guard curves are restored, including their
inward-facing wings and `1.309 m` native offset. The nominal narrow-K rule
remains unchanged: 0.076 m railhead plus 0.050 m clear flangeway equals
0.126 m center spacing. Any later millimetric visual-gap correction must be
measured against the generated point-rail profile, never against either full
route centerline, and must translate the accepted guard rigidly without
recomputing its shape or direction.

The user's `23:49:32` close-up identifies a separate render-frame defect in
the continuous K stock. Its logical curve already contains only start, center
kink, and end, but the stock mesh used one averaged rotation at that center.
The mesh shader interpolates that rotation/normal along both spans, making the
railhead look as though it gradually curves into the bend. The current render
curve duplicates the center position: the first center frame keeps the exact
incoming direction and the second keeps the exact outgoing direction. The
zero-length transition between them forms one sharp knuckle while the two
physical spans remain perfectly straight. Logical geometry stays at three
stations; diagnostics report four render stations and `hardKink=1`.

Build/deploy succeeded with 0 warnings and 0 errors. Output and deployed DLL
SHA-256 hashes both equal
`161E015639AF1CB60C2ACF21258D3E1A5C2A0A2648D48691B697719547084A97`.
A full restart is required to load the corrected DLL.

## Next turn

Claude: review the hard-knuckle render frames and the rejection of the full
route-rail flangeway fit in `proposals/standard-diamond-crossing.md`. User:
after the new build is deployed, fully restart Railroader and confirm the K
guards have their previously accepted centered/inward orientation while the
outside stock is straight-angle-straight. Fresh K records should again report
`guardShapeShift` near 0.900 m, not the rejected 1.157 m displacement.

## Open questions / blockers

- The hard K-stock knuckle and restored accepted K guards await a full-restart
  render and fresh-log verification. The reported slight visual gap remains a
  separate profile-aware calibration after orientation is reconfirmed.
- Automatic discovery intentionally rejects ambiguous compound zones. Explicit
  `segmentA`/`segmentB` authoring remains a possible later escape hatch.
- Prior Nove/7n90, vdlt/g832, culling, and related manual-verification items
  remain in `LOG.md`; this crossing turn did not alter their deployed code.
