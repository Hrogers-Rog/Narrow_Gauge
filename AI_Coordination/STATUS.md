# Coordination Status

Last updated by: Claude - 2026-07-06

## Current phase: fixing broken special-work switches (log validity != visual correctness)

User report: narrow-gauge plain turnouts mostly work in-game; all 14
currently-measured dual-gauge special-work switches were broken in the live
2026-07-05/06 session. Items 1+2 landed and a fresh `Player.log` now shows
`objects=14, invalid=0` - **but the user tested in-game and confirmed several
switches are still visually broken** (disconnected floating rail/guard-rail
fragments near frogs and along diverging routes, from fresh screenshots).
`valid=True` in this mod's own log is necessary but not sufficient - see
`reviews/switch-validation-failures-2026-07-05.md`'s "Critical update"
section for the confirmed cause (two `Main.Warn(...)` "Rendering anyway"
checks in `SectionedSpecialWorkBuilder.cs` that were downgraded from hard
failures to warnings in earlier commits, silencing real geometry gaps
instead of fixing them).

**Standing rule going forward**: do not trust `Player.log` `valid=True` as
proof a fix worked. Ask the user for actual in-game screenshots/confirmation
before declaring a switch fixed. Do not "fix" a validator gap by relaxing it
further - if a check is currently a warning and the geometry it warns about
is actually broken, either fix the geometry or make the check a real failure
again once fixed.

Codex's usage-limit block (hit 2026-07-06) has cleared - confirmed available
again same day.

## Switch-fix backlog

1. **TOP PRIORITY - narrow-branch-joins-main rendering gaps behind relaxed
   warnings.** `ValidateSectionedDualGaugeSpecialWork` in
   `SectionedSpecialWorkBuilder.cs` (~line 2520-2597) logs `Main.Warn`
   ("Rendering anyway") instead of failing validation for: (a) a blade not
   connecting into a rendered closure/fixed section after its root distance,
   and (b) the diverging fixed narrow stock/running rail having no
   renderable role sections at all. Both fired in the fresh log across the
   `dual.narrow-branch-joins-main` nodes (`N178`, `NCustom_7n90`,
   `NCustom_vdlt`, `NCustom_g832`, `Nove` - all 5 now carry this preset after
   `Nove`/`NCustom_7n90` were reclassified from `dual.split-standard-narrow`
   this session). This is the best current lead for the user's screenshots
   of floating disconnected rail fragments. Investigate
   `ResolveDivergingFixedStockRail`/`HasApproachSection`/`RailRoleSection`
   construction and fix the actual geometry gap - do not just relax
   validation further.
2. `dual.split-standard-narrow` (item 1, done) and `dual.both-diverge`
   `SharedDuplicate` suppression (item 2, done) - both landed, both build
   clean, both now report `valid=True` in-game, but per the standing rule
   above still need the user's visual confirmation once item "1" above (the
   new top priority) is addressed, since some of the floating-fragment
   screenshots may overlap with these too.
3. `dual.standard-branch-joins-main` (`NCustom_fl15`, `NDeHartPassing_33d6`)
   and `NCustom_g832`'s prior rail-role mismatch: **both now separately
   report `valid=True`** in the same fresh log, without either of us having
   touched them - side effect of items 1+2, or of the narrow-branch
   reclassification. Do not assume this means they're actually correct
   in-game; re-check visually once the top-priority item is fixed.
4. Frog guard-rail/approach-section gaps (`NCustom_fc97`, `Npv2`) and any
   remaining blade under-build - re-check against a fresh log/screenshots
   after item 1 above, may already be resolved or may share its root cause.

## Verification

Neither agent can launch Railroader from this session. The user is actively
testing in-game and providing screenshots + fresh `Player.log` snapshots -
use those as the real verification signal, not just build success or
`valid=True`. Deploy each build for testing with:
`dotnet build NarrowGaugeMod.csproj -p:RailroaderDir="C:\Steam\steamapps\common\Railroader" -p:EnableModDeploy=true`
(plain `dotnet build` without `EnableModDeploy=true` does NOT update the
installed mod the user actually plays with - confirmed this session the
installed DLL was stale for weeks before this was caught).

## Still open

Top-priority item above (narrow-branch rendering gaps). Re-verification of
everything else once that's fixed and the user has re-tested in-game.

## Next turn

Codex - top-priority item above. Read
`reviews/switch-validation-failures-2026-07-05.md`'s "Critical update"
section first, then the LOG.md tail for full context on what's landed so
far and why `valid=True` can't be trusted alone. After your fix, remind
whoever's turn is next to deploy with `-p:EnableModDeploy=true` and ask the
user to re-test in-game before declaring anything done.

## Open questions / blockers

None blocking Codex from starting the top-priority item. Do need the user's
continued help testing in-game and sharing fresh screenshots/`Player.log`
once a fix for it lands - static/build verification alone is not enough for
this class of bug, as this session already demonstrated twice.
