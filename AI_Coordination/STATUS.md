# Coordination Status

Last updated by: Codex - 2026-07-06

## Current phase: fixing broken special-work switches

User report: narrow-gauge plain turnouts mostly work in-game; all 14
currently-measured dual-gauge special-work switches were broken in the live
2026-07-05/06 session (`Special-work analysis: objects=14, invalid=14`), with
a visible symptom of a disconnected vertical rail stub floating above
otherwise normal-looking track. `Fuse_geometry_engine` is not the vehicle for
this fix; this repo is being fixed in place.

Full evidence and root-cause notes:
`reviews/switch-validation-failures-2026-07-05.md`.

## Switch-fix backlog

1. **`dual.split-standard-narrow` derives zero blades** - code fix landed by
   Codex this turn; build succeeds. Changes:
   `dual.split-standard-narrow` now expects one movable assembly, the
   sectioned builder uses a dedicated measured split-blade fallback that can
   choose standard/narrow or generated narrow-through/narrow-diverge
   candidates, and the switch-geometry patch bypasses any exactly-one-hidden
   gauge-separation control pair so `Nove` should no longer fall into native
   `SwitchGeometry.Calculate` for hidden-control plus real narrow branch.
   Needs live in-game verification from the user: `Nove` and
   `NCustom_7n90` should report `valid=True`, and `Player.log` should no
   longer contain the captured `SwitchGeometry.Calculate` failure for
   `fuse-ng:n:Nove`.
2. **`dual.both-diverge` missing `SharedDuplicate` suppression** - still open.
   Five of seven instances (`NCustom_l4a4`, `NCustom_ltci`, `NCustom_p997`,
   `NCustom_u6n0`, `NDeHartPassing_wqbb`) fail with missing
   `SharedDuplicate`; still the best match for the floating-stub visual
   symptom.
3. **`dual.standard-branch-joins-main` never attempted** - still open. Both
   instances (`NCustom_fl15`, `NDeHartPassing_33d6`) fall back before measured
   special-work validation.
4. Blade under-build (`N178`, `NCustom_vdlt`, `NCustom_fc97`) and frog
   guard-rail/approach-section gaps (`NCustom_fc97`, `Npv2`) - still open.
5. `NCustom_g832` rail-role mismatch - still open.

Do not treat `docs/special-work-turnout-combo-status.md`'s `DONE` markings as
current truth; update it only after live re-verification.

## Verification

Codex verified this turn:

- `dotnet build NarrowGaugeMod.csproj -p:RailroaderDir="C:\Steam\steamapps\common\Railroader"` succeeds with 0 warnings and 0 errors.
- Reviewed current sectioned special-work validator/truth-table paths and the
  exported stale plan evidence for `Nove`/`NCustom_7n90`.

Still needs live in-game verification:

- Launch Railroader with this committed build, load the affected map, and
  confirm via fresh `Player.log` that `Nove` and `NCustom_7n90` are valid.
- Confirm no captured `SwitchGeometry.Calculate` failure remains for
  `fuse-ng:n:Nove`.
- Visually inspect the two split-standard-narrow switches.

## Next turn

Claude - review Codex's item 1 fix and the updated review note, then either
ask the user for the live verification above or continue with backlog item 2
if you judge the code/build verification sufficient to proceed.

## Open questions / blockers

Need a live in-game re-test for item 1; Codex cannot launch Railroader from
this session.
