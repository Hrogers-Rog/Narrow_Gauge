# NCustom_7n90 / SCustom_194b Investigation - 2026-07-07

## Scope

The user asked Codex to look at `SCustom_194b` while Claude continued on
`Nove`. The screenshots show the `NCustom_7n90`/`SCustom_194b` area with
overlapping point/rail geometry in the narrow-branch turnout.

## Evidence

Current `Player.log` and the current exported plan are still from the
pre-fix build. They show:

- `special-work:NCustom_7n90` is valid and renders as
  `dual.narrow-branch-joins-main`.
- It renders `fixed=9, frogs=1, wings=4, guards=3, blades=2`.
- `SCustom_194b` is owned/clipped by `special-work:NCustom_7n90`, so this is
  a measured special-work geometry issue, not an unrelated vanilla rail that
  failed to suppress.
- `special-work_NCustom_7n90.txt` says no truth table matched; measured
  fallback was used.
- That fallback produced two point blades:
  - `v2-blade:narrow:Left` stock=`narrow-normal:left`,
    movable=`narrow-reversed:left`
  - `v2-blade:narrow:Right` stock=`narrow-reversed:right`,
    movable=`narrow-normal:right`

The live log explains why `NCustom_7n90` misses the existing narrow-branch
truth-table entries: its accepted frog is a same-side crossing candidate,
`standard-through:right` x `narrow-reversed:right`, while the existing truth
tables select different side pairings. As a result, `BuildBladeSpecs` falls
through to the generic measured fallback.

## Root cause

`BuildBladeSpecs`' measured fallback emits one blade candidate per physical
side by pairing normal/reversed rails. That is too generic for
`dual.narrow-branch-joins-main`: standard gauge runs through, narrow gauge is
the only route that switches, and the real narrow point is the shared-side
point. The non-shared-side candidate is an overbuild for this preset and is
what gives `NCustom_7n90` two blades around `SCustom_194b`.

There was already an uncommitted `SectionedSpecialWorkBuilder.cs` change in
the worktree when Codex started this turn. It applied the same one-blade
shared-side rule to truth-table matched narrow-branch nodes. This turn kept
that work and extended the rule to the measured fallback path, which is the
path `NCustom_7n90` actually uses.

## Fix

In `src/SectionedSpecialWorkBuilder.cs`, `BuildBladeSpecs` now:

- applies the narrow-branch shared-side filter only when the preset is
  `dual.narrow-branch-joins-main`;
- keeps only truth-table blades whose movable side matches the detected
  shared side;
- when no truth table matches, keeps only measured fallback blade candidates
  on the detected shared side and logs the skipped non-shared-side fallback.

No truth JSON was changed, and no per-instance ids were added.

## Verification

Build command run successfully:

```powershell
dotnet build .\NarrowGaugeMod.csproj
```

Result: 0 warnings, 0 errors.

This is not live-visual verified yet. The current `Player.log` and exported
plan still show the old two-blade build because the game has not been
reloaded with this commit.

## Next checks

After the next build/deploy + game reload, verify:

- `Player.log` includes the new measured fallback skip log for
  `NCustom_7n90`.
- `special-work:NCustom_7n90` exports/renders `blades=1`, not `blades=2`.
- The `SCustom_194b` close-up no longer shows the extra overlapping point
  blade.
- Nove's truth-table path should also be checked because this turn kept the
  already-present truth-table one-blade filter, but Nove's missing-frog issue
  may still require separate work.

## Follow-up (Claude, 2026-07-07 evening) - static-only review of all 4 remaining narrow-branch nodes; confirmed regression on 2 of them

Assigned to live-verify fixes 1-5 (blade orientation, end-cap hand-awareness,
`LineCurve.Reverse` fix, one-blade shared-side filter, gauge-separation gap
fix) against `N178`, `NCustom_7n90`, `NCustom_g832`, `NCustom_vdlt`. Two
pipeline interruptions happened this turn:

