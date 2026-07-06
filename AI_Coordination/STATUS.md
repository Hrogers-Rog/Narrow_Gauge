# Coordination Status

Last updated by: Claude - 2026-07-06

## Current phase: camera-goto bridge built; needs a live test of the full automated visual-verification loop

User authorized proceeding autonomously ("do whatever you need to do... without my input"). Built the
camera-control piece that was missing after the last turn's `FUSE.TestBridge`
verification.

## This turn's addition: `NarrowGaugeTestBridge` (self-contained, no FUSE repo changes)

Investigated adding a proper console command reachable through
`FUSE.TestBridge`'s "console" verb, but confirmed that's a dead end without
editing the separate FUSE repo: `FuseTestApi.Commands()`
(`FUSE/Testing/FuseTestApi.cs`) builds its dispatch table *only* from
`FuseConsoleCommands.CreateAll()` - it does not fall through to the real
game's `ConsoleCommandHandler._commands` dictionary, so a NarrowGaugeMod
console command registered the way `FuseLegacyAssemblyHost.cs` registers
FUSE's (via reflection into `ConsoleCommandHandler.Register<T>`) would work
in the interactive console but never be reachable via the automated bridge.

Instead, added `src/NarrowGaugeTestBridge.cs` - a small, self-contained
file-based bridge living entirely in this repo, modeled on the same pattern
as `FUSE.TestBridge` but scoped to one job: move the camera. Drop
`ng_goto_request.json` (`{"nodeId": "Nove"}`) next to the deployed
`NarrowGaugeMod.dll`; within 0.5s the camera jumps to that node via the same
call the base game's own `/tp` command uses internally
(`CameraSelector.shared.JumpToPoint(WorldTransformer.WorldToGame(node.transform.position), node.transform.rotation, null)`,
confirmed by reading `TeleportCommand.cs` and `CameraSelector.cs` directly -
not guessed), then `ng_goto_result.json` reports `{"ok": ..., "message": ...}`
and the request file is deleted. Gated behind the `NARROWGAUGE_TEST_BRIDGE=1`
environment variable so it's completely inert for normal players, mirroring
`FUSE.TestBridge`'s own dev-only gating. Registered as a component on the
existing `ManagerObject` in `Main.cs` alongside `NarrowGaugeManager`/
`SpecialWorkDebugRenderer`/`SpecialWorkAdjustmentUI`.

Built and deployed:
`dotnet build NarrowGaugeMod.csproj -p:RailroaderDir="C:\Steam\steamapps\common\Railroader" -p:EnableModDeploy=true`
- 0 warnings/0 errors. **Not yet tested live** - this needs the same launch
recipe Codex proved last turn, plus setting `NARROWGAUGE_TEST_BRIDGE=1` in
the launch environment, plus writing the `ng_goto_request.json` file and
polling for the result, then requesting a screenshot via the existing
`FUSE.TestBridge` "screenshot" verb.

## Full loop to test (combining both bridges)

1. Set both env vars for the launch: `FUSE_TEST_BRIDGE=1` did NOT propagate
   to the actual game process last turn (Railroader re-launches itself as a
   second `/editor` process) - the working method was toggling deployed
   `FUSE.TestBridge/Info.json` to `"Enabled": true` before launch and back
   to `false` after. Confirm whether `NARROWGAUGE_TEST_BRIDGE` has the same
   propagation problem - if so, this bridge's env-var gate may need the same
   kind of toggle-a-file fallback (e.g. check for a sentinel file's
   existence instead of/in addition to the env var) before it's reliable
   from an automated launch. Test this explicitly rather than assuming the
   env var will just work this time.
2. Launch, confirm `FUSE.TestBridge` reaches `Connected`.
3. `loadSave` the user's save (`2026-06-25` or `2026-06-25_auto1`), wait for
   `mapLoaded=true` / `Special-work analysis: objects=14` in `Player.log`.
4. Write `ng_goto_request.json` with `{"nodeId": "Nove"}` next to the
   deployed `NarrowGaugeMod.dll`
   (`C:\Steam\steamapps\common\Railroader\Mods\FUSE.NarrowGauge\`). Poll for
   `ng_goto_result.json` (should appear within ~1s if the bridge is active).
5. Send a `screenshot` request via `FUSE.TestBridge` and read the result -
   this should now show Nove's switch specifically instead of an arbitrary
   camera position.
6. Look at the actual image (not just trust `Ok=true`) to check whether the
   blade orientation symptom the user reported is visible.
7. Close cleanly (`umm close`/`CloseMainWindow`, confirmed no force-kill
   needed last turn), restore `FUSE.TestBridge/Info.json` to
   `"Enabled": false`.

## Standing rules (unchanged, plus one addition)

- Do not trust `Player.log`/exports unless the session is confirmed fresh
  AND the automated pipeline's own `mapLoaded`/heartbeat state confirms it.
- Always restore `FUSE.TestBridge`'s `Info.json` to `Enabled: false` after
  any automated session - confirm by reading the file, not assuming.
- Do not force-kill Railroader; use `umm close`/`CloseMainWindow`.
- Screenshot automation only shows what the camera is pointed at - now that
  `NarrowGaugeTestBridge` exists, confirm the goto actually happened (read
  `ng_goto_result.json`, and ideally cross-check the screenshot's visible
  content/labels against the target node) before treating a screenshot as
  evidence about a specific switch.
- New: don't assume an env-var gate propagates to the actual game process
  the way a deployed-file gate does - this bit us once already with
  `FUSE_TEST_BRIDGE`. Verify empirically for `NARROWGAUGE_TEST_BRIDGE` too.

## Next turn

Codex - test the full loop above end to end against `Nove` specifically
(the reported blade-orientation symptom). Report exactly what worked, what
didn't, and include/describe the actual screenshot content, not just
whether the request returned `Ok=true`. If the env var doesn't propagate,
figure out a working alternative (e.g. a sentinel file check) and document
it the same way the `Info.json` toggle was documented last turn.

## Open questions / blockers

Whether `NARROWGAUGE_TEST_BRIDGE=1` actually reaches the final game process
is unverified - test this explicitly, don't assume. Once the full loop is
proven, resume the substantive investigation (Nove's blade orientation,
`SCustom_ttpp`'s cut-source ambiguity, double-frog mapping, "too many
rails") using real screenshots of the actual switches instead of guessing
from coordinate data.
