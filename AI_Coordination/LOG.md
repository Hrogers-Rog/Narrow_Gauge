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

### [Claude] 2026-07-06 10:40 - Resolved SCustom_ttpp's cut-source ambiguity

Picked up the recommendation from my own previous entry (make progress on
the cut-source ambiguity, since it doesn't need camera work). Added
source-tagged logging to `CreateRailMeshesWithFrogCuts`
(`src/NarrowGaugeTrackBuilder.cs`): computed `ownershipCuts`,
`gaugeSeparationCuts`, and `sharedRailFlipCuts` as separate arrays before
merging (previously they were concatenated directly into `MergeCutIntervals`
with no way to tell them apart afterward), and logged each non-empty
source under a new `[SpecialWorkSegmentClipSource]` tag alongside the
existing merged `[SpecialWorkSegmentClip]` summary. Verified
`MergeCutIntervals` genuinely unions overlapping intervals (confirmed by
reading it, not assumed), so this couldn't be done by tagging the merged
output after the fact - had to capture each source before the merge.

Built (0 warnings/0 errors), deployed, and ran a full live session myself
(same recipe as the previous turn: toggle `FUSE.TestBridge/Info.json`,
temporary `steam_appid.txt`, direct `/editor` launch with
`NARROWGAUGE_TEST_BRIDGE=1`, `loadSave`) specifically to capture this new
diagnostic - a validation-level check like this doesn't need the camera
bridge at all, just the game loading and rebuilding its graph once.

Result, read directly from the fresh `Player.log`: `SCustom_ttpp`'s cuts on
all three rails (`DualL`/`DualM`/`DualR`) are **100% `source=Ownership`** -
zero `GaugeSeparation`, zero `SharedRailFlip`. This resolves Codex's
investigation-flagged ambiguity cleanly: the fragment isn't from
gauge-separation frog synthesis or the already-dead shared-rail-flip path,
it's a measured special-work node's `WorkInterval` (per
`SpecialWorkHardwareRenderer.OwnershipCuts`) claiming the first ~2m of this
plain segment as its own render territory - confirmed this only fires when
`analysis.MeshPlan.IsValid == true` by reading the function directly.

Did not chase the remaining sub-question this turn (which specific
neighboring node - `fl15` or `ltci` - claims this interval, and whether its
own rendered pieces actually cover the cut) - that requires translating
between `SCustom_ttpp`'s segment-relative cut distances and the claiming
node's route-relative `WorkInterval` distances, a real coordinate
translation rather than a quick log read, and it was a reasonable stopping
point after a clean result rather than rushing further.

Cleaned up and verified the second live session the same way as the first:
`tasklist` shows no `Railroader.exe` process, `Info.json` restored to
`Enabled: false`, `steam_appid.txt` removed, no leftover bridge/goto files.

Next: whoever's turn - trace which measured node claims `SCustom_ttpp`'s
ownership interval and whether its render covers the cut (answers whether
this is a real bug or a correct handoff). Double-frog and blade-orientation
questions still need better camera framing or the user's own screenshots.

### [Claude] 2026-07-06 10:50 - Found the double-claim on SCustom_ttpp

Followed my own previous entry's recommendation directly: added one more
log line to `SpecialWorkHardwareRenderer.OwnershipCuts`
(`src/SpecialWorkHardwareRenderer.cs:250-266`), right where
`analysis.Definition.Id` was already in scope - no restructuring needed,
just a `Main.Log` call at the existing cut-creation point, tagged
`[SpecialWorkOwnershipCutClaim]` with the claiming node, rail, and interval.

Built (0 warnings/0 errors), deployed, and ran a third full live session
myself (same recipe: toggle `Info.json`, temporary `steam_appid.txt`,
direct `/editor` launch with the env var, `loadSave 2026-06-25`) - each
session this turn only took the specific new diagnostic I'd just added, so
a fresh launch was needed each time rather than reusing a running one
(consistent with the earlier finding that a running process never picks up
a new deploy).

Result, read directly from `Player.log`: **`NCustom_fl15` and
`NCustom_ltci` both independently claim ownership of the same physical
rails on `SCustom_ttpp`**, starting at the same distance (0.120) but with
different extents - `fl15` claims roughly 1.45-1.47m, `ltci` claims roughly
2.02-2.03m, across matching rail pairs (narrow-through/standard-reversed
for `fl15`; narrow-normal/narrow-reversed/standard-normal/standard-reversed
for `ltci`). Since `MergeCutIntervals` unions overlapping intervals
(re-confirmed by reading it, not assumed), the actual rendered cut takes
the larger extent - matching exactly the `ltci`-sized numbers
(`0.120-2.028` etc.) seen in the earlier `[SpecialWorkSegmentClipSource]`
result.

This is now a concrete, testable hypothesis rather than an open ambiguity:
if `fl15`'s own rendered replacement geometry only reaches its own smaller
claimed interval (~1.45m) while the rail is cut all the way to `ltci`'s
larger claim (~2.03m), there's a ~0.5-0.6m gap between where `fl15`'s
render stops and where the cut rail actually ends - matching the size and
shape of the floating-fragment symptom reported since the very start of
this session. Documented as the specific next step (verify which node's
render actually covers how much of the cut) rather than assumed-confirmed
or guessed-fixed.

Cleaned up and verified the third live session identically to the first
two: `tasklist` shows no process, `Info.json` restored, `steam_appid.txt`
removed, no leftover bridge files.

Next: whoever's turn - verify whether `fl15`'s or `ltci`'s rendered pieces
actually cover the full cut interval on `SCustom_ttpp`, which will confirm
or refute this as the real bug behind that segment's reported fragment.

### [Claude] 2026-07-06 12:10 - User pushed back correctly; found and fixed a real orientation-dependent bug

User said directly: "doesn't seem like we've fixed anything." Correct -
everything landed so far passed log validation but nothing had been
visually confirmed. They then gave extremely specific close-up evidence:
`S4u5` has blades on the wrong rails (left-through/right-diverge instead of
left-diverge/right-through), and a widespread pattern across `e6i0`,
`tliv`, `s3y7`, `ttpp`, `snvo`, `6wx3` where "the frog or guard is shifted
about the width of a rail head to the left or right... there all slightly
different." Confirmed `Nove`'s blade is still broken. One screenshot
(`snvo`, extreme top-down close-up) unambiguously shows a short rail piece
laterally offset and disconnected from the continuous rail beside it -
real, visually confirmed, not a log artifact.

Read "shifted sideways, inconsistently left or right across different
switches" as the specific signature of code assuming a fixed physical role
for a fixed `Left`/`Right` label when that mapping actually varies per
switch. Went looking for exactly that pattern rather than continuing to
theorize from logs. Found it: `SuppressDualBothDivergeFrogDuplicate`
(`src/SectionedSpecialWorkBuilder.cs` ~1947) always looked up
`"narrow-normal:left"` as the one duplicate rail to suppress for
`dual.both-diverge` presets. Checked `BuildNarrowRailsFromStandardCenterline`
(~377) to confirm this is unsafe: whether `narrow-normal:left` or
`narrow-normal:right` carries the curve *shared* with standard gauge (the
actual duplicate) depends on that switch's own `sharedSide` orientation
(`DualGauge_L` vs `_R`) - the hardcode was only correct for one orientation,
leaving the true duplicate unsuppressed (and sometimes cutting the wrong,
already-distinct rail instead) on the other. This explains the
inconsistent left/right pattern directly: different switches on the map
have different orientations.

Fixed by calling the existing `DetectSharedSide(definition)` helper
(already used elsewhere for the same purpose) and picking the correct rail
id based on it, instead of a hardcoded string. Grepped for other hardcoded
`"narrow-*:left/right"` literals in the file - found none besides this one.
Reviewed `ChooseSharedOwner` (the general shared-rail tie-break function)
too - it's orientation-agnostic (family/stock/movable/diverging-side/
alphabetical priority chain, no hardcoded side), so this isn't a second
instance of the same bug class.