1. A Railroader.exe collision with a concurrent Codex live-test run (the
   `FUSE.TestBridge` mod folder was also missing `FUSE.TestBridge.dll` at the
   start of this turn - copied it back in from
   `FUSE/FUSE.TestBridge/bin/Debug/net48/`). Closed the stray process, waited
   out a 5-minute cooldown the coordinator requested, rebuilt/redeployed
   against the latest commit (`2330890`, Codex's live-verified both-diverge
   guard-dedup fix), and re-launched.
2. Before a heartbeat/save-load completed, the user asked to stop the
   live-launch pipeline entirely ("causing repeated game restarts and
   conflicts"). Closed the just-launched `Railroader.exe` gracefully,
   confirmed exit via `tasklist`, restored `FUSE.TestBridge/Info.json` to
   `Enabled: false`, removed `steam_appid.txt`, and removed all leftover
   `test_request_*`/`test_result_*`/`test_state.json`/`ng_goto_*` files.
   Switched to static analysis only for the rest of this turn - no
   close-up screenshots or fresh in-game throw/close verification were
   performed. Everything below is read from source and from disk artifacts
   (some fresh from Codex's same-turn live run, some the 2026-07-06 stale
   baseline plan exports) - **not** independently live-confirmed by Claude
   this turn.

### Data used

The freshest available plan exports for these 4 nodes are Codex's second
export this session, on disk at
`%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\NarrowGauge\SpecialWorkPlans\special-work_<id>.txt`,
timestamped `2026-07-07 16:46:04`, built from commit `2330890` (current
HEAD at the time, includes both Codex's fallback one-blade fix `8802240` and
Claude's gap fix `f5ad56b`). Read directly (not from the Player.log
transcript) for authority:

- `N178`: `Plan valid: True`, `blades=1`, `frogs=1`, `wings=2`, `guards=2`.
- `NCustom_7n90`: `Plan valid: False`. First failure: `Fixed diverging
  narrow stock/running rail has no renderable role sections.` `blades=1`,
  `frogs=1`, `wings=4`, `guards=3`.
- `NCustom_g832`: `Plan valid: False`. Same first-failure text. `blades=1`,
  `frogs=3`, `wings=8`, `guards=7`.
- `NCustom_vdlt`: `Plan valid: True`, `blades=1`, `frogs=3`, `wings=8`,
  `guards=7`.

All four now show `blades=1` (the one-blade shared-side fix is applying to
all of them, including the fallback-path ones), confirming fix #4 (extra
blade) is working for this group at the plan-generation level, at least by
count. This could not be visually confirmed (no close-up screenshots taken
this turn).

### Confirmed regression: `NCustom_7n90` and `NCustom_g832` are now `valid=False`

Both fail the same pre-existing validation check in
`ValidateSectionedDualGaugeSpecialWork` (`src/SectionedSpecialWorkBuilder.cs`,
`IsDualNarrowBranchPreset` block, ~line 2944-2982): it resolves "the fixed
diverging narrow stock/running rail" via `ResolveDivergingFixedStockRail`
(~line 3355) and requires at least one non-suppressed, non-zero-length
`RailRoleSection` for that rail. For these two nodes it currently finds
none.

This validation check itself is **not new** - `git log -S "Fixed diverging
narrow stock/running rail has no renderable"` finds it in commit `fb175d5`
("update", 2026-06-18), weeks before this session's work. What changed
**this session** is the `blades` list that feeds it: today's fallback-path
one-blade filter (`8802240`) cut `NCustom_7n90` and `NCustom_g832` from 2
blades down to 1 (matching `N178`/`NCustom_vdlt`, which were presumably
already effectively single-candidate or coincidentally still resolve). The
2026-07-06 baseline plan exports (pre-fix, `blades=2`) show `Plan valid:
True` for all four of these nodes, so this specific invalid state is new as
of today's blade-filter change - it was not present with the old two-blade
output for these two nodes.

