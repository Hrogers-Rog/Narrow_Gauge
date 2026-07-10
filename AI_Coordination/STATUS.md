# Coordination Status

Last updated by: Codex - 2026-07-09 21:35 EDT

## Critical testing constraints

- `NarrowGaugeMod.dll` is loaded only at full Railroader process startup. A
  save reload does not load this deployment.
- The user retired the automated Railroader/TestBridge pipeline. Do not launch
  or drive Railroader. Build/deploy is allowed; live verification is manual.

## Current phase: narrow point-hand regression removed; standard-only correction deployed

The three-path point-hand build was live-rejected. In screenshot `212930`,
`NarrowThroughFrog` moved left by exactly one railhead width and some
`NarrowReversedFrog` meshes retained only half a rail. This directly identifies
the new narrow hand reversals as regressions: the asymmetric head moved while
the existing flangeway half-planes stayed fixed.

Both narrow paths now retain their original measured hands again. The inward
profile correction remains only on the standard outside point copy,
`StandardThroughFrog`, matching the user's narrower outside-stock report. The
helper is renamed `FaceBothDivergeStandardCrossingPointInward` to make that
scope explicit.

No cutter, cut window, span, centerline, continuous handoff, guard, or ordinary
running-rail changes were made.

Built and deployed: 0 warnings, 0 errors. Built/deployed DLL timestamp
2026-07-09 21:35:11, size 737,792 bytes, SHA-256
`98FBEC1381450E4344011D0452C0E29B0C7E14381ECAECFDEA5B349733426437` on both
copies. No game process was launched or controlled. Full evidence:
`reviews/frog-direction-gap-frame-investigation-2026-07-09.md`.

## Next turn

1. Fully quit and restart Railroader; a save reload is insufficient.
2. Repeat the `212930` view: `NarrowThroughFrog` should return one head width
   to its measured alignment and `NarrowReversedFrog` should render its full
   head again.
3. At fc97, confirm the standard outside point remains projected inward as
   intended by the `211109` report.
4. Confirm the continuous handoff and guard remain unchanged.
5. Spot-check l4a4 and a mirror-hand switch such as p997/ltci.

## Open questions / blockers

- Manual live verification is required; the screenshots prove the intended
  side but static compilation cannot prove final mesh clipping.
- Other narrow-branch issues (`N178`, `vdlt`, `7n90`) remain separate from
  this both-diverge flangeway correction.
- `NCustom_ltci` / `SCustom_ttpp` neighboring ownership overlap remains open.
