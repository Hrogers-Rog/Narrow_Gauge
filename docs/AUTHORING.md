# Authoring Guide

How to build narrow-gauge and dual-gauge track in a FUSE route package.

The workflow is: **place normal track, assign segment gauges, then bind a preset
to one anchor node.** Narrow Gauge derives the routes, physical rails,
intersections, frogs, guards, blades, and native topology from the surrounding
track — you do not hand-place any of it.

## Segment Gauges

Set `gauge` on FUSE `tracks.segments` entries:

| Value | Meaning |
| --- | --- |
| `Narrow` | Narrow gauge only |
| `DualGauge` | Dual gauge, shared rail side inferred |
| `DualGauge_L` | Dual gauge, shared rail is the left standard rail |
| `DualGauge_R` | Dual gauge, shared rail is the right standard rail |
| `DualGauge_T` | Dedicated shared-rail transition segment |

Omitting `gauge` leaves the segment standard.

### Dual-Gauge Rules

A `DualGauge` segment **automatically receives a real native narrow-gauge ghost
route** with deterministic `fuse-ng:*` ids. Do not author ghost track yourself.

`DualGauge_L` and `DualGauge_R` explicitly select which outer standard rail is
shared. Left and right refer to the physical frame authored on the track nodes, so
reversing a segment's `startId` and `endId` does **not** swap the selected rail —
this is deliberate, and it is the thing most likely to surprise you if you think
of left/right as direction-relative.

Plain `DualGauge` infers the shared side from narrow branch transitions and
propagates it through connected dual segments. Explicit
`fuse-ng:shared-rail=left|right` tags still work. An unconstrained dual-gauge
component defaults to the right rail.

`DualGauge_T` marks a degree-two transition between a `DualGauge_L` and a
`DualGauge_R` segment, where the shared rail flips sides. Its generated narrow
route shifts between the neighbouring offsets. This is catalog preset
`dual.shared-rail-flip`; it is segment-anchored and generates automatically when
the marked segment has exactly one `DualGauge_L` and one `DualGauge_R` neighbour.

### Switches

Fully dual-gauge turnouts mechanically synchronise their standard and generated
narrow switch routes. Route mapping follows linked segments rather than assuming
both native switches share the same normal/reversed ordering.

A switch with two `DualGauge` main legs and one `Narrow` branch compiles
automatically into a fixed standard route plus a real three-leg narrow switch on
the generated narrow graph. Standard equipment cannot route onto the narrow-only
branch.

## Selecting Special Work

Bind a preset to one existing anchor node through the
`narrowGauge.specialWork` extension. FUSE itself needs no Narrow Gauge schema or
patch.

```json
{
  "extensions": {
    "narrowGauge.specialWork": {
      "version": 1,
      "objects": {
        "yard-narrow-lead": {
          "preset": "dual.narrow-branch-joins-main",
          "anchorNode": "NCustom_0ifg",
          "parameters": {
            "sharedRailSide": "auto",
            "frogNumber": 8
          }
        }
      }
    }
  }
}
```

`objects` also accepts an array of objects with explicit `id` properties.

Unknown presets, incompatible topology, duplicate ids, and duplicate anchor
bindings are logged as authoring errors rather than silently ignored. Check
`FUSE.log` when a preset does not appear.

### Preset Families

The full list with topology contracts is in
[special-work-catalog.md](special-work-catalog.md).

| Family | Examples |
| --- | --- |
| Turnouts | `turnout.standard.left/right/wye`, `turnout.narrow.*` |
| Dual-gauge turnouts | `dual.narrow-branch-joins-main`, `dual.standard-branch-joins-main`, `dual.both-diverge`, `dual.split-standard-narrow` |
| Three-way | `three-way.standard`, `three-way.narrow`, `three-way.dual` |
| Crossings | `crossing.diamond`, `crossing.arbitrary-angle`, `crossing.90-degree` |
| Slips | `slip.single`, `slip.double` |
| Stubs | `stub.left`, `stub.right`, `stub.three-way` |
| Transitions | `dual.shared-rail-flip` |

## Automatic Diamonds

Fixed standard-gauge diamonds are the exception to the anchor-node workflow. Two
sufficiently long standard segments that cross at the same grade **without being
joined** are detected directly — they need neither an anchor node nor ghost nodes.

The source routes stay graph-disconnected; only their rail and tie rendering is
replaced through the measured crossing envelope. Skew diamonds classify the four
physical intersections as two outward acute V-frogs and two inner obtuse K-frogs.

## Verifying Your Work

1. Load the route and run `/fuse.loaded` to confirm the package applied.
2. Check `FUSE.log` for authoring errors from the special-work parser.
3. Enable the debug view in the Unity Mod Manager panel and look at the colours —
   cyan narrow routes, green shared rails, orange intersections, red frog
   candidates. If a frog candidate is missing or in the wrong place, the topology
   is not what the preset expected.
4. Run a train over it.

## Limits

- **Three-way switches** are in the catalog as staged topology, but Railroader
  does not support them as ordinary graph switches.
- **Cross-family coupling** is not active. `DualGaugeLinkRegistry.CanBridgeCoupling(...)`
  is the boundary where that permission will live, but no patch enforces it yet.
- **Invalid measured plans** fall back to existing fallback geometry rather than
  failing the load — so bad geometry shows up as "it looks like it used to,"
  not as an error.

## Related

- [Special-Work Catalog](special-work-catalog.md)
- [Getting Started](GETTING_STARTED.md)
- [FUSE Package Author Guide](https://github.com/F-U-S-E-E/FuseDevelopmentGroup/blob/main/docs/PACKAGE_AUTHOR_GUIDE.md)
