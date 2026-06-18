# Railroader Narrow- and Dual-Gauge Special Trackwork Investigation

## Executive conclusion

The least disruptive architecture is:

1. Keep Railroader's single native `Track.Graph`.
2. Represent each gauge as a disconnected family of real native `TrackNode` and
   `TrackSegment` objects inside that graph.
3. For dual-gauge track, keep the authored standard-gauge segment and have Fuse
   automatically generate a parallel, narrow-gauge ghost segment with stable
   IDs and an offset narrow-gauge centerline.
4. Suppress the ghost segment's ordinary track mesh.
5. Build one visible dual-gauge mesh from the authored segment plus its linked
   ghost definition.
6. Compile special trackwork into ordinary native three-leg switches wherever
   possible. Do not create graph nodes with more than three connected segments.
7. Reuse the base game's route curves, binary switch topology, switch state,
   route search, and standard turnout geometry. Add a custom geometry layer for
   the third rail, additional frogs, additional guard rails, and gauge
   transitions.

This satisfies the requirement that ghost graphs are never hand-placed. A map
author places ordinary Fuse track and selects a gauge mode. Fuse expands that
definition into the required runtime graph before its single graph rebuild.

The current `ShadowNarrowGaugeGraph` is useful as a geometry prototype and debug
model, but it is not a routable graph. Cars can only occupy native
`Track.Location` values backed by real native `TrackSegment` objects.

## Implemented module foundation

The initial implementation intentionally keeps NarrowGauge outside Fuse:

- `FUSE.NarrowGauge` is a separate UMM module that requires and loads after
  Fuse.
- Fuse exposes one generic `FuseEvents.TrackGraphApplying` event from
  `TrackAPI.RebuildGraph()`.
- The event runs inside a Fuse track batch immediately before the existing
  rebuild.
- NarrowGauge synchronizes generated native ghost nodes and segments through
  public `TrackAPI` methods.
- No Fuse private loader, merged-plan type, or Harmony patch is used.
- Gauge metadata is read through `TrackAPI.GetSegmentDefinition(...)`; installed
  JSON files are no longer scanned.
- `DualGaugeLinkRegistry` is the future cross-family coupling boundary.

This module-first approach is the recommended development path while topology,
geometry, placement, and coupling behavior are still evolving. Once mature,
the compiler can move into Fuse's merge pipeline without changing the authored
data model or generated-ID contract.

## Scope and source basis

The findings below are based on:

- Base game decompile:
  `C:\Hrogers_Railroader_mods_Projects\Decompiled dlls base game`
- DKW reference:
  `C:\Hrogers_Railroader_mods_Projects\Decompiled DLLs Not BASE GAME\C_L_B.DKW`
- Fuse source:
  `C:\Hrogers_Railroader_mods_Projects\Rail\FUSE`
- Current narrow-gauge experiment:
  `C:\Hrogers_Railroader_mods_Projects\Narrow_Gauge\src`

DKW uses StrangeCustoms because that is how the reference mod was built. The
proposed architecture does not use StrangeCustoms. DKW's useful patterns are
translated to Fuse-native expansion and runtime APIs.

## A. How the base game system works

### Native graph model

`Track.TrackSegment` is the fundamental routable edge:

- Source: `Assembly-CSharp\Track\TrackSegment.cs`
- Important members: `id`, `a`, `b`, `priority`, `speedLimit`, `groupId`,
  `style`, `trackClass`, and `turntable`.
- `CreateBezier()` builds the route curve from the endpoint node positions and
  rotations.
- A car location is a distance and direction on a real `TrackSegment`.
- `IsInvisible` only covers turntable-owned segments. There is no general
  ghost/invisible-track flag.

`Track.TrackNode` is the fundamental graph junction:

- Source: `Assembly-CSharp\Track\TrackNode.cs`
- It has one binary `isThrown` state.
- `SegmentCanReachSegment` uses endpoint tangent geometry to determine the
  facing route relationship.
- One node cannot natively express a multi-position slip switch.

`Track.Graph` builds and owns the runtime topology:

- Source: `Assembly-CSharp\Track\Graph.cs`
- `RebuildCollections()` discovers child nodes and segments, rebuilds ID
  dictionaries and endpoint connection caches, and broadcasts
  `GraphDidRebuildCollections`.
- `AddNode(...)` and `AddSegment(...)` create or register native graph objects.
- `SegmentsConnectedTo(...)` returns every segment attached to a node.
- `SegmentsReachableFrom(...)` applies switch state when movement reaches a
  switch from its facing side.
