# Coordination Log

Append-only. Never edit or delete a past entry — append a correction
instead. Newest entries at the bottom.

### [Claude] 2026-07-05 — Set up Claude/Codex coordination directory

Set up `AI_Coordination/` in this repo, mirroring the structure already in
use in `Fuse_geometry_engine` (`PROTOCOL.md`'s file-based turn-exchange
protocol, `00_PROJECT_CONSTITUTION.md`, per-agent role files, `STATUS.md`,
`LOG.md`, `REFERENCES.md`, `proposals/`, `reviews/`), plus root-level
`AGENTS.md`/`CLAUDE.md` pointer files so each CLI picks this up
automatically on startup.

Adapted rather than copied verbatim: this repo is the live, shipping mod
(not a from-scratch design rewrite like `Fuse_geometry_engine`), so both
agents may implement code from the start (no design-only phase), and the
constitution documents this repo's relationship to that parallel rewrite
effort — the two projects and their coordination logs stay independent.

The sibling folder `Narrow_Gauge-main` (a stale, non-git manual snapshot,
missing files present in this repo and differing in most others) was
deliberately left out of scope per the user's explicit instruction — this
setup only covers the real git repo.

Next: user to pick the first item of work.

### [Claude] 2026-07-06 — Ground-truthed all 14 broken special-work switches from a live session

User reported plain narrow turnouts mostly work but dual-gauge special work
is broken in-game (two screenshots showing a disconnected vertical rail stub
floating above normal track), and asked to get the other switches working
since `Fuse_geometry_engine` won't be ready in time — this repo needs direct
fixes, not a migration.

Read the user's live `Player.log` and `NarrowGauge/SpecialWorkPlans/*.txt` at
`C:\Users\roger\AppData\LocalLow\Giraffe Lab LLC\Railroader` (both get
overwritten next session, so I copied the relevant evidence into
`reviews/switch-validation-failures-2026-07-05.md` rather than leaving it
only in those external files). Confirmed `Special-work analysis: objects=14,
invalid=14` — literally every measured special-work switch on the map is
failing validation right now, contradicting `docs/special-work-turnout-combo-status.md`'s
2026-06-13 `DONE` markings for several of these same combos.

Categorized the 14 failures into 6 groups by apparent root cause rather than
treating each node independently: `dual.split-standard-narrow` deriving zero
blades (both instances, `Nove` + `NCustom_7n90`, `Nove` also throws a hard
native `SwitchGeometry.Calculate` exception), `dual.both-diverge` missing
`SharedDuplicate` suppression (5 of 7 instances — best match for the
reported visual bug, since one of the two other `dual.both-diverge`-adjacent
cases, `N178`, literally logs "Shared duplicate rail ... still renders"),
`dual.standard-branch-joins-main` never even attempting custom rendering
(both instances fall back to plain standard-gauge visuals), blade
under-build (2 narrow-branch instances + 1 both-diverge instance build 1
blade where 2-3 are expected), frog guard-rail/approach-section gaps, and
one single-node rail-role mismatch (`NCustom_g832`). Did not attempt a code
fix this turn — flagged `SectionedSpecialWorkBuilder.AddSharedSuppressions`'s
`RailParticipatesInAcceptedFrog` skip as an unconfirmed candidate for the
SharedDuplicate issue, but `git log -p -L` shows that function unchanged
since 2026-06-18, so the actual regression (if any) is upstream of it or
this specific node/orientation combination was simply never actually
verified despite the June 13 doc's claims.

Set `STATUS.md`'s backlog to the priority order in the review (split-
standard-narrow zero-blade fix first: worst broken + the only hard crash;
both-diverge SharedDuplicate second: highest node count and closest match to
the visual report; standard-branch-joins-main third; blade-count/frog-guard
issues and the single g832 mismatch after).

Next: Codex, backlog item 1. Ask the user for a fresh `Player.log` after any
fix lands rather than trusting validation logic alone — they're available to
re-test in-game.

### [Codex] 2026-07-06 00:16 - Fix split-standard-narrow blade fallback and control placement

Picked up backlog item 1 (`dual.split-standard-narrow`). Read the live
exported plans for `Nove` and `NCustom_7n90`: both had `blades=0` and "no
truth table matched; measured geometry fallback used." Root cause in the
sectioned builder was that split blades were only emitted from matched
truth-table selectors or from generic normal/reversed route pairs; the live
right/right crossing geometry matched neither split selector, and the split
routes are not a simple same-family normal/reversed pair. Added a measured
split fallback in `SectionedSpecialWorkBuilder` that derives one
`narrow-separation` blade from same-side `standard-through`/`narrow-diverge`
rail divergence, scoring movable ownership by frog participation and then
`narrow-diverge` preference. Updated the `dual.split-standard-narrow` preset
expectation to one movable assembly.

Also traced `Nove`'s native `SwitchGeometry.Calculate` exception to the
runtime hidden control segment being placed from the generated ghost node's
stored rotation. For split topology the hidden control leg should follow the
physical standard-through continuation, translated to the generated narrow
node, so `SpecialWorkTopologySynchronizer` now derives the control pose from
the source node's standard-only continuation tangent and tags the hidden
control segment as `dual.split-standard-narrow`.

Verification this turn: `dotnet build .\NarrowGaugeMod.csproj` succeeded
with 0 warnings and 0 errors. Not in-game verified yet; next turn should
review this diff and ask the user for a fresh `Player.log` to confirm
`Nove` no longer throws "Switch tracks do not intersect" and both split
instances now derive a blade. Watch `Nove`'s `shared=0` specifically; the
control-route correction may restore it, but that needs live confirmation.

### [Codex] 2026-07-06 00:17 - Finalize dual split hidden-control bypass

Follow-up correction to the previous entry: the `Nove`
`SwitchGeometry.Calculate` exception was not fully explained by control-node
placement. The live failing pair was hidden-control plus the real narrow
branch, while `Patch_SwitchGeometry_Calculate` only bypassed hidden-control
plus generated-ghost pairs. Updated `Patches.cs` so any exactly-one-hidden
segment pair at a gauge-separation control node uses the control-shell
geometry instead of native/narrow switch geometry.

Also tightened the dual-split blade fix in `SectionedSpecialWorkBuilder.cs`:
`dual.split-standard-narrow` now skips the generic binary-switch fallback and
uses one measured split blade selected from standard/narrow-diverge or
generated narrow-through/narrow-diverge candidates. This should avoid both
zero blades and accidental two-blade generic output.

Verification this turn: ran
`dotnet build NarrowGaugeMod.csproj -p:RailroaderDir="C:\Steam\steamapps\common\Railroader"`
after the final patch; it succeeded with 0 warnings and 0 errors. Not
in-game verified from this session. Live verification still needed:
`Player.log` should show `Nove` and `NCustom_7n90` as `valid=True`, no
captured `SwitchGeometry.Calculate` failure for `fuse-ng:n:Nove`, and the two
split-standard-narrow switches should be visually inspected. Next: Claude
review, then user live re-test or backlog item 2.

### [Codex] 2026-07-06 00:18 - Build-command correction

Correction to the previous log entry's verification sentence: the build I
actually ran this turn was `dotnet build .\NarrowGaugeMod.csproj` from the
repo root, not a build with an explicit `RailroaderDir` property override.
It succeeded with 0 warnings and 0 errors. The rest of the verification
state is unchanged: no in-game verification was performed in this turn.

### [Claude] 2026-07-06 00:35 - Reviewed Codex's item 1 fix, landed item 2 (SharedDuplicate suppression)

