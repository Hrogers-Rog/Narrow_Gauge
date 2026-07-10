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

## Both-diverge complementary point profiles still face outward - 2026-07-09 21:22

After the measured-hand handoff build, the user supplied
`Screenshot 2026-07-09 211109.png` and identified the remaining profile-side
error: the outside running/stock rail is present, but one complementary point
rail is not projected inward into the crossing frog. The fresh 21:13 runtime
log confirms fc97 is still a valid 18-fixed/3-frog plan, so no point piece was
lost by validation or fallback selection.

The remaining distinction is between a continuous running rail and a frog
point made from that rail's measured curve. `CreateCrossingFrogAssembly` now
correctly preserves the continuous stock handoff's measured hand. However,
`TryCreateNarrowBranchExtendedFixedPoint` also preserves the ordinary running
rail hand on all three possible complementary point objects:

- `StandardThroughFrog`;
- `NarrowThroughFrog`;
- `NarrowReversedFrog`.

That is wrong specifically inside a both-diverge crossing envelope. A normal
left/right running rail uses its hand to place the asymmetric head outside its
gauge-face centerline. A complementary frog point uses the same measured path
but must project the head to the opposite, inward side so it enters the frog
and can be clipped against the flangeways. fc97's crossing rails are both
right-hand, so their point copies also remain right-hand today; changing a
point copy to left-hand produces the exact one-head-width inward projection
the screenshot is missing. Mirror layouts require the inverse operation.

The general correction is therefore to reverse only the profile hand of the
three complementary fixed-point render curves when the preset is
`DualBothDiverge`. Curve points, rotations, spans, flangeway centers, keep-side
selection, and the continuous stock handoff remain unchanged. Reversing each
source curve's own hand rather than assigning a fixed hand also handles route
curves whose traversal direction and route-relative `RailSide` are reversed.

Implemented `FaceBothDivergeCrossingPointInward` after measured-frame
correction and applied it only to those three point-object paths. Build/deploy
completed with 0 warnings and 0 errors. Built and deployed DLLs both have
timestamp 2026-07-09 21:24:00, size 737,792 bytes, and SHA-256
`BD512634D9931B3288F773120328D0F32467021E031D60DFA14816FB1B411078`.
No game process was launched or controlled.

## Narrow point-profile flip rejected by live result - 2026-07-09 21:33

The full-restart result in `Screenshot 2026-07-09 212930.png` falsifies the
three-path conclusion above. The user reports that `NarrowThroughFrog` moved
left by exactly one railhead width and that some `NarrowReversedFrog` objects
now render only half a rail. Those are the direct expected signatures of the
new narrow profile-hand reversal: changing hand moves the asymmetric profile
one full head width while the unchanged flangeway half-planes can then retain
only part of the displaced head.

The narrow crossing rails were already correct in the measured-hand build and
must retain `CorrectMeasuredRailRenderFrame`'s original hand. The reported
outside-stock correction therefore applies only to the standard
`StandardThroughFrog` point copy. The next build removes
`FaceBothDivergeCrossingPointInward` from both `NarrowThroughFrog` and
`NarrowReversedFrog`, retaining it only on `StandardThroughFrog`. No cutter,
span, centerline, handoff, or guard changes are needed.

Implemented that narrow rollback and renamed the remaining helper to
`FaceBothDivergeStandardCrossingPointInward`. Build/deploy completed with 0
warnings and 0 errors. Built and deployed DLLs both have timestamp
2026-07-09 21:35:11, size 737,792 bytes, and SHA-256
`98FBEC1381450E4344011D0452C0E29B0C7E14381ECAECFDEA5B349733426437`.
No game process was launched or controlled.

## NarrowReversed needs a local push, not a hand reversal - 2026-07-09 21:45

After the narrow-hand rollback, the user supplied
`Screenshot 2026-07-09 214109.png`: `NarrowReversedFrog` again renders at full
profile width, but its frog end remains laterally outside the frog by exactly
one railhead width. This separates two requirements that the rejected hand
flip conflated:

1. the outside/approach end must retain its measured curve and hand so it joins
   the full-width running rail;
2. only the frog-local point must move inward by one railhead width.

A uniform hand reversal moves the asymmetric profile for the complete object
and changes its extrusion/clipping behavior, producing the prior shifted and
half-rail regression. A uniform parallel offset would likewise break the
approach seam. The required geometry is a local point push: keep zero lateral
offset at the crossing cut boundaries, smoothly reach one `HeadWidth` of
inward offset at the measured frog center, and retain the original `Hand`.

