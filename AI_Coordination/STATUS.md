# Coordination Status

Last updated by: Codex - 2026-07-09 22:41 EDT

## Critical testing constraints

- `NarrowGaugeMod.dll` is loaded only at full Railroader process startup. A
  save reload does not load this deployment.
- The user retired the automated Railroader/TestBridge pipeline. Do not launch
  or drive Railroader. Build/deploy is allowed; live verification is manual.

## Current phase: symmetric opposing-flangeway cutter fix deployed

Screenshot `221009` rejected both frog-point deformation experiments:
`NarrowReversedFrog` remained one railhead outside after its local curve push,
while the profile-hand reversal kinked and malformed `StandardThroughFrog`.
Both frog rails are now restored to their measured curves and hands.

The actual crossing clip uses ordered flangeway cutters: standard at index 0
and narrow at index 1. The both-diverge correction now flips only the opposing
cutter for each affected point:

- `StandardThroughFrog`: flip narrow cutter index 1;
- `NarrowReversedFrog`: flip standard cutter index 0;
- localize both cuts to the measured crossing-frog window;
- carry the same local window through `SpecialWorkAdjustmentUI` rebuilds.

`NarrowThroughFrog`, rail centerlines/hands/rotations, continuous handoff,
guards, and ordinary running rails are unchanged.

Built and deployed: 0 warnings, 0 errors. Built/deployed DLL timestamp
2026-07-09 22:41:25, size 737,792 bytes, SHA-256
`C770A49E4F94C984C05335BE73340654FCDEF5252244B5C9D4F6268F51D153A3` on both
copies. No game process was launched or controlled. Full evidence:
`reviews/frog-direction-gap-frame-investigation-2026-07-09.md`.

## Next turn

1. Fully quit and restart Railroader; a save reload is insufficient.
2. Repeat the fc97 `221009` view: `NarrowReversedFrog` should retain the inside
   side of the standard flangeway and meet the crossing point.
3. Confirm `StandardThroughFrog` follows its measured curve without a kink and
   is cut only inside the frog window.
4. Confirm `NarrowThroughFrog`, the continuous handoff, and guards remain in
   their prior positions.
5. Spot-check another both-diverge/mirror layout for cutter-index symmetry.

## Open questions / blockers

- Manual live verification is required; the screenshots prove the intended
  side but static compilation cannot prove final mesh clipping.
- Other narrow-branch issues (`N178`, `vdlt`, `7n90`) remain separate from
  this both-diverge flangeway correction.
- `NCustom_ltci` / `SCustom_ttpp` neighboring ownership overlap remains open.
