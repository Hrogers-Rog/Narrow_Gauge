# Coordination Status

Last updated by: Codex - 2026-07-09 21:48 EDT

## Critical testing constraints

- `NarrowGaugeMod.dll` is loaded only at full Railroader process startup. A
  save reload does not load this deployment.
- The user retired the automated Railroader/TestBridge pipeline. Do not launch
  or drive Railroader. Build/deploy is allowed; live verification is manual.

## Current phase: frog-local NarrowReversed point push deployed

The narrow hand rollback restored full-width rendering, but screenshot
`214109` proves `NarrowReversedFrog`'s frog end remains outside the frog by
exactly one railhead width. This distinguishes a local point displacement from
a profile-hand correction: the approach end and measured hand are correct,
while only the frog-local nose must move inward.

`PushBothDivergeNarrowReversedPointIntoFrog` now applies that geometry only to
the both-diverge `NarrowReversedFrog` path:

- zero lateral offset at the crossing cut boundaries/approach seam;
- a smooth increase to one `HeadWidth` at the measured frog center;
- signed inward direction derived from the curve's own hand;
- rebuilt curve rotations but unchanged hand.

`NarrowThroughFrog`, all flangeway cutters/keep-side rules, the continuous
handoff, guards, and ordinary running rails are unchanged. The prior
standard-only point correction also remains unchanged.

Built and deployed: 0 warnings, 0 errors. Built/deployed DLL timestamp
2026-07-09 21:48:04, size 738,304 bytes, SHA-256
`67877FCF903C39801BCF2127EABDC70F929F3B4753D50B1AD5CF85AE8EE89896` on both
copies. No game process was launched or controlled. Full evidence:
`reviews/frog-direction-gap-frame-investigation-2026-07-09.md`.

## Next turn

1. Fully quit and restart Railroader; a save reload is insufficient.
2. Repeat the `214109` view: the `NarrowReversedFrog` approach should retain
   its full-width alignment while its frog-local nose moves inward one head
   width.
3. Confirm `NarrowThroughFrog` remains in the restored measured position from
   the prior build.
4. Confirm the standard point, continuous handoff, and guard remain unchanged.
5. Spot-check a mirror-hand switch for the signed local push.

## Open questions / blockers

- Manual live verification is required; the screenshots prove the intended
  side but static compilation cannot prove final mesh clipping.
- Other narrow-branch issues (`N178`, `vdlt`, `7n90`) remain separate from
  this both-diverge flangeway correction.
- `NCustom_ltci` / `SCustom_ttpp` neighboring ownership overlap remains open.
