# `ResolveDivergingFixedStockRail` hand bug - 2026-07-07 (evening)

## Origin

User pointed at `C:\Steam\...\Mods.Personal\narrow_gauge test\game-graph.json`
(a hand-built save with 8 confirmed-working narrow gauge switches) and at
`C:\Steam\...\Mods\EFA Track Pack\game-graph.json` (`EF&A Track Pack`, the
large third-party layout mod that the live save `2026-06-25` is built on top
of), with the idea: if a switch works in one of these known-good graphs,
its geometry is a template we can compare the broken live-save nodes
against - without needing a game launch, since the pipeline is currently
retired.

Two concrete leads followed from this:

1. `NCustom_438l`/`NCustom_fokf`/`NCustom_qf3e` in the small test save form
   a working "narrow diverges, dual-gauge mainline runs through" switch -
   the same pattern Nove is trying to be. Screenshot of the equivalent
   working switch at `NCustom_5f81` (same test save) confirmed a clean
   single blade + single frog, switch stand at the throat, no duplicate
   rails.
2. A screenshot of the live save's `NCustom_7n90` showed the opposite - a
   tangle of overlapping thin rail lines at the throat, described by the
   user as "the opposite turnout direction of Nove" and "should be this
   turnout of the ef&a but obviously it's not working."

## Key discovery: EF&A Track Pack node IDs overlap with the live save

`NCustom_vdlt`, `N178`, and `NCustom_g832` - three of the five nodes in
STATUS.md's "narrow-branch group" - are not just similarly-named, they are
**the same authored nodes** from `EFA Track Pack\game-graph.json` (the live
save is built on that mod). That means their exact authored geometry,
including a field our code never reads, is available directly as JSON with
no game launch required:

| node | gauge tags on the two "through" segments | `flipSwitchStand` | live-save validity (per STATUS.md) |
|---|---|---|---|
| `NCustom_vdlt` | `DualGauge_L`, `DualGauge_L` | `false` | `valid=True` |
| `N178` | `DualGauge_L`, `DualGauge_L` | `false` | `valid=True` (never screenshot-verified) |
| `NCustom_g832` | `DualGauge_R`, `DualGauge_R` | **`true`** | `valid=False` (regression) |

`g832` is the only one of the three built with `DualGauge_R` (mirrored
shared-rail side) and the only one the original EF&A author had to
hand-correct with `flipSwitchStand: true`. This lines up with the user's
"opposite turnout direction" framing of `NCustom_7n90`'s tangled screenshot.
(`flipSwitchStand` itself is cosmetic - it only rotates the physical switch
stand prop, see `NarrowGaugeSwitchGeometry.cs:127,162` - it is not itself
wired into blade/shared-side selection. It's corroborating evidence that
`g832` is the mirrored-hand case, not the fix.)

## Root cause, traced through the code

`BuildNarrowRailsFromStandardCenterline`
(`SectionedSpecialWorkBuilder.cs:378-411`) builds the two physical narrow
rails for a `dual.narrow-branch-joins-main` switch. Whichever `RailSide`
(`Left`/`Right`) matches the switch's `sharedSide` gets the real shared
standard-centerline geometry (renderable); the other side gets a synthetic
"third rail" curve with `Role=Unknown` and no renderable sections:

```csharp
LineCurve sharedCurve = sharedSide == RailSide.Right
    ? standardCenterline.Parallel(stdHalf, Hand.Right)
    : standardCenterline.Parallel(-stdHalf, Hand.Left);
...
yield return new RailCenterline(narrowPath.LeftRailId, ..., RailSide.Left,
    sharedSide == RailSide.Right ? thirdCurve : sharedCurve, ...);
yield return new RailCenterline(narrowPath.RightRailId, ..., RailSide.Right,
    sharedSide == RailSide.Right ? sharedCurve : thirdCurve, ...);
```

`sharedSide` comes from `DetectSharedSide(definition)`
(`SectionedSpecialWorkBuilder.cs:345-362`), which is genuinely hand-aware:
it returns `Right` for `DualGauge_R` segments, `Left` for `DualGauge_L`
(via `DualGaugeSharedRailRegistry.SharesRightRail`).

But `ResolveDivergingFixedStockRail`
(`SectionedSpecialWorkBuilder.cs:3357-3391`, added this session as a
diagnostic-only change) picked the "fixed diverging stock rail" candidate
with:

```csharp
.OrderBy(rail => rail.Side == RailSide.Left ? 0 : 1)
```

This hardcodes a preference for `RailSide.Left` with no reference to the
switch's actual `sharedSide`. For `DualGauge_L` switches (`vdlt`, `N178`)
`Left` happens to be the renderable side, so this coincidentally worked.
For `DualGauge_R` switches (`g832`, and per the user's screenshot,
`NCustom_7n90`) the renderable side is `Right`, so the hardcoded `Left`
preference picks the synthetic, non-renderable "third rail" candidate
instead - producing exactly the observed validation failure: `"Fixed
diverging narrow stock/running rail has no renderable role sections."`

This also explains why the same-session one-blade shared-side filter
regressed `NCustom_7n90`/`NCustom_g832` specifically (both `DualGauge_R`,
per this pattern) while leaving `N178`/`NCustom_vdlt` (`DualGauge_L`) fine,
and why it was reverted rather than understood at the time (see
`STATUS.md`'s prior revision and
`reviews/ncustom-7n90-194b-investigation-2026-07-07.md`).

## Fix applied

`ResolveDivergingFixedStockRail` now computes `preferredSide =
DetectSharedSide(definition) ?? RailSide.Left` and orders candidates by
`rail.Side == preferredSide` instead of the hardcoded `RailSide.Left`
check. Falls back to `Left` (prior behavior) when `sharedSide` can't be
determined. The existing `[DivergingFixedRail]` diagnostic log line now
also prints `preferredSide` so the next live run can directly confirm this
picks the renderable candidate for `NCustom_7n90`/`NCustom_g832`.

Built and deployed (`dotnet build NarrowGaugeMod.csproj
-p:RailroaderDir="C:\Steam\steamapps\common\Railroader"
-p:EnableModDeploy=true`): 0 warnings, 0 errors.

## Not yet done / caveats

- **Not live-verified.** Per this session's standing rule, static reasoning
  on this file has been wrong before more than once. This is a
  well-evidenced hypothesis (cross-referenced against real authored EF&A
  geometry, not just code reading), but needs a fresh game load to confirm:
  - `[DivergingFixedRail]` log shows `preferredSide=Right` for
    `NCustom_g832`/`NCustom_7n90` and resolves to the rail with a
    renderable role.
  - `special-work:NCustom_g832` / `special-work:NCustom_7n90` report
    `valid=True`.
  - Close-up screenshot of `NCustom_7n90` no longer shows the overlapping
    rail tangle from the user's screenshot this turn.
- This fix only touches which candidate `ResolveDivergingFixedStockRail`
  picks for validation/frog-continuity purposes on the
  `dual.narrow-branch-joins-main` preset. It does not touch
  `BuildBladeSpecs`' fallback-path one-blade filter (still reverted to
  "yield both candidates unconditionally" - see the comment at
  `SectionedSpecialWorkBuilder.cs:803-815`), so the cosmetic extra-blade
  tangle the user screenshotted at `NCustom_7n90` will likely still need
  that filter re-applied *with the same hand-awareness fix* once this is
  confirmed. Worth revisiting once `valid=True` is confirmed for both
  nodes.
- Did not check whether any other caller in this file has the same
  hardcoded-`Left` assumption; this was a targeted fix for the specific
  failure this evidence pointed at, not an exhaustive sweep.
