# Coordination Status

Last updated by: Claude - 2026-07-06

## Current phase: found and fixed a real orientation-dependent bug affecting many both-diverge switches

User pushed back hard on "doesn't seem like we've fixed anything" - correctly,
since nothing had been visually confirmed yet. They then provided extremely
detailed close-up screenshots with exact node/segment names: `S4u5` (blades
on the wrong rails - left-through/right-diverge instead of left-diverge/
right-through), and a widespread pattern across `e6i0`, `tliv`, `s3y7`,
`ttpp`, `snvo`, `6wx3` and others - "the frog or guard is shifted about the
width of a rail head to the left or right... there all slightly different."
Also confirmed `Nove`'s blade is still broken (not fixed by anything landed
so far). One screenshot (`snvo`, extreme top-down close-up) unambiguously
shows a short rail piece laterally offset and disconnected from the
continuous rail next to it - a real, visually confirmed defect, not a log
artifact.

## Found and fixed: orientation-dependent hardcoded duplicate rail

"Shifted sideways, inconsistently left or right across different switches"
is the signature of code that assumes a fixed physical role for a fixed
`Left`/`Right` label, when that mapping actually depends on each switch's
own shared-rail orientation. Traced this to
`SuppressDualBothDivergeFrogDuplicate` (`src/SectionedSpecialWorkBuilder.cs`,
~line 1947): it always looked up `"narrow-normal:left"` as *the* duplicate
rail to cut/suppress for `dual.both-diverge` presets.

Checked `BuildNarrowRailsFromStandardCenterline` (same file, ~line 377) to
confirm whether that assumption is safe - it isn't:

- When a switch's `sharedSide` is `Right`, `narrow-normal:right` carries the
  curve shared with standard gauge (the true duplicate), and
  `narrow-normal:left` carries the physically distinct narrow-only curve.
- When `sharedSide` is `Left`, it's the reverse - `narrow-normal:left` is
  the shared/duplicate curve.

So the hardcoded `"narrow-normal:left"` lookup was only correct for
switches authored/inferred as `sharedSide == Left`. For the opposite
orientation, it suppressed the wrong (already-distinct) rail while leaving
the *actual* duplicate unsuppressed - exactly matching "sometimes left,
sometimes right, all slightly different," since different switches on this
map have different shared-rail orientations.

**Fix**: call the existing `DetectSharedSide(definition)` helper (already
used by `BuildPhysicalRails`/`BuildNarrowRailsFromStandardCenterline` for
the same purpose) and pick `narrow-normal:right` when `sharedSide == Right`,
`narrow-normal:left` otherwise. Built and deployed:
`dotnet build NarrowGaugeMod.csproj -p:RailroaderDir="C:\Steam\steamapps\common\Railroader" -p:EnableModDeploy=true` -
0 warnings/0 errors.

**Checked for similar bugs and found none (this specific string pattern is
the only hardcoded occurrence)**: grepped for other hardcoded
`"narrow-normal:left"`/`"narrow-reversed:left"`/etc. literals across
`SectionedSpecialWorkBuilder.cs` - only the one fixed here. Also reviewed
`ChooseSharedOwner` (the general shared-rail tie-break function used
elsewhere) - it's orientation-agnostic (family > stock-rail > movable-rail >
diverging-side > alphabetical priority chain, no hardcoded side), so it's
not a second instance of the same bug class.

**Not yet live-verified.** This fix targets one specific function
(`dual.both-diverge`'s vee-frog shared-duplicate suppression) - it likely
explains part of what the user is seeing, but probably not all of it
(`S4u5`'s wrong-side blade assignment and `e6i0`'s "inside out" frog
rendering-plus-attempted-blades look like separate, not-yet-investigated
symptoms). Do not claim this closes out the user's report.

## Standing rule reinforced this turn

Do not report a fix as done based on log validation or an inconclusive wide
screenshot. The user was right to push back - "passes `valid=True`" and
"actually renders correctly" are different claims, and this session had
been conflating them. Only a close-up screenshot (or the user's own
confirmation) that specifically shows the previously-broken geometry now
looking correct counts as "fixed."

## Next turn

Live-verify this fix: deploy (already done), launch, `loadSave`, goto one of
the affected `dual.both-diverge` switches (`NCustom_l4a4`, `NCustom_ltci`,
`NCustom_p997`, `NCustom_u6n0`, `NDeHartPassing_wqbb`, `Npv2`, `NCustom_fc97`),
get a close screenshot (tighter framing than the wide elevated shots so far -
worth improving `NarrowGaugeTestBridge`'s camera distance/angle for this),
and look specifically for whether the disconnected/shifted fragment near
the vee frog is gone. Separately investigate `S4u5`'s wrong-side blade
assignment and `e6i0`'s inside-out frog - likely different root causes than
the one just fixed, given they're different symptoms (blade-rail pairing
vs. frog orientation) at different presets/nodes.

## Open questions / blockers

Whether the `SuppressDualBothDivergeFrogDuplicate` fix actually resolves
the visual symptom (not yet checked against a close-up screenshot).
`S4u5`'s blade-rail swap and `e6i0`'s inside-out frog remain unexplained.
Codex is still blocked as of ~12:08 PM EDT (block was until ~1:52 PM) -
recheck before assuming it's available.
