# Both-Diverge Group Investigation - 2026-07-06

Investigator: Codex

Scope: investigation-only for the measured `dual.both-diverge` group:
`NCustom_p997`, `NCustom_ltci`, `NCustom_u6n0`,
`NDeHartPassing_wqbb`, `NCustom_fc97`, `NCustom_l4a4`, `Npv2`.

No source or truth-table files were edited.

## Live Evidence

- Built and deployed with
  `dotnet build .\NarrowGaugeMod.csproj -p:RailroaderDir="C:\Steam\steamapps\common\Railroader" -p:EnableModDeploy=true`.
  Result: succeeded, 0 warnings, 0 errors.
- Launched a fresh direct `Railroader.exe /editor` process with
  `NARROWGAUGE_TEST_BRIDGE=1`, loaded save `2026-06-25`, and forced a
  fresh `exportPlans` request through `NarrowGaugeTestBridge`.
- Fresh measured plans were written under
  `%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\NarrowGauge\SpecialWorkPlans`
  at `2026-07-06 16:30:58` local time.
- Close-up screenshots were captured for the higher-priority switches:
  - `%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE-test-shots\both-diverge-NCustom_p997-20260706.png`
  - `%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE-test-shots\both-diverge-NCustom_ltci-20260706.png`
  - `%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE-test-shots\both-diverge-NCustom_u6n0-20260706.png`
  - `%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE-test-shots\both-diverge-NDeHartPassing_wqbb-20260706.png`
- Cleanup was verified after the run:
  - no `Railroader` process remained,
  - `Mods.fuseGEo\FUSE.TestBridge\Info.json` had `"Enabled": false`,
  - `steam_appid.txt` was absent,
  - no `test_request_*.json` / `test_result_*.json` remained in the runtime bridge dir,
  - no `ng_goto_request.json`, `ng_goto_result.json`, or
    `ng_test_bridge_enabled` remained in `Mods\FUSE.NarrowGauge`.

## Confirmed Findings

There are at least two distinct bug classes in this group.

1. Duplicate/overlapping hardware in both-diverge guards.

   `NCustom_p997`, `NCustom_ltci`, and `NDeHartPassing_wqbb` all export
   exact duplicate guard endpoint groups. The duplicates are not continuity
   noise: they have identical start/end positions in the fresh
   `PieceEndpoints` section.

   The generation path is in `src/SectionedSpecialWorkBuilder.cs`.
   `BuildGuardRails` adds ordinary frog guards and, for crossing frogs, a
   local crossing guard at `SectionedSpecialWorkBuilder.cs:2383-2395`.
   Then it unconditionally calls `AddDualBothDivergeSupplementalGuards` at
   `SectionedSpecialWorkBuilder.cs:2399`. That helper only checks the preset
   and selected rail/frog families (`SectionedSpecialWorkBuilder.cs:2403-2453`);
   `AddSupplementalGuardPair` then creates up to two more guards from those
   frogs (`SectionedSpecialWorkBuilder.cs:2455-2495`) without checking
   whether an ordinary guard already occupies the same line.

   This is a confirmed root cause for duplicate guard hardware in these
   switches. It does not by itself prove every shifted/inside-out visual
   complaint in the group, but it is a real overlapping-hardware defect.

