# Authored dual + standard + narrow join review (7n90 / Nove)

## User evidence

The user identified `NCustom_7n90` as a three-leg gauge join, not a
conventional two-route narrow turnout:

- dual gauge enters the junction;
- standard gauge continues through;
- narrow gauge joins/diverges;
- only the shared rail has a movable blade;
- the runtime `:control` node is an extra, non-physical leg.

The July 10 screenshot shows large gaps and two apparent narrow point routes.
Those symptoms agree with the exported plan: it currently contains
`narrow-normal` and `narrow-reversed`, including a 0.400 m closure, even though
the authored junction has no second narrow route.

## Authored topology evidence

`EFA Track Pack/game-graph.json` has exactly three real segments at
`NCustom_7n90`:

- `SCustom_rhb0`: `DualGauge_L`, `NCustom_8uxw -> NCustom_7n90`;
- `SCustom_194b`: standard, `NCustom_7n90 -> N8hq`;
- `SCustom_b4hq`: `Narrow`, `NCustom_7n90 -> NCustom_sog7`.

`Nove` has the same physical signature (`SCustom_rhxm` dual, `Stjh` standard,
`SCustom_epu2` narrow). `N178`, `NCustom_g832`, and `NCustom_vdlt` instead have
two dual legs plus one narrow leg, so they are outside this correction.

## Root cause

`SpecialWorkTopologySynchronizer.FindImplicitNarrowBranchSourceNodeId`
recognizes both `2 dual + 1 narrow` and `1 dual + 1 standard + 1 narrow` as the
same narrow-branch topology. It rewires the real narrow segment from the
authored node to the generated dual-gauge ghost node. The source and ghost
then each have only two legs, so `EnsureRuntimeGaugeSeparationControls` adds a
five-metre hidden control leg to make the ghost look like a binary switch.

`SpecialWorkRuntimeDiscovery` sees that artificial three-leg ghost as
`dual.narrow-branch-joins-main` and produces two narrow logical routes. The
second narrow route, its second blade/closure, and its associated cuts are all
artifacts of the synthetic control leg.

## Reviewed correction

Treat the exact authored `1 dual + 1 standard + 1 narrow` signature as
`dual.split-standard-narrow` at the authored node:

1. Keep (or restore) the real narrow segment on the authored node.
2. Do not create a hidden control node/segment for this signature.
3. Discover two real logical routes only:
   - `standard-through`: authored dual + standard;
   - the narrow join: generated narrow centerline of the dual leg + authored
     narrow leg, connected by the shadow graph's lateral gauge-transition
     curve (the established internal id remains `narrow-diverge` only for
     compatibility with the split hardware catalogs).
4. Bind the single measured `narrow-separation` blade to the real authored
   switch node and to the native state that contains the narrow leg.
5. Change the dual-split preset's logical-route count from three to two.

The correction is anatomy-scoped rather than node-id-scoped. Conventional
`2 dual + 1 narrow` branch switches retain their existing topology and blade
logic.

## Verification target

After a full Railroader restart, `NCustom_7n90` and `Nove` should have:

- no `fuse-ng:*:control` creation log;
- preset `dual.split-standard-narrow`;
- two wheel paths (`standard-through` plus the narrow join);
- exactly one measured blade;
- no 0.400 m fake narrow closure or control-leg rail;
- continuous standard-through and narrow-to-dual running rails, with cuts
  limited to the real shared-rail blade/frog anatomy.

## Implementation and build

Implemented the reviewed correction in:

- `SpecialWorkTopologySynchronizer.cs`: fresh authored joins are excluded
  from implicit narrow-branch rewiring; an already-rewired join is restored
  and its obsolete runtime control is removed;
- `SpecialWorkRuntimeDiscovery.cs`: the exact authored signature is compiled
  at the source node into the two real routes, with the narrow route assembled
  from the full dual third-rail approach, the shadow graph's lateral
  dual-to-narrow transition, and the full narrow departure, then assigned to
  its decoded native switch state;
- `SpecialWorkPresetCatalog.cs`: dual split expects two logical routes.

Build and deploy succeeded with 0 warnings and 0 errors. Deployed DLL:

- timestamp: `2026-07-10 07:30:48`;
- size: `753,152` bytes;
- SHA-256: `9989517E82FB2FA5C8906257E44FCF63FA82D609BEA5C7177778B38B86391400`.

No Railroader process was launched or controlled. Live verification remains
manual and requires a full process restart.

### First live rejection and correction

