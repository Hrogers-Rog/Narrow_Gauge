# G832 missing right blade and uncut narrow through rail - 2026-07-09

## Report and live evidence

The user reports that `NCustom_g832` now has the correct left blade and frog
hardware, but is missing the right blade. A separate full-length narrow-gauge
left through rail renders over the correct measured hardware, cutting through
the left blade and frog.

The current `Player.log` (2026-07-09 19:28) confirms the deployed build is the
post-correction build and that G832 is valid, but has only one generated blade:

- truth table `DualGauge_NarrowBranch_Left` is selected with two blade entries;
- `NarrowPointBlade` is rewritten from
  `narrow-reversed:right / narrow-normal:right` to
  `narrow-normal:left / narrow-reversed:left` using the accepted
  standard x narrow crossing frog;
- `NarrowStraightPointBlade`, which already describes that left-side pairing,
  is then skipped because its authored movable side is not the detected shared
  side;
- the final plan summary is `valid=True ... blades=1`.

That sequence explains the missing right blade exactly: the rewrite turns the
right blade into a duplicate of the left blade, then the shared-side filter
discards the table's original left-blade entry. The truth table's two
entries are complementary. Mirroring narrow normal/reversed hand swaps the two
entries, but does not change the physical two-blade set, so the paired table
must be kept intact for this anatomy.

The same log identifies the overlaid through rail as an ownership-clipping
problem rather than another measured fixed piece. On G832's two authored
dual-gauge through segments (`SCustom_snvo` and `SCustom_6wx3`), ownership
claims are emitted only for `standard-through:left/right`; there are no
`narrow-normal:left/right` claims. The generated narrow counterparts do exist
and are logged as `fuse-ng:s:SCustom_snvo` and
`fuse-ng:s:SCustom_6wx3`.

`SpecialWorkHardwareRenderer.OwnershipCuts` currently limits eligible work
rails to routes whose `SourceSegmentIds` contain the authored source segment
id. Narrow routes contain the deterministic ghost counterpart id instead
(`fuse-ng:s:<source id>`), so the filter excludes the narrow-through work rail.
Consequently the ordinary dual-gauge third rail is never clipped even though
it physically overlaps the measured narrow-through blade/frog territory.

## Fix applied

1. For a truth-table-matched `dual.narrow-branch-joins-main` layout with an
   accepted cross-family crossing frog, keep the table's complete paired blade
   set and do not rewrite both entries from the one crossing rail. The existing
   shared-side one-blade filter remains active for the simpler narrow-branch
   layouts with no cross-family crossing (`N178`/`Nove`). The measured fallback
   path (`NCustom_7n90`) is unchanged.
2. When building authored-segment ownership cuts, treat a route containing
   `fuse-ng:s:<source id>` as belonging to the same physical dual-gauge source
   segment. This admits the narrow work interval needed to clip the third rail
   while preserving the source-route boundary filter that prevents unrelated
   neighboring routes from claiming the segment.

This is anatomy/topology based; there is no G832 id special case.

## Verification still required

- Built and deployed without launching or driving Railroader (the automated
  live pipeline remains retired at the user's request):
  `dotnet build .\NarrowGaugeMod.csproj
  -p:RailroaderDir="C:\Steam\steamapps\common\Railroader"
  -p:EnableModDeploy=true` completed with 0 warnings and 0 errors. The built
  and deployed DLLs both have timestamp 2026-07-09 19:34:25 and size 734,720
  bytes.
- After a full game restart, G832 should log `blades=2` and ownership claims for
  `narrow-normal` on `SCustom_snvo`/`SCustom_6wx3` where its rail overlaps the
  visible third rail.
- Visually confirm both point blades render and the full-length narrow through
  rail no longer overlays the left blade/frogs.
- Spot-check `NCustom_vdlt`, the mirror anatomy with a cross-family crossing;
  it should also move from one to two blades. `N178` and `Nove` should stay at
  one blade, and `NCustom_7n90` should remain on its existing fallback path.
