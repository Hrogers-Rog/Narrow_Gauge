# Coordination Status

Last updated by: Codex - 2026-07-06

## Current phase: both-diverge investigation complete; source fixes still pending

Codex completed the investigation-only pass for the seven
`dual.both-diverge` measured switches and wrote the full handoff to:

`AI_Coordination/reviews/both-diverge-group-investigation-2026-07-06.md`

No `src/*.cs` or `truth/*.json` files were edited. Build/deploy and the
live test bridge pipeline were used only to force a fresh plan export and
capture reference screenshots. Cleanup was verified directly: no
`Railroader` process remained, TestBridge was disabled in
`Mods.fuseGEo\FUSE.TestBridge\Info.json`, `steam_appid.txt` was removed,
and no temporary bridge request/result files remained.

Confirmed findings from the both-diverge group:

- `NCustom_p997`, `NCustom_ltci`, and `NDeHartPassing_wqbb` have exact
  duplicate guard endpoint groups in the fresh `PieceEndpoints` export.
  The confirmed code path is ordinary guard generation followed by
  `AddDualBothDivergeSupplementalGuards` /
  `AddSupplementalGuardPair` in `src/SectionedSpecialWorkBuilder.cs`
  without any de-duplication against already-created guards.
- `NCustom_ltci` has a separate ownership-cut boundary bug: it double-claims
  `SCustom_ttpp` with `NCustom_fl15` and `SCustom_snvo` with
  `NCustom_g832`. The traced mechanism is `OwnershipCuts` in
  `src/SpecialWorkHardwareRenderer.cs`: for non-`DualSplit` presets it
  admits an analysis by source segment, then scans all work intervals
  instead of filtering intervals by the matching source route ids.
- `NCustom_u6n0` was investigated and remains inconclusive: no duplicate
  guard endpoints and no double-owner claim on `SCustom_s3y7`, but it uses
  measured-geometry fallback and a synthesized frog.
- `NCustom_fc97`, `NCustom_l4a4`, and `Npv2` were sanity-checked. No exact
  duplicate guard endpoints or `GeometryContinuity` issues were found in
  the fresh exports. `l4a4` and `Npv2` also use measured-geometry fallback.

## Next turn

Claude should read the new both-diverge review and any parallel
narrow-branch investigation results before applying source fixes
sequentially. Do not start from the older p997-only conclusions; the fresh
2026-07-06 export is now the authoritative both-diverge evidence.

Suggested fix order:

1. De-duplicate or make semantic the both-diverge supplemental guard pass so
   it fills missing guard coverage instead of blindly adding guards that can
   exactly duplicate ordinary frog guards.
2. Tighten `OwnershipCuts` so non-`DualSplit` measured switches filter work
   intervals by the source route ids for the source segment, then test
   whether a further node-end/nearest-owner boundary rule is needed for
   shared-entry route ambiguity.
3. Re-check the crossing frog `ContinuousStockHandoff` path for p997/ltci
   only after duplicate guards are removed. It is active for both-diverge
   crossing frogs, but this investigation did not prove it is the root
   cause.
4. Rebuild/deploy, force a fresh plan export, grep ownership claims for
   `SCustom_ttpp`, `SCustom_snvo`, and `SCustom_6wx3`, scan for duplicate
   guard endpoints, and capture close-up screenshots for at least
   `NCustom_p997`, `NCustom_ltci`, `NCustom_u6n0`, and
   `NDeHartPassing_wqbb`.

## Open questions / blockers

- Whether the parallel narrow-branch-joins-main investigation has completed
  and found additional shared truth-table or blade-assignment causes.
- Whether route-id filtering alone is enough for ownership cuts. Because
  `TryBuildRoute` stores both incoming and outgoing segment ids on a route,
  some common-entry segments may still need boundary scoping after the first
  filter is added.
- Whether synthesized frogs in `NCustom_u6n0`, `NDeHartPassing_wqbb`,
  `NCustom_l4a4`, or `Npv2` are laterally shifted in the user's "one rail
  head width" sense. This was not proven in the investigation-only pass.

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
