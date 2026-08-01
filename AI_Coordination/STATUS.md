# Coordination Status

Last updated by: Codex - 2026-07-31 23:14

## Current phase: EF&A K guards moved across gauge and winged

The `SCollieDillsboro_7kkq` / `SDillsYard2_uvlz` diamond remains the live
14.79-degree fixture. The user's latest end-on overview reports that the two
acute V frogs are still approximately 0.5 degrees too tight. The current
renderer used the measured heel points and theoretical physical-rail
intersection directly, so its included angle was fixed by those long chords.

Fresh runtime evidence proves the first acute correction loaded, but solving in
large world coordinates lost part of the requested change. The two
source/target pairs were `14.782/15.264` and `14.760/15.246`, only
+0.482/+0.486 degrees. The current renderer keeps both measured heels fixed and
performs the same 24-iteration angle-bisector solve after subtracting crossing
home. That local frame preserves the requested exact +0.500-degree change.
Only standard-diamond acute frogs receive the adjustment.

The first K-guard implementation also loaded, but fresh diagnostics measured
`stockKink=10.244/10.268` versus `guardKink=-9.944/-10.001`. The user's
comparison images show the guard is a detached rail across the gauge, not a
local parallel in line with the frog. The current renderer rigidly translates
the complete stock working length toward crossing center by
`Gauge.Standard.Inside - GuardCenterOffset` (about 1.309 m), then reflects its
center knuckle across the translated endpoint chord. Translation plus
reflection preserves the stock kink's exact magnitude and reverses its sign.

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

The prior DLL proved the two continuous K stocks, two generated K guards, four
ordinary approach checks, and acute correction all execute. The current DLL
supersedes only the guard placement/length/wings and the coordinate frame of
the V-angle solve. K diagnostics now report stock/guard lengths, five guard
stations, transverse offset, extension/wing dimensions, and signed kink values.

Build/deploy succeeded with 0 warnings and 0 errors; deployed and output DLL
SHA-256 hashes match. A full restart is required to load the corrected DLL.

## Next turn

Claude: review the crossing-local V solve and five-station across-gauge K guard
in `proposals/standard-diamond-crossing.md`. User: fully restart Railroader and
inspect both acute frogs and K guards. Fresh logs should show an exact
0.500-degree V increase, `stockStations=3`, `guardStations=5`, approximately
1.309 m guard offset, equal/opposite kink angles, and `selected=6 kGuards=2`.

## Open questions / blockers

- The local-frame acute +0.5-degree solve and across-gauge, two-wing K guards
  await a full-restart render and fresh-log verification.
- Automatic discovery intentionally rejects ambiguous compound zones. Explicit
  `segmentA`/`segmentB` authoring remains a possible later escape hatch.
- Prior Nove/7n90, vdlt/g832, culling, and related manual-verification items
  remain in `LOG.md`; this crossing turn did not alter their deployed code.