Reviewed Codex's commit `af43867` by reading the actual diffs, not the
summary. `Patches.cs`: widened `Patch_SwitchGeometry_Calculate`'s
control-shell condition from "hidden-control + visible-ghost" to "exactly
one of the pair is hidden-control" - confirmed safe because
`IsGaugeSeparationControlNode` (the outer gate) is unchanged and already
strictly requires the 3-segment hidden-control/visible-ghost/real-narrow
shape, so this only affects nodes already in that shape, matching Nove's
actual failing pair (hidden-control + real narrow branch). `SectionedSpecialWorkBuilder.cs`'s
new `TryBuildMeasuredDualSplitBlade` fallback is gated by
`IsDualSplitPreset(definition)` and only runs `if (!yieldedAny)`, so it's
purely additive for the split preset and can't affect any other preset's
blade output. `SpecialWorkTopologySynchronizer.cs`'s hidden-control-segment
retag from `DualNarrowBranch` to `DualSplit`: confirmed
`EnsureRuntimeGaugeSeparationControls` only ever runs on nodes matching
`IsGaugeSeparationTopology` (1 dual + 1 standard-only physical segment at
the source, exactly 1 real narrow branch off the ghost node) - a shape that
describes split-standard-narrow specifically, not narrow-branch-joins-main
(which would have dual+dual/narrow physical segments, not dual+standard-only).
So the retag corrects a stale/wrong preset id rather than reclassifying
already-working narrow-branch nodes. Independently reran the build (not just
trusted Codex's report) - confirmed 0 warnings/0 errors. Verdict: agreed, no
disagreement raised.

Then picked up backlog item 2 myself (`dual.both-diverge` missing
`SharedDuplicate` suppression - the one most likely tied to the user's
reported floating-rail-stub screenshots). Root cause: `RailParticipatesInAcceptedFrog(rail,
frogs)` checked whether a rail participates in an accepted frog *anywhere on
its whole length*, then used that as a reason to skip cutting a
shared-duplicate interval entirely - even when the shared interval being
considered was nowhere near that frog. A rail can legitimately need both "cut
this duplicate over here" and "keep this frog-adjacent section over there" at
different points along its own length; the whole-rail check conflated the
two and left the duplicate uncut, which is exactly what produced N178's
"Shared duplicate rail 'narrow-reversed:left' still renders" and the 5 other
both-diverge nodes' "missing required suppressed interval kind
'SharedDuplicate'" truth-table failures.

Fix: replaced the whole-rail check with
`RailParticipatesInAcceptedFrogNearInterval(rail, frogs, start, end)`, which
compares each frog's actual position along `rail` (via
`RailIntersection.DistanceA`/`DistanceB`, whichever side matches) against the
specific interval being considered, with a margin of
`Max(frog.CutHalfLength, MinimumPieceLength)` so a frog just outside the
interval's exact bounds still counts as protecting it. Updated all 4 call
sites: `AddSharedSuppressions` (moved `start`/`end` computation before the
check instead of after), `AddCrossFamilySharedSuppressions` (moved the check
inside the per-overlap-interval loop instead of once per rail pair),
`SuppressDualBothDivergeFrogDuplicate` (interval already available, just
swapped the call), and the truth-table validator's own "still renders"
diagnostic (computed the same distances the builder does, for consistency
between what gets cut and what gets flagged). Removed the now-unused
whole-rail `RailParticipatesInAcceptedFrog` method rather than leaving it as
dead code.

Verification this turn: `dotnet build NarrowGaugeMod.csproj
-p:RailroaderDir="C:\Steam\steamapps\common\Railroader"` - 0 warnings, 0
errors. This mod has no unit test project (Unity/UMM-dependent, unlike
`Fuse_geometry_engine`), so build success plus the code-level reasoning above
is the only verification available without a live session - **not**
in-game verified. The five `dual.both-diverge` nodes
(`NCustom_l4a4`/`ltci`/`p997`/`u6n0`, `NDeHartPassing_wqbb`) and `N178`
should be rechecked in a fresh `Player.log` for `valid=True` and no "still
renders"/"missing required suppressed interval" messages.

Did not start backlog item 3 (`dual.standard-branch-joins-main` never
attempted) this turn - handing back to Codex per the turn procedure.

Next: Codex, backlog item 3.

### [Claude] 2026-07-06 - Deployed for live testing, confirmed Codex available again

Codex's earlier usage-limit block (see previous open-questions note) has
cleared - confirmed with a no-op `codex exec` call. Before resuming the
backlog, checked the actually-installed mod at
`C:\Steam\steamapps\common\Railroader\Mods\FUSE.NarrowGauge`: the deployed
`NarrowGaugeMod.dll` was dated 2026-06-14, well before even the original
`Player.log` bug report - none of items 1 or 2's fixes (or anything back to
commit `576be80`) had ever actually been loaded in-game. Rebuilt with
`dotnet build NarrowGaugeMod.csproj -p:RailroaderDir="C:\Steam\steamapps\common\Railroader" -p:EnableModDeploy=true`
to deploy the current build. User asked explicitly to make sure we're
testing in-game, so pausing the code backlog here until a fresh `Player.log`
confirms or refutes items 1+2 - resuming further fixes on unverified
guesses risks compounding an actually-still-broken foundation.

Next: user to launch Railroader and load the map; then re-check
`Player.log` for `Nove`/`NCustom_7n90`/both-diverge validity before
resuming with Codex on item 3.

### [Claude] 2026-07-06 - User confirms valid=True still visually broken; found the real cause

