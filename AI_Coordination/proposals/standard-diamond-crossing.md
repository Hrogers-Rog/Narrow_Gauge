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
   side and the wing rails on the approach side, separated by the base game's
   0.100 m switch-frog center offset. With the 0.076 m standard railhead this
   leaves the same approximately 0.024 m visible slot as a native switch. Open
   the rendered acute V by 0.5 degrees relative
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
   kink-straight-wing construction. Both guards remain centered in their native
   K-frog locations. Cross-pair only their SHAPES: recenter the guard derived
   from either K stock on the other guard's native center station. This retains
   both correct locations while making the two knuckles and end wings face
   inward toward one another.
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
wings face outward. The first correction also widened the center offset from
0.100 m to `RailHeadWidth + FlangewayWidth` = 0.126 m. Later direct comparison
with a native switch and the decompiled base-game construction proves that
widening was incorrect; the base game itself uses 0.100 m.

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

## Base-game acute-frog flangeway calibration

The user's current close-up shows the clearance between each acute V point and
its wing rail is visibly wider than on a base-game switch. The decompiled
`Track/SwitchGeometry.cs` supplies the authoritative dimension: its
`FlangewayWidth` constant is 0.100 m, and each switch wing/point curve appends
its frog-end station exactly 0.100 m laterally from the corresponding frog
point. This is a rail-center separation, not a 0.100 m clear opening.

`CreateVeeFrogAssembly` was already modeled on that algorithm and defaults to
0.100 m. Only `CreateDiamondAcuteFrogAssembly` overrode it with 0.126 m
(`RailHeadWidth + FlangewayWidth`). The diamond now passes the same explicit
0.100 m center separation as `SwitchGeometry`. With the standard 0.076 m
railhead, the expected visible edge clearance is approximately 0.024 m. Frog
orientation, the exact +0.500-degree V opening, wing hard-kink frames, and all
K-frog geometry remain unchanged. `[DiamondAcuteFrog]` reports
`wingSeparation=0.100 visibleFlangeway=0.024` for restart verification.

## Acute V-frog heel seam diagnosis

The user's post-restart `08:43/08:44` close-ups show a very small lateral step
where the V-frog casting meets its adjoining ordinary rail. This is not the
wing flangeway and is not a center-point gap. Both pieces take their endpoint
position from the same source rail at `intersectionDistance +/-
frog.CutHalfLength`.

The mismatch is in the mesh frame at that shared position. Decompiled
`TrackMeshBuilder.BuildFrogMesh` ignores the supplied heel `LinePoint.Rotation`
and reconstructs it with `LookRotation(renderNose - heel)`. The adjoining
`BuildStockRailMesh` instead extrudes with the measured source-curve rotation.
The diamond also moves `renderNose` to open the V by 0.500 degrees, so its
heel-to-nose chord is intentionally not identical to the original rail tangent.
`MakeRailOnlyProfile` offsets the asymmetric rail profile by half the 0.076 m
railhead along each frame's local X. Therefore even coincident center points
render slightly different railhead centers when those two frames differ,
producing the photographed step.

Changing the supplied frog heel rotations cannot fix this because
`BuildFrogMesh` discards them. The current diamond-only renderer therefore
reproduces `BuildFrogMesh`'s winding and endpoint-frame calculation, then
iteratively shifts each render-only heel center so the frog profile center
lands exactly on the adjoining stock rail's rendered profile center. It solves
that compensation together with the nose setback so the final mesh retains the
exact source +0.500-degree included angle. Generic/compound V frogs do not opt
into this compensation.

The same profile-side error explained why a 0.100 m CURVE-point offset still
looked wider than a base-game switch. For each diamond wing, the renderer now
computes the frog and source railhead profile centers, sets the desired wing
profile center exactly 0.100 m beyond the frog profile center, and iterates the
wing endpoint center/frame until `ProfileCenter` reaches that target. It uses
`ReheadRenderFrame` from the wing's measured frame so reversed curves preserve
their profile side. The resulting visible edge clearance is 0.100 - 0.076 =
0.024 m. Runtime evidence is exposed as `[VeeFrogHeelAlignment]` and
`[VeeWingGap] profileSeparation=0.100m visibleFlangeway=0.024m`.

## Paired K-guard evidence

The user's `23:22:26` full-restart screenshot proves the five-station guards,
0.9 m extensions, and two 0.35 m/10-degree wings loaded, but the two complete
guard curves are assigned to the wrong obtuse-frog locations: the upper guard
belongs at the lower K and the lower guard belongs at the upper K. The fresh
log simultaneously proves all other calibration inputs loaded:
`guardStations=5`, `guardLength=3.043/3.056 m`, `guardOffset=1.309 m`, and two
selected K guards.

The first attempted pair correction translated each complete guard by the
vector between the two obtuse intersections. The user's `23:33:15` screenshot
proves that was wrong: it placed both guards entirely outside the trackwork.
The guards must remain in the middle of their respective K frogs and face
inward toward each other.

The corrected renderer constructs both native guards in crossing-local
coordinates, uses each native center station as an immutable target anchor,
and translates only the paired guard SHAPE onto that anchor. Therefore the
upper-derived shape occupies the lower guard's centered position and the
lower-derived shape occupies the upper guard's centered position without the
extra two-gauge displacement. Keeping the output local also removes the
large-world float loss in the reflected kink. Because the two measured K
angles differ slightly, the recentered shape retains its paired inward-facing
sign but is recalibrated to the target stock's native kink magnitude.

## K-guard flangeway calibration

The user's next restart accepted the centered inward-facing guards but reported
that their gap to the frog rail remained slightly too large. The intended
narrow K-frog construction is `RailHeadWidth + FlangewayWidth`: 0.076 + 0.050
= 0.126 m between rail centers, leaving 0.050 m clear between railhead edges.

The first calibration attempt fitted the guard center to the nearer complete
route-rail centerline. Fresh runtime evidence falsifies that method. It measured
the accepted guard at 1.164/1.162 m from those rails and moved it to 0.126 m,
a roughly 1.04 m translation through the frog. The resulting
`guardShapeShift=1.157m` explains why the user saw the accepted guards turned
back around. Neither complete route curve represents the generated K point-rail
profile at the working flangeway.

That fitter and its `guardCenterBefore`/`guardCenter`/`guardClear` diagnostics
are removed. The exact pre-fit centered cross-paired guard curves are restored,
with their 1.309 m native offset and inward-facing wing geometry unchanged.
The 0.076 + 0.050 = 0.126 m nominal rule remains authoritative. A future visual
clearance adjustment must reference the generated point rail and rendered
railhead profile, then apply only a small rigid lateral translation to the
accepted guard; it must never select a full route centerline or derive a new
guard direction.

## Hard K-stock knuckle rendering

The user's `23:49:32` close-up shows the outside K stock visually easing into
its center angle instead of remaining straight on both sides. The logical
stock curve is already correct at three stations. The remaining smoothing is
caused by giving the single center extrusion ring an averaged miter rotation:
the stock-mesh normals interpolate from each endpoint to that averaged frame,
so the highlight and railhead profile appear gradually curved.

The render-only curve now contains four frames while retaining the same three
positions: start/incoming, center/incoming, center/outgoing, end/outgoing. The
two center frames are coincident, so they add no length or intermediate bend.
They make the first extrusion span use only its incoming orientation and the
second use only its outgoing orientation, with the zero-length center bridge
forming the one hard angle. The guard compiler continues to consume the
original three-station logical stock curve.

## Open disagreements

(none)
