# Coordination Status

Last updated by: Claude - 2026-07-06

## Current phase: SCustom_ttpp cut-source ambiguity resolved; camera framing still the open item for visual questions

Codex is blocked on usage limit until 1:52 PM today. Claude continued
working autonomously (user authorized this), driving the live-game
pipeline directly a second time this session.

## Resolved: `SCustom_ttpp`'s cut source

Added source-tagged diagnostic logging to `CreateRailMeshesWithFrogCuts`
(`src/NarrowGaugeTrackBuilder.cs`) - it previously merged three cut sources
(measured-plan `Ownership`, `GaugeSeparation` frog synthesis, and the
already-dead `SharedRailFlip`) into one opaque `[SpecialWorkSegmentClip]`
log line, which is exactly what left `SCustom_ttpp`'s cut source ambiguous
in Codex's investigation. Added a new `[SpecialWorkSegmentClipSource]` log
line per non-empty source, logged before the existing merged summary.

Built, deployed, and ran a full live session (same recipe as before) to
capture the new diagnostic. Result, read directly from `Player.log`:

```
[SpecialWorkSegmentClipSource] segment=SCustom_ttpp rail=DualL source=Ownership cuts=0.120-1.457,0.120-2.028,0.120-2.028
[SpecialWorkSegmentClipSource] segment=SCustom_ttpp rail=DualM source=Ownership cuts=0.120-1.456,0.120-2.024,0.120-2.024
[SpecialWorkSegmentClipSource] segment=SCustom_ttpp rail=DualR source=Ownership cuts=0.120-1.466,0.120-1.453,0.120-2.017,0.120-2.017,0.120-2.017,0.120-2.017
```

**All of `SCustom_ttpp`'s cuts on all three rails (`DualL`/`DualM`/`DualR`)
come exclusively from `source=Ownership`.** Zero `GaugeSeparation` or
`SharedRailFlip` entries. This fully resolves the ambiguity: the floating
fragment isn't from gauge-separation frog synthesis or the dead
shared-rail-flip path - it's a measured special-work node's `WorkInterval`
(per `SpecialWorkHardwareRenderer.OwnershipCuts`, `src/SpecialWorkHardwareRenderer.cs:221-268`)
claiming the first ~2m of this plain segment as its own rendering
territory (only happens when `analysis.MeshPlan.IsValid == true` - this
mechanism only fires for currently-valid measured plans).

**Not yet resolved, and the natural next step**: which specific neighboring
measured node (`NCustom_fl15` or `NCustom_ltci` - both touch this segment
per Codex's earlier authored-graph read) actually claims this ~2m
ownership interval, and whether that node's own rendered pieces
(`FixedRailPieces` etc.) actually cover the exact overlap - if they do,
this is a correct plain-to-measured handoff, not a bug; if there's a gap
between what's cut and what's rendered to replace it, that's the real
defect. This requires cross-referencing `SCustom_ttpp`'s segment-relative
cut distances against the claiming node's own route-relative
`WorkInterval`/`FixedRailPieces` distances - a coordinate-system
translation, not a quick read. Left for the next turn rather than rushed.

## Cleanup confirmed (second live session this turn, same as the first)

`tasklist` shows no `Railroader.exe` process; `FUSE.TestBridge/Info.json`
restored to `"Enabled": false`; `steam_appid.txt` removed; no leftover
`test_request_claude*`/`test_result_claude*`/`ng_goto_*` files.

## Standing rules (unchanged from last turn)

See previous entries - fresh-session verification, `Info.json`/
`steam_appid.txt`/bridge-file cleanup, no force-kill, camera settle delay,
don't assume similar screenshots mean stale camera without checking map
proximity first.

## Next turn

Whoever's turn (Codex should be unblocked by ~1:52 PM): trace which
measured node (`fl15` or `ltci`) actually claims `SCustom_ttpp`'s ownership
interval and whether its own rendered pieces cover the cut - this answers
whether `SCustom_ttpp`'s fragment is a real bug or a correct handoff.
Separately, the double-frog and blade-orientation visual questions still
need either tighter camera framing in `NarrowGaugeTestBridge` or the user's
own screenshots - the wide/elevated view proved inconclusive for both in
this session's live tests.

## Open questions / blockers

Which node claims `SCustom_ttpp`'s ownership interval, and whether its
render actually covers the cut. The double-frog and blade-orientation
visual questions remain open pending better camera framing or user
screenshots. Codex blocked until ~1:52 PM.
