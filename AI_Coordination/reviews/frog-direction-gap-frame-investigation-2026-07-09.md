# Frog direction, uncovered gap, and inverted-frame investigation - 2026-07-09

## Report and fresh evidence

After a full restart with commit `826054a`, the user reports four related
visual failures:

- `l4a4`, `fc97`, and `N178` still show the previously seen malformed/double
  frog issue;
- `NCustom_7n90` has a large empty rail gap as well as malformed frog work;
- `NCustom_vdlt` renders a V frog and a double/crossing frog at the same
  cluster where only the double frog should be visible;
- some tapered rail pieces are inside-out.

The fresh `Player.log` is from 2026-07-09 19:45 and confirms all five measured
plans are valid. The failures are therefore inside the measured/procedural
replacement geometry, not invalid-plan fallback or stale DLL behavior.

## Root causes

### 1. Frog kind uses route-relative sides without route direction

`RailIntersectionPrototype` and the `dual.both-diverge` override in
`SectionedSpecialWorkBuilder.BuildAcceptedFrogs` currently classify a crossing
from raw `RailSide` equality. `RailSide.Left` is relative to each route's
curve direction. When the two intersecting route curves run in opposite
directions, one route's physical left side is the other route's physical
right side.

The exported `NCustom_vdlt` geometry demonstrates this orientation case. At
its three accepted intersections, the tangent dot products are approximately
`-0.995`, `-0.990`, and `+0.993`. The current raw-side test consequently gives
V, Crossing, V. Comparing physical sides after inverting side equality for
opposed tangents gives Crossing, V, V instead, moving the double-frog anatomy
to the physical crossing shown in the screenshots. This is direction-based,
not a node-id exception.

### 2. Frog ownership rehoming preserves a stale kind and stale dimensions

Fresh `N178` logging accepts a V frog from
`narrow-normal:right / narrow-reversed:left`, then logs:

`[FrogOwner] v2-frog:0 rehomed railA narrow-normal:right@39.587 to standard-through:left@16.402.`

The final physical pair is therefore `standard-through:left /
narrow-reversed:left`, but `RehomeSharedDuplicateFrogRail` copies the old V
kind and old V dimensions into the replacement candidate. The rehomed
physical same-side pair should be classified again as a crossing/double frog,
with crossing head margin, cut length, nose, and handedness recalculated from
the replacement rails.

### 3. A valid measured plan leaves one procedural 7n90 cut uncovered

`SCustom_194b` receives the measured ownership cuts near `0.120-15.679`, but
the gauge-separation rail cutter also removes a second procedural frog span at
`20.832-23.761`. `NCustom_7n90`'s valid measured plan contains only one frog.
`CreateGaugeSeparationControlShell` suppresses *all* procedural fallback
hardware whenever any valid measured plan exists, leaving the second cut with
no replacement rail or frog. That exactly accounts for the screenshot's large
empty gap.

The control shell should render only gauge-separation sites not spatially
covered by a valid measured frog. Invalid plans still receive all fallback
sites and the fallback blade. This also applies to `Nove` without duplicating
any measured frog it already owns.

### 4. Both-diverge crossings are sent through narrow-branch stock handoff

`CreateCrossingFrogAssembly` currently sends every standard/narrow crossing
through `BuildNarrowBranchStockHandoff`, irrespective of preset. That handoff
is appropriate when a narrow branch joins a fixed standard stock rail. In
`dual.both-diverge`, the standard crossing route is itself part of the
diverging point/closure anatomy, so the continuous handoff omits the full
crossing-point pair. `fc97` and `l4a4` both have this anatomy and both report
the same visual defect.

Both-diverge crossing candidates should use the existing generic crossing
point renderer. The narrow-branch continuous stock handoff remains limited to
the narrow-branch preset.

### 5. Render-frame correction is asymmetric and procedural reverse is unsafe

`NeedsMeasuredRailFrameCorrection` covers only the left narrow-branch truth
table, not the right-hand truth (`NCustom_vdlt`) or measured fallback
(`NCustom_7n90`). All three are the same measured preset and can inherit
route-relative rotations that must be rebuilt for rendering.

Separately, the gauge-separation fallback `SliceRail` uses raw
`LineCurve.Reverse()`. Base-game `BuildStockRailMesh` is hand-sensitive, and
raw reversal retains stale per-point rotations; this is a known source of
inside-out rail profiles. It should use the project's hand-aware
`SectionedSpecialWorkBuilder.ReverseRailCurve` helper.

