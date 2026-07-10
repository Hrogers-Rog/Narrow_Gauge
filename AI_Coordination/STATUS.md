# Coordination Status

Last updated by: Codex - 2026-07-09 20:14 EDT

## Critical testing constraints

- `NarrowGaugeMod.dll` is loaded only at full Railroader process startup. A
  save reload does not load this deployment.
- The user retired the automated Railroader/TestBridge pipeline. Do not launch
  or drive Railroader. Build/deploy is allowed; live verification is manual.

## Current phase: systemic double-frog renderer regression rolled back

After restarting with commit `826054a`, the user supplied four screenshots
and reported:

- malformed double frogs at `l4a4`, `fc97`, and `N178`;
- a large empty cut plus malformed frog work at `NCustom_7n90`;
- overlapping/wrong V and double-frog anatomy at `NCustom_vdlt`;
- inside-out rail pieces.

Fresh 19:45 `Player.log` evidence made four independent general causes
concrete:

1. Frog kind compared route-relative `Left`/`Right` without reversing that
   relationship when route tangents oppose. `vdlt` has two such opposed-route
   intersections.
2. `N178` rehomes an accepted frog from `narrow-normal:right` to
   `standard-through:left`, but retained its pre-rehome V kind and dimensions.
3. `SCustom_194b` has a gauge-separation cut at `20.832-23.761` outside its
   valid measured ownership/replacement span. The valid-plan control shell
   suppressed all procedural replacement hardware, leaving the cut empty.
4. Every standard/narrow crossing was sent through narrow-branch continuous
   stock-handoff geometry, including `dual.both-diverge` switches such as
   `fc97`/`l4a4`. A generic crossing-point replacement was investigated.

Inside-out geometry had two related causes: frame normalization covered only
the left narrow-branch truth-table hand, and procedural reversed slices used
raw `LineCurve.Reverse()` with stale per-point rotations.

Implemented:

- direction-aware physical-side frog classification in prototype and accepted
  plan stages;
- full reclassification/recalculation after frog physical-owner replacement;
- measured-plan-aware gauge-separation supplementation that renders only
  uncovered procedural frog sites and never adds a blade to a valid plan;
- render-frame correction for all `DualNarrowBranch` plans and hand-aware
  procedural curve reversal.

The first full restart showed that the attempted generic both-diverge
crossing renderer caused a systemic regression: every inspected double frog
had a long empty cut span. Fresh logs showed all plans still valid with normal
frog counts, isolating the renderer swap rather than classification or plan
generation. The user confirmed G832 was unaffected; because G832 is a
narrow-branch preset, that is an additional negative control isolating the
both-diverge-only branch. That one renderer change is now rolled back. Both-diverge
standard/narrow crossings again use the previously live-confirmed continuous
stock handoff. `fc97`/`l4a4`'s original localized issue remains open.

No switch ids are used in the fixes. Full evidence and implementation details:
`reviews/frog-direction-gap-frame-investigation-2026-07-09.md`.

Rollback built and deployed against the real Railroader install: 0 warnings,
0 errors. Built/deployed DLL timestamp 2026-07-09 20:14:22, size 737,280 bytes. No game
process was launched or controlled.

## Next turn

1. Fully quit and restart Railroader, then first confirm the systemic long-gap
   regression is gone on a both-diverge double frog. The rollback cannot load
   through a save reload.
2. Then inspect `l4a4`, `fc97`, `N178`, `NCustom_7n90`, and `NCustom_vdlt` in
   both switch states where relevant.
3. Confirm the 7n90 gap is filled without adding another blade. The fresh log
   should report gauge-separation supplemental hardware with `frogs=1`,
   `covered=1`, `blade=0`.
4. Re-evaluate N178's frog: the first post-change log did not emit a
   reclassification because the final owner tangents still classify it as V.
5. Confirm vdlt no longer places the V and double frog on the wrong physical
   crossings, and that no tapered rail profiles are inside-out.
6. Spot-check `Nove`, `G832`, and one previously good both-diverge turnout for
   regression. Then review the fresh plan summaries and frog-kind logs.

## Open questions / blockers

- Manual live verification is required; static build cannot prove final scene
  overlap, animation, or mesh winding.
- `NCustom_ltci` / `SCustom_ttpp` neighboring ownership overlap remains open
  and is outside this change.
