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