## General corrections applied

1. Physical side equality now uses both rail side and tangent direction in
   prototype and accepted-frog classification. If physical-owner selection
   changes a rail, its tangent is aligned to the replacement owner's curve
   direction before the intersection is stored.
2. A frog candidate is reclassified after physical-owner rehoming. Its angle,
   setbacks, cut length, nose/heel, and handedness are rebuilt from the final
   physical rail pair; reclassification is explicitly logged.
3. A valid gauge-separation plan is supplemented with only procedural frog
   sites farther than 0.35 m from every measured frog. Invalid plans still
   receive all fallback sites and the fallback blade. Supplemental mode never
   adds a blade.
4. An attempted change routed `dual.both-diverge` crossing frogs to generic
   crossing-point geometry. Live testing proved that geometry incompatible
   with the existing cut envelope, so this item was rolled back; both-diverge
   standard/narrow crossings again use the continuous stock handoff.
5. Render-frame normalization now covers every `DualNarrowBranch` measured
   plan, and procedural gauge-separation slices use the hand-aware reverse
   helper.

No switch/node ids are used by these corrections.

## Verification

Built and deployed without launching or driving Railroader:

`dotnet build .\NarrowGaugeMod.csproj
-p:RailroaderDir="C:\Steam\steamapps\common\Railroader"
-p:EnableModDeploy=true`

Result: 0 warnings, 0 errors. The built and deployed DLLs both have timestamp
2026-07-09 20:00:40 and size 737,792 bytes.

Manual verification still requires a full Railroader quit/restart. Expected
fresh-log evidence:

- `N178` logs a `[FrogOwner] ... reclassified` line after its rail rehome;
- `NCustom_vdlt` logs direction-aware `samePhysicalSide` and `tangentDot`
  values, with its opposed-route frog kinds swapped to their physical types;
- `fuse-ng:n:NCustom_7n90` logs one covered and one supplemental procedural
  frog site, with `blade=0`;
- `fc97` and `l4a4` keep valid plans and, after the rollback below, return to
  their pre-turn continuous stock handoff while their localized defect remains
  open.

Visually recheck all five named switches and spot-check `Nove`, `G832`, and a
previously good both-diverge switch. Static compilation cannot prove mesh
winding or final scene overlap.

## Live regression and rollback - 2026-07-09 20:13

The first full restart after deployment showed an immediate systemic
regression on every inspected double frog: long cut spans were left empty,
with generic tapered point rails ending well before the rail resumed. The
user supplied `Screenshot 2026-07-09 201150.png` showing the failure.

The fresh log confirms all both-diverge plans remain valid with their expected
three frogs, so classification/count changes did not invalidate or remove the
plans. The regression is the renderer change in item 4 above: routing every
`dual.both-diverge` crossing through `CreateGenericCrossingPoints` replaced the
previous continuous standard/narrow handoff across the existing
cut/wing-rail envelope. The generic assembly does not cover that envelope in
these plans and causes the visible long gaps.

The user then supplied `Screenshot 2026-07-09 201352.png` and confirmed G832
was not affected by this new regression. G832 is
`dual.narrow-branch-joins-main`, so that negative control further isolates the
failure to the new `dual.both-diverge` early renderer branch rather than the
shared direction, frame, or frog-plan changes.

That renderer change is rolled back. Both-diverge standard/narrow crossings
again use the previously live-confirmed continuous stock handoff. The other
changes in this review are retained: direction-aware classification,
post-owner recalculation, uncovered gauge-separation supplementation, and
frame/reversal corrections. `fc97`/`l4a4` therefore return to their pre-turn
double-frog rendering while their original localized defect remains open for
a renderer-compatible investigation.

Rollback build/deploy completed with 0 warnings and 0 errors. The built and
deployed DLLs both have timestamp 2026-07-09 20:14:22 and size 737,280 bytes.

## Rejected planed-point anatomy attempt - 2026-07-09 20:40

The hypothesis in this section was implemented and then falsified by the
user's live isolation test. It is retained only to explain the rejected build;
the current diagnosis and replacement fix are in the next section.

The rollback restart removed the systemic long-gap regression while leaving
the original malformed both-diverge double frogs visible. The current code
shows why the previous all-generic replacement was the wrong level of change
and why the original assembly still overbuilds the frog.

