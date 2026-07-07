# Coordination Status

Last updated by: Claude - 2026-07-07 16:30

## Current phase: found a systemic gap bug affecting all gauge-separation switches; Nove's frog-collapse logic also suspect; scope widening to multi-agent sweep across remaining switches

Since the previous entry (Codex's 7n90 one-blade fix), Claude continued
live-testing Nove directly with the user. Confirmed via user feedback: the
extra-blade fix and blade-orientation fixes are working (user said "much
better" earlier), but two new issues surfaced and one is now understood and
fixed (pending live re-verification):

### Fixed this turn: 5m gap at every gauge-separation control switch (systemic, not Nove-specific)

`SpecialWorkTopologySynchronizer.EnsureRuntimeGaugeSeparationControls`
creates a fake "control" node+segment purely so the base game's switch
detection sees a valid 3-way junction at a ghost node where only one gauge
diverges (`GhostControlLength = 5f` meters away, in the same direction as
the standard-gauge continuation). This is a real, physical, visible gap the
user confirmed ("large gaps between nove and nove:control... real rail with
a visible physical gap").

Root cause: `CreateGaugeSeparationControlShell`
(`src/NarrowGaugeTrackBuilder.cs`) builds fallback rails for this stub only
when `!SpecialWorkHardwareRenderer.HasValidPlan(node)` - for switches with a
valid measured plan (Nove: `planValid=True`), it assumes "measured
special-work owns all turnout rails" and builds nothing. But the measured
special-work system has no concept of this fake control segment at all (not
a real route) - nothing was ever drawing it. Confirmed via log:
`vanillaRailObjects=16` vs `specialWorkRailObjects=14` - special work is
short by exactly a rail pair, and that pair is this stub.

Fix: `IsGeneratedGhostDescriptor`'s `SegmentDescriptor` case in
`src/NarrowGaugeTrackBuilder.cs` was suppressing this segment's own rail
descriptor via **two independent, redundant checks**
(`IsHiddenControlSegment` directly, and `NarrowGaugeManager.IsGeneratedGhost`
- both match because the control segment shares the same `"fuse-ng:s:"` id
prefix as real ghost segments). First attempt only removed the
`IsHiddenControlSegment` check and didn't work ("didn't help") because
`IsGeneratedGhost` alone still matched. Corrected: excluded hidden-control
segments from both checks, so the base game's own default rail rendering
now draws this stub instead of nothing. **Not yet re-verified live** - user
was about to test when this session's scope widened.

This is **systemic** - it affects every switch using
`EnsureRuntimeGaugeSeparationControls` (any `dual.narrow-branch-joins-main`
or `dual.split-standard-narrow` switch with a ghost-node gauge separation),
not just Nove. Should visibly improve `N178`, `NCustom_7n90`,
`NCustom_g832`, `NCustom_vdlt` too, if they have the same control-node
mechanism - not yet confirmed which of them actually do.

### Still open: Nove's frog position/shape

User confirmed (after the blade-orientation and extra-blade fixes) that a
frog now renders where none did before (real progress), but its
position/shape is still wrong. Traced the mechanism:
`CollapseDuplicateFrogHardware`/`ResolveFrogHardwareRail` in
`src/SectionedSpecialWorkBuilder.cs` detects two frog candidates at nearly
the same position for Nove - one for `standard-through x narrow-normal`,
one for `standard-through x narrow-reversed` - and collapses them into a
single frog using only the `narrow-normal` pairing (because
`narrow-reversed`'s rail at that point is flagged `SharedDuplicate` of
`narrow-normal`, so hardware gets redirected to the rail that's actually
rendered). This looks like deliberate, sensible logic in isolation, but may
not correctly capture the true geometry where standard, narrow-normal, and
narrow-reversed all converge near the same point. **Not fixed - needs
further investigation**, ideally with a live diagnostic (add logging,
rebuild, have user reload, check Player.log - this is what worked
repeatedly this session, much better than static reasoning alone).

## Scope widening: multi-agent sweep requested

User: "We need to use multiple agents and codex and dig into this narrow
gauge stuff and figure out why we're having issues" - wants a broader,
parallelized investigation now rather than continuing single-threaded on
Nove alone. See LOG.md for the exact assignment split this turn.

## Standing rule (reinforced hard this session)

Static tip/root/distance/suppression reasoning about this codebase gets it
wrong repeatedly, even on second and third re-derivation. The pattern that
actually worked every time: add a targeted diagnostic log, rebuild/deploy,
have a human or live session reload, then read the real logged numbers
before proposing a fix. Do not skip the live-check step to save a cycle -
it has caught wrong theories every single time this session.

## Previous phase (superseded, kept for history)

<details><summary>original text below, no longer current</summary>

## Current phase: Codex patched NCustom_7n90 / SCustom_194b one-blade overbuild; build clean; live verification still needed

User asked Codex to look at `SCustom_194b` while Claude continued on `Nove`.
The `SCustom_194b` screenshots map to `special-work:NCustom_7n90`, a
`dual.narrow-branch-joins-main` measured switch.

## What changed this turn

`NCustom_7n90` is a fallback case: the current exported plan says no truth
table matched and measured geometry fallback was used. That fallback emitted
two narrow point blades, one per side:

- `v2-blade:narrow:Left`
- `v2-blade:narrow:Right`

`BuildBladeSpecs` in `src/SectionedSpecialWorkBuilder.cs` now applies the
same one-blade shared-side rule to both code paths:

1. truth-table matched `dual.narrow-branch-joins-main` nodes keep only blades
   whose movable side matches the detected shared side;
2. measured fallback `dual.narrow-branch-joins-main` nodes now skip fallback
   blade candidates on the non-shared side.

There was already an uncommitted truth-table shared-side filter in
`SectionedSpecialWorkBuilder.cs` when Codex started. Codex kept it, cleaned up
the comment, and extended the same rule to the fallback path that
`NCustom_7n90` actually uses. No truth JSON was changed and no map-specific
ids were added.

Full notes:

`AI_Coordination/reviews/ncustom-7n90-194b-investigation-2026-07-07.md`

## Verification

Build succeeded:

```powershell
dotnet build .\NarrowGaugeMod.csproj
```

Result: 0 warnings, 0 errors.

No fresh live reload/screenshot was performed this turn. The current
`Player.log` and exported `special-work_NCustom_7n90.txt` are still from the
old build and still show `blades=2`.

## Next turn

Claude:

1. Build/deploy/reload the game and verify `NCustom_7n90` with fresh data.
   Expected signs:
   - `Player.log` shows the new measured fallback skip log for one
     `NCustom_7n90` side.
   - `special-work:NCustom_7n90` exports/renders `blades=1`.
   - A close-up of `SCustom_194b` no longer shows the extra overlapping point
     blade.
2. Continue the Nove investigation. This turn's truth-table one-blade filter
   may affect Nove's extra-blade symptom, but Nove's missing frog at the
   narrow-normal/narrow-reversed crossing is not proven fixed.
3. Re-verify the both-diverge fixes (`p997`/`ltci`/`wqbb`) live when the
   narrow-branch checks are stable.

## Open questions / blockers

- `NCustom_7n90`/`SCustom_194b` is build-verified only; live screenshot proof
  is still pending.
- Whether the shared-side one-blade rule also resolves Nove's extra blade in
  the truth-table path.
- Whether Nove's missing frog is a separate frog-candidate/collapse issue.
- Whether other `dual.narrow-branch-joins-main` nodes (`N178`, `NCustom_g832`,
  `NCustom_vdlt`) need the same live one-blade verification.

</details>
