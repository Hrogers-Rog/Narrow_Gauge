# Dual-Gauge and Special-Trackwork Geometry Generator

## Scope

This document proposes a turnout-geometry generator for the NarrowGauge FUSE
module. Its first target is one dual-gauge turnout where standard gauge
continues straight and narrow gauge has a straight and diverging route.

The architecture intentionally supports later special work without assuming
that one visible object equals one native switch or one frog:

- single and double slips
- stub switches
- crossings at arbitrary angles
- three-way switches
- gauge splits and merges
- standard-only and narrow-only branches joining or leaving dual gauge
- compound dual-gauge turnouts
- multiple synchronized native switch nodes

This work does not require mixed-gauge coupling or a replacement graph system.
It should compile special-work definitions into ordinary Railroader graph
nodes and segments wherever possible.

## Executive Decision

Do not build a dual-gauge turnout by drawing a complete standard turnout and a
complete narrow turnout on top of each other.

Instead, treat special trackwork as a compiler with two related outputs:

```text
SpecialWorkDefinition
  -> logical route centerlines
  -> physical running-rail centerlines
  -> merge shared rail intervals
  -> detect and classify physical rail intersections
  -> cut rails and generate frogs, wing rails, guards, and movable assemblies
  -> SpecialWorkMeshPlan

SpecialWorkDefinition
  -> NativeTopologyPlan
  -> ordinary Railroader nodes, segments, and binary switch nodes
  -> synchronized switch groups where required
```

Geometry and graph topology come from the same definition, but neither should
be inferred from the other after the fact. A physical rail crossing does not
connect graph routes. A slip route connects graph routes only when that route
is explicitly present in the definition.

The existing base turnout builder remains useful for its curve, mesh, and
switch-control primitives. It is not suitable as the complete special-work
builder because it assumes exactly one native switch and one frog.

## Findings: Base Game

### Descriptor and Native Switch Assumptions

`TrackObjectManager.BuildDescriptors(...)` in
`Assembly-CSharp/Track/TrackObjectManager.cs` identifies an ordinary switch at
a native three-leg `TrackNode`. It selects the two branch segment proxies,
calls `SwitchGeometry.Calculate(...)`, and creates one switch descriptor.

This is a useful and stable graph contract:

- keep native switch nodes binary and three-leg
- compile complex visible work into multiple ordinary native switch nodes
- synchronize those nodes through a module-owned switch group when necessary

It is not a suitable visible-object contract for compound trackwork because
one special-work object may contain several native switches, crossings, and
movable assemblies.

### `SwitchGeometry.Calculate(...)`

`Assembly-CSharp/Track/SwitchGeometry.cs` does the following:

1. Aligns two route-centerline curves to the common switch origin.
2. Calls `MakeTrackLineSegments(center, gauge)` to make two physical rail
   centerlines for each route.
3. Looks for the first crossing between opposite route rails:
   route A left against route B right, or A right against B left.
4. Uses that one crossing to slice closure rails, stock rails, points, guards,
   and one frog.
5. Returns one `SwitchGeometry` containing one frog point triplet, two guards,
   two stock rails, two closure rails, and two point rails.

This method is built around the assumption `one switch = one frog`. It cannot
represent a dual-gauge turnout's multiple rail crossings or a diamond/slip.

The following parts are reusable:

- route-centerline handling patterns
- `MakeTrackLineSegments(...)` for gauge-offset rail centerlines
- curve slicing and distance-along-curve concepts
- ordinary turnout dimensions as defaults

The first-intersection search and the one-frog result type must not be reused
as the special-work data model.

### Rail and Frog Mesh Primitives

`TrackMeshBuilder.BuildStockRailMesh(...)` is the most reusable base primitive.
It extrudes a rail along a `LineCurve` and accepts a per-point profile-scale
function. That supports:

- ordinary running rails
- tapered point blades
- wing rails
- guard rails
- narrowed or widened frog/crossing pieces

`TrackMeshBuilder.BuildFrogMesh(LinePoint[3], Gauge)` builds one ordinary vee
frog. It derives handedness from the signed cross product around the frog
point and widens its profile using approximately:

```text
profileScale = 1 / sin(crossingAngle / 2)
```

It should remain available for simple vee frogs, but crossing diamonds and
compound frogs need mesh plans made from multiple trimmed rail pieces.

