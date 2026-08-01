Status: Draft

User-directed implementation exists; Claude review is pending.

# Standard-Gauge Diamond Crossing

## Scope

Implement the EF&A interlock diamond as the first accepted instance of the
generic `crossing.diamond` preset. This is ordinary standard-gauge fixed
special work. It does not use the narrow ghost graph.

## Topology

The diamond has four logical ports (`A0`, `A1`, `B0`, `B1`) and two fixed
routes:

- route A connects `A0` to `A1`
- route B connects `B0` to `B1`

The routes cross geometrically but remain graph-disconnected. There is no
native or generated node at the crossing and no possible A-to-B movement.
Prefer two continuous authored segments across the work. If the source pack
already splits a route at the diamond, any coincident degree-two nodes must
remain distinct per route and must not be merged into a degree-four junction.

For this standard-gauge instance the generated ghost-node count is zero.
Four *ports* must not be confused with four ghost nodes.

## Discovery and authoring

The user's explicit request was to build crossing support from the physical
crossing, so the first implementation follows the useful geometric part of
`C_L_B.DKW`'s KRE model without requiring map-specific IDs. Runtime discovery
scans ordinary standard-gauge segment pairs using XZ bounds before performing
proper polyline intersections. Validation requires:

- two distinct standard-gauge segments
- one proper interior centerline intersection
- no shared endpoint joining the routes at the crossing
- no more than 0.25 m vertical separation
- an acute angle of at least 8 degrees
- angle-derived endpoint lead sufficient for the outer physical rail
  intersections, frogs, and guard rails
- no second crossing on a shared segment inside the same 8 m compound zone

This automatic path intentionally handles isolated fixed diamonds only.
Ambiguous compound crossings are rejected rather than guessed. An explicit
`segmentA`/`segmentB` authoring override remains a possible later addition if a
map needs to opt into or out of a geometrically ambiguous case.

## Geometry and rendering

1. Find the proper centerline intersection in a stable local XZ projection.
2. Derive the two left/right rail centerlines for each standard-gauge route.
3. Detect the four physical rail-pair intersections.
4. Use the earliest/latest rail intersection along each route, plus a measured
   lead margin, to define the special-work window.
5. Suppress or split only the ordinary rendered segment proxies in that
   window. Do not split or reconnect the native train graph.
6. Classify the four physical intersections by their radius from the crossing
   center: the distant pair are inward-facing acute V-frogs and the near pair
   are obtuse/K frogs. At each acute frog, put the point rail on the diamond
   side and the wing rails on the approach side, separated by one railhead plus
   the configured flangeway. Open the rendered acute V by 0.5 degrees relative
   to its theoretical heel-to-intersection chords by setting the nose back on
   the V bisector; keep both measured heel connections fixed. Build each K frog
   from two opposing point rails
   relieved against the measured wheel-flange guides and one outside
   stock/knuckle rail. Join that outside rail's two planned wing spans through
   its outer obtuse piece and extrude the result as one uninterrupted
   three-station mesh: straight outer span, one frog-center kink, straight
   outer span. Do not preserve intermediate spline samples through the knuckle.
   Select one approach check rail per running rail. Derive each of the two
   central K-frog guards by rigidly translating the complete stock/knuckle
   working length across the gauge toward crossing center by
   `Gauge.Standard.Inside - GuardCenterOffset`. Mirror the guard's center point
   across its translated endpoint chord so it kinks by the stock rail's exact
   angle in the opposite signed direction. Extend both ends and use the
   established narrow-gauge check-rail wing geometry on each tip: 0.35 m at 10
   degrees away from the frog line. This yields wing-straight-reverse
   kink-straight-wing construction. Cross-pair the finished guards between the
   two obtuse frogs: the guard shaped from either K stock is translated to the
   other K location. This puts the upper-derived guard at the lower K and the
   lower-derived guard at the upper K, as required by the crossing anatomy.
7. Build replacement ties while retaining the two source roadbeds and
   continuous route colliders.
8. Retain the original continuous segment traversal and block ownership.

The implemented renderer attaches the shared crossing object to the
lexicographically first participating segment. Both segment descriptors still
claim their own rail/tie cuts, but only that deterministic owner emits the
fixed rail pieces, four frog assemblies, guards, and replacement ties. This
prevents descriptor build order from double-rendering the hardware.

The pictured EF&A crossing is shallow enough that the four rail intersections
span a substantial distance along both routes. The render-suppression window
must therefore be derived from the outermost rail intersections plus the
angle-derived frog and guard extents. DKW's fixed 1.5 m proxy margin is not a
safe general boundary for this instance.

The existing `RailIntersectionPrototype` local geometric intersection work is
the preferred foundation. DKW's overall arrangement is useful evidence, but
its decompiled `Intersects` helper should not be copied verbatim because the
existing investigation identified a likely source/decompiler defect.

## Interlocking behavior

The fixed diamond itself has no switch state. EF&A signal or route locking may
treat routes A and B as conflicting, but that conflict belongs in interlocking
logic, not graph connectivity. Adding a shared crossing node would incorrectly
permit turns and would violate the base game's supported node topology.

## Evidence from C_L_B.DKW

`KRESpliney.BuildSpliney` accepts `segmentAId` and `segmentBId`, detects their
centerline crossing, derives four rail intersections, and builds custom
crossing geometry. `CalculateKREProxies` returns front/back render proxies for
both source segments. The descriptor patch substitutes those proxies for
ordinary rendering while leaving the two native routes graph-disconnected.

