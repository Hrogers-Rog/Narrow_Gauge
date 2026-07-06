# Review: all 14 measured special-work switches currently invalid (2026-07-05/06 session)

Ground-truthed from a live in-game session's `Player.log` and
`NarrowGauge/SpecialWorkPlans/*.txt` at
`C:\Users\roger\AppData\LocalLow\Giraffe Lab LLC\Railroader` (both get
overwritten on the next play session — this file is the durable snapshot of
that evidence). User-reported symptom: a short, disconnected vertical rail
stub floating above otherwise-normal-looking dual-gauge track (two
screenshots), and "all 14 switches are broken."

**Confirmed: `Special-work analysis: objects=14, invalid=14`.** Every
currently-measured special-work switch on the map fails validation. This
supersedes `docs/special-work-turnout-combo-status.md` (dated 2026-06-13,
which marked `dual.both-diverge` and `dual.split-standard-narrow` variants as
`DONE`) — something regressed between then and now, or that doc's
"visually accepted" sign-off no longer holds after later commits. Whoever
picks up item 1 or 3 below should check `git log` on the implicated files
between those dates to find what changed, rather than assuming it never
worked.

## Likely explanation for the reported visual symptom

`N178`'s issue list includes `Shared duplicate rail 'narrow-reversed:left'
still renders` — i.e. a rail that should have been cut/suppressed as a
duplicate is instead rendering as a stray, disconnected piece. This is the
same defect family as issue #4 below (`is missing required suppressed
interval kind 'SharedDuplicate'` on 5 other switches): the shared-duplicate
suppression pass is failing to cut/hide a rail that duplicates another one,
leaving a disconnected stub sticking out of otherwise-normal track — matching
the floating vertical rail segment in both screenshots. **Start
investigation at issue #4/#5 below** (`AddSharedSuppressions` /
`RailParticipatesInAcceptedFrog` in `SectionedSpecialWorkBuilder.cs`) — it's
both the most common failure and the best match for the actual visual bug
report.

## Failure catalog (all 14, grouped by apparent root cause)

### 1. `dual.split-standard-narrow` derives zero blades — both instances (P0)

- `Nove`: `blades=0`, `shared=0`. FIRST FAILURE: "Preset requires shared rail
  intervals but none were derived." **Also throws a hard exception**, not
  just a validation warning: `FUSE captured SwitchGeometry.Calculate failure
  node='fuse-ng:n:Nove' segmentA='fuse-ng:s:Nove:control'
  segmentB='fuse-ng:s:SCustom_rhxm' exception='Exception: Switch tracks do
  not intersect'`. The synthesized gauge-separation control segment for Nove
  doesn't geometrically intersect its neighbor — this is a placement bug in
  control-node/segment synthesis for this node, on top of the missing shared
  rail intervals.
- `NCustom_7n90`: `blades=0`, `shared=1`. FIRST FAILURE: "Expected at least 1
  movable assemblies but derived 0."

Both known live instances of this preset produce zero movable blade
assemblies. This reads as the preset being fundamentally non-functional
right now, not two independent per-node bugs — investigate the
`dual.split-standard-narrow` blade-derivation path in
`SectionedSpecialWorkBuilder.cs` before touching either node individually.

Codex follow-up, 2026-07-06: the exported `Nove` and `NCustom_7n90` plans
both said "no truth table matched; measured geometry fallback used". The
sectioned builder only produced blades from matched truth-table blade rules
or from generic normal/reversed route pairs; a split has a fixed
`standard-through` route plus `narrow-diverge`/hidden `narrow-through`, so
the generic fallback could not produce a gauge-separation blade when the
two hand-authored split truth-table selectors did not match the current
right/right crossing geometry. Fix landed in
`SectionedSpecialWorkBuilder.cs`: if no truth-table blade was yielded for
`dual.split-standard-narrow`, it now skips the generic binary-switch fallback
and measures one best split blade from same-side
`standard-through`/`narrow-diverge` or generated
`narrow-through`/`narrow-diverge` stock/movable pairs. Candidates are scored
by frog participation, `narrow-diverge` movable preference, and stock
separation, then emitted as one `narrow-separation` blade/cut/suppression.

