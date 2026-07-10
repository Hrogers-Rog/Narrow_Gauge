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
4. `dual.both-diverge` crossing frogs now use generic crossing-point geometry;
   the continuous stock handoff remains available to narrow-branch anatomy.
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
- `fc97` and `l4a4` keep valid plans, while their crossing candidates render
  generic double-frog point rails rather than a continuous stock handoff.

Visually recheck all five named switches and spot-check `Nove`, `G832`, and a
previously good both-diverge switch. Static compilation cannot prove mesh
winding or final scene overlap.