For a standard/narrow `CrossingFrogCandidate`, the renderer creates a
`ContinuousStockHandoff` from the standard rail's blade-side cut boundary,
through the measured intersection, to the narrow rail's opposite cut
boundary. That handoff is the correct boundary-spanning piece and its lateral
position was previously confirmed live at p997.

Before the handoff is added, however,
`TryCreateNarrowBranchExtendedFixedPoint` replaces an adjacent fixed piece on
each participating rail with a mesh spanning from one cut boundary all the
way through the intersection and out the other side. The later handoff then
duplicates one half of the standard extension and one half of the narrow
extension. The result is three full paths stacked through one crossing, which
matches the full-width overlapping/V-like rails in the supplied image.

A proper K/double-frog assembly for this measured envelope is:

1. retain the continuous standard-to-narrow handoff exactly as built;
2. leave the fixed approach on each handoff-owned boundary unextended so it
   joins the handoff once;
3. extend only the complementary standard and narrow approaches to the
   measured intersection as planed/tapered point rails, using the handoff as
   their stock reference and maintaining a flangeway at each tip.

This fills the existing cut envelope and changes only the two duplicate
full-span extensions. It does not substitute the generic four-point crossing
assembly, alter frog counts/cuts, or affect narrow-branch switches such as
G832. The rule is scoped to `DualBothDiverge` anatomy, not node ids.

Implemented in `TryCreateNarrowBranchExtendedFixedPoint`: for a
both-diverge crossing, each fixed piece adjacent to the crossing cut is
classified by which side of the measured intersection it occupies. A piece
on a handoff-owned side falls through to ordinary fixed rendering. A piece on
the complementary side is extended only to the measured intersection and
rendered with `CreatePlanedFrogPointRail`, using the corrected continuous
handoff curve as its stock reference. The point tip is planed to the standard
head-plus-flangeway separation and tapered over the existing frog setback.

A static scan of the fresh exports for `fc97`, `l4a4`, `ltci`, `p997`,
`u6n0`, `NDeHartPassing_wqbb`, and `Npv2` found exactly one fixed approach on
both sides of both rails at every standard/narrow crossing cut. Therefore the
new rule has the required four inputs on every current both-diverge plan and
will select exactly two ordinary handoff approaches plus two planed point
approaches per double frog.

Build/deploy completed with 0 warnings and 0 errors. Built and deployed DLLs
both have timestamp 2026-07-09 20:34:26, size 738,816 bytes, and SHA-256
`0975075306CCACA535585E4487DEB07EDFF96676506642ACC01DA86AF107F487`.

Manual verification after a full restart should show two
`[BothDivergeCrossing]` log entries per both-diverge switch—one standard and
one narrow complementary point—and no such entries for G832. Visually, the
continuous handoff must remain joined at both cut boundaries while the two
formerly full-span overlapping rails become tapered K-frog points beside it.

## Planed-point attempt rejected; confirmed flangeway mirror error - 2026-07-09 20:45

The user live-tested the planed-point build and rejected it: it changed the
frog points adversely and did not fix the reported defect. That entire source
change was removed and the stable `349fb99` renderer was rebuilt/redeployed
before proceeding.

The user then isolated fc97's existing components in the adjustment UI. With
`CrossingFrog-2-ContinuousStockHandoff` disabled, the two extended frog rails
remain visible and prove that the point mesh is being clipped on the blue,
outside edge of the narrow through rail. The desired flangeway is the red,
inside edge. This falsifies the full-span-duplication diagnosis as the primary
bug and directly identifies a mirror/keep-side error in the existing mesh
clipper.

The fc97 standard point is `Fixed-10-StandardThroughFrog`. Its renderer passes
two ordered cutters to `BuildFlangewayCutFrogRailMesh`:

1. `standardFlangeway` at index 0;
2. `narrowFlangeway` at index 1.

`ShouldAutoFlipFlangewayKeepSide` currently always returns false even though
the mesh builder already supports inverting one cutter, and
`AutoFlipFlangewayKeepSideIndex` selects index 1 when enabled. The user's
red/blue evidence calls for exactly that operation: invert only the narrow
flangeway cutter on a both-diverge `StandardThroughFrog`. The cut also needs
the existing distance window at every both-diverge instance, not the current
fc97-id-only localization, so the inverted half-plane cannot affect parallel
rail territory outside the frog.

