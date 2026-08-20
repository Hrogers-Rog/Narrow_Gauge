# NCustom_0ifg vs NCustom_24b2 Continuity Report

Date: 2026-06-10

Sources:

- `C:\Steam\steamapps\common\Railroader\Mods\narrow_gauge test\game-graph.json`
- `%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\NarrowGauge\SpecialWorkPlans\special-work_NCustom_0ifg.txt`
- `%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\NarrowGauge\SpecialWorkPlans\special-work_NCustom_24b2.txt`
- Current code in `src/SpecialWorkRuntimeDiscovery.cs`, `src/SpecialWorkGeometryAnalyzer.cs`, and `src/SpecialWorkGeometryBuilder.cs`

## Summary

`NCustom_0ifg` validates false because it derives zero blade plans:

```text
Switch groups exist but no route-divergence blade plans were derived.
```

`NCustom_24b2` validates true because it keeps two narrow reversed rails as point blades:

```text
blade:0 stock=narrow-normal:left movable=narrow-reversed:left
blade:1 stock=narrow-normal:right movable=narrow-reversed:right
```

The first continuity failure in `NCustom_0ifg` happens before frog mesh generation. It happens when the current system tries to infer switch blades/closure rails from rail centerlines and shared intervals instead of wheel/flange paths.

## Earliest Failure

The earliest failure is:

```text
NCustom_0ifg narrow reversed route loses blade/closure ownership at the switch throat.
```

In `0ifg`, both reversed-route rails become `SuppressedRail`:

```text
narrow-reversed:left  role=SuppressedRail
narrow-reversed:right role=SuppressedRail
```

That means the builder has no surviving movable/closure rail pair for the narrow diverging route. The later frog logic is then operating on rails that are already incorrectly classified.

In `24b2`, the corresponding rails remain alive as point blades:

```text
narrow-reversed:left  role=PointBlade
narrow-reversed:right role=PointBlade
```

So `24b2` has a valid route split and `0ifg` does not.

## Why This Happens

The current pipeline does this:

```text
LogicalRoute centerline
-> generic left/right rail offsets
-> shared interval detection
-> raw rail intersections
-> frog candidates
-> blade inference
```

That is not enough for dual-gauge transition work.

For `0ifg`, the narrow branch joins/leaves dual gauge. The rails that matter are not just generic left/right offsets. The builder must know which wheel flange is being guided by which stock rail, which rail is shared, which rail becomes the movable point, and which closure rail carries the wheel path into the frog.

Right now it does not calculate wheel/flange paths. It calculates rail centerlines only:

```csharp
route.Centerline.Parallel(-gauge.Inside / 2f, Hand.Left)
route.Centerline.Parallel( gauge.Inside / 2f, Hand.Right)
```

That produces physical rail centerlines, but it does not say:

- which flange path is protected,
- which rail is stock,
- which rail is movable,
- which closure rail continues to the frog,
- which shared rail is owned by which route.

## Route/Topology Comparison

### NCustom_0ifg

Preset:

```text
dual.narrow-branch-joins-main
```

Authoring graph neighborhood:

| Segment | Gauge | From | To |
|---|---|---|---|
| `SCustom_yq1c` | DualGauge | `NCustom_kvg2` | `NCustom_0ifg` |
| `SCustom_ql13` | DualGauge | `NCustom_0ifg` | `NCustom_9sjz` |
| `SCustom_j9wm` | Narrow | `NCustom_0ifg` | `NCustom_jp45` |

Generated routes:

| RouteId | Family | Intended wheel path |
|---|---|---|
| `standard-through` | Standard | fixed dual main through `NCustom_0ifg` |
| `narrow-normal` | Narrow | generated narrow ghost main through `NCustom_0ifg` |
| `narrow-reversed` | Narrow | generated narrow ghost to real narrow branch |

Validation:

```text
valid=False
blades=0
```

### NCustom_24b2

Preset:

```text
dual.both-diverge
```

Authoring graph neighborhood:

| Segment | Gauge | From | To |
|---|---|---|
| `SCustom_h9k9` | DualGauge | `NCustom_9sjz` | `NCustom_24b2` |
| `SCustom_sxx7` | DualGauge | `NCustom_24b2` | `NCustom_nt6h` |
| `SCustom_oxw9` | DualGauge | `NCustom_24b2` | `NCustom_y6ml` |

