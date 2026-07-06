# Plain and Measured Visual Defect Findings - 2026-07-06

Investigator: Codex

Scope source: `AI_Coordination/reviews/broad-visual-defect-investigation-2026-07-06.md`

No code changes were made in this turn. The goal was to separate the four
reported visual symptoms by rendering system before another patch.

## Summary

`SCustom_ttpp` is not a measured special-work node and is not the ID of a
missing 15th special-work plan. It is an ordinary authored `DualGauge_R`
segment between two measured special-work nodes, `NCustom_fl15` and
`NCustom_ltci`. Its running rails are built by the plain dual-gauge segment
renderer, then clipped by special-work/gauge-separation cut logic.

That means the latest screenshots are crossing a boundary between two
systems:

- Measured special work: `SectionedSpecialWorkBuilder.cs` plus
  `SpecialWorkHardwareRenderer.cs`, covering the 14 generated plan exports.
- Plain/generated dual/narrow pipeline: `NarrowGaugeTrackBuilder.cs` plus
  `NarrowGaugeSwitchGeometry.cs`, covering ordinary dual-gauge segments,
  plain narrow switches, generated transition switches, and mixed
  dual/narrow switch visuals.

The reported symptoms should not be fixed with one broad patch. Current
evidence maps them to different parts of those two systems.

## Evidence for `SCustom_ttpp`

Generated plan exports:

- The live plan directory contains exactly 14 `special-work_*.txt` files:
  `N178`, `NCustom_7n90`, `NCustom_fc97`, `NCustom_fl15`, `NCustom_g832`,
  `NCustom_l4a4`, `NCustom_ltci`, `NCustom_p997`, `NCustom_u6n0`,
  `NCustom_vdlt`, `NDeHartPassing_33d6`, `NDeHartPassing_wqbb`, `Nove`,
  and `Npv2`.
- Searching those plan exports for `SCustom_ttpp`/`ttpp` returned no matches.
  Absence from the text export alone is not final proof, because the exporter
  does not print every source segment ID. The authored graph and runtime log
  are the stronger evidence.

Authored graph:

- `C:\Steam\steamapps\common\Railroader\Mods\EFA Track Pack\game-graph.json`
  has `SCustom_ttpp` at lines 14632-14640 as:
  - `startId`: `NCustom_fl15`
  - `endId`: `NCustom_ltci`
  - `gauge`: `DualGauge_R`
- The adjacent nodes are authored switch nodes:
  - `NCustom_fl15` at lines 3796-3799, with a measured export using preset
    `dual.standard-branch-joins-main`.
  - `NCustom_ltci` at lines 3822-3825, with a measured export using preset
    `dual.both-diverge`.

Runtime log:

- `Player.log` has no `Rendering measured special-work 'special-work:SCustom_ttpp'`
  line and no `special-work_SCustom_ttpp.txt` export.
- The only `SCustom_ttpp` runtime build evidence is:
  - hidden generated descriptor suppression:
    `segment-fuse-ng:s:SCustom_ttpp-(1797.0, 588.1, 1229.9)-(1797.7, 588.4, 1252.3)`
  - segment/tie cut logs:
    - `segment=SCustom_ttpp rail=DualL ... cuts=0.120-2.028`
    - `segment=SCustom_ttpp rail=DualM ... cuts=0.120-2.024`
    - `segment=SCustom_ttpp rail=DualR ... cuts=0.120-2.017`
    - `segment=SCustom_ttpp cuts=0.000-2.143`
- The same log repeatedly warns that `NCustom_fl15` generated narrow endpoints
  disagree by up to `0.260m`, and `SCustom_ttpp` starts at `NCustom_fl15`.

Conclusion: `SCustom_ttpp` is plain dual-gauge segment rendering adjacent to
two measured nodes. It is not a measured-node mesh plan, but it is still
affected by measured-node ownership cuts and possibly gauge-separation cuts.

## Plain Pipeline Map

### Dual-gauge segments

`NarrowGaugeTrackBuilder.BuildDualGaugeSegment` is the entry point for normal
dual-gauge segments (`src/NarrowGaugeTrackBuilder.cs:539`). It only switches
to the special shared-rail transition path when
`DualGaugeSharedRailRegistry.IsSharedRailTransition(segment.Segment)` is true
(`src/NarrowGaugeTrackBuilder.cs:543`).

