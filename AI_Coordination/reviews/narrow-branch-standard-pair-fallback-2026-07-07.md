# `FindMatchingStandardRoute` fallback for fixed-through switches - 2026-07-07 (evening, fourth turn)

## REVERTED same turn - confirmed regression, not a fix

User live-tested this within the same turn. Result: **all five**
`dual.narrow-branch-joins-main` nodes dropped to `frogs=0, wings=0,
guards=0` (previously `frogs=1-3, wings=2-8, guards=2-7`), and 4 of 5
(`N178`, `NCustom_7n90`, `NCustom_vdlt`, `Nove`) flipped from `valid=True`
to `valid=False`. Only `NCustom_g832` stayed `valid=True` (but still
`frogs=0` - still broken).

Cause, confirmed via the same `[NarrowRailBuild]` diagnostic: `"standard-through"`'s
centerline is not actually near the narrow route for most of these nodes -
`centerlineGap` (distance between the two routes' `Centerline.Head` points)
was `55.971`, `105.000`, and `16.963` meters for four of the five, and only
`0.260` for `g832` (the one that stayed valid). Building the narrow rails
as a `Parallel()` offset of a centerline that's tens of meters away and
likely a very different length entirely broke downstream intersection-finding
- not just "still offset," but zero frog/wing/guard candidates found at all.

**Reverted** `FindMatchingStandardRoute` to its original behavior (suffix
match only, `null` on failure - i.e. back to the original `<none>`/wrong-centerline
behavior this doc set out to fix). The `<none>` case is still wrong (that's
the original bug), but this fallback made it categorically worse, so it's
not an acceptable interim fix either. Real fix still needed - see
"Next steps" at the bottom, added after the revert.

The rest of this document (root-cause analysis of *why* `standardPair`
comes back `<none>` for this preset) is still accurate and worth keeping -
only the proposed fix was wrong.

---


## Origin

Following up on the "every frog candidate off by a track-width" report,
added two targeted `[NarrowRailBuild]` diagnostic log lines (no behavior
change) in `BuildPhysicalRails`/`BuildNarrowRailsFromStandardCenterline`
(`SectionedSpecialWorkBuilder.cs` ~300-330) rather than keep guessing after
several static-reading dead ends (K-frog guard shift, frog-piece math,
`LineCurve.Parallel`'s `Hand` semantics, intersection geometry - all
checked out and were ruled out, see `STATUS.md`). User reloaded and pasted
back a fresh `Player.log`.

## Evidence

Grepping `[NarrowRailBuild]` from the fresh log:

```
route=narrow-normal   sharedSide=Left  standardPair=<none>   (N178)
route=narrow-reversed sharedSide=Left  standardPair=<none>   (N178)
route=narrow-normal   sharedSide=Left  standardPair=<none>   (NCustom_7n90)
route=narrow-reversed sharedSide=Left  standardPair=<none>   (NCustom_7n90)
route=narrow-normal   sharedSide=Right standardPair=<none>   (NCustom_g832)
route=narrow-reversed sharedSide=Right standardPair=<none>   (NCustom_g832)
route=narrow-normal   sharedSide=Left  standardPair=<none>   (NCustom_vdlt)
route=narrow-reversed sharedSide=Left  standardPair=<none>   (NCustom_vdlt)
route=narrow-normal   sharedSide=Left  standardPair=<none>   (Nove)
route=narrow-reversed sharedSide=Left  standardPair=<none>   (Nove)
```

(node attribution confirmed by matching against the immediately-following
`[DivergingFixedRail] node=special-work:<id>` log lines for each block.)

**Every single node in the `dual.narrow-branch-joins-main` group -
`N178`, `NCustom_7n90`, `NCustom_g832`, `NCustom_vdlt`, `Nove` - shows
`standardPair=<none>`.** Compare against the `dual.both-diverge` group
(`NCustom_fc97`, `l4a4`, `ltci`, `p997`, `u6n0`, `NDeHartPassing_wqbb`,
`Npv2`), logged later in the same run, which all show real matches:
`standardPair=standard-normal`/`standard-reversed`, `centerlineGap=0.087`
to `0.260` (i.e. genuinely close, correct route).

## Root cause

`FindMatchingStandardRoute` (`SectionedSpecialWorkBuilder.cs:364-376`)
guesses the paired standard route's id by taking the narrow route's own
suffix (`"-normal"`, `"-reversed"`, etc.) and prefixing it with
`"standard"` instead of `"narrow"`. This works for `dual.both-diverge`,
where the standard side independently switches too, so
`standard-normal`/`standard-reversed` routes genuinely exist.

`dual.narrow-branch-joins-main` (and `dual.split-standard-narrow`) don't
work that way: only the narrow side switches. The standard side is a
single, fixed, unswitched route, and it is literally named
`"standard-through"` in both
`TryBuildNarrowBranchTransition` and `TryBuildDualSplitTransition`
(`SpecialWorkRuntimeDiscovery.cs:158,237`). So for every node in this
preset, `FindMatchingStandardRoute("narrow-normal", ...)` looks for
`"standard-normal"`, which does not exist, returns `null`, and
`BuildPhysicalRails` (`SectionedSpecialWorkBuilder.cs:300-317`) silently
falls through to the generic non-shared-centerline branch - building the
narrow route's own gauge-width rails around its own centerline, with no
reference to where the real shared/standard rail actually sits. Since the
narrow route's centerline isn't positioned on the true shared rail (it's
built as its own independent thing), the resulting narrow rail pair sits
systematically offset from the correct dual-gauge geometry by roughly the
difference between the two centerlines - matching "off by a track-width"
exactly, and matching it for every switch in this preset, not one-off.

This also explains why this session's earlier `ResolveDivergingFixedStockRail`
hand-bug fix (see `reviews/diverging-fixed-stock-rail-hand-bug-2026-07-07.md`)
genuinely helped `NCustom_7n90` (now `valid=True`, `blades=2`, confirmed in
this same log) without contradicting this finding: `valid=True` only checks
role/coverage bookkeeping on whichever rails got built, not whether those
rails are in the geometrically correct position. The plan can pass
validation while still being visually wrong - reinforcing why this
session's standing rule requires a screenshot, not just a clean log, before
calling something fixed.

`NCustom_g832` is still `valid=False` in this same log, but now for an
unrelated, narrower reason confirmed directly: it has only **one**
stock-rail candidate (`narrow-normal:right`, `role=Unknown`) rather than
two, so `preferredSide` selection is moot - the rail itself never got a
renderable role assigned. That's a separate bug from both this one and the
hand-preference one, not yet investigated.

## Fix attempted, then reverted (see top of doc)

`FindMatchingStandardRoute` briefly fell back to a route literally named
`"standard-through"` when the suffix-based guess found nothing. Confirmed
live to be a regression (all five nodes dropped to `frogs=0/wings=0/guards=0`,
four flipped `valid=True`→`False`) because `"standard-through"`'s centerline
is not positioned near the narrow route for most of these nodes
(`centerlineGap` up to 105m via `[NarrowRailBuild]`). Reverted to original
behavior. See the "REVERTED" section at the top for full detail.

## Next steps (not yet attempted)

The root-cause analysis above is still believed accurate: `standardPair=<none>`
for this whole preset is real and is why the narrow rails are built from
the wrong centerline. What's still unknown is *why* `"standard-through"`'s
centerline sits so far from the narrow route for 4 of 5 nodes but not
`NCustom_g832` - that's the question a real fix needs answered first,
rather than assuming any route named `"standard-through"` is automatically
the right pairing:

- Log (don't yet fix) the actual `Length` of `standardPair.Centerline` vs.
  `narrowPath.Centerline` for both the `<none>` case and a successful
  `dual.both-diverge` match, to see whether `"standard-through"` is simply
  much shorter/longer than the narrow route for these nodes specifically -
  if so, `BuildNarrowRailsFromStandardCenterline`'s `Parallel()`-of-the-whole-curve
  approach may need the narrow route's own length, not the standard route's,
  regardless of which standard route is matched.
- Check whether `centerlineGap` (Head-to-Head distance) is even the right
  metric - if `"standard-through"`'s curve runs in the opposite direction
  or starts from a different physical end than the narrow routes, comparing
  `Head` to `Head` could show a large distance even for a *correctly*
  matched pair, and the real problem might be curve orientation/direction
  rather than route-matching at all. Compare `Head`-to-`Head` **and**
  `Tail`-to-`Head`/`Head`-to-`Tail` before concluding a route is "wrong."
- `NCustom_g832`'s separate `role=Unknown` single-candidate issue (unrelated
  to this thread) is still open too.
- The fallback path's still-reverted one-blade filter (the `NCustom_7n90`
  overlapping-rail-tangle cosmetic issue) is still open, unaffected by this
  thread either way.
