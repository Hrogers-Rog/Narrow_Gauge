# Coordination Status

Last updated by: Claude - 2026-07-06

## Current phase: blade tip/root fix confirmed NOT to touch Nove; scope widened to all 14 measured switches, split into parallel investigation threads

**Correction to previous turn**: user tested the deployed blade tip/root fix
live at `Nove` (multiple close-up screenshots, including a very tight
rail-level close-up) and confirmed the blade still looks backwards. I
re-derived `bladeExtendsForward` for Nove's actual measured data
(`tip=29.856 root=34.457` for `NarrowPointBlade`, `switchDist` computed the
same way inside `TryFindBladeDistances`) and confirmed `bladeExtendsForward
== true` for both of Nove's blades - meaning the `.Reverse()` branch added
last turn **never executes for Nove**. That fix is real and may still be
correct for whatever backward-extending blade originally motivated it, but
it is **not** Nove's bug. Do not re-claim it as Nove's fix.

Also re-examined the truth-table selector theory that was flagged as
unsafe-to-patch last turn: `SpecialWorkTruthTableCatalog.TryGet`'s
`MatchesSelector` checks a real geometric frog/intersection pair
(`selectorFrogPair.railA` x `railB`, by route+side), not an arbitrary
first-match - so `DualGauge_NarrowBranch_Left` vs `_Right` choosing
differently for `Nove` vs `N178` is not automatically a bug; it could
reflect genuinely mirrored physical geometry. This needs live plan/frog
data per switch to confirm, not more static reading - handing this to a
subagent to investigate with fresh eyes now that scope has widened.

## Scope widened: user wants all measured switches audited, not just Nove

User: "there are issues with every turnout but 936m and that's because it
doesn't have a double frog" - confirming 936m is a plain (non-measured)
switch outside this system, and every one of the 14 measured special-work
switches has a reported or suspected defect. Full list with preset and
known symptom (segment -> owning switch mapping done via
`[SpecialWorkOwnershipCutClaim]` grep in `Player.log`):

**dual.narrow-branch-joins-main (5)**: `N178` (segment `S4u5`: blades on
wrong rails - should be left-diverge/right-through, are left-through/
right-diverge), `NCustom_7n90` (segment `194b`), `NCustom_g832` (segment
`6wx3`), `NCustom_vdlt` (segment `e6i0`: "frog rendering inside out and
trying to render blades"), `Nove` (blade still runs into the switch when
thrown - confirmed broken by user this turn, see correction above).

**dual.both-diverge (7)**: `NCustom_p997` (segment `dkzn`: multiple
disconnected/kinked rail fragments, "double frog" mess - Codex found a
literal duplicate `v2-guard:8`==`v2-guard:0` here, not yet fixed),
`NCustom_ltci` (double-claims BOTH `ttpp` and `snvo`, which otherwise
belong to `NCustom_fl15` and `NCustom_g832` respectively - likely
over-claiming past its own switch's boundary), `NCustom_u6n0` (segment
`s3y7`), `NDeHartPassing_wqbb` (segment `tliv`), `NCustom_fc97`,
`NCustom_l4a4`, `Npv2` (no specific symptom reported yet - lower priority,
worth a sanity pass).

**dual.standard-branch-joins-main (2)**: `NCustom_fl15` (loses part of
`ttpp` to `ltci`'s over-claim above), `NDeHartPassing_33d6` (no specific
symptom reported yet).

General pattern from the user's original description: "every switch that
has a double frog [has its] frog or guard shifted about the width of a
rail head to the left or right... sometimes inside out."

## This turn's plan: two parallel investigation-only threads, then sequential fixes

Given multiple switches share a preset (likely shared root cause within a
group) and multiple agents editing `SectionedSpecialWorkBuilder.cs`/
`SpecialWorkHardwareRenderer.cs` concurrently risks real conflicts, this
round is investigation-only for both threads (read code + live plan/frog
data, write findings to a new `reviews/*.md` file, do **not** edit source,
do **not** commit code changes). Claude will apply fixes sequentially once
both threads report back, then rebuild/redeploy/re-verify live with
screenshots per the standing rule below.

- **Codex** (this turn): both-diverge group - `NCustom_p997`/`dkzn`
  overlapping hardware (continue prior thread), `NCustom_ltci`'s
  `ttpp`/`snvo` over-claim, `NCustom_u6n0`/`s3y7`, `NDeHartPassing_wqbb`/
  `tliv`. Also sanity-pass `fc97`/`l4a4`/`Npv2`/`fl15`/`33d6` (no reported
  symptom yet) for the same overlapping-hardware pattern using fresh plan
  exports.
