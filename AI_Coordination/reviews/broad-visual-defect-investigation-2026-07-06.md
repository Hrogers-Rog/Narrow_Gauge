# Broad visual-defect investigation (2026-07-06)

## Why this file exists

After the narrow-branch geometry fix (`2b6cef8`/`916ee61`) and the
geometry-continuity diagnostic tool landed, the user kept testing in-game
and sending screenshots (with the mod's debug label overlay on, showing
exact segment IDs like `fuse-ng:s:Nove:control`, `SCustom_e6i0`,
`SCustom_ttpp`). The pattern of what's still wrong is broader than the two
specific checks fixed so far - this file scopes a real investigation before
anyone patches further.

## User's direct symptom report (verbatim intent, 2026-07-06)

"Still a lot of the same issues with double frogs, blades being on the
outside or wrong side of the rail, and oddness where it seems there is too
many rails or even maybe a transition in the middle of a switch."

Read literally, these are four distinct failure modes, not one:

1. **Double frogs** - two frog castings rendered where one is expected.
   Possible causes: `RehomeSharedDuplicateFrogRail`'s rehoming (landed this
   session) placing a frog on a rail that already has its own separate frog
   from a different intersection pair, producing two castings very close
   together instead of one; or `CollapseDuplicateFrogHardware`'s merge
   condition (`SameFrogHardware`, requires same `Kind` + same rail pair +
   position within `CorridorTolerance * 2f` = 0.17m) being too strict to
   catch a real duplicate that's slightly further apart.
2. **Blades on the outside/wrong side of the rail** - this sounds like a
   `RailSide`/`Hand` orientation bug, not a suppression/connectivity bug.
   Neither of this session's fixes touched blade side/hand selection
   directly - worth checking `SwitchBladePlan` construction and whatever
   decides which physical side (`RailSide.Left`/`Right`) a blade sits on
   relative to its stock rail.
3. **Too many rails** - could be the `SharedDuplicate` suppression still not
   covering every case (this session's fix in `AddSharedSuppressions`/
   `AddCrossFamilySharedSuppressions`/`SuppressDualBothDivergeFrogDuplicate`
   was interval-scoped to fix a *different* bug - it doesn't mean every
   duplicate-rail path is covered), or a genuinely separate rail being
   authored/generated that shouldn't exist for this topology.
4. **A transition in the middle of a switch** - unclear if this means a
   `GaugeTransition`/shared-rail-side-flip artifact appearing where it
   shouldn't, or just visual confusion from the other three symptoms
   compounding. Ask the user to point this out specifically with a
   debug-labeled screenshot if it recurs after the other three are
   understood better - don't guess at this one blind.

## Confirmed: defects also exist outside the 14 measured special-work plans

`SCustom_ttpp` (visible with a disconnected fragment in a user screenshot,
debug-labeled) does **not** appear in any of the 14
`NarrowGauge/SpecialWorkPlans/*.txt` exports. Confirmed via
`grep -rl "ttpp" NarrowGauge/SpecialWorkPlans/*.txt` (no matches) and via
`Player.log`, which only shows ordinary `[SpecialWorkSegmentClip]`/
`[SpecialWorkTieClip]` entries for it (rail-clipping near a special-work
exclusion zone, not membership in a measured plan). Its neighbors in that
same log sequence: `SCustom_bltm` (only `rail=L`/`R` - a **narrow-only**
segment, 2 rails) immediately before it, `SCustom_ttpp` (`rail=DualL/M/R` -
a **dual-gauge** segment, 3 rails) itself, then `SCustom_s3y7` (dual again)
after. This is exactly the shape of an ordinary narrow-branch-off-of-dual
transition rendered by the **plain track pipeline**
(`NarrowGaugeTrackBuilder.cs`/`NarrowGaugeSwitchGeometry.cs`), not by
`SectionedSpecialWorkBuilder.cs` at all.

**This means the bug surface is at least two separate systems, not one:**

- The measured special-work system (`SectionedSpecialWorkBuilder.cs` +
  `SpecialWorkTruthTableValidator.cs`), which is what this session's fixes
  and the new `GeometryContinuity` diagnostic cover. This system only
  applies to the 14 nodes complex enough to be classified as measured
  special work.
- The plain dual/narrow-gauge rendering pipeline
  (`NarrowGaugeTrackBuilder.cs`, `NarrowGaugeSwitchGeometry.cs`,
  `DualGaugeSharedRailRegistry.cs`) that handles every other dual-gauge
  segment and plain narrow switch on the map - which has **no** equivalent
  diagnostic tooling at all right now, and no confirmed root cause for
  `SCustom_ttpp`'s fragment.

The user originally reported plain narrow turnouts "mostly work," so this
second system isn't uniformly broken - but at least one plain dual-gauge
segment near a special-work zone is showing the same class of defect
(disconnected fragment) as the measured switches. Investigate whether this
is confined to segments adjacent to special-work zones (e.g. the
tie/rail-clipping logic interacting badly with neighboring geometry) or a
wider issue in the plain pipeline itself.

## What NOT to do

- Do not patch symptom-by-symptom from screenshot descriptions alone. Four
  distinct failure modes were reported; conflating them risks a fix for one
  masking or half-addressing another the way earlier "relax to a warning"
  patches did.
- Do not assume `SectionedSpecialWorkBuilder.cs` is the only place to look -
  `SCustom_ttpp` proves it isn't.
- Do not extend the `GeometryContinuity` diagnostic to cover the plain
  pipeline as a first step - understand the plain pipeline's actual
  rendering path first (per `00_PROJECT_CONSTITUTION.md`'s Process section:
  investigate before changing shared topology/geometry code). A diagnostic
  built on a wrong mental model of the pipeline would just produce more
  false positives, the way the first `GeometryContinuity` pass did before
  `FrogPieces`/`Guard` were understood.

## Suggested investigation approach

1. Reproduce/confirm `SCustom_ttpp`'s defect against a fresh session: is it
   truly outside every measured special-work plan, or does it belong to one
   of the 14 nodes under a segment ID this investigation hasn't connected
   yet (e.g. via `[SpecialWorkSegmentClip]`'s neighbor pattern matching a
   `dual.narrow-branch-joins-main` boundary)? Don't take this file's
   coordinate-based guess as settled fact.
2. Read `NarrowGaugeTrackBuilder.cs`'s and `NarrowGaugeSwitchGeometry.cs`'s
   actual rail-piece construction for plain dual-gauge segments and narrow
   switches, the same level of care already given to
   `SectionedSpecialWorkBuilder.cs` this session, and write findings to a
   new `reviews/*.md` file (not this one - this one is the investigation
   scope/kickoff, not the findings).
3. For each of the user's four symptoms, identify which system (measured
   special-work vs. plain pipeline) and which specific node(s)/screenshot(s)
   it maps to before proposing a fix. If a symptom can't be confidently
   mapped to a cause, say so in `STATUS.md` rather than guessing.
4. Prefer building a lightweight text diagnostic for whatever's found (in
   the same spirit as `GeometryContinuity`, but modeled on the plain
   pipeline's actual piece/rail shapes, not copy-pasted) over asking for
   more screenshots, once the investigation clarifies what "correct" looks
   like for that pipeline.
