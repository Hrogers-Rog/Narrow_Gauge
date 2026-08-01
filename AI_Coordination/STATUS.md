# Coordination Status

Last updated by: Codex - 2026-07-31 22:42

## Current phase: EF&A acute frogs reversed and frog flangeways rebuilt

The latest live overview confirms the role classifier closed the diamond and
emitted both acute and K-frog locations for `SCollieDillsboro_7kkq` /
`SDillsYard2_uvlz` at 14.79 degrees. Fresh logs classify `frog:3`/`frog:0` as
the outer acute pair and `frog:1`/`frog:2` as the inner K pair, and select four
check rails.

The user's close-up and railway diagram exposed two remaining manufacturing
errors. Both acute assemblies were reversed: their point rails faced away from
the enclosed diamond while their wings occupied the diamond side. The 0.10 m
wing offset also approximated only one railhead and omitted the configured
0.05 m flangeway. The K-frog point/elbow pieces were full-width solid meshes,
so they likewise had no wheel-flange relief.

The deployed correction rotates both acute assemblies 180 degrees as exact
mirrors: each nose now faces inward, its vee heels face the approach, and its
wings occupy the outside. Acute wing separation is now measured railhead width
plus plan flangeway width. Each K frog is rebuilt from both physical rails,
with two mesh halves clipped to opposite sides of the other route's actual
wheel-flange guide. The omitted 0.05 m guide bands form the four K-frog flange
openings. Existing non-diamond vee frogs keep their old 0.10 m placement.

Build/deploy succeeded with 0 warnings and 0 errors; deployed and output DLL
SHA-256 hashes match. A full restart is required to load the corrected DLL.

## Next turn

Claude: review the inward acute orientation and wheel-guide clipping diff plus
`proposals/standard-diamond-crossing.md`. User: fully restart Railroader and
inspect both acute ends and both K frogs. Fresh log evidence should include
two `[DiamondAcuteFrog]` and two `[DiamondFlangewayFrog]` entries.

## Open questions / blockers

- The inward/relieved third render has not yet received visual acceptance.
- Automatic discovery intentionally rejects ambiguous compound zones. Explicit
  `segmentA`/`segmentB` authoring remains a possible later escape hatch.
- Prior Nove/7n90, vdlt/g832, culling, and related manual-verification items
  remain in `LOG.md`; this crossing turn did not alter their deployed code.