Generated routes:

| RouteId | Family | Intended wheel path |
|---|---|---|
| `standard-normal` | Standard | standard normal route through native switch |
| `standard-reversed` | Standard | standard reversed route through native switch |
| `narrow-normal` | Narrow | generated narrow normal route |
| `narrow-reversed` | Narrow | generated narrow reversed route |

Validation:

```text
valid=True
blades=2
```

## Rail Role Comparison

| RailId | 0ifg Role | 24b2 Equivalent Role | Continuity Meaning |
|---|---|---|---|
| `standard-through:left` | `SharedRail` | `standard-normal:left = SharedRail` | Standard/dual shared rail survives. |
| `standard-through:right` | `FrogRail` | `standard-normal:right = SharedRail` or `standard-reversed:right = SuppressedRail` | 0ifg standard right is treated as frog conflict rail, not just route rail. |
| `narrow-normal:left` | `SharedRail` | `narrow-normal:left = SharedRail` | Normal narrow left is shared/suppressed as expected. |
| `narrow-normal:right` | `SharedRail` | `narrow-normal:right = SharedRail` | 0ifg marks this shared, but this rail also participates in the narrow V crossing. |
| `narrow-reversed:left` | `SuppressedRail` | `narrow-reversed:left = PointBlade` | This is the first major divergence. 0ifg suppresses what should help define the diverging point/closure path. |
| `narrow-reversed:right` | `SuppressedRail` | `narrow-reversed:right = PointBlade` | Same failure on the other reversed route rail. |

## Rail Piece Report

The current export does not include true wheel-path start/end ports, so the ports below are inferred from the authoring graph and route names.

### NCustom_0ifg

| RailId | Role | RouteId | GaugeFamily | StartPort | EndPort | ConnectedTo | SharedRailOwner | ContinuityValid |
|---|---|---|---|---|---|---|---|---|
| `standard-through:left` | `SharedRail` | `standard-through` | Standard | `NCustom_kvg2` dual main | `NCustom_9sjz` dual main | shares with `narrow-normal:left`, `narrow-reversed:left` | `standard-through:left` | Yes |
| `standard-through:right` | `FrogRail` | `standard-through` | Standard | `NCustom_kvg2` dual main | `NCustom_9sjz` dual main | crosses `narrow-reversed:left`, `narrow-reversed:right` | none | Partial, cut by frogs |
| `narrow-normal:left` | `SharedRail` | `narrow-normal` | Narrow | generated ghost dual main | generated ghost dual main | shares with `standard-through:left`, `narrow-reversed:left` | `standard-through:left` | Suppressed duplicate, expected if shared owner is standard |
| `narrow-normal:right` | `SharedRail` | `narrow-normal` | Narrow | generated ghost dual main | generated ghost dual main | shares with `narrow-reversed:right`, crosses `narrow-reversed:left` | `narrow-normal:right` | Partial, but role is ambiguous |
| `narrow-reversed:left` | `SuppressedRail` | `narrow-reversed` | Narrow | generated ghost dual main | `NCustom_jp45` narrow branch | shares with `standard-through:left`, crosses `standard-through:right`, crosses `narrow-normal:right` | not owner | No, loses blade/closure ownership |
| `narrow-reversed:right` | `SuppressedRail` | `narrow-reversed` | Narrow | generated ghost dual main | `NCustom_jp45` narrow branch | shares with `narrow-normal:right`, crosses `standard-through:right` | not owner | No, loses blade/closure ownership |

Fixed rail pieces:

| Piece | RailId | Kind | Distance Interval | Continuity Comment |
|---|---|---|---|---|
| `fixed:0` | `standard-through:left` | `SharedRunning` | `65.490-73.723` | continuous shared stock rail |
| `fixed:1` | `standard-through:right` | `FixedRunning` | `65.490-65.896` | cut by crossing candidate |
| `fixed:2` | `standard-through:right` | `FixedRunning` | `68.084-71.437` | middle piece survives between cuts |
| `fixed:3` | `standard-through:right` | `FixedRunning` | `73.009-73.723` | post-frog continuation |
| `fixed:4` | `narrow-normal:right` | `FixedRunning` | `65.489-68.615` | route rail before narrow V crossing |
| `fixed:5` | `narrow-normal:right` | `FixedRunning` | `70.426-73.722` | route rail after narrow V crossing |
| `fixed:6` | `narrow-reversed:left` | `FixedRunning` | `65.632-68.672` | survives, but role says suppressed rather than closure/blade |
| `fixed:7` | `narrow-reversed:left` | `FixedRunning` | `70.483-71.543` | survives between cuts, not role-owned as closure |
| `fixed:8` | `narrow-reversed:left` | `FixedRunning` | `73.115-73.829` | short continuation |
| `fixed:9` | `narrow-reversed:right` | `FixedRunning` | `65.515-65.921` | short pre-crossing piece |
| `fixed:10` | `narrow-reversed:right` | `FixedRunning` | `68.109-73.645` | long piece survives, but not classified as closure/blade |

### NCustom_24b2

| RailId | Role | RouteId | GaugeFamily | StartPort | EndPort | ConnectedTo | SharedRailOwner | ContinuityValid |
|---|---|---|---|---|---|---|---|---|
| `standard-normal:left` | `SharedRail` | `standard-normal` | Standard | `NCustom_9sjz` dual main | normal dual exit | shares with standard/narrow left rails | `standard-normal:left` | Yes |
| `standard-normal:right` | `SharedRail` | `standard-normal` | Standard | `NCustom_9sjz` dual main | normal dual exit | shares with `standard-reversed:right` | `standard-normal:right` | Yes |
| `standard-reversed:left` | `SharedRail` | `standard-reversed` | Standard | `NCustom_9sjz` dual main | reversed dual exit | shares with left rails | `standard-reversed:left` in its far interval | Yes |
| `standard-reversed:right` | `SuppressedRail` | `standard-reversed` | Standard | `NCustom_9sjz` dual main | reversed dual exit | crosses `standard-normal:left`, `narrow-normal:right` | not owner | Partial, but standard route has enough pieces |
| `narrow-normal:left` | `SharedRail` | `narrow-normal` | Narrow | generated ghost dual main | generated ghost normal exit | shares with standard left rails | standard owner | Suppressed duplicate, expected |
| `narrow-normal:right` | `SharedRail` | `narrow-normal` | Narrow | generated ghost dual main | generated ghost normal exit | crosses `standard-reversed:right` | `narrow-normal:right` | Yes after cut |
| `narrow-reversed:left` | `PointBlade` | `narrow-reversed` | Narrow | generated ghost dual main | generated ghost reversed exit | blade against `narrow-normal:left` | not shared owner | Yes, but suppressed as shared duplicate after ownership |
| `narrow-reversed:right` | `PointBlade` | `narrow-reversed` | Narrow | generated ghost dual main | generated ghost reversed exit | blade against `narrow-normal:right`, crosses `standard-normal:left` | not shared owner | Yes |

Fixed rail pieces:

| Piece | RailId | Kind | Distance Interval | Continuity Comment |
|---|---|---|---|---|
| `fixed:0` | `standard-normal:left` | `SharedRunning` | `76.469-79.655` | shared route piece before frog area |
| `fixed:1` | `standard-normal:left` | `SharedRunning` | `81.628-82.728` | shared route piece between cuts |
| `fixed:2` | `standard-normal:left` | `SharedRunning` | `84.342-85.035` | shared route continuation |
| `fixed:3` | `standard-normal:right` | `FixedRunning` | `76.469-85.035` | continuous stock/running rail |
| `fixed:4` | `standard-reversed:left` | `FixedRunning` | `76.313-84.848` | continuous route rail |
| `fixed:5` | `standard-reversed:right` | `FixedRunning` | `76.492-76.738` | pre-cut rail |
| `fixed:6` | `standard-reversed:right` | `FixedRunning` | `79.246-82.826` | middle route rail |
| `fixed:7` | `standard-reversed:right` | `FixedRunning` | `84.440-85.133` | continuation |
| `fixed:8` | `narrow-normal:right` | `FixedRunning` | `76.469-76.715` | pre-cut narrow normal rail |
| `fixed:9` | `narrow-normal:right` | `FixedRunning` | `79.223-85.035` | narrow normal continuation |
| `fixed:10` | `narrow-reversed:right` | `ClosureRail` | `76.425-79.706` | valid closure rail after blade root |
| `fixed:11` | `narrow-reversed:right` | `ClosureRail` | `81.679-85.029` | valid closure continuation after frog gap |