Built (0 warnings/0 errors) and deployed. **Not yet live-verified** - this
fix targets one specific function (`dual.both-diverge`'s vee-frog
shared-duplicate suppression), so it likely explains part but not all of
what the user reported. `S4u5`'s wrong-side blade pairing and `e6i0`'s
"inside out" frog rendering are different symptoms (blade-rail pairing vs.
frog orientation, possibly different presets) not yet investigated -
explicitly did not claim this fix resolves those too.

Reinforced the standing rule the hard way: log validation and an
inconclusive wide screenshot are not the same as a confirmed fix. Only a
close-up that specifically shows previously-broken geometry now looking
right, or the user's own confirmation, counts.

Next: live-verify this fix with a close screenshot of an affected
both-diverge switch's vee frog area (may need tighter camera framing than
used so far), then investigate `S4u5` and `e6i0` as separate symptoms.

### [Claude] 2026-07-06 15:15 - Two more leads found, handed to Codex (unblocked)

Ran a live verification session for the both-diverge duplicate-rail fix:
`objects=14, invalid=0` held, and a medium-distance screenshot of
`NCustom_u6n0` didn't show obvious fragments - recorded as inconclusive
(not close-up), not proven, per the standing rule.

User then confirmed no photos had come through on a prior message, resent
one: an extreme close-up of `dkzn` showing multiple distinct
disconnected/kinked rail fragments near a crossing - messier than a single
clean gap. Traced ownership via the `SpecialWorkOwnershipCutClaim`
diagnostic (already in `Player.log` from the prior session, no new launch
needed): `dkzn` is claimed solely by `NCustom_p997` (`dual.both-diverge`),
no double-claim conflict this time. Read
`CreateCompoundVeeFrogAssembly`/`CreateVeeFrogAssembly`/`CreateVeeWingRail`/
`TryResolveCompoundVeeRails` (`src/SpecialWorkHardwareRenderer.cs`
~1380-1700) looking for the same hardcoded-orientation bug class as last
turn's fix - found none; `TryResolveCompoundVeeRails` matches the shared
rail by actual object identity, not a hardcoded Left/Right string, so it's
structurally different from the bug already fixed. Grepped the whole file
for other hardcoded `"narrow-*"`/`"standard-*"` rail-id literals - found
exactly one, and it's a debug-log filter condition
(`LogDualBothDivergeNarrowClosureFrame`), not something affecting rendered
geometry. Concluded `dkzn`'s defect is a different, deeper bug - likely in
the actual heel-point/wing-rail geometry math, not a quick grep-and-fix.

Also traced `S4u5` (user: blades on the wrong rails, should be
left-diverge/right-through, are left-through/right-diverge) - claimed by
`N178`, same `dual.narrow-branch-joins-main` preset as `Nove` (which the
user has now twice confirmed still shows a backwards blade). Compared their
`[Blades]` plan data: the stock/movable pairing is a mirror image between
the two switches. Flagged as ambiguous rather than guessed at: this could
be a legitimate opposite-hand pairing (the log shows each switch selecting
a different truth table, `DualGauge_NarrowBranch_Left` vs `_Right`), or the
truth-table *selection* itself could be picking the wrong hand for one of
them based on actual measured geometry - didn't have time to determine
which, and didn't want to guess at a fix without knowing.

Confirmed Codex's usage-limit block cleared (no-op check: "I'm available").
Since both remaining leads need careful, uninterrupted geometric tracing
(not another quick grep), handed both to Codex as a real investigation with
full context in `STATUS.md` rather than continuing to grind solo.

