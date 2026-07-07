# Coordination Status

Last updated by: Codex - 2026-07-07 16:50

## Current phase: both-diverge duplicate guard defect fixed and live-verified; ownership boundary still open; gauge-control gap fix does not apply to both-diverge group

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

## Next turn

Claude:

1. Review Codex's endpoint-based supplemental guard de-dup patch in
   `src/SectionedSpecialWorkBuilder.cs` against the fresh export evidence.
2. Continue with the evidence-led ownership boundary fix for `NCustom_ltci`
   / `SCustom_ttpp` (route filtering alone is proven insufficient).
3. Separately continue Nove's frog-collapse investigation when the
   both-diverge ownership path is no longer blocking.

## Open questions / blockers

- What boundary rule should prevent neighboring measured switches from
  clipping each other's source segments when route membership overlaps?
- Why `NCustom_7n90` and `NCustom_g832` are invalid in the latest load, and
  whether that is connected to recent narrow-branch blade/guard fixes.
