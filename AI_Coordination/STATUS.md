# Coordination Status

Last updated by: Codex - 2026-07-06

## Current phase: investigation findings, no visual fix claimed

This turn investigated the two scoped leads and did **not** make a code fix.
That is intentional: both leads produced concrete root-cause candidates, but
neither is safe to patch and call fixed without a targeted close-up screenshot.

Standing rule still applies: `valid=True`, a passing build, and a wide/medium
screenshot are not proof of a visual fix. Only a close-up screenshot showing
the previously broken geometry now correct, or the user's confirmation, counts.

## What Codex found this turn

### `dkzn` / `NCustom_p997`

Live run rebuilt the special-work analysis (`objects=14, invalid=0`) and
confirmed `SCustom_dkzn` is claimed only by `NCustom_p997`. The automated
camera/goto/screenshot path worked, but the screenshot
`C:\Users\roger\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE-test-shots\codex_p997.png`
is still a medium/wide view, not a close-up suitable for proving a fix.

Important correction to the prior framing: `NCustom_p997` is
`dual.both-diverge`, so it does **not** call
`CreateCompoundVeeFrogAssembly`. In `AddAdditionalHardware`, compound vee
assemblies are only used when `IsDualStandardBranch(analysis)` is true.
`p997` renders three independent frog assemblies instead:

- `v2-frog:0` `standard-normal:left` / `standard-reversed:right`, vee, near
  `(1805.83, 1305.94)`.
- `v2-frog:1` `standard-normal:left` / `narrow-reversed:left`, crossing, near
  `(1804.08, 1300.56)`.
- `v2-frog:2` `standard-reversed:right` / `narrow-normal:left`, vee, near
  `(1805.41, 1303.03)`.

The likely explanation for the user's "multiple fragments" close-up is that
several hardware systems are overlapping around the crossing, not that one
clean frog gap is wrong:

- `CreateCrossingFrogAssembly` treats any standard+narrow crossing frog as a
  narrow-branch crossing and renders one `ContinuousStockHandoff` kinked
  curve via `BuildNarrowBranchStockHandoff`.
- `BuildGuardRails` adds the normal guards for the crossing frog and also
  `TryBuildLocalCrossingGuard`; in the stale-but-log-matching p997 export,
  this is `v2-guard:4`, a 2.888 m kinked guard on `narrow-reversed:left`
  running through the same local area.
- `AddDualBothDivergeSupplementalGuards` also adds `v2-guard:8`; in the
  export it has the same endpoints as `v2-guard:0`, so p997 has at least one
  duplicated guard geometry.

No p997 code fix was made. The next patch target should be the crossing
handoff/local-crossing-guard/supplemental-guard interaction, not
`CreateCompoundVeeFrogAssembly`.

### `S4u5` / `N178` vs `Nove`

`N178`/`S4u5` and `Nove` are both `dual.narrow-branch-joins-main`, but they
select opposite truth tables:

- `N178`: `DualGauge_NarrowBranch_Left`, yielding the user-reported wrong
  pairing: left-through/right-diverge.
- `Nove`: `DualGauge_NarrowBranch_Right`, yielding the mirror pairing:
  left-diverge/right-through.

There is a real selector bug: `SpecialWorkTruthTableCatalog.TryGet(...,
intersections, ...)` currently matches a truth-table selector against **any**
intersection between the two rails, including zero-angle `SharedOverlap`.
`BuildBladeSpecs` uses this early intersection-based path before frogs are
accepted/collapsed. In N178's plan data, the `DualGauge_NarrowBranch_Left`
selector matches `standard-through:left x narrow-reversed:right` only as a
`SharedOverlap` (`angle=0.000`), while the accepted vee frog is a different
measured geometry that later gets rehomed. That explains how S4u5 can pick
the mirror blade table.

However, simply filtering selector matches to accepted frog/crossing
intersections is not a complete confident fix: for N178 it appears likely to
fall through rather than positively measure the correct hand. Also, `Nove`
already selects the table that matches the user's expected S4u5 hand, yet the
user has twice confirmed Nove still has a blade running into the switch.

Nove has a second, separate-looking problem in the blade geometry itself. Its
stale-but-log-matching export shows `NarrowPointBlade:closure` only about
0.386 m long. Reading `TryFindBladeDistances` found why this can happen:
the function starts from the switch point as the blade tip, but when the
blade extends toward lower curve distance it returns `tip=endpoint` and
`root=switchDistance` to preserve a sorted interval. The renderer treats
`BladeCurve.Head` as the tip and `Tail` as the pivot/root, so negative-
direction blades can have their semantic tip/root swapped. The older
`SpecialWorkGeometryBuilder` code handled the analogous case by reversing
the blade/closure curves; the sectioned narrow-branch builder does not.

No S4u5/Nove code fix was made. The next patch should separate the two
issues: make truth-table hand selection measure real frog/crossing geometry,
then fix negative-direction blade tip/root/closure semantics and verify Nove
with a close-up screenshot.

## Confirmed reference from earlier this session

- Item 1 (split-standard-narrow zero blades, Codex) - reviewed, agreed.
- Item 2 (both-diverge SharedDuplicate suppression, Claude) - reviewed,
  agreed, but not close-up visually proven.
- Narrow-branch rendering gaps (frog rehoming, stock-rail selection, blade
  endpoint reservation, Codex two turns) - reviewed, agreed.
- Plain-pipeline `aThirdRails.right` hardcode (Claude) - reviewed, agreed,
  but confirmed this does not touch `Nove`.
- `NarrowGaugeTestBridge` camera-goto tool + diagnostics are working.
- `SCustom_ttpp` double-claim (`fl15` + `ltci`) found, not fixed.

## Next turn

Claude or Codex:

1. For `p997`/`dkzn`, do not chase compound-vee code first. Investigate and
   patch the interaction among `CreateCrossingFrogAssembly`,
   `BuildNarrowBranchStockHandoff`, `TryBuildLocalCrossingGuard`, and
   `AddDualBothDivergeSupplementalGuards` for `dual.both-diverge`. A likely
   first safety patch is to prevent the local crossing guard or duplicated
   supplemental guard from being generated where it overlaps existing frog
   hardware, but verify geometrically before editing.
2. For `S4u5`/`Nove`, split the work:
   - Fix truth-table hand selection so `SharedOverlap` cannot select a hand
     as if it were measured frog geometry.
   - Then handle negative-direction blade tip/root/closure semantics so a
     narrow-branch blade that extends toward lower curve distance still has
     `BladeCurve.Head` at the switch tip and `Tail` at the pivot/root.
3. Any fix needs a fresh build/deploy, live load of `2026-06-25`, and a
   close-up screenshot of the exact previously broken area before reporting
   it as fixed.

## Live-session cleanup status

Codex cleaned up directly this turn: Railroader closed cleanly via
`umm close` and `CloseMainWindow`; `FUSE.TestBridge/Info.json` read back as
`Enabled:false`; `steam_appid.txt` is absent; `test_request_*.json`,
`test_result_*.json`, `ng_goto_request.json`, `ng_goto_result.json`, and
`ng_test_bridge_enabled` are removed.
