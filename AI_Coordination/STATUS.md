# Coordination Status

Last updated by: Claude - 2026-07-06

## Current phase: found a real process bug that invalidates recent live tests - need a genuine relaunch

**Root cause of Codex's "stale guard isolation" question, confirmed:** the
user's Railroader process has been running as a single continuous session
this entire time - confirmed via `Player.log` containing exactly one
`Initialize engine version` line and exactly one
`[FUSE.NarrowGauge] Version '0.4.0'. Loading.` line for the whole file.
.NET assemblies do not hot-reload; replacing `NarrowGaugeMod.dll` on disk
with `-p:EnableModDeploy=true` has no effect on an already-running process.
That means every screenshot and every `Player.log`/`.txt` export the user
has produced in the last stretch of this session reflects whatever build was
deployed **before that Railroader process launched** - not any fix deployed
after. This explains Codex's diagnostic-caution flag exactly: the live
exports still showing `ISOLATED: v2-guard:*` lines aren't stale exporter
logic, they're a stale *running process* that never picked up the fix at
all.

**Action needed before any more live testing means anything:** the user
must fully quit Railroader (not just close the map/navigate away) and
relaunch it. Only then will the currently-deployed DLL (see below) actually
load.

## This turn's fix (Claude)

Reviewed Codex's investigation (`reviews/plain-and-measured-visual-defect-findings-2026-07-06.md`)
by independently reading the cited code, not just trusting the write-up.
Confirmed the `aThirdRails.right` hardcode claim is real and verified it's
safe to fix now rather than wait for a labeled screenshot, because the fix
is strictly one-directional:

- `CreateDualGaugeNarrowSplitSwitchRailObjects` (`src/NarrowGaugeTrackBuilder.cs`)
  hardcoded `aThirdRails.right` as the dual middle rail reference at two live
  call sites (used to resolve `TryResolveDualGaugeNarrowBranchRails` and to
  orient `dualMiddleFromNode`).
- The function immediately above it in the same file does this correctly:
  `DualGaugeSharedRailRegistry.SharesRightRail(aProxy.Segment) ? aThirdRails.left : aThirdRails.right`.
  11 other call sites across the file consult `SharesRightRail` the same way.
- The hardcode is only wrong when `SharesRightRail(aProxy.Segment)` is
  `true` (correct answer would be `.left`); when it's `false`, `.right` is
  already correct and unchanged by fixing this. So applying the same
  conditional pattern **cannot regress an already-working case** - it only
  fixes the orientation that was definitely wrong. Applied the fix.
- A third hardcoded `.right` usage exists in `CalculateDualGaugeNarrowSplitSlices`,
  but that function has **zero callers anywhere in `src/*.cs`** - confirmed
  dead code, left untouched (not part of the live bug, not worth the risk of
  touching unrelated dead code in this pass).

This is very likely (not yet confirmed in-game) the root cause of the
"blades on the outside/wrong side of the rail" symptom on plain mixed
dual/narrow switches - i.e. wherever `CreateDualGaugeNarrowSplitSwitchRailObjects`
renders a switch on a `DualGauge_L`-oriented (or whichever orientation makes
`SharesRightRail` true) segment.

Rebuilt and redeployed:
`dotnet build NarrowGaugeMod.csproj -p:RailroaderDir="C:\Steam\steamapps\common\Railroader" -p:EnableModDeploy=true`
- 0 warnings/0 errors. This deploy includes: this fix, the earlier
`GeometryContinuity`/`FrogPieces`/`Guard`-exclusion diagnostic fix, and both
of Codex's narrow-branch geometry fixes - **none of which the currently-running
game process has loaded yet** per the finding above.

## Other findings from Codex's investigation (not yet acted on)

- `SCustom_ttpp` confirmed as an ordinary authored `DualGauge_R` segment
  between measured nodes `NCustom_fl15`/`NCustom_ltci`, not a 15th
  special-work plan. Its `[SpecialWorkSegmentClip]` log line conflates
  measured-ownership cuts, gauge-separation frog cuts, and shared-rail-flip
  cuts under one label - a per-source-cut diagnostic is still recommended
  before concluding anything more about it.
- Double frogs map more strongly to measured special-work rendering
  (`NCustom_fl15`, `NCustom_ltci`, `NCustom_fc97` all currently render 3
  frogs) than to the plain pipeline - needs a labeled screenshot at one of
  those specific nodes before touching frog-collapse/compound-vee code.
- "Too many rails" and "transition in the middle of a switch" remain
  unmapped to a specific cause - see the findings file's Symptom Map.

## Standing rules

- Do not trust `Player.log` `valid=True`, or any live test at all, unless
  you've confirmed the running game process actually launched *after* the
  relevant deploy. Check for exactly one `Initialize engine version` +
  one mod-version-load line per session in `Player.log`, and compare
  wall-clock timing against the last deploy if there's any doubt.
- Do not relax validation to hide geometry defects.
- Do not patch all four reported symptoms together - map a symptom to a
  node/system/code path first (the `aThirdRails.right` fix above is an
  exception because the fix was provably one-directional and safe, not
  because the mapping rule is being abandoned).
- Always deploy with `-p:EnableModDeploy=true` for anything meant to be
  tested in-game, AND confirm the game was actually relaunched afterward.

## Next turn

User: please fully quit Railroader (not just alt-tab or return to menu -
the whole process) and relaunch it, then re-test and let it write a fresh
session. This is required for any of this session's fixes to actually be
in effect. Once that's done, whoever picks this up (Claude or Codex) should
re-check `Player.log` for the one-init/one-load pattern to confirm it's
truly fresh, then re-read the `GeometryContinuity` sections and check the
`aThirdRails.right` fix against any mixed dual/narrow switch visually.

If the user is also going to explore automating launch/test/close (raised
this session - see `FUSE.TestBridge`/`FUSE.LiveHarness` in the sibling FUSE
repo, which already supports headless console-command execution and
screenshots via a file-based protocol), whoever builds that must make the
relaunch-vs-hot-reload distinction a first-class part of the design - a
kill+relaunch of the actual process, confirmed via the one-init-line check
above, not just a "wait N seconds" heuristic.

## Open questions / blockers

Blocked on a genuine Railroader relaunch before any further live test
result can be trusted. Still open from Codex's investigation: which exact
node/segment each remaining symptom (double frogs, too many rails, possible
mid-switch transition) belongs to, and whether `SCustom_ttpp`'s cuts come
from measured ownership, gauge separation, or both.