`SwitchGeometry.MakeGuardRail(...)` provides a useful basic tapered parallel
rail, but its selection and placement policy assumes one ordinary turnout
frog. The special-work builder must decide which wheel path requires each
guard rail before using a similar mesh primitive.

### Point Blade Control

`TrackObjectBuilder.CreateSwitchObjectHelper(...)` renders one ordinary switch.
It creates stock rails, closure rails, one frog, two guards, two point rails,
ties, and a stand. It then uses:

```text
SwitchPointRails.Configure(node, normalPointRail, reversedPointRail, ...)
```

to animate the moving point rails from native switch state.

The complete helper cannot render special work, but `SwitchPointRails` is a
useful control primitive for a simple point-blade assembly. Compound work
needs a group controller that can bind several movable assemblies to one or
more native nodes.

## Findings: C_L_B.DKW and KRE

C_L_B.DKW does not ask the ordinary base turnout builder to create its
compound crossing geometry. It procedurally finds rail intersections, cuts
rail curves, and creates crossing parts.

### Useful Patterns

`DKW_Util.Intersects(LineCurve, LineCurve, out point, out distA, out distB)`
returns both the intersection position and cumulative distances along both
curves. Distances on both rails are essential because every crossing must
later cut and reconnect both participating rails.

`KRESpliney.CalculateKREProxies(...)` creates left and right rail curves for
two crossing route centerlines and tests all four physical rail pairs:

```text
A.left  x B.left
A.left  x B.right
A.right x B.left
A.right x B.right
```

It uses the earliest and latest physical-rail crossings to isolate a crossing
region with additional margin.

KRE and `DKWSpliney` then:

- calculate local rail directions and crossing angles
- trim physical rails around each crossing
- create frog noses, hearts, and wing rails
- create several guard rails
- use angle-derived setbacks and rail profile scales

Representative useful geometric relationships are:

```text
outer rail-head setback ~= railHeadWidth / tan(angle / 2)
inner/flangeway setback ~= flangewayWidth / sin(angle / 2)
frog profile scale      ~= 1 / sin(angle / 2)
```

These values require clamping and validation at shallow angles.

`FakeLineCurve.removeAtLeastDistance(...)` and
`FakeLineCurve.setBackDistance(...)` demonstrate the needed ability to trim
and split rail curves at distance positions. A new implementation should use
a structured `RailPiece` and interval API instead of copying this mutable,
special-case-heavy type.

`DKW_Util.CreateGuardrail(...)` and the multiple guard calls in `DKWSpliney`
show that crossing protection is plural and angle-dependent.

### What Not to Copy

DKW/KRE contains valuable procedural geometry, but it is tightly coupled to
specific crossing layouts and uses many positional assumptions. The new
builder should reuse the mathematical ideas, not copy its implementation as a
general-purpose API.

It also should not use physical crossings to infer graph connectivity. That
would incorrectly make every diamond into a slip switch.

## Findings: Current NarrowGauge Module

`NarrowGaugeSwitchGeometry.Calculate(...)` is a gauge-parameterized version of
the ordinary base switch calculation. It remains useful for narrow-only
ordinary turnouts, but it still returns one frog and one point pair.

`NarrowGaugeTrackBuilder.CreateDualGaugeSwitchRailObjects(...)` currently
renders selected standard turnout parts and adds a guessed third rail. It does
not model shared-rail intervals, multiple rail crossings, rail cuts, or the
additional frog/guard assemblies. This directly explains the incomplete
geometry visible in the current test.

`CreateDualGaugeNarrowSplitSwitchRailObjects(...)` is a useful experiment, but
its transition/intersection guesses and omitted frog mesh cannot scale to
compound work.

`GhostGraphSynchronizer` is suitable for ordinary dual-gauge track, but its
offset-and-average node behavior is not a geometry definition for special
work. At one test switch, generated narrow endpoint candidates disagreed by
about `0.347m`; averaging them hides the disagreement rather than creating a
valid route.

Special-work routes and their native graph topology must be compiled together
from an explicit special-work definition. Users still place one visible
special-work object and select its gauge/layout mode; they never hand-place
ghost graph segments.

## Findings: FUSE Integration

The geometry implementation should remain in the NarrowGauge module so FUSE
updates do not repeatedly break it. The module can use existing FUSE public
extension points:

- `FuseEvents.TrackGraphApplying`
- `TrackAPI.RebuildGraph()`
- `TrackAPI.AddNode(...)`
- `TrackAPI.AddSegment(...)`
- `TrackAPI.GetSegmentDefinition(...)`