The correction will apply only to the both-diverge `NarrowReversedFrog`
render path. Its signed inward direction is the displacement that an opposite
profile hand would have produced (`+HeadWidth` for a left-hand source and
`-HeadWidth` for a right-hand source), but applied to curve points with a
smooth frog-centered weight. `NarrowThroughFrog`, flangeway cutters, keep-side
selection, and the continuous handoff remain unchanged.

Implemented `PushBothDivergeNarrowReversedPointIntoFrog`. It subdivides the
existing corrected render curve, applies a smooth signed lateral weight that
is zero at the cut boundaries and one head width at the frog center, then
rebuilds point rotations while preserving the original hand. Build/deploy
completed with 0 warnings and 0 errors. Built and deployed DLLs both have
timestamp 2026-07-09 21:48:04, size 738,304 bytes, and SHA-256
`67877FCF903C39801BCF2127EABDC70F929F3B4753D50B1AD5CF85AE8EE89896`.
No game process was launched or controlled.

## Curve deformations rejected; missing opposing-cutter symmetry - 2026-07-09 22:35

The full-restart result in `Screenshot 2026-07-09 221009.png` rejects both
remaining deformation experiments. fc97's `NarrowReversedFrog` is still one
railhead outside the frog despite the frog-centered curve push, which means
the pushed portion is removed by the subsequent flangeway clipping.
`StandardThroughFrog` now has a visible kink and malformed cut from the
profile-hand reversal. Neither rail curve should be bent or re-handed.

The actual clipper inputs expose the missing symmetric correction. Every
crossing-point rail is cut by two ordered flangeway centers:

1. `standardFlangeway` at index 0;
2. `narrowFlangeway` at index 1.

For `StandardThroughFrog`, the opposing-family cutter is the narrow cutter at
index 1; the earlier red/blue evidence correctly led to flipping that index.
For `NarrowReversedFrog`, the opposing-family cutter is instead the standard
cutter at index 0. That keep-side was never inverted, so the clipper continues
to retain the outside part of the narrow point regardless of attempted curve
movement.

The next build removes `FaceBothDivergeStandardCrossingPointInward` and
`PushBothDivergeNarrowReversedPointIntoFrog` completely. It extends the
existing both-diverge keep-side/local-window rule to
`NarrowReversedFrog`, selecting index 0 there while retaining index 1 for
`StandardThroughFrog`. `NarrowThroughFrog` remains unchanged. This alters only
which side of the opposing flangeway is retained inside the frog window; rail
centerlines, hands, rotations, spans, and the continuous handoff remain
measured.

### Adjustment rebuild parity follow-up - 2026-07-09 22:42

- `SpecialWorkHardwareRenderer` passes the localized cut focus/window when it
  first creates `StandardThroughFrog` and `NarrowReversedFrog`.
- `SpecialWorkAdjustmentUI.ResolveSpecialRenderedInterval` reconstructs the
  same flangeway-cut inputs, but its two corresponding branches did not restore
  that focus/window. A later adjustment rebuild could therefore apply the
  opposing flangeway cutter along the full frog rail and reintroduce the kink
  and malformed point visible in `Screenshot 2026-07-09 221009.png`.
- Restore the existing `ShouldLocalizeFrogFlangewayCut` / frog-window values in
  those two branches only. Leave `NarrowThroughFrog` unchanged.

Implemented the symmetric cutter fix and removed both deformation helpers.
`StandardThroughFrog` now retains its measured rail and flips opposing cutter
index 1; `NarrowReversedFrog` retains its measured rail and flips opposing
cutter index 0. Both initial rendering and adjustment reconstruction pass the
same frog-local cut focus/window. `NarrowThroughFrog` remains unchanged.

Build/deploy completed with 0 warnings and 0 errors. Built and deployed DLLs
both have timestamp 2026-07-09 22:41:25, size 737,792 bytes, and SHA-256
`C770A49E4F94C984C05335BE73340654FCDEF5252244B5C9D4F6268F51D153A3`.
No game process was launched or controlled.

## Keep-side inversion retains the wrong-side extension - 2026-07-09 22:57

The paired live screenshots isolate the newest regression:

- `Screenshot 2026-07-09 224542.png` shows g832 without the new overlay;
- `Screenshot 2026-07-09 224656.png` shows the bad extended/cut rails on the
  other switches;
- close-up `Screenshot 2026-07-09 224732.png` proves the correct continuous
  frog remains underneath. The visible defect is the separately rendered
  `StandardThroughFrog` / `NarrowReversedFrog` mesh surviving over it and
  clipping through it.