2. Ownership-cut overclaiming for `NCustom_ltci`.

   `NCustom_ltci` claims cuts on both neighboring segments:
   `SCustom_ttpp`, otherwise also claimed by `NCustom_fl15`, and
   `SCustom_snvo`, otherwise also claimed by `NCustom_g832`.

   The mechanism is in `src/SpecialWorkHardwareRenderer.cs:221-272`.
   `OwnershipCuts` admits an analysis if any route contains the source
   segment (`SpecialWorkHardwareRenderer.cs:225-230`) and computes
   `sourceRouteIds` for that segment (`SpecialWorkHardwareRenderer.cs:236-241`).
   But for non-`DualSplit` presets it scans every
   `analysis.MeshPlan!.WorkIntervals` entry (`SpecialWorkHardwareRenderer.cs:242-250`).
   The `sourceRouteIds` filter is only applied for gauge separation
   (`SpecialWorkHardwareRenderer.cs:244-247`).

   The result is visible in fresh `Player.log`: after `NCustom_ltci` is
   admitted for `ttpp` or `snvo`, it emits ownership cuts from its own
   both-diverge work intervals into neighboring segment mesh cuts. Those
   cuts are merged into visible stock rail clipping by
   `CreateRailMeshesWithFrogCuts` in `src/NarrowGaugeTrackBuilder.cs:2350-2392`.

   A simple "filter all presets by `sourceRouteIds`" is likely necessary,
   but may not be sufficient by itself. `TryBuildRoute` stores both incoming
   and outgoing segment ids on every logical route
   (`src/SpecialWorkRuntimeDiscovery.cs:541-547`), so common-entry switch
   segments can still appear in multiple route ids. A robust fix likely also
   needs node-end/route-side boundary scoping or a nearest-owning-analysis
   tie-break so one measured switch cannot extend its ownership cut into a
   neighboring measured switch's replacement territory.

## Rendering Path Notes

The earlier `NCustom_p997` thread suspected `CreateCompoundVeeFrogAssembly`.
Fresh code reading confirms that is not the p997 path:

- `CreateCompoundVeeFrogAssembly` is gated behind `IsDualStandardBranch` at
  `src/SpecialWorkHardwareRenderer.cs:513-531`.
- Both-diverge frogs instead render through ordinary vee frogs at
  `SpecialWorkHardwareRenderer.cs:533-544` and crossing frogs at
  `SpecialWorkHardwareRenderer.cs:547-558`.
- Crossing frogs call `CreateCrossingFrogAssembly`
  (`SpecialWorkHardwareRenderer.cs:1707-1752`). If
  `TryResolveNarrowBranchCrossingRails` succeeds, that path creates a
  `ContinuousStockHandoff` from `BuildNarrowBranchStockHandoff`
  (`SpecialWorkHardwareRenderer.cs:1734-1751`,
  `SpecialWorkHardwareRenderer.cs:2785-2829`) instead of generic crossing
  points.

For p997/ltci the crossing handoff, the local crossing guard, and the
supplemental both-diverge guards all cluster in the same throat. I confirmed
the duplicate guards exactly, but did not prove whether the
`ContinuousStockHandoff` is also wrong or only visually adjacent.

## Per-Switch Results

### `NCustom_p997`

Status: confirmed duplicate guard root cause; additional ownership anomaly
noted.

Fresh plan:

- Header: `WheelPaths=4 rails=8 shared=8 intersections=13 cuts=22 frogs=3 wings=8 guards=9 blades=2`
- Truth: `passed: DualGauge_BothDiverge_LeftHand`
- Frogs:
  - `v2-frog:0` `standard-normal:left/standard-reversed:right`, angle
    `11.122`, hand `Right`, cutHalf `0.819`
  - `v2-frog:1` `standard-normal:left/narrow-reversed:left`, angle
    `7.157`, hand `Right`, cutHalf `1.443`
  - `v2-frog:2` `standard-reversed:right/narrow-normal:left`, angle
    `9.249`, hand `Left`, cutHalf `0.978`
- Blades:
  - `v2-blade:StandardNormalLeft` tip `21.021`, root `25.621`
  - `v2-blade:StandardReversedRight` tip `20.848`, root `25.887`
- Duplicate endpoint group: `v2-guard:0 == v2-guard:8`
- `GeometryContinuity`: no reported issues.

Exact duplicate evidence from `PieceEndpoints`:

- `v2-guard:0` start `(1806.741,588.453,1304.676)`, end
  `(1807.322,588.454,1306.381)`
- `v2-guard:8` start `(1806.741,588.453,1304.676)`, end
  `(1807.322,588.454,1306.381)`

Ownership:

- `SCustom_dkzn` is only claimed by `NCustom_p997` in the fresh log
  (`cut=0.120-1.486`, `0.120-1.483`, `0.120-1.478`, `0.120-1.478`).
  This does not look like the same double-owner bug as `ltci`.