User tested in-game and reported: even switches that "show good" (i.e. pass
validation) are still sometimes visually broken. Fresh `Player.log` after
this session's deploy showed `Special-work analysis: objects=14, invalid=0`
- every switch reports valid, including `dual.standard-branch-joins-main`
(`fl15`/`33d6`) and `NCustom_g832` (backlog items 3/5) which neither of us
had touched yet, and `Nove`/`NCustom_7n90` which are now classified as
`dual.narrow-branch-joins-main` instead of `dual.split-standard-narrow` (a
side effect of item 1's control-geometry/tag fix). User then shared 8
in-game screenshots showing small disconnected white rail/guard-rail
fragments floating near frogs and along diverging routes on multiple
switches, confirming valid=True is not sufficient.

Cross-referenced the fresh log against the screenshots and found the real
cause: `ValidateSectionedDualGaugeSpecialWork` in
`SectionedSpecialWorkBuilder.cs` logs two checks as `Main.Warn(...)`
"Rendering anyway" rather than `yield return` validation failures - a blade
not connecting into a rendered closure/fixed section after its root
distance, and the diverging fixed narrow stock/running rail having no
renderable role sections at all. Both fired in the fresh log, across the
`dual.narrow-branch-joins-main` nodes specifically. Per earlier commit
messages (`ae87b7e`, `8a5ea4d`), these were deliberately downgraded from
hard failures to warnings in the past - which means the underlying "this
rail/blade has no actual continuous renderable geometry" problem was never
fixed, just hidden from the valid/invalid signal. This is a strong, concrete
match for the floating-fragment screenshots and is now the new top-priority
backlog item, ahead of the old items 3-5 (which, per the same fresh log,
separately and unexpectedly now report valid=True too - worth re-verifying
visually rather than assuming items 1+2 incidentally fixed them).

Updated `reviews/switch-validation-failures-2026-07-05.md` with a "Critical
update" section, and rewrote `STATUS.md`'s backlog around this finding, plus
added a standing rule: don't trust `valid=True` alone going forward, and
don't fix a validator gap by relaxing it further - fix the geometry or
restore the check once fixed.

Also confirmed (again) that plain `dotnet build` does not update the
installed mod - `-p:EnableModDeploy=true` is required for the user to
actually test a fix in-game. Documented this explicitly in STATUS.md's
Verification section so it isn't missed again.

Did not attempt the top-priority fix myself this turn - handing to Codex
fresh with full context, since this needs real investigation into
`RailRoleSection` construction, not a quick patch.

Next: Codex, top-priority item (narrow-branch rendering gaps behind relaxed
warnings).

### [Codex] 2026-07-06 00:58 - Fix narrow-branch geometry gaps hidden by warnings

Picked up the top-priority narrow-branch visual-gap item. Read `STATUS.md`,
the latest `LOG.md` tail, and the critical update in
`reviews/switch-validation-failures-2026-07-05.md`, then inspected
`SectionedSpecialWorkBuilder.cs` against the fresh exported
`SpecialWorkPlans` in the user's Railroader profile.

Findings: the "fixed diverging narrow stock/running rail has no renderable
role sections" check was often looking at the wrong rail. It selected the
first narrow stock rail from the blade list; on the left-hand narrow-branch
truth shape that is the shared/through duplicate (`narrow-normal:right`),
which can be intentionally fully suppressed. The actual fixed narrow stock
rail in the truth/anatomy is the `narrow-reversed` stock. Separately,
`NCustom_7n90` had a blade root measured all the way to a route endpoint,
leaving no closure/fixed section after the root. The exported N178 plan also
showed the fragment-producing path: a rail cut as a `SharedDuplicate` loser
could still be used as a frog/wing hardware source, so replacement hardware
was built on a suppressed duplicate rather than the nearby rendered physical
owner.

Fixes in `SectionedSpecialWorkBuilder.cs`: `ResolveDivergingFixedStockRail`
now prefers narrow stock rails sourced from `narrow-reversed`; blade
measurement reserves a short fixed closure when the measured blade root would
otherwise run to the rail endpoint; and after shared-duplicate cuts are known
but before frog cuts/hardware are built, frog candidates rehome any frog rail
that is already cut as a `SharedDuplicate` loser to a nearby unsuppressed
physical owner within rail-head/flangeway tolerance. Restored the two named
"Rendering anyway" validation downgrades to hard `yield return` failures, so
if this path is still geometrically wrong the fresh log should go invalid
instead of silently reporting `valid=True`.

Verification this turn: ran
`dotnet build NarrowGaugeMod.csproj -p:RailroaderDir="C:\Steam\steamapps\common\Railroader" -p:EnableModDeploy=true`.
It succeeded with 0 warnings and 0 errors and reported that
`NarrowGaugeMod.dll` was deployed to
`C:\Steam\steamapps\common\Railroader\Mods\FUSE.NarrowGauge`. This is not
in-game visual verification; the user still needs to launch Railroader and
confirm with fresh screenshots/`Player.log` whether the floating fragments are
gone.

Next: Claude should review the diff, then ask the user to test the deployed
build in-game. In the fresh log, check for any hard validation issues replacing
the old blade-root/fixed-stock warnings on the five
`dual.narrow-branch-joins-main` nodes.

### [Codex] 2026-07-06 01:02 - Tighten rehomed frog hardware handling

After the narrow-branch fix commit appeared, kept the worktree follow-up
focused on the shared-duplicate/frog-owner path. Tightened
`SectionedSpecialWorkBuilder.cs` so rehomed frog rails choose the closest
unsuppressed physical owner before family tie-breaks, and added a
`CollapseDuplicateFrogHardware` pass to drop duplicate frog candidates when
rehoming puts them on the same physical rail pair. This is intended to prevent
the fix from merely moving duplicate/fragmented frog hardware from a
suppressed route rail onto its rendered owner.

Verification: reran
`dotnet build NarrowGaugeMod.csproj -p:RailroaderDir="C:\Steam\steamapps\common\Railroader" -p:EnableModDeploy=true`.
It succeeded with 0 warnings and 0 errors and deployed the DLL to the live
mod folder. Still needs user in-game verification with fresh screenshots and
`Player.log`.

### [Claude] 2026-07-06 01:20 - Reviewed both Codex fixes; added geometry-continuity diagnostic tooling

Reviewed Codex's two most recent commits by reading the actual diffs, not
summaries. `2b6cef8`: `RehomeSharedDuplicateFrogRail`/`ResolveFrogHardwareRail`
detects when a frog's rail was cut as a `SharedDuplicate` loser at that exact
position and re-homes the frog hardware onto a nearby unsuppressed rail
before frog/wing/guard geometry is built - a real match for "frog hardware
attached to a rail that itself gets hidden," a strong candidate for the
floating-fragment symptom. `ResolveDivergingFixedStockRail` now prefers the
stock rail whose `SourceRouteIds` actually contains `narrow-reversed`
instead of an arbitrary first match. `ReserveEndpointClosure` stops a blade
root from consuming an entire route's remaining length. The two `Main.Warn`
"Rendering anyway" checks are `yield return` failures again, exactly as
asked. `916ee61` (landed by the time I checked back - user or a parallel
Codex session ran a follow-up turn): `CollapseDuplicateFrogHardware` merges
frog candidates that ended up rehomed onto the same physical rail pair
(keeping the larger `CutHalfLength`), and reordered the rehoming tie-break to
prefer closest physical rail over family. Both commits agreed - well-scoped,
consistent with the stated problem, no disagreement raised. Independently
rebuilt combined state (not just trusted either report): 0 warnings/0
errors.

User then relayed real domain feedback from in-game testing: defects cluster
around K-frog castings, blade position, and rotation, and explicitly asked
for better diagnostic tooling since neither agent can see the running game.
Built this into `SpecialWorkPlanExporter.cs`'s per-switch `.txt` export
(`NarrowGauge/SpecialWorkPlans/*.txt`):

- `PieceEndpoints` section: world-space position + tangent direction for
  every rendered piece's both endpoints (fixed rail pieces, wing rails,
  guard rails, blade curves, and blade closure curves) - the export
  previously only had rail-relative stationing, never actual 3D
  position/orientation.
- `GeometryContinuity` section: for every piece, checks whether each
  endpoint actually meets another piece's endpoint in world space (within
  0.12m) rather than trusting that contiguous station numbers mean
  contiguous geometry. A piece with no connected neighbor at either end is
  flagged `ISOLATED` - "this is almost certainly a disconnected floating
  fragment in-game," directly matching the defect class from the user's
  screenshots, readable from the exported `.txt` instead of inferred from a
  screenshot. Where two pieces do join, compares tangent lines at the joint
  and flags `ANGLE MISMATCH` past 20 degrees - a candidate for the
  "rotational issues" the user described.

Used `Core.LineCurve.Head`/`Tail` (each a `LinePoint` with `.point`/
`.direction`) for endpoint geometry, not `EndPoint1`/`EndPoint2`/
`GetDirection` - those exist on a different `LineCurve`-named type used
elsewhere in this codebase (`SegmentProxy.Curve` in
`NarrowGaugeSwitchGeometry.cs`); caught this from a real build failure, not
assumed.

Deliberately did not try to guess a fix for any specific remaining defect
from this alone - the point was to build the instrument, not diagnose blind
before it exists. Rebuilt and deployed the combined state:
`dotnet build NarrowGaugeMod.csproj -p:RailroaderDir="C:\Steam\steamapps\common\Railroader" -p:EnableModDeploy=true`
- 0 warnings/0 errors, deployed successfully. Not in-game verified - this is
new tooling; its own output hasn't been read against a real session yet.

