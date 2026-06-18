# SpecialWorkDefinition Preset Library

## Goal

The preset library lets a map author select a named special-work layout,
connect its external ports, and adjust a small set of meaningful dimensions.
The author does not manually define:

- logical routes
- physical rails
- shared rail intervals
- frogs
- guard rails
- switch blades or stub assemblies
- internal native graph nodes and segments
- switch synchronization

A preset expands into one complete `SpecialWorkDefinition`. The existing
special-work compilers then independently produce its native topology and
visible geometry.

## Authoring Model

A map author creates a lightweight instance:

```json
{
  "id": "yard-narrow-entry",
  "preset": "dual.narrow-branch-joins-main.right",
  "position": [0, 0, 0],
  "rotation": [0, 0, 0],
  "parameters": {
    "frogNumber": 8,
    "sharedRailSide": "Right",
    "controlMode": "Independent"
  },
  "ports": {
    "common": "segment-dual-west",
    "through": "segment-dual-east",
    "branch": "segment-narrow-yard"
  }
}
```

The preset library validates the port gauge modes, generates route
centerlines, compiles native graph topology, and passes the result to the
geometry builder.

The author can preview and validate the expanded preset before applying it to
the live graph.

## Canonical Recipes and Aliases

The library should expose friendly names without implementing every name as
unrelated code:

- left and right turnouts are mirrored instances of one turnout recipe
- standard and narrow turnouts use the same recipe with a different family
- narrow-diverges and narrow-branch-joins-dual-main are the same bidirectional
  topology with different author-facing names
- standard-diverges and standard-branch-joins-dual-main are the same
  bidirectional topology
- diamond, arbitrary-angle, and 90-degree crossings share one crossing recipe
  with different angle policies
- point and stub turnouts share graph recipes but use different movable
  assembly recipes
- standard, narrow, and dual three-way switches share a staged-binary topology
  recipe, repeated once per active gauge family

This reduces implementation risk and ensures mirrored or gauge variants
receive the same bug fixes.

## Preset Definition Schema

```csharp
internal sealed class SpecialWorkPresetDefinition
{
    public string Id;
    public string DisplayName;
    public SpecialWorkCategory Category;
    public IReadOnlyList<PresetParameterDefinition> Parameters;
    public IReadOnlyList<PortTemplate> Ports;
    public IReadOnlyList<RouteTemplate> Routes;
    public NativeTopologyRecipe Topology;
    public SwitchGroupRecipe SwitchGroups;
    public MovableAssemblyRecipe MovableAssemblies;
    public GeometryExpectation GeometryExpectation;
}
```

### Port Templates

```csharp
internal sealed class PortTemplate
{
    public string Id;
    public GaugeAvailability RequiredFamilies;
    public PortRole Role;
    public PortPlacementRule Placement;
}
```

Common roles:

- `Common`
- `Through`
- `BranchLeft`
- `BranchRight`
- `WyeLeft`
- `WyeRight`
- `CrossingA0`, `CrossingA1`
- `CrossingB0`, `CrossingB1`
- `StandardOnly`
- `NarrowOnly`

Dual ports expose independent standard and narrow connections. Single-family
ports expose only their declared family.

### Route Templates

A route template generates a logical wheel-path centerline:

```csharp
internal sealed class RouteTemplate
{
    public string Id;
    public GaugeFamily Family;
    public string FromPortId;
    public string ToPortId;
    public RouteCurveRecipe Curve;
    public string RequiredSwitchGroupId;
    public string RequiredStateId;
}
```

Useful curve recipes:

- `Straight`
- `TurnoutDivergence`
- `WyeDivergence`
- `CrossingStraight`
- `SlipConnection`
- `GaugeSplitTransition`
- `ThreeWayFirstDivergence`
- `ThreeWaySecondDivergence`

### Native Topology Recipes

```csharp
internal enum NativeTopologyRecipe
{
    FixedRoutes,
    BinarySwitch,
    TwoFamilyBinarySwitches,
    StagedThreeWay,
    TwoFamilyStagedThreeWay,
    PlainCrossing,
    SingleSlip,
    DoubleSlip
}
```

Every generated native switch remains an ordinary three-leg binary
Railroader switch.

### Geometry Expectations

Presets do not author frogs or guards. They provide validation expectations:

```csharp
internal sealed class GeometryExpectation
{
    public CountRange ExpectedFrogs;
    public CountRange ExpectedGuardRails;
    public SharedRailRequirement SharedRails;
    public bool DeriveFrogsFromIntersections;
    public int ExpectedMovableAssemblies;
}
```

