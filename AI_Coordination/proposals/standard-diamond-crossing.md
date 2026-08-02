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
   side and the wing rails on the approach side. The source rails have already
   exchanged sides where they intersect at the acute point, so terminate each
   wing beyond the opposite frog heel, continuing on the same outside side from
   which that source wing approaches. Do not move either target back between
   the heels; that exchanges the source sides a second time and crosses the two
   wing rails. Match the K guards' flangeway: 0.076 m railhead plus 0.050 m clear
   opening, or 0.126 m between rendered profile centers. Construct the complete
   working wing as one straight rendered-profile line parallel to its frog leg.
   Extend the incoming source rail until its rendered profile intersects that
   flange line; that solved intersection, not a fixed 0.45 m cutoff, is the hard
   bend. Give the outgoing bend station and blunt endpoint the same flange-line
   frame so moving the far endpoint cannot bow or skew the wing. Open
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
wings face outward.

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

## Acute-frog flangeway calibration

The decompiled base-game `Track/SwitchGeometry.cs` uses 0.100 m between its
switch frog and wing curve points. That observation was initially applied to
the diamond as a 0.100 m rendered-profile separation, leaving only 0.024 m
clear between 0.076 m railheads. The user's `09:36` prototype reference and
explicit correction establish a different requirement for this fixed diamond:
the V-wing slot must be identical to its guards' 0.050 m clear flangeway.

The diamond therefore uses `RailHeadWidth + FlangewayWidth` = 0.126 m between
the final rendered profile centers. The generic `CreateVeeFrogAssembly`
default remains the base-game 0.100 m, so ordinary switch and compound paths
do not change. `[DiamondAcuteFrog]` and `[VeeWingGap]` report
`wingSeparation/profileSeparation=0.126` and `visibleFlangeway=0.050` for
restart verification.

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

The first attempted correction moved each frog heel center to compensate for
the profile-frame difference. The user's `09:39` close-up falsified that
approach: it merely converted the frame discrepancy into a visible centerline
step at the stock-rail handoff. The corrected renderer keeps both frog heel
points exactly coincident with their stock-rail centerlines. The first
post-mesh correction rotated the frog's terminal rings and caps into the
calculated stock frames. The user's `10:27` close-up showed that this reduced
but did not eliminate the small lateral step: reproducing the stock builder's
frame was still only an approximation of its finished terminal cross-section.

The final diamond-only pass builds a temporary two-station stock rail through
each heel with the same `BuildStockRailMesh` path used by the adjoining fixed
rail. It copies that mesh's exact heel profile ring, end-cap vertices, and
normals onto the frog terminal. The frog center points and nose ring remain
untouched, so the exact source +0.500-degree V opening is preserved. The
user's fresh `10:45` screenshot shows the lateral railhead step removed; the
remaining thin horizontal mark is the boundary between the separate frog and
stock end caps. Generic/compound V frogs do not opt into this profile copy.

For each diamond wing, the renderer computes the frog and source railhead
profile centers and targets 0.126 m rendered-profile separation, leaving the
same 0.050 m visible flangeway as the guards. The first side correction placed
that target from the opposite frog heel back toward the source heel. The user's
fresh `09:53` screenshot proves this was a crossed assignment: because the two
source rails already exchange sides at the acute intersection, moving both
targets between the heels makes them exchange sides again. The corrected target
starts at the opposite frog profile and continues away from the source profile,
preserving the wing's outside approach side without changing its calibrated
flangeway. Runtime evidence is exposed as `[VeeFrogHeelAlignment]
centerShift=0`, `[VeeWingGap] side=outside`, and the reported rendered
separation/clearance.

## Straight V-wing construction

The user's `10:03/10:04` close-ups identify the remaining wing error more
precisely than the earlier overview. The non-crossing target and final heel gap
are correct, but the renderer still cuts the incoming source rail at a fixed
0.45 m setback and moves only the far endpoint of the wing. That makes the
working wing an arbitrary chord from the fixed cutoff to the corrected heel;
it is neither a true straight flange path nor parallel to the frog leg. It also
leaves the incoming portion too short, so the paired throats appear too far
apart.

The diamond-only path now defines an infinite rendered-profile flange line
through the 0.126 m heel target, parallel to the opposite frog leg. It samples
the incoming source rail's rendered profile and bisects the signed line-side
root to find their actual intersection. The source slice extends to that solved
distance. At the bend, one incoming frame ends the source rail and one outgoing
frame begins the wing at the same rendered profile center; the blunt endpoint
uses that identical outgoing frame. Thus the entire working wing is one exact
straight line along the flangeway, while the leading rail reaches all the way to
its real bend. Generic V paths retain the original fixed-setback construction.
`[VeeWingGap]` now also reports `bendSetback`, `straightWing=1`, and
`straightError`, expected to be 0.0000 m.

The user accepted this construction as perfect after a full restart. Fresh
runtime diagnostics measured `bendSetback=0.196-0.199m`, retained
`profileSeparation=0.126m` / `visibleFlangeway=0.050m`, and reported
`straightError=0.0000-0.0004m` across all four wings. These values are now the
accepted baseline and must not be disturbed by heel-seam work.

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

## Fixed-running ownership seam

The user's `11:23` debug-labeled close-up corrects the preceding seam
diagnosis. The remaining mismatch is not at a V-frog heel. It is the outer
handoff from custom `FixedRunning crossing-b:right` to the normal stock rail
that continues beyond the diamond ownership window. Frog, wing, and K-guard
geometry must remain unchanged while this boundary is corrected.

The two rails currently originate from different approximations of the same
authored Bezier. Automatic crossing discovery calls
`OrientedSegmentCurve`, which approximates the world-space segment with
`Approximate(1.000005, 0.25, 16, 20)`. The base-game segment renderer first
subtracts `EndPoint1`, then calls `SwitchGeometry.MakeTrackLineSegments`,
whose approximation is `Approximate(1.000005, 0.5, 16, 40)` in that local
coordinate frame. Their parallel offsets therefore do not have identical
positions and frames at the ownership overlap, particularly at the pictured
roughly 21.7-km world coordinate.

For automatically discovered standard diamonds, the route centerline must be
the exact render-space approximation: subtract `EndPoint1`, use the base-game
`0.5/16/40` sampling, restore the world offset, and only then orient the point
order from segment A to B while rotating each exact reversed frame by a local
180-degree yaw. This makes the
compiler's `crossing-a/b:left/right` rails and the normal segment's L/R rails
come from the same source stations. It is a general fixed-diamond correction;
other authored special-work discovery retains its existing finer analysis
curve.

The user's visual check of the fresh `11:46` editor build confirmed that this
fix aligns `FixedRunning crossing-b:right` with its connecting stock rail.
The render-matched handoff is accepted.

## Open disagreements

(none)