- `NCustom_p997` also claims the tail end of `SCustom_6wx3`, which is also
  claimed by `NCustom_g832`. Examples: p997 emits `cut=18.061-20.904`,
  `18.000-20.840`, and `17.888-20.728` on `SCustom_6wx3`. This may be the
  same ownership-boundary weakness as `ltci`, but I did not chase it to a
  separate visual symptom in this turn.

Visual check:

- The p997 close-up shows extra/overlapping silver guard fragments in the
  switch throat, matching the duplicate endpoint data.

Conclusion:

- Confirmed: duplicate supplemental guard generation contributes real
  overlapping hardware at p997.
- Not proven: whether the crossing `ContinuousStockHandoff` is also
  geometrically wrong. It is on the active render path and should be
  inspected after guard de-duplication.

### `NCustom_ltci`

Status: confirmed duplicate guard root cause and confirmed ownership
overclaim root cause.

Fresh plan:

- Header: `WheelPaths=4 rails=8 shared=8 intersections=13 cuts=22 frogs=3 wings=8 guards=9 blades=2`
- Truth: `passed: DualGauge_BothDiverge_LeftHand`
- Frogs:
  - `v2-frog:0` `standard-normal:left/standard-reversed:right`, angle
    `9.565`, hand `Right`, cutHalf `0.946`
  - `v2-frog:1` `standard-normal:left/narrow-reversed:left`, angle
    `5.850`, hand `Right`, cutHalf `1.715`
  - `v2-frog:2` `standard-reversed:right/narrow-normal:left`, angle
    `7.705`, hand `Left`, cutHalf `1.167`
- Blades:
  - `v2-blade:StandardNormalLeft` tip `22.621`, root `28.421`
  - `v2-blade:StandardReversedRight` tip `22.417`, root `28.753`
- Duplicate endpoint group: `v2-guard:0 == v2-guard:8`
- `GeometryContinuity`: no reported issues.

Exact duplicate evidence:

- `v2-guard:0` start `(1799.482,588.446,1267.804)`, end
  `(1799.681,588.448,1269.593)`
- `v2-guard:8` start `(1799.482,588.446,1267.804)`, end
  `(1799.681,588.448,1269.593)`

Ownership overclaim evidence, unique claim set from fresh `Player.log`:

- On `SCustom_ttpp`, `NCustom_fl15` claims its own intervals at cuts around
  `0.120-1.45`, while `NCustom_ltci` also claims eight rails:
  `standard-normal:left`, `standard-reversed:left`, `narrow-normal:left`,
  `narrow-reversed:left`, `standard-normal:right`,
  `standard-reversed:right`, `narrow-normal:right`,
  `narrow-reversed:right`. The ltci cuts extend to about `2.028`,
  `2.024`, or `2.017` depending on rail.
- On `SCustom_snvo`, `NCustom_g832` claims six rails around `0.120-1.859`,
  while `NCustom_ltci` also claims `standard-normal:left`,
  `narrow-normal:left`, `standard-normal:right`, and
  `narrow-normal:right` around `0.120-1.471` / `0.120-1.472`.

Those ltci claims are not just logged; `NarrowGaugeTrackBuilder` merges
ownership cuts into segment mesh clipping, so this can remove or shorten
neighboring stock rails.

Visual check:

- The ltci close-up shows the same class of extra throat hardware as p997.

Conclusion:

- Confirmed: ltci has duplicate supplemental guard hardware.
- Confirmed: ltci also demonstrates the ownership-cut boundary bug in
  `OwnershipCuts`. This is likely distinct from the duplicate guard issue,
  although both are exposed by both-diverge measured geometry.

### `NCustom_u6n0`

Status: investigated, inconclusive.

Fresh plan:

- Header: `WheelPaths=4 rails=8 shared=8 intersections=10 cuts=23 frogs=3 wings=8 guards=7 blades=3`
- Truth: `no truth table matched; measured geometry fallback used`
- Frogs:
  - `v2-frog:0` `standard-normal:right/standard-reversed:left`, angle
    `6.657`, hand `Left`, cutHalf `1.345`
  - `v2-frog:1` `standard-normal:right/narrow-reversed:left`, angle
    `5.450`, hand `Left`, cutHalf `1.635`
  - `v2-frog:synth:2` `standard-reversed:left/narrow-normal:left`, angle
    `3.982`, hand `Right`, cutHalf `1.500`
- Blades:
  - `v2-blade:standard:Left` tip `69.989`, root `82.473`
  - `v2-blade:standard:Right` tip `69.987`, root `79.974`
  - `v2-blade:narrow:Left` tip `69.988`, root `79.988`
- Duplicate endpoint groups: none found by the fresh plan scan.
- `GeometryContinuity`: no reported issues.

Ownership:

- `SCustom_s3y7` is only claimed by `NCustom_u6n0` in the fresh log, with
  four cuts all `0.120-1.518`. I did not find a neighboring double-owner
  pattern like ltci.

Notable:

- The plan falls back instead of matching `DualGauge_BothDiverge_LeftHand`.
- One frog is synthesized by the both-diverge missing-cross-family path
  (`AddMissingCrossFamilyCrossingFrogs`, called at
  `src/SectionedSpecialWorkBuilder.cs:134-137`).
- Exported guards are `7`, not `9`, and there are no exact duplicate guard
  endpoints.

Conclusion:

- I ruled out exact duplicate guard endpoints and wrong-interval ownership
  claims on `SCustom_s3y7`.
- Still unclear: whether the synthesized frog/guard set is laterally wrong
  in the user's "shifted one rail-head width" sense. This needs either a
  targeted visual comparison after the confirmed duplicate/ownership fixes
  or a deeper geometry audit of synthesized crossing frogs for both-diverge
  fallback switches.

### `NDeHartPassing_wqbb`

Status: confirmed duplicate guard root cause; synthesized frog also present.

Fresh plan:

- Header: `WheelPaths=4 rails=8 shared=8 intersections=12 cuts=22 frogs=3 wings=8 guards=9 blades=2`
- Truth: `passed: DualGauge_BothDiverge_LeftHand`
- Frogs:
  - `v2-frog:0` `standard-normal:left/standard-reversed:right`, angle
    `5.300`, hand `Right`, cutHalf `1.680`
  - `v2-frog:1` `standard-reversed:right/narrow-normal:left`, angle
    `4.448`, hand `Left`, cutHalf `1.995`
  - `v2-frog:synth:2` `standard-normal:left/narrow-reversed:left`, angle
    `3.737`, hand `Right`, cutHalf `1.500`
- Blades:
  - `v2-blade:StandardNormalLeft` tip `38.652`, root `51.853`
  - `v2-blade:StandardReversedRight` tip `38.650`, root `48.866`
- Duplicate endpoint groups:
  - `v2-guard:0 == v2-guard:8`
  - `v2-guard:3 == v2-guard:7`
- `GeometryContinuity`: no reported issues.

Exact duplicate evidence:

- `v2-guard:0` start `(1817.627,586.663,1119.226)`, end
  `(1817.148,586.682,1120.961)`
- `v2-guard:8` start `(1817.627,586.663,1119.226)`, end
  `(1817.148,586.682,1120.961)`
- `v2-guard:3` start `(1819.221,586.601,1113.449)`, end
  `(1818.743,586.620,1115.184)`
- `v2-guard:7` start `(1819.221,586.601,1113.449)`, end
  `(1818.743,586.620,1115.184)`

Ownership:

- `SDeHartPassing_tliv` is only claimed by `NDeHartPassing_wqbb` in the
  fresh log, with four cuts around `0.120-1.49`.

Conclusion:

- Confirmed: duplicate supplemental guard generation contributes real
  overlapping hardware at wqbb.
- Also notable but not proven as a visual root cause: one frog is
  synthesized (`v2-frog:synth:2`).

### `NCustom_fc97`

Status: investigated, inconclusive / no confirmed defect from the quick
sanity pass.

Fresh plan:

- Header: `WheelPaths=4 rails=8 shared=8 intersections=11 cuts=23 frogs=3 wings=8 guards=9 blades=3`
- Truth: `passed: DualGauge_BothDiverge_LeftHand`
- Frogs:
  - `v2-frog:0` `standard-normal:left/standard-reversed:right`, angle
    `7.949`, hand `Right`, cutHalf `1.132`
  - `v2-frog:1` `standard-normal:left/narrow-reversed:right`, angle
    `6.872`, hand `Right`, cutHalf `1.304`
  - `v2-frog:2` `standard-reversed:right/narrow-normal:right`, angle
    `5.533`, hand `Left`, cutHalf `1.801`
- Duplicate endpoint groups: none found.
- `GeometryContinuity`: no reported issues.

Conclusion:

- I found no exact duplicate guard endpoints or continuity errors in the
  fresh export. No root cause confirmed this turn.

### `NCustom_l4a4`

Status: investigated, inconclusive / no confirmed defect from the quick
sanity pass.

Fresh plan:

- Header: `WheelPaths=4 rails=8 shared=8 intersections=13 cuts=23 frogs=3 wings=8 guards=7 blades=3`
- Truth: `no truth table matched; measured geometry fallback used`
- Frogs:
  - `v2-frog:0` `standard-normal:right/standard-reversed:left`, angle
    `7.824`, hand `Left`, cutHalf `1.149`
  - `v2-frog:1` `standard-normal:right/narrow-reversed:right`, angle
    `5.202`, hand `Left`, cutHalf `1.901`
  - `v2-frog:2` `standard-reversed:left/narrow-normal:right`, angle
    `6.692`, hand `Right`, cutHalf `1.338`
- Duplicate endpoint groups: none found.
- `GeometryContinuity`: no reported issues.

Conclusion:

- I found no exact duplicate guard endpoints or continuity errors in the
  fresh export. The truth-table fallback and lower guard count may be worth
  revisiting after the confirmed bugs are fixed, but no root cause was
  proven here.

### `Npv2`

Status: investigated, inconclusive / no confirmed defect from the quick
sanity pass.

Fresh plan:

- Header: `WheelPaths=4 rails=8 shared=8 intersections=11 cuts=23 frogs=3 wings=8 guards=7 blades=3`
- Truth: `no truth table matched; measured geometry fallback used`
- Frogs:
  - `v2-frog:0` `standard-normal:right/standard-reversed:left`, angle
    `6.937`, hand `Left`, cutHalf `1.292`
  - `v2-frog:1` `standard-normal:right/narrow-reversed:left`, angle
    `6.060`, hand `Left`, cutHalf `1.474`
  - `v2-frog:2` `standard-reversed:left/narrow-normal:left`, angle
    `4.744`, hand `Right`, cutHalf `2.063`
- Duplicate endpoint groups: none found.
- `GeometryContinuity`: no reported issues.

Conclusion:

- I found no exact duplicate guard endpoints or continuity errors in the
  fresh export. No root cause confirmed this turn.

## Proposed Fixes To Apply Later

Do not apply these concurrently with another agent touching the same files.

1. De-duplicate supplemental both-diverge guards.

   Before `AddSupplementalGuardPair` appends a `GuardRailPlan`, compare the
   candidate guard curve against existing guards. Reject the candidate if it
   is the same frog/opposite rail or if its start/end endpoints match an
   existing guard within a small tolerance. The exact duplicate groups above
   should become regression checks:
   `NCustom_p997` and `NCustom_ltci` should no longer export
   `v2-guard:0 == v2-guard:8`, and `NDeHartPassing_wqbb` should no longer
   export either duplicate group.

   A slightly more semantic variant is to make
   `AddDualBothDivergeSupplementalGuards` only fill missing guard coverage
   around the two both-diverge vee/cross-family frogs, not blindly add two
   supplemental guards after ordinary guard generation.