Next: user to launch Railroader and re-test in-game (this deploy includes
both Codex fixes and the new diagnostic). Then read the fresh
`ISOLATED`/`ANGLE MISMATCH` lines in each switch's `.txt` export alongside
`Player.log` - that should let whoever's turn is next diagnose remaining
visual defects without needing new screenshots for every iteration.

### [Claude] 2026-07-06 01:35 - Fixed a real gap in the new diagnostic before trusting its first output

User re-tested in-game with the previous deploy; still `objects=14,
invalid=0`, but the new `GeometryContinuity` section flagged `ISOLATED`
pieces on literally all 14 switches, including several `v2-guard:*` pieces
on every single one. That pattern (100% of switches, same category every
time) was suspicious enough to check the diagnostic itself before reporting
any of it as a real bug.

Found two real problems in `SpecialWorkPlanExporter.cs`'s new code:

1. `CollectPieceEndpoints` never included `plan.FrogPieces` - a completely
   separate `RailPiece` collection from `plan.Frogs` (the abstract
   candidates) holding the actual frog casting geometry. Any fixed/wing
   piece that legitimately connects to a frog casting would have been
   falsely flagged `ISOLATED` purely because the frog geometry wasn't in the
   connectivity graph at all. Fixed by adding a `FrogPiece` category loop.
2. Guard rails (confirmed by reading `BuildGuardRails`/`GuardLeadLength`/
   `GuardTrailLength` in `SectionedSpecialWorkBuilder.cs`) are built as a
   fixed lead/trail length measured from the frog center, not sliced to
   join any other piece - both ends are *always* meant to be free-standing
   (tie-mounted alongside the rail, not rail-joined at either end, matching
   real guard-rail construction). Flagging every guard rail as `ISOLATED`
   was a guaranteed false positive on every switch, not a signal of
   anything. Excluded the `Guard` category from the `ISOLATED` verdict
   (still included in the connectivity graph as a candidate neighbor for
   other pieces, just never itself flagged).

Wing rails were checked too and left as-is: they're sliced from the frog
point outward (`BuildWingRails`), so they legitimately connect at one end
(the frog) and flare free at the other - the existing "flag only if *both*
ends are unconnected" check already handles that correctly without change.

Rebuilt and redeployed:
`dotnet build NarrowGaugeMod.csproj -p:RailroaderDir="C:\Steam\steamapps\common\Railroader" -p:EnableModDeploy=true`
- 0 warnings/0 errors. The previous round's `ISOLATED` findings should not
be treated as confirmed bugs - they were substantially (possibly entirely)
an artifact of the tool's own coverage gap. Needs one more fresh in-game
session before any `ISOLATED`/`ANGLE MISMATCH` finding from this tool is
trusted.

Next: user to re-test in-game one more time with this corrected build.
Whoever reads the resulting `.txt` exports should treat only `Fixed`/
`Blade`/`Closure`/`Wing`/`FrogPiece` category `ISOLATED` findings as real
candidates now - `Guard` findings are suppressed as expected-by-design.

### [Claude] 2026-07-06 01:45 - Scoped a broad investigation instead of continuing to patch symptom-by-symptom

User kept testing in-game with the mod's debug label overlay on and sent
several more screenshots (exact segment IDs visible: `fuse-ng:s:Nove:control`,
`NCustom_7n90:control`, `SCustom_e6i0`, `SCustom_ttpp`, others), then gave a
direct symptom report: "double frogs, blades being on the outside or wrong
side of the rail, and oddness where it seems there is too many rails or
even maybe a transition in the middle of a switch."

Checked `SCustom_ttpp` (visibly showing a disconnected fragment in one
screenshot) against all 14 measured special-work plan exports -
`grep -rl "ttpp" NarrowGauge/SpecialWorkPlans/*.txt` found nothing, and
`Player.log` only shows ordinary `SpecialWorkSegmentClip`/
`SpecialWorkTieClip` entries for it (rail-clipping near a special-work
zone, not membership in a measured plan). Its neighbors in that log
sequence are a narrow-only segment then another dual-gauge segment -
exactly the shape of an ordinary narrow-branch-off-dual transition handled
by the **plain track pipeline** (`NarrowGaugeTrackBuilder.cs`/
`NarrowGaugeSwitchGeometry.cs`), not `SectionedSpecialWorkBuilder.cs`. This
is a real finding, not a guess: **the bug surface spans at least two
separate systems**, and the plain pipeline has no diagnostic tooling and no
confirmed root cause at all right now.

Asked the user directly whether to keep reacting to individual screenshots
or stop and scope a real investigation - they chose the investigation.
Wrote `reviews/broad-visual-defect-investigation-2026-07-06.md`: breaks the
four reported symptoms into distinct failure-mode hypotheses (double frogs
- possibly the rehoming/collapse logic from this session's fix interacting
badly with a case it wasn't designed for; wrong-side blades - a
`RailSide`/`Hand` orientation question neither of this session's fixes
touched; too many rails - either a suppression gap this session's fix
didn't cover, or a genuinely extra authored/generated rail; mid-switch
transition - unclear, needs a targeted screenshot once the others are
understood), documents the `SCustom_ttpp` cross-system finding in full, and
explicitly tells the next agent not to patch symptom-by-symptom or assume
`SectionedSpecialWorkBuilder.cs` is the only place to look.

Rewrote `STATUS.md` to hand this off as an investigation, not a fix - listed
everything confirmed-landed so far so it doesn't get re-litigated, restated
the standing rules, and pointed the next turn at the review file with
explicit instructions to write findings before proposing any code change.

Next: Codex, full investigation per
`reviews/broad-visual-defect-investigation-2026-07-06.md`. Do not patch yet.

### [Codex] 2026-07-06 01:52 - Mapped broad visual defects across measured and plain renderers

Completed the investigation scoped in
`reviews/broad-visual-defect-investigation-2026-07-06.md` and wrote findings
to `reviews/plain-and-measured-visual-defect-findings-2026-07-06.md`. No code
patch was made.

Confirmed `SCustom_ttpp` is not a measured special-work node or a missing
15th plan. The authored graph has it as an ordinary `DualGauge_R` segment
from `NCustom_fl15` to `NCustom_ltci`; both endpoint nodes have measured
special-work exports. Its rails are built by the plain dual-gauge segment
path in `NarrowGaugeTrackBuilder.cs`, then clipped by
`CreateRailMeshesWithFrogCuts`. The current `[SpecialWorkSegmentClip]` log
label is ambiguous because that helper merges measured ownership cuts,
gauge-separation frog cuts, and shared-rail flip cuts under one label.

Mapped the four reported symptoms:

- double frogs: most likely measured special-work frog/compound-vee handling
  on nodes such as `NCustom_fl15`, `NCustom_ltci`, or `NCustom_fc97`, not the
  plain `SCustom_ttpp` segment itself;
- wrong-side/outside blades: still needs a labeled node, but the plain
  dual-to-narrow split switch path has a strong code hypothesis because it
  hardcodes `aThirdRails.right` instead of consulting the shared-rail side;
- too many rails: could be measured fixed/shared pieces, generated transition
  duplicate suppression, or the current reverse-orientation mixed-switch
  fallback;
- possible mid-switch transition: not confirmed by logs; shared-rail flip is
  disabled and no current `SharedRailTransition` log entries were found for
  the `SCustom_ttpp` area.

Also found a diagnostic caution: live plan exports still show guard
`ISOLATED` lines even though the checked-out exporter suppresses guard-only
isolation. Treat current guard isolation output as stale/mismatched diagnostic
evidence; fixed-piece isolation near `NCustom_fl15`/`NCustom_ltci` remains a
plausible measured-fragment candidate.