- `LocationFrom(...)` selects the next segment using `TrackNode.isThrown`.
- `DecodeSwitchAt(...)` recognizes a switch only when exactly three segments
  connect to the node. It infers the entering, normal, and diverging segments
  from tangent continuity, priority, and divergence angle.

This creates a hard topology rule:

> Native routing, AI, switch state, and switch rendering assume binary,
> three-leg switches.

A node with four or more segments may exist in the connection dictionary, but
it will not behave as a native switch. It should not be used for a slip switch
or a combined dual-gauge junction.

### Route search and movement

`Track.Search.Searcher` performs route search:

- Source: `Assembly-CSharp\Track\Search\Searcher.cs`
- `GetNeighbors(...)` calls `Graph.DecodeSwitchAt(...)`.
- Search costs and available routes assume enter/normal/diverging switch
  semantics.

`Model.AI.AutoEngineerPlanner` also calls `DecodeSwitchAt(...)` when planning
switch settings.

`TrainController` owns one `Graph` reference and uses it for placement,
movement, route checks, physics-set construction, coupling, and save restore:

- Source: `Assembly-CSharp\TrainController.cs`
- `UpdateSets(...)` finds adjacent cars by moving along native graph routes.
- `IntegrationSetDidCouple(...)` rejects coupling unless the cars pass
  `Graph.CheckSameRoute(...)`.
- `GraphDidRebuildCollections()` treats cars on missing or disabled segments as
  lost.
- `PopulateSnapshotForSave(...)` stores car locations and thrown switch node
  IDs.
- `HandleSnapshotSwitches(...)` restores switch state by native node ID.

`Model.Physics.IntegrationSet` moves all cars in a physics set along the graph
passed to that set:

- Source: `Assembly-CSharp\Model\Physics\IntegrationSet.cs`
- `PositionCars(...)` advances native locations.
- Coupling constraints assume cars have a consistent ordered route.

The game does not have a rolling-stock track-gauge concept. Any equipment gauge
classification must be supplied by a custom component or definition metadata.

### Save and load

The relevant save format is intentionally small:

- `Graph.CreateSnapshotTrackLocation(...)` stores segment ID, distance, and
  facing end.
- `Graph.MakeLocation(...)` resolves the saved segment ID back to a runtime
  segment.
- Switch state is saved as a set of thrown node IDs.

The save does not serialize custom topology. The map and installed mods must
recreate every referenced segment before car snapshot restoration.

Consequences for ghost track:

- Generated ghost segment and switch-node IDs must be deterministic.
- The generation algorithm is part of the save compatibility contract.
- Ghost segments must exist before cars are restored.
- Changing or removing generated IDs can orphan narrow-gauge cars.

### Base turnout geometry

`Track.SwitchGeometry.Calculate(...)` builds the base turnout geometry:

- Source: `Assembly-CSharp\Track\SwitchGeometry.cs`
- It aligns the two route proxies at the switch node.
- It currently hardcodes `Gauge.Standard`.
- `MakeTrackLineSegments(center, gauge)` converts a route centerline into left
  and right rail centerlines and is reusable with other gauges.
- It finds an opposing-rail intersection and treats that as the frog.
- It splits route proxies around the frog.
- It constructs stock rails, closure rails, point rails, frog points, guard
  rails, and switch-stand location.

The current blade calculation is frog-relative: point and closure rails are
split based on a distance before the detected frog. That works for a normal
turnout, but it is not a reliable rule for compound dual-gauge trackwork.

For custom special trackwork:

- Frog and crossing assemblies should be intersection-derived.
- Switch blades should be route-split-derived, starting at the switch point and
  ending after a configured blade length or divergence threshold.

### Base track mesh generation

`Track.TrackMeshBuilder` procedurally extrudes the rail profiles:

- Source: `Assembly-CSharp\Track\TrackMeshBuilder.cs`
- `BuildStockRailMesh(...)` extrudes rail along a `LineCurve`.
- `BuildFrogMesh(...)` constructs a frog and scales the profile around the
  crossing angle.
- `BuildColliderMesh(...)` creates the track collider.

Several useful methods are private, so a companion mod must either use
reflection, request a public Fuse/base-game wrapper, or maintain a carefully
tested copy.

### Descriptor and visible-object build

`Track.TrackObjectManager` translates graph topology into render descriptors:

- Source: `Assembly-CSharp\Track\TrackObjectManager.cs`
- `Rebuild()` calls `Graph.RebuildCollections()`, calls private
  `BuildDescriptors(...)`, then gives the descriptors to the track rebuilder.
- `BuildDescriptors(...)` begins with segment proxies.
- For every node accepted by `DecodeSwitchAt(...)`, it calls
  `SwitchGeometry.Calculate(...)`, replaces the affected segment proxies with
  remainders, and creates a switch descriptor.