Screenshot `072150` showed only the fallback standard-switch visual. The fresh
log proved the first implementation never produced a plan: the authored source
node and generated narrow node are intentionally separated by 0.260 m, so the
0.250 m "co-located" splice gate rejected both 7n90 and Nove (`objects=12`).
That offset is the dual gauge's third-rail centerline offset, not an error.

The deployed 07:30 correction replaces the straight endpoint splice with the
existing `ShadowNarrowGaugeTransition`: full offset dual approach -> measured
lateral transition -> full narrow branch. The next restart should restore the
two missing analyses and prevent the builder's standard-switch fallback.

### Second live rejection and renderer-dispatch correction

The 07:30 build restored both analyses (`objects=14`). 7n90 produced a valid
two-route/one-blade plan (`rails=4`, `blades=1`), but screenshot `073443` was
still the standard-switch fallback. The fresh log isolated the remaining
failure to renderer dispatch: `[Build] Switch 'NCustom_7n90' connects mixed
gauge segments; leaving its visuals standard.`

The base `SwitchDescriptor` exposes the two selectable legs, not necessarily
the enter leg. For 7n90/Nove those proxies are standard+narrow; the dual leg is
the omitted enter segment. The old dispatch only allowed measured ownership
when one exposed proxy itself was dual, so it ignored the valid plan and left
the ordinary-segment ownership cuts unfilled.

The 07:39 build now sends every validated measured special-work switch through
the special-work builder, regardless of the descriptor's two-proxy gauge mix.
This matches the proven N178/vdlt ownership model while retaining 7n90/Nove's
two-route/one-blade topology difference. Deployed DLL:

- timestamp: `2026-07-10 07:39:08`;
- size: `753,152` bytes;
- SHA-256: `A32D8D019F4308F87F687C346CFD39316F891B36BFA9A6B51435A5890569BBC0`.

### Third live correction: toe alignment, blade length, and upper frog path

The 07:39 renderer fix successfully displayed 7n90's measured plan. Screenshot
`074646` then exposed the real geometry: no usable point by the stand, a kink
through the toe, and only a tiny detached blade. Screenshot `074847` showed a
second large gap at the upper frog/work boundary.

Fresh diagnostics proved the blade was only 0.500 m:
`movable=standard-through:left`, `stock=narrow-diverge:left`,
`40.340-40.840`. The intended narrow movable candidate failed because the
symmetrical shadow preview put it 0.131 m from the stock rail at the toe; the
tolerance is 0.121 m. The inverse standard candidate barely passed and reached
root separation after only 0.5 m. Its route also generated the wrong frog/work
boundaries upstream.

The 07:54 correction keeps the narrow centerline on the dual third-rail offset
all the way to the switch toe (so its shared rail closes exactly to the
standard shared rail), then builds an asymmetric seven-metre Bézier transition
solely on the narrow-branch side. This follows the N178/vdlt throat model,
selects the real narrow shared-rail blade, removes the midpoint kink, and
recomputes the upper frogs/cuts from the corrected physical path.

Deployed DLL:

- timestamp: `2026-07-10 07:54:50`;
- size: `752,128` bytes;
- SHA-256: `6CDDA8B6B987334A7720B968B3230B084974B4CB71CA195E3726C13672137494`.

### Fourth live correction: move the throat up without moving the incoming seam

Screenshots `075934`/`075938` showed the corrected blade but established that
the physical throat still began too low: its bend occurred before the upper
handoff, leaving a full-width gap above. The user correctly identified that
the switch development needed to move up to that seam.

The incoming dual ownership math rules out translating the rendered root: its
ordinary/measured intervals already overlap by about 0.04 m. A rigid shift
would merely move the gap to that end. Instead, the 08:07 build retains a 2 m
straight shared-rail lead through the stand, then starts the existing 7 m
gauge-separation transition. This shifts the blade/frog throat two metres up
the outgoing side as one geometric unit while preserving the incoming seam.

Deployed DLL: timestamp `2026-07-10 08:07:01`, size 752,640 bytes, SHA-256
`E0007807E55CFAD0B916C50065CC3C3735ED427F9E60D7001068797FE035C74B`.

### Fifth live correction: continuous development curve and outgoing-tail ownership

Screenshots `091750`/`091833` rejected the 08:07 interpretation. The two-metre
straight lead did not close the remote gap; it split the same nine-metre
centerline handoff into a straight plus cubic and left a visible S-shaped
curvature break. The fresh plan log also locates the final V frog near route
distance 61 m while the generic work interval ends at the last event plus
three metres. Both authored outgoing legs continue beyond that boundary.
Because this mixed standard/narrow `SwitchDescriptor` has all vanilla leg
meshes suppressed when its valid measured plan renders, no ordinary mesh
fills the remainder behind the V frog.