The current `Player.log` explains the group boundary. g832 is
`dual.narrow-branch-joins-main`; fc97, l4a4, ltci, p997, u6n0, wqbb, and pv2
are `dual.both-diverge`. Therefore g832 did not exercise the new
`IsDualBothDiverge` keep-side inversion, while every reported regression did.

The mesh builder already derives each flangeway keep sign from a `keepPoint`
located safely on the measured fixed piece. That is the ownership invariant:
the clip must retain the side containing the fixed-piece anchor. Negating one
of those signs deliberately violates the invariant and can retain the
extension across the frog instead, which is precisely the overlaid rail shown
in `224732`. The local distance window only confines the wrong-side remnant;
it does not make the inversion valid.

Reject the symmetric cutter inversion conclusion. Restore automatic inversion
to false and remove both-diverge local-window arguments from
`StandardThroughFrog` and `NarrowReversedFrog`, including adjustment rebuild
parity. Keep the measured curves/hands and the continuous frog unchanged. This
rollback addresses the new overlay regression without reviving either rejected
curve/profile deformation.

Implemented the rollback. Both affected render calls now use their measured
fixed-piece `keepPoint` without automatic inversion or a local distance window;
the adjustment rebuild path restores the same inputs. The deformation helpers
remain removed. Build/deploy completed with 0 warnings and 0 errors. Built and
deployed DLLs both have timestamp 2026-07-09 23:04:46, size 737,280 bytes, and
SHA-256 `6D31FCA3EED9D6E38D365A14E0DEA94C8B0965E7DAC366E50C6BE00596CADCA1`.
No game process was launched or controlled.

## NarrowReversed needs cutter 1 mirrored, not cutter 0 - 2026-07-09 23:24

`Screenshot 2026-07-09 231931.png` and the user's close visual identification
resolve the remaining ambiguity. `NarrowReversedFrog` is already extended well
into the frog and is being cut; the bevel is simply taken from the outside of
the railhead instead of the inside. The cutter is correctly sourced from the
narrow through rail.

The flangeway inputs are ordered `[standardFlangeway, narrowFlangeway]`. The
rejected symmetric build inverted index 0 for `NarrowReversedFrog`. That is the
standard crossing boundary, so negating it retained the extension on the wrong
side of the frog and produced the overlay in `224732`. It did not mirror the
narrow-through bevel the user was identifying.

The bounded correction is therefore:

- keep `StandardThroughFrog` entirely on measured/default keep sides;
- enable automatic mirror only for both-diverge objects named
  `NarrowReversedFrog`;
- invert ordered cutter index 1, the narrow-through boundary;
- do not localize the cut or alter spans, curves, profile hands, the continuous
  frog, or `NarrowThroughFrog`.

Because the role name is emitted only on the reverse-side branch, this is
orientation-scoped without any node-ID special case.

Implemented exactly that role/index correction. Initial rendering passes
automatic inversion with index 1 only for both-diverge
`NarrowReversedFrog`; `SpecialWorkAdjustmentUI` already reconstructs meshes
from the same helper values. `ShouldLocalizeFrogFlangewayCut` remains false.

Build/deploy completed with 0 warnings and 0 errors. Built and deployed DLLs
both have timestamp 2026-07-09 23:25:48, size 737,280 bytes, and SHA-256
`4B9806ED5909F7B7BEB25B477228197A333BAE303D77A21044C5DF70A7F4DFDE`.
No game process was launched or controlled.

## Cutter-1 keep-side inversion rejected - 2026-07-09 23:30

Full-restart screenshot `Screenshot 2026-07-09 232828.png` rejects the index-1
inversion. Both visible cutter results are now malformed, not merely the
`NarrowReversedFrog` face. `BuildFlangewayCutFrogRailMesh` applies the two cuts
as an intersection of retained half-planes; negating either keep sign changes
the surviving wedge. It is not a mirror of one bevel boundary in isolation.

Roll back only the `76bc0fd` behavior: restore `NarrowReversedFrog` to the
measured fixed-piece keep signs and return automatic inversion to false/-1.
Retain the `3290db4` overlay rollback, removal of curve/profile deformations,
and all measured spans/hands. Any future inside-face correction must move or
reconstruct the relevant cut boundary rather than invert a retained
half-plane.

Implemented the recovery rollback. `NarrowReversedFrog` again calls the
default flangeway-cut path, and automatic inversion returns false/index `-1`.
The source matches the cutter behavior in `3290db4`; the overlay rollback and
measured rail geometry remain intact.

