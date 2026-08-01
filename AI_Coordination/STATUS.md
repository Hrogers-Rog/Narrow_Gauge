# Coordination Status

Last updated by: Codex - 2026-07-31 22:25

## Current phase: EF&A diamond frog topology corrected, second visual pending

The first live custom render proved automatic discovery, plan validation,
ordinary rail/tie clipping, and single-owner rendering work. The exact pictured
fixture is `SCollieDillsboro_7kkq` / `SDillsYard2_uvlz` at 14.79 degrees. Its
plan is valid: four rails, four intersections/frogs, twelve fixed pieces,
six guard candidates, and zero blades.

The first renderer missed the physical diamond anatomy: it treated all four
rail intersections as identical isolated crossing points instead of two acute
frogs and two obtuse/K frogs. It also applied the ordinary 0.35 m minimum rail
length to required K-frog wings only about 0.18-0.30 m long, silently dropping
them. The screenshot shows the resulting disconnected/open center.

The deployed correction classifies the two distant frog positions as outward
acute V-frogs and the near pair as obtuse/K frogs. Each K frog uses the
compiler's two point/elbow pieces plus all four short wing rails. Check rails
are reduced from six collision-filter candidates to one farthest-out candidate
per physical running rail (four total when all rail groups are present).

Build/deploy succeeded with 0 warnings and 0 errors; deployed and output DLL
hashes match. A full restart is required to load the corrected DLL.

## Next turn

Claude: review the acute/obtuse role correction and the actual diff plus
`proposals/standard-diamond-crossing.md`. User: fully restart Railroader and
inspect the same Dillsboro diamond. Fresh log evidence should include
`[DiamondFrogRoles]` and `[DiamondCheckRails]`.

## Open questions / blockers

- The second corrected render has not yet received visual acceptance.
- Automatic discovery intentionally rejects ambiguous compound zones. Explicit
  `segmentA`/`segmentB` authoring remains a possible later escape hatch.
- Prior Nove/7n90, vdlt/g832, culling, and related manual-verification items
  remain in `LOG.md`; this crossing turn did not alter their deployed code.