An exact count is appropriate for ordinary turnouts and plain crossings.
Compound dual-gauge work uses minimums or ranges because shared-rail side,
frog number, and route angle can change the number of physical rail
intersections.

The geometry builder remains authoritative. The expectation catches an
incorrect expansion or classifier result.

## Common Parameters

| Parameter | Applies to | Purpose |
| --- | --- | --- |
| `handedness` | turnouts, slips, stubs | Mirror left/right geometry |
| `family` | ordinary, narrow, crossings, slips, stubs | Select standard or narrow route family |
| `frogNumber` | turnouts, three-way, dual work | Controls divergence length/angle |
| `crossingAngle` | crossings, slips | Explicit or derived crossing angle |
| `sharedRailSide` | dual work | Select which physical rail is shared |
| `controlMode` | dual and compound work | Independent, synchronized, or staged |
| `bladeLength` | point turnouts | Override calculated point length |
| `stubTravel` | stub switches | Movable route-end travel |
| `guardPolicy` | advanced override | Automatic by default |

Parameters that would create invalid geometry are rejected rather than
silently adjusted.

## Count and Shared-Rail Notation

The catalog uses:

- `R`: route-shared physical rail intervals, where multiple logical routes
  overlap before diverging
- `G`: gauge-shared rail intervals used by standard and narrow families
- `R+G`: both forms
- `None`: no intended shared interval
- `Derived`: generated from classified physical rail intersections and
  protected wheel paths
- `Ghost required`: the preset contains dual-gauge ports/routes
- `Ghost optional`: the recipe is family-parameterized and can later be
  instantiated for dual gauge, but the named preset does not require it

Expected frog counts include vee and crossing frog candidates. They do not
include blade convergence points.

## Ordinary Presets

### Ports and Topology

| Preset | Required ports | Route families | Native topology |
| --- | --- | --- | --- |
| Left turnout | `common: Standard`, `through: Standard`, `left: Standard` | 2 standard routes | 1 binary switch at common port |
| Right turnout | `common: Standard`, `through: Standard`, `right: Standard` | 2 standard routes | 1 binary switch at common port |
| Wye turnout | `common: Standard`, `left: Standard`, `right: Standard` | 2 standard routes | 1 binary switch at common port; neither exit is preferred straight |

### Geometry and Control

| Preset | Native switch nodes | Routes | Expected frogs | Guard rails | Shared rails | Switch groups | Auto frogs |
| --- | ---: | ---: | ---: | ---: | --- | --- | --- |
| Left turnout | 1 | 2 | 1 | 2 | R | None required | Yes |
| Right turnout | 1 | 2 | 1 | 2 | R | None required | Yes |
| Wye turnout | 1 | 2 | 1 | 2 | R | None required | Yes |

The preset may use the base ordinary turnout builder initially, but it should
also pass through the general centerline/intersection validator.

## Narrow Presets

### Ports and Topology

| Preset | Required ports | Route families | Native topology |
| --- | --- | --- | --- |
| Narrow left turnout | `common: Narrow`, `through: Narrow`, `left: Narrow` | 2 real narrow routes | 1 binary narrow switch |
| Narrow right turnout | `common: Narrow`, `through: Narrow`, `right: Narrow` | 2 real narrow routes | 1 binary narrow switch |
| Narrow wye turnout | `common: Narrow`, `left: Narrow`, `right: Narrow` | 2 real narrow routes | 1 binary narrow switch |

### Geometry and Control

| Preset | Native switch nodes | Routes | Expected frogs | Guard rails | Shared rails | Switch groups | Auto frogs |
| --- | ---: | ---: | ---: | ---: | --- | --- | --- |
| Narrow left turnout | 1 | 2 | 1 | 2 | R | None required | Yes |
| Narrow right turnout | 1 | 2 | 1 | 2 | R | None required | Yes |
| Narrow wye turnout | 1 | 2 | 1 | 2 | R | None required | Yes |

These use real narrow graph segments. Ghost graph is not needed unless the
preset is later embedded in dual-gauge compound work.

## Dual-Gauge Presets

### Ports and Topology

