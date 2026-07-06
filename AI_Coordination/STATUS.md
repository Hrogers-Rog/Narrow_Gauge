# Coordination Status

Last updated by: Claude - 2026-07-06

## Current phase: SCustom_ttpp root cause hypothesis found - two nodes double-claim overlapping-but-different-length ownership

Codex is blocked on usage limit until ~1:52 PM today. Claude continued
working autonomously (user authorized), running a third live session to
finish the `SCustom_ttpp` investigation started this session.

## Strong finding: overlapping double-claim on `SCustom_ttpp`

Added one more diagnostic line: `SpecialWorkHardwareRenderer.OwnershipCuts`
(`src/SpecialWorkHardwareRenderer.cs:250-266`) now logs
`[SpecialWorkOwnershipCutClaim]` with the claiming node's id, which rail,
its own claimed interval, and the resulting cut - right at the point where
`analysis.Definition.Id` is already in scope, so no restructuring was
needed, just a log call. Built, deployed, ran a full live session
(`loadSave 2026-06-25`), confirmed `objects=14, invalid=0` still holds, and
read the result directly from `Player.log`:

```
segment=SCustom_ttpp claimedBy=special-work:NCustom_fl15  claimingRail=narrow-through:left     cut=0.120-1.456
segment=SCustom_ttpp claimedBy=special-work:NCustom_fl15  claimingRail=narrow-through:right    cut=0.120-1.453
segment=SCustom_ttpp claimedBy=special-work:NCustom_fl15  claimingRail=standard-reversed:left   cut=0.120-1.457
segment=SCustom_ttpp claimedBy=special-work:NCustom_fl15  claimingRail=standard-reversed:right  cut=0.120-1.466
segment=SCustom_ttpp claimedBy=special-work:NCustom_ltci  claimingRail=narrow-normal:left        cut=0.120-2.024
segment=SCustom_ttpp claimedBy=special-work:NCustom_ltci  claimingRail=narrow-normal:right       cut=0.120-2.017
segment=SCustom_ttpp claimedBy=special-work:NCustom_ltci  claimingRail=narrow-reversed:left       cut=0.120-2.024
segment=SCustom_ttpp claimedBy=special-work:NCustom_ltci  claimingRail=narrow-reversed:right      cut=0.120-2.017
segment=SCustom_ttpp claimedBy=special-work:NCustom_ltci  claimingRail=standard-normal:left       cut=0.120-2.028
segment=SCustom_ttpp claimedBy=special-work:NCustom_ltci  claimingRail=standard-normal:right      cut=0.120-2.017
segment=SCustom_ttpp claimedBy=special-work:NCustom_ltci  claimingRail=standard-reversed:left     cut=0.120-2.028
segment=SCustom_ttpp claimedBy=special-work:NCustom_ltci  claimingRail=standard-reversed:right    cut=0.120-2.017
```

**Both `NCustom_fl15` and `NCustom_ltci` independently claim ownership of
the same physical rails on `SCustom_ttpp`, starting at the same point
(0.120) but with different extents** - `fl15` claims ~1.45-1.47m, `ltci`
claims ~2.02-2.03m. `MergeCutIntervals` unions overlapping intervals (confirmed
by reading it directly, not assumed), so the actual rendered cut takes the
larger extent (matches the earlier `[SpecialWorkSegmentClip]` result:
`0.120-2.028` etc. - `ltci`'s numbers, not `fl15`'s).

**This is a strong, concrete hypothesis for the actual defect**: if
`fl15`'s own rendered replacement geometry only extends to its own smaller
claimed interval (~1.45m) while the rail is cut all the way to `ltci`'s
larger claim (~2.03m), there's roughly a **0.5-0.6m gap** between where
`fl15`'s rendering ends and where the cut rail actually stops - exactly the
size and shape of the floating/disconnected fragment symptom reported since
the start of this session. Not yet proven (would need to check whether
`fl15`'s or `ltci`'s actual `FixedRailPieces`/wing/guard geometry extends
onto `SCustom_ttpp` far enough to cover the full cut), but this is now a
specific, testable claim instead of an open ambiguity.

## Cleanup confirmed (third live session this turn)

Same as both previous sessions this turn: no `Railroader.exe` process,
`Info.json` restored to `Enabled: false`, `steam_appid.txt` removed, no
leftover bridge/goto files.

## Standing rules (unchanged)

See previous entries - fresh-session verification, cleanup discipline, no
force-kill, camera settle delay, map-proximity check before assuming stale
camera.

## Next turn

Whoever's turn (Codex should be unblocked ~1:52 PM): determine whether
`fl15` or `ltci` (or neither) actually renders geometry covering the full
`0.120-2.028`-ish cut on `SCustom_ttpp`, or whether there's a genuine
~0.5-0.6m uncovered gap as hypothesized. This is the last step to either
confirm or refute `SCustom_ttpp` as a real bug (as opposed to a legitimate
but redundant double-claim that still renders correctly). If confirmed,
the fix is almost certainly in whichever node's plan is *supposed* to
render into this shared segment but doesn't extend far enough - likely
`fl15`, since a `dual.standard-branch-joins-main` preset's approach section
would plausibly need to reach further into the shared segment than
`ltci`'s `dual.both-diverge` claim does, but this needs verification, not
assumption.

Also still open: double-frog and blade-orientation visual questions (need
tighter camera framing in `NarrowGaugeTestBridge` or the user's own
screenshots), and the "too many rails"/mid-switch-transition symptoms from
the original investigation.

## Open questions / blockers

Whether `fl15`/`ltci`'s actual rendered pieces cover the full cut interval
on `SCustom_ttpp` (the last step to confirm/refute this as a real bug).
Double-frog, blade-orientation, and "too many rails" visual questions
remain open pending better camera framing or user screenshots. Codex
blocked until ~1:52 PM.
