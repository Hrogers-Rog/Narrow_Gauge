# Coordination Status

Last updated by: Codex - 2026-07-06

## Current phase: broad visual-defect investigation findings ready for review

Codex completed the investigation scoped by
`reviews/broad-visual-defect-investigation-2026-07-06.md`. Findings are in:

`reviews/plain-and-measured-visual-defect-findings-2026-07-06.md`

No code patch was made this turn.

## Main findings

- `SCustom_ttpp` is confirmed as an ordinary authored `DualGauge_R` segment,
  not a measured special-work node and not a missing 15th plan. In the
  authored graph it runs from `NCustom_fl15` to `NCustom_ltci`; both endpoint
  nodes have measured special-work exports. Its rails are built by the plain
  dual-gauge segment path in `NarrowGaugeTrackBuilder.cs`, then clipped by
  `CreateRailMeshesWithFrogCuts`.
- The `SCustom_ttpp` clip logs are real, but their source is ambiguous:
  `CreateRailMeshesWithFrogCuts` merges measured ownership cuts,
  gauge-separation frog cuts, and possible shared-rail flip cuts under one
  `[SpecialWorkSegmentClip]` log label. A narrow diagnostic that logs cut
  source by interval is the next useful step for this specific segment.
- The plain dual/narrow switch pipeline has at least one strong code
  hypothesis for wrong-side blades: `CreateDualGaugeNarrowSplitSwitchRailObjects`
  hardcodes `aThirdRails.right` when resolving the dual middle rail, while the
  normal dual-gauge segment/gauge-separation code consults
  `DualGaugeSharedRailRegistry.SharesRightRail`. Do not patch this until a
  labeled failing switch is tied to this path.
- Double frogs currently map more strongly to measured special-work rendering
  than to `SCustom_ttpp`. Relevant nodes include `NCustom_fl15`,
  `NCustom_ltci`, and `NCustom_fc97`, all of which render three frogs in the
  current logs. The likely code area is frog candidate collapse and compound
  vee rendering, but the specific screenshot node is still needed before
  changing tolerances or suppressions.
- "Too many rails" may be measured-plan extra fixed/shared pieces, generated
  transition-switch duplicate suppression misses, or the current plain
  mixed-switch fallback for `aNarrowOnly && bDual`. The symptom is not mapped
  to a single code path yet.
- The possible "transition in the middle of a switch" is not confirmed by the
  current logs. `TryResolveSharedRailFlip` is disabled, and no current
  `SharedRailTransition` log entries were found around `SCustom_ttpp`.

## Diagnostic caution

Live plan exports still show `ISOLATED: v2-guard:*` lines, but the checked-out
`SpecialWorkPlanExporter.cs` source now suppresses guard-only isolated
verdicts. Treat guard isolation lines in the current live exports as stale or
diagnostic-mismatch evidence, not confirmed geometry defects. Fixed-piece
isolation lines near `NCustom_fl15` and `NCustom_ltci` remain plausible visual
fragment candidates.

## Standing rules

- Do not trust `Player.log` `valid=True` as proof anything is visually
  correct.
- Do not relax validation to hide geometry defects.
- Do not patch all four reported symptoms together. Map a symptom to a
  node/system/code path first.
- Always deploy with `-p:EnableModDeploy=true` for anything meant to be
  tested in-game. This turn made documentation/review updates only, so no
  deploy was done.

## Next turn

Claude - review
`reviews/plain-and-measured-visual-defect-findings-2026-07-06.md`, especially:

1. the `SCustom_ttpp` membership conclusion and cut-source ambiguity;
2. the plain split-switch `aThirdRails.right` shared-rail-side hypothesis;
3. the measured frog/compound vee mapping for double frogs;
4. the stale/mismatched guard isolation diagnostic warning.

Then decide whether to add a targeted diagnostic first or ask the user for a
focused debug-labeled screenshot for one symptom. Avoid a broad patch.

## Open questions / blockers

- Which exact node/segment label corresponds to each reported wrong-side
  blade and double-frog screenshot?
- Are `SCustom_ttpp` cuts coming from measured ownership, gauge separation, or
  both?
- Were the current live plan exports generated from the checked-out exporter
  code, given the guard-isolation mismatch?
