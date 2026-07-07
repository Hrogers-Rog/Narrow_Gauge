# Coordination Status

Last updated by: Claude - 2026-07-07 17:10

## Standing rule change: automated TestBridge/live-launch pipeline retired for now

The user asked to stop using the automated Railroader.exe launch/TestBridge
pipeline entirely - it caused repeated game restarts and process collisions
between concurrent Claude/Codex turns this session (twice this turn alone:
once because a stray process from an earlier attempt was still running,
once because Codex's own live-test session was still up). **Do not launch or
drive `Railroader.exe` from either agent until the user says otherwise.**
Live verification (close-up screenshots, throw/close checks, fresh
`Player.log`/plan exports) is manual-only for now - the user will do it
themselves. Static code review plus whatever plan-export/log artifacts
already exist on disk (noting their age) is the only verification available
to either agent in the meantime.

## Current phase: narrow-branch group static review found a likely regression (`NCustom_7n90`/`NCustom_g832` now `valid=False`); both-diverge duplicate guard defect fixed and live-verified last turn; ownership boundary still open

Codex re-ran the live-game pipeline against save `2026-06-25` and forced
fresh `exportPlans` twice this turn. The first run re-verified the deployed
fixes; one fix was incomplete, so Codex applied a narrow follow-up patch and
re-ran the live export/screenshots.

## What changed this turn

`src/SectionedSpecialWorkBuilder.cs` now adds a geometric endpoint de-dup
check inside `AddSupplementalGuardPair`. The earlier semantic check skipped
only duplicate `(FrogId, OppositeRunningRail)` pairs, but fresh live exports
proved the remaining duplicate guards at `NCustom_p997`, `NCustom_ltci`, and
`NDeHartPassing_wqbb` used different route-derived opposite rails while
resolving to the exact same physical start/end points. The new check flares
the supplemental guard first, then skips it if its final endpoints match an
existing guard curve within `0.01m` in either direction.

No truth JSON files were edited.

## Live verification

Build/deploy after the patch succeeded:

```powershell
dotnet build .\NarrowGaugeMod.csproj -p:RailroaderDir="C:\Steam\steamapps\common\Railroader" -p:EnableModDeploy=true
```

Result: 0 warnings, 0 errors.

Fresh post-patch exports were written at `2026-07-07 16:46:04` local time.
Guard duplicate scan across all seven both-diverge nodes:

- `NCustom_p997`: `guards=7`, duplicate guard endpoint groups `0`.
- `NCustom_ltci`: `guards=7`, duplicate guard endpoint groups `0`.
- `NCustom_u6n0`: `guards=7`, duplicate guard endpoint groups `0`.
- `NDeHartPassing_wqbb`: `guards=7`, duplicate guard endpoint groups `0`.
- `NCustom_fc97`: `guards=9`, duplicate guard endpoint groups `0`.
- `NCustom_l4a4`: `guards=7`, duplicate guard endpoint groups `0`.
- `Npv2`: `guards=7`, duplicate guard endpoint groups `0`.

Close-up screenshots captured after UMM close:

- `C:\Users\roger\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE-test-shots\codex-bothdiverge-NCustom_p997-20260707-postfix.png`
- `C:\Users\roger\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE-test-shots\codex-bothdiverge-NCustom_ltci-20260707-postfix.png`
- `C:\Users\roger\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE-test-shots\codex-bothdiverge-NDeHartPassing_wqbb-20260707-postfix-offset180.png`

The screenshots are aimed at the target switchwork and no longer show the
old exact stacked guard line. This closes the exact duplicate guard endpoint
defect only; it does not prove every crossing-handoff/synthesized-frog
concern in the both-diverge group is fixed.

Cleanup was verified directly: `tasklist` showed no `Railroader.exe`,
`Mods\FUSE.TestBridge\Info.json` read back `"Enabled": false`,
`steam_appid.txt` was absent, no `test_request_*.json` /
`test_result_*.json` remained, and no Narrow Gauge bridge request/result
files remained. The active `Mods\FUSE.TestBridge` folder was missing
`FUSE.Core.dll`; Codex copied it from `Mods.fuseGEo\FUSE.TestBridge` only for
the live test and removed it during cleanup to restore the pre-run state.

## Remaining issues

The `OwnershipCuts` source-route filter is active, but live data shows it is
not sufficient for `NCustom_ltci`:

- `SCustom_ttpp` is still double-claimed by `special-work:NCustom_fl15` and
  `special-work:NCustom_ltci`. Latest cuts include `fl15` at
  `0.120-1.457` / `0.120-1.466` and `ltci` at `0.120-2.028` /
  `0.120-2.017`.
- `SCustom_snvo` is only claimed by `ltci` in this run, but
  `NCustom_g832` is invalid, so this is not proof the boundary problem is
  solved there.
- `SCustom_6wx3` is only claimed by `p997` in this run, also with
  `NCustom_g832` invalid.

Fresh `Player.log` created runtime-only gauge-separation controls only for
`Nove` and `NCustom_7n90`, not for any both-diverge node
(`NCustom_p997`, `NCustom_ltci`, `NCustom_u6n0`,
`NDeHartPassing_wqbb`, `NCustom_fc97`, `NCustom_l4a4`, `Npv2`). Therefore
Claude's hidden-control gap fix does not apply to the both-diverge group in
this save, and there was no both-diverge control-stub gap to confirm.

This load reports `Special-work analysis: objects=14, invalid=2`.
The invalid plans are `NCustom_7n90` and `NCustom_g832`, both failing with
`Fixed diverging narrow stock/running rail has no renderable role sections`.

Nove's frog position/shape remains open from Claude's prior turn.

## This turn (Claude): static-only review of the remaining narrow-branch group

Assigned to live-verify fixes 1-5 against `N178`, `NCustom_7n90`,
`NCustom_g832`, `NCustom_vdlt` (the 4 `dual.narrow-branch-joins-main` nodes
other than `Nove`, already extensively tested). Two live-pipeline
interruptions happened (a stray-process collision with Codex's own run, then
the user's request to stop the pipeline entirely - see above), so this turn
ended up static-only: no fresh close-up screenshots, no fresh throw/close
check. Full findings appended to
`AI_Coordination/reviews/ncustom-7n90-194b-investigation-2026-07-07.md`
("Follow-up" section). Summary:

- Read the freshest available plan exports on disk (Codex's own
  `2026-07-07 16:46:04` export, built from current HEAD `2330890`):
  - `N178`: `valid=True`, `blades=1`, no gauge-separation control mechanism.
  - `NCustom_7n90`: **`valid=False`** - `Fixed diverging narrow
    stock/running rail has no renderable role sections.` `blades=1`. Has the
    5m-gap control mechanism (`fuse-ng:n:NCustom_7n90:control` created in
    logs), but likely moot since the measured build may be skipped entirely
    while invalid.
  - `NCustom_g832`: **`valid=False`**, same failure text, `blades=1`. No
    gauge-separation control mechanism. Codex's log confirms `[Build]
    Skipping measured special-work ... customAllowed=False` for this node -
    i.e. it is not rendering any of this session's fixes right now.
  - `NCustom_vdlt`: `valid=True`, `blades=1`, no gauge-separation control
    mechanism.
- All four now generate exactly 1 blade (the one-blade shared-side fix is
  active across the whole fallback path), but 2 of 4 regressed to
  `valid=False` as a side effect. The 2026-07-06 pre-fix baseline exports
  (`blades=2`) show these same two nodes as `valid=True`, so this is new
  today, not pre-existing. Root cause traced to `ResolveDivergingFixedStockRail`
  (`src/SectionedSpecialWorkBuilder.cs` ~line 3355) resolving to a rail with
  zero renderable sections once only one blade candidate remains - but
  *why* it resolves differently for these two nodes vs. `N178`/`NCustom_vdlt`
  could not be determined from static reading alone (wing/guard counts don't
  correlate: `NCustom_g832` and `NCustom_vdlt` have identical
  `wings=8,guards=7` but opposite validity). Needs a live diagnostic log of
  the resolved rail id per node - do not attempt a source fix without one,
  per this session's standing rule.
- Confirmed (whole-session `Player.log` transcript grep) that of this
  4-node group, only `NCustom_7n90` ever creates the runtime-only
  gauge-separation control stub; `N178`/`NCustom_g832`/`NCustom_vdlt` do not
  use that mechanism at all, so the 5m-gap fix (`f5ad56b`) has nothing to
  verify on those three regardless of pipeline availability.
- Fixes 1-3 (blade orientation, end-cap hand-awareness, `LineCurve.Reverse`)
  are implemented generically with no per-node id checks, so they should
  apply uniformly across this group - but this is inference from reading the
  call sites, not a live-confirmed claim.

No source files were changed this turn - the `NCustom_7n90`/`NCustom_g832`
regression is reported, not fixed, per the "don't fix without live evidence"
instruction for this turn.

## Next turn

Whoever picks this up next (user will run live verification manually until
the pipeline is un-retired):

1. Get a fresh `Player.log`/plan export for `NCustom_7n90` and confirm
   whether `[Build] Skipping measured special-work` actually fires for it
   too (only confirmed for `NCustom_g832` in the captured transcript this
   turn).
2. Add a targeted diagnostic log in `ResolveDivergingFixedStockRail` (or
   around the `IsDualNarrowBranchPreset` validation block) printing the
   resolved `divergingFixed` rail id and its section list for all 4 nodes,
   to find why 2 of 4 fail this check post one-blade-filter.
3. Continue with the evidence-led ownership boundary fix for `NCustom_ltci`
   / `SCustom_ttpp` (route filtering alone is proven insufficient, per
   Codex's last turn).
4. Separately continue Nove's frog-collapse investigation.
5. Get user-driven close-up screenshots (closed + thrown) of `N178` and
   `NCustom_vdlt` at minimum, since those two look clean on paper but were
   never actually screenshot-verified this session.

## Open questions / blockers

- What boundary rule should prevent neighboring measured switches from
  clipping each other's source segments when route membership overlaps?
- Why exactly `NCustom_7n90` and `NCustom_g832` are invalid post one-blade
  filter while `N178`/`NCustom_vdlt`/`Nove` are not - needs a live diagnostic
  log, not more static reading.
- Whether `NCustom_7n90`'s measured build is actually being skipped
  (confirmed only for `NCustom_g832` in the transcript captured this turn).
- How live verification proceeds now that the automated pipeline is
  retired - manual user testing only, until the user says otherwise.
