# Coordination Status

Last updated by: Claude - 2026-07-06

## Current phase: live-game automation verified working; camera control is the remaining gap

Codex's `FUSE.TestBridge` harness turn (previous entry, kept below) was
independently verified, not just trusted:

- Confirmed via `tasklist` that Railroader was not left running after the
  session.
- Confirmed the deployed `FUSE.TestBridge/Info.json` was actually restored to
  `"Enabled": false` (read the file directly).
- Viewed the captured screenshot directly
  (`FUSE-test-shots/narrow-gauge-harness-20260706-0903.png`) - it's real: a
  genuine in-game trackside view with World Labels on, showing actual
  segment IDs (`SCustom_e6i0`, `fuse-ng:s:SCustom_47ab`, etc.), confirming
  this was a real running session, not a fabricated report.

**This means we now have real, working automation for validation-level
testing**: build+deploy NarrowGaugeMod -> toggle TestBridge's `Info.json`
`Enabled: true` -> launch Railroader normally (not `-batchmode`, that path
doesn't work yet - see below) -> poll `test_state.json` for a fresh
heartbeat -> `loadSave` the user's real save by name (`2026-06-25` or
`2026-06-25_auto1`) -> wait for `mapLoaded=true` and/or
`Special-work analysis: objects=14` in `Player.log` -> run `console` verb
requests (e.g. `/fuse.report json`) for structured data -> close cleanly
(`umm close` / `CloseMainWindow`, confirmed no force-kill needed) -> restore
`Info.json` to `Enabled: false`. Either agent can now independently verify a
fix's log/validation-level effect without the user launching anything.

## The remaining gap: no camera control

Checked the base game's decompiled console commands for a way to point the
camera at a specific track node/switch before a `screenshot` request.
Found `/tp <place>` (`Decompiled dlls base game/Assembly-CSharp/UI/Console/Commands/TeleportCommand.cs`) -
but it only jumps to a predefined named `SpawnPoint` or follows an existing
`Car` by name/ID. It does **not** accept arbitrary world coordinates or a
track node ID. Searched FUSE's own console commands too (`FuseConsoleCommands.cs`) -
nothing camera-related there either. So `screenshot` only ever captures
whatever the camera happens to already be looking at (wherever the save's
camera was last left) - it cannot yet target a specific switch like `Nove`
or `SCustom_ttpp` on demand.

To close this gap, the concrete option is to add a new debug console
command to NarrowGaugeMod itself (we own this source) - e.g.
`/ng.goto <nodeId>` that reads the node's world transform from the live
graph and moves the camera there, mirroring what `/tp` does internally
(`CameraSelector.shared.JumpToPoint(position, rotation, null)`). Not built
yet - this is a real, scoped feature request, not a quick patch, and the
user should decide whether it's worth building before more automated visual
verification work happens.

## What did not work (Codex's findings, still true)

- True headless (`-batchmode -nographics`) launch does not reach a connected
  bridge state yet - do not assume headless screenshot/console automation
  is available. Normal graphical launch + temporary `Info.json` toggle is
  the only proven path right now.
- Environment-variable activation (`FUSE_TEST_BRIDGE=1`) does not propagate
  to the actual game process (Railroader re-launches itself as a second
  `/editor` process that doesn't inherit it) - the `Info.json` toggle is the
  reliable activation method.
- `FUSE.TestCli`'s own `dotnet run` path failed in this sandbox (NuGet.Config
  permission issue) - direct JSON request/response files against the bridge
  worked fine and are sufficient without it.

## Standing rules (unchanged, now with one addition)

- Do not trust `Player.log`/exports unless the session is confirmed fresh
  AND the automated pipeline's own `mapLoaded`/heartbeat state confirms it,
  not just file content.
- Always restore `FUSE.TestBridge`'s `Info.json` to `Enabled: false` after
  any automated session - confirm this by reading the file, not assuming a
  cleanup step ran.
- Do not force-kill Railroader; use `umm close`/`CloseMainWindow`. This
  session proved a clean shutdown path exists - there's no excuse to skip it.
- Screenshot automation currently only captures the existing camera view -
  do not claim it verifies a *specific* switch unless the camera was
  actually confirmed pointed at it.

## Next turn

Open question for the user: build the `/ng.goto <nodeId>` camera command (a
real, scoped NarrowGaugeMod feature, not exploratory anymore) to get full
automated visual verification of specific switches, or continue relying on
the user's own screenshots for visual confirmation while using the now-proven
automation for validation-level checks (build/deploy/launch/loadSave/report/close)?
Once decided: if building the camera command, that's a Claude or Codex
implementation turn like any other code change (build, verify, no in-game
test needed beyond the automation we now have). If not, resume investigating
the still-open symptoms (Nove's blade orientation, `SCustom_ttpp`'s
cut-source ambiguity, double-frog mapping, "too many rails") using the
automated pipeline for log-level checks and the user's screenshots for
visual ones.

## Open questions / blockers

Waiting on the user's decision: build the camera-goto command, or proceed
without it. Also still open from before: Nove's blade-orientation symptom
(unresolved, needs either a screenshot after camera control exists or the
user's continued manual testing), `SCustom_ttpp` cut-source ambiguity,
double-frog mapping, unmapped "too many rails" symptom.
