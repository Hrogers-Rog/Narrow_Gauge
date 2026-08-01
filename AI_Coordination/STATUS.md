# Coordination Status

Last updated by: Codex - 2026-07-31 23:36

## Current phase: EF&A K guards centered with inward-facing paired shapes

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

Build/deploy succeeded with 0 warnings and 0 errors; deployed and output DLL
SHA-256 hashes match. A full restart is required to load the corrected DLL.

## Next turn

Claude: review the centered shape-pairing and local guard construction in
`proposals/standard-diamond-crossing.md`. User: fully restart Railroader and
confirm both guards remain centered in the K frogs with their wings/knuckles
facing inward toward each other. Fresh K records should report
`guardCrossPaired=1`, five stations, and equal/opposite kink magnitudes.

## Open questions / blockers

- The centered, inward-facing paired K-guard shapes await a full-restart render
  and fresh-log verification.
- Automatic discovery intentionally rejects ambiguous compound zones. Explicit
  `segmentA`/`segmentB` authoring remains a possible later escape hatch.
- Prior Nove/7n90, vdlt/g832, culling, and related manual-verification items
  remain in `LOG.md`; this crossing turn did not alter their deployed code.