Normal dual-gauge segments call `CreateDualGaugeTrackObject`
(`src/NarrowGaugeTrackBuilder.cs:553`). That method:

- builds standard left/right rails from `Gauge.Standard`
  (`src/NarrowGaugeTrackBuilder.cs:1004`);
- builds third-rail candidates from `ThirdRailGauge`
  (`src/NarrowGaugeTrackBuilder.cs:1005`);
- chooses the middle rail with
  `sharesRightRail ? thirdCurves.left : thirdCurves.right`
  (`src/NarrowGaugeTrackBuilder.cs:1006`);
- renders `DualL`, `DualM`, and `DualR` through
  `CreateRailMeshesWithFrogCuts`
  (`src/NarrowGaugeTrackBuilder.cs:1008-1010`).

`CreateRailMeshesWithFrogCuts` merges three possible cut sources under one
log label (`src/NarrowGaugeTrackBuilder.cs:2350-2380`):

- `SpecialWorkHardwareRenderer.OwnershipCuts(worldRail, sourceSegment)`
- `GaugeSeparationFrogCuts(worldRail, sourceSegment)`
- `SharedRailFlipMiddleCuts(worldRail, sourceSegment)` for `DualM` only

Therefore `[SpecialWorkSegmentClip] segment=SCustom_ttpp ...` does not prove
that the cut came only from measured special-work ownership. The current log
does not distinguish ownership cuts from gauge-separation frog cuts.

The older shared-rail flip helper is effectively disabled:
`TryResolveSharedRailFlip` always returns `false`
(`src/NarrowGaugeTrackBuilder.cs:2303-2309`). I found no current
`SharedRailTransition` or `SharedRailFlip` evidence in `Player.log` for the
reported `SCustom_ttpp` area.

### Gauge-separation layout

Gauge-separation cuts are still live. `TryResolveGaugeSeparationRailLayout`
scans gauge-separation source nodes and returns a layout when the source
segment is the candidate dual, standard, or narrow segment
(`src/NarrowGaugeTrackBuilder.cs:4027-4050`). The layout contains one dual,
one standard, and one narrow segment and chooses the dual middle rail using
`DualGaugeSharedRailRegistry.SharesRightRail`
(`src/NarrowGaugeTrackBuilder.cs:4072-4130`).

If `SCustom_ttpp` is near one of these source nodes, its
`SpecialWorkSegmentClip` intervals could be gauge-separation cuts rather than
measured-node ownership cuts. The log currently cannot tell which.

### Plain/generated switches

`BuildDualGaugeSwitch` chooses a bespoke dual-to-narrow split renderer only
for the orientation `aDual && bNarrowOnly`
(`src/NarrowGaugeTrackBuilder.cs:2668-2683`). The reverse orientation,
`aNarrowOnly && bDual`, logs a warning and uses the full dual turnout visual
for now (`src/NarrowGaugeTrackBuilder.cs:2684-2699`). That asymmetry is a
real candidate for "too many rails" or wrong-side point hardware on plain
mixed switches, but it needs a node-specific screenshot before patching.

Inside `CreateDualGaugeNarrowSplitSwitchRailObjects`, the bespoke split path
hardcodes the dual middle rail as `aThirdRails.right` when resolving the
narrow branch rails (`src/NarrowGaugeTrackBuilder.cs:2854-2867`). This does
not consult `DualGaugeSharedRailRegistry.SharesRightRail`, unlike the normal
dual-gauge segment and gauge-separation layout code. This is the strongest
plain-pipeline code suspect for blades on the outside/wrong side, especially
on `DualGauge_L` vs `DualGauge_R` mixed-switch orientations. It is still a
hypothesis, not a patch target, until tied to a specific reported node.

`NarrowGaugeSwitchGeometry.Calculate` decides a plain switch's handedness by
testing rail intersections in this order:

- `fullRailsA.left` with `fullRailsB.right`
- then `fullRailsA.right` with `fullRailsB.left`

