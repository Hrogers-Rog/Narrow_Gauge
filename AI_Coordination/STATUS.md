# Coordination Status

Last updated by: Claude - 2026-07-10 (Nove camera-angle culling: bounds diagnostic deployed; earlier: 7n90 chord tangents + vee wing kink-frame fix)

## UPDATE: bounds ruled out (0 anomalies on a fresh run); occlusion-culling opt-out deployed

`[MeshBoundsAnomaly]` stayed silent on a fresh restart with the diagnostic
confirmed present in the running DLL - every special-work rail mesh has
healthy local bounds, so mesh-bounds culling is ruled out by evidence. Next
suspect with all facts fitting (angle-dependent, whole cluster, ties immune
via instanced draw): Unity dynamic-occlusion culling against the map's
baked occlusion data, which was baked without this mod-added track. The
decompile shows the base game never sets `allowOcclusionWhenDynamic`
anywhere, so all runtime rails default to occlusion-enabled; vanilla rails
follow baked corridors, ours do not. Deployed the probe-that-is-the-fix:
`CreateMeshObject` wrapper now sets `allowOcclusionWhenDynamic = false` on
every special-work MeshRenderer (one line, reversible; bounds diagnostic
left in). If Nove still flickers after this, the next suspect is the
game's own descriptor-level object toggling - note Nove's rails hang under
the GHOST node descriptor (`switch-fuse-ng:n:Nove`, source
`BuildDualGaugeSwitch`) unlike 7n90's (authored-node descriptor) - a real
asymmetry worth chasing only if occlusion is disproven.

## Superseded below (bounds hypothesis - disproven by the 0-anomaly run)

## Open (Claude): Nove's special-work rails frustum-cull as one cluster at certain camera angles

User screenshots at Nove: ALL rendered rails vanish at some camera angles
(ties remain - they draw via the instanced drawer, not per-renderer bounds)
and reappear on camera move. Pattern matches per-renderer frustum culling
with bounds that do not cover the visible geometry: rails disappear when
the node/switchHome area leaves the frame - consistent with mesh bounds
collapsed near the local origin instead of spanning the pieces.

Static reading exhausted cleanly: base `TrackMeshBuilder.ExtrudePoints`
DOES RecalculateBounds (single site, all rail meshes flow through it), our
vertex-mutating paths (RemoveRailEndCap, flangeway clipping) all
RecalculateBounds afterward. Per the standing evidence rule, deployed an
anomaly-only diagnostic instead of a guessed fix:
`NarrowGaugeTrackBuilder.CreateMeshObject` now logs
`[MeshBoundsAnomaly] name=... parent=... center=... extents=... verts=...`
for any special-work mesh whose local bounds are degenerate
(extents < 0.01) or far from the switch frame (center > 150m). Healthy
pieces log nothing.

Next: full restart, visit Nove, reproduce the vanish, then grep
`[MeshBoundsAnomaly]`. Also worth asking: does 7n90 flicker the same way
(same new dual-split path) - scopes whether this is the new join route or
Nove-specific catalogs.

## Current phase (Claude): 7n90 kinked blade/diverge rail + compressed development - backwards tangents in the new narrow-join Bezier

User screenshots (post-08:07 build): a hard kink in 7n90's blade and in the
narrow diverge rail, and the whole switch development compressed downstream
leaving a bare gap after the (correctly placed) stand.