The 09:40 correction therefore:

- replaces the two-metre lead plus seven-metre curve with one nine-metre
  tangent-matched cubic from the authored toe to the narrow branch;
- uses one-third-chord control handles instead of the overdriven half-chord
  handles and rejects backward endpoint tangents;
- extends only an exact two-route `dual.split-standard-narrow` plan's work
  intervals through every outgoing rail tail, meeting the next authored
  descriptors at their real endpoints;
- logs `[DualSplitWorkInterval]` and the stored/chord tangent alignment for
  full-restart verification.

Build/deploy succeeded with 0 warnings and 0 errors. Deployed DLL timestamp
`2026-07-10 09:40:33`, size 753,664 bytes, SHA-256
`501B69A4285CC99C9504DD1E0F4344E7A01BB483A056C37067FBC3B38E72AF34`.
The next full restart must confirm a smooth join, no post-Vee gaps on either
outgoing route, one blade, and a valid measured plan for both 7n90 and Nove.

### Nove mirrored live rejection: dual approach centerline loses its offset

The user accepted 7n90 after the 09:40 restart, then supplied screenshot
`094757` showing Nove still has an empty throat and no blade. The same fresh
log proves this is not the post-Vee work-window bug: Nove is valid and both
outgoing tails extend, but it derives `blades=0`. Every same-side candidate
fails at the authored node; the closest is
`standard-through:right / narrow-diverge:right` at 0.271 m separation. That
value is the dual-to-narrow centerline offset, so Nove's narrow approach is
arriving on the standard centerline instead of the generated third-rail
centerline.

The authored mirror explains the asymmetry. Both source segments are
`DualGauge_L`, but 7n90's dual segment ends at its junction while Nove's dual
segment starts at its junction. The shadow transition reverses Nove's anchor
with a plain `LineCurve.Reverse()`; fresh tangent logging independently shows
that stored direction facing backward (`storedStartDot=-1.000`). The reviewed
correction is to take the approach from the already-generated `ghostDual`
segment, oriented toward its generated ghost node with the direction-safe
runtime helper. That curve is the authoritative narrow centerline of the dual
leg and preserves the physical offset regardless of authored traversal. The
existing nine-metre connection to the real narrow departure remains unchanged.

Implemented that reviewed correction and added `toeCenterOffset` to the join
diagnostic. Build/deploy succeeded with 0 warnings and 0 errors. Deployed DLL
timestamp `2026-07-10 10:03:46`, size 754,176 bytes, SHA-256
`036B9B1036DCB2FC58DF154677F3F1D3A56D6B1B406E95B41C864A147832E925`.
Full-restart targets: Nove and 7n90 both log a toe offset near 0.260 m; Nove
derives one blade and fills its throat; accepted 7n90 remains unchanged.

### Nove frog rejection after the offset fix

Screenshots `100824`/`100841` confirm the ghost-centerline correction filled
the throat and restored Nove's right-side blade. The fresh plan is valid with
`blades=1`, and the join itself is tangent-aligned (`chordStartDot=0.999`,
`chordEndDot=1.000`). The remaining visible S/straight interruption coincides
with a catalog-forced hardware change, not the nine-metre join curve:

`intersection:1 standard-through:left/narrow-diverge:left:
CrossingFrogCandidate => VeeFrogCandidate`.

The physical classifier already identified the double/crossing frog the user
expects. `ApplyNoveSplitFrogCatalog` then overwrites every standard-through x
narrow-diverge intersection as V, including that crossing, while the separate
left/right rail intersection is already a legitimate V. The reviewed fix is
to preserve the geometry-derived kind for standard-through x narrow-diverge
pairs. Nove therefore retains its real V but renders the same-side crossing as
a double frog, eliminating the forced V nose that interrupts the curved rail.

### Functional topology correction: the narrow leg belongs on the ghost node

The user then identified the deeper functional failure: 7n90 and Nove's real
narrow branches are attached to the authored standard node, while narrow
trains travel on the generated ghost graph. A train reaching
`fuse-ng:n:NCustom_7n90` or `fuse-ng:n:Nove` therefore sees the graph end and
cannot traverse the switch. This also explains the remaining artificial
straight section: the real narrow curve is being forced to start from the
standard center instead of its 0.260 m offset node.