- Remaining proxies become ordinary segment descriptors.

`Track.TrackObjectBuilder` turns descriptors into procedural objects:

- Source: `Assembly-CSharp\Track\TrackObjectBuilder.cs`
- `CreateTrackObject(...)` always builds standard-gauge two-rail track.
- `CreateSwitchObject(...)` and `CreateSwitchObjectHelper(...)` build the frog,
  stock rails, closure rails, guards, animated point rails, ties, stand,
  collider, and masks.
- `CreateSwitchMasks(...)` and roadbed generation are separate from rail mesh
  creation.

The practical extension seam is the descriptor/object-build stage. The current
NarrowGaugeMod already patches `TrackObjectManager.BuildGameObject` and reuses
the base builder through reflection. This is directionally correct.

## B. How C_L_B.DKW implements slips and crossings

### DKW topology

DKW does not patch the base graph into supporting a native multi-state switch.
It decomposes a double slip into ordinary binary switches.

`DKWSceneryPlugin.HandleGraphWillChange(...)`:

- Source: `C_L_B.DKW\DKW\DKWSceneryPlugin.cs`
- Receives a DKW spliney definition.
- Generates eight inner/outer nodes.
- Generates four approach segments, two crossing/straight routes, and two
  diagonal routes.
- Each inner node has exactly three connected segments and is therefore a valid
  native base-game switch.

`NodeSynchronizer`:

- Source: `C_L_B.DKW\DKW\NodeSynchronizer.cs`
- Synchronizes selected pairs of ordinary `TrackNode.isThrown` values.
- Produces coordinated physical behavior without creating a new graph switch
  type.

This is DKW's most important reusable routing pattern:

> Compile complex special work into a network of ordinary native three-leg
> switches, then synchronize switch states where one physical control should
> operate multiple native nodes.

In the Fuse architecture, the equivalent topology should be generated by a
Fuse track-expansion phase, not a StrangeCustoms graph event.

### DKW visible geometry

`DKWSpliney` builds the visible double-slip trackwork procedurally:

- Source: `C_L_B.DKW\DKW\DKWSpliney.cs`
- Builds Bezier route curves.
- Calls base `SwitchGeometry.MakeTrackLineSegments(...)` to obtain rail
  centerlines.
- Splits and joins `LineCurve` objects to form stock rails, point rails,
  closures, crossing hearts, and guards.
- Uses `DKW_Util.BuildStockRailMesh(...)` to generate meshes.
- Builds ties, masks, switch stands, and point-rail animation.
- Does not call `SwitchGeometry.Calculate(...)` for the complete DKW.
- Does not use a prefabbed DKW rail mesh.

`DKW_Util` contains copied or adapted procedural mesh logic:

- Source: `C_L_B.DKW\DKW\DKW_Util.cs`
- Supplies rail extrusion, guards, ties, masks, and switch-stand helpers.
- This exists largely because useful base mesh methods are private.

### DKW descriptor interception

DKW patches private `TrackObjectManager.BuildDescriptors(...)`:

- It removes the DKW's internal nodes from the nodes passed to the base
  descriptor builder.
- It removes or replaces segment proxies so the base builder does not render
  ordinary rails through the custom work.
- It uses a transpiler to substitute its adjusted proxy set.

It also patches `TrackObjectBuilder.CreateSwitchObject(...)` to capture/use the
builder for custom stands and masks.

The custom `DKWDescriptor` and `KREDescriptor` return empty objects. They act
more like lifecycle queues than fully integrated replacement descriptors.

This works, but the private-method transpiler is brittle. A dedicated Fuse or
NarrowGaugeMod render-suppression API is preferable.

### KRE crossing implementation

`KRESpliney` implements the crossing/diamond case:

- Source: `C_L_B.DKW\DKW\KRESpliney.cs`
- References two existing native graph segments.
- Detects their route-centerline intersection.
- Generates left/right rail centerlines with
  `SwitchGeometry.MakeTrackLineSegments(...)`.
- Detects all rail-to-rail intersections.
- Cuts the visible ordinary rails around the crossing.
- Procedurally generates the crossing pieces, frogs, ties, and masks.
- Leaves the two native routes graph-disconnected.

That is the correct graph behavior for an ordinary 90-degree crossing: trains
continue on their own route and cannot turn onto the crossing route.

There is no DKW angle restriction in the KRE code comparable to the DKW slip
angle restriction. A 90-degree crossing is feasible in principle, although it
still needs an in-game test for tolerances, cuts, colliders, and wheel visuals.

