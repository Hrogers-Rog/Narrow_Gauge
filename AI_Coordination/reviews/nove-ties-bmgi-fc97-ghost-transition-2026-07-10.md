# Nove ties and bmgi-to-fc97 ghost transition - 2026-07-10

## Nove tie suppression

The fresh `Player.log` proves the valid Nove plan reaches the measured tie
renderer, but `SpecialWorkHardwareProfileCatalog.ShouldSuppressSpecialWorkTies`
suppresses it by literal node id:

```
[SpecialWorkTies] Suppressed by hardware catalog for 'special-work:Nove'.
[SpecialWorkObjects] ... specialWorkTieObjects=0
```

This is stale cosmetic policy, not geometry. Commit `1f9be55` previously
identified and removed the same Nove-only exception. Nove now has a valid
two-route, one-blade measured plan, so it should use the ordinary measured tie
span just like 7n90. The built-in id match is removed while the explicit
`suppress-special-work-ties` authoring profile remains supported.

U6n0 does not have the same cause. The same runtime created 59 measured ties
for `special-work:NCustom_u6n0` over a 32.467 m span.

## bmgi -> fc97 train path does not match the visible narrow route

The installed EFA graph has this exact authored corridor:

- `SCustom_s3y7`: `DualGauge_L`, `NCustom_u6n0 -> NCustom_bmgi`;
- `SCustom_8vbl`: `DualGauge_R`, `NCustom_bmgi -> NCustom_fc97`;
- fc97's other two dual legs, `SCustom_kfah` and `SCustom_47ab`, are both
  `DualGauge_L`.

The visible renderer can honor each segment's explicit shared side. The
functional ghost generator cannot currently represent the side changes at
their real transition spans because it creates exactly one generated node per
authored node. At bmgi the L and R candidates are 0.520 m apart, so
`ResolveNodeDefinition` averages them onto the authored standard centerline
(`maxDeviation=0.260 m`). At fc97 one R and two L candidates are averaged;
the live ghost switch is only about 0.087 m off the standard node and reports
`maxDeviation=0.347 m`. The generated `SCustom_8vbl` counterpart therefore
runs on or near the standard center instead of the physical narrow wheel path.

This is not a visual special-work defect and must not be corrected by moving
the already-approved U6n0/fc97 rail meshes. It is also not the cataloged
`DualGauge_T` case: that contract requires a dedicated degree-two segment
between one L and one R neighbor, while `SCustom_8vbl` terminates at the
degree-three fc97 switch.

The functional correction needs a graph-side shared-rail transition that
preserves the L body of `SCustom_s3y7`, the R body of `SCustom_8vbl`, and a
finite transition between their offset routes, while selecting fc97's incoming
R route as the ghost switch approach. Simply choosing one endpoint candidate
would move the mismatch onto the adjoining segment and would still make the
train drift across an entire ordinary segment. No graph mutation is made in
this tie-restoration build.

Tie-restoration build/deploy succeeded with 0 warnings and 0 errors. Built
and deployed DLLs have timestamp `2026-07-10 11:59:09`, size 757,248 bytes,
and SHA-256
`DE1E80F4BC4D5ED64CCA6C4600278383A596A006860A4065E546DE50CBD3BCB4`.
No game process was launched or controlled.

## Implemented finite ghost offset handoffs and split-route ties

The user confirmed that the generated train centerline riding between rails is
visually unacceptable and specified that the transition must remain smooth on
the same ghost graph. The implementation now expands a dual source's generated
counterpart into a route chain only when one of its physical endpoint
candidates differs from the resolved ghost node by more than 0.05 m:

1. the canonical generated body stays on the source segment's physical narrow
   centerline;
2. a generated boundary node is sampled from that physical offset curve;
3. a short generated handoff connects the boundary to the existing ghost node;
4. every route-chain part remains linked/tagged to the same authored dual
   source, so switch-state synchronization and measured ownership continue to
   map by source identity rather than assuming one generated segment id.

At a degree-three node with a physical majority (two coincident candidates and
one opposite-side candidate), the ghost switch now uses the majority center
and only the minority route receives an 18 m handoff. At a fixed two-leg L/R
join such as bmgi, the established midpoint remains and each side receives a
7.5 m handoff. Consequently `SCustom_8vbl` keeps its long body on the
`DualGauge_R` narrow center, transitions locally at bmgi, and transitions into
fc97's majority switch center only inside the switch approach. Generated
handoff objects remain hidden.

