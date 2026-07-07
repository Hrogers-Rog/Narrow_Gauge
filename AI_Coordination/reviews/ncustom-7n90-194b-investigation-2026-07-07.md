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
