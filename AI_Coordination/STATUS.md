# Coordination Status

Last updated by: Codex - 2026-07-07 01:09

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