`TrackGraphApplying` is the right point for compiling generated native graph
pieces immediately before a rebuild. If FUSE later adopts special work
directly, the module's definition and compiler interfaces can move without
changing the geometry model.

Any FUSE changes made now should be limited to generic public events or
definition fields that are useful beyond narrow gauge.

## Proposed Data Model

### `SpecialWorkDefinition`

One authored or generated visible object:

```csharp
internal sealed class SpecialWorkDefinition
{
    public string Id;
    public LocalFrame Frame;
    public IReadOnlyList<SpecialWorkPort> Ports;
    public IReadOnlyList<LogicalRoute> Routes;
    public IReadOnlyList<SwitchGroupDefinition> SwitchGroups;
    public SpecialWorkDimensions Dimensions;
}
```

It may own many logical routes, native nodes, frogs, guards, and movable
assemblies. Ports connect the object to surrounding ordinary track.

### `SpecialWorkPort`

Every external leg declares which graph families are present. Dual gauge is a
physical corridor containing both independent route families; it is not a
third graph family.

```csharp
[Flags]
internal enum GaugeAvailability
{
    Standard = 1,
    Narrow = 2,
    Dual = Standard | Narrow
}

internal sealed class SpecialWorkPort
{
    public string Id;
    public GaugeAvailability AvailableFamilies;
    public SharedRailSide SharedRailSide; // None for single-gauge ports
    public Vector3 Position;
    public Vector3 Tangent;
}
```

Connection rules are strict:

- a standard route may connect to a standard-only or dual-gauge port
- a narrow route may connect to a narrow-only or dual-gauge port
- a standard route never connects to a narrow route
- a dual-gauge port exposes two independent graph connections
- a route family may begin or end at the special-work boundary

This lets a narrow-only yard lead join the narrow family embedded in a
dual-gauge main without making the yard lead or every yard track dual gauge.
The inverse standard-only-to-dual transition uses the same model.

### `LogicalRoute`

A route is a wheel-path centerline, not a physical rail:

```csharp
internal sealed class LogicalRoute
{
    public string Id;
    public GaugeFamily Family;       // Standard or Narrow
    public string EntryPortId;
    public string ExitPortId;
    public BezierCurve Centerline;
    public string SwitchGroupId;     // null for always-open routes
    public string RequiredStateId;   // null for always-open routes
}
```

A crossing route remains graph-disconnected from another crossing route.
Single/double slips add explicit connecting `LogicalRoute` instances.

### `RailCenterline`

A physical running-rail centerline generated from one or more logical routes:

```csharp
internal sealed class RailCenterline
{
    public string Id;
    public IReadOnlySet<string> SourceRouteIds;
    public IReadOnlySet<GaugeFamily> Families;
    public RailSide Side;
    public LineCurve Curve3D;
    public ProjectedPolyline Curve2D;
    public bool IsShared;
}
```

Shared rails are not a special third-rail guess. They are physical rail
intervals whose curves coincide within configured position, tangent, and
overlap-length tolerances.

### `RailIntersection`

```csharp
internal sealed class RailIntersection
{
    public string Id;
    public string RailAId;
    public string RailBId;
    public float DistanceA;
    public float DistanceB;
    public Vector2 LocalPoint;
    public Vector3 WorldPoint;
    public Vector2 TangentA;
    public Vector2 TangentB;
    public float SignedAngleDegrees;
    public float AcuteAngleDegrees;
    public RailIntersectionKind Kind;
}
```

`Kind` includes at least:

- `Unclassified`
- `SharedOverlapBoundary`
- `RouteJoin`
- `BladeConvergence`
- `VeeFrog`
- `CrossingFrog`
- `InvalidShallowCrossing`

### `FrogCandidate`

```csharp
internal sealed class FrogCandidate
{
    public string Id;
    public FrogKind Kind; // Vee, Crossing, future KCrossing
    public IReadOnlyList<string> IntersectionIds;
    public IReadOnlyList<string> RailIds;
    public IReadOnlyList<string> ProtectedRouteIds;
    public Vector3 Position;
    public Vector2 NoseDirection;
    public Vector2 HeelDirection;
    public float AcuteAngleDegrees;
    public IReadOnlyList<RailCut> RequiredCuts;
    public IReadOnlyList<WingRailPlan> WingRails;
}
```

One logical turnout may produce several `FrogCandidate` objects.

### `GuardRailCandidate`

