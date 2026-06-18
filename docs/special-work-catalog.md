# Special-Work Catalog

The catalog identifies reusable special-work types from authored gauge markers
and measured topology. Runtime matching must not depend on a particular
`NCustom_*` or `SCustom_*` instance ID.

## Catalog Model

| Anchor | Used For | Recognition |
|---|---|---|
| Node | Turnouts, wyes, slips, and multi-route junctions | Connected route families, switch states, intersections, and measured rail geometry |
| Segment | Fixed transitions and future segment-based crossings | Segment marker, endpoint degree, neighboring track families, and measured rail geometry |

Truth tables are appropriate when a type has movable blades, route-state
ownership, or compound frog/rail ownership that must be validated. Fixed
transitions use a segment topology contract instead.

## Transitions

### `dual.shared-rail-flip`

Fixed transition that moves the narrow-gauge shared rail from one standard
outer rail to the other.

Recognition contract:

- The anchor segment is marked `DualGauge_T`.
- Both anchor endpoints are degree two after generated and hidden control
  segments are excluded.
- Each endpoint has exactly one non-transition dual-gauge neighbor.
- The two neighbors are one explicitly marked `DualGauge_L` and one explicitly
  marked `DualGauge_R`.
- Neighbor order is irrelevant. Both L-to-R and R-to-L generate the same
  procedural type.

Generated hardware:

- Two continuous standard running rails.
- Two measured narrow transition rails.
- Two tapered shared-rail frog points.
- Four guard rails.
- Dual-gauge transition ties and collider.

Invalid topology renders the normal dual-gauge fallback and logs the failed
contract. This transition has no movable blades or switch state, so it does not
use a turnout truth table.

## Switches And Wyes

| Preset | Family |
|---|---|
| `turnout.standard.left` | Standard turnout |
| `turnout.standard.right` | Standard turnout |
| `turnout.standard.wye` | Standard wye |
| `turnout.narrow.left` | Narrow turnout |
| `turnout.narrow.right` | Narrow turnout |
| `turnout.narrow.wye` | Narrow wye |
| `dual.narrow-branch-joins-main` | Dual-gauge turnout |
| `dual.standard-branch-joins-main` | Dual-gauge turnout |
| `dual.both-diverge` | Dual-gauge turnout |
| `dual.split-standard-narrow` | Dual-gauge split |
| `three-way.standard` | Standard three-way |
| `three-way.narrow` | Narrow three-way |
| `three-way.dual` | Dual-gauge three-way |

## Crossings And Slips

| Preset | Family |
|---|---|
| `crossing.diamond` | Diamond crossing |
| `crossing.arbitrary-angle` | Measured-angle crossing |
| `crossing.90-degree` | Right-angle crossing |
| `slip.single` | Single slip |
| `slip.double` | Double slip |

## Stub Switches

| Preset | Family |
|---|---|
| `stub.left` | Left stub turnout |
| `stub.right` | Right stub turnout |
| `stub.three-way` | Three-way stub turnout |