| Preset | Required ports | Route families | Native topology |
| --- | --- | --- | --- |
| Narrow diverges | `common: Dual`, `through: Dual`, `branch: Narrow` | 1 fixed standard route; 2 narrow routes | 1 narrow ghost binary switch; standard fixed through |
| Standard diverges | `common: Dual`, `through: Dual`, `branch: Standard` | 2 standard routes; 1 fixed narrow route | 1 standard binary switch; narrow ghost fixed through |
| Both diverge | `common: Dual`, `through: Dual`, `branch: Dual` | 2 standard routes; 2 narrow routes | 1 standard and 1 narrow binary switch |
| Dual splits into separate standard and narrow routes | `common: Dual`, `standard: Standard`, `narrow: Narrow` | 1 standard and 1 narrow fixed route | Fixed routes; normally no switch |
| Narrow branch joins dual main | `west: Dual`, `east: Dual`, `branch: Narrow` | 1 fixed standard route; 2 narrow routes | Same bidirectional recipe as Narrow diverges |
| Standard branch joins dual main | `west: Dual`, `east: Dual`, `branch: Standard` | 2 standard routes; 1 fixed narrow route | Same bidirectional recipe as Standard diverges |

`Narrow diverges` and `Narrow branch joins dual main` are author-facing aliases
for the same bidirectional recipe. The same is true for the two standard
variants.

### Geometry and Control

| Preset | Native switch nodes | Routes | Expected frogs | Guard rails | Shared rails | Switch groups | Auto frogs |
| --- | ---: | ---: | --- | --- | --- | --- | --- |
| Narrow diverges | 1 | 3 | At least 1 narrow vee; crossing frogs Derived | Derived | R+G | None required | Yes |
| Standard diverges | 1 | 3 | At least 1 standard vee; crossing frogs Derived | Derived | R+G | None required | Yes |
| Both diverge | 2 | 4 | At least 2 vee; crossing frogs Derived | Derived | R+G | Optional one 2-node synchronized group | Yes |
| Dual splits into separate standard and narrow routes | 0 | 2 | 0 or more, Derived from gauge separation | Derived | G ending inside object | None | Yes |
| Narrow branch joins dual main | 1 | 3 | At least 1 narrow vee; crossing frogs Derived | Derived | R+G | None required | Yes |
| Standard branch joins dual main | 1 | 3 | At least 1 standard vee; crossing frogs Derived | Derived | R+G | None required | Yes |

For both-diverge, `controlMode` decides whether the standard and narrow switch
nodes move independently or as one synchronized physical control.

All dual-gauge presets require ghost graph support. The preset compiler creates
and terminates ghost routes automatically at gauge-aware ports.

## Crossing Presets

### Ports and Topology

| Preset | Required ports | Route families | Native topology |
| --- | --- | --- | --- |
| Diamond crossing | `A0`, `A1`, `B0`, `B1`, all same selected family | 2 graph-disconnected crossing routes | 2 fixed routes; no switch |
| Arbitrary-angle crossing | Same as diamond | 2 graph-disconnected crossing routes | Same recipe; author/ports provide angle |
| 90-degree crossing | Same as diamond | 2 graph-disconnected crossing routes | Same recipe; angle locked to 90 degrees |

### Geometry and Control

| Preset | Native switch nodes | Routes | Expected frogs | Guard rails | Shared rails | Switch groups | Auto frogs |
| --- | ---: | ---: | ---: | --- | --- | --- | --- |
| Diamond crossing | 0 | 2 | 4 | Typically 4, Derived | None | None | Yes |
| Arbitrary-angle crossing | 0 | 2 | 4 | Typically 4, Derived | None | None | Yes |
| 90-degree crossing | 0 | 2 | 4 | Typically 4, Derived | None | None | Yes |

All four physical rail-pair intersections become crossing frog candidates.
The graph routes remain disconnected.

The initial crossing presets support one selected real family: standard or
narrow. The schema permits a future dual-gauge crossing variant, which would
require ghost graph support and a much larger derived frog set.

## Slip Presets

### Ports and Topology

| Preset | Required ports | Route families | Native topology |
| --- | --- | --- | --- |
| Single slip | `A0`, `A1`, `B0`, `B1`, all same selected family | 2 crossing routes plus 1 connecting slip route | 2 ordinary binary switch nodes |
| Double slip | `A0`, `A1`, `B0`, `B1`, all same selected family | 2 crossing routes plus 2 connecting slip routes | 4 ordinary binary switch nodes |

### Geometry and Control

| Preset | Native switch nodes | Routes | Expected frogs | Guard rails | Shared rails | Switch groups | Auto frogs |
| --- | ---: | ---: | --- | --- | --- | --- | --- |
| Single slip | 2 | 3 | At least 4; typically 6, Derived | Typically 4-6, Derived | R | 1 synchronized 2-node group | Yes |
| Double slip | 4 | 4 | At least 4; typically 8, Derived | Typically 6 or more, Derived | R | 2 synchronized node-pair groups plus valid-state rules | Yes |