```csharp
internal sealed class GuardRailCandidate
{
    public string Id;
    public string FrogId;
    public string ProtectedRouteId;
    public string OppositeRunningRailId;
    public float StartDistance;
    public float EndDistance;
    public RailSide OffsetSide;
    public float Offset;
    public float EndTaperLength;
}
```

Guard candidates are generated from a frog and its protected wheel path, then
deduplicated when their intervals overlap.

### `SwitchBladeCandidate`

```csharp
internal sealed class SwitchBladeCandidate
{
    public string Id;
    public MovableAssemblyKind Kind; // PointBlade or StubEnd
    public string SwitchGroupId;
    public string StockRailId;
    public IReadOnlyList<string> MovableRailIds;
    public float TipDistance;
    public float RootDistance;
    public IReadOnlyDictionary<string, MovableRailPose> StatePoses;
}
```

This candidate comes from route divergence metadata, not from a frog
intersection.

### Supporting Plans

The implementation also needs:

- `RailPiece`: a source-rail interval that can be split and trimmed safely
- `SharedRailInterval`: mapping from several source rails to one physical rail
- `SpecialWorkMeshPlan`: all fixed and movable visible geometry
- `NativeTopologyPlan`: native nodes, segments, and binary switches
- `SwitchGroupDefinition`: mapping from visible states to several native node
  states
- `SpecialWorkValidationResult`: errors and warnings with object locations
- `SpecialWorkDebugView`: centerlines, intersections, candidates, and labels

## `DualGaugeTurnoutGeometryBuilder`

`DualGaugeTurnoutGeometryBuilder` should be a preset/facade over a general
`SpecialWorkGeometryBuilder`, not the long-term core abstraction:

```csharp
internal sealed class SpecialWorkGeometryBuilder
{
    public SpecialWorkBuildResult Build(SpecialWorkDefinition definition);

    private LogicalRouteSet BuildRoutes(SpecialWorkDefinition definition);
    private PhysicalRailSet ExpandPhysicalRails(LogicalRouteSet routes);
    private PhysicalRailSet MergeSharedRails(PhysicalRailSet rails);
    private IReadOnlyList<RailIntersection> FindAllIntersections(
        PhysicalRailSet rails);
    private void ClassifyIntersections(
        PhysicalRailSet rails,
        IReadOnlyList<RailIntersection> intersections);
    private IReadOnlyList<FrogCandidate> BuildFrogCandidates(...);
    private IReadOnlyList<GuardRailCandidate> BuildGuardCandidates(...);
    private IReadOnlyList<SwitchBladeCandidate> BuildMovableCandidates(...);
    private RailPieceSet CutAndAssembleRailPieces(...);
    private SpecialWorkMeshPlan BuildMeshPlan(...);
    private SpecialWorkValidationResult Validate(...);
}

internal sealed class DualGaugeTurnoutGeometryBuilder
{
    public SpecialWorkBuildResult Build(DualGaugeTurnoutDefinition definition);
}
```

## A. Detecting Dual-Gauge Rail Crossings

### 1. Generate Logical Routes First

Each legal wheel path is generated as a route centerline. Gauge family is an
explicit property of that route. Route connectivity and switch-state
requirements are already known before physical rails are generated.

For the first proof of concept:

- standard route: entry to straight exit, always open
- narrow normal route: narrow entry to narrow straight exit
- narrow reverse route: narrow entry to narrow diverging exit

### 2. Expand Routes into Physical Rail Centerlines

Offset each route centerline by half its gauge to produce left and right
running-rail centerlines. The base game's
`SwitchGeometry.MakeTrackLineSegments(...)` is a usable starting primitive.

Record source route, family, and rail side on every generated rail.

### 3. Merge Shared Rail Intervals

Before testing crossings, detect near-collinear overlapping intervals:

- project samples into the object's local 2D frame
- compare point distance, tangent alignment, and overlap length
- split rails at overlap boundaries
- replace coincident intervals with one `RailCenterline`
- union their source routes and gauge families

This is interval-level merging. Two rails may share only part of their length
and then separate. Whole-curve deduplication is insufficient.

Merged shared intervals do not create frogs.

### 4. Project to Stable Local 2D

Create a local frame from the special-work object's origin, average track-up,
and primary route direction. Intersections are solved in local horizontal 2D
to avoid small elevation noise. Preserve distance/parameter mappings to
reconstruct each result on the original 3D curves.

### 5. Find Every Physical-Rail Intersection