Next: Codex - investigate the `dkzn`/`p997` compound-vee-frog geometry and
the `S4u5`/`N178`/`Nove` truth-table hand-selection question. Verify any
fix with a close-up screenshot via the proven live-game pipeline before
claiming either is resolved.

### [Codex] 2026-07-06 15:45 - Investigated p997/dkzn and S4u5/Nove; found concrete leads but made no code fix

Read `STATUS.md` in full and the recent `LOG.md` tail before starting. Also
read the relevant code in `SpecialWorkHardwareRenderer.cs`,
`SectionedSpecialWorkBuilder.cs`, and `SpecialWorkTruthTableValidator.cs`.

Live pipeline used:

- Ran deploy build:
  `dotnet build .\NarrowGaugeMod.csproj -p:RailroaderDir="C:\Steam\steamapps\common\Railroader" -p:EnableModDeploy=true`
  - succeeded with 0 warnings / 0 errors and deployed the mod DLL.
- Closed an existing `Railroader.exe` PID 42908 cleanly with
  `CloseMainWindow`.
- Set `C:\Steam\steamapps\common\Railroader\Mods\FUSE.TestBridge\Info.json`
  to `"Enabled": true`.
- Tried to create temporary
  `C:\Steam\steamapps\common\Railroader\steam_appid.txt` containing
  `1683150`, but PowerShell returned `Access denied`. Because approval is
  unavailable, launched without that file but with
  `NARROWGAUGE_TEST_BRIDGE=1`, `SteamAppId=1683150`, and
  `SteamGameId=1683150` in the direct process environment.
- Launched `Railroader.exe /editor` directly; process PID 19912.
- Waited for `FUSE.TestBridge` heartbeat from PID 19912.
- Sent `loadSave` for `2026-06-25`; result:
  `Booting save '2026-06-25' from the main menu.`
- Waited for `mapLoaded=true` and a fresh `Player.log` line:
  `[FUSE.NarrowGauge] Special-work analysis: objects=14, invalid=0, elapsedMs=34149.`
- Sent `umm` / `close`; result `UMM window closed.`
- Used `ng_goto_request.json` for `NCustom_p997`; result:
  `Jumped to 'NCustom_p997' at (302.72, 588.45, 292.87).`
- Waited 6 seconds and captured screenshot with FUSE `screenshot` arg
  `codex_p997`; result artifact:
  `C:\Users\roger\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE-test-shots\codex_p997.png`.
  Viewed it directly. It is a real p997-area screenshot, but still
  medium/wide rather than a close-up capable of proving any visual fix.

One methodology caveat: the plan export files did not refresh in this live
run. `special-work_NCustom_p997.txt`, `special-work_N178.txt`, and
`special-work_Nove.txt` still had LastWriteTime `2026-07-06 12:08:40`. I
used those exports only where the fresh `Player.log` matched the same
frog/blade facts.

Findings for `dkzn` / `NCustom_p997`:

- Important correction: p997 is `dual.both-diverge`, so it does **not** call
  `CreateCompoundVeeFrogAssembly`. In `AddAdditionalHardware`, compound vee
  assemblies are only created for `IsDualStandardBranch(analysis)`.
- Fresh log confirmed p997's relevant frogs:
  - `v2-frog:0` `standard-normal:left` / `standard-reversed:right`, vee,
    angle `11.12`, `cutHalf=0.819`, near `(1805.83,1305.94)`.
  - `v2-frog:1` `standard-normal:left` / `narrow-reversed:left`, crossing,
    angle `7.16`, `cutHalf=1.443`, near `(1804.08,1300.56)`.
  - `v2-frog:2` `standard-reversed:right` / `narrow-normal:left`, vee,
    angle `9.25`, `cutHalf=0.978`, near `(1805.41,1303.03)`.
- The likely reason the user's close-up shows multiple distinct
  disconnected/kinked fragments is overlapping generated hardware around
  the crossing, not one clean gap:
  - `CreateCrossingFrogAssembly` routes standard+narrow crossing frogs into
    `BuildNarrowBranchStockHandoff`, producing a kinked
    `ContinuousStockHandoff` even for this `dual.both-diverge` crossing.
  - `BuildGuardRails` also calls `TryBuildLocalCrossingGuard` for crossing
    frogs. The stale-but-matching p997 export shows this as `v2-guard:4`, a
    2.888 m kinked guard on `narrow-reversed:left`, with endpoints
    approximately `(1804.602,1301.908)` to `(1803.925,1299.120)`.
  - `AddDualBothDivergeSupplementalGuards` adds `v2-guard:8`; in the export
    it has exactly the same endpoints as `v2-guard:0`
    `(1806.741,1304.676)` to `(1807.322,1306.381)`, so at least one guard is
    duplicate geometry.
- No p997 code fix made. The next useful patch target is the crossing
  handoff/local crossing guard/supplemental guard interaction, not
  `CreateCompoundVeeFrogAssembly`.

Findings for `S4u5` / `N178` vs `Nove`:

- Fresh `Player.log`: N178 selects `DualGauge_NarrowBranch_Left`.
  Its stale export blade pairing is
  `NarrowPointBlade stock=narrow-normal:right movable=narrow-reversed:right`
  and
  `NarrowStraightPointBlade stock=narrow-reversed:left movable=narrow-normal:left`,
  which matches the user's "left-through/right-diverge" complaint.
- Fresh `Player.log`: Nove selects `DualGauge_NarrowBranch_Right`.
  Its stale export blade pairing is the mirror:
  `NarrowPointBlade stock=narrow-normal:left movable=narrow-reversed:left`
  and
  `NarrowStraightPointBlade stock=narrow-reversed:right movable=narrow-normal:right`.
