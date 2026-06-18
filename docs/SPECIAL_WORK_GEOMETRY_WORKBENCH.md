# Special-Work Geometry Workbench

The geometry workbench calculates special trackwork before custom hardware is
rendered. A plan that passes validation owns the fixed rail mesh inside its
calculated work envelope; an invalid plan retains the existing fallback mesh.

## Pipeline

1. Discover logical standard and narrow route centerlines.
2. Offset each route into physical left and right rail centerlines.
3. Project physical rails into a turnout-local 2D plane.
4. Sample the rails and create explicit 2D line segments.
5. Detect shared rail intervals and choose one physical owner.
6. Detect and classify physical rail intersections using orientation tests.
7. Calculate frog orientation and setbacks from the crossing angle.
8. Create explicit rail cuts around accepted frogs.
9. Create wing, frog-nose, guard, movable-blade, and closure plans.
10. Split remaining running rails into non-overlapping fixed rail pieces.
11. Validate the complete `SpecialWorkMeshPlan`.

Physical rail crossings never change graph connectivity.

## Frog Calculation

For each accepted physical rail intersection:

```text
railHeadSetback = railHeadWidth / tan(crossingAngle / 2)
flangewaySetback = flangewayWidth / sin(crossingAngle / 2)
cutHalfLength = clamp(max(railHeadSetback, flangewaySetback))
```

The normalized rail tangents determine the frog nose angle bisector. The 2D
cross-product sign determines handedness.

## Shared Rails

Coincident physical rail intervals are merged in 2D. One deterministic rail is
selected as the owner and all duplicate intervals become `SharedDuplicate`
cuts. Shared physical rail therefore renders once even when several logical
routes use it.

## Switch Blades

Blades are calculated from the normal and reversed logical routes in each
native switch group. The blade tip is where the route rails stop being
coincident. The root is where lateral separation reaches the configured
threshold or the configured maximum blade length.

Blade convergence is never treated as a frog.

## Guard Rails

Guard rails are derived only from accepted frogs. The builder finds the
opposite rail of each protected route, offsets a check rail inward by the
configured center offset, tapers both ends, and rejects plans that collide with
other running rails.

## Outputs

Enable the mod's special-work debug view to see the calculated plan in game.

Use **Export measured 2D special-work plans** in the mod UI to write an SVG and
text measurement report for every runtime special-work object:

```text
Railroader persistent data/NarrowGauge/SpecialWorkPlans
```

The SVG colors are:

- Blue: standard physical rails
- Cyan: narrow physical rails
- Green: shared rails and guard plans
- Yellow dashed: removed rail intervals
- Red: frog candidates and frog-nose plans
- Orange: wing rail plans
- Purple: movable blade plans

The text report contains exact local coordinates, distances along rails,
crossing angles, frog setbacks, cut intervals, blade tip/root distances, plan
parameters, and validation failures.

## Research Constraints

- Three-rail dual gauge uses one common physical rail.
- Switch points must fit against stock rails and their heel/root must be
  secured.
- Check/guard geometry is measured relative to the frog gauge line and must
  constrain the opposing wheel path.
- Wing rails and check rails are separate from switch blades; blades are
  derived from route divergence rather than frog location.