DKW's `DKW_Util.Intersects(BezierCurve, BezierCurve, ...)` appears to sample
`curveA` for both curves in one part of the decompile. Whether that is a source
bug or decompiler artifact, it should not be reused verbatim. A new robust
local-2D intersection implementation is recommended.

### What DKW does and does not provide

DKW provides useful examples of:

- Decomposing a complex switch into native binary switches.
- Synchronizing multiple native switch nodes.
- Procedural special-work geometry.
- Cutting normal render proxies while preserving native routing.
- Keeping a non-routable crossing graph-disconnected.

DKW does not provide:

- A generalized multi-state graph patch.
- A ghost-graph system.
- Gauge-aware rolling stock.
- A save format for custom topology.
- A robust public descriptor extension API.
- Fuse-native authoring or placement.

## Fuse-native integration

Fuse is the authoritative authoring and graph-apply layer for this project.

Confirmed relevant Fuse behavior:

- `FuseSegment` already stores `Gauge`.
  Source: `FUSE\Authoring\Data\Tracks\FuseTrackDefinition.cs`
- `FuseModLoader.ApplyDefinitionsToRuntimeStaged(...)` builds one final merged
  track plan.
  Source: `FUSE\Loading\FuseModLoader.TrackMerging.cs`
- `BuildMergedTrackPlan(...)` resolves the final cross-package nodes and
  segments.
- `ApplyMergedTrackGraph(...)` applies nodes and segments, then requests a
  single graph rebuild before final span binding.
- `TrackAPI.AddNode(...)`, `AddSegment(...)`, `UpdateNode(...)`, and
  `UpdateSegment(...)` create and maintain native graph objects.
- `TrackAPI.GetSegmentDefinition(...)` preserves Fuse metadata such as gauge in
  the runtime definition cache.
- `FuseDefinitionValidator.ValidateTrack(...)` is the existing authoring
  validation layer.
- `FuseTrackDebugOverlay` and Fuse console/audit infrastructure are suitable
  homes for gauge-family diagnostics.

### Future fully integrated Fuse extension

After the module design is mature, add a Fuse-native final-track expansion
stage:

```text
Load and validate authored Fuse definitions
    -> BuildMergedTrackPlan
    -> ExpandGeneratedTrackwork
    -> ValidateExpandedTrackPlan
    -> ApplyMergedTrackGraph
    -> Single native graph rebuild
```

`ExpandGeneratedTrackwork` should:

- Read final authored `FuseSegment.Gauge` values.
- Hydrate partial segment patches against the base-graph snapshot before trying
  to generate topology. A gauge-only partial patch may not carry endpoint IDs.
- Generate ghost nodes and segments for dual-gauge definitions.
- Generate explicitly defined special-trackwork topology.
- Assign deterministic package/source-derived IDs.
- Preserve source ownership and claims.
- Add generated definitions to the same final plan before runtime apply.
- Emit a source-to-generated link registry for NarrowGaugeMod.
- Ensure generated claims participate in Fuse unload, restore, and reapply.

The merged plan types are currently private inside `FuseModLoader`. Because
Fuse is owned by this project, the clean change is to add a deliberate public
or internal extension surface rather than Harmony-patching this private method.

A useful API shape would be:

```csharp
public interface IFuseTrackPlanExpander
{
    string Id { get; }
    int Priority { get; }
    void Expand(FuseTrackExpansionContext context);
}
```

`FuseTrackExpansionContext` should expose final read-only authored definitions
plus methods such as:

- `AddGeneratedNode(ownerPackageId, sourceId, generatedId, FuseNode node)`
- `AddGeneratedSegment(ownerPackageId, sourceId, generatedId, FuseSegment segment)`
- `LinkSegments(sourceSegmentId, counterpartSegmentId, linkMetadata)`
- `AddSwitchGroup(groupDefinition)`
- `ReportError(...)` and `ReportWarning(...)`

NarrowGaugeMod registers an expander during mod load. Fuse invokes it before
`ApplyMergedTrackGraph(...)`.

This is preferable to using `FuseEvents.GraphRebuilt`, because a post-rebuild
listener would create topology too late, require a second rebuild, and risk
running after save-car restoration begins.

### NarrowGaugeMod integration changes

The current mod scans installed JSON files to recover gauge metadata. With Fuse
as the required data layer, NarrowGaugeMod should instead:

- Reference Fuse's public API.
- Read `TrackAPI.GetSegmentDefinition(segment.id).Gauge`.
- Consume Fuse's generated source/counterpart link registry.
- Subscribe to Fuse graph lifecycle events only for cache refresh and debug UI.
- Remove legacy StrangeCustoms scanning and stale StrangeCustoms log messages.

