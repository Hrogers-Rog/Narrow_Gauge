# Coordination Status

Last updated by: Claude - 2026-07-07

## Current phase: found and fixed real root causes for Nove's blade orientation via live diagnostic logging; both-diverge fixes also applied; new structural issues surfaced at Nove (extra blade, missing frog) still open

This turn applied the two confirmed both-diverge fixes from Codex's review,
then did a live, diagnostic-driven investigation of Nove's "blade still
backwards" symptom (previous turns' static-code-reading theories about this
were wrong or incomplete - see below). The user drove live testing
throughout since the automated TestBridge pipeline broke; several rounds of
add-diagnostic-log -> build -> deploy -> user reloads -> read Player.log
were used instead of screenshots alone.

## Applied both-diverge fixes (from Codex's review, both confirmed via code reading)

1. `OwnershipCuts` (`src/SpecialWorkHardwareRenderer.cs` ~221-239): removed
   the `isGaugeSeparation`-only gate on the `sourceRouteIds` filter so ALL
   measured presets (not just `DualSplit`) filter work intervals to the
   routes that actually touch the source segment. Fixes `NCustom_ltci`
   double-claiming `SCustom_ttpp`/`SCustom_snvo` from neighboring switches.
2. `AddSupplementalGuardPair` (`src/SectionedSpecialWorkBuilder.cs` ~2467):
   now skips a candidate guard if one already exists for the same
   `(FrogId, OppositeRunningRail)` pair. Fixes the confirmed exact-duplicate
   guards (`v2-guard:0`==`v2-guard:8` etc.) at `NCustom_p997`,
   `NCustom_ltci`, `NDeHartPassing_wqbb`.

Not yet re-verified live after deployment (session moved on to the
narrow-branch investigation) - next turn should re-check
`p997`/`ltci`/`wqbb` with fresh screenshots.

## Root-caused and fixed: Nove's blade orientation (three distinct bugs, found via live diagnostic logging, not static reading)

Static code reading in prior turns (including my own) produced multiple
wrong conclusions about this bug (the "backward-extending blade" fix from
`c1b5873` was claimed to not apply to Nove - that claim was **itself wrong**,
verified this turn by actually working through the real logged numbers).
Going forward: **don't trust static distance/direction reasoning about this
switch-blade code without a live diagnostic log to check it against** - the
geometry is subtle enough that hand-tracing gets it wrong repeatedly.

1. **`switchNode`'s position is not a reliable stand-in for the physical
   toe.** The code (`BuildDualNarrowBranchBlades`/
   `TryBuildMeasuredDualSplitBlade` in `src/SectionedSpecialWorkBuilder.cs`)
   used `switchNode.transform.localPosition` (the `Nove:control` node) as
   the reference point to decide which end of a blade's tip/root interval is
   the physical tip. The user confirmed directly in-game that
   `Nove:control` sits **past the end of the switch entirely**, on the frog
   side, not at the toe. Confirmed via a live diagnostic log
   (`[BladeVsFrogDiagnostic]`) that `BladeCurve.Head` was *closer* to the
   real frog than `BladeCurve.Tail` was (20.569 vs 25.166) - i.e. backwards.
   **Fix**: added `IsForwardTipFartherFromFrog` - determines tip-vs-root
   direction from the nearest real frog-kind `RailIntersection` instead of
   `switchNode`'s position, for both blade-construction call sites. Applies
   consistently to both `bladeCurve` and `closureCurve` (a first attempt
   only patched `bladeCurve` after the fact and left `closureCurve` wrong -
   corrected to determine direction once, upstream, before building either).

2. **`RemoveRailEndCap`'s "start cap" was hand-unaware.**
   `BuildStockRailMesh` (decompiled base game, `TrackMeshBuilder.cs`)
   reverses point order internally for `Hand.Left` curves before extruding
   (with a compensating `profileScale` remap that's correct). But that means
   for `Hand.Left` blades, the mesh's own "first" cap (in the triangle
   buffer `RemoveRailEndCap` operates on) corresponds to *our* Tail, not
   Head. `CreatePointBlade` (`src/SpecialWorkHardwareRenderer.cs`) called
   `RemoveRailEndCap(mesh, points.Length, removeStartCap: true)`
   unconditionally, so `Hand.Left` blades removed the cap from the
   full-width heel and left one on the knife-edge tip - backwards. **Fix**:
   `removeStartCap: pivotedCurve.hand != Hand.Left`.

