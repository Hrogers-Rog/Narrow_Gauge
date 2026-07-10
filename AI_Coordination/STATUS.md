# Coordination Status

Last updated by: Codex - 2026-07-09 23:31 EDT

## Critical testing constraints

- `NarrowGaugeMod.dll` is loaded only at full Railroader process startup. A
  save reload does not load this deployment.
- The user retired the automated Railroader/TestBridge pipeline. Do not launch
  or drive Railroader. Build/deploy is allowed; live verification is manual.

## Current phase: broken cutter inversion rolled back

Screenshot `232828` rejects the `23:25` index-1 inversion: it broke both
visible cutter results. The two flangeway clips are intersected retained
half-planes, so negating either keep sign changes the entire surviving wedge;
it cannot mirror one railhead face independently.

The recovery build restores the cutter behavior from `3290db4`:

- `StandardThroughFrog` and `NarrowReversedFrog` both derive keep signs from
  their measured fixed-piece anchor;
- automatic inversion is false and its index is `-1`;
- the wrong-side overlay fix remains intact;
- the rejected curve pushes/profile-hand changes remain removed.

The original `NarrowReversedFrog` inside-versus-outside bevel remains open.
The next correction must move or reconstruct its narrow-through cut boundary,
not invert either retained half-plane.

Built and deployed: 0 warnings, 0 errors. Built/deployed DLL timestamp
2026-07-09 23:31:13, size 737,280 bytes, SHA-256
`4AA7E65E1D2553738C83A9DFF537926BF7C9361E90FC44987C8AA314CC17A3CC` on both
copies. No game process was launched or controlled. Full evidence:
`reviews/frog-direction-gap-frame-investigation-2026-07-09.md`.

## Next turn

1. Fully quit and restart Railroader; a save reload is insufficient.
2. Confirm the `232828` two-cutter regression is gone and the visual returns to
   the pre-experiment `231931` state.
3. Preserve the proven facts: the reverse frog is deep enough, the narrow
   through rail is the correct cutter, and only its boundary face is wrong.
4. Implement the next attempt by reflecting/translating that cut boundary while
   retaining both measured keep half-planes.
5. Keep `StandardThroughFrog`, the continuous frog, spans, and rail hands
   unchanged.

## Open questions / blockers

- Manual live verification is required; the screenshots prove the intended
  side but static compilation cannot prove final mesh clipping.
- Other narrow-branch issues (`N178`, `vdlt`, `7n90`) remain separate from
  this both-diverge flangeway correction.
- `NCustom_ltci` / `SCustom_ttpp` neighboring ownership overlap remains open.