## C. Proposed ghost-graph architecture

### One native graph, two disconnected families

Use one base `Track.Graph`, because the game, train controller, AI, physics, and
save system already expect one graph.

Inside it, maintain two disconnected graph families:

- `Standard` family
- `Narrow` family

The family is metadata, not a second `Graph` instance. A car remains on the
family of its native `TrackSegment` for its entire movement. There is no runtime
graph swapping.

### Segment modes

| Authored gauge mode | Native standard family | Native narrow family | Visible mesh |
|---|---|---|---|
| Standard | Authored segment | None | Standard two rail |
| Narrow | None, unless the authored segment itself is designated narrow | Authored narrow segment | Narrow two rail |
| DualGauge | Authored standard segment | Generated ghost counterpart | One dual-gauge mesh |

For dual gauge, the narrow centerline is not identical to the standard
centerline when three rails share one side. Its center offset is half the
difference between the standard and narrow inside gauges, with sign determined
by shared-rail side.

A native `TrackSegment` is one cubic Bezier derived from two endpoint nodes.
A mathematically exact constant offset of a cubic Bezier is generally not
another cubic Bezier. This creates three implementation choices:

- V1: offset the endpoint positions, preserve appropriate endpoint tangents,
  and accept the small centerline error on broad curves.
- Build the visible rails to match the generated native centerlines closely
  enough that car/wheel placement remains believable.
- Later, split tight dual-gauge curves into deterministic generated
  subsegments when the offset error exceeds a validation tolerance.

Automatic subsegmentation improves accuracy but increases ID count and save
compatibility risk, so it should not be part of the first proof of concept.

At a source node shared by several dual-gauge segments, the expander may reuse
one generated narrow node only when the offset endpoints and tangents are
compatible. Opposite shared-rail sides, discontinuous offsets, or compound
junctions require explicit transition/special-work definitions rather than
blindly merging every generated endpoint.

### Generated IDs

IDs must be stable and namespaced. Example:

```text
source segment: package:s:dual-main-01
ghost segment:  package:s:dual-main-01~ng
source node A:  package:n:dual-main-a
ghost node A:   package:n:dual-main-a~ng
```

The exact suffix is less important than making it:

- Deterministic.
- Collision checked.
- Immutable after release.
- Reproducible from the authored source definition.

### Required metadata

Use an immutable runtime link record rather than trying to store all behavior
on `TrackSegment`:

```text
GaugeSegmentLink
  sourceDefinitionId
  runtimeSegmentId
  family: Standard | Narrow
  mode: Standard | Narrow | DualGauge
  counterpartSegmentId
  isGeneratedGhost
  sharedRailSide
  specialTrackworkId
  routeRole
```

An equipment component should independently declare:

```text
EquipmentGauge
  family: Standard | Narrow
```

### Visibility and picking

Generated ghost segments must remain routable but must not receive ordinary
visible track objects.

Preferred implementation:

- Add a NarrowGaugeMod-owned render registry that identifies generated ghost
  segments.
- Suppress their normal segment descriptors or return an empty render object.
- Build the visible dual-gauge object from the authored source segment.

Descriptor filtering is cleaner because it avoids unnecessary ties, roadbed,
colliders, and masks. Patching private `BuildDescriptors(...)` is brittle,
however. A small public extension in Fuse or a narrowly guarded
`TrackObjectManager` patch is preferable to DKW's broad transpiler.

Track picking and placement also need family filtering. Without it, a click
near dual-gauge track can select either parallel native centerline. Placement
must select the family matching the equipment gauge.

## D. Proposed custom dual-gauge turnout geometry

### Separate logical topology from physical rails

Define special work in two layers:

1. Logical routes per graph family.
2. Visible rail assemblies shared by those routes.

The logical layer compiles to native nodes and segments. The geometry layer
builds all physical rails, blades, frogs, guards, ties, masks, and colliders.

### Reuse from the base game

Reuse these base systems wherever the topology remains an ordinary turnout:

- `TrackSegment.CreateBezier()` route splines.
- `Graph.DecodeSwitchAt(...)` binary switch topology.
- `TrackNode.isThrown` and switch save state.
- Base route search and AI.
- `SwitchGeometry.MakeTrackLineSegments(...)`.
- Base standard turnout geometry as an initial visible layer.
- Base switch stand and point animation patterns.

For narrow-only ordinary turnouts, a gauge-parameterized copy/wrapper of
`SwitchGeometry.Calculate(...)` is appropriate. The current
`NarrowGaugeSwitchGeometry.Calculate(...)` already demonstrates this.

### Custom rail-centerline pipeline

For dual-gauge and compound special work:

1. Build every logical route centerline.
2. Build every physical rail centerline for that route and gauge.
3. Deduplicate shared rails.
4. Project the trackwork to a local 2D plane.
5. Detect rail-segment intersections.
6. Reject same-rail, near-parallel, endpoint, and tolerance-noise hits.
7. Cluster near-identical intersections.
8. Classify each cluster as frog, diamond crossing, or invalid conflict.
9. Cut rail centerlines around accepted candidates.
10. Generate frog/wing/guard assemblies.
11. Generate point blades independently from route divergence.

DKW/KRE validates the overall rail-centerline-first concept. A new robust 2D
implementation is needed for multiple crossings and deterministic output.

### Frog and guard rules

Frog candidates should come from crossings between physical rail centerlines,
not from route centerline crossings alone.

Each candidate should retain:

- Both rail IDs and route IDs.
- Both graph families.
- Intersection position.
- Crossing angle.
- Distance along each rail.
- Whether either rail is shared.

Guard rails can then be generated from the accepted frog candidate and the
opposing stock rail, using configured flange clearances and lengths. Automatic
generation should permit explicit per-candidate overrides for unusual work.

### Switch blade rules

Point blades should not be inferred from frog intersections.

For each switch route split:

- Start at the authored/native switch point.
- Identify the stock rail and moving route rail.
- Follow the moving rail until a configured blade length or lateral divergence
  threshold is reached.
- Split into point and closure rail there.
- Bind movement to the corresponding native switch node or switch group.

This supports cases where a route splits but does not cross another rail until
far later, or never crosses one at all.

### Basic dual-gauge turnout cases

| Physical case | Standard-family topology | Narrow-family topology | Control |
|---|---|---|---|
| Standard straight, narrow diverges | Continuous route, no switch | Binary switch | Narrow switch node |
| Standard diverges, narrow continues | Binary switch | Continuous route | Standard switch node |
| Both gauges diverge | Binary switch | Binary switch | Independent or synchronized group |
| Dual gauge splits to standard-only and narrow-only | Standard route exits to standard-only | Narrow ghost route exits to narrow-only | Usually no shared route after split |
| Narrow-only branch joins/leaves dual main | Continuous route, no switch | Dual-main ghost route connects to real narrow branch through binary switch | Narrow switch node |
| Standard-only branch joins/leaves dual main | Dual-main standard route connects to real standard branch through binary switch | Continuous ghost route, no switch | Standard switch node |

Do not infer these cases only from a three-leg native node. The authored Fuse
special-trackwork definition should state route family, port gauge mode,
shared-rail side, and switch grouping.

These branch transitions are bidirectional. The narrow-only branch case lets a
narrow yard lead enter a dual-gauge main without converting the yard lead or
other yard tracks to dual gauge. The generated narrow ghost route exists only
on the dual-gauge ports and connects to the real narrow-only segment at the
special-work boundary. The standard-only branch case is symmetrical.

### Slips and crossings

For a slip:

- Compile each family route into a set of native degree-three switches.
- Add synchronized switch groups where multiple native nodes represent one
  physical control.
- Build one custom visible special-work object.

For a plain crossing, including 90 degrees:

- Keep the native routes graph-disconnected.
- Cut only the visible rails/colliders around the crossing.
- Build crossing frogs and guards from physical rail intersections.

## E. Specific classes and extension points

### Preferred Fuse changes

- `FUSE.Authoring.Data.FuseSegment`
  - Keep `Gauge`.
  - Add or associate `sharedRailSide`, family metadata, and special-work
    references.
- `FUSE.Authoring.Data.FuseTrackDefinition`
  - Add explicit special-trackwork and switch-group definitions.
- `FuseModLoader.BuildMergedTrackPlan(...)`
  - Preserve final source definitions as it already does.
- New `FuseTrackExpansionAPI` / `IFuseTrackPlanExpander`
  - Expand ghost topology and special work before apply.
- `FuseModLoader.ApplyMergedTrackGraph(...)`
  - Apply expanded definitions in the existing single-rebuild transaction.
- `FuseDefinitionValidator.ValidateTrack(...)`
  - Validate authored gauge and special-work definitions.
- New expanded-plan validator
  - Validate generated IDs, graph families, node degree, and counterpart links.
- `TrackAPI.AddNode(...)`, `AddSegment(...)`, `UpdateNode(...)`,
  `UpdateSegment(...)`, and `GetSegmentDefinition(...)`
  - Continue to materialize native graph objects and expose runtime metadata.
- `FuseTrackDebugOverlay` and Fuse console commands
  - Display family, gauge mode, counterpart, ghost state, rail centerlines, and
    validation findings.

