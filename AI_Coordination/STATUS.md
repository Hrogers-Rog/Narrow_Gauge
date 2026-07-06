# Coordination Status

Last updated by: Codex - 2026-07-06

## Current phase: full Nove visual loop proven; env propagation caveat documented

Codex completed the end-to-end live loop against `Nove` and captured/viewed
the resulting screenshots. The original "launch Railroader.exe normally and
let it relaunch to `/editor`" path still drops `NARROWGAUGE_TEST_BRIDGE=1`
before the final Steam-launched `/editor` process, exactly like the earlier
`FUSE_TEST_BRIDGE` finding. In that run `FUSE.TestBridge` connected, the save
loaded, and `Special-work analysis: objects=14, invalid=0` appeared, but
`ng_goto_request.json` stayed unprocessed and no `ng_goto_result.json`
appeared.

The working path for getting the env var into the actual game process was:

1. Temporarily write `C:\Steam\steamapps\common\Railroader\steam_appid.txt`
   with content `1683150`.
2. Launch the final editor process directly:
   `Railroader.exe /editor` from the Railroader directory with
   `NARROWGAUGE_TEST_BRIDGE=1` in that process environment.
3. Keep `FUSE.TestBridge/Info.json` toggled to `"Enabled": true` for the
   FUSE bridge, as before.

Without `steam_appid.txt`, direct `/editor` starts far enough to load mods
but then exits with `Steamworks is not initialized`. Steam URI / Steam
`-applaunch 1683150` did not produce a live Railroader process in this
session.

## Source change this turn

`src/NarrowGaugeTestBridge.cs` now also accepts a deployed sentinel file named
`ng_test_bridge_enabled` next to `NarrowGaugeMod.dll`, in addition to the
existing `NARROWGAUGE_TEST_BRIDGE=1` gate, and logs which gate enabled it.
This is an inert dev-only fallback for the relaunch/env problem; no sentinel
file is left deployed after the turn. The successful full loop itself used
the env var via the temporary `steam_appid.txt` direct-final-process launch,
not the sentinel.

Build/deploy command run after the source change:

`dotnet build .\NarrowGaugeMod.csproj -p:RailroaderDir="C:\Steam\steamapps\common\Railroader" -p:EnableModDeploy=true`

Result: 0 warnings, 0 errors.

## Live test result

Successful final session:

- FUSE heartbeat: PID `41988`, `mapLoaded=true`, `canApply=true`, fresh
  heartbeat.
- Process command line: `"C:\Steam\steamapps\common\Railroader\Railroader.exe" /editor`.
- `Player.log`: `NarrowGaugeTestBridge enabled via NARROWGAUGE_TEST_BRIDGE`.
- `loadSave 2026-06-25`: `Ok=true`, `Booting save '2026-06-25' from the main menu.`
- Map load: `[FUSE.NarrowGauge] Special-work analysis: objects=14, invalid=0, elapsedMs=34305.`
- `ng_goto_request.json` content: `{"nodeId":"Nove"}`.
- `ng_goto_result.json`: `{"ok": true, "message": "Jumped to 'Nove' at (1747.79, 589.26, 1369.73)."}`
- Screenshot result: `Ok=true`, artifact
  `C:\Users\roger\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE-test-shots\nove-goto-clear.png`
  (4,749,731 bytes). The first screenshot
  `nove-goto.png` was real but obscured by the UMM window, so Codex closed
  UMM and retook the clear screenshot.

Screenshot inspection: the clear frame is targeted at Nove. It shows the
`fuse-ng:s:Nove:control` world label, the lower green switch stand left of
the rails, and the visible point/blade geometry farther up the special-work
assembly near `SCustom_epu2` / `SCustom_d84` / `Stjh`. In this captured view,
the originally reported symptom ("the switch blade is behind the switch stand
with the blade running towards the middle of the switch") is not visible at
the lower stand; the lower stand area shows stock/straight rails passing to
the right of the stand, with no blade tucked behind it. The image is still an
elevated view with labels and some tree occlusion, so this is visual evidence
from the automated camera position, not a close-up proof of every blade edge.

## Cleanup confirmed

Closed with `umm close` and `CloseMainWindow`; no force-kill was used.
Confirmed afterward:

- no `Railroader.exe` process remained
- `FUSE.TestBridge/Info.json` contained `"Enabled": false`
- temporary `steam_appid.txt` was removed
- `ng_test_bridge_enabled`, `ng_goto_request.json`, and `ng_goto_result.json`
  were removed from the deployed Narrow Gauge mod folder

## Next turn

Claude - review Codex's `NarrowGaugeTestBridge` gate fallback and the live
test findings. If you agree the automated screenshot does not show the Nove
lower-stand blade symptom, resume the substantive special-work investigation
with the now-proven loop: `SCustom_ttpp` cut-source ambiguity, double-frog
mapping, and the remaining "too many rails"/mid-switch-transition reports.

## Open questions / blockers

- The normal Steam relaunch path still drops process-local env vars. For
  repeatable automation, use either the temporary `steam_appid.txt` +
  direct `/editor` launch with env, or test/adopt the new
  `ng_test_bridge_enabled` sentinel fallback.
- The automated Nove screenshot is useful but not a close-up. If the user
  still reports the lower-stand blade issue, add a closer camera pose or a
  second bridge command before treating the visual question as fully closed.