## Shared Rail Comparison

`0ifg` has four shared intervals:

```text
standard-through:left x narrow-normal:left
standard-through:left x narrow-reversed:left
narrow-normal:left x narrow-reversed:left
narrow-normal:right x narrow-reversed:right
```

The problem is not that shared rails exist. The problem is that the shared-rail owner decision suppresses the reversed narrow rails before the switch blade/closure ownership has been established.

`24b2` also has many shared intervals, but it still produces blade plans. The important difference is:

```text
24b2 blade derivation falls back to same-side normal/reversed rail pairing.
0ifg sees a same-family Vee candidate and tries to derive blades from Vee rails instead.
```

That Vee-based derivation fails in `0ifg`, and the fallback is skipped.

## The Wheel/Flange Path Problem

The requested correction is valid: the generator should trace wheel/flange paths, not just rail centerlines.

Current implementation has no `WheelPath` data structure. It has:

```text
LogicalRoute -> RailCenterline(left/right)
```

It needs:

```text
LogicalRoute
-> WheelPath
-> left/right flange guide paths
-> stock rail ownership
-> movable blade ownership
-> closure rail continuity
-> frog/check/guard decisions
```

For the first dual-gauge narrow-branch test, each narrow wheel path should know:

| WheelPath | Family | Entry | Exit | Guided by | Expected rail ownership |
|---|---|---|---|---|
| narrow normal | Narrow | dual ghost main | dual ghost main | shared rail + third rail | shared rail stays stock, opposite rail stays continuous |
| narrow reversed | Narrow | dual ghost main | narrow-only branch | shared rail + branch rail | point blade at route split, closure rail curves into first frog |

The flange path should be calculated along the gauge face/check side, not inferred after the rail intersections are found. Frogs and guard rails should then be derived from where those flange paths require wheel support/checking.

## First Point Where Continuity Is Lost

Ordered pipeline check:

| Stage | NCustom_0ifg | NCustom_24b2 | Result |
|---|---|---|---|
| Native topology | 2 dual + 1 narrow branch, ghost narrow switch | 3 dual, standard + narrow switch families | both discovered |
| Logical routes | 1 standard through, 2 narrow switched routes | 2 standard routes, 2 narrow routes | both have routes |
| Generic rail offsets | 6 rails | 8 rails | both generated |
| Shared intervals | 4 intervals, reversed narrow rails overlap shared rails | 8 intervals, reversed narrow rails overlap shared rails | both shared, but 0ifg more fragile |
| Rail role assignment | reversed narrow rails become `SuppressedRail` | reversed narrow rails become `PointBlade` | first failure |
| Blade derivation | 0 blades | 2 blades | validation failure |
| Closure continuity | no `ClosureRail` pieces | `narrow-reversed:right` has closure pieces | 0ifg loses route continuity |
| Frog/cut generation | happens after the failure | happens after valid blades | not earliest cause |

Earliest failing condition:

```text
0ifg does not establish a wheel-path-owned point blade and closure rail before shared duplicate suppression and frog cuts.
```

## Immediate Fix Direction

Do not start by changing frog meshes.

First fix the route/wheel path stage:

1. Add `WheelPath` with route id, family, entry/exit ports, centerline, left/right flange guide paths, stock side, movable side, blade tip/root, closure start, and route ownership.
2. Build `WheelPath` before `RailCenterline`.
3. For dual-gauge branch transitions, explicitly mark the shared rail and branch-side rail from the wheel path.
4. Derive stock rails and point blades from the wheel path split near the switch node.
5. Only after blade/closure ownership is stable, use rail intersections to identify frog candidates.
6. Shared duplicate suppression must not suppress a route's only blade/closure rail before that route has a valid wheel path.

Concrete code-level first fix:

```text
SpecialWorkGeometryBuilder.BuildSwitchBlades should not skip fallback same-side route split logic when Vee-based blade derivation fails.
```

That will make `0ifg` validate again, but it is only a symptom fix. The real fix is to add wheel/flange path ownership so the blade pair is chosen from the route split, not from frog/Vee rail identity.