### NarrowGaugeMod changes

- `NarrowGaugeManager`
  - Replace installed-file scanning with Fuse runtime-definition lookup.
  - Maintain the source/counterpart runtime registry.
- `ShadowNarrowGaugeGraph`
  - Retain only as a geometry/debug model or replace it with generated native
    ghost topology.
- `Patch_Graph_RebuildCollections`
  - Refresh caches and validate; do not generate topology here.
- `Patch_TrackObjectManager_BuildGameObject`
  - Build visible narrow/dual/special-work objects.
- New descriptor/proxy suppression extension
  - Prevent ordinary ghost and special-work rails from rendering.
- `NarrowGaugeSwitchGeometry`
  - Continue supporting ordinary narrow-only turnouts.
- New `SpecialTrackworkGeometryBuilder`
  - Rail-centerline intersection, frog, guard, blade, and crossing generation.
- New `GaugeAwarePlacement` patches
  - Filter placement and track picking by equipment family.

### Base-game methods likely patched or wrapped

Rendering:

- `TrackObjectManager.BuildDescriptors(...)`
- `TrackObjectManager.BuildGameObject(...)`
- `TrackObjectManager.BuildMaskObject(...)`
- `TrackObjectBuilder.CreateTrackObject(...)`
- `TrackObjectBuilder.CreateSwitchObject(...)`
- `TrackObjectBuilder.CreateSwitchMasks(...)`
- `SwitchGeometry.Calculate(...)`
- `SwitchGeometry.MakeTrackLineSegments(...)`
- `TrackMeshBuilder.BuildStockRailMesh(...)`
- `TrackMeshBuilder.BuildFrogMesh(...)`
- `TrackMeshBuilder.BuildColliderMesh(...)`

Placement and family enforcement:

- `Graph.TryGetLocationFromGamePoint(...)`
- `Graph.TryGetLocationFromWorldPoint(...)`
- `ObjectPicker` track-location selection
- `ConsistPlacer` / `TrainController` placement validation

V2 cross-family coupling only:

- `TrainController.UpdateSets(...)`
- `TrainController.IntegrationSetDidCouple(...)`
- The controlled car-neighbor lookup path
- `IntegrationSet.PositionCars(...)` or a pre-movement validator

Core methods that should preferably remain unpatched:

- `Graph.DecodeSwitchAt(...)`
- `Graph.SegmentsReachableFrom(...)`
- `Track.Search.Searcher.GetNeighbors(...)`
- `AutoEngineerPlanner` switch planning

They can remain native if every generated route is compiled into valid
degree-three binary switches.

## F. Risk list

### Lower risk

- Narrow and dual-gauge procedural segment meshes.
- Gauge-parameterized ordinary turnout geometry.
- Rail-centerline debug rendering.
- Plain graph-disconnected crossing visuals.
- Automatic frog-candidate detection with conservative validation.
- Fuse validation and audit tooling.

### Medium risk

- Deterministic ghost node/segment expansion.
- Suppressing ghost meshes without disturbing roadbed/masks elsewhere.
- Gauge-aware placement and picking on two close parallel centerlines.
- Basic explicitly authored dual-gauge turnout types.
- Coordinated switch groups compiled from native nodes.
- Runtime reload/reapply while no cars occupy affected generated track.

### High risk

- Arbitrary special-work inference from only nearby track geometry.
- Native nodes with more than three connected segments.
- Changing generated IDs after saves exist.
- Rebuilding/removing generated track while cars occupy it.
- AI/CTC behavior across synchronized multi-node special work.
- Multiplayer synchronization of generated topology and grouped switch state.
- Mixed-gauge coupling and physics sets.
- Broad transpilers against private `TrackObjectManager.BuildDescriptors(...)`.

### Save-break risks

- Changing the ghost ID algorithm.
- Generating ghost track after car snapshot restore.
- Removing a ghost segment referenced by a saved narrow car.
- Duplicate generated IDs across packages.
- Changing synchronized switch node IDs or grouping semantics.

### Routing-break risks

- Any generated node degree above three.
- Accidentally sharing a native node between standard and narrow families.
- Incorrect endpoint rotations that cause `DecodeSwitchAt(...)` to infer the
  wrong enter/normal/diverging relationship.
- A generated switch group whose members disagree about route mapping.

### Geometry-break risks

- Intersection tolerances that create duplicate or missing frogs.
- Treating a shared rail as two crossing rails.
- Generating blades from frog locations instead of route splits.
- Cutting base segment proxies without matching mask/collider cuts.
- Assuming KRE's decompiled intersection helper is robust.

## G. Implementation plans

### V1

