# Coordination Status

Last updated by: Codex - 2026-07-09 23:04 EDT

## Critical testing constraints

- `NarrowGaugeMod.dll` is loaded only at full Railroader process startup. A
  save reload does not load this deployment.
- The user retired the automated Railroader/TestBridge pipeline. Do not launch
  or drive Railroader. Build/deploy is allowed; live verification is manual.

## Current phase: wrong-side extended-frog overlay rollback deployed

Screenshots `224542`, `224656`, and close-up `224732` prove the correct
continuous frog is still present. The defect is the separately rendered
`StandardThroughFrog` / `NarrowReversedFrog` extension surviving over it and
clipping through it on every `dual.both-diverge` switch. g832 is an unaffected
`dual.narrow-branch-joins-main` comparison.

Each flangeway-cut mesh already computes its retained side from a `keepPoint`
on the real measured fixed piece. The both-diverge cutter inversion negated
that ownership anchor and could retain the extension on the opposite side of
the frog. The local cut window only confined the invalid remnant.

Automatic keep-side inversion is now disabled again for both frog roles, and
the added local-window arguments were removed from initial rendering and
adjustment reconstruction. The measured rail curves/hands, continuous frog,
`NarrowThroughFrog`, handoff, guards, and ordinary rails remain unchanged.

Built and deployed: 0 warnings, 0 errors. Built/deployed DLL timestamp
2026-07-09 23:04:46, size 737,280 bytes, SHA-256
`6D31FCA3EED9D6E38D365A14E0DEA94C8B0965E7DAC366E50C6BE00596CADCA1` on both
copies. No game process was launched or controlled. Full evidence:
`reviews/frog-direction-gap-frame-investigation-2026-07-09.md`.

## Next turn

1. Fully quit and restart Railroader; a save reload is insufficient.
2. Repeat the `224732` close-up: the extended `StandardThroughFrog` and
   `NarrowReversedFrog` meshes must no longer survive over the continuous frog.
3. Compare one both-diverge switch with g832 for absence of the overlay/cuts.
4. Reassess the original fc97 point alignment only after the overlay regression
   is confirmed gone; do not reintroduce keep-side inversion.
5. Confirm `NarrowThroughFrog`, the continuous handoff, and guards remain in
   their prior positions.

## Open questions / blockers

- Manual live verification is required; the screenshots prove the intended
  side but static compilation cannot prove final mesh clipping.
- Other narrow-branch issues (`N178`, `vdlt`, `7n90`) remain separate from
  this both-diverge flangeway correction.
- `NCustom_ltci` / `SCustom_ttpp` neighboring ownership overlap remains open.