Test all non-identical physical rail pairs whose 2D bounds overlap. Return
every intersection, not only the first. Each result must include:

- both rail IDs
- distance/parameter on both rails
- local 2D and reconstructed world 3D position
- tangent of each rail at the intersection
- acute and signed crossing angles
- distance to each rail endpoint
- source routes and gauge families

Use spatial bounds or a grid index to avoid testing every sampled segment
against every other segment when larger special-work objects are introduced.

### 6. Classify, Do Not Assume

Classify each intersection using physical geometry and route metadata:

| Condition | Classification | Result |
| --- | --- | --- |
| coincident interval | shared rail | merge; no frog |
| matching route endpoint | route join | no frog |
| authored route divergence near blade tip | blade convergence | no frog |
| inner rails of diverging wheel paths meet | vee frog | build frog |
| unrelated physical rails cross | crossing frog | cut both; build crossing assembly |
| angle below supported threshold | invalid shallow crossing | validation error |

The route graph decides whether vehicles can change routes. The physical
intersection classifier decides only how the visible rails must be cut and
assembled.

## B. Frog Orientation

At an intersection, calculate normalized local 2D tangents `tA` and `tB`.

```text
signedSide = cross2D(tA, tB)
acuteAngle = acos(abs(dot(tA, tB)))
```

The signed cross determines handedness/quadrant ordering. Route travel
directions determine which tangent directions point toward and away from the
candidate.

For a vee frog:

- identify the two outgoing inner running rails
- orient both tangent vectors away from the frog
- nose direction is the normalized bisector of those outgoing vectors
- heel direction is the opposite vector
- trim both rails using angle-derived rail-head/flangeway setbacks
- generate wing rails on the approach side

For a diamond/crossing frog:

- order the four approach rays around the intersection
- build opposing nose/wing assemblies for the required quadrants
- cut both crossing rails to leave flange gaps
- preserve both routes as graph-disconnected unless slip routes exist

Approximate initial setbacks may follow the DKW/KRE relationships:

```text
railHeadSetback = railHeadWidth / tan(acuteAngle / 2)
flangeSetback   = flangewayWidth / sin(acuteAngle / 2)
```

All divisions require configured minimum angles, maximum setbacks, and a
validation error when a physically useful frog cannot fit.

The ordinary base `BuildFrogMesh(...)` can render simple vee frogs after this
classification. A crossing frog should be assembled from trimmed rail pieces
and wing rails using `BuildStockRailMesh(...)`.

## C. Guard Rail Placement

Guard rails are generated from a `FrogCandidate` and the wheel path being
protected, not merely from whichever rail happens to be nearby.

For each protected route through a frog:

1. Identify the running rail opposite the frog's hazardous flange gap.
2. Offset a check rail inward from that opposite rail using the configured
   check gauge/flangeway.
3. Start before the frog's wing region and end after its nose/critical region.
4. Taper both ends.
5. Verify clearance against every running, shared, wing, and movable rail.
6. Merge/deduplicate compatible overlapping guard candidates.

A diamond generally requires several guards. A compound dual-gauge turnout
may require different guards for standard and narrow wheel paths even when
they run beside one shared rail.

The base guard mesh pattern and DKW's tapered guard-rail construction are
useful rendering references. Candidate selection must be new.

## D. Switch Blades and Stub Assemblies

Switch blades must be generated independently from frog detection.

The builder already knows where two legal route centerlines begin to diverge.
At that authored or calculated divergence region it creates a
`SwitchBladeCandidate`:

- point-blade type for tapered movable rails against stock rails
- stub-end type for an entire movable route-end assembly
- controlling `SwitchGroupDefinition`
- state-to-pose mapping

For point blades:

- calculate tip and root positions from route divergence and desired blade
  length
- construct stock and closure rail intervals around the blade
- taper the movable rail profile with `BuildStockRailMesh(...)`
- use `SwitchPointRails` for a simple native-node pair or a module-owned group
  controller for compound work

For stub switches:

- do not create tapered point blades
- create movable rail-end assemblies
- bind each end pose to the switch group
- keep the compiled graph as ordinary binary switch nodes where possible

Frog position may influence closure-rail length validation, but it must not be
the event that creates a blade.

## Native Topology Compilation

The graph compiler turns the same `SpecialWorkDefinition` into normal graph
pieces:

- ordinary fixed route: one or more normal native segments
- binary route choice: one ordinary three-leg native switch node
- three-way switch: two or more staged ordinary three-leg switch nodes
- single/double slip: explicit connecting routes plus multiple native switch
  nodes as needed
- physical crossing without slip: separate graph routes with no connection
- dual gauge: separate standard and narrow graph families
- narrow-only branch joining dual gauge: fixed standard route plus one narrow
  three-leg switch
- standard-only branch joining dual gauge: fixed narrow route plus one standard
  three-leg switch
- dual gauge splitting into one standard-only and one narrow-only exit: one
  fixed route per family, usually with no switch node

One `SwitchGroupDefinition` can map a visible state to several native node
states. This generalizes the module's current dual-gauge switch
synchronization without requiring a new graph type.

Generated narrow graph pieces remain automatic. The map maker places one
normal special-work object and selects its layout/gauge mode.

## Gauge-Entry and Gauge-Exit Special Work

Gauge transitions must be first-class presets. A transition is bidirectional:
the same object that lets narrow gauge diverge from a dual-gauge main also
lets a narrow-only yard lead join that dual-gauge main.

### Required Transition Presets

| Preset | Standard-family topology | Narrow-family topology | Typical use |
| --- | --- | --- | --- |
| Narrow branch joins/leaves dual main | Fixed through route | Three-leg binary switch: dual main both directions plus narrow-only branch | Narrow yard or industrial lead enters dual-gauge running track |
| Standard branch joins/leaves dual main | Three-leg binary switch: dual main both directions plus standard-only branch | Fixed through route | Standard-only lead enters dual-gauge running track |
| Dual splits/merges to separate standard and narrow tracks | Fixed route to standard-only port | Fixed route to narrow-only port | Begin or end a shared dual-gauge corridor |
| Dual main continues and both families have branches | Binary standard switch | Binary narrow switch | Compound junction with independent or synchronized controls |

For the narrow-branch preset:

```text
Standard graph:
    dual-main-west ---------------- dual-main-east

Narrow graph:
    dual-main-west --------o------- dual-main-east
                           \
                            narrow-only yard lead
```

The standard graph has no switch at this object. The narrow graph has one
ordinary native three-leg switch. On the dual-gauge main, its narrow route is
the generated ghost family. At the branch port it connects directly to the
real narrow-only authored segment. No ghost counterpart is generated for the
narrow-only yard lead.

The standard-branch preset is exactly symmetrical: the narrow ghost graph
continues fixed through the dual main while a real standard native switch
connects the standard-only branch.

At a dual-to-separate-single-gauge split, both graph families can remain fixed
routes with no switch state at all. The visible rails spread apart and stop
sharing rail intervals, but standard vehicles still follow only the standard
route and narrow vehicles only the narrow route.

### Placement and Authoring

The map maker should place or select one preset and assign the gauge mode of
each external leg:

- `Dual`
- `StandardOnly`
- `NarrowOnly`

The module then generates all internal native nodes, ghost segments, route
centerlines, and visible transition geometry. It must reject incompatible
port assignments rather than silently converting adjacent track to dual
gauge.

The ghost graph generator needs a special-work boundary rule:

1. Generate narrow ghost routes only along dual-gauge ports and corridors.
2. At a narrow-only port, terminate the ghost route and connect it to the real
   narrow segment through the compiled native narrow switch or fixed route.
3. At a standard-only port, expose no narrow connection.
4. Preserve deterministic IDs across rebuild and save/load.

Physical geometry still follows the centerline/intersection pipeline. The
dual side usually has three physical rails; the single-gauge side has two.
Shared rail intervals end or begin inside the special-work object, while the
non-shared rails may cross and create multiple frog/guard candidates.

## Rendering Strategy

Do not call `TrackObjectBuilder.CreateSwitchObjectHelper(...)` once per route.
That would recreate duplicate rails and one-frog assumptions.

Instead:

1. Let ordinary surrounding track render normally.
2. Suppress or slice ordinary rendering inside the special-work object's
   controlled region.
3. Render the final deduplicated `SpecialWorkMeshPlan`:
   - fixed running and shared rail pieces
   - closure and wing rails
   - vee/crossing frog pieces
   - guard rails
   - movable point/stub assemblies
   - special-work ties and stands as later presentation work
4. Bind movable assemblies to native nodes through switch groups.

Use base game rail materials, `BuildStockRailMesh(...)`, and ordinary frog
mesh where it fits. Keep custom geometry procedural and data-driven rather
than prefab-only.

