# vdlt selects the mirror narrow-branch truth table - 2026-07-09 (Claude)

## Symptom

`NCustom_vdlt` renders its narrow blades on the wrong rails: the through
(`narrow-normal`) blade on the right and the diverging (`narrow-reversed`)
blade on the left. The user wants through-left / diverge-right, which is what
the working mirror switch `NCustom_g832` already shows. The user states the
mirror is wrong for this one switch only; everything else is working
(Codex stabilized the rest).

## Diagnosis

`dual.narrow-branch-joins-main` has two truth tables, mirror images of each
other (`truth/SpecialWorkTruthTables.json`):

- `DualGauge_NarrowBranch_Left`  blades: `narrow-normal:Left` (through) /
  `narrow-reversed:Right` (diverge)  = through-left, diverge-right.
- `DualGauge_NarrowBranch_Right` blades: `narrow-normal:Right` (through) /
  `narrow-reversed:Left` (diverge)  = through-right, diverge-left.

Live log (Codex's current build):
- g832 -> `DualGauge_NarrowBranch_Left` (correct, user-confirmed working).
- vdlt -> `DualGauge_NarrowBranch_Right` (wrong hand, the reported defect).

Selection is done by `SpecialWorkTruthTableCatalog.MatchesSelector`
(`SpecialWorkTruthTableValidator.cs:833-851`): a table matches if ANY
intersection of ANY kind exists between its `selectorFrogPair`'s two rails.
Near a frog, narrow rails cross the standard rail on both sides, so both
tables' selector pairs frequently have a matching intersection. `TryGet`
then takes `candidates.FirstOrDefault(MatchesSelector)`, and `_Right` is
first in file order, so it wins any ambiguous case. That is why vdlt lands
on `_Right`.

The selector's rail-side labels are themselves subject to the same narrow
hand mislabeling seen all session (narrow normal/reversed/left/right come
from an independent ghost-node `DecodeSwitchAt`), so matching on rail-side
identity is fundamentally unreliable for choosing the hand. The physical
divergence direction is not: EF&A geometry shows vdlt's narrow branch
(`-> NCustom_12uq`, bearing 338.5deg) leaves ~6deg to the LEFT of its
straight-through line (`-> NDeHartPassing_33d6`, 344.5deg). A left-hand
divergence should select the `_Left` table.

## Fix (design)

Select the narrow-branch variant by the switch's true divergence direction
instead of by ambiguous rail-pair intersection matching, but ONLY for the
crossing-frog anatomy (the paired-blade-set case Codex added, i.e.
vdlt/g832). Switches with no cross-family crossing frog (`N178`, `Nove`) are
left entirely on the existing selector path - not touched.

Divergence hand is computed unambiguously using the cross-family crossing
frog as the downstream reference (removing the curve-traversal-direction
ambiguity that made the fallback path's `leftHandTurnout` unreliable):

- `forward` = crossing frog position - switch throat (node) position.
- `offset` = (narrow-reversed nearest point to the frog) - (narrow-normal
  nearest point to the frog).
- `divergesLeft = Vector3.Cross(forward, offset).y < 0` (Unity left-handed
  Y-up: `Cross(forward, offset).y > 0` means offset is to the right of
  forward).
- Left divergence -> `_Left` table; right -> `_Right`.

Safety: this only changes the table for crossing-frog narrow-branch
switches. g832 currently selects `_Left` and (per the user wanting vdlt to
match it) diverges the same way, so it should compute left and keep `_Left`
- no change. The only intended behavior change is vdlt `_Right` -> `_Left`.
A prominent `[BladeSpecs] NarrowBranchHand` diagnostic logs the computed
hand and chosen table for every narrow-branch switch so the calibration is
visible on the next live run. If the sign is inverted (g832 shows right /
regresses), it is a one-line flip of the `< 0` comparison.

## Verification required (manual, full game restart - DLL loads at startup)

- vdlt: `[BladeSpecs] NarrowBranchHand ... divergesLeft=True chosen=..._Left`
  and its blades render through-left / diverge-right (matching g832).
- g832: still `_Left`, unchanged, both blades correct.
- N178 / Nove: unchanged (no crossing frog; existing selector path).
- 7n90: unchanged (measured fallback path, no truth table).
