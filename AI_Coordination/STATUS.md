# Coordination Status

Last updated by: Codex - 2026-07-31 22:04

## Current phase: EF&A standard-gauge diamond implemented, live verification pending

At the user's explicit direction, generic standard-gauge fixed-diamond support
is implemented and deployed. It uses **zero ghost nodes**: four logical ports
and two continuous, graph-disconnected routes. The geometric intersection does
not become a native graph connection.

`StandardCrossingDiscovery` detects isolated same-grade ordinary standard
segment pairs, rejects shared endpoints, grade separations, angles below 8
degrees, insufficient approach lead, and overlapping compound zones, then
feeds the existing `crossing.diamond` geometry compiler. Both segment renderers
clip ordinary rails/ties through the measured envelope; one deterministic owner
renders the fixed pieces, four crossing frogs, guards, and two tie beds.

The installed EF&A graph's straight-endpoint fixture scan finds four candidates
passing the same filters. The likely pictured pair is
`SCollieElaRework_ibsa` / `SCollieCoalTrack_mapf` at 26.2 degrees. Runtime curve
results will be visible through `[CrossingDiscovery]`, plan validation, segment
clip, tie clip, and `[CrossingBuild]` log entries after a full restart.

Build/deploy succeeded with 0 warnings and 0 errors; deployed and output DLL
hashes match. `proposals/standard-diamond-crossing.md` remains Draft because
the coordination protocol still requires Claude review even though the user
directed implementation immediately.

## Next turn

Claude: review the actual generic discovery, ownership clipping, and crossing
hardware diff plus `proposals/standard-diamond-crossing.md`. Agree or record a
specific disagreement. Do not replace it with EF&A ID special cases. User:
restart Railroader and inspect the EF&A crossing plus fresh log evidence.

## Open questions / blockers

- The likely pictured EF&A pair is inferred from geometry, not yet confirmed by
  a restarted runtime log/camera inspection.
- Automatic discovery intentionally rejects ambiguous compound zones. Explicit
  `segmentA`/`segmentB` authoring remains a possible later escape hatch.
- Prior Nove/7n90, vdlt/g832, culling, and related manual-verification items
  remain in `LOG.md`; this crossing turn did not alter their deployed code.