- **Claude subagent** (this turn): narrow-branch-joins-main group (all 5:
  `N178`, `7n90`, `g832`, `vdlt`, `Nove`) - investigate whether the
  truth-table selection is actually correct per-switch (needs live frog
  data to check `selectorFrogPair` matches), and whether blade
  `movableRouteId`/`movableSide`/`stockRouteId`/`stockSide` assignment in
  the truth table JSON (`truth/SpecialWorkTruthTables.json`) is consistent
  with each switch's actual measured hand.

## Previous phase (superseded, kept for history)

<details><summary>original text below, no longer current</summary>

Reviewed Codex's investigation turn (found real, specific bugs, correctly
did not claim a fix without proof - good discipline). Verified its most
concrete finding directly by reading the code myself, not just trusting
the write-up, and fixed it.

## Fixed: negative-direction blade tip/root swap

`TryFindBladeDistances` (`src/SectionedSpecialWorkBuilder.cs` ~3507) always
returns `(tip, root)` numerically sorted ascending (`root - tip >=
MinimumPieceLength` is enforced) - needed for interval bookkeeping (cuts,
closures). But the *physical* blade tip is always at the switch throat
(`switchDist`/`tipDistance`, where `switchNode.transform.localPosition` is)
regardless of which direction the blade extends. When a blade extends
*backward* (toward decreasing curve distance - confirmed this happens for
`Nove`, per Codex's stale-export finding of an oddly short
`NarrowPointBlade:closure`), the smaller sorted value ("tip") is actually
the physical root/heel, and the larger sorted value ("root", == switchDist)
is actually the physical tip.

Both call sites (`BuildDualNarrowBranchBlades` ~452-479, and
`TryBuildMeasuredDualSplitBlade` ~581-598) built `BladeCurve` via a plain
ascending `Slice(movable.Curve, tip, root)`, which for backward-extending
blades put the physical *root* at `BladeCurve.Head` and the physical *tip*
at `BladeCurve.Tail` - exactly backward from what
`CalculateBladeOpenRotation`/`CreatePointBlade`
(`src/SpecialWorkHardwareRenderer.cs`) assume (`Head`=tip, `Tail`=root/pivot).
This means the rotation math that's supposed to swing the free tip away
from the stock rail was operating on the wrong end - a strong, well-reasoned
match for "the blade running into the switch instead of away from it."

**Fix**: reverse the sliced curve for backward-extending blades
(`bladeExtendsForward` already computed and available at both call sites)
so `Head`/`Tail` land on the correct physical ends regardless of numeric
sort direction. Confirmed `LineCurve.Reverse()` exists
(`Decompiled dlls base game/Core/LineCurve.cs`) rather than assumed. Did
not touch the numeric `tip`/`root` fields themselves (`TipDistance`/
`RootDistance` on `SwitchBladePlan`) since other code (cut/suppression
intervals) correctly doesn't care about direction, only extent - keeping
that minimal rather than restructuring more than needed.

Built (0 warnings/0 errors), deployed. **Live verification in progress** -
about to get a close-up screenshot of `Nove`'s blade specifically via the
proven `NarrowGaugeTestBridge` pipeline. Per the standing rule, will not
report this as fixed until that close-up actually shows the blade opening
away from the switch instead of into it.

## Did not touch this turn (per Codex's findings, still open)

- `dkzn`/`NCustom_p997`'s overlapping hardware (crossing handoff, local
  crossing guard, duplicated supplemental guard `v2-guard:8`/`v2-guard:0`) -
  Codex correctly flagged this needs careful interaction analysis, not a
  quick patch. Still open.
- The truth-table selector matching on `SharedOverlap` (zero-angle, not a
  real frog/crossing) instead of measured geometry - Codex flagged this
  isn't safe to patch without more work (risk of "falls through" for N178).
  Still open. Note: this is a *separate* bug from the one just fixed - even
  if `N178` is selecting a technically-valid-but-wrong-for-this-geometry
  truth table, `Nove`'s blade issue was a distinct geometry-construction bug
  regardless of which table it selected.

## Standing rule (still in force)

Do not claim `Nove` is fixed until a close-up screenshot actually shows it.
Log validation and medium-distance screenshots are not proof.

## Next turn

Verify the blade fix live (in progress - close-up screenshot of Nove).
If confirmed: check whether it also helps `S4u5`/`N178` (same bug class,
different node) with its own close-up. If refuted: re-open the
investigation, the bug may be more subtle than diagnosed (e.g. wrong-side
still visible even with correct Head/Tail, pointing at the separate
truth-table selector issue instead/also).

Then resume `dkzn`/`p997`'s overlapping-hardware investigation per Codex's
scoped next steps.

## Open questions / blockers

Whether the blade tip/root fix actually resolves Nove's visual symptom -
close-up verification in progress.

</details>