The explicit slip routes create graph connectivity. The crossing routes alone
remain disconnected. Frog classification uses route metadata so slip blade
convergence is not mistaken for a frog.

Initial slip presets are standard or narrow single-family work. A dual-gauge
slip remains possible as a future compound preset, not an implicit option.

## Stub-Switch Presets

### Ports and Topology

| Preset | Required ports | Route families | Native topology |
| --- | --- | --- | --- |
| Left stub turnout | `common`, `through`, `left`, all selected family | 2 routes | 1 binary switch |
| Right stub turnout | `common`, `through`, `right`, all selected family | 2 routes | 1 binary switch |
| Three-way stub turnout | `common`, `left`, `center`, `right`, all selected family | 3 routes | 2 staged binary switch nodes |

### Geometry and Control

| Preset | Native switch nodes | Routes | Expected frogs | Guard rails | Shared rails | Switch groups | Auto frogs |
| --- | ---: | ---: | ---: | ---: | --- | --- | --- |
| Left stub turnout | 1 | 2 | 1 | 2 | R | None required | Yes |
| Right stub turnout | 1 | 2 | 1 | 2 | R | None required | Yes |
| Three-way stub turnout | 2 | 3 | 2 | Typically 4, Derived | R | 1 staged valid-state group | Yes |

The topology matches point turnouts. The movable assembly recipe emits
complete movable rail ends instead of tapered blades.

Stub presets initially accept standard or narrow real families. Dual-gauge
stub movement should be a later explicit compound preset.

## Three-Way Presets

### Ports and Topology

| Preset | Required ports | Route families | Native topology |
| --- | --- | --- | --- |
| Standard three-way | `common`, `left`, `center`, `right`, all Standard | 3 standard routes | 2 staged standard binary switches |
| Narrow three-way | `common`, `left`, `center`, `right`, all Narrow | 3 real narrow routes | 2 staged narrow binary switches |
| Dual-gauge three-way | `common`, `left`, `center`, `right`, all Dual | 3 standard and 3 narrow routes | 2 staged binary switches per family |

### Geometry and Control

| Preset | Native switch nodes | Routes | Expected frogs | Guard rails | Shared rails | Switch groups | Auto frogs |
| --- | ---: | ---: | --- | --- | --- | --- | --- |
| Standard three-way | 2 | 3 | 2 | Typically 4, Derived | R | 1 staged valid-state group | Yes |
| Narrow three-way | 2 | 3 | 2 | Typically 4, Derived | R | 1 staged valid-state group | Yes |
| Dual-gauge three-way | 4 | 6 | At least 4 vee; crossing frogs Derived | Derived | R+G | 2 family staged groups; optional synchronized composite group | Yes |

No three-way preset creates a native node with four legs. The staged group
exposes three legal visible states while compiling to two ordinary binary
switches per active family.

## Summary Catalog

| Preset | Native nodes | Routes | Frogs | Guard rails | Shared rails | Supports ghost graph |
| --- | ---: | ---: | --- | --- | --- | --- |
| Ordinary left turnout | 1 | 2 | 1 | 2 | R | No |
| Ordinary right turnout | 1 | 2 | 1 | 2 | R | No |
| Ordinary wye turnout | 1 | 2 | 1 | 2 | R | No |
| Narrow left turnout | 1 | 2 | 1 | 2 | R | No; real narrow graph |
| Narrow right turnout | 1 | 2 | 1 | 2 | R | No; real narrow graph |
| Narrow wye turnout | 1 | 2 | 1 | 2 | R | No; real narrow graph |
| Dual narrow diverges | 1 | 3 | At least 1 + Derived | Derived | R+G | Required |
| Dual standard diverges | 1 | 3 | At least 1 + Derived | Derived | R+G | Required |
| Dual both diverge | 2 | 4 | At least 2 + Derived | Derived | R+G | Required |
| Dual splits to separate routes | 0 | 2 | 0 or more, Derived | Derived | G ends | Required |
| Narrow branch joins dual main | 1 | 3 | At least 1 + Derived | Derived | R+G | Required |
| Standard branch joins dual main | 1 | 3 | At least 1 + Derived | Derived | R+G | Required |
| Diamond crossing | 0 | 2 | 4 | Typically 4 | None | Optional future dual variant |
| Arbitrary-angle crossing | 0 | 2 | 4 | Typically 4 | None | Optional future dual variant |
| 90-degree crossing | 0 | 2 | 4 | Typically 4 | None | Optional future dual variant |
| Single slip | 2 | 3 | Typically 6, Derived | Typically 4-6 | R | Optional future dual variant |
| Double slip | 4 | 4 | Typically 8, Derived | Typically 6+ | R | Optional future dual variant |
| Left stub turnout | 1 | 2 | 1 | 2 | R | No in initial preset |
| Right stub turnout | 1 | 2 | 1 | 2 | R | No in initial preset |
| Three-way stub turnout | 2 | 3 | 2 | Typically 4 | R | No in initial preset |
| Standard three-way | 2 | 3 | 2 | Typically 4 | R | No |
| Narrow three-way | 2 | 3 | 2 | Typically 4 | R | No; real narrow graph |
| Dual-gauge three-way | 4 | 6 | At least 4 + Derived | Derived | R+G | Required |