No build or deploy was run because this was an investigation-only turn and
only coordination/review Markdown files changed.

Next: Claude to review the new findings and choose either a targeted
cut-source diagnostic for `SCustom_ttpp` or a focused debug-labeled screenshot
mapping before any geometry patch.

### [Claude] 2026-07-06 01:55 - Fixed the plain-pipeline aThirdRails.right bug; found why "fresh" tests weren't fresh

User pointed out a specific visual defect at `Nove`: the switch blade sits
behind the switch stand, running toward the switch center instead of away
from it. Also asked for two things: (1) find a way for Claude and Codex to
test/fix without the user, since Codex can apparently launch and close
Railroader itself, and (2) make sure Claude and Codex actually check each
other's work, not just work in isolation.

Reviewed Codex's investigation findings by independently reading the cited
code (not trusting the write-up alone). Confirmed the `aThirdRails.right`
hardcode in `CreateDualGaugeNarrowSplitSwitchRailObjects`
(`src/NarrowGaugeTrackBuilder.cs`) is real: the function immediately above
it in the same file, plus 11 other call sites across the file, all resolve
the dual middle rail via `DualGaugeSharedRailRegistry.SharesRightRail(segment)
? .left : .right`, but this one function skips that check entirely.
Reasoned that the fix is safe to apply now, contrary to Codex's more
cautious "wait for a labeled screenshot" stance: the hardcode is only wrong
when `SharesRightRail` is true (correct answer would be `.left`); when
false, `.right` is already correct, so applying the same conditional pattern
cannot regress an already-working case, only fix the definitely-wrong one.
Also found a third `.right` hardcode in `CalculateDualGaugeNarrowSplitSlices`
but confirmed via grep it has zero callers anywhere in `src/*.cs` - dead
code, left alone. Fixed the two live call sites, verified build (0
warnings/0 errors).

While investigating whether the diagnostic-tool fix from an earlier turn had
actually been tested (Codex's turn flagged live exports still showing guard
`ISOLATED` lines despite the checked-out source suppressing them), found the
real explanation: `Player.log` contains exactly one `Initialize engine
version` line and exactly one `[FUSE.NarrowGauge] Version '0.4.0'. Loading.`
line for its entire current content - the user's Railroader process has
been running continuously as one session this whole stretch. Since .NET
assemblies don't hot-reload, every screenshot and log/export from this
stretch reflects whichever build was deployed *before* that process
launched, not any fix deployed after (including my diagnostic fix and now
this `aThirdRails.right` fix). This is not a diagnostic bug - it's a process
bug in how we've been testing.

Rebuilt and redeployed the combined state (this fix + everything prior):
`dotnet build NarrowGaugeMod.csproj -p:RailroaderDir="C:\Steam\steamapps\common\Railroader" -p:EnableModDeploy=true`
- 0 warnings/0 errors. Rewrote `STATUS.md` around this finding: nothing from
the recent test stretch (SCustom_ttpp, the guard isolation lines, the Nove
blade screenshot, Codex's investigation's live-log evidence) can be fully
trusted until the user does a genuine full quit-and-relaunch of Railroader,
confirmed afterward by checking for a fresh single init/load line pair.
Flagged this as directly relevant to the user's automation ask: any
launch/test/close pipeline (using `FUSE.TestBridge`/`FUSE.LiveHarness`
per the earlier discussion) must guarantee a real process restart, not
assume a file-on-disk change takes effect.

Did not yet act on Codex's other findings (SCustom_ttpp cut-source
ambiguity, double-frog mapping, unmapped "too many rails"/mid-switch-
transition symptoms) - those still need the node/screenshot mapping Codex
recommended, and a trustworthy live test to check against.

Next: user to fully quit and relaunch Railroader, then re-test. Whoever
picks this up next should verify the fresh-session check before trusting
anything in the resulting log/screenshots.

### [Claude] 2026-07-06 02:45 - Fresh session confirms fixes hold; diagnostic validated; corrected the Nove mapping

User fully quit and relaunched Railroader. Verified freshness properly this
time: deployed DLL timestamp (01:59) is before the new `Player.log`'s
activity, and it shows exactly one engine-init/mod-load pair, same as a
stale session would - so I also checked wall-clock ordering, not just the
count, per the standing rule I wrote last turn.

Confirmed in the genuinely fresh session: `objects=14, invalid=0` holds -
all of this session's measured-system fixes (items 1/2, narrow-branch
rendering gaps) are real, not an artifact of a stale process. More
importantly, the `GeometryContinuity` diagnostic fix (`FrogPieces` coverage
+ `Guard` exclusion, from an earlier turn) actually works: 9 of 14 switches
now report zero isolated pieces, down from every single switch showing
multiple false positives before.

Investigated the one remaining pattern (all 5 `dual.narrow-branch-joins-main`
switches, and only those, each show one `ISOLATED` ~18-32m fixed piece) by
reading the actual `FixedRailPieces` data rather than trusting the verdict.
Found the outer `standard-through` rail in these switches is built as one
unsubdivided piece with both ends at the measured-zone boundary (legitimate,
not a gap), while the equivalent rail in a clean `dual.both-diverge` switch
(`NCustom_l4a4`) happens to be chopped into 3 chained pieces, giving each an
internal neighbor even though its outer boundaries are the same kind of
zone edge. Concluded this is very likely a diagnostic blind spot (can't
distinguish "real gap" from "single piece at the zone boundary"), not a
confirmed defect. Documented rather than acted on - refining this needs more
thought about reliably detecting a zone boundary from the plan data
available, and it isn't blocking anything right now.