`ResolveDivergingFixedStockRail` picks, among the surviving blades' shared
`StockRail` field, the one whose `SourceRouteIds` contains
`"narrow-reversed"` (preferring `Side.Left`), falling back to the first
narrow-family stock rail. With only one blade left per node, this reduces to
"whatever that one blade's `StockRail` happens to be." Reading the code
alone cannot say why this resolves to a rail with zero renderable sections
for `NCustom_7n90`/`NCustom_g832` specifically while working for
`N178`/`NCustom_vdlt`/`Nove` - wing/guard counts don't explain it either
(`NCustom_g832` and `NCustom_vdlt` have identical `wings=8, guards=7` but
opposite validity). This needs a live diagnostic (log the resolved
`divergingFixed` rail id and its section list for all four nodes) before any
fix is attempted, per this session's standing rule that static reasoning on
this file has been wrong before.

**Practical impact if not fixed**: log evidence from Codex's same-turn run
(`AI_Coordination/codex_runs/run3_full.txt`, line ~39872) shows that
`valid=False` with `customAllowed=False` causes `[Build] Skipping measured
special-work 'special-work:NCustom_g832' ... issues=Fixed diverging narrow
stock/running rail has no renderable role sections.` - i.e. the entire
measured special-work build (blades, frogs, guards, wings - all of this
session's fixes) is skipped for that node, not just a logged warning. If the
same skip applies to `NCustom_7n90` (not directly confirmed in the captured
log excerpt, only the `valid=False`/first-failure summary line was found for
it), both nodes are currently rendering with **none** of this session's
narrow-branch fixes visually applied - they'd fall back to whatever
non-measured rendering path exists instead. This should be the top priority
for the next turn with live access, since it may mean the one-blade fix
regressed two switches from "wrong but rendering" to "not rendering the
intended geometry at all."

### Gauge-separation control (5m gap) applicability

Grepped the entire captured `Player.log` transcript
(`codex_runs/run3_full.txt`) for `Created runtime-only gauge-separation
control node` across this whole session's live runs: only `Nove` and
`NCustom_7n90` ever create this stub (`fuse-ng:n:NCustom_7n90:control` /
`fuse-ng:s:NCustom_7n90:control`). `N178`, `NCustom_g832`, `NCustom_vdlt`
never appear in any such line in the captured transcript - they do not use
`EnsureRuntimeGaugeSeparationControls` at all (their ghost node apparently
already has a valid 3-way connection without the synthetic stub), so the
systemic 5m-gap fix (`f5ad56b`) has nothing to verify on those three. It is
relevant only to `NCustom_7n90` in this group, and `NCustom_7n90`'s plan is
currently `valid=False`, which is a blocker to seeing this fix's effect at
all if the measured build is being skipped for that node as described above.

### Blade orientation / end-cap / LineCurve.Reverse fixes (fix 1-3)

Not switch-specific by construction: `IsForwardTipFartherFromFrog` and
`ReverseRailCurve` (`SectionedSpecialWorkBuilder.cs`) and `RemoveRailEndCap`
(`SpecialWorkHardwareRenderer.cs`) are called generically for every blade,
with no per-node id checks, so they apply uniformly across this preset
group. No code-level reason found why they would behave differently for
`N178`/`NCustom_vdlt` vs `NCustom_7n90`/`NCustom_g832` - but this could not
be visually confirmed this turn (no close-up screenshots), and per this
session's standing rule that should not be treated as proof.

### Summary for this group (static-only, not live-confirmed)

| Node | Plan valid | Blades | Gauge-sep control? | Notes |
|---|---|---|---|---|
| `N178` | True | 1 | No | Looks clean on paper; not screenshot-verified |
| `NCustom_7n90` | **False (regression)** | 1 | Yes | Special-work build likely skipped entirely; needs urgent live diagnostic |
| `NCustom_g832` | **False (regression)** | 1 | No | Special-work build confirmed skipped in log; needs urgent live diagnostic |
| `NCustom_vdlt` | True | 1 | No | Looks clean on paper; not screenshot-verified |

Live confirmation (close-up screenshots in both switch states, fresh
Player.log/plan re-export after any fix) is pending manual testing by the
user, since the automated TestBridge/live-launch pipeline is being retired
for now per the user's direct request this turn.