- Found a real truth-table selector bug:
  `SpecialWorkTruthTableCatalog.TryGet(..., intersections, ...)` matches a
  selector against any `RailIntersection`, including zero-angle
  `SharedOverlap`. `BuildBladeSpecs` uses this early intersection-based path
  before frog acceptance/collapse. In N178, the
  `DualGauge_NarrowBranch_Left` selector pair
  `standard-through:left x narrow-reversed:right` exists only as a
  `SharedOverlap` (`angle=0.000`), not as the accepted frog geometry.
  That explains how S4u5 can pick a mirror blade table.
- I did **not** patch this by simply filtering out `SharedOverlap`, because
  for N178 that likely falls through rather than positively measuring the
  correct hand, and because Nove already selects the table that matches the
  user's expected S4u5 hand yet remains visually broken.
- Found a separate Nove blade-geometry lead:
  `NarrowPointBlade:closure` in the stale-but-matching Nove export is only
  about 0.386 m long. `TryFindBladeDistances` starts from the switch point
  as the blade tip, but when the blade extends toward lower curve distance
  it returns `tip=endpoint` and `root=switchDistance` to preserve a sorted
  interval. The renderer treats `BladeCurve.Head` as the point tip and
  `Tail` as the pivot/root, so negative-direction blades can have semantic
  tip/root swapped. The older `SpecialWorkGeometryBuilder` has an analogous
  reverse-curve branch; the sectioned narrow-branch builder does not.
- No S4u5/Nove code fix made. The next patch should separate truth-table
  hand selection from negative-direction blade tip/root/closure semantics.

After investigation I ran a plain build for sanity:
`dotnet build .\NarrowGaugeMod.csproj` - succeeded with 0 warnings / 0
errors.

Cleanup performed and verified directly:

- Sent one final FUSE `umm` / `close` request; result `UMM window closed.`
- Closed `Railroader.exe` PID 19912 with `CloseMainWindow`; subsequent
  `Get-Process Railroader` returned no process. An explicit `tasklist /FI
  "IMAGENAME eq Railroader.exe"` check was attempted per procedure, but this
  shell returned `ERROR: Access denied`; the PowerShell process check was the
  direct verifier available in this environment.
- Restored `FUSE.TestBridge/Info.json` and read it back showing
  `"Enabled": false`.
- Checked `C:\Steam\steamapps\common\Railroader\steam_appid.txt`:
  `Test-Path` returned `False`.
- Removed `test_request_*.json` / `test_result_*.json` from
  `FUSE.TestBridge`; verified no matching files remain.
- Removed `ng_goto_request.json`, `ng_goto_result.json`, and
  `ng_test_bridge_enabled` from `FUSE.NarrowGauge`; verified no matching
  files remain.

Wrote detailed handoff notes to
`AI_Coordination/reviews/p997-and-narrow-branch-hand-investigation-2026-07-06.md`
and updated `STATUS.md`. No code files were changed and no visual fix is
claimed.

### [Claude] 2026-07-06 15:45 - Fixed the negative-direction blade tip/root swap

Reviewed Codex's investigation turn - good discipline, found real specific
bugs and correctly declined to claim a fix without proof. Verified its most
concrete finding by reading the code myself rather than trusting the
write-up: `TryFindBladeDistances` (`src/SectionedSpecialWorkBuilder.cs`
~3507) always returns `(tip, root)` sorted numerically ascending (needed
for interval bookkeeping), but the *physical* blade tip is always at the
switch throat regardless of which direction the blade extends. For
backward-extending blades (confirmed this is `Nove`'s case, per Codex's
finding of an oddly-short `NarrowPointBlade:closure` in its stale export),
the smaller sorted value is actually the physical root/heel and the larger
one is the physical tip - opposite of the numeric labels.

Both call sites that build `BladeCurve` from these values
(`BuildDualNarrowBranchBlades` and `TryBuildMeasuredDualSplitBlade`) sliced
straight from `tip` to `root` ascending, which for backward-extending
blades puts the physical root at `BladeCurve.Head` and the physical tip at
`BladeCurve.Tail` - exactly backward from what
`CalculateBladeOpenRotation`/`CreatePointBlade` assume. This is a
confident, well-reasoned match for "blade running into the switch instead
of away from it."

Fixed both call sites: reverse the sliced curve for backward-extending
blades (using the already-computed `bladeExtendsForward` flag at each site)
so `Head`/`Tail` land on the correct physical ends regardless of numeric
sort direction. Confirmed `LineCurve.Reverse()` is a real method
(`Decompiled dlls base game/Core/LineCurve.cs`) before using it, not
assumed. Left the numeric `TipDistance`/`RootDistance` fields alone - other
consumers (cut/suppression intervals) only care about extent, not
direction, so touching those would have been unnecessary scope.

Built (0 warnings/0 errors), deployed. Live verification in progress - not
yet claiming this is fixed until a close-up screenshot of Nove's blade
specifically confirms it, per the standing rule reinforced twice already
this session.

Did not touch `dkzn`/`p997`'s overlapping-hardware issue or the
truth-table `SharedOverlap` selector bug this turn - both still open per
Codex's findings, and are separate from the bug just fixed (even if a
switch selects a technically-valid-but-wrong-for-its-geometry truth table,
the blade curve direction bug would still exist independently).

Next: verify live with a close-up of Nove. If confirmed, check S4u5/N178
too (same bug class). If not, the symptom may need the truth-table selector
issue addressed as well/instead.

---

## [Claude] 2026-07-06 - blade tip/root fix does not touch Nove; scope widened to all 14 measured switches

