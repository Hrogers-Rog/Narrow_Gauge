# Coordination Status

Last updated by: Claude - 2026-07-06

## Current phase: broad investigation (defects span more than one system)

This session's targeted fixes (split-standard-narrow zero blades, both-diverge
`SharedDuplicate` suppression, narrow-branch rendering gaps) all landed,
reviewed, and re-tested `valid=True`. The user kept testing in-game with
debug labels on and sent more screenshots. Their direct report:

> "Still a lot of the same issues with double frogs, blades being on the
> outside or wrong side of the rail, and oddness where it seems there is too
> many rails or even maybe a transition in the middle of a switch."

That's four distinct failure modes, not a continuation of the two checks
already fixed. Worse: one flagged fragment (`SCustom_ttpp`, debug-labeled in
a screenshot) **is not part of any of the 14 measured special-work plans at
all** - confirmed via `grep -rl "ttpp" NarrowGauge/SpecialWorkPlans/*.txt`
(no match) and `Player.log` (only ordinary
`SpecialWorkSegmentClip`/`SpecialWorkTieClip` entries, ordinary dual-gauge
segment, not measured-plan membership). This means the bug surface spans at
least two separate systems:

1. `SectionedSpecialWorkBuilder.cs` + `SpecialWorkTruthTableValidator.cs`
   (the measured special-work system this session has been fixing, and
   where `GeometryContinuity`'s new diagnostic applies).
2. The plain dual/narrow-gauge pipeline (`NarrowGaugeTrackBuilder.cs`,
   `NarrowGaugeSwitchGeometry.cs`) that renders every other dual-gauge
   segment and plain narrow switch - **no diagnostic tooling exists for this
   at all**, and no confirmed root cause yet.

Full scope, the four symptoms broken down, and a suggested investigation
approach: `reviews/broad-visual-defect-investigation-2026-07-06.md`. Per
user instruction: stop reacting to individual screenshots, do a real
investigation before patching further.

## What's actually landed and confirmed so far (don't re-litigate these)

- `dual.split-standard-narrow` zero-blade fix (Codex, reviewed by Claude).
- `dual.both-diverge` `SharedDuplicate` suppression fix (Claude).
- Narrow-branch rendering-gap fix: frog rehoming off shared-duplicate rails,
  duplicate-frog collapse, correct diverging-stock-rail selection, blade-root
  endpoint reservation, two validation checks restored to hard failures
  (Codex, two turns, both reviewed by Claude).
- `GeometryContinuity`/`PieceEndpoints` diagnostic sections added to the
  per-switch `.txt` export (Claude), then corrected after a real coverage
  gap was found (`FrogPieces` wasn't included; `Guard` pieces were falsely
  flagged since they're built free-standing by design, not joined to
  anything - see `BuildGuardRails`/`GuardLeadLength`/`GuardTrailLength`).
  This diagnostic is scoped to the measured special-work system only - it
  does not (and should not yet) cover the plain pipeline.
- All four rounds independently rebuilt+deployed with
  `-p:EnableModDeploy=true` by whoever made the change, not just trusted
  from a report.

## Standing rules

- Do not trust `Player.log` `valid=True` as proof anything is visually
  correct - it only covers the 14 measured special-work nodes, and even
  there only checks what it's coded to check.
- Do not "fix" a validator gap by relaxing it further - fix the geometry, or
  restore a check to a hard failure once actually fixed.
- Do not patch symptom-by-symptom from screenshot descriptions. Investigate
  which system + which specific node a symptom belongs to first (per
  `00_PROJECT_CONSTITUTION.md`'s Process section), write findings to
  `reviews/*.md`, then fix.
- Always deploy with `-p:EnableModDeploy=true` for anything meant to be
  tested in-game; plain `dotnet build` does not update what the user plays.

## Next turn

Codex - read `reviews/broad-visual-defect-investigation-2026-07-06.md` in
full, then do the investigation it scopes (not a patch): confirm/refute
`SCustom_ttpp`'s system membership, read `NarrowGaugeTrackBuilder.cs`/
`NarrowGaugeSwitchGeometry.cs` for the plain pipeline with the same rigor
this session gave `SectionedSpecialWorkBuilder.cs`, and map each of the
user's four symptoms to a system/node/cause before proposing any fix.
Record findings in a new `reviews/*.md` file (the investigation-scope file
is not the findings file). If a symptom can't be confidently mapped, say so
in this file rather than guessing - the user is available to clarify with
another debug-labeled screenshot if a specific finding needs visual
confirmation.

## Open questions / blockers

None blocking the start of this investigation. Will likely need the user
again once specific hypotheses exist to check against a screenshot or a
fresh session - but don't ask for more screenshots blind before that.