3. **`LineCurve.Reverse()` doesn't recompute per-point rotation/direction,
   only point order.** (Confirmed by reading the decompiled
   `Core/LineCurve.cs`: `Reverse()` is just
   `new LineCurve(Points.Reverse(), hand)`.) Every place in this mod that
   called raw `.Reverse()` on a *singly*-reversed slice (not the
   double-reversal flare pattern, which cancels out and is safe) left each
   point's direction facing the *original* forward direction - backwards
   relative to the new traversal order after reversal. For `Hand.Left`
   curves this accidentally canceled out against `BuildStockRailMesh`'s own
   internal Hand.Left reversal (double-reversal happens to restore
   consistency); for `Hand.Right` curves it didn't, and the mesh rendered
   with flipped winding/normals ("inside out" - this is what the user saw
   after fix #1 above changed which blades got reversed). **Fix**: added
   `SectionedSpecialWorkBuilder.ReverseRailCurve` (internal, reused from
   `SpecialWorkHardwareRenderer.cs` too) - reverses point order AND negates
   each point's direction so the curve is self-consistent. Replaced the
   single-reversal call sites: both blade-curve builders in
   `SectionedSpecialWorkBuilder.cs`, and `CreateVeeWingRail`'s wing-rail
   slice + `SliceSignedSpan` in `SpecialWorkHardwareRenderer.cs`. Did **not**
   touch the guard-rail flare functions (`FlareGuardRailEnds`/
   `FlareGuardRailEndsAwayFrom`) - those reverse twice back-to-back
   (compute/insert a flare point, reverse, repeat for the other end, reverse
   back), which cancels out safely and inserts new points with their own
   freshly-computed rotations. There may be other single-reversal call
   sites elsewhere in the codebase not yet audited (this was a targeted fix
   for the reported symptoms, not an exhaustive sweep) - grep `\.Reverse()`
   across `src/` if a similar "inside-out" symptom shows up elsewhere.

User confirmed after all three fixes: "much better" - the backwards-facing
taper and inside-out wing-rail rendering are visibly improved. **Do not
claim Nove is fully fixed** - two new, distinct issues surfaced in the same
testing pass, still open (see below).

## New issues found at Nove this turn - NOT yet investigated in code

1. **Nove may only need one blade, not two.** User: "there should only be
   one blade on the right.. this is a narrow diverge only standard through
   its a only one blade." If true, Nove's actual physical layout only
   switches the narrow-gauge route (standard gauge runs through fixed,
   unswitched) - meaning `BuildBladeSpecs`/the truth table currently
   selected for Nove is producing a second blade (`NarrowStraightPointBlade`,
   for the narrow-normal:right/narrow-reversed:right pair) that shouldn't
   exist at all. Not yet investigated - needs checking which truth table
   Nove actually selects and whether a different one (or the same one with
   a corrected `blades` array) matches its real layout.
2. **Missing frog where narrow-normal and narrow-reversed physically
   cross.** User screenshot shows two rails crossing directly with no frog
   casting at that point (segment `SCustom_epu2`, confirmed via
   `Player.log` ownership-claim grep to belong solely to `special-work:Nove`
   - not a neighboring-switch issue). Not yet investigated - may be the
   same root cause as #1 (if the truth table doesn't anticipate this
   crossing needing its own frog) or a separate frog-candidate-detection
   gap.

These two are very likely connected to the SAME underlying issue: Nove's
selected truth table / blade-spec generation may not match its actual
measured geometry (2 blades + missing frog vs. the user's expectation of 1
blade). Next turn should investigate `BuildBladeSpecs`
(`src/SectionedSpecialWorkBuilder.cs` ~708) and which
`SpecialWorkTruthTableCatalog` entry Nove is actually selecting, with fresh
live frog/intersection data, before changing anything.

## Diagnostic logging left in place (intentional, not cleanup debt yet)

`src/SectionedSpecialWorkBuilder.cs` (`[SwitchPointDiagnostic]`) and
`src/SpecialWorkHardwareRenderer.cs` (`[BladeMeshDiagnostic]`,
`[BladeVsFrogDiagnostic]`) still log on every measured-switch build. These
were essential to actually finding the bugs above through static reading
repeatedly getting it wrong - keep them until the remaining two issues
above are resolved, then strip them in a follow-up cleanup pass.

## Standing rule (still in force, reinforced this turn)

Do not claim a switch is fixed from log validation or static code reading
alone - and per this turn's experience, do not fully trust static
tip/root/distance reasoning about this blade code either, even when
double- and triple-checked by hand. Add a live diagnostic log and check it
against real logged numbers before committing to a theory. Only a live,
current-build screenshot (or the user's direct in-game confirmation)
counts as verification.

## Next turn

1. Investigate whether Nove should have one blade instead of two, and the
   missing frog at the narrow-normal/narrow-reversed crossing - likely the
   same root cause. Use fresh live plan/frog data and the diagnostic logs
   already in place.
2. Re-verify the both-diverge fixes (`p997`/`ltci`/`wqbb`) live with fresh
   screenshots - applied but not re-checked after deployment this turn.
3. Once Nove is confirmed genuinely fixed (close-up screenshot, both closed
   and thrown states, matching what a correct switch should look like),
   check whether the same three blade-orientation bugs explain `N178`/
   `S4u5`, `NCustom_7n90`/`194b`, `NCustom_g832`/`6wx3`,
   `NCustom_vdlt`/`e6i0` (the rest of the narrow-branch-joins-main group) -
   they share the same code path, so likely yes, but confirm per-switch
   rather than assuming.

## Open questions / blockers

- Whether the "one blade, not two" / "missing frog" issues at Nove share a
  root cause with the blade-orientation bugs just fixed, or are separate.
- Whether the other 4 narrow-branch-joins-main switches have the same
  extra-blade/missing-frog pattern as Nove, or if that's specific to Nove's
  layout.
- Whether there are other single-`.Reverse()`-call sites elsewhere in the
  codebase with the same stale-rotation bug, not yet audited.

## Previous phase (superseded, kept for history)

<details><summary>original text below, no longer current</summary>

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
