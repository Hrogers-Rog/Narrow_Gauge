# Coordination Status

Last updated by: Claude - 2026-07-06

## Current phase: narrow-branch visual-gap fix + new diagnostic tooling deployed, awaiting live verification

User report: after items 1+2 landed and were properly deployed, a fresh
`Player.log` showed `objects=14, invalid=0`, but the user tested in-game and
still saw visually broken switches: small disconnected white rail/guard-rail
fragments near frogs and along diverging routes (screenshots). Claude traced
the strongest lead to two `Main.Warn(...)` "Rendering anyway" checks in
`SectionedSpecialWorkBuilder.cs` that had been downgraded from hard
validation failures. Codex fixed the underlying geometry across two turns
(`2b6cef8`, `916ee61`, both reviewed and agreed by Claude):

- `ResolveDivergingFixedStockRail` now prefers the anatomy/truth-table
  `narrow-reversed` stock rail instead of an arbitrary first narrow stock
  rail, which could be a fully suppressed shared/through duplicate.
- Measured blade roots reserve a short endpoint closure instead of running
  all the way to a route endpoint.
- Frog candidates rehome frog hardware off a rail already cut as a
  `SharedDuplicate` loser onto a nearby unsuppressed physical owner before
  frog cuts/wings/guards/sections are built, and duplicate frog hardware
  produced by rehoming onto the same physical rail pair is collapsed.
- The two named "Rendering anyway" checks are hard validation failures
  again - if the geometry is still wrong, it should show `invalid>0` instead
  of hiding behind `valid=True`.

User then gave real domain feedback (defects cluster around K-frog castings,
blade position/rotation) and asked for better diagnostics since neither
agent can see the running game. Claude added to
`SpecialWorkPlanExporter.cs`'s per-switch `.txt` export:

- **`PieceEndpoints`**: world-space position + tangent for every rendered
  piece's both endpoints (previously only rail-relative stationing existed).
- **`GeometryContinuity`**: flags a piece `ISOLATED` if neither endpoint
  meets another piece's endpoint in world space within 0.12m ("almost
  certainly a disconnected floating fragment in-game"), and flags
  `ANGLE MISMATCH` when two joined pieces' tangents differ by more than 20
  degrees (candidate for "rotational issues"). Use these two sections in the
  exported `.txt` files (`NarrowGauge/SpecialWorkPlans/*.txt`) to diagnose
  remaining visual defects without needing new screenshots each round.

## Verification

`dotnet build NarrowGaugeMod.csproj -p:RailroaderDir="C:\Steam\steamapps\common\Railroader" -p:EnableModDeploy=true`
- 0 warnings, 0 errors, deployed to
`C:\Steam\steamapps\common\Railroader\Mods\FUSE.NarrowGauge`, run
independently by both Codex and Claude across these turns.

Neither agent can launch Railroader. This is only static reasoning plus
build/deploy verification. The new diagnostic tooling itself is also
unverified against a real session - its output hasn't been read yet. The
user still needs to test in-game and provide a fresh `Player.log`
(and, if the diagnostic doesn't fully explain something, screenshots) before
anyone claims the visual bug is fixed.

## Switch-fix backlog

1. **Await live verification.** Check all five `dual.narrow-branch-joins-main`
   nodes (`N178`, `NCustom_7n90`, `NCustom_vdlt`, `NCustom_g832`, `Nove`) for
   log validity AND read their `.txt` exports' new `GeometryContinuity`
   section for `ISOLATED`/`ANGLE MISMATCH` lines before assuming anything is
   fixed. If validation goes `invalid>0` again, that's expected/working as
   intended now (the checks are hard failures again) - read which specific
   issue reappears.
2. Re-check `dual.split-standard-narrow` and `dual.both-diverge` the same
   way - they reported `valid=True` before, but that's not proof.
3. Re-check `dual.standard-branch-joins-main` (`NCustom_fl15`,
   `NDeHartPassing_33d6`) and `NCustom_g832`'s prior mismatch the same way.
4. Frog guard-rail/approach-section gaps (`NCustom_fc97`, `Npv2`) and any
   remaining blade under-build - revisit after the above.

## Standing rule

Do not trust `Player.log` `valid=True` as proof a fix worked. Read the new
`GeometryContinuity`/`PieceEndpoints` sections in each switch's `.txt`
export, and ask the user for in-game screenshots when the log/diagnostic
isn't conclusive. Do not "fix" a validator gap by relaxing it further - fix
the geometry, or restore a check to a hard failure once actually fixed.

## Next turn

User to launch Railroader with the deployed build and let it write a fresh
`Player.log`/`.txt` exports. Whoever picks this up next (Claude or Codex)
should read the new `GeometryContinuity` sections first - they should
narrow down remaining defects (if any) to specific pieces/positions without
needing new screenshots. Do not redeploy with plain `dotnet build`; always
use `-p:EnableModDeploy=true` for anything meant to be tested in-game.

## Open questions / blockers

Blocked on the user testing in-game and producing a fresh session (log +
`.txt` exports). Static/build verification cannot prove the floating
fragments are gone, and the new diagnostic tooling itself needs to be
checked against real data before it can be trusted either.