The user separately identified `Guard-6`. The fresh fc97 plan proves guard 6
is the local crossing guard generated by `TryBuildLocalCrossingGuard` after
the two ordinary guards for each of the three frogs. That function correctly
chooses the `+/- GuardCenterOffset` candidate farther from the stock handoff,
then applies an extra `-/+ RailHeadWidth` offset in the opposite direction,
moving the already-correct guard back toward the wrong side. Removing only
that extra railhead shift preserves the measured opposite diagonal and its
flangeway-selected side.

The replacement fix therefore makes only two side corrections:

- enable narrow-cutter index 1 inversion and localized clipping for
  `DualBothDiverge` `StandardThroughFrog` meshes;
- return the already-selected local crossing guard without the additional
  railhead-width shift.

It does not alter frog point spans, handoff geometry, compiler cuts, or frog
counts.

Implemented exactly those two corrections. `ShouldAutoFlipFlangewayKeepSide`
now enables inversion for `DualBothDiverge` `StandardThroughFrog` objects;
`AutoFlipFlangewayKeepSideIndex` consequently selects ordered cutter index 1,
the narrow flangeway. `ShouldLocalizeFrogFlangewayCut` now applies the existing
frog-centered cut window to the same anatomy across the preset instead of
hardcoding fc97. `TryBuildLocalCrossingGuard` now returns its already-selected
farther guard curve directly, without the extra `RailHeadWidth` offset toward
the handoff.

Build/deploy completed with 0 warnings and 0 errors. Built and deployed DLLs
both have timestamp 2026-07-09 20:45:27, size 737,280 bytes, and SHA-256
`D36F4A1FEBB9A2D87AD6F6D8D944E082FDA990903C0124462C27A6089E2E464E`.
The failed planed-point source is absent from this DLL.

After a full restart, fc97 should retain the original point spans and
continuous handoff. With the handoff hidden in the adjustment UI,
`Fixed-10-StandardThroughFrog` should now be clipped on the red inside edge of
the narrow through rail rather than the blue outside edge, and guard 6 should
move outward by exactly one railhead width into its intended check-rail
position. Then spot-check the mirror-hand both-diverge switches to confirm the
anatomy-based rule flips correctly there too.

## Live guard regression and one-railhead handoff offset - 2026-07-09 21:00

The full-restart result separated the remaining errors. The user reports that
removing the local guard's final `RailHeadWidth` shift puts fc97's guard in the
correct position, but moves the corresponding guards on the other switches too
far. The fc97 continuous stock handoff also remains displaced by exactly one
railhead width. The user identified the second supplied image
(`Screenshot 2026-07-09 204917.png`) as fc97.

Both generated kinked-rail helpers hardcode `Hand.Left` in the returned
`LineCurve`. That metadata is not merely descriptive: Railroader's asymmetric
rail profile is centered half a head width to one side of the curve, so
changing the rendered curve between `Hand.Left` and `Hand.Right` moves the
visible railhead by one complete head width. fc97's measured standard crossing
rail and selected narrow guard owner are right-hand curves. The continuous
handoff therefore has the exact one-head-width error reported by the user even
though its boundary points are correct.

The removed local-guard shift was compensating for the same forced-left
profile frame. The correct general rule is to preserve the measured owner
hand, not to delete the geometric compensation globally:

- construct the continuous stock handoff with `standardRail.Curve.hand`;
- construct the local guard diagonal with `guardOwner.Curve.hand` and its
  comparison handoff with the standard owner's hand;
- restore the original one-head-width guard-center compensation.

For an existing left-hand guard this preserves the pre-regression placement.
For fc97's right-hand guard, the corrected profile hand moves the visible
railhead one width while the restored centerline compensation moves it back
the opposite width, retaining the live-confirmed guard position. This is based
on measured rail handedness and introduces no node-id or switch-name case.

Implemented the measured-hand propagation in both kinked helpers and restored
the guard-center compensation. Build/deploy completed with 0 warnings and 0
errors. Built and deployed DLLs both have timestamp 2026-07-09 20:59:50, size
737,792 bytes, and SHA-256
`3850E8CD4E322223ACE9D42C4D27B3D15E6794E1E171390164B01E7C9BCC3785`.
No game process was launched or controlled.