2. Scope `OwnershipCuts` by source route for non-`DualSplit` presets, then
   add a measured-switch boundary rule if the route filter is not enough.

   First patch should likely apply the existing `sourceRouteIds` filter to
   all measured presets, not only `DualSplit`. Then live-test
   `NCustom_ltci` against `SCustom_ttpp` and `SCustom_snvo`.

   If ltci still claims neighboring territory, the next layer should use
   node-end or ownership-distance scoping so the owning analysis closest to
   a segment end controls the first replacement interval and a neighboring
   analysis cannot cut beyond its own measured throat. The route data alone
   may be ambiguous because route `SourceSegmentIds` contain both incoming
   and outgoing segments.

3. Re-check crossing-frog handoff after duplicate guards are removed.

   For p997/ltci, the render path creates a
   `CrossingFrog-*-ContinuousStockHandoff` via
   `BuildNarrowBranchStockHandoff`. If duplicate guard removal still leaves
   kinked/disconnected crossing hardware, compare that handoff path against
   `CreateGenericCrossingPoints` and the local crossing guard generated by
   `TryBuildLocalCrossingGuard`. I did not prove this handoff is wrong in
   this investigation-only turn.

4. After fixes, re-run the live plan export and screenshot checks for at
   least `NCustom_p997`, `NCustom_ltci`, `NCustom_u6n0`, and
   `NDeHartPassing_wqbb`.

## Suggested Regression Checks

- Build/deploy with 0 warnings and 0 errors.
- Force fresh `exportPlans`.
- For the both-diverge group, scan `PieceEndpoints` for exact duplicate
  guard endpoint pairs.
- Grep fresh `Player.log` for:
  - `segment=SCustom_ttpp`
  - `segment=SCustom_snvo`
  - `segment=SCustom_6wx3`
  Verify no unrelated neighboring switch claims remain, or document why any
  remaining claim is geometrically correct.
- Capture close-up screenshots after closing UMM:
  - `NCustom_p997`
  - `NCustom_ltci`
  - `NCustom_u6n0`
  - `NDeHartPassing_wqbb`

## 2026-07-07 Codex Live Re-Verification And Follow-Up Fix

Codex re-ran the live-game pipeline against save `2026-06-25` with the
current deployed code, then applied one small source fix and re-ran the
pipeline again.

Process notes:

- Build/deploy succeeded with
  `dotnet build .\NarrowGaugeMod.csproj -p:RailroaderDir="C:\Steam\steamapps\common\Railroader" -p:EnableModDeploy=true`
  before the first live run and again after the source patch; both builds
  reported 0 warnings and 0 errors.
- The active `Mods\FUSE.TestBridge` folder was missing `FUSE.Core.dll`, so
  FUSE.TestBridge initially failed to load. For the live run only, Codex
  copied `FUSE.Core.dll` from `Mods.fuseGEo\FUSE.TestBridge` into the active
  bridge folder, then removed it during cleanup to restore the pre-run
  install state.
- Fresh post-patch measured plans were exported at `2026-07-07 16:46:04`
  under
  `%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\NarrowGauge\SpecialWorkPlans`.
- Cleanup was verified directly: `tasklist` reported no `Railroader.exe`,
  `Mods\FUSE.TestBridge\Info.json` read back `"Enabled": false`,
  `steam_appid.txt` was absent, no `test_request_*.json` /
  `test_result_*.json` remained, and no Narrow Gauge bridge request/result
  files remained.

### Guard de-duplication result

The earlier semantic guard de-duplication was insufficient. In the first
fresh export of this turn, `NCustom_p997`, `NCustom_ltci`, and
`NDeHartPassing_wqbb` still exported exact duplicate guard endpoints, now as
`v2-guard:0 == v2-guard:7`. The remaining duplicate was not the same
`(FrogId, OppositeRunningRail)` pair: the ordinary guard used
`opposite=standard-normal:right`, while the supplemental guard used
`opposite=narrow-normal:right`. Those two route-derived guard rails resolve
to the same physical line, so a semantic rail-id check alone cannot catch
the overlap.

Codex patched `src/SectionedSpecialWorkBuilder.cs` so
`AddSupplementalGuardPair` flares the candidate guard first and skips it if
its final start/end endpoints match any existing guard curve within
`0.01m`, checking both orientations. This is intentionally limited to
supplemental guard insertion and leaves ordinary frog/local crossing guard
generation unchanged.

