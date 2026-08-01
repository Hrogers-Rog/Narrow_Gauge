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
6. Build the four crossing-frog assemblies, wing/point continuations, derived
   guard rails, and replacement ties. Retain the two source roadbeds and
   continuous route colliders.
7. Retain the original continuous segment traversal and block ownership.

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

## Open disagreements

(none)