The link registry now retains all generated route-chain parts while preserving
the canonical body for the legacy single-counterpart API. Switch
synchronization, graph validation, measured route discovery, and visible-rail
ownership resolve generated parts by their source-segment tag.

Nove/7n90 tie generation is separately scoped to the
`dual.split-standard-narrow` preset. It samples each logical route against only
that route's rail work intervals, uses standard or narrow tie width by family,
and suppresses a duplicate narrow tie only while both wheel paths still occupy
the same three-rail approach. After separation, it produces two independent
tie beds instead of one timber spanning both routes.

Build/deploy succeeded with 0 warnings and 0 errors. Built and deployed DLLs
have timestamp `2026-07-10 18:50:29`, size 765,952 bytes, and SHA-256
`64F8C1F671C67EDFB91150FEA1CCE9C143FFB960D1F701767F9F5AA8AB624B24`.
No game process was launched or controlled. Full-restart verification must
check route-separated ties on Nove/7n90, a train staying on physical rails from
U6n0 through bmgi and `SCustom_8vbl` into fc97, synchronized switch states, and
no visible generated handoff objects.

## Rejected handoff model; continuous-side correction

The user's restart disproved the finite-handoff interpretation. The generated
route visibly crossed from one side through the middle and onto the other side.
That is not the physical layout: the narrow wheel path stays on one continuous
side for the entire U6n0 -> bmgi -> fc97 corridor. `DualGauge_T` is the only
authoring contract that permits a shared-rail crossing; bmgi has no such piece.

The handoff implementation was fully rolled back. The replacement treats the
`DualGauge_L`/`DualGauge_R` disagreement at an ordinary join as a local
authoring-orientation mismatch:

- each degree-three node uses `Graph.DecodeSwitchAt` to anchor the physical
  narrow center from its entry leg;
- its adjoining dual-gauge routes select whichever local offset reaches that
  same physical point;
- a degree-two join keeps those endpoints coincident and mirrors a mismatched
  local side label instead of averaging or generating a crossing.

No extra ghost nodes, handoff segments, midpoint, or transition curves are
created. The route-separated Nove/7n90 tie change remains intact.

Build/deploy succeeded with 0 warnings and 0 errors. Built and deployed DLLs
have timestamp `2026-07-10 19:55:36`, size 762,368 bytes, and SHA-256
`6C9991EA052EBA61D883E99034FF70969CAA8D17683FED0591FF68791A2A1A9B`.
No game process was launched or controlled. A full restart must verify that the
third rail and train center remain on one side through U6n0, bmgi, and fc97.

## Switch-anchored normalization rejected and rolled back

Screenshots `200647` and `200656` proved the 19:55 normalization was globally
wrong. It made otherwise-correct segments including `SCustom_w8sq`,
`SCustom_8vbl`, and `SCustom_eeo2` select different physical shared rails at
ordinary boundaries. The runtime log also showed `SCustom_w8sq` oscillating:
one end normalized it at vdlt and the other immediately normalized it back at
i8x0. That is direct evidence that a switch-entry/global-side rule cannot
replace segment-facing semantics.

The user clarified the authoring contract: `DualGauge_L` and `DualGauge_R` are
relative to a segment's running direction. Oppositely faced/flipped segments
can therefore carry different L/R labels while using the same physical shared
rail. The switch/degree-two normalization was removed in full and the recovery
DLL deployed. No replacement orientation mutation is included in this recovery.

Recovery build/deploy succeeded with 0 warnings and 0 errors. Built and
deployed DLLs have timestamp `2026-07-10 20:09:12`, size 758,784 bytes, and
SHA-256 `9AF0CF27D01EFB1B507E71755E3B9F7E48DE751DF355808ED4475A718F02359F`.
No game process was launched or controlled. A full restart is required because
the current process log still contains output from the rejected loaded DLL.

## Ghost-only flipped-segment offset correction