## Live fallback evidence

The user's `2026-07-31 21:42:21` screenshot shows both complete vanilla rail
and tie meshes drawn through one another. There are no rail cuts, crossing
points, guards, or special-work tie treatment. The immediately preceding
`Player-prev.log` reports that the preset catalog loaded, then lists 13
analyzed special-work objects, none with a crossing preset. This confirms the
failure is the absent segment-pair discovery/render path, not a four-node
topology mistake or a rejected generated plan.

## First custom-render evidence

The user's `2026-07-31 22:16:29` screenshot and matching `Player.log` confirm
that discovery, validation, ownership clipping, and single-owner rendering all
ran for `SCollieDillsboro_7kkq` / `SDillsYard2_uvlz` at 14.79 degrees. The plan
was valid with four physical intersections, four frogs, twelve fixed pieces,
six guard candidates, and no blades.

The visible geometry nevertheless missed the prototype because the renderer
sent all four physical intersections through one isolated generic crossing
builder. It never assigned the required two acute and two obtuse roles.
Additionally, planned obtuse-frog wing spans at this shallow angle are shorter
than the renderer's general 0.35 m rail-piece cutoff, so several required
connections were silently discarded. The first corrective renderer assigned
acute and obtuse roles and retained the required short pieces, closing the
diamond, but its acute direction and frog-head manufacture were still wrong.

## Second custom-render evidence

The user's `2026-07-31 22:31:58` overview confirms that the first role fix
closed the long diamond and emitted both K-frog locations. The matching fresh
log identifies `frog:3` and `frog:0` as the outer acute pair at 5.572 m radius,
and `frog:1` and `frog:2` as the inner K pair at 0.724 m radius. It also confirms
four selected check rails.

The `22:38:46` close-up then identifies the remaining acute error exactly: the
point rail is on the approach side and the wings are on the diamond side. Both
ends must be rotated 180 degrees, so the two acute noses face inward and the
wings face outward. The close-up and the supplied railway diagram also show
that the former 0.10 m wing offset allowed the railheads to touch; it did not
include the 0.05 m wheel-flange slot. The corrected acute renderer uses the
measured `RailHeadWidth + FlangewayWidth` separation.

## Third custom-render evidence

Fresh `[DiamondAcuteFrog]` diagnostics and the user's `22:46:37` close-up show
the acute orientation correction loaded. The `22:49:14` K-frog close-up shows
that the two opposing tapered point rails are now correct, but also isolates a
new construction error: the outside stock/knuckle rail appears as two cut
halves. The user's `22:48:37` prototype image shows the required result—those
halves are one continuous bent rail around the two point tips. The same live
log reports six derived guard candidates but only four selected, matching the
user's observation that the two central K-frog guards are absent.

The corrected K renderer keeps only the two enclosed-diamond halves as relieved
point rails. It chooses the outward one of the compiler's two obtuse pieces,
matches its endpoints to the two measured wing spans, and retains only their
two outside endpoints plus the obtuse piece's center knuckle. It recalculates
one miter frame there and extrudes the resulting straight-kink-straight stock
rail once. Guard selection still keeps the four farthest approach checks. Each
central K guard translates that complete working length across the standard
gauge, mirrors only the center knuckle, extends 0.9 m past both stock ends, and
flares the outer 0.35 m of each extension by 10 degrees. Thus it is detached
from the frog rails, longer than the stock working section, winged at both
ends, and has an equal-magnitude/opposite-direction center kink.

## Acute-angle calibration evidence

The user's subsequent end-on overview reports that the two acute V castings are
still approximately 0.5 degrees too tight. The existing mesh uses the measured
heel points and the theoretical physical-rail intersection as its three V
vertices. For a shallow crossing, changing the heel locations would break the
connections to the fixed running rails. The renderer therefore preserves both
heels and moves only the point nose along their angle bisector until the
horizontal included angle is exactly `source + 0.5 degrees`. This is a diamond-only mesh
correction; ordinary turnout V frogs retain their original construction. A
`[VeeFrogAngle]` diagnostic records source angle, corrected target angle, and
the resulting nose setback for each acute frog. The first live solve in world
coordinates achieved only +0.482/+0.486 degrees because the large map
coordinates exhausted float precision. The current implementation subtracts
crossing home before solving and building the V mesh, preserving the requested
exact +0.500-degree delta in a small local coordinate frame.

## Paired K-guard evidence

The user's `23:22:26` full-restart screenshot proves the five-station guards,
0.9 m extensions, and two 0.35 m/10-degree wings loaded, but the two complete
guard curves are assigned to the wrong obtuse-frog locations: the upper guard
belongs at the lower K and the lower guard belongs at the upper K. The fresh
log simultaneously proves all other calibration inputs loaded:
`guardStations=5`, `guardLength=3.043/3.056 m`, `guardOffset=1.309 m`, and two
selected K guards.

The renderer now treats the obtuse frogs as an explicit pair. It constructs
each guard in crossing-local coordinates and then translates the finished
curve by the vector from its source obtuse intersection to the paired obtuse
intersection. Keeping the output curve local to the crossing also removes the
large-world float loss which left the nominally reflected kinks a few
hundredths of a degree short.

## Open disagreements

(none)
