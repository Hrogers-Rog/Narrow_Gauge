# Coordination Status

Last updated by: Codex - 2026-07-09 23:25 EDT

## Critical testing constraints

- `NarrowGaugeMod.dll` is loaded only at full Railroader process startup. A
  save reload does not load this deployment.
- The user retired the automated Railroader/TestBridge pipeline. Do not launch
  or drive Railroader. Build/deploy is allowed; live verification is manual.

## Current phase: NarrowReversed narrow-through bevel mirror deployed

The `23:04` rollback removed the wrong-side overlaid extensions on most
switches. Screenshots `231157` and close-up `231931` isolate the remaining
defect: `NarrowReversedFrog` is already deep enough and is being cut, but the
narrow-through rail removes the outside face instead of the inside face.

The ordered cutters are standard index 0 and narrow index 1. The rejected
symmetric build flipped index 0 for `NarrowReversedFrog`, which controlled the
crossing-side extension and caused the overlay. The current build instead:

- mirrors only narrow-through cutter index 1;
- applies only to both-diverge objects named `NarrowReversedFrog`;
- leaves `StandardThroughFrog`, index 0, and `NarrowThroughFrog` unchanged;
- does not alter spans, rail curves/hands, or the continuous frog.

The adjustment/rebuild path consumes the same role-based auto-mirror rule.

Built and deployed: 0 warnings, 0 errors. Built/deployed DLL timestamp
2026-07-09 23:25:48, size 737,280 bytes, SHA-256
`4B9806ED5909F7B7BEB25B477228197A333BAE303D77A21044C5DF70A7F4DFDE` on both
copies. No game process was launched or controlled. Full evidence:
`reviews/frog-direction-gap-frame-investigation-2026-07-09.md`.

## Next turn

1. Fully quit and restart Railroader; a save reload is insufficient.
2. Repeat the `231931` close-up: `NarrowReversedFrog` should keep the outside
   railhead face and bevel from the inside/narrow-through side.
3. Confirm it remains pushed to the same depth; this build must not move its
   span or approach seam.
4. Confirm `StandardThroughFrog` keeps the proper cut restored by the rollback
   and that the `224732` overlay remains absent.
5. Spot-check a switch that emits `NarrowThroughFrog`; it is unchanged.

## Open questions / blockers

- Manual live verification is required; the screenshots prove the intended
  side but static compilation cannot prove final mesh clipping.
- Other narrow-branch issues (`N178`, `vdlt`, `7n90`) remain separate from
  this both-diverge flangeway correction.
- `NCustom_ltci` / `SCustom_ttpp` neighboring ownership overlap remains open.