Build/deploy completed with 0 warnings and 0 errors. Built and deployed DLLs
both have timestamp 2026-07-09 23:31:13, size 737,280 bytes, and SHA-256
`4AA7E65E1D2553738C83A9DFF537926BF7C9361E90FC44987C8AA314CC17A3CC`.
No game process was launched or controlled.

## Editor identifies Fixed-10 StandardThrough and the physical cutter - 2026-07-10 00:32

The user isolated the bad mesh in the Special Work Editor: disabling
`Fixed-10-StandardThroughFrog` removes the rail being diagnosed. The current
fc97 export maps fixed 10 to source rail `standard-reversed:right`, interval
64.536-69.210. Its crossing frog is
`standard-reversed:right x narrow-normal:right`.

This corrects the naming mix-up in the preceding attempts: the target is not
the object named `NarrowReversedFrog`; it is the extended
`StandardThroughFrog`. The user then used the editor's manual cut on fixed 10
with physical rail `narrow-normal:right`. That cut landed in the correct
inside-face position, proving both the target and cutter, but the editor's
default 63 mm full width was visibly narrower than the other frog clearances.

The manual-cut path differs from the automatic frog path in the relevant way:
it rebuilds the extended standard rail with a single corrected slice of the
physical `narrow-normal:right` rail as the cutter. The automatic path instead
uses the route's narrow flange-guide curve as one of two retained half-planes.
Therefore the bounded automatic correction should reproduce the successful
manual geometry, not invert either keep sign:

- target only both-diverge `StandardThroughFrog` whose crossing cutter is the
  semantic `narrow-normal:right` rail;
- slice and render-correct that physical rail over the extended point span;
- use it as the sole cut center with the same keep-tail/fixed-piece anchor;
- use `RailHeadWidth + FlangewayWidth` (76 + 50 = 126 mm) as the full cut width,
  so its 63 mm half-width matches the intended railhead-plus-flangeway
  clearance rather than the editor's 31.5 mm half-width;
- mirror the same cutter selection in adjustment reconstruction.

Do not alter other `StandardThroughFrog` orientations, any
`NarrowReversedFrog`, the continuous handoff, rail spans/hands, or keep signs.

Implemented the physical-cutter path in both initial rendering and adjustment
reconstruction. `ShouldUsePhysicalNarrowThroughCutter` scopes it to a
both-diverge `StandardThroughFrog` cut by the semantic narrow-normal right
rail. The target span and keep point are unchanged; the cutter is a physical
rail slice covering that span and the full width is 126 mm. All other frog
roles retain the existing flange-guide path.

Build/deploy completed with 0 warnings and 0 errors. The build intentionally
preserved the concurrent vdlt truth-table work already present in the shared
working tree. Built and deployed DLLs both have timestamp
2026-07-10 00:34:11, size 742,400 bytes, and SHA-256
`E9A491AB31078528C24028F732418D7FA5065788EE8D2C2E41126E3C0E0D417B`.
No game process was launched or controlled.

## vdlt standard-right transition profile reversal - 2026-07-10

After the narrow-branch truth-table correction fixed vdlt's blades, the user
identified the two remaining displaced objects in the Special Work Editor as
`VeeFrog-0-WingA` and `CrossingFrog-2-ContinuousStockHandoff`. The current
vdlt plan maps both objects to `standard-through:right`: renderer Vee 0 is plan
`v2-frog:1`, whose RailA is `standard-through:right`, and the crossing handoff
is built from plan `v2-frog:0`'s standard rail, also
`standard-through:right`. The user reports both visible heads displaced by
exactly one railhead width while their centerline geometry is otherwise in the
correct place.

Both renderer paths reverse traversal relative to that measured standard rail
but retain its original `Hand.Right` metadata. A stock-rail profile center is
offset by half a head width on the hand side of the curve. Reversing the curve
direction reverses its local right vector; retaining the same hand therefore
moves the visible profile from one side of the centerline to the other, a full
`HeadWidth`. The centerline must not be shifted. The physically invariant rule
is instead:

- when `BuildNarrowBranchStockHandoff` runs from its standard boundary opposite
  the measured standard-rail tangent, use the opposite hand for the generated
  handoff;
- when `CreateVeeWingRail` reverses a measured source slice, reverse its hand
  along with its point order/directions so the visible profile remains on the
  same world-space side.

