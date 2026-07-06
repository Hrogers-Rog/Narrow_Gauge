# Coordination Status

Last updated by: Codex - 2026-07-06

## Current phase: narrow-branch visual-gap fix deployed, awaiting live verification

User report: after items 1+2 landed and were properly deployed, a fresh
`Player.log` showed `objects=14, invalid=0`, but the user tested in-game and
still saw visually broken switches: small disconnected white rail/guard-rail
fragments near frogs and along diverging routes. Claude traced the strongest
lead to two `Main.Warn(...)` "Rendering anyway" checks in
`SectionedSpecialWorkBuilder.cs` that had been downgraded from hard
validation failures: blade root not connecting to rendered closure/fixed
geometry, and fixed diverging narrow stock/running rail having no renderable
sections.

Codex implemented the code-side fix this turn:

- `ResolveDivergingFixedStockRail` now prefers the anatomy/truth-table
  `narrow-reversed` stock rail instead of the first narrow stock rail, which
  could be a fully suppressed shared/through duplicate.
- measured blade roots now reserve a short endpoint closure if the root would
  otherwise run all the way to a route endpoint, addressing the `NCustom_7n90`
  blade-root gap path.
- frog candidates now rehome frog hardware off a rail already cut as a
  `SharedDuplicate` loser onto a nearby unsuppressed physical owner before
  frog cuts, wings, guards, and sections are built. This targets the N178-style
  path where a shared duplicate could still own frog/wing hardware and render
  detached fragments.
- follow-up tightening collapses duplicate frog hardware if rehomed candidates
  land on the same physical rail pair, and chooses the closest unsuppressed
  owner before family tie-breaks.
- the two named "Rendering anyway" checks are hard validation failures again.
  If the geometry is still wrong in a fresh game run, it should show up as
  `invalid>0` instead of being hidden behind `valid=True`.

## Verification

Codex ran the required build/deploy command:

`dotnet build NarrowGaugeMod.csproj -p:RailroaderDir="C:\Steam\steamapps\common\Railroader" -p:EnableModDeploy=true`

Result: build succeeded with 0 warnings and 0 errors, and MSBuild reported
`Deployed NarrowGaugeMod to C:\Steam\steamapps\common\Railroader\Mods\FUSE.NarrowGauge`.

Neither agent can launch Railroader here. This is only static reasoning plus
build/deploy verification. The user still needs to test in-game and provide a
fresh `Player.log`/screenshots before anyone claims the visual bug is fixed.

## Switch-fix backlog

1. **Await live verification of the narrow-branch visual-gap fix.** Check all
   five `dual.narrow-branch-joins-main` nodes (`N178`, `NCustom_7n90`,
   `NCustom_vdlt`, `NCustom_g832`, `Nove`) for both log validity and actual
   visuals. In the fresh `Player.log`, specifically look for any hard
   validation issues replacing the old warnings:
   `Blade '...' does not connect into a rendered closure/fixed section...` and
   `Fixed diverging narrow stock/running rail has no renderable role sections.`
2. Re-check `dual.split-standard-narrow` and `dual.both-diverge` visually.
   They reported `valid=True` after earlier fixes, but the standing rule is
   that `valid=True` alone is not proof.
3. Re-check `dual.standard-branch-joins-main` (`NCustom_fl15`,
   `NDeHartPassing_33d6`) and prior `NCustom_g832` mismatch visually. They now
   report `valid=True`, but that may only mean the validator path changed.
4. Frog guard-rail/approach-section gaps (`NCustom_fc97`, `Npv2`) and any
   remaining blade under-build should be revisited after the user confirms or
   refutes the narrow-branch fix in game.

## Standing rule

Do not trust `Player.log` `valid=True` as proof a fix worked. Ask the user for
actual in-game screenshots/confirmation before declaring a switch fixed. Do not
"fix" a validator gap by relaxing it further. If a check is currently a
warning and the geometry it warns about is actually broken, fix the geometry or
make the check a real failure again once fixed.

## Next turn

Claude - review Codex's diff, then have the user launch Railroader with the
deployed DLL and report fresh screenshots plus `Player.log`. Do not redeploy
with plain `dotnet build`; use `-p:EnableModDeploy=true` for any further build
that should be tested in-game.

## Open questions / blockers

Blocked on user in-game verification. Static/build verification cannot prove
the floating fragments are gone.