The hard `Nove` native exception had an additional patch-layer cause: the
Harmony prefix bypassed native `SwitchGeometry.Calculate` only for
hidden-control plus generated-ghost segment pairs, but the live failure was
hidden-control plus the real narrow branch. `Patches.cs` now routes any
exactly-one-hidden segment pair at a gauge-separation control node through
the control-shell geometry. The hidden control segment is also tagged with
`dual.split-standard-narrow`, and the control node is placed along the source
node's standard-only continuation tangent. This builds cleanly, but needs a
fresh in-game `Player.log` to confirm `Nove` has no captured
`SwitchGeometry.Calculate` failure and whether `Nove`/`NCustom_7n90` now
report `valid=True`.

### 2. `dual.standard-branch-joins-main` never attempts custom rendering — both instances (P0)

- `NCustom_fl15`, `NDeHartPassing_33d6`: neither reaches the "measured
  special-work" build/validation path at all. Log instead shows `[Build]
  Switch 'X' connects mixed gauge segments; leaving its visuals standard.` —
  full fallback to a plain single-gauge switch visual, no special-work
  attempt, no validation issues logged.

Also read as one systemic gap (both known instances of the preset), not two
node-specific bugs. Find why these get `customAllowed=False`/fall back
before even reaching measured-special-work analysis — likely a runtime
discovery/classification gap for this preset, not a geometry bug.

### 3. `dual.narrow-branch-joins-main` blade under-build (2/3 instances) (P1)

- `N178`: FIRST FAILURE "Expected exactly 2 physical point blades but built
  1." Also: blade doesn't connect into a rendered closure/fixed section, and
  the shared-duplicate rail issue noted above.
- `NCustom_vdlt`: same "Expected exactly 2... built 1", plus "Fixed
  diverging narrow stock/running rail is not continuous to frog cut 81.180"
  and "NarrowPointBlade is not bound to a movable point blade."
- `NCustom_g832` (the third instance) does *not* show this pattern — its
  issue is a truth-table rail-role mismatch instead (see #6). So this is 2/3,
  not fully universal to the preset — but still a real cross-node blade-count
  bug worth investigating once, not twice.

### 4. `dual.both-diverge` SharedDuplicate suppression missing (5/7 instances) (P1 — likely tied to the visual bug report)

- `NCustom_l4a4`, `NCustom_ltci`, `NCustom_p997`, `NCustom_u6n0`,
  `NDeHartPassing_wqbb`: all report `TruthTable[DualGauge_BothDiverge_*]
  rail 'X' is missing required suppressed interval kind 'SharedDuplicate'`,
  several also with `shared rail interval 'A'/'B' is rendered by both
  logical rails`.
- The other 2 both-diverge instances (`NCustom_fc97`, `Npv2`) show different
  first-listed issues (blade count / frog guard rails — see #6/#7), so this
  may or may not affect them too; the validator might just be reporting a
  different issue first. Worth checking once the SharedDuplicate suppression
  bug is understood.

Candidate code: `SectionedSpecialWorkBuilder.cs`'s `AddSharedSuppressions` —
for `IsDualBothDivergePreset`, it skips adding the `SharedDuplicate` cut/
suppression when `RailParticipatesInAcceptedFrog(loser, frogs)` is true
(line ~1424-1428). If the frog-acceptance check now matches in cases where
the truth table still unconditionally requires the suppressed interval, that
mismatch would produce exactly this failure signature on every affected
node. Not confirmed — `git log -p -L` on this function shows it unchanged
since 2026-06-18, so if this is the cause, the regression is either in what
`RailParticipatesInAcceptedFrog` or `frogs` now compute upstream, or this
combination of preset/orientation was simply never actually validated before
(the June 13 doc's "DONE" claims may not have covered these specific nodes).

Claude follow-up, 2026-07-06: confirmed this was the cause. `RailParticipatesInAcceptedFrog`
checked frog membership across the rail's *entire length*, not near the
specific `SharedRailInterval` under consideration — a rail with an accepted
frog anywhere on it had every shared-duplicate interval skipped, including
ones nowhere near that frog. Replaced with
`RailParticipatesInAcceptedFrogNearInterval(rail, frogs, start, end)`
(compares each frog's `RailIntersection.DistanceA`/`DistanceB` against the
interval bounds with a `Max(frog.CutHalfLength, MinimumPieceLength)` margin)
at all 4 call sites: `AddSharedSuppressions`, `AddCrossFamilySharedSuppressions`,
`SuppressDualBothDivergeFrogDuplicate`, and the truth-table validator's own
"still renders" diagnostic (which had the same whole-rail check, which is
why it correctly flagged `N178`'s case but would have silently agreed with
the builder's wrong skip on the other 5 nodes rather than catching it).
Build succeeds; live in-game verification still needed since this repo has
no unit test project.

### 5. Frog guard-rail / approach-section gaps (P2)

- `NCustom_fc97`: "Expected exactly 3 physical point blades but built 1"
  (same family as #3, different preset), plus "Frog 'v2-frog:0' has fewer
  than 3 type-specific guard rails" and "no rendered approach section before
  cut 69.138."
- `Npv2`: "Frog 'v2-frog:0' has fewer than 3 type-specific guard rails",
  "rail 'standard-reversed:left' has no rendered approach section before cut
  92.380", "Frog 'v2-frog:1' ... no rendered approach section before cut
  87.611."

### 6. `NCustom_g832` rail-role mismatch (P2, single node)

`dual.narrow-branch-joins-main` (left-hand variant). FIRST FAILURE:
`TruthTable[DualGauge_NarrowBranch_Left] rail 'SharedStandardFrogRail' maps
to 'standard-through:left' with resolved roles
'FixedRunningRail,FrogApproachRail', expected one of
'SharedRail,FrogApproachRail,FrogRail'.` Also `'NarrowThroughStockRail' has
no rendered 'FixedRunning' piece.` Looks node/orientation-specific (the
mirror-hand `NCustom_vdlt`/`N178` don't show this particular mismatch) —
lower priority than the cross-node systemic issues above.

## Full per-node data (for reference)

| Node | Preset | blades | frogs | shared | First failure |
|---|---|---|---|---|---|
| `N178` | narrow-branch-joins-main | 1 (want 2) | 1 | 5 | blade count |
| `NCustom_7n90` | split-standard-narrow | 0 | 1 | 1 | 0 movable assemblies |
| `NCustom_fc97` | both-diverge | 1 (want 3) | 6 | 13 | blade count |
| `NCustom_fl15` | standard-branch-joins-main | 2 | 3 | 3 | never attempted (mixed-gauge fallback) |
| `NCustom_g832` | narrow-branch-joins-main | 2 | 3 | 4 | rail-role mismatch |
| `NCustom_l4a4` | both-diverge | 3 | 5 | 8 | missing SharedDuplicate |
| `NCustom_ltci` | both-diverge | 3 | 5 | 8 | missing SharedDuplicate |
| `NCustom_p997` | both-diverge | 3 | 5 | 8 | missing SharedDuplicate |
| `NCustom_u6n0` | both-diverge | 3 | 5 | 8 | missing SharedDuplicate |
| `NCustom_vdlt` | narrow-branch-joins-main | 1 (want 2) | 3 | 7 | blade count |
| `NDeHartPassing_33d6` | standard-branch-joins-main | 2 | 4 | 4 | never attempted (mixed-gauge fallback) |
| `NDeHartPassing_wqbb` | both-diverge | 3 | 11 | 5 | missing SharedDuplicate |
| `Nove` | split-standard-narrow | 0 | 1 | 0 | no shared rail intervals + hard exception |
| `Npv2` | both-diverge | 3 | 8 | 10 | frog guard rails / approach section |

## Critical update, 2026-07-06: `valid=True` does not mean visually correct

After items 1+2 landed, a fresh `Player.log` shows `Special-work analysis:
objects=14, invalid=0` — all 14 report valid. **The user tested in-game
anyway and confirmed several switches are still visually broken**: small
disconnected white rail/guard-rail fragments floating near frogs and along
diverging routes (screenshots), on multiple switch types including at least
one that looks like a plain narrow-only view of a `dual.narrow-branch-joins-main`
switch. This is not a false "all clear" from validation being wrong in a
vague sense — it's a specific, identifiable gap:

The fresh log contains `Main.Warn(...)` messages, **not** `yield return`
issues, so they never count toward `invalid`:

- `[Validation] Blade 'v2-blade:narrow:Right' does not connect into a
  rendered closure/fixed section after root 45.188. Rendering anyway.`
- `[Validation] Fixed diverging narrow stock/running rail has no renderable
  role sections. Rendering anyway.` (appears 3x this session, across
  multiple `dual.narrow-branch-joins-main` nodes — all 5 instances now carry
  this preset after Nove/`NCustom_7n90` were reclassified from
  `dual.split-standard-narrow` this session, see item 1's follow-up notes)

Both come from `ValidateSectionedDualGaugeSpecialWork` in
`SectionedSpecialWorkBuilder.cs` (~line 2520-2597). Per `git blame`/prior
commit messages (`ae87b7e` "Relax blade-closure-connection validation to a
warning", `8a5ea4d` "Relax blade validation"), these were previously hard
`yield return` failures that got downgraded to `Main.Warn` + "render anyway"
so presets would produce *something* rather than fall back entirely. That
tradeoff is exactly what's now hiding real geometry gaps behind a passing
`valid=True` — the underlying "this rail/blade doesn't actually have
continuous renderable geometry here" problem was never fixed, just silenced
in the pass/fail signal.

**This is now the top-priority item, ahead of the old items 3-5** (which,
per this same fresh log, all now separately report `valid=True` too — `fl15`
and `33d6` reached the measured-special-work path at all for the first time
this session, and `g832` no longer shows its truth-table mismatch. Whether
those are *actually* correct in-game needs the same skepticism applied here,
not just taken at face value from the log).

Next step: don't just relax validation further or add another warning
downgrade. Investigate `ResolveDivergingFixedStockRail`, `HasApproachSection`,
and how `RailRoleSection`s get built for `dual.narrow-branch-joins-main`
closely enough to understand *why* the diverging fixed rail and blade
closure sometimes produce zero renderable/continuous geometry, and fix that
directly. Cross-reference against the user's screenshots (which switch,
which specific fragment) rather than trusting log output alone once a fix
lands.

Codex follow-up, 2026-07-06: fixed the sectioned builder's narrow-branch
geometry path rather than relaxing validation further. Findings from the
fresh exported plans:

- `ResolveDivergingFixedStockRail` was selecting the first narrow stock rail
  from the blade list. For `DualGauge_NarrowBranch_Left` this picks
  `narrow-normal:right`, which is the shared/through duplicate and can be
  fully suppressed; the truth-table narrow stock rail is the
  `narrow-reversed` stock. The resolver now prefers narrow stock rails sourced
  from `narrow-reversed`.
- `NCustom_7n90`'s blade warning was an endpoint-root case: measured blade
  detection let a movable point consume the route all the way to the endpoint,
  leaving no fixed closure section after the root. Blade measurement now
  reserves a short endpoint closure when it would otherwise run to the end of
  the rail.
- The N178-style floating-fragment path was a shared-duplicate/frog ownership
  mismatch: a rail could be cut as a shared duplicate and still be used as a
  frog/wing hardware source. After shared-duplicate cuts are known but before
  frog cuts/hardware are built, frog candidates now rehome a frog rail from a
  shared-duplicate loser to a nearby unsuppressed physical owner when such an
  owner is within the rail-head/flangeway tolerance.

The two "Rendering anyway" checks named above have been restored to hard
validation failures (`yield return`). Build/deploy verification succeeded:
`dotnet build NarrowGaugeMod.csproj -p:RailroaderDir="C:\Steam\steamapps\common\Railroader" -p:EnableModDeploy=true`
completed with 0 warnings/0 errors and copied the DLL into the live mod
folder. This is **not** visual proof; the user still needs to launch the game
and confirm the fragments are gone in screenshots/fresh `Player.log`.

## Suggested working order

1. `dual.split-standard-narrow` zero-blade bug (#1) — worst state (nothing
   renders custom at all for either instance), plus Nove's hard exception is
   the only outright crash in this set.
2. `dual.both-diverge` SharedDuplicate suppression (#4) — highest node count
   (5, possibly 7), and the best current match for the reported visual bug.
3. `dual.standard-branch-joins-main` fallback gap (#2) — currently gets zero
   attempt at all, likely a smaller, more mechanical fix (classification/
   discovery gap) than a geometry bug.
4. Blade under-build (#3) and frog guard-rail gaps (#5) — likely related to
   each other and to #1/#4 once those are understood; revisit after.
5. `NCustom_g832` (#6) — single-node, lowest leverage, do last.