Caught and corrected a mapping mistake before it stood: `Nove` is one of
the 14 measured special-work switches, so its blade geometry comes from
`SectionedSpecialWorkBuilder`/`SpecialWorkHardwareRenderer`, not
`CreateDualGaugeNarrowSplitSwitchRailObjects` (confirmed via
`vanillaRailObjects=0` for measured nodes in Codex's earlier investigation -
the plain/legacy pipeline is suppressed there). This means the
`aThirdRails.right` fix from last turn - real, and worth keeping - almost
certainly does not explain the Nove blade-orientation symptom the user
originally reported. Told the user this directly rather than letting an
unearned "fixed" stand. Asked for two specific checks (Nove's blade in the
fresh session, and a plain mixed switch outside the 14-name list to see the
actual fix's effect) - no answer yet.

Next: waiting on the user's report. If Nove's blade issue persists,
investigate `SpecialWorkHardwareRenderer.CalculateBladeOpenRotation`/
`CreatePointBlade` for the narrow-branch preset - a different code area
than anything patched this session so far.

### [Codex] 2026-07-06 09:05 - Proved FUSE.TestBridge live-game pipeline, with headless/env caveats

Built and deployed the sibling repo's `FUSE.TestBridge` into the live
Railroader install from
`C:\Hrogers_Railroader_mods_Projects\FUSE\FUSE.TestBridge`:
`dotnet build FUSE.TestBridge.csproj -p:EnableTestBridgeDeploy=true -p:GameDir="C:\Steam\steamapps\common\Railroader"`.
Build succeeded with 0 warnings and 0 errors. Confirmed from `Main.cs` and
the deployed `Info.json` that the runtime gate is either
`FUSE_TEST_BRIDGE=1` or deployed `Info.json` `"Enabled": true`.

Tried the requested headless path first: launched with
`FUSE_TEST_BRIDGE=1` plus `-batchmode -nographics`. It wrote one
`test_state.json` heartbeat from PID `21440`, then the heartbeat went stale
and a later `Railroader.exe -batchmode -nographics /editor` process had no
live bridge. Closed that failed attempt by `CloseMainWindow`/posted Windows
close messages; no force kill was used. Tried a normal graphics launch with
only `$env:FUSE_TEST_BRIDGE='1'`; that also did not enable the final process.
Railroader appears to hand off to a second `Railroader.exe /editor`, and the
final `Player.log` said `FUSE.TestBridge present but disabled`. Closed that
disabled session cleanly with `CloseMainWindow`.

The successful path was to temporarily set the deployed
`C:\Steam\steamapps\common\Railroader\Mods\FUSE.TestBridge\Info.json`
to `"Enabled": true`, launch `Railroader.exe` normally, then restore it to
`"Enabled": false` after shutdown. That produced a fresh connected heartbeat
from live PID `27828`. `FUSE.TestCli` could not be used from this sandbox
because `dotnet run` failed reading
`C:\Users\roger\AppData\Roaming\NuGet\NuGet.Config` with access denied, so I
used the documented file protocol directly (`test_request_<id>.json` /
`test_result_<id>.json`) and deleted result files after reading them.

Direct bridge requests worked:
`saves` returned `2026-06-25_auto1` and `2026-06-25`.
`loadSave` with arg `2026-06-25` returned `Ok=true` and
`Booting save '2026-06-25' from the main menu.` After load, `test_state.json`
showed `mapLoaded=true`, `canApply=true`, and a fresh heartbeat; `Player.log`
showed `[FUSE.NarrowGauge] Special-work analysis: objects=14, invalid=0,
elapsedMs=34074.` A `console` request running `/fuse.report json` returned
`Ok=true` with 17,586 bytes of JSON text, summary prefix
`FUSE: 20 loaded | faults 1 | conflicts 0 | assets 29 | graph 2 | transfers 0 | suppressions 130 | orphans 0 | /fuse.report`.
`umm close` returned `UMM window closed.` `screenshot` returned `Ok=true` and
artifact
`C:\Users\roger\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE-test-shots\narrow-gauge-harness-20260706-0903.png`
(3,686,542 bytes). I opened it: it captures the current in-game camera
exactly as-is, a trackside view with world labels and the top HUD; the
protocol has no camera-position argument. `cleanup` returned `Ok=true` /
`Removed 0 test save(s).`

Closed the successful Railroader session cleanly with `CloseMainWindow`; no
`Stop-Process`, `taskkill`, or forced termination was used. After shutdown,
the final successful `Player.log` freshness check passed: exactly one
`Initialize engine version` line, exactly one `[FUSE.NarrowGauge] Version`
line, exactly one `FUSE.TestBridge enabled; watching its folder for test
requests.` line, and exactly one `Special-work analysis: objects=14` line.
Restored the deployed bridge `Info.json` to `"Enabled": false` so normal
future launches stay inert.

No NarrowGauge source changed this turn. Next: Claude can build on this by
adding a small local harness/wrapper if desired; it should toggle deployed
`Info.json` true before launch and false after shutdown unless a better
environment-propagation path is found. Do not assume true `-batchmode
-nographics` works yet, and do not assume screenshots are camera-controlled.

### [Claude] 2026-07-06 09:10 - Verified the TestBridge harness independently; found the camera-control gap

Did not just trust Codex's report - checked each claim directly. `tasklist`
confirmed Railroader was not left running. Read the deployed
`FUSE.TestBridge/Info.json` directly and confirmed `"Enabled": false` was
genuinely restored, not just claimed. Viewed the captured screenshot
(`FUSE-test-shots/narrow-gauge-harness-20260706-0903.png`) directly - it's a
real in-game trackside view with World Labels on, showing real segment IDs
(`SCustom_e6i0`, `fuse-ng:s:SCustom_47ab`, etc.), proving this was a genuine
running session end to end, not a fabricated or stale result. Verdict:
confirmed, no disagreement.

This gives us real working automation for validation-level testing: build,
deploy, toggle the bridge on, launch, load the user's actual save by name,
wait for `mapLoaded`/`Special-work analysis`, run console commands for
structured data, close cleanly, restore the bridge to disabled. Either agent
can now check a fix's log-level effect independently, without the user
launching anything.

Then looked for what Codex's turn flagged as the remaining gap: camera
control for targeted screenshots. Checked the base game's decompiled
console commands - found `/tp <place>`
(`Decompiled dlls base game/Assembly-CSharp/UI/Console/Commands/TeleportCommand.cs`),
but it only accepts a predefined named `SpawnPoint` or an existing `Car` by
name, not arbitrary coordinates or a track node ID. Checked FUSE's own
console commands too - nothing camera-related. So `screenshot` still only
captures whatever the camera already happens to be looking at; it cannot
target a specific switch like `Nove` on demand yet.

Documented the concrete option to close this gap (a new `/ng.goto <nodeId>`
debug console command in NarrowGaugeMod itself, mirroring `/tp`'s
`CameraSelector.shared.JumpToPoint` call but resolving a track node's world
transform instead of a spawn point) - not built, since this is a real
scoped feature decision for the user to make, not something to build
unasked. Rewrote `STATUS.md` around this decision point.

Next: waiting on the user - build the camera-goto command for full
automated visual verification, or proceed with the now-proven
validation-level automation and continue relying on the user's own
screenshots for anything visual.

### [Claude] 2026-07-06 09:25 - Built the camera-goto bridge; user authorized full autonomy

User authorized proceeding without further check-ins ("do whatever you need
to do to keep working on this, without my input"). Also asked whether the
automation was running headless - corrected this: the successful runs used
a normal graphical launch with the `Info.json` toggle, not true
`-batchmode`/`-nographics` (that path was tried and didn't reach a
connected bridge state, per the previous turn's findings).

Investigated wiring a real console command into `FUSE.TestBridge`'s
automation for camera control, and found a real dead end rather than
guessing: `FuseTestApi.Commands()` (`FUSE/Testing/FuseTestApi.cs`) builds
its dispatch table only from `FuseConsoleCommands.CreateAll()` - confirmed
by reading the method - so it never reaches the real game's
`ConsoleCommandHandler._commands` dictionary at all. A NarrowGaugeMod
command registered the way FUSE registers its own (reflection into
`ConsoleCommandHandler.Register<T>`, per `FuseLegacyAssemblyHost.cs`) would
work in the interactive console but be invisible to the automated bridge.
Making it reachable through the bridge would require editing the separate
FUSE repo - out of scope to do unasked.

Built a self-contained alternative instead, entirely within this repo:
`src/NarrowGaugeTestBridge.cs`, a small file-based bridge modeled on
`FUSE.TestBridge`'s own request/result-file pattern but scoped to one job -
move the camera to a named track node. Verified the exact camera API by
reading `TeleportCommand.cs` and `CameraSelector.cs` directly rather than
guessing: `CameraSelector.shared.JumpToPoint(gamePoint, rotation, null)`,
where `gamePoint` is `Helpers.WorldTransformer.WorldToGame(node.transform.position)`
- confirmed `WorldToGame` is a real public static method, not assumed.
Drop `ng_goto_request.json` (`{"nodeId": "Nove"}`) next to the deployed
`NarrowGaugeMod.dll`; the bridge polls every 0.5s, resolves the node via
`Graph.Shared.GetNode(nodeId)`, jumps the camera, deletes the request file,
and writes `ng_goto_result.json` with `{"ok": ..., "message": ...}`. Gated
behind `NARROWGAUGE_TEST_BRIDGE=1` so it's inert for normal players,
mirroring `FUSE.TestBridge`'s own dev-only gate. Registered it as a new
component on the existing `ManagerObject` in `Main.cs`.

Built and deployed:
`dotnet build NarrowGaugeMod.csproj -p:RailroaderDir="C:\Steam\steamapps\common\Railroader" -p:EnableModDeploy=true`
- 0 warnings/0 errors. Not yet tested live - flagged a real risk in
`STATUS.md` before handing off: the `FUSE_TEST_BRIDGE` env var didn't
propagate to the actual game process last turn (a re-launch hop dropped
it), so `NARROWGAUGE_TEST_BRIDGE` might have the identical problem and
needs to be verified empirically, not assumed to work just because the
code is gated the same way conceptually.

Next: Codex, test the full loop (launch, load save, goto Nove, screenshot,
look at the actual image, close cleanly) end to end and report exactly what
happened - including describing the screenshot's real content, not just
whether requests returned Ok=true.

### [Codex] 2026-07-06 10:00 - Proved Nove goto/screenshot loop; documented env relaunch failure and direct-editor workaround

Read `STATUS.md` in full, skimmed the recent `LOG.md` tail, checked for open
coordination disagreements, and inspected `src/NarrowGaugeTestBridge.cs`,
`src/Main.cs`, deployed `FUSE.TestBridge/Info.json`, and the FUSE test-bridge
file protocol before running anything. Worktree was clean at start. No
`Railroader.exe` process was running.

Built/deployed before the first live attempt:
`dotnet build .\NarrowGaugeMod.csproj -p:RailroaderDir="C:\Steam\steamapps\common\Railroader" -p:EnableModDeploy=true`.
Result: build succeeded, 0 warnings, 0 errors, deployed
`NarrowGaugeMod.dll` to
`C:\Steam\steamapps\common\Railroader\Mods\FUSE.NarrowGauge`.

Initial test of the exact normal relaunch recipe:

- Toggled deployed
  `C:\Steam\steamapps\common\Railroader\Mods\FUSE.TestBridge\Info.json` to
  `"Enabled": true`, cleared stale `test_request_*.json` /
  `test_result_*.json`, cleared stale Narrow Gauge goto files, set
  `$env:NARROWGAUGE_TEST_BRIDGE='1'`, then launched
  `C:\Steam\steamapps\common\Railroader\Railroader.exe` normally with no
  `-batchmode` and no `/editor`.
- Fresh FUSE heartbeat connected on PID `45648`, command line
  `C:\Steam\steamapps\common\Railroader\Railroader.exe /editor`, parent PID
  `21028` (`steam.exe`).
- `Player.log` showed
  `[FUSE.TestBridge] FUSE.TestBridge enabled; watching its folder for test requests.`
  and did not contain any `NarrowGaugeTestBridge` enable line.
- Sent FUSE request:
  `{"schema":1,"requestId":"codex-load-20260706-093928","verb":"loadSave","arg":"2026-06-25",...}`.
  Result file content:
  `{"schema":1,"requestId":"codex-load-20260706-093928","ok":true,"text":"Booting save '2026-06-25' from the main menu.","completedUtc":"2026-07-06T13:39:28.7935868Z"}`.
- Waited for a fresh heartbeat after load and found
  `mapLoaded=true`, `canApply=true`, and
  `[FUSE.NarrowGauge] Special-work analysis: objects=14, invalid=0, elapsedMs=34119.`
- Wrote
  `C:\Steam\steamapps\common\Railroader\Mods\FUSE.NarrowGauge\ng_goto_request.json`
  with content:
  `{"nodeId":"Nove"}`.
  After 5 seconds no `ng_goto_result.json` appeared, and
  `ng_goto_request.json` still existed with the same content.
- Conclusion: with the normal Steam relaunch path,
  `NARROWGAUGE_TEST_BRIDGE=1` does not reach the final `/editor` process.
  This matches the earlier `FUSE_TEST_BRIDGE` env-var finding.

Closed that first session cleanly:

- Sent FUSE `umm` request with `arg:"close"`.
  Result: `Ok=true`, text `UMM window closed.`
- Called `CloseMainWindow()` on PID `45648`; returned `true`.
- Confirmed no remaining `Railroader.exe` processes.

Launch/workaround investigation:

- Tried direct final process launch:
  `Start-Process Railroader.exe -ArgumentList '/editor'` with
  `NARROWGAUGE_TEST_BRIDGE=1`. It wrote one heartbeat from PID `8496` and
  exited. `Player.log` ended with `InvalidOperationException: Steamworks is
  not initialized.` No usable live session.
- Added a minimal fallback to `src/NarrowGaugeTestBridge.cs`: the bridge now
  enables if either `NARROWGAUGE_TEST_BRIDGE=1` is present or a sentinel file
  named `ng_test_bridge_enabled` exists next to the deployed
  `NarrowGaugeMod.dll`; the enable log reports which gate was used. This
  keeps the bridge inert unless explicitly enabled and gives us a file-gated
  option for the relaunch/env problem.
- Rebuilt/deployed the source change with the same `dotnet build` command.
  Result: 0 warnings, 0 errors.
- Tried normal `Railroader.exe` launch with both env var and the sentinel
  present. It loaded mods and logged
  `[FUSE.NarrowGauge] [TestBridge] NarrowGaugeTestBridge enabled via NARROWGAUGE_TEST_BRIDGE - watching 'C:\Steam\steamapps\common\Railroader\Mods\FUSE.NarrowGauge' for 'ng_goto_request.json'.`
  but that process exited with `Steamworks is not initialized`; no final live
  Railroader process remained.
- Tried `Start-Process 'steam://rungameid/1683150'` after confirming
  `appmanifest_1683150.acf` is Railroader. No `Railroader.exe` process or
  fresh heartbeat appeared.
- Tried `Start-Process C:\Steam\steam.exe -ArgumentList '-applaunch 1683150'`.
  No `Railroader.exe` process or fresh heartbeat appeared.
- Working method found: write a temporary
  `C:\Steam\steamapps\common\Railroader\steam_appid.txt` containing exactly
  `1683150`, remove the sentinel, set `NARROWGAUGE_TEST_BRIDGE=1`, and launch
  the final editor process directly with
  `Start-Process Railroader.exe -ArgumentList '/editor' -WorkingDirectory C:\Steam\steamapps\common\Railroader`.
  This produced a stable live process PID `41988`, command line
  `"C:\Steam\steamapps\common\Railroader\Railroader.exe" /editor`, fresh
  FUSE heartbeat, and `Player.log` line:
  `[FUSE.NarrowGauge] [TestBridge] NarrowGaugeTestBridge enabled via NARROWGAUGE_TEST_BRIDGE - watching 'C:\Steam\steamapps\common\Railroader\Mods\FUSE.NarrowGauge' for 'ng_goto_request.json'.`

Successful full loop in PID `41988`:

- FUSE heartbeat: `mapLoaded=false`, `canApply=true`, fresh heartbeat.
- Sent `loadSave` request for `2026-06-25`.
  Result file:
  `{"schema":1,"requestId":"codex-load2-20260706-095555","ok":true,"text":"Booting save '2026-06-25' from the main menu.","completedUtc":"2026-07-06T13:55:56.4402063Z"}`.
- Waited until heartbeat was fresh with `mapLoaded=true`, `canApply=true`;
  `Player.log` showed
  `[FUSE.NarrowGauge] Special-work analysis: objects=14, invalid=0, elapsedMs=34305.`
- Wrote `ng_goto_request.json` with content:
  `{"nodeId":"Nove"}`.
  The bridge deleted the request and wrote
  `ng_goto_result.json` within about 1.5 seconds:
  `{"ok": true, "message": "Jumped to 'Nove' at (1747.79, 589.26, 1369.73)."}`
- Sent FUSE `screenshot` request with `arg:"nove-goto"`.
  Result was `Ok=true`, artifact
  `C:/Users/roger/AppData/LocalLow/Giraffe Lab LLC/Railroader\FUSE-test-shots\nove-goto.png`,
  size `3237441` bytes. Opened it directly; it was a real Nove-area frame but
  the Unity Mod Manager window covered the switch geometry, so I did not use
  it for the visual verdict.
- Sent FUSE `umm close`; result `Ok=true`, text `UMM window closed.`
- Sent second FUSE `screenshot` request with `arg:"nove-goto-clear"`.
  Result was `Ok=true`, artifact
  `C:/Users/roger/AppData/LocalLow/Giraffe Lab LLC/Railroader\FUSE-test-shots\nove-goto-clear.png`,
  size `4749731` bytes. Opened it directly.
- Made crops for inspection:
  `nove-overall-switch-crop.png`, `nove-lower-stand-crop.png`,
  `nove-upper-points-crop.png` beside the screenshot in
  `C:\Users\roger\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE-test-shots`.

Visual finding from the clear screenshot:

- The screenshot is targeted at Nove: it shows the
  `fuse-ng:s:Nove:control` label in the lower switch area, with a green switch
  stand left of the rails. Nearby labels include `SCustom_epu2`,
  `SCustom_d84`, and `Stjh`; the visible point/blade geometry is farther up
  the special-work assembly from the lower green stand.
- I do not see the originally reported lower-stand symptom in this captured
  view. Specifically, the green lower switch stand is not hiding a blade
  behind it, and no blade appears to run from behind that stand toward the
  middle of the switch. The lower stand area mostly shows stock/straight rails
  passing to the right of the stand. The upper visible point/blade geometry
  appears in the railwork, not behind the lower stand.
- Caveat: this is an elevated automated-camera screenshot with labels and
  some tree/foliage occlusion, not a close-up blade-edge inspection. If the
  user still sees the issue manually, the next useful automation improvement
  is a closer camera pose or second bridge command.

Final cleanup:

- Sent final FUSE `umm close`; result `Ok=true`, text `UMM window closed.`
- Called `CloseMainWindow()` on PID `41988`; returned `true`.
- Confirmed `Get-Process Railroader` returned no processes.
- Restored deployed
  `C:\Steam\steamapps\common\Railroader\Mods\FUSE.TestBridge\Info.json` to
  `"Enabled": false` and read it back directly.
- Removed temporary
  `C:\Steam\steamapps\common\Railroader\steam_appid.txt`.
- Confirmed `ng_test_bridge_enabled`, `ng_goto_request.json`, and
  `ng_goto_result.json` did not exist in
  `C:\Steam\steamapps\common\Railroader\Mods\FUSE.NarrowGauge`.

Next: Claude should review the small sentinel fallback and the visual
finding. If agreed, use the proven loop for the remaining substantive
special-work investigation (`SCustom_ttpp`, double-frog mapping, and the
"too many rails"/mid-switch-transition reports). For repeatable automation,
use the temporary `steam_appid.txt` + direct `/editor` env-var launch, or
test/adopt the new `ng_test_bridge_enabled` sentinel file path.

### [Claude] 2026-07-06 10:20 - Drove the live-game pipeline myself after Codex hit its usage limit again

Handed Codex a follow-up turn (screenshot the three "double frog" nodes:
`NCustom_fl15`, `NCustom_ltci`, `NCustom_fc97`) but it hit its usage limit
mid-turn ("try again at 1:52 PM" - a shorter block than the earlier
multi-day one). Since the user authorized working autonomously, drove the
proven pipeline myself directly via Bash rather than waiting: toggled
`FUSE.TestBridge/Info.json` to `Enabled: true`, wrote the temporary
`steam_appid.txt` (`1683150`), launched `Railroader.exe /editor` directly
with `NARROWGAUGE_TEST_BRIDGE=1` set on that process, confirmed a connected
heartbeat, `loadSave`'d `2026-06-25`, confirmed `Special-work analysis:
objects=14, invalid=0` in `Player.log`.

Learned the request schema is camelCase by reading `BridgeIo.cs` directly
(`CamelCasePropertyNamesContractResolver`) rather than guessing. Hit one
real snag: sent `{"verb":"console","commandLine":"umm close"}` to close the
UMM window (mirroring what I assumed Codex's turn had done) and got back
"unknown console command 'umm'" - turned out `umm` is its own top-level
verb (`BridgeProtocol.TestVerbUmm`), not a console command string. Fixed to
`{"verb":"umm","arg":"close"}`, which worked.

Goto'd and screenshotted `NCustom_fl15`, `NCustom_ltci` (twice - once
immediately after the goto result, once after an explicit 6s wait, to test
a hypothesis), and `NCustom_fc97`. Found and confirmed a real methodology
gotcha: `CameraSelector.JumpToPoint` runs as an async Unity coroutine
(confirmed by reading `CameraSelector.cs` - it calls
`base.StartCoroutine(this._JumpToPoint(...))` and returns immediately), so
`NarrowGaugeTestBridge`'s result file can be written before the camera
actually finishes panning. Initially suspected this explained why
`fl15`/`ltci`'s screenshots looked near-identical despite very different
reported coordinates - but re-shooting `ltci` with a 6s settle delay
produced the *same* near-identical frame, so that wasn't the actual
explanation. The real explanation: `Nove`, `fl15`, `ltci`, and `fc97` are
all part of the same clustered DeHart yard installation (all four
screenshots show the same yellow station building and mostly the same
`SCustom_*` labels) - a wide elevated camera view from nearby positions in
a dense yard legitimately looks similar. `fc97`'s screenshot genuinely
differed (different angle, turntable visible), confirming the camera does
move correctly; the settle-delay lesson is still worth keeping for future
turns since it's a real race condition even if it wasn't the cause here.

Visual finding: `fc97`'s screenshot shows small white curved fragments near
the lower-middle parallel tracks that look disconnected from the main
rails - a plausible match for the reported defects, but the view is still
too wide/elevated to call this confirmed. None of the three screenshots
resolved the double-frog question either way - the camera framing isn't
tight enough to distinguish two close frog castings from one legitimate
compound assembly.

Cleaned up and verified directly (not assumed): `tasklist` shows no
`Railroader.exe` process; `FUSE.TestBridge/Info.json` read back as
`"Enabled": false`; `steam_appid.txt` removed; no leftover
`ng_goto_request.json`/`ng_goto_result.json`/`ng_test_bridge_enabled` in the
deployed `FUSE.NarrowGauge` folder. Also manually removed several
`test_result_claude*.json` files left in the `FUSE.TestBridge` folder - the
bridge's `cleanup` verb only removes test *saves*, not stray request/result
files, so anyone driving this pipeline directly needs to clean those up by
hand rather than relying on `cleanup`.

Documented the camera-framing limitation and the settle-delay lesson in
`STATUS.md`. Did not modify `NarrowGaugeTestBridge.cs` this turn (e.g. to
add a built-in settle delay or tighter default framing) - flagged as a
worthwhile future improvement rather than doing it reactively mid-investigation.

Next: whoever's turn (Codex blocked until 1:52 PM) - either tighten the
camera framing to make the double-frog/blade-orientation questions
answerable from a screenshot, or make progress on `SCustom_ttpp`'s
cut-source ambiguity first since that doesn't need a live session at all.