After the recovery restart the user confirmed the visible shared rails were
restored, isolating the remaining defect to the functional ghost graph. Fresh
logs reported endpoint disagreements at bmgi, fc97, i8x0, wqbb, and npv2 while
the corresponding visible rails were correct. The ghost generator was
recomputing an A-to-B offset from the authored label; that can disagree with
the renderer on segments whose effective curve/facing is flipped.

`GhostGraphSynchronizer.TryCreateNodeCandidate` now asks the visible track
builder for the actual rendered narrow wheel center at each source endpoint.
That helper constructs the same standard and third-rail line curves used by
the mesh, selects the same shared-side pair via `SharesRightRail`, and returns
their midpoint. This changes only generated ghost-node positions: it does not
change source gauges, segment directions, visible rails, ties, or special-work
ownership.

If a disagreement remains, its warning now lists every source segment and its
measured rendered-center coordinate so the flipped leg is identifiable without
another speculative global rule.

Build/deploy succeeded with 0 warnings and 0 errors. Built and deployed DLLs
have timestamp `2026-07-10 20:58:03`, size 759,808 bytes, and SHA-256
`6525F301C6497F888594560194BA7B3A30686C759C5ADFA01A0338363CC6BAB3`.
No game process was launched or controlled. Full restart verification must
confirm the five endpoint-disagreement warnings disappear and trains follow
the already-correct visible narrow rail pair.

## Do not average opposing rendered centers

Screenshots `210555`/`210545` showed S09h and S4u5 at visibly different ghost
offset magnitudes. The new coordinates made the cause exact: Npv2 had two
coincident candidates at the correct 0.260 m offset (`S09h`, `SCustom_3mfe`)
and one candidate on the opposite side (`SCustom_eeo2`). Averaging 2-to-1 put
the shared ghost node only about 0.087 m from the standard center, so S09h's
ghost curve gradually collapsed inward while S4u5 retained the full offset.

`ResolveNodeDefinition` now clusters candidates within 0.05 m and selects the
largest coincident cluster. A 2-to-1 switch therefore uses the two agreeing
full-offset routes. A two-candidate tie selects one deterministic full-offset
candidate; it never averages to the standard center and never preserves a
previous midpoint. No transition geometry is created. The rendered-center
helper also no longer depends on manager gauge classification during early
startup, which had briefly skipped S09h/S4u5 before a later synchronization.

Build/deploy succeeded with 0 warnings and 0 errors. Built and deployed DLLs
have timestamp `2026-07-10 21:14:42`, size 761,344 bytes, and SHA-256
`1D5AF61E98843BB00A6E5937527FD3145387305552CD4381229C1AD1FCCD38EA`.
No game process was launched or controlled. Full restart verification should
show S09h and S4u5 at the same 0.260 m wheel-center offset.

## Route-specific ghost bodies for opposing switch clusters

Follow-up screenshots found `SCustom_eeo2`, `SDeHartPassing_vp0t`,
`SCustom_8vbl`, and `SCustom_s3y7` directly overlapping their source track at
mid-segment. The cluster diagnostics explain why: each body belonged to the
selected cluster at one endpoint and the opposite cluster at the other, so a
single canonical ghost node at each source node forced its curve to cross the
standard center halfway. `SDeHartPassing_tliv` had the same predicted pattern.

Generated dual counterparts now keep route-specific endpoints when their
rendered center differs from the canonical switch cluster. The canonical body
begins 5 m inside the source leg on its true rendered offset; a runtime-hidden
join connects that boundary to the canonical ghost switch node only inside the
switch work area. The long segment body therefore remains parallel at the full
0.260 m offset and never crosses its source in open track. No visible rail or
authored L/R value changes.

The link registry now associates the primary ghost body and hidden join parts
with one dual source. Switch synchronization compares that shared source link,
while validation accepts route endpoint nodes and ignores hidden join pieces.

Build/deploy succeeded with 0 warnings and 0 errors. Built and deployed DLLs
have timestamp `2026-07-10 21:48:52`, size 765,440 bytes, and SHA-256
`393922C8DB61E7146CD3707E32C6D5E3FE1D86336A8F6C7F1FE69B877B5B566B`.
No game process was launched or controlled. Full restart verification must
check eeo2, vp0t, 8vbl, s3y7, and tliv bodies plus linked switch operation.

## Route joins rejected by live train; direct direction transform