1. Make Fuse the only gauge metadata source.
2. Add the Fuse final-track expansion extension.
3. Generate real native narrow ghost nodes and segments for dual-gauge track.
4. Add stable source/counterpart link metadata.
5. Suppress ghost visible descriptors.
6. Keep coupling same-gauge only.
7. Add explicit basic dual-gauge turnout definitions.
8. Compile each family into native degree-three switches.
9. Reuse base switch topology and ordinary turnout geometry where applicable.
10. Add rail-centerline, frog-candidate, and ghost-graph debug views.
11. Add a generated-graph validator.

V1 validator checks:

- Every generated ID is deterministic and unique.
- Every dual-gauge source has exactly one narrow counterpart.
- Every counterpart link is reciprocal.
- Standard and narrow families never share a native node.
- Every switch node has exactly three segments.
- Endpoint positions/rotations are continuous within tolerance.
- Narrow center offsets match shared-rail-side metadata.
- Native ghost-curve offset error remains below the configured V1 tolerance.
- Generated segments exist before save-car restore.
- Ghost descriptors are suppressed.
- Basic route reachability succeeds in each family.

### V2: `DualGaugeCoupler`

Attach `DualGaugeCoupler` to locomotive/car coupler slots.

Cross-family coupling detection should:

- Search only equipment on the linked counterpart of the current dual-gauge
  segment.
- Map position by a deterministic source-to-counterpart arc-length mapping.
- Require compatible coupler direction, distance, and tangent alignment.
- Bridge graph families only for coupling detection.
- Never change either car's graph family.
- Never allow coupling merely because two unrelated tracks cross spatially.

Required components:

- `DualGaugeCoupler`
- `EquipmentGauge`
- `LinkedTrackPositionMapper`
- `MixedGaugeConsistValidator`

Likely patches:

- Extend `TrainController.UpdateSets(...)` adjacency discovery for approved
  counterpart candidates.
- Allow `IntegrationSetDidCouple(...)` to bypass `CheckSameRoute(...)` only for
  a validated dual-gauge counterpart pair.
- Before movement, project the requested consist movement along both family
  routes.
- Block movement if either family lacks a valid forward path or has an
  incompatible switch state.

This is high risk because `IntegrationSet` assumes an ordered common route.
Parallel linked segments may satisfy that assumption if the position mapping is
monotonic, but every transition, switch, reversal, and split must be validated.
Do not globally weaken `Graph.CheckSameRoute(...)`.

### Least-risk proof of concept

The first test should be one straight Fuse-authored dual-gauge segment:

1. Author one segment with `gauge: "DualGauge"` and an explicit shared-rail
   side.
2. Have Fuse generate one real narrow ghost segment and two real ghost nodes
   before `ApplyMergedTrackGraph(...)`.
3. Give every generated object deterministic IDs.
4. Suppress the ghost segment's ordinary mesh.
5. Build one visible three-rail mesh from the source segment.
6. Place one standard car on the source segment.
7. Place one narrow car on the ghost segment.
8. Move both independently.
9. Save and reload.
10. Verify each car restores to the same segment family.

This test proves the hardest foundational assumption without involving turnout
geometry, AI, slip states, or mixed-gauge coupling.

After that succeeds, the next test should be a dual-gauge-to-narrow-only
transition, followed by one basic dual-gauge turnout with only the narrow family
diverging.

## Current project assessment

The current NarrowGaugeMod already has several useful foundations:

- A three-foot `Gauge`.
- A gauge-parameterized ordinary turnout calculation.
- Custom narrow and dual-gauge segment rendering.
- A custom dual-gauge switch renderer that reuses base geometry.
- A shadow-centerline model useful for transition geometry and debugging.

The main architectural changes needed are:

- Replace metadata-only shadow routing with Fuse-generated real native ghost
  segments.
- Stop scanning installed Fuse JSON and consume Fuse runtime definitions/API.
- Generate topology before Fuse's single graph rebuild, not in
  `Graph.RebuildCollections` postfix.
- Add a supported render-suppression path for ghost/special-work segments.
- Make special-work definitions explicit enough to compile each graph family
  into native binary switches.

## Final recommendation

Do not replace the base turnout builder wholesale.

Use it for ordinary route splines, native switch topology, switch state,
routing, and standard or narrow ordinary-turnout geometry. Add a custom
special-trackwork layer for the physical rails that the base builder cannot
represent: third rail, extra frogs, extra guards, dual-gauge crossings, gauge
transitions, slips, and diamonds.

Use DKW as evidence that complex work can be decomposed into native switches and
rendered procedurally, but implement the production system through Fuse's merged
track plan, validation, runtime APIs, and single graph rebuild.
