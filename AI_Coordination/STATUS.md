# Coordination Status

Last updated by: Codex - 2026-07-09 20:46 EDT

## Critical testing constraints

- `NarrowGaugeMod.dll` is loaded only at full Railroader process startup. A
  save reload does not load this deployment.
- The user retired the automated Railroader/TestBridge pipeline. Do not launch
  or drive Railroader. Build/deploy is allowed; live verification is manual.

## Current phase: confirmed flangeway mirror and guard-6 side fix deployed

The generic both-diverge renderer caused long gaps and was rolled back in
`349fb99`. A subsequent uncommitted experiment replacing full-span rails with
planed points was live-rejected by the user and has also been completely
removed. The deployed source is back on the stable continuous-handoff and
original point-span renderer.

The user's fc97 adjustment-UI isolation identifies the actual original defect:

- With `CrossingFrog-2-ContinuousStockHandoff` disabled, the point meshes
  remain visibly clipped on the blue outside edge of the narrow through rail.
  They must retain/cut toward the red inside flangeway instead.
- Guard 6 is also on the wrong side. The fresh fc97 plan proves it is the local
  K-frog guard emitted after guards 0-5 for the three accepted frogs.

The code has direct mechanisms for both errors:

1. `Fixed-10-StandardThroughFrog` passes ordered cutters
   `[standardFlangeway, narrowFlangeway]` into the mesh clipper.
   `ShouldAutoFlipFlangewayKeepSide` always returned false, although its
   companion already selects cutter index 1 when enabled. The new rule enables
   that inversion for `DualBothDiverge` `StandardThroughFrog`, so only the
   narrow cutter is mirrored to the red inside edge. The existing
   frog-centered cut window now applies across that anatomy rather than only
   to an fc97 id.
2. `TryBuildLocalCrossingGuard` correctly selected the candidate farther from
   the continuous handoff, then shifted it back toward the handoff by one
   `RailHeadWidth`. That extra 0.076 m shift is removed; guard 6 now stays on
   its already-selected flangeway side.

No frog spans, handoff geometry, cuts, counts, or node ids were changed.

Built and deployed: 0 warnings, 0 errors. Built/deployed DLL timestamp
2026-07-09 20:45:27, size 737,280 bytes, SHA-256
`D36F4A1FEBB9A2D87AD6F6D8D944E082FDA990903C0124462C27A6089E2E464E` on both
copies. No game process was launched or controlled. Full evidence:
`reviews/frog-direction-gap-frame-investigation-2026-07-09.md`.

## Next turn

1. Fully quit and restart Railroader; a save reload is insufficient.
2. At fc97, hide the continuous frog again. `Fixed-10-StandardThroughFrog`
   should now be cut/kept on the red inside edge, not the blue outside edge.
3. Check guard 6: it should move away from the handoff by one railhead width
   and align as the local K-frog check rail.
4. Re-enable the continuous handoff and confirm the original point lengths and
   boundary coverage remain intact with no generic-renderer long gap and no
   planed-point deformation.
5. Spot-check l4a4 and a left-side crossing such as p997/ltci. The rule is
   anatomy-based and should mirror with their rail frames.

## Open questions / blockers

- Manual live verification is required; the screenshots prove the intended
  side but static compilation cannot prove final mesh clipping.
- Other narrow-branch issues (`N178`, `vdlt`, `7n90`) remain separate from
  this both-diverge flangeway correction.
- `NCustom_ltci` / `SCustom_ttpp` neighboring ownership overlap remains open.
