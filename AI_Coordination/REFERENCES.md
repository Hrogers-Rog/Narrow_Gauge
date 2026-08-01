# Reference Paths

Absolute paths on this machine to code this repo depends on or is checked
against. None of these are inside this repo — read them in place, do not
copy them in.

## This repo

`C:\Hrogers_Railroader_mods_Projects\Narrow_Gauge`

- `src/` — mod source. See root `README.md`'s "Project Layout" section for
  the current file-by-file breakdown (ghost graph sync, shared-rail
  registries, special-work geometry/anatomy/preset catalog, hardware
  rendering, lifecycle/patches). Treat `README.md` as the up-to-date map;
  update it in the same turn as any change to layout or behavior contract.
- `docs/` — prior design notes and investigations (history/rationale, not
  necessarily current truth — verify against `src/` before relying on one).
- `truth/SpecialWorkTruthTables.json` — hand-authored known-good
  rail-role/topology combinations consumed by
  `src/SpecialWorkTruthTableValidator.cs`.

## FUSE (dependency framework this mod builds on)

`C:\Hrogers_Railroader_mods_Projects\FUSE`

- `FUSE.Core/Model/FuseTrackDefinition.cs` — the authoring-time node/segment/
  span/area graph this mod's `gauge` tags attach to. `Gauge` on `FuseSegment`
  is a bare string, inert to FUSE itself; this mod is what gives it meaning.
- `FUSE/Runtime/API/TrackAPI*.cs` — the live native-graph mutation surface
  (batch/node/segment CRUD, rebuild, snapshot) this mod's ghost-graph
  synchronizer calls into.
- `FUSE/Loading/FuseModLoader.TrackMerging.cs` — multi-package mod-merge
  algorithm (load-order priority, patch hydration, one native rebuild per
  load batch) that this mod's synchronization hooks run inside of.

## Base game decompiled source (read-only reference — never edit)

`C:\Hrogers_Railroader_mods_Projects\Decompiled dlls base game`

- `SimpleGraph.Runtime/` (`Node.cs`, `Edge.cs`, `SimpleGraph.cs`) and
  `Assembly-CSharp/Track/Graph.cs`, `TrackNode.cs`, `TrackSegment.cs` — the
  native single-gauge, degree-3-switch graph this mod's ghost topology and
  hidden control segments have to stay valid against.
- `Assembly-CSharp/Track/SwitchGeometry.cs` — the native procedural switch
  rail-offset/frog algorithm this mod's special-work geometry is modeled on
  (`MakeTrackLineSegments` already takes a `Gauge` parameter; only the
  switch-level call site hardcodes `Gauge.Standard`).
- `Assembly-CSharp/Track/Gauge.cs` — numeric gauge definition
  (`Inside`/`HeadWidth`/`RailHeight`); only `Gauge.Standard` is public in
  this decompile, so non-standard gauges require the reflection technique
  already used in `src/NarrowGaugeTrackBuilder.cs`.

A second decompile exists at
`C:\Hrogers_Railroader_mods_Projects\Decompiled DLLs Not BASE GAME` — unclear
vintage/purpose, verify before relying on it for anything.

- `C:\Hrogers_Railroader_mods_Projects\Decompiled DLLs Not BASE GAME\C_L_B.DKW\C_L_B.DKW\DKW\KRESpliney.cs`
  and `DKW_Util.cs` - verified reference for C_L_B.DKW's KRE fixed-crossing
  implementation. It explicitly binds two native segments, detects their
  centerline and four rail intersections, substitutes split render proxies,
  and leaves the routes graph-disconnected. Reuse the topology pattern, not
  the decompiled intersection helper verbatim.

## Parallel rewrite effort (reference only, not authoritative for this repo)

`C:\Hrogers_Railroader_mods_Projects\Fuse_geometry_engine`

A separate project rewriting the ideas in this repo into one physical
`TrackAssembly` model. Its own `AI_Coordination/REFERENCES.md` contains a
detailed file-by-file catalog of this repo's `src/` as reviewed on
2026-07-01/02 — useful starting context for understanding a file you haven't
touched yet, but it will drift out of date as this repo changes; verify
against current code here, don't treat that catalog as current truth. Do not
adopt `TrackAssembly`-shaped types in this repo as a side effect of reading
it; see `00_PROJECT_CONSTITUTION.md`'s "Relationship to Fuse_geometry_engine".

## Updating this file

Add an entry here whenever work touches a dependency or reference path not
yet listed above, so the map stays accurate for whichever agent reads it
next.
