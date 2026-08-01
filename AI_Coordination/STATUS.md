# Coordination Status

Last updated by: Codex - 2026-07-31 23:00

## Current phase: EF&A V angle opened; kinked K guards deployed

The `SCollieDillsboro_7kkq` / `SDillsYard2_uvlz` diamond remains the live
14.79-degree fixture. The user's latest end-on overview reports that the two
acute V frogs are still approximately 0.5 degrees too tight. The current
renderer used the measured heel points and theoretical physical-rail
intersection directly, so its included angle was fixed by those long chords.

The deployed acute correction keeps both heel endpoints fixed so their seams
remain aligned to the measured running rails. It moves only the rendered frog
nose along the horizontal angle bisector, using a 24-iteration binary solve
until the V angle is exactly 0.5 degrees wider. The correction is passed only
by the standard-diamond acute renderer; every non-diamond V frog retains a
zero-degree adjustment.

The user also clarified that the missing kink belongs specifically to the two
K-frog guards. Each K renderer now derives its guard from an inward parallel of
the completed continuous outside stock/knuckle rail, after a short end trim.
It then reflects the guard's center point across the chord between its two
endpoints. Consequently the guard turns through exactly the stock rail's kink
angle in the opposite signed direction. The four ordinary approach check rails
remain unchanged and are not kinked.

The user's latest K close-up also proves that preserving the source wing-curve
samples rounds the outside stock rail through the frog. The deployed stock
builder now emits exactly three stations: measured outside endpoint, the
compiler's frog-center knuckle, and measured outside endpoint. Both spans are
straight and all direction change occurs at that single center kink. Its offset
K guard consequently has the same three-station form with the center kink
mirrored to the opposite direction.

The same deployed DLL still contains the preceding continuous K-stock and
two-K-guard corrections. The current Player log shows their older predecessor
(`pieces=4`, `selected=4`), proving the game has not yet loaded either the K
correction or this angle correction. The next restart should additionally log
one `[VeeFrogAngle]` record per acute frog with target minus source equal to
0.500 degrees. K records now additionally report `kinkedGuard=1`.

Build/deploy succeeded with 0 warnings and 0 errors; deployed and output DLL
SHA-256 hashes match. A full restart is required to load the corrected DLL.

## Next turn

Claude: review the diamond-only V-angle solve plus the outstanding K-stock and
guard diff in `proposals/standard-diamond-crossing.md`. User: fully restart
Railroader and inspect both acute frogs. Fresh logs should show two
`[VeeFrogAngle]` entries with an exact 0.500-degree increase, two K records with
`continuousStock=1 stockStations=3 kinkedGuard=1`, and one check-rail record
with `selected=6 kGuards=2`.

## Open questions / blockers

- The acute +0.5-degree angle, continuous K stock rails, and kinked K guards
  await a full-restart render and fresh-log verification.
- Automatic discovery intentionally rejects ambiguous compound zones. Explicit
  `segmentA`/`segmentB` authoring remains a possible later escape hatch.
- Prior Nove/7n90, vdlt/g832, culling, and related manual-verification items
  remain in `LOG.md`; this crossing turn did not alter their deployed code.
