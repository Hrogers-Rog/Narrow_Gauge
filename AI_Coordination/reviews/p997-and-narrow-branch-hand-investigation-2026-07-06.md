# p997 and Narrow-Branch Hand Investigation - 2026-07-06

## Scope

This review covers the two Codex investigations from the 2026-07-06 turn:

1. `NCustom_p997` / `SCustom_dkzn` compound-looking frog/guard mess.
2. `S4u5` / `N178` versus `Nove` blade pairing and hand selection.

No code fix was made. The evidence below is intended to guide the next patch
without claiming either visual defect is fixed.

## Live session

Build/deploy succeeded before launch:

```powershell
dotnet build .\NarrowGaugeMod.csproj -p:RailroaderDir="C:\Steam\steamapps\common\Railroader" -p:EnableModDeploy=true
```

The game was launched directly with `/editor` and
`NARROWGAUGE_TEST_BRIDGE=1` in the process environment. Creating
`C:\Steam\steamapps\common\Railroader\steam_appid.txt` failed with access
denied, so the process was launched with `SteamAppId=1683150` and
`SteamGameId=1683150` environment variables instead.

`loadSave` for `2026-06-25` succeeded. `Player.log` showed a fresh special
work rebuild:

```text
[FUSE.NarrowGauge] Special-work analysis: objects=14, invalid=0, elapsedMs=34149.
```

`ng_goto_request.json` for `NCustom_p997` succeeded:

```json
{"ok": true, "message": "Jumped to 'NCustom_p997' at (302.72, 588.45, 292.87)."}
```

Screenshot request succeeded:

```text
%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE-test-shots\codex_p997.png
```

The screenshot is not close enough to prove or disprove the user's close-up
defect.

The existing plan exports for `NCustom_p997`, `N178`, and `Nove` did **not**
refresh during this live run; all still read `2026-07-06 12:08:40`.
They were used only where the fresh `Player.log` confirmed the same frog and
blade facts.

## `NCustom_p997` / `SCustom_dkzn`

The prior instruction to inspect `CreateCompoundVeeFrogAssembly` was useful
background, but p997 does not call that path. In
`SpecialWorkHardwareRenderer.AddAdditionalHardware`, compound vee assemblies
are created only when `IsDualStandardBranch(analysis)` is true. `p997` is
`dual.both-diverge`, so its frogs are rendered independently.

Fresh log facts:

```text
[BladeSpecs] Using truth table 'DualGauge_BothDiverge_LeftHand' blades (3)
[FrogAccepted] v2-frog:0 railA=standard-normal:left railB=standard-reversed:right kind=VeeFrogCandidate angle=11.12 cutHalf=0.819 pos=(1805.83,1305.94)
[FrogAccepted] v2-frog:1 railA=standard-normal:left railB=narrow-reversed:left kind=CrossingFrogCandidate angle=7.16 cutHalf=1.443 pos=(1804.08,1300.56)
[FrogAccepted] v2-frog:2 railA=standard-reversed:right railB=narrow-normal:left kind=VeeFrogCandidate angle=9.25 cutHalf=0.978 pos=(1805.41,1303.03)
```

The stale-but-matching p997 export shows:

- 3 frogs.
- 8 wings.
- 9 guards.
- `v2-guard:4` is a 2.888 m kinked local crossing guard on
  `narrow-reversed:left`.
- `v2-guard:8` duplicates `v2-guard:0` exactly by endpoint:
  `(1806.741,1304.676)` to `(1807.322,1306.381)`.

Likely p997 defect sources:

- `CreateCrossingFrogAssembly` treats every standard+narrow crossing frog as
  eligible for `BuildNarrowBranchStockHandoff`, even in `dual.both-diverge`.
- `TryBuildLocalCrossingGuard` adds a special kinked guard on the crossing
  rail itself.
- `AddDualBothDivergeSupplementalGuards` can add a duplicate guard already
  produced by ordinary guard generation.

Confidence: high that the p997 screenshot's "multiple fragments" should be
investigated as overlapping crossing handoff/guard hardware, not as a
compound-vee heel-point bug. Confidence is not high enough to remove any one
piece without a close-up before/after verification.

## `S4u5` / `N178` and `Nove`

Fresh log facts for `N178`:

```text
[BladeSpecs] Using truth table 'DualGauge_NarrowBranch_Left' blades (2)
[FrogAccepted] v2-frog:0 railA=narrow-normal:right railB=narrow-reversed:left kind=VeeFrogCandidate angle=5.75 cutHalf=1.551 pos=(1859.16,970.97)
[FrogOwner] v2-frog:0 rehomed railA narrow-normal:right@39.587 to standard-through:left@16.402.
```

Stale export blade pairing:

```text
NarrowPointBlade stock=narrow-normal:right movable=narrow-reversed:right
NarrowStraightPointBlade stock=narrow-reversed:left movable=narrow-normal:left
```

This matches the user's report that `S4u5` is left-through/right-diverge
when it should be left-diverge/right-through.

Fresh log facts for `Nove`:

```text
[BladeSpecs] Using truth table 'DualGauge_NarrowBranch_Right' blades (2)
[FrogAccepted] intersection:4 standard-through:right x narrow-normal:left VeeFrogCandidate
[FrogAccepted] intersection:5 standard-through:right x narrow-reversed:left VeeFrogCandidate
[FrogOwner] v2-frog:1 rehomed railB narrow-reversed:left@9.276 to narrow-normal:left@9.276.
[FrogOwner] Collapsed duplicate frog hardware v2-frog:0/v2-frog:1 on standard-through:right/narrow-normal:left.
```

Stale export blade pairing:

```text
NarrowPointBlade stock=narrow-normal:left movable=narrow-reversed:left
NarrowStraightPointBlade stock=narrow-reversed:right movable=narrow-normal:right
```

This is the mirror of N178 and matches the hand the user expects for S4u5,
but the user still sees Nove's blade running into the switch. Therefore a
truth-table hand fix alone probably does not solve Nove.

Selector finding:

`SpecialWorkTruthTableCatalog.TryGet(..., intersections, ...)` uses
`MatchesSelector` that accepts any rail-pair intersection. It does not
filter out `SharedOverlap` or require `VeeFrogCandidate` /
`CrossingFrogCandidate` with a meaningful angle. `BuildBladeSpecs` uses this
intersection-based path before frog acceptance and rehoming.

For N178, the `DualGauge_NarrowBranch_Left` selector matches
`standard-through:left x narrow-reversed:right` only as a zero-angle
`SharedOverlap`; the accepted vee frog is different geometry. This makes
the N178 table choice suspect.

Nove blade-geometry finding:

Nove's stale export shows:

```text
v2-blade:NarrowPointBlade:blade start=(1748.479,1369.918) end=(1749.700,1365.482)
v2-blade:NarrowPointBlade:closure start=(1749.700,1365.482) end=(1749.806,1365.096)
```

That closure is only about 0.386 m long. Reading `TryFindBladeDistances`
shows a likely cause: for negative-direction blades the function preserves a
sorted numeric interval by returning `tip=endpoint` and
`root=switchDistance`, even though the semantic blade tip started at the
switch point. The renderer uses `BladeCurve.Head` as the tip and
`BladeCurve.Tail` as the pivot/root. The older `SpecialWorkGeometryBuilder`
handled the analogous case by reversing the blade and closure curves when
the tip was at the higher curve distance; the sectioned narrow-branch path
does not.

## Recommended next patch shape

Patch the two narrow-branch problems separately:

1. Truth table selection should not allow `SharedOverlap` to choose the hand.
   Prefer a measured hand selector based on accepted frog/crossing geometry,
   not a raw overlap fallback.
2. Negative-direction blades need semantic tip/root handling independent of
   numeric interval sorting. Do not only reverse the mesh unless the related
   ownership/closure checks still receive a valid sorted interval.

Patch p997 separately:

1. Re-evaluate whether `CreateCrossingFrogAssembly` should build the narrow
   branch stock handoff for `dual.both-diverge` crossings.
2. Prevent `TryBuildLocalCrossingGuard` from adding a kinked guard where it
   overlaps the crossing/vee frog hardware in p997.
3. Prevent `AddDualBothDivergeSupplementalGuards` from adding a guard that
   duplicates an already-generated guard on the same physical curve.

Any patch needs a close-up screenshot of the exact broken geometry before it
is reported as fixed.