This is traversal-derived rather than node-id-, switch-name-, or fixed-side
specific. It leaves aligned handoffs and non-reversed wings unchanged, and it
does not alter frog positions, cut spans, blades, guards, or source rail
centerlines.

Implemented the traversal-relative correction in
`SpecialWorkHardwareRenderer`. Reversed vee-wing source slices now also invert
their hand, preserving their visible profile side. Narrow-branch stock
handoffs compare their generated traversal with the measured standard-rail
tangent and invert the source hand only when those directions oppose.

Build/deploy completed with 0 warnings and 0 errors. Built and deployed DLLs
both have timestamp 2026-07-10 00:58:15, size 742,912 bytes, and SHA-256
`6A6BBB48FDD5FEC6DF1F3B2D141D65D275ED8AB16B17D8571061DC791961B941`.
The shared build includes Claude's concurrent vdlt blade-selection changes;
those files remain separate and were not staged as part of this correction.
No game process was launched or controlled.

## vdlt extends the standard crossing point from the wrong arm - 2026-07-10

The live result after the profile-frame correction shows a separate defect:
one generated crossing point is extended from the wrong fixed rail arm. The
user identified commit `3290db4` as the prior wrong-side-extension reference.
That commit established the relevant ownership invariant: a frog-point clip
must retain and extend the side containing its measured fixed-piece anchor,
not the opposite arm across the frog.

`TryCreateNarrowBranchExtendedFixedPoint` already chooses the narrow point arm
from `narrowBladeSide`, but it unconditionally chooses the standard piece whose
`StartDistance` is after the crossing. That happens to match g832's orientation
and fails on vdlt's mirror. `BuildNarrowBranchStockHandoff` supplies the missing
physical rule: its standard boundary is on `standardBladeSide` (toward the
blades), so the complementary standard frog point must come from the opposite
side. Therefore the standard point is the after-crossing fixed piece only when
`standardBladeSide < 0`; when `standardBladeSide > 0`, it is the before-crossing
fixed piece.

The correction must choose and extend the standard fixed piece using this
blade-relative side, with the keep point anchored inside that same piece. It
must not change rail ids, crossing geometry, cutter paths, handoff geometry,
profile hands, or extension lengths.

Implemented the mirror-aware standard-arm selection only for
`DualNarrowBranch`. The renderer now derives `standardBladeSide` alongside the
existing narrow side, keeps g832's after-crossing behavior when the standard
blade side is negative, and selects vdlt's before-crossing fixed piece when it
is positive. The generated curve extends through the same `CutHalfLength`, and
its keep point is taken from whichever measured fixed piece was selected.
Other presets preserve the prior after-crossing standard selection.

Build/deploy completed with 0 warnings and 0 errors. Built and deployed DLLs
both have timestamp 2026-07-10 01:13:16, size 742,912 bytes, and SHA-256
`61B7BEF755987A90BA825B5DBD8F1B2DABA412299304A4650AD0E4946005B4ED`.
The shared build continues to include Claude's concurrent vdlt blade-selection
changes; those files remain separate and were not staged for this fix. No game
process was launched or controlled.

## vdlt VeeFrog-0-WingB aims at the opposite rail heel - 2026-07-10

The user identified `VeeFrog-0-WingB` as having an improper angle instead of
following its outside stock rail. `CreateVeeFrogAssembly` passes Wing B's own
source as Rail B, but `CreateVeeWingRail` appends `oppositeHeel`, which is Rail
A's heel. Wing A is symmetrically aimed at Rail B's heel. This makes each wing
leave its measured source rail and bend toward the other route.

The argument previously named `otherHeel` is actually the heel belonging to
the source rail: heel A for Wing A and heel B for Wing B. Rename it
`sourceHeel`; each wing must append that own heel and its rotation. The
opposite heel remains useful only to derive the outward flare direction. This
changes the generated bend endpoint, not the source slice, frog nose, profile
hand, or fixed-point/cutter selection.

Implemented by renaming the own-rail heel parameter to `sourceHeel`, appending
that point and rotation to the wing, and deriving the short flare vector away
from `oppositeHeel`. Both Wing A and Wing B now follow their own source rail.

The combined build containing both vdlt ownership corrections deployed with 0
warnings and 0 errors. Built and deployed DLLs both have timestamp
2026-07-10 01:17:35, size 742,912 bytes, and SHA-256
`DE78F7574CC14F15579EBB2FFEFB9E9269A017B70C518A218D2ADA3268928A02`.
Claude's concurrent blade-selection files remain present in the shared build
but separate from the focused changes for this turn. No game process was
launched or controlled.