## Validation and Debug View

The debug view is required for the first proof of concept. Suggested colors:

| Item | Color |
| --- | --- |
| standard logical routes | blue |
| narrow logical routes | cyan |
| unmerged physical rails | white/yellow |
| merged shared rail intervals | green |
| intersections | orange |
| frog candidates | red |
| guard candidates | magenta |
| movable blade/stub candidates | purple |
| invalid geometry | bright red with label |

Validation should fail the build or visibly mark the object when:

- an intersection remains unclassified
- a shared interval exceeds position or tangent tolerance
- a frog lacks required rail cuts or protected wheel paths
- a guard collides with running/movable rails
- a blade is not associated with route divergence and a switch group
- a required route loses physical rail continuity after cuts
- any compiled native switch node has more than three legs
- generated native ports do not match surrounding graph ports
- a crossing angle is too shallow for configured dimensions

## Future Cases

### Single and Double Slips

A slip is a crossing plus explicit connecting logical routes. The physical
crossing pipeline remains unchanged. Additional blade candidates and native
binary switch nodes are introduced only for the slip routes. One visible slip
object may synchronize several native nodes.

### Arbitrary-Angle and 90-Degree Crossings

The same rail-centerline intersection pipeline handles both. Ninety degrees
is only a particular crossing angle. Frog setbacks, wing geometry, and guards
derive from the measured angle.

### Three-Way Switches

The visible definition contains three exit routes and multiple blade
assemblies. The topology compiler emits staged ordinary three-leg switch nodes
rather than one unsupported four-leg native node.

### Stub Switches

The route and topology model is unchanged. The movable-assembly generator
emits stub rail ends instead of point blades.

### Compound Dual Gauge

Because routes, physical rails, frogs, guards, and movable assemblies are all
plural collections, gauge splits/merges and separate standard/narrow diverges
do not require a new core data model.

### Gauge-Entry and Gauge-Exit Transitions

Standard-only and narrow-only branches are represented by port family
availability, not by converting the branch into dual gauge. A branch
join/diverge is bidirectional and compiles a native switch only for the family
that actually has the route choice.

## F. Smallest Proof of Concept

### Definition

Create one isolated test content pack containing one visible special-work
object:

- dual-gauge main enters and exits through two dual-gauge ports
- standard gauge continues straight through the dual-gauge main
- narrow normal route continues through the dual-gauge main
- narrow reverse route joins/diverges through one narrow-only yard-lead port
- one real standard graph route
- one real narrow/ghost binary native switch route
- one real narrow-only branch connected to that narrow native switch
- no cross-family graph connection

The object definition, not offset-and-averaged ghost endpoints, supplies the
three route centerlines and their gauge-aware ports. Because track movement is
bidirectional, this one proof proves both narrow-only-to-dual entry and
dual-to-narrow-only divergence.

### Implementation Stages

1. **Debug-only compiler**
   Generate logical routes, physical rail centerlines, shared intervals, all
   intersections, classifications, and candidate labels. Keep current visible
   track temporarily.

2. **Deduplicated visible rails**
   Render physical running rails from the generated centerlines. Shared rail
   intervals must render once. Do not cut crossings yet.

3. **Rail cuts, frogs, and wing rails**
   Build `RailPiece` intervals, trim every classified frog/crossing, and render
   all required assemblies.

4. **Guard rails**
   Generate and render guard candidates for both gauge families.

5. **Narrow moving points**
   Generate the narrow `SwitchBladeCandidate`, render tapered point rails, and
   bind them to the real narrow native switch node.

6. **End-to-end validation**
   Drive narrow equipment through both narrow routes and standard equipment
   through the fixed standard route. Enter the dual-gauge main from the
   narrow-only lead and leave it through that lead. Verify route continuity,
   visible state, save/load, and rebuild behavior.

### Least-Risk First Test

The least-risk first test is stage 1 in a new isolated content pack:

- no replacement graph system
- no changes to coupling
- no mesh suppression initially
- no modification to current working dual-gauge test switches
- one deterministic special-work definition
- debug drawing of every intermediate result

Success means the debug view correctly shows:

- three logical routes
- separate real standard and narrow graph families
- two dual-gauge ports and one narrow-only port
- the ghost narrow route ending cleanly at the real narrow-only branch
- the correct shared physical rail interval
- every physical rail intersection
- multiple correct frog/crossing candidates where required
- guard candidates protecting the correct wheel paths
- exactly one narrow point-blade assembly
- no accidental graph connection at physical crossings

