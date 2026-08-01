# Coordination Status

Last updated by: Codex - 2026-07-31 23:29

## Current phase: EF&A K guards cross-paired between obtuse frogs

The `SCollieDillsboro_7kkq` / `SDillsYard2_uvlz` diamond remains the live
14.79-degree fixture. The user's latest end-on overview reports that the two
acute V frogs are still approximately 0.5 degrees too tight. The current
renderer used the measured heel points and theoretical physical-rail
intersection directly, so its included angle was fixed by those long chords.

Fresh runtime evidence proves the crossing-local acute correction is now exact:
the two source/target pairs are `14.782/15.282` and `14.760/15.260`. Both are
exactly +0.500 degrees, so the acute calibration is complete pending only the
user's final visual acceptance.

The user's `23:22:26` screenshot proves the across-gauge placement, five
stations, longer working length, and both wings loaded. It also isolates the
remaining assignment error: the complete upper guard curve belongs at the
lower K, and the complete lower guard curve belongs at the upper K. The current
renderer explicitly pairs the two obtuse frogs and translates each finished
guard by the vector to the other obtuse intersection. This swaps the complete
rails without altering their accepted lengths, wings, or reverse kinks.

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

The fresh log reports both K guards at five stations and 3.043/3.056 m length,
1.309 m transverse offset, 0.9 m extensions, and 0.35 m/10-degree wings. It
also shows the reflected guard kinks remained 0.023/0.046 degrees short after
world-coordinate storage. The current guard curves are built and rendered in
crossing-local coordinates, preserving the exact reflected angle while also
performing the pair swap.

Build/deploy succeeded with 0 warnings and 0 errors; deployed and output DLL
SHA-256 hashes match. A full restart is required to load the corrected DLL.

## Next turn

Claude: review the paired-guard translation and local guard construction in
`proposals/standard-diamond-crossing.md`. User: fully restart Railroader and
confirm the upper-derived guard is now at the lower K and the lower-derived
guard is at the upper K. Fresh K records should include a nonzero `guardSwap`,
five stations, and equal/opposite stock and guard kink magnitudes.

## Open questions / blockers

- The cross-paired, local-coordinate K guards await a full-restart render and
  fresh-log verification.
- Automatic discovery intentionally rejects ambiguous compound zones. Explicit
  `segmentA`/`segmentB` authoring remains a possible later escape hatch.
- Prior Nove/7n90, vdlt/g832, culling, and related manual-verification items
  remain in `LOG.md`; this crossing turn did not alter their deployed code.