This supersedes the original review's instruction to keep the real narrow leg
on the authored node. The correct runtime anatomy is:

1. authored source node: real dual leg + standard-only leg;
2. generated ghost node: generated narrow centerline of the dual leg + real
   narrow-only branch + one hidden control leg;
3. the hidden leg represents the blade's blocked/non-narrow alignment for the
   native graph switch, but it is not a physical rail or a second route;
4. measured geometry still compiles only two real wheel paths:
   `standard-through` and `narrow-diverge` (`ghostDual ↔ narrowBranch`);
5. the single blade is bound to the ghost switch's actual state, allowing
   narrow trains to route onto the real branch.

The implementation must therefore rewire the narrow branch to the ghost node,
restore the topology-only hidden control, prefer gauge-separation discovery
before generic narrow-branch discovery, omit the hidden route from the measured
definition, and suppress the hidden segment's own mesh. This preserves the
working one-blade renderer while making the train graph continuous and lets the
authored narrow curve start at the correct offset node.

Implemented the topology correction together with the pending Nove frog fix:

- exact authored dual + standard + narrow signatures are tagged as
  `dual.split-standard-narrow` and their real narrow leg is rewired to the
  generated ghost node;
- the topology-only hidden control is recreated so the native graph has a
  switchable blocked/open state;
- gauge-separation discovery now runs before the generic ghost narrow-branch
  detector and compiles only `standard-through` plus the real
  `ghostDual ↔ narrowBranch` route;
- the narrow route is bound to whichever real ghost-switch state contains the
  branch;
- hidden control segment meshes are suppressed;
- Nove's geometry-derived crossing kind is preserved instead of being forced
  to V.

Build/deploy succeeded with 0 warnings and 0 errors. Deployed DLL timestamp
`2026-07-10 10:39:35`, size 755,712 bytes, SHA-256
`09882AF110F8D4B0F9678EB43E3BF207935129E614E31361193748C159C54230`.
Full-restart verification must confirm both rewires, both ghost controls,
two-route/one-blade valid plans, no visible hidden stub, correct Nove double
frog, and successful narrow-train routing through both ghost switches.

### Post-topology live cleanup: 7n90 missing V and Nove reversed standard route

The first graph-connected restart succeeded functionally and visually except
for two isolated defects:

- the user confirms 7n90 is missing only its upper V frog; its fresh plan has
  one accepted crossing frog (`standard-through:right × narrow-diverge:right`)
  and no V;
- Nove's frog cluster is correct, but its right standard rail has a large gap
  before the stand. Its log projects the same frog events around 25-33 m on
  the standard route and 68-75 m on the narrow route, and it loses its blade.

`TryBuildGaugeSeparation` currently passes `standardMain[0]` and
`standardMain[1]` to `TryBuildFixedRoute`. Collection order differs between
the mirrors, so Nove's standard centerline is traversed opposite its narrow
centerline. The resulting work/ownership interval is cut in the wrong portion
of the approach. The reviewed correction explicitly identifies the real dual
and standard-only segments and always builds `dual → source → standard`, the
same direction as `ghostDual → ghost → narrowBranch`.

For 7n90, the physical missing V is the opposite narrow-side pair from its
accepted crossing: `standard-through:right × narrow-diverge:left`, farther
from the blade. Add a geometry-derived V only when a two-route dual split has
an accepted crossing but no V. Nove already has a V, so this supplement is a
no-op there. This is anatomy-scoped and leaves both accepted crossing frogs,
topology, and blade logic unchanged.

Implemented both corrections. Gauge-separation discovery now identifies the
real dual and standard-only source segments explicitly and always assembles
the standard route as `dual -> source -> standard-only`; mirror-dependent
connected-segment collection order can no longer reverse Nove's ownership
interval. The sectioned builder now supplements a missing V only for a dual
split which already has an accepted cross-family crossing but no accepted V.
It uses the same standard rail as that crossing and the opposite side of the
`narrow-diverge` route, derives the crossing point/tangents from geometry, and
orients the V nose toward the one real blade. Since Nove already has its V,
the supplement does nothing there.

Build/deploy succeeded with 0 warnings and 0 errors. Built and deployed DLLs
both have timestamp `2026-07-10 11:05:30`, size 757,248 bytes, SHA-256
`540FC24200E53FBDEE3437174FC6C197975D7FF5858C7537781C7A2AF5D609AC`.
No game process was launched or controlled. A full restart must verify that
Nove's right standard approach is continuous and retains its double frog, and
that 7n90 now has its crossing plus the formerly missing V.
