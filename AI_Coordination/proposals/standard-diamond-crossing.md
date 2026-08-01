Status: Draft

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

## Authoring

Follow the useful part of `C_L_B.DKW`'s KRE model: explicitly identify the two
participating segments, then measure their intersection. Do not scan every
segment pair and automatically convert every geometric crossing; that would
misclassify grade-separated or intentionally untreated overlaps.

The current `narrowGauge.specialWork` binding only accepts `anchorNode`, so it
cannot describe a segment pair. Extend the backward-compatible schema with a
crossing-specific pair such as `segmentA` and `segmentB` (final names to be
agreed during implementation). Validation should require:

- two distinct standard-gauge segments
- one proper interior centerline intersection
- acceptable vertical separation
- a non-parallel crossing angle within supported numerical limits

## Geometry and rendering

1. Find the proper centerline intersection in a stable local XZ projection.
2. Derive the two left/right rail centerlines for each standard-gauge route.
3. Detect the four physical rail-pair intersections.
4. Use the earliest/latest rail intersection along each route, plus a measured
   lead margin, to define the special-work window.
5. Suppress or split only the ordinary rendered segment proxies in that
   window. Do not split or reconnect the native train graph.
6. Build the four crossing-frog assemblies, wing/point continuations, derived
   guard rails, ties, ballast mask, and any required render colliders.
7. Retain the original continuous segment traversal and block ownership.

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

## Open disagreements

(none)
