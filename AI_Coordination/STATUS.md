# Coordination Status

Last updated by: Codex - 2026-07-31 22:52

## Current phase: K point rails accepted; continuous stock rail and guards deployed

The `SCollieDillsboro_7kkq` / `SDillsYard2_uvlz` diamond remains the live
14.79-degree fixture. The acute-direction/flangeway build loaded successfully.
The user's latest K close-up explicitly accepts its two tapered point rails.
The remaining visible defect was the outside stock/knuckle rail: the previous
wheel-guide implementation rendered it as two cut halves instead of the one
continuous bent rail shown in the supplied correct prototype.

The runtime plan already contains the needed anatomy per K frog: four wing
spans and two obtuse pieces around the two physical-rail paths. The deployed
renderer retains the two enclosed-diamond, wheel-guide-clipped point rails. It
selects the outward obtuse piece, joins its two matching wing spans through it,
and emits that entire outside stock rail as one mesh with no cut at the frog.
This is performed symmetrically for both K-frog locations.

Fresh logs also proved the plan derives six guard candidates while the prior
renderer selected only four. Those four remain the farthest approach check
rails, and the two remaining candidates closest to the crossing center are now
restored as the missing K-frog guards. Diagnostics will report
`selected=6 kGuards=2` and each K frog will report
`pointRails=2 continuousStock=1` after restart.

Build/deploy succeeded with 0 warnings and 0 errors; deployed and output DLL
SHA-256 hashes match. A full restart is required to load the corrected DLL.

## Next turn

Claude: review the continuous K-stock joining and restored-guard diff plus
`proposals/standard-diamond-crossing.md`. User: fully restart Railroader and
inspect the two K stock rails and two new central guards. Fresh logs should
show two `[DiamondFlangewayFrog]` entries with `continuousStock=1` and one
`[DiamondCheckRails]` entry with `selected=6 kGuards=2`.

## Open questions / blockers

- The two K point rails have visual acceptance; the continuous outside stock
  rails and restored K guards await the next render.
- Automatic discovery intentionally rejects ambiguous compound zones. Explicit
  `segmentA`/`segmentB` authoring remains a possible later escape hatch.
- Prior Nove/7n90, vdlt/g832, culling, and related manual-verification items
  remain in `LOG.md`; this crossing turn did not alter their deployed code.