Root cause: `TryBuildNarrowJoinRouteFromShadowTransition`
(SpecialWorkRuntimeDiscovery.cs, Codex's new dual-split join) takes its
Bezier handle tangents from `LinePoint.direction`
(`dualApproach.Tail.direction`, `narrowTransitionEnd.direction`). The shadow
anchors are oriented by a plain base-game `Reverse()`
(`ShadowNarrowGaugeGraph.cs:393` TryGetOrientedCurve), which flips point
order but NOT the stored per-point directions - the same "Reverse() doesn't
recompute rotations" defect that caused the inside-out blades earlier this
week (see LOG 2026-07-07). A backwards tangent puts the Bezier handle on the
wrong side of the joint: kink at the joint, ballooned/S-shaped transition,
compressed development.

Fix (surgical, consumer-side): both tangents now come from chords of the
curves themselves (orientation-proof); added a one-line
`[NarrowJoinTangents] node=... startDot=... endDot=...` diagnostic - a
negative dot on the next restart PROVES which anchor's stored directions
were backwards. Deliberately did NOT change TryGetOrientedCurve's plain
Reverse() itself: other consumers may compensate; flagged for Codex below.

**Architecture flag for Codex (attributed, per protocol):**
`ShadowNarrowGaugeGraph.TryGetOrientedCurve` returning plain-`Reverse()`d
curves means every consumer of `OrientedCurve` inherits backwards per-point
directions whenever reversal was needed. Worth auditing whether the plain
transition rendering compensates or is also subtly wrong; the proper fix
may be reversing via `SectionedSpecialWorkBuilder.ReverseRailCurve`
(direction-correcting) there instead - but that changes every consumer at
once, so it needs its own verification pass.

Built/deployed 0 warnings/errors. NOT live-verified. Verify on FULL
restart: 7n90 blade/diverge rail follow a smooth curve through the toe (no
kink); development fills toward the frogs; `[NarrowJoinTangents]` dots
logged; Nove same check. If geometry is smooth but the development still
ends short of the frogs, the fixed 2m+7m spans are the next suspect (do NOT
touch until the tangent fix is verified - one change at a time).

## Current phase (Claude): vee wing pairs don't mirror (N178 + vdlt) - synthetic kink point frame fixed

User reported `VeeFrog-0-WingB` isn't a mirror of `WingA` on vdlt, then that
N178 has the same defect. Log proved vdlt's uncommitted hardcoded
`ShouldRotateWingToFixedRail` yaw band-aid (+7.783deg, fired only on vdlt's
`standard-through:right x narrow-reversed:right` vee) was NOT the root cause -
N178 shows the same mismatch with no rotation firing.

Root cause (generic, all vee wings): `CreateVeeWingRail` appends its kink
point (across to the opposite heel) stamped with `oppositeHeel.Rotation` - a
frame lifted from the OTHER rail's curve, facing with or against the wing's
traversal depending on that rail's arbitrary orientation. For frame-corrected
switches, `NormalizeRenderFrames`' profile-center compensation shifts a
backwards-facing point a FULL railhead width while a forward-facing one moves
~zero - so per switch, one wing's tip lands a railhead width off while its
twin is fine. Same railhead-quantum family as every other hand bug this week.

Fix: stamp the appended kink point with `LookRotation` of the kink's own
direction (hand-agnostic, both wings, all presets), and REMOVE the vdlt-only
rotation special case + helpers (it was tuned over the broken frame and would
re-skew a corrected wing). Nothing else touched.

Built/deployed 0 warnings/errors. NOT live-verified. Verify on FULL restart:
N178 + vdlt wing pairs mirror; no `[VeeWingFixedRailRotation]` lines in log;
fc97/p997 both-diverge vee wings unchanged-or-better (same code path,
preserveProfileCenter=false there, so expect no visible change).

## Critical testing constraints

- `NarrowGaugeMod.dll` is loaded only at full Railroader process startup. A
  save reload does not load this deployment.
- The user retired the automated Railroader/TestBridge pipeline. Do not launch
  or drive Railroader. Build/deploy is allowed; live verification is manual.
- Both agents deploy to the same `Mods/FUSE.NarrowGauge/NarrowGaugeMod.dll`;
  whoever builds last wins it. The current deployed build is Codex's 10:39:35
  build, SHA-256
  `09882AF110F8D4B0F9678EB43E3BF207935129E614E31361193748C159C54230`.

## Current phase (Codex): 7n90/Nove use their authored three-leg, one-blade gauge join

The user clarified `NCustom_7n90` is dual gauge in, standard-only through,
narrow-only join/diverge, with one movable shared-rail blade. EF&A's authored
graph confirms exactly one segment of each gauge at the node. `Nove` has the
same signature; N178/g832/vdlt are conventional 2-dual + 1-narrow branches
and are excluded.

The old synchronizer rewired the real narrow leg onto the generated ghost
node and added a five-metre `:control` leg. Discovery then decoded the fake
leg as a second narrow route, causing 7n90's extra blade, 0.400 m closure, and
large cuts/gaps. The fix keeps/restores the real authored junction, removes an
obsolete control if present, and discovers only `standard-through` plus the
narrow join at the authored switch. The established internal id for that join
remains `narrow-diverge` only for catalog compatibility. The dual-split
measured fallback therefore produces exactly one shared-rail blade.

Review/evidence:
`reviews/dual-standard-narrow-authored-join-2026-07-10.md`. Built/deployed,
0 warnings/errors. DLL timestamp `2026-07-10 10:39:35`, size 755,712 bytes,
SHA-256
`09882AF110F8D4B0F9678EB43E3BF207935129E614E31361193748C159C54230`.
Not live-verified; requires a full restart.

First live test of the 07:18 build was rejected: screenshot `072150` was the
plain standard-switch fallback and `Player.log` showed `objects=12` plus a
0.260 m co-located-splice rejection. That separation is the intentional
third-rail centerline offset. The 07:30 build now assembles the narrow join
with `ShadowNarrowGaugeTransition` instead of directly splicing the two
offset endpoints.

The 07:30 restart restored `objects=14` and a valid one-blade 7n90 plan, but
`073443` still showed the standard fallback. The log proved the remaining
issue was renderer dispatch: the descriptor exposes standard+narrow proxies
while its dual enter leg is omitted, so the old gate ignored the valid plan.
The 07:39 build now lets every valid measured plan take ownership, matching
N178/vdlt's rendering path regardless of proxy-pair gauges.

The 07:39 live render exposed a 0.500 m inverse standard blade and midpoint
kink (`074646`) plus an upper gap (`074847`). The symmetric shadow preview
left the narrow rail halfway between centerlines at the toe. The 07:54 build
now keeps the narrow path on the dual third-rail offset through the toe and
transitions for seven metres only on the branch side, matching N178/vdlt's
throat orientation and recomputing the blade/frog boundaries.

Screenshots `075934`/`075938` then showed the whole throat needed to move up.
The 08:07 build adds a 2 m straight toe lead before the 7 m transition,
shifting blade/frog development up while preserving the already-correct
incoming ownership seam.

Screenshots `091750`/`091833` then proved that two-piece handoff introduced an
S-shaped curvature break and did not affect the remote post-Vee gap. The 09:40
build replaces it with one continuous 9 m tangent-matched curve. The gap is a
separate ownership-window defect: exact two-route dual splits suppress their
vanilla leg meshes but previously stopped measured rails at last-event + 3 m.
Those four work intervals now continue to both authored outgoing endpoints.

The user accepted 7n90 after that restart. Nove, its reversed/left-hand
counterpart, still rendered `blades=0` with an empty throat. Its closest rail
pair was 0.271 m apart, proving the narrow approach had lost the third-rail
center offset. The 10:03 build sources the dual approach directly from the
generated ghost dual centerline, preserving the 0.260 m offset regardless of
which authored end touches the junction. Next restart: verify Nove gains one
blade and 7n90 remains fixed; compare new `toeCenterOffset` diagnostics.

The user then found that both real narrow branches were on the authored
standard nodes, so the ghost train graph ended at each switch. The 10:39 build
supersedes that topology: branch endpoints are rewired to the ghost nodes;
one hidden control leg supplies the native blocked/open switch states; measured
discovery includes only the two real routes and one blade; the hidden leg's
mesh is suppressed. This should also remove the remaining artificial straight
section because the narrow curve now starts at its proper offset node. Nove's
same-side crossing is additionally preserved as a double frog instead of a
forced V. Full restart and actual narrow-train traversal are required.

## Prior phase (Claude): fixed vdlt selecting the mirror narrow-branch truth table

`NCustom_vdlt` rendered its narrow blades mirrored (through on the right,
diverge on the left) vs the working `NCustom_g832` (through-left,
diverge-right). Root cause: the two narrow-branch truth tables
(`DualGauge_NarrowBranch_Left/_Right`) are mirror images, and
`MatchesSelector` picks by ambiguous rail-side-labeled intersection pairs;
`_Right` wins ties by file order, so vdlt landed on `_Right`. Fix: for
crossing-frog narrow-branch switches only, reselect the variant by the true
physical divergence hand (`TryComputeNarrowDivergesLeft`, using the crossing
frog as a downstream reference). Scoped so g832 keeps `_Left` and
N178/Nove/7n90 are untouched. Writeup:
`reviews/vdlt-narrow-branch-variant-selection-2026-07-09.md`. Built/deployed,
0 warnings/errors. NOT live-verified. Verify on restart: vdlt through-left/
diverge-right AND g832 still correct. If g832 regressed, invert the
`Cross(...).y < 0` sign (one-char flip); nothing else can regress.

Reverted this turn: my own bad extension of the `NarrowReversedFrog` push to
narrow-branch (it moved g832's through frog). Restored to both-diverge only;
Codex's push body left intact.

## Prior phase (Codex): broken cutter inversion rolled back

Screenshot `232828` rejects the `23:25` index-1 inversion: it broke both
visible cutter results. The two flangeway clips are intersected retained
half-planes, so negating either keep sign changes the entire surviving wedge;
it cannot mirror one railhead face independently.

The recovery build restores the cutter behavior from `3290db4`:

- `StandardThroughFrog` and `NarrowReversedFrog` both derive keep signs from
  their measured fixed-piece anchor;
- automatic inversion is false and its index is `-1`;
- the wrong-side overlay fix remains intact;
- the rejected curve pushes/profile-hand changes remain removed.

The original `NarrowReversedFrog` inside-versus-outside bevel remains open.
The next correction must move or reconstruct its narrow-through cut boundary,
not invert either retained half-plane.

Built and deployed: 0 warnings, 0 errors. Built/deployed DLL timestamp
2026-07-09 23:31:13, size 737,280 bytes, SHA-256
`4AA7E65E1D2553738C83A9DFF537926BF7C9361E90FC44987C8AA314CC17A3CC` on both
copies. No game process was launched or controlled. Full evidence:
`reviews/frog-direction-gap-frame-investigation-2026-07-09.md`.

## Next turn

1. Fully quit and restart Railroader; a save reload is insufficient.
2. Inspect Nove and 7n90 ties. The common three-rail approach should have one
   tie bed; after the routes separate, each route should have its own normal
   width ties and no timber should span both tracks.
3. Confirm the recovery restores continuous physical shared rail at w8sq,
   8vbl, eeo2, and the two screenshot boundaries. L/R must remain interpreted
   relative to each segment's own running direction; do not normalize it from
   switch entry legs or lexical neighboring segments.
4. Re-test the narrow train from U6n0 through bmgi into fc97 and across the
   flipped w8sq/tliv/eeo2 corridor. Ghost nodes now use the midpoint of the
   exact visible narrow rail pair and select a full-offset coincident candidate
   cluster instead of averaging opposing sides. S09h and S4u5 must have the
   same ~0.260 m spacing. Fresh disagreement logs may remain as diagnostics,
   but must say `selected full-offset cluster`; no node may preserve/average a
   midpoint.
5. Confirm all rejected `fuse-ng:s:join:*` pieces are gone. Inspect eeo2, vp0t,
   8vbl, s3y7, and tliv: ghost endpoints now apply authored L/R directly in
   each segment's A-to-B frame, so flipped L and forward R resolve to the same
   physical shared rail without connectors or midpoint crossings.
6. The latest build supersedes raw per-segment resolution with a ghost-only
   component pass: switch consensus anchors the physical side and propagation
   converts it to each segment's local sign. Verify wqbb, u6n0, m29y, bmgi,
   i8x0, eeo2, tliv, 84fv, and s3y7. Fresh logs should contain no endpoint
   disagreement or `[GhostSharedSideConflict]` for this component.

Live result: user restarted and accepted this fix with “Finally!” The ghost
shared-side propagation is complete and must be preserved.

Nove/7n90 separated narrow-route ties now use the normal 6'9" three-foot tie
length scaled against the actual standard prefab base. Restart and verify only
the narrow join bed shrinks; standard/shared-approach ties must remain intact.
5. Regression-check N178 remains finished and vdlt/g832 retain their existing
   conventional narrow-branch geometry.

## Open questions / blockers

- Manual live verification is required; the screenshots prove the intended
  side but static compilation cannot prove final mesh clipping.
- The graph-connected 7n90/Nove topology was user-confirmed; the follow-up Nove
  route-orientation and 7n90 V-frog corrections are compiled but not yet
  live-verified.
- Nove/7n90 split-route ties remain deployed. Both the finite ghost handoff and
  the later switch-anchored shared-side normalization were rejected and fully
  removed. The replacement changes ghost endpoint placement only and requires
  full-restart train/log verification.
- vdlt remains pending live verification of Claude's mirror-table fix.
- `NCustom_ltci` / `SCustom_ttpp` neighboring ownership overlap remains open.