Only after that output matches the reference turnout should the generated mesh
replace the current experimental dual-gauge switch rendering.

## Recommended Module Boundaries

Keep these in NarrowGauge initially:

- special-work definitions and presets
- route/physical-rail compiler
- intersection classifier
- frog/guard/movable candidate generation
- mesh-plan renderer
- native topology compiler
- switch-group controller
- debug view and validator

Use FUSE public APIs for graph application and rebuild. Add to FUSE only when
a narrowly scoped generic extension is actually missing. This keeps the
module least invasive and protects it from routine FUSE updates.

The reusable author-facing preset catalog, topology recipes, count
expectations, and required debug preview are specified in
`docs/special-work-preset-library-design.md`.

## Concrete Extension and Patch Points

### Base Game Primitives to Call or Mirror

- `SwitchGeometry.MakeTrackLineSegments(...)`
- `TrackMeshBuilder.BuildStockRailMesh(...)`
- `TrackMeshBuilder.BuildFrogMesh(...)` for ordinary vee frogs
- `SwitchPointRails.Configure(...)` for simple point assemblies
- curve slicing and `LineSegment.Intersects(...)` concepts

### Base Game Boundaries That May Need Narrow Patches

- track-object descriptor/build interception so one special-work object can
  suppress ordinary rail rendering in its controlled region
- track-object cleanup/rebuild lifecycle for generated mesh objects
- optional switch stand/state binding when one visible object controls several
  native nodes

Avoid patching `SwitchGeometry.Calculate(...)` globally. Ordinary game
turnouts should keep using it.

### NarrowGauge Classes to Replace or Extend

- replace dual-gauge special-work rendering inside
  `NarrowGaugeTrackBuilder.CreateDualGaugeSwitchRailObjects(...)`
- retire `CreateDualGaugeNarrowSplitSwitchRailObjects(...)` after the POC
  proves the general builder
- keep `NarrowGaugeSwitchGeometry` for ordinary narrow-only turnouts
- extend `DualGaugeSwitchSynchronizer` into or alongside a general
  `SpecialWorkSwitchGroupController`
- bypass `GhostGraphSynchronizer` endpoint averaging for authored/generated
  special-work definitions

### FUSE Extension Points

- subscribe to `FuseEvents.TrackGraphApplying`
- apply generated nodes/segments through `TrackAPI`
- rebuild through `TrackAPI.RebuildGraph()`
- add only generic special-work definition/registration hooks if later needed

## Risk Assessment

### Lower Risk

- generating and debug-drawing route/rail centerlines
- all-intersections detection with distances along both rails
- reusing base rail extrusion/materials
- rendering fixed shared and unshared running rails
- compiling one narrow binary switch node
- keeping standard and narrow graph families disconnected

### Medium Risk

- reliable shared-interval merging on curved/elevated track
- cutting and reconnecting rail pieces without visible seams
- candidate classification at endpoints and blade convergence
- ordinary vee frog rendering at nonstandard gauges
- movable point binding and object rebuild cleanup
- suppressing only the ordinary meshes covered by special work

### High Risk

- visually and mechanically correct shallow-angle crossing frogs
- automatic guard selection for compound multi-frog layouts
- arbitrary compound slips and synchronized state truth tables
- save/load identity for generated topology after definitions change
- AI/pathing behavior through several synchronized native switch nodes
- fitting special work onto malformed or too-short user routes

### Save and Routing Risks

Generated graph IDs must be deterministic from special-work object ID, port,
route, and generated-piece role. Changing ID-generation rules can break saves.

Definition versioning must distinguish:

- presentation-only mesh changes, which should not alter graph IDs
- topology changes, which require migration or a clear compatibility warning

Crossing geometry must never add graph connectivity implicitly. Doing so would
break routing and AI behavior.

## Final Recommendation

Build the proof of concept as a NarrowGauge module feature backed by one
general `SpecialWorkDefinition`. Reuse Railroader's native graph, rail mesh
extrusion, materials, and simple switch controls. Reimplement the useful
DKW/KRE intersection, angle, trimming, and crossing-assembly ideas as clean
structured stages.

The key invariant is:

```text
one visible special-work object
!= one native switch
!= one frog
```

That invariant permits the first dual-gauge turnout without blocking slips,
stub switches, arbitrary-angle crossings, three-way switches, or compound
dual-gauge work.