Screenshot `220322` showed locomotive 71 actually traversing
`fuse-ng:s:join:Npv2:SCustom_eeo2` laterally off the physical rails. This
rejects route-specific join segments even when they are visually hidden: the
train graph cannot contain an off-rail connector.

The user clarified the actual authoring invariant: every dual-gauge segment
uses the same physical shared rail. L/R is relative to segment direction, so a
reversed `DualGauge_L` segment and forward `DualGauge_R` segment can name the
same physical rail. Ghost creation must apply the label in the segment's A-to-B
frame exactly once.

All route nodes/joins and multipart link changes were rolled back. Cleanup
still recognizes the rejected route-join tag so stale runtime joins are removed
on restart. `TryCreateNodeCandidate` now computes:

```
DualGauge_R => +OffsetMagnitude along right-of-A-to-B
DualGauge_L => -OffsetMagnitude along right-of-A-to-B
```

Flipping A/B reverses the frame and therefore reverses L/R physically without
special cases, clustering mutations, or a lateral connector. Generic dual and
explicit transition gauges retain the registry fallback.

Build/deploy succeeded with 0 warnings and 0 errors. Built and deployed DLLs
have timestamp `2026-07-10 22:17:26`, size 761,344 bytes, and SHA-256
`8C55ED01B86240FC048E7453A84882F9E3B197DABFE978D6CDE2B180F28A1399`.
No game process was launched or controlled. Full restart required.

## Ghost-only switch-anchor propagation

Reviewing the track graph, decoded switch behavior, and code together exposed
the missing layer. The visible special-work plans already resolve wqbb,
u6n0, and Npv2 to shared side Right and fc97 to Left. The ghost generator did
not consume an equivalent component-level result; it interpreted each raw L/R
label independently and then clustered contradictory endpoint positions.

The correction is isolated to `GhostGraphSynchronizer`:

1. explicit `DualGauge_T` segments split components because they intentionally
   change shared side;
2. each ordinary three-leg dual switch clusters its raw endpoint candidates
   and supplies a physical switch anchor;
3. that world-space target propagates through every connected ordinary node;
4. each segment selects +0.260 m or -0.260 m in its own A-to-B frame according
   to which sign reaches the propagated physical rail;
5. later switch anchors only diagnose a true component contradiction and never
   mutate visible geometry.

This is the switch-aware version of the earlier idea, but unlike the rejected
19:55 build it does not modify `DualGaugeSharedRailRegistry`; visible rails,
frogs, ties, and blades are untouched. It also creates no midpoint or connector
segments.

Build/deploy succeeded with 0 warnings and 0 errors. Built and deployed DLLs
have timestamp `2026-07-10 22:55:03`, size 766,976 bytes, and SHA-256
`C72C0AA5B421D37C9231736FB6BAEEBF4A15CBEB36A4CF393C1F47D1F7004D9A`.
No game process was launched or controlled. Full restart verification should
show no candidate disagreement at wqbb/u6n0/m29y/bmgi/i8x0 unless the authored
component truly changes physical shared rail without `DualGauge_T`.

## Live verification

The user restarted and accepted the 22:55 ghost-only switch-anchor propagation
with “Finally!” This confirms the reported wrong-side/zero-offset cases are
resolved by component-level physical-side propagation. Treat midpoint
averaging, lateral joins, finite handoffs, and visible-registry normalization
as rejected approaches; do not reintroduce them.

## Narrow join tie length

Nove/7n90 route-separated narrow ties were structurally correct but slightly
oversized. The code used `(ThreeFootGauge.Inside + 1m)` as a normalization
divisor, while `CreateTieMatrix` actually scales the standard tie prefab. A
narrow scale of 1.0 therefore still produced a standard-length timber.

Separated narrow routes now use the existing `NarrowOnlyTieLength` of 6'9"
(2.0574 m), centered on the narrow wheel path, and divide by the standard
prefab length (`Gauge.Standard.Inside + 1m`). Standard-route and shared-approach
ties are unchanged.

Build/deploy succeeded with 0 warnings and 0 errors. DLL timestamp
`2026-07-10 23:25:02`, size 766,976 bytes, SHA-256
`5CB89B9AAB40291AF759960A6C756F3F64F87B8B78E1BD0BC0CF599673CBE29C`.