See `src/NarrowGaugeSwitchGeometry.cs:33-49` and stock/point assignment at
`src/NarrowGaugeSwitchGeometry.cs:74-101`. `AlignSwitchCurves` only orients
the two curves from their shared endpoint (`src/NarrowGaugeSwitchGeometry.cs:240-275`),
and `Intersects` returns the first segment intersection found while ignoring
the `frogDepth` argument (`src/NarrowGaugeSwitchGeometry.cs:277-299`). This
plain-switch algorithm has no diagnostic export comparable to the measured
special-work plans.

Generated transition switches suppress duplicate visible dual rails in
`CreateTransitionSwitchRailObjects`. The duplicate detector samples the
candidate curve against visible dual rails at `DuplicateRailSampleSpacing`
`0.1m` and `DuplicateRailTolerance` `0.055m`
(`src/NarrowGaugeTrackBuilder.cs:42-43`, `src/NarrowGaugeTrackBuilder.cs:4210-4235`).
This is another candidate for "too many rails" if it misses overlap.

## Symptom Map

### 1. Double frogs

Most likely system: measured special work, not the plain `SCustom_ttpp`
segment itself.

Evidence:

- The measured renderer creates frog assemblies from `plan.Frogs` in
  `SpecialWorkHardwareRenderer.AddAdditionalHardware`
  (`src/SpecialWorkHardwareRenderer.cs:348-381`, `src/SpecialWorkHardwareRenderer.cs:508-535`).
- Current runtime logs show multiple measured nodes rendering three frogs:
  - `NCustom_fl15`: `fixed=15, frogs=3, wings=8, guards=7, blades=2`
  - `NCustom_ltci`: `fixed=16, frogs=3, wings=8, guards=9, blades=2`
  - `NCustom_fc97`: `fixed=18, frogs=3, wings=8, guards=9, blades=3`
- At these nodes, `SpecialWorkObjects` reports `vanillaRailObjects=0`, so an
  extra frog seen on a valid measured node is probably inside the measured
  plan/hardware assembly, not unsuppressed legacy switch rails.

Relevant code paths:

- Duplicate candidate collapse requires same frog kind, same unordered rail
  pair IDs, and position distance `<= CorridorTolerance * 2f`
  (`src/SectionedSpecialWorkBuilder.cs:1783-1817`). Close physical frogs on
  different rail pairs will not collapse here.
- `FindCloseVeeFrogPairs` only pairs vee frogs within `0.18m` and only when
  `TryResolveCompoundVeeRails` succeeds
  (`src/SpecialWorkHardwareRenderer.cs:1501-1540`).
- `CreateCompoundVeeFrogAssembly` renders the standard frog and skips the
  mixed overlay only when the two frog nose positions are within
  `PhysicalOverlapTolerance` (`0.06m`)
  (`src/SpecialWorkHardwareRenderer.cs:16`, `src/SpecialWorkHardwareRenderer.cs:1587-1595`).

Status: mapped to measured frog generation/rendering. Needs the specific
double-frog screenshot node before changing collapse tolerances or compound
frog rendering.

### 2. Blades on the outside / wrong side of the rail

Possible systems:

- measured special work, if the screenshot is at one of the 14 measured
  nodes;
- plain mixed dual/narrow switch rendering, if the screenshot is at a
  generated/plain switch or adjacent segment label.

Measured code path:

- `BuildBladeSpecs` uses truth-table blades when present
  (`src/SectionedSpecialWorkBuilder.cs:671-699`).
- Blade rendering then goes through `CreatePointBlade` and
  `CalculateBladeOpenRotation` in `SpecialWorkHardwareRenderer`
  (`src/SpecialWorkHardwareRenderer.cs:482-503`).

Plain code path:

- `CreateDualGaugeNarrowSplitSwitchRailObjects` hardcodes
  `aThirdRails.right` as the dual middle rail for branch resolution
  (`src/NarrowGaugeTrackBuilder.cs:2854-2867`).
- The generic dual-gauge code does not do this; it chooses the middle rail
  from `SharesRightRail`. That difference is a credible root-cause candidate
  for wrong-side blades on one shared-rail side.

Status: not yet mapped to a node. The plain-pipeline hardcode is narrow and
suspicious, but patching it without a labeled failing switch risks changing a
case that already works.

### 3. Too many rails

Possible systems:

- measured special work: extra fixed/shared pieces within the generated plan;
- plain/generated transition switch: duplicate visible dual rails not cut
  away;
