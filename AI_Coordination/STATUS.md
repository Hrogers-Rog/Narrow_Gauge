# Coordination Status

Last updated by: Codex - 2026-07-06

## Current phase: FUSE.TestBridge live-game harness proven, with launch caveats

This turn set up and exercised the sibling FUSE repo's `FUSE.TestBridge`
against the live Railroader install. No NarrowGauge source was modified.

## Confirmed working

- Built and deployed `FUSE.TestBridge` from
  `C:\Hrogers_Railroader_mods_Projects\FUSE\FUSE.TestBridge` with:
  `dotnet build FUSE.TestBridge.csproj -p:EnableTestBridgeDeploy=true -p:GameDir="C:\Steam\steamapps\common\Railroader"`.
  Build/deploy completed with 0 warnings and 0 errors.
- The runtime activation gate is exactly as documented in `Main.cs`: either
  `FUSE_TEST_BRIDGE=1` or deployed `Info.json` `"Enabled": true`.
- The successful launch path was: temporarily set the deployed
  `C:\Steam\steamapps\common\Railroader\Mods\FUSE.TestBridge\Info.json`
  to `"Enabled": true`, launch `Railroader.exe` normally, then restore the
  deployed `Info.json` to `"Enabled": false` after shutdown.
- The bridge reached `Connected` with fresh heartbeat from live PID `27828`
  and `MapLoaded=false` at the main menu, then loaded the user's real save
  by name:
  - `saves` returned `2026-06-25_auto1` and `2026-06-25`
  - `loadSave` with arg `2026-06-25` returned `Ok=true` and
    `Booting save '2026-06-25' from the main menu.`
- The save finished loading and settled:
  - `test_state.json`: `mapLoaded=true`, `canApply=true`, fresh heartbeat
  - `Player.log`: `[FUSE.NarrowGauge] Special-work analysis: objects=14, invalid=0, elapsedMs=34074.`
- A `console` verb request for `/fuse.report json` returned `Ok=true` with
  17,586 bytes of meaningful JSON text. Prefix summary:
  `FUSE: 20 loaded | faults 1 | conflicts 0 | assets 29 | graph 2 | transfers 0 | suppressions 130 | orphans 0 | /fuse.report`.
- `umm close` returned `Ok=true` / `UMM window closed.`
- `screenshot` returned `Ok=true` and wrote:
  `C:\Users\roger\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE-test-shots\narrow-gauge-harness-20260706-0903.png`
  (3,686,542 bytes). It captures the current in-game camera exactly as-is:
  a trackside view with world labels and the top HUD. The protocol does not
  expose camera positioning.
- `cleanup` returned `Ok=true` / `Removed 0 test save(s).`
- Railroader was closed cleanly with `CloseMainWindow`; no `Stop-Process`,
  `taskkill`, or other forced termination was used for the successful run.
- Final successful `Player.log` freshness check passed:
  exactly one `Initialize engine version`, exactly one
  `[FUSE.NarrowGauge] Version`, exactly one bridge-enabled line, exactly one
  `Special-work analysis: objects=14`.

## What did not work / risks

- A true Unity headless launch using `-batchmode -nographics` is not proven.
  It wrote one bridge heartbeat from PID `21440`, then the heartbeat went
  stale; a later `Railroader.exe -batchmode -nographics /editor` process had
  no live bridge. It was closed by posting Windows close messages; no force
  kill was needed.
- Launching normally with only `$env:FUSE_TEST_BRIDGE='1'` also did not work
  end-to-end. Railroader hands off to a second `Railroader.exe /editor`
  process, and the final process did not inherit the environment variable:
  `Player.log` showed `FUSE.TestBridge present but disabled`. That disabled
  session was closed cleanly with `CloseMainWindow`.
- `FUSE.TestCli` could not be used from this sandbox because `dotnet run`
  failed reading `C:\Users\roger\AppData\Roaming\NuGet\NuGet.Config` with
  access denied. Direct protocol JSON files worked and should be enough for a
  NarrowGauge-local wrapper if needed.
- Since the deployed `Info.json` was restored to `"Enabled": false`, future
  automation should explicitly toggle it true before launch and false after
  shutdown, or find a launch path where the environment variable reaches the
  final `/editor` process.
- The bridge can prove load/console/report/screenshot, but screenshot-based
  visual regression still needs a camera-control strategy. Current screenshot
  automation only captures whatever the game camera is already viewing.

## Next turn

Claude can treat the live-game bridge pipeline as proven for normal graphics
launches with temporary deployed-`Info.json` enablement. If continuing
automation work, the next useful step is a small local harness script that
toggles the bridge flag, launches the game, sends direct JSON requests, waits
for readiness, captures screenshots/reports, closes the game, and restores
the flag.

## Open questions / blockers

- True `-batchmode -nographics` operation is not working yet; do not assume
  headless screenshots or headless request processing are available.
- Camera positioning is not exposed by the current bridge protocol.