Post-patch fresh export scan:

- `NCustom_p997`: `guards=7`, duplicate guard endpoint groups: `0`.
- `NCustom_ltci`: `guards=7`, duplicate guard endpoint groups: `0`.
- `NCustom_u6n0`: `guards=7`, duplicate guard endpoint groups: `0`.
- `NDeHartPassing_wqbb`: `guards=7`, duplicate guard endpoint groups: `0`.
- `NCustom_fc97`: `guards=9`, duplicate guard endpoint groups: `0`.
- `NCustom_l4a4`: `guards=7`, duplicate guard endpoint groups: `0`.
- `Npv2`: `guards=7`, duplicate guard endpoint groups: `0`.

Screenshots captured after closing UMM:

- `%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE-test-shots\codex-bothdiverge-NCustom_p997-20260707-postfix.png`
- `%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE-test-shots\codex-bothdiverge-NCustom_ltci-20260707-postfix.png`
- `%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE-test-shots\codex-bothdiverge-NDeHartPassing_wqbb-20260707-postfix-offset180.png`

Visual check from those frames: the old exact stacked guard line is no
longer visible at `p997` or `ltci`; the useful `wqbb` frame is the
`offset180` shot and shows the switchwork without the prior duplicate guard
endpoint export. This does not prove every crossing-handoff or synthesized
frog concern in the group is fixed; it only closes the exact duplicate guard
hardware defect.

### Ownership-cut result

The source-route filter in `OwnershipCuts` is active in code, but live data
shows it is not enough to solve the measured-switch boundary problem:

- `SCustom_ttpp` is still double-claimed by `special-work:NCustom_fl15` and
  `special-work:NCustom_ltci`. Fresh post-patch examples:
  `NCustom_fl15` cuts `0.120-1.457` / `0.120-1.466`, while `NCustom_ltci`
  still cuts `0.120-2.028` and `0.120-2.017` on the same segment.
- `SCustom_snvo` is currently claimed only by `special-work:NCustom_ltci` in
  this run, but that is not proof of correctness: `special-work:NCustom_g832`
  is invalid in this load (`Fixed diverging narrow stock/running rail has no
  renderable role sections`), so it never competes for its side of `snvo`.
- `SCustom_6wx3` is currently claimed only by `special-work:NCustom_p997` in
  this run for the same reason (`NCustom_g832` invalid), with p997 cuts at
  `18.061-20.904` / `17.888-20.728`.

Conclusion: the earlier route filter removed one obvious over-broad scan,
but route membership is still ambiguous at common-entry/neighboring switch
segments. The next fix should use node-end/side ownership or nearest-owning
analysis boundary logic rather than another all-route static inference.

### Gauge-separation control applicability

Fresh `Player.log` only created runtime-only gauge-separation controls for
`Nove` and `NCustom_7n90`:

- `fuse-ng:n:Nove:control` / `fuse-ng:s:Nove:control`
- `fuse-ng:n:NCustom_7n90:control` / `fuse-ng:s:NCustom_7n90:control`

There were no `Created runtime-only gauge-separation control` lines for any
both-diverge node in this review's scope (`NCustom_p997`, `NCustom_ltci`,
`NCustom_u6n0`, `NDeHartPassing_wqbb`, `NCustom_fc97`, `NCustom_l4a4`,
`Npv2`). Therefore Claude's hidden-control descriptor fix does not apply to
the both-diverge group in this save, and there was no both-diverge
control-stub gap to confirm as resolved.

### New/remaining observations

- This load reports `Special-work analysis: objects=14, invalid=2`.
  The invalid plans are `NCustom_7n90` and `NCustom_g832`, both
  `dual.narrow-branch-joins-main`, each failing with `Fixed diverging narrow
  stock/running rail has no renderable role sections`.
- The exact duplicate guard endpoint defect is fixed and live-verified after
  the endpoint-overlap patch.
- The `ltci` ownership-cut boundary bug remains live and needs a separate,
  evidence-led fix.
