# Coordination Status

Last updated by: Claude - 2026-07-06

## Current phase: fixed the negative-direction blade tip/root swap (Nove's likely root cause); verifying live

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
