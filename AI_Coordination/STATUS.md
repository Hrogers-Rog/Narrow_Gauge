# Coordination Status

Last updated by: Claude - 2026-07-06

## Current phase: two more concrete leads found, handed to Codex (now unblocked) for deep investigation

User sent a very close-up screenshot of `dkzn` showing multiple distinct
disconnected/kinked rail fragments (not just one clean gap) - a messier
defect than anything investigated so far. Traced ownership: `dkzn` is
claimed solely by `NCustom_p997` (`dual.both-diverge`), no double-claim
conflict this time (unlike `SCustom_ttpp`). Read
`CreateCompoundVeeFrogAssembly`/`CreateVeeWingRail`/`TryResolveCompoundVeeRails`
(`src/SpecialWorkHardwareRenderer.cs` ~1380-1700) - these use topological
matching (shared rail *object identity*, not hardcoded Left/Right strings)
so they're not the same bug class as the `SuppressDualBothDivergeFrogDuplicate`
fix from last turn. Grepped the whole file for other hardcoded
`"narrow-*"`/`"standard-*"` rail-id literals - found only one, and it's a
debug-log filter condition (`LogDualBothDivergeNarrowClosureFrame`), not
something that affects rendered geometry. **This means `dkzn`'s defect is a
different, not-yet-found bug** - likely in the actual heel-point/wing-rail
geometry math (`HeelPoint`, `DirectionTowardBlades`, `SideTowardDirection`,
`SliceSignedSpan`), not a simple orientation-label mixup. Needs careful
line-by-line geometric reasoning, not a quick grep.

Also checked `S4u5` (user: blades on the wrong rails - should be
left-diverge/right-through, are left-through/right-diverge). Traced
ownership: claimed by `N178` (`dual.narrow-branch-joins-main`, same preset
as `Nove`, which the user also confirmed still has a backwards-running
blade). Compared their `[Blades]` plan data: `Nove`'s blade pairing
(stock=narrow-normal:left/movable=narrow-reversed:left,
stock=narrow-reversed:right/movable=narrow-normal:right) is a mirror image
of `N178`'s (stock=narrow-normal:right/movable=narrow-reversed:right,
stock=narrow-reversed:left/movable=narrow-normal:left). This could be a
*legitimate* opposite-hand pairing if the two switches are genuinely built
with opposite orientations (recall the log's `[BladeSpecs] Using truth
table 'DualGauge_NarrowBranch_Left'` vs `'...Right'` selection) - or the
truth-table *selection* itself could be picking the wrong hand for one or
both of these switches based on their actual measured geometry. Not yet
determined which.

Codex's usage-limit block has cleared (confirmed with a no-op check). Given
both remaining leads need careful, uninterrupted geometric tracing rather
than another grep-and-fix pass, handing both to Codex as a real
investigation - see the turn prompt for the exact scope.

## Confirmed landed this session (for reference, don't re-litigate)

- Item 1 (split-standard-narrow zero blades, Codex) - reviewed, agreed.
- Item 2 (both-diverge SharedDuplicate suppression, Claude) - reviewed, agreed.
- Narrow-branch rendering gaps (frog rehoming, stock-rail selection, blade
  endpoint reservation, Codex two turns) - reviewed, agreed.
- Plain-pipeline `aThirdRails.right` hardcode (Claude) - reviewed, agreed,
  but confirmed this doesn't touch `Nove` (measured system, different code
  path).
- `NarrowGaugeTestBridge` camera-goto tool + `SpecialWorkOwnershipCutClaim`/
  `SpecialWorkSegmentClipSource` diagnostics (Claude) - working, proven
  across multiple live sessions.
- `SCustom_ttpp` double-claim (`fl15`+`ltci` both claim overlapping-but-
  different-length ownership) - found, not yet fixed (need to confirm which
  node's render should actually cover the gap first).
- `SuppressDualBothDivergeFrogDuplicate` orientation-dependent hardcode
  (Claude) - fixed, built, deployed. **Not close-up visually confirmed
  yet** - do not claim this is proven.

**Standing rule reinforced hard this session**: log validation
(`valid=True`) and a non-close-up screenshot are not proof of a visual fix.
Only a close-up screenshot specifically showing previously-broken geometry
now looking correct, or the user's own confirmation, counts.

## Next turn

Codex - investigate (do not rush to patch) two things:

1. **`dkzn`/`NCustom_p997`'s compound vee-frog mess** - read
   `CreateCompoundVeeFrogAssembly`, `CreateVeeFrogAssembly`,
   `CreateVeeWingRail`, `HeelPoint`, `DirectionTowardBlades`,
   `SideTowardDirection`, `SliceSignedSpan` in
   `src/SpecialWorkHardwareRenderer.cs` line by line against `p997`'s actual
   plan data (frogs/wings/blades) and figure out why multiple distinct
   disconnected/kinked fragments render near its crossing, not just find
   another hardcoded string (there isn't one - already checked).
2. **`S4u5`/`N178` vs `Nove` blade pairing** - determine whether the
   mirrored stock/movable pairing between these two `dual.narrow-branch-
   joins-main` switches is legitimate (genuinely opposite hand) or whether
   the truth-table selection (`SpecialWorkTruthTableCatalog.TryGet`,
   `DualGauge_NarrowBranch_Left`/`_Right`) is picking the wrong hand for one
   of them based on its actual measured geometry. This same preset covers
   `Nove`, which the user has now twice confirmed still shows a backwards
   blade - if this is the root cause, fixing it likely fixes `Nove` too.

Use the live-game pipeline (`NarrowGaugeTestBridge` + `FUSE.TestBridge`,
recipe documented in earlier LOG entries) to get close-up screenshots for
verification once a fix is confident - not before.

## Open questions / blockers

Both investigations above are open. Do not assume the both-diverge
duplicate-rail fix from last turn is proven without a close-up check.