## Preset Expansion Pipeline

```text
SpecialWorkPresetInstance
  -> resolve preset and aliases
  -> validate parameters and bound port gauge modes
  -> generate gauge-aware SpecialWorkPorts
  -> generate LogicalRoutes from route recipes
  -> compile native binary topology and switch groups
  -> create SpecialWorkDefinition
  -> preview/debug/validate
  -> apply graph topology
  -> build physical rails and visible geometry
```

The preview occurs before graph mutation. A bad preset must not leave partial
native nodes or ghost segments behind.

## Debug Preview Mode

Debug preview is a required authoring feature. It draws the expanded preset
before final mesh generation:

| Color | Draws |
| --- | --- |
| Blue | Standard logical routes |
| Cyan | Narrow logical routes, including generated ghost routes |
| Green | Merged shared physical rail intervals |
| Orange | All classified and unclassified physical rail intersections |
| Red | Frog candidates and their nose/heel orientation |
| Purple | Switch blades and movable stub assemblies |

Recommended representation:

- routes: thin directional polylines with route ID labels
- shared intervals: thicker green line drawn over the contributing rails
- intersections: orange spheres with angle and classification labels
- frogs: red nose arrow plus required rail-cut markers
- blades/stubs: thick purple segment with switch-group/state label
- native switch nodes: text labels only, so they do not compete with the
  required color language

```csharp
internal enum SpecialWorkDebugMode
{
    Off,
    SelectedObject,
    AllSpecialWork,
    ValidationErrorsOnly
}
```

Debug mode should provide independent toggles for routes, shared rails,
intersections, frogs, and movable assemblies while preserving the fixed color
meaning.

The preview panel should also show:

- preset ID and canonical recipe ID
- bound port IDs and gauge modes
- route count by family
- generated native switch-node count
- expected versus derived frog count
- expected versus derived guard count
- switch-group states and native-node mappings
- validation errors and warnings

## Validation Rules

Every expanded preset must validate:

- all required ports are bound exactly once
- bound track gauge availability matches each port template
- every logical route connects compatible family ports
- every native switch node has exactly three legs
- generated graph IDs are deterministic
- switch groups expose only legal native-state combinations
- fixed crossing routes remain graph-disconnected
- slip routes are the only added crossing connections
- expected exact/minimum frog counts are satisfied
- every frog has required rail cuts and protected wheel paths
- every guard candidate protects a known wheel path
- every movable assembly belongs to a switch group or direct native node
- no unclassified physical rail intersection remains
- ghost routes exist only through dual-gauge ports and terminate correctly at
  real narrow-only ports

## Recommended Initial Library

Implement canonical recipes in this order:

1. `turnout.binary` with standard/narrow family and left/right/wye parameters
2. `dual.branch-transition` for narrow-only branch joining dual main
3. mirrored `dual.branch-transition` for standard-only branch
4. `dual.split` for dual-to-separate standard/narrow routes
5. `crossing.plain` with arbitrary and locked 90-degree angle policies
6. `three-way.staged`
7. `stub.binary` and `stub.three-way`
8. `slip.single`
9. `slip.double`
10. compound dual-gauge both-diverge and dual three-way presets

The first implementation test remains the narrow-only yard branch joining a
dual-gauge main. It exercises preset expansion, gauge-aware ports, automatic
ghost termination, one native switch, shared rails, derived frogs/guards, and
the complete debug color language without starting with the highest-risk
compound layouts.
