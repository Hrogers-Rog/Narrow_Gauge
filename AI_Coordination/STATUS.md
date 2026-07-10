# Coordination Status

Last updated by: Codex - 2026-07-09 21:00 EDT

## Critical testing constraints

- `NarrowGaugeMod.dll` is loaded only at full Railroader process startup. A
  save reload does not load this deployment.
- The user retired the automated Railroader/TestBridge pipeline. Do not launch
  or drive Railroader. Build/deploy is allowed; live verification is manual.

## Current phase: hand-aware fc97 handoff and non-regressing guard fix deployed

The previous build moved fc97's local crossing guard correctly, but regressed
the corresponding guards on the other switches. The user also confirmed that
the second new screenshot (`204917`) is fc97 and that its continuous handoff is
still displaced by exactly one railhead width.

Both generated kinked-rail helpers forced `Hand.Left`. Railroader's asymmetric
rail profile makes a wrong hand visible as exactly one full head-width lateral
offset. fc97's measured standard crossing rail and narrow guard owner are
right-hand curves. The deployed correction therefore:

1. gives the continuous stock handoff `standardRail.Curve.hand`;
2. gives the local guard diagonal `guardOwner.Curve.hand` and the comparison
   handoff its standard owner's hand;
3. restores the original `RailHeadWidth` guard-center compensation, which the
   other switches require.

This should retain fc97's live-confirmed guard location through the opposing
profile-hand and centerline corrections while restoring the other guards. It
also shifts fc97's continuous handoff by exactly the reported one head width.
The earlier inside-flangeway cutter inversion/localization remains unchanged.
No frog spans, cut distances, counts, or node ids changed.

Built and deployed: 0 warnings, 0 errors. Built/deployed DLL timestamp
2026-07-09 20:59:50, size 737,792 bytes, SHA-256
`3850E8CD4E322223ACE9D42C4D27B3D15E6794E1E171390164B01E7C9BCC3785` on both
copies. No game process was launched or controlled. Full evidence:
`reviews/frog-direction-gap-frame-investigation-2026-07-09.md`.

## Next turn

1. Fully quit and restart Railroader; a save reload is insufficient.
2. At fc97, confirm the continuous handoff now joins the intended railhead
   instead of running one head width beside it, while its guard stays in the
   position shown as correct in the prior live test.
3. Recheck the guard in the first new screenshot and another previously good
   switch. Restoring the centerline compensation with the real curve hand
   should remove the global guard regression.
4. Hide fc97's continuous frog again and confirm the point is cut/kept on the
   red inside edge, not the blue outside edge.
5. Confirm original point lengths and boundary coverage remain intact.

## Open questions / blockers

- Manual live verification is required; the screenshots prove the intended
  side but static compilation cannot prove final mesh clipping.
- Other narrow-branch issues (`N178`, `vdlt`, `7n90`) remain separate from
  this both-diverge flangeway correction.
- `NCustom_ltci` / `SCustom_ttpp` neighboring ownership overlap remains open.
