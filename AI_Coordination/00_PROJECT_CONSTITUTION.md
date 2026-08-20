# Project Constitution

Status: Agreed (Claude + Codex, 2026-07-05)

This repository is the live FUSE Narrow Gauge mod: a FUSE companion module
that adds narrow-gauge and dual-gauge track, generated narrow ghost routing,
and custom special-work (turnout/crossing/slip/wye) rendering to Railroader.
It is a shipping mod with real map authors depending on its current behavior
contract (see `README.md`), not a from-scratch design exercise.

## Why this exists

Railroader's base game graph only understands single-gauge, degree-3-switch
track. This mod makes narrow and dual gauge trackwork work on top of that by
generating a second ("ghost") narrow-gauge graph synchronized to authored
dual-gauge segments, synthesizing hidden control segments so the base game's
3-node switch model still validates, and building custom rail meshes for
special work the base game has no native concept of.

## Relationship to `Fuse_geometry_engine`

`<projects>\Fuse_geometry_engine` is a separate,
parallel repo doing a from-scratch architecture rewrite of the ideas proven
here, replacing bolted-on ghost-graph/registry conventions with one physical
`TrackAssembly` model. That project treats this repo as read-only reference
material (`AI_Coordination/REFERENCES.md` there catalogs this repo's files).
**This repo is not that rewrite.** Work here is ordinary maintenance and
feature development on the current mod as it ships today. Do not block a fix
or feature here on the rewrite landing, and do not silently adopt
`TrackAssembly`-shaped types here — if a change here would be informed by (or
would usefully inform) that rewrite, note it in this repo's `LOG.md` and, if
warranted, add a pointer in the other repo's `REFERENCES.md`, but keep the
two codebases and coordination logs independent.

## Core Rules

- **The behavior contract in `README.md` is the spec.** Gauge tags
  (`Narrow`, `DualGauge`, `DualGauge_L`, `DualGauge_R`, `DualGauge_T`), the
  shared-rail inference/propagation rules, and the preset catalog are what
  map authors build against. A change that alters this contract needs the
  README updated in the same turn, not left to drift out of sync.
- **The ghost graph is generated, not authored.** Map authors tag FUSE
  segments with a gauge; this mod derives `fuse-ng:*` narrow topology and
  hidden control segments deterministically. Never require a map author to
  hand-place ghost geometry to get correct behavior.
- **Old-mod behavior is not automatically ground truth.** Where this mod's
  existing formulas/heuristics are buggy or inconsistent with the base game
  or FUSE, fix them — don't preserve a wrong result for compatibility with
  itself. Verify against the actual decompiled base game and FUSE source
  when in doubt, not against what this mod currently happens to do.
- **Don't mix concerns.** Ghost-graph/topology synchronization, special-work
  geometry/anatomy analysis, mesh/GameObject rendering, and FUSE
  extension-schema parsing are separate layers (see `README.md`'s Project
  Layout). A fix in one should not reach into another's internal
  representation when a narrower change is available.
- **Build and test before committing.** This is a shipping mod; a change
  that doesn't build against a real Railroader install, or that isn't
  exercised (unit test or a described manual/in-game check) before it's
  marked done, isn't done.

## Non-goals

- Do not re-architect this mod into the `Fuse_geometry_engine` model here —
  that migration is a separate, later, deliberate cutover, not incidental
  cleanup during a bug fix.
- Do not add a new per-instance/per-map special case (id substring matching,
  hardcoded map-specific ids) when a general fix is available; the existing
  `SpecialWorkHardwareProfileCatalog.cs` `NoveIds`/`U6N0Ids` pattern is a
  known wart, not a precedent to extend.

## Process

Investigate the actual runtime/generated behavior before changing shared
topology-synchronization or geometry code — these are load-bearing for every
map that uses dual/narrow gauge today. Small, well-scoped fixes don't need a
review-then-propose cycle; anything touching ghost-graph generation,
shared-rail inference, or the special-work compiler pipeline does.

## Open disagreements

(none yet — this is where Codex or Claude raises objections to the above per
`PROTOCOL.md`'s agreement rule; resolve here before status can move away from
Agreed)