Live-verified the previous turn's blade tip/root-swap fix against `Nove`
using the `NarrowGaugeTestBridge` pipeline (extended it this turn with a
`closeUp`/first-person jump mode, an `action:exportPlans` bridge action to
force a fresh plan export, and an `action:throwSwitch` bridge action via
`TrainController.Shared.TrySetSwitch` - all in `src/NarrowGaugeTestBridge.cs`).
Multiple automated screenshot attempts had camera-positioning bugs (an
absolute eye/lookAt jump computed from the plan export's `pos=` coordinates
landed in unrelated woods - the export coordinates and the live
WorldTransformer/CameraSelector coordinate space are not directly
interchangeable the way I assumed; node-relative jumps worked fine). The
user ultimately captured their own close-up screenshots (including an
extreme rail-level close-up) and confirmed the blade is still backwards.

Re-derived `bladeExtendsForward` for Nove's actual data by hand: for
`NarrowPointBlade`, `tip=29.856 root=34.457`, and `switchDist` (computed
identically inside `TryFindBladeDistances` as
`movableCurve.DistanceTo(switchPoint)`) is very close to `tip`'s value
(confirmed via the exported `PieceEndpoints`, where `BladeCurve.Head` sits
almost exactly at Nove's switch node world position). This means
`bladeExtendsForward = root(34.457) > switchDist(~29.86) = true` for both
of Nove's blades - so the `.Reverse()` branch added last turn, which only
fires when `bladeExtendsForward == false`, never executes for Nove. The fix
is real for whichever switch actually has a backward-extending blade, but
it was never Nove's bug. Correcting the previous turn's claim.

Re-examined the truth-table selector theory (previously flagged by Codex as
"maybe wrong, unsafe to patch"): read `SpecialWorkTruthTableCatalog.TryGet`
and `MatchesSelector` in full (`src/SpecialWorkTruthTableValidator.cs`
~771-851). The selector is NOT an arbitrary/first-match heuristic - it
checks whether a specific `(routeId, side)` rail pair actually has a
matching frog/intersection in this switch's live geometry
(`selectorFrogPair.railA`/`railB`). Read both
`DualGauge_NarrowBranch_Left`/`_Right`'s `selectorFrogPair` and `blades`
sections in `truth/SpecialWorkTruthTables.json` (lines ~1-330): they check
genuinely different rail/side pairs, and their `blades` sections assign
mirror-image `movableSide`/`stockSide`. So `Nove` and `N178` selecting
different tables is not automatically a bug - could be correct if they are
genuinely mirrored physical switches. This needs live per-switch frog data
to settle, which is now handed to a subagent (see below) rather than more
static reading from me, since scope has widened past what I can single-
thread through carefully.

## Scope widened per user request

User: "there are issues with every turnout but 936m and that's because it
doesn't have a double frog" - confirms 936m is a plain non-measured switch,
and wants all 14 measured special-work switches audited, with Codex and a
parallel Claude subagent both working simultaneously ("deploy codex and
multiple claude agents").

Mapped every previously-reported broken segment to its owning measured
switch via `grep '[SpecialWorkOwnershipCutClaim]'` in `Player.log` (segment
ids are track-segment labels, not switch/node ids - this mapping wasn't
done explicitly before). Full breakdown by preset now in STATUS.md. Two
new findings from this mapping alone, before any deeper investigation:
- `NCustom_ltci` (both-diverge) double-claims BOTH `SCustom_ttpp` (which
  otherwise belongs to `NCustom_fl15`) and `SCustom_snvo` (which otherwise
  belongs to `NCustom_g832`) - i.e. it's claiming territory past its own
  switch's boundary into two different neighboring switches. This is a
  distinct, concrete lead, not previously identified this precisely.
- All 5 `dual.narrow-branch-joins-main` switches (`N178`, `NCustom_7n90`,
  `NCustom_g832`, `NCustom_vdlt`, `Nove`) have a reported symptom - none of
  the 5 in this preset group are confirmed working.

## This turn's plan

Given concurrent edits to the same shared files
(`SectionedSpecialWorkBuilder.cs`, `SpecialWorkHardwareRenderer.cs`) by
multiple actors risks real conflicts, both parallel threads this round are
investigation-only (read + write findings to a new `reviews/*.md` file, no
source edits, no commits). Claude will apply fixes sequentially once both
threads report back to avoid stepping on either.

- Codex: both-diverge group (`p997`/`dkzn` overlapping hardware - continuing
  its own prior thread -, `ltci`'s `ttpp`/`snvo` over-claim, `u6n0`/`s3y7`,
  `wqbb`/`tliv`, plus a sanity pass on `fc97`/`l4a4`/`Npv2`/`fl15`/`33d6`
  which have no reported symptom yet).
- Claude subagent: narrow-branch-joins-main group (all 5 switches) -
  whether the truth-table selection is actually correct per-switch given
  live frog data, and whether the truth table JSON's blade
  movable/stock/side assignment matches each switch's actual measured hand.

Full per-switch symptom/preset breakdown is in STATUS.md - not duplicating
it here to keep this entry shorter.

---

## [Codex] 2026-07-06 - both-diverge group investigation: duplicate guards and ltci ownership overclaim

Completed the investigation-only pass for the seven
`dual.both-diverge` measured switches. Wrote the full findings to
`AI_Coordination/reviews/both-diverge-group-investigation-2026-07-06.md`.
No source files or truth tables were edited.

Live pipeline used this turn: built/deployed successfully with 0 warnings
and 0 errors, enabled the FUSE TestBridge metadata only for the run, used
temporary `steam_appid.txt=1683150`, launched `Railroader.exe /editor`
directly with `NARROWGAUGE_TEST_BRIDGE=1`, loaded save `2026-06-25`, and
forced a fresh `exportPlans` through `NarrowGaugeTestBridge`. Fresh plans
were written at `2026-07-06 16:30:58` local time under
`C:\Users\roger\AppData\LocalLow\Giraffe Lab LLC\Railroader\NarrowGauge\SpecialWorkPlans`.
Captured close-up screenshots for `NCustom_p997`, `NCustom_ltci`,
`NCustom_u6n0`, and `NDeHartPassing_wqbb` under
`C:\Users\roger\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE-test-shots`.

Confirmed duplicate guard endpoint groups in the fresh plan exports:

- `NCustom_p997`: `v2-guard:0 == v2-guard:8`.
- `NCustom_ltci`: `v2-guard:0 == v2-guard:8`.
- `NDeHartPassing_wqbb`: `v2-guard:0 == v2-guard:8` and
  `v2-guard:3 == v2-guard:7`.

The traced code path is ordinary guard generation plus the unconditional
both-diverge supplemental guard pass:
`BuildGuardRails` calls `AddDualBothDivergeSupplementalGuards`, and
`AddSupplementalGuardPair` appends guard curves without checking whether an
ordinary guard already occupies the same endpoints. This is a confirmed
overlapping-hardware cause for p997/ltci/wqbb. `CreateCompoundVeeFrogAssembly`
is not involved for p997 or other both-diverge switches; it is gated to
dual.standard-branch cases.

Also confirmed a separate ownership-cut bug at `NCustom_ltci`. Fresh
`Player.log` shows `NCustom_ltci` double-claiming both `SCustom_ttpp`
with `NCustom_fl15` and `SCustom_snvo` with `NCustom_g832`. The traced
mechanism is `OwnershipCuts` in `src/SpecialWorkHardwareRenderer.cs`: for
non-`DualSplit` presets it admits an analysis by source segment, then
scans all `MeshPlan.WorkIntervals`; the `sourceRouteIds` interval filter
currently only runs for `DualSplit`. Because `CreateRailMeshesWithFrogCuts`
merges ownership cuts into stock rail mesh clipping, these overclaims can
remove neighboring segment rail.

Investigated but did not confirm a root cause for `NCustom_u6n0`,
`NCustom_fc97`, `NCustom_l4a4`, or `Npv2`. The fresh plan scan found no
exact duplicate guard endpoints and no `GeometryContinuity` issues in
those four. `u6n0`, `l4a4`, and `Npv2` use measured-geometry fallback;
`u6n0` also has a synthesized frog. Those may still relate to the user's
general shifted-frog complaint, but this turn did not prove that.

Cleaned up the live environment after capture: closed UMM through the
bridge, closed the Railroader window without force-kill, verified no
`Railroader` process remained, restored TestBridge `Info.json` to
`"Enabled": false`, removed `steam_appid.txt`, and removed/verified absence
of leftover `test_request_*`, `test_result_*`, `ng_goto_request.json`,
`ng_goto_result.json`, and `ng_test_bridge_enabled` files.

Next: apply fixes sequentially after reading this review and the parallel
narrow-branch findings. Start with supplemental guard de-duplication, then
tighten ownership interval scoping and live-test `ttpp`/`snvo`/`6wx3`.

---

## [Claude] 2026-07-07 - found real root causes for Nove's blade orientation via live diagnostic logging (not static reading); both-diverge fixes applied

Session continuity note: this conversation was interrupted (Claude Code
process exited) partway through spawning a Claude subagent for the
narrow-branch-joins-main investigation; that subagent's work was lost.
Recovered by committing Codex's completed both-diverge investigation
(it had finished and written its review file but not committed before the
interruption), then proceeding directly rather than re-spawning a lost
subagent, since the user was available to test live.

Applied Codex's two confirmed both-diverge fixes directly (both read and
verified against the actual code before applying, not just trusted from
the review write-up):

1. `OwnershipCuts` (`src/SpecialWorkHardwareRenderer.cs`): removed the
   `isGaugeSeparation`-only gate on the `sourceRouteIds` filter - now all
   measured presets filter work intervals to routes actually touching the
   source segment, not just `DualSplit`.
2. `AddSupplementalGuardPair` (`src/SectionedSpecialWorkBuilder.cs`): skips
   adding a guard if one already exists for the same
   `(FrogId, OppositeRunningRail)` pair, fixing the confirmed exact-duplicate
   guards at p997/ltci/wqbb.

Built, deployed. Not yet re-verified live this turn (moved on to Nove
investigation) - still needs a fresh screenshot check next turn.

## Nove blade orientation - the real story

User pushed back hard and correctly: after the previous turn's
"blade tip/root swap" fix (`c1b5873`) was deployed, close-up screenshots
still showed the blade backwards. My own prior-turn re-analysis (right
after context compaction) had concluded that fix "doesn't apply to Nove"
based on hand-worked distance math - **that conclusion was wrong**, proven
this turn by adding a live diagnostic log
(`[SwitchPointDiagnostic]` in `BuildDualNarrowBranchBlades`) and reading
the actual numbers: `bladeExtendsForward` really was false for Nove, the
reversal really was firing, and `BladeCurve.Head` really was landing near
`switchNode`'s position (only ~0.5 units off). The static hand-tracing was
self-consistent every time I redid it, and still wrong, because it rested
on an unverified assumption.

The actual chain of bugs, found by testing hypotheses against real logged
data rather than more static reasoning:

**Bug 1 - `switchNode` position is not the toe.** Every distance/direction
computation in `BuildDualNarrowBranchBlades`/`TryBuildMeasuredDualSplitBlade`
used `switchNode.transform.localPosition` (i.e. the `Nove:control` node) as
a stand-in for "the physical switch throat/toe." The user confirmed
directly in-game: `Nove:control` sits *past the end of the switch entirely*,
on the frog side. Added `[BladeVsFrogDiagnostic]` (logs blade Head/Tail vs.
nearest real frog position) and confirmed: `distHeadToFrog=20.569 <
distTailToFrog=25.166` - Head (suppposedly the "tip," meant to be far from
the frog) was actually *closer* to the frog than Tail. Backwards.

Fix: added `IsForwardTipFartherFromFrog` - walks `intersections` for the
nearest `VeeFrogCandidate`/`CrossingFrogCandidate`, and determines which of
the numeric tip/root distances is actually farther from it. Replaced the
`switchDist`/`bladeExtendsForward` computation in both call sites so
*both* `bladeCurve` and `closureCurve` are built from the same,
frog-grounded direction (a first attempt patched only `bladeCurve` after
the fact via a `CorrectBladeTipAwayFromFrog` post-hoc reversal - this left
`closureCurve` still using the old, wrong direction, which the user caught
immediately: "still using nove:control and shouldn't be." Removed that
patch and fixed the direction determination upstream instead, once, before
either curve is built).

**Bug 2 - `RemoveRailEndCap` ignores which end `BuildStockRailMesh`
actually put "first."** `BuildStockRailMesh` (decompiled base game,
`TrackMeshBuilder.cs`) reverses point order internally for `Hand.Left`
curves before extruding, with a `profileScale` remap that's correct
(verified by hand-tracing the remap indices twice). But `CreatePointBlade`
called `RemoveRailEndCap(mesh, points.Length, removeStartCap: true)`
unconditionally - for `Hand.Left` blades this removed the cap from the
full-width heel (wrong end) and left one on the knife-edge tip. Fixed:
`removeStartCap: pivotedCurve.hand != Hand.Left`.

**Bug 3 - `LineCurve.Reverse()` doesn't recompute per-point rotation.**
After fixing bug 1, the user reported a *new* symptom: "the blade is
rendering inside out now. like the rail mesh itself is inside out." Read
the decompiled `Core/LineCurve.cs`: `Reverse()` is
`new LineCurve(Points.Reverse(), hand)` - point order flips, but each
point's own `Rotation` (and derived `.direction`) is untouched, still
facing the *original* forward direction. For a curve that's reversed
exactly once (as my new frog-based direction fix now does for some blades
that weren't reversed before), the per-point direction ends up backwards
relative to the new traversal order, flipping the extruded mesh's
winding/normals. For `Hand.Left` curves this accidentally cancels out
against `BuildStockRailMesh`'s own internal Hand.Left reversal (reversed
twice nets back to consistent), which is why this only became visible now
that the frog-based fix changed which blades get reversed at all (some
`Hand.Right` blades that previously weren't reversed now are).

Fix: added `SectionedSpecialWorkBuilder.ReverseRailCurve` (internal,
reversed points + negated direction, kept consistent) and replaced the
single-reversal call sites: both blade-curve constructions, plus
`CreateVeeWingRail`'s wing slice and `SliceSignedSpan` in
`SpecialWorkHardwareRenderer.cs` (the user also reported wing rails
rendering inside-out - same root cause, same fix). Deliberately did **not**
touch `FlareGuardRailEnds`/`FlareGuardRailEndsAwayFrom` - those reverse
twice back-to-back in a way that cancels out safely and inserts new points
with freshly-computed rotations, so they're not affected by this bug. Did
not do an exhaustive `grep -rn '\.Reverse()'` sweep of the whole codebase -
this was a targeted fix for the reported symptoms. If a similar
"inside-out" symptom shows up on a switch not yet checked, grep for other
single-reversal call sites.

User confirmed after all three fixes: "much better." Not claiming Nove
fully fixed - see below.

## Two new issues surfaced at Nove, not yet investigated

1. User: "there should only be one blade on the right.. this is a narrow
   diverge only standard through its a only one blade" - i.e. Nove's real
   layout may only switch the narrow-gauge route, with standard-gauge
   running through fixed/unswitched, meaning the second blade
   (`NarrowStraightPointBlade`) that the current truth table produces
   shouldn't exist.
2. User screenshot: two rails (confirmed via `Player.log`
   `[SpecialWorkOwnershipCutClaim]` grep to be `special-work:Nove`'s own
   `narrow-normal`/`narrow-reversed` rails on segment `SCustom_epu2`) cross
   directly with no frog casting there at all.

These are likely the same root cause (wrong truth table / blade-spec
generation for Nove's actual physical layout) but not yet confirmed. Full
detail and next-turn plan in STATUS.md - not duplicating here.

Diagnostic logging (`[SwitchPointDiagnostic]`, `[BladeMeshDiagnostic]`,
`[BladeVsFrogDiagnostic]`) deliberately left in the code - it's what
actually cracked this after multiple rounds of static reasoning got it
wrong. Strip it once the two remaining issues are resolved.

### [Codex] 2026-07-07 01:09 - Patch 7n90/194b measured fallback to one narrow-branch blade
Picked up the user's `SCustom_194b` request while Claude continued on Nove.
Read the coordination files, current `Player.log`, and the exported
`special-work_NCustom_7n90.txt`. Confirmed `SCustom_194b` is owned by
`special-work:NCustom_7n90`, and the active plan is a
`dual.narrow-branch-joins-main` fallback case: no truth table matched, so
`BuildBladeSpecs`' generic measured fallback emitted two blades
(`v2-blade:narrow:Left` and `v2-blade:narrow:Right`).

Found a pre-existing uncommitted `SectionedSpecialWorkBuilder.cs` change that
already applied a one-blade shared-side filter to truth-table matched
narrow-branch nodes. Kept that change, cleaned up its comment, and extended
the same rule to the measured fallback path so fallback narrow-branch nodes
skip the non-shared-side blade candidate. This specifically targets
`NCustom_7n90`, whose plan was still using fallback and therefore was not
affected by the truth-table-only filter. Wrote the review handoff to
`AI_Coordination/reviews/ncustom-7n90-194b-investigation-2026-07-07.md`.

Built with `dotnet build .\NarrowGaugeMod.csproj` (0 warnings, 0 errors). No
fresh in-game reload/screenshot was performed this turn, so do not claim
`SCustom_194b` visually fixed yet. Next: Claude should deploy/reload and
verify fresh `Player.log`/plan data shows `NCustom_7n90` with `blades=1`,
then capture a close-up of `SCustom_194b`; continue Nove separately because
its missing-frog issue may remain.

---

## [Claude] 2026-07-07 - found systemic gauge-separation gap bug; Nove frog-collapse traced but unfixed; scope widening to multi-agent sweep

Continued live-testing Nove directly with the user after the previous
entry (Codex's 7n90 one-blade fix). User confirmed the blade-orientation
and extra-blade fixes are working well, then surfaced two more issues via
close-up screenshots.

## Fixed: 5-meter gap at gauge-separation control switches (systemic)

User: "there is large gaps between nove and nove:control" - confirmed via
follow-up question this is a real physical gap a train would hit, not a
label artifact.

Traced to `SpecialWorkTopologySynchronizer.EnsureRuntimeGaugeSeparationControls`:
creates a fake "control" node+segment purely so the base game's own switch
detection sees a valid 3-way junction at a ghost node where only the narrow
gauge actually diverges (a trick needed because the base game requires
exactly 3 connections to recognize a switch). `GhostControlLength = 5f`
meters, positioned in the same direction as the standard-gauge continuation
(`BuildGhostControlPose`).

`CreateGaugeSeparationControlShell` (`src/NarrowGaugeTrackBuilder.cs`) only
builds fallback rails for this stub when
`!SpecialWorkHardwareRenderer.HasValidPlan(node)`. For switches with a valid
measured plan (Nove: confirmed `planValid=True` in `Player.log`), it does
nothing, trusting "measured special-work owns all turnout rails" - but the
measured special-work system has no route concept for this fake segment at
all, so nothing was ever drawing it. Confirmed via existing log line:
`vanillaRailObjects=16` vs `specialWorkRailObjects=14` - special work is
short by exactly one rail pair, matching this stub.

First fix attempt: removed the `IsHiddenControlSegment` check from
`IsGeneratedGhostDescriptor`'s `SegmentDescriptor` case (which was
suppressing this segment's own rail descriptor from the base game's default
rendering). User: "didn't help." Investigated why: `NarrowGaugeManager
.IsGeneratedGhost(segment)` **independently** also matches this exact
segment, because both real ghost segments and this control stub share the
identical `"fuse-ng:s:"` id prefix (`GhostGraphSynchronizer
.GeneratedSegmentPrefix`) - `IsGeneratedGhost` alone was still suppressing
it via the OR condition I hadn't touched. Corrected fix: excluded
hidden-control segments from *both* checks
(`NarrowGaugeManager.IsGeneratedGhost(segment) &&
!SpecialWorkTopologySynchronizer.IsHiddenControlSegment(segment)`), so the
base game's own default rail rendering now draws this stub. Left
`SwitchDescriptor`/`BumperDescriptor` suppression untouched - those still
need special handling for switch/topology detection, only the rail
descriptor itself needed this change.

Built, deployed. **Not yet re-verified live** - user's message moved on to
requesting a broader multi-agent investigation before re-testing this
specific fix. This is a **systemic** fix (affects every switch using
`EnsureRuntimeGaugeSeparationControls`, i.e. any `dual.narrow-branch-joins-main`
or `dual.split-standard-narrow` switch with ghost-node ownership
separation), not Nove-specific - should be checked across all affected
switches once confirmed working at Nove.

## Traced but not fixed: Nove's frog position/shape

User confirmed (via AskUserQuestion) that after the earlier fixes, a frog
now renders where none did before, but its position/shape is still wrong.
Read `CollapseDuplicateFrogHardware`/`ResolveFrogHardwareRail`/
`SameFrogHardware` in `src/SectionedSpecialWorkBuilder.cs`: Nove's raw
intersection data has two frog candidates at nearly the same position - one
pairing `standard-through` with `narrow-normal`, one pairing
`standard-through` with `narrow-reversed`. `ResolveFrogHardwareRail`
redirects the `narrow-reversed` one to `narrow-normal` (because
`narrow-reversed`'s rail at that exact distance is flagged as a
`SharedDuplicate` cut of `narrow-normal` - i.e. not actually rendered
there), which then makes the two candidates look identical
(`SameFrogHardware`'s `PairKey` match), so
`CollapseDuplicateFrogHardware` merges them into one, keeping only the
`narrow-normal` pairing. This is coherent, deliberate-looking logic, but may
not correctly represent the true 3-way convergence geometry (standard,
narrow-normal, and narrow-reversed all meeting near the same point). Did
not attempt a fix this turn - needs a live diagnostic (log the two
candidates' exact pre-collapse geometry and compare against what the
in-game frog should look like) before touching this, per the standing rule
below.

## Standing rule, reinforced hard this session

Static tip/root/distance/suppression reasoning about this codebase has been
wrong on first (and sometimes second) attempt, repeatedly, across totally
different code paths (blade Head/Tail, end-cap removal, LineCurve.Reverse,
shared-side blade filtering, and now this descriptor-suppression bug where
the first fix attempt missed a second, independent suppression path). The
pattern that worked every single time: add a targeted diagnostic log,
rebuild/deploy, have the user reload live, then read the actual logged
numbers before proposing or committing to a fix. Do not skip the live-check
step to save a cycle.

## Scope widening

User: "We need to use multiple agents and codex and dig into this narrow
gauge stuff and figure out why we're having issues" - moving from
single-threaded Nove investigation to a parallel multi-agent sweep. See
STATUS.md for the current state each thread should pick up from.
