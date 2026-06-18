# Special-Work Turnout Combo Status

Date: 2026-06-13

This is the working checklist for which turnout/special-work combinations are
implemented, visually accepted, and ready for truth-table hardening.

## Status Legend

| Status | Meaning |
|---|---|
| DONE | Visually accepted in game and log-valid. Ready to preserve with truth tables/tests. |
| LOG-VALID | Runtime plan validates and renders, but needs a final visual pass. |
| PARTIAL | Some geometry exists, but behavior is known to be incomplete or orientation-sensitive. |
| TODO | Catalog/runtime concept exists, but no accepted in-game example yet. |
| RISK | Known likely breakage under another mirror/shared-rail orientation. |

## Current Runtime Examples

These are the custom nodes currently appearing in `Player.log` as measured
special work.

| Combo | Example node(s) | Current result | Truth table state | Notes |
|---|---|---|---|---|
| Dual narrow branch joins/leaves main, right-hand/outside-frog variant | `NCustom_0ifg` | LOG-VALID | Existing table: `DualGauge_NarrowBranch_Right` | Log shows `valid=True`, `frogs=3`, `guards=7`, `blades=2`. Needs final visual sign-off before calling it done. |
| Dual narrow branch joins/leaves main, left-hand/shared-rail-side variant | `NCustom_kvg2` | LOG-VALID | Existing table: `DualGauge_NarrowBranch_Left` | Log shows `valid=True`, `frogs=1`, `guards=2`, `blades=2`. User identified this as the left-hand version of `0ifg`. |
| Dual standard branch joins/leaves main | `NCustom_hzqv` | LOG-VALID | Missing | Log shows `valid=True`, `frogs=2`, `guards=3`, `blades=2`. Needs visual review and then a truth table. |
| Dual both families diverge, mirror pair | `NCustom_24b2`, `NCustom_303j` | DONE | `DualGauge_BothDiverge_LeftHand` / `DualGauge_BothDiverge_RightHand` | Both are visually accepted and log-valid. Their truth tables select the mirror from accepted measured frog pairs and enforce three blades, three frogs, eight wings, and nine guards. |
| Dual splits to standard through and narrow diverging, mirror pair | `NCustom_5f81`, `NCustom_qf3e` | DONE | `DualGauge_SplitStandardNarrow_OutsideFrogs` / `DualGauge_SplitStandardNarrow_SharedSideFrog` | Both are visually accepted and log-valid. Blade ownership, blade direction, frog selection, and crossing-rail trimming are derived from measured geometry rather than node IDs. |

## DONE Combos

### `dual.both-diverge`: `NCustom_24b2` / `NCustom_303j`

Done for the current shared-rail orientation.

Accepted behavior:

- Both mirror examples render valid measured special work.
- Blades are on the correct through/diverging rails for both hands.
- Stock rails remain continuous where expected.
- Rails no longer run through the switch blades.
- Rails no longer run through the vee frogs.
- Supplemental guards are present at the extra frog protection locations.
- `303j` is the right-hand version of `24b2` per visual/user review.

Truth-table requirements:

- Two `dual.both-diverge` truth-table variants select one mirror hand each.
- Assert exactly three blade assemblies.
- Assert at least three accepted vee frogs and eight wing rails.
- Assert the supplemental guard count reaches nine.
- Reject fixed rails under blade corridors.
- Reject duplicate shared rail rendering except where the accepted frog owns the rail.
- Preserve the hand-specific blade sets now implemented in
  `SectionedSpecialWorkBuilder.BuildBladeSpecs`.
- Preserve the hand-specific supplemental guard rail choices now implemented in
  `SectionedSpecialWorkBuilder.AddDualBothDivergeSupplementalGuards`.

Known risk:

- This is not yet proven for `DualGauge_R`. If the shared rail moves to the
  other side, route names may still match while the physical middle/outside
  rail roles change. That should be treated as a separate combo until tested.

## Log-Valid But Not Done

### `dual.narrow-branch-joins-main`: `NCustom_0ifg` / `NCustom_kvg2`

Current state:

- Both variants are log-valid.
- Existing truth tables cover the two narrow-branch hands.
- Needs a final visual pass after the recent shared-rail and blade fixes.

Checklist before DONE:

- Confirm both shared-rail orientations render once.
- Confirm blades are on the through rails and stock rails stay continuous.
- Confirm no fixed rail runs through a blade.
- Confirm guards are on the correct rails and sides for both hands.
- Confirm `DualGauge_R` separately if we support it.

### `dual.standard-branch-joins-main`: `NCustom_hzqv`

Current state:

- Runtime discovery and rendering are log-valid.
- No truth table exists yet.
- Needs visual review.

Checklist before DONE:

- Identify both mirror examples.
- Confirm branch hand and shared-rail side.
- Confirm standard blades and stock rails.
- Confirm narrow-through rail continuity.
- Add truth tables after visual sign-off.

## Catalog Backlog

These presets exist in `SpecialWorkPresetCatalog`, but are not currently marked
done from the live examples above.

| Preset | Status | Notes |
|---|---|---|
| `turnout.standard.left` | TODO | Cataloged; no accepted special-work example in current checklist. |
| `turnout.standard.right` | TODO | Cataloged; no accepted special-work example in current checklist. |
| `turnout.standard.wye` | TODO | Cataloged; no accepted special-work example in current checklist. |
| `turnout.narrow.left` | TODO | Runtime can discover narrow-only switches, but no current accepted example is listed. |
| `turnout.narrow.right` | TODO | Runtime can discover narrow-only switches, but no current accepted example is listed. |
| `turnout.narrow.wye` | TODO | Runtime can discover narrow-only switches, but no current accepted example is listed. |
| `dual.narrow-branch-joins-main` | LOG-VALID | `0ifg`/`kvg2`; truth tables exist, visual sign-off still needed. |
| `dual.standard-branch-joins-main` | LOG-VALID | `hzqv`; needs mirror examples and truth table. |
| `dual.both-diverge` | DONE | `24b2`/`303j`; selector-based mirror truth tables added after visual approval. |
| `dual.split-standard-narrow` | DONE | `5f81`/`qf3e`; both mirror variants have procedural blade/frog ownership and selector-based truth tables. |
| `dual.shared-rail-flip` | LOG-VALID | Cataloged segment-anchored fixed transition; procedurally matches a degree-2 `DualGauge_T` between one `DualGauge_L` and one `DualGauge_R`. |
| `crossing.diamond` | TODO | Cataloged; no accepted example yet. |
| `crossing.arbitrary-angle` | TODO | Cataloged; no accepted example yet. |
| `crossing.90-degree` | TODO | Cataloged; no accepted example yet. |
| `slip.single` | TODO | Cataloged; no accepted example yet. |
| `slip.double` | TODO | Cataloged; no accepted example yet. |
| `stub.left` | TODO | Cataloged; no accepted example yet. |
| `stub.right` | TODO | Cataloged; no accepted example yet. |
| `stub.three-way` | TODO | Cataloged; no accepted example yet. |
| `three-way.standard` | TODO | Cataloged; no accepted example yet. |
| `three-way.narrow` | TODO | Cataloged; no accepted example yet. |
| `three-way.dual` | TODO | Cataloged; no accepted example yet. |

## Shared-Rail Orientation Matrix

Use this matrix when testing future mirrors.

| Preset family | DualGauge/current shared side | DualGauge_R swapped shared side |
|---|---|---|
| `dual.narrow-branch-joins-main` | LOG-VALID on `0ifg`/`kvg2`; needs visual sign-off | RISK / TODO |
| `dual.standard-branch-joins-main` | LOG-VALID on `hzqv`; needs visual sign-off | RISK / TODO |
| `dual.both-diverge` | DONE on `24b2`/`303j` | RISK / TODO |
| `dual.split-standard-narrow` | DONE on `5f81`/`qf3e` | Additional shared-rail-flipped examples fall back to measured geometry unless a matching truth-table selector exists. |
| `dual.shared-rail-flip` | PROCEDURAL / VISUAL TEST | PROCEDURAL / VISUAL TEST |
| `three-way.dual` | TODO | TODO |