- plain mixed switch fallback: reverse `aNarrowOnly && bDual` orientation
  intentionally uses the full dual turnout visual.

Evidence and paths:

- For measured nodes such as `NCustom_fl15`, `NCustom_ltci`, `NCustom_fc97`,
  `Nove`, and `NCustom_7n90`, current `SpecialWorkObjects` logs show
  `vanillaRailObjects=0`. That reduces the likelihood of old vanilla rails
  being the "too many rails" source on those nodes.
- The live `NCustom_fl15` and `NCustom_ltci` plan exports still report
  isolated fixed pieces near the same coordinate range as `SCustom_ttpp`:
  - `NCustom_fl15`: `v2-fixed:5`, `v2-fixed:7`, `v2-fixed:12`
  - `NCustom_ltci`: `v2-fixed:2`, `v2-fixed:11`, `v2-fixed:13`
  These are plausible measured-plan extra/fragments near the plain segment
  boundary.
- Generated transition switch duplicate suppression depends on sampling
  visible dual rails (`src/NarrowGaugeTrackBuilder.cs:4210-4235`). A miss
  there can leave extra narrow rail pieces.
- Mixed switch reverse orientation falls back to full dual visual
  (`src/NarrowGaugeTrackBuilder.cs:2684-2699`).

Status: mapped to multiple plausible systems. Needs the visible label/node
for each "too many rails" screenshot before choosing a fix.

### 4. Possible transition in the middle of a switch

Current evidence does not confirm an actual shared-rail transition rendered
inside a switch.

Evidence:

- `TryResolveSharedRailFlip` is disabled and returns `false`
  (`src/NarrowGaugeTrackBuilder.cs:2303-2309`).
- Current `Player.log` has no `SharedRailTransition`, `SharedRailFlip`, or
  `SharedRailTransitionCut` entries near the reported `SCustom_ttpp` area.
- `SCustom_ttpp` is a short dual-gauge segment between measured nodes, and
  its first roughly two meters of all three rails are cut. That boundary can
  visually read like a transition if a measured fragment or clipped plain
  rail appears in the middle of a larger switch scene.

Status: unresolved. Treat as a visual interpretation to verify, not a known
transition-system defect.

## Diagnostic Cautions

The current live plan exports still include `ISOLATED: v2-guard:*` lines,
but the checked-out exporter source now suppresses guard-only isolated
verdicts because guard rails are intended to be free-standing
(`src/SpecialWorkPlanExporter.cs:556-562`). Therefore:

- do not treat guard isolation lines in the current live exports as confirmed
  geometry defects;
- fixed-piece isolation lines remain relevant candidates;
- before relying on the diagnostic export for a new patch, regenerate exports
  from a known deployed build and confirm the guard-only false positives are
  gone.

Also, `[SpecialWorkSegmentClip]` currently conflates cut sources. For
`SCustom_ttpp`, the next useful diagnostic would split the logged intervals
by source:

- measured ownership cuts;
- gauge-separation frog cuts;
- shared-rail flip middle cuts, if any.

That diagnostic would answer whether the `SCustom_ttpp` floating/clipped
fragment is primarily a measured ownership-boundary issue or a
gauge-separation issue.

## Recommended Next Work

Do not patch all four symptoms together.

Recommended next steps:

1. Add a temporary cut-source diagnostic for `CreateRailMeshesWithFrogCuts`
   and rerun around `SCustom_ttpp`. This is the smallest way to identify
   which subsystem owns the segment cut.
2. Ask for or use one focused debug-labeled screenshot for each remaining
   symptom, especially wrong-side blades and double frogs. The relevant
   question is the visible node/segment label, not another broad report.
3. If wrong-side blades map to a plain mixed dual/narrow switch, test the
   `aThirdRails.right` hardcode against `DualGauge_L` and `DualGauge_R`
   cases before patching.
4. If double frogs map to a measured `dual.both-diverge` or
   `dual.standard-branch-joins-main` node, inspect the exact frog candidates
   in that plan export before changing duplicate collapse or compound vee
   rendering tolerances.
5. Regenerate plan exports from the currently checked-out build before using
   `GeometryContinuity` as hard evidence, because the live guard isolation
   output appears inconsistent with the checked-out exporter source.

