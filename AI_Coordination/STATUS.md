# Coordination Status

Last updated by: Claude - 2026-07-06

## Current phase: Claude independently drove the live-game pipeline; real findings, one methodology lesson

Codex hit its usage limit again mid-turn (blocked until 1:52 PM this time,
shorter than the earlier multi-day block). Since the user authorized
working autonomously, Claude drove the full launch/goto/screenshot/close
loop directly via Bash, using the exact recipe Codex documented (toggle
`FUSE.TestBridge/Info.json` to `Enabled: true`, write a temporary
`steam_appid.txt` with `1683150`, launch `Railroader.exe /editor` directly
with `NARROWGAUGE_TEST_BRIDGE=1` in that process's environment, poll
`test_state.json`, `loadSave`, write `ng_goto_request.json`/read
`ng_goto_result.json`, screenshot via the bridge, `umm`/`cleanup` verbs,
`CloseMainWindow`). This confirms either agent can run this pipeline, not
just review the other's run of it.

## Real methodology lesson found this run

`CameraSelector.JumpToPoint` starts a Unity **coroutine**
(`base.StartCoroutine(this._JumpToPoint(...))` - confirmed in
`CameraSelector.cs`) and returns immediately; the camera pan happens over
subsequent frames, not synchronously. `NarrowGaugeTestBridge` writes its
result file as soon as `JumpToPoint` returns, so a screenshot requested
immediately after reading `ng_goto_result.json` can race the still-panning
camera. Recommend adding a short settle delay (~3-6s empirically) between
reading a `ng_goto_result.json` and requesting a screenshot in any future
automation of this loop, until/unless `NarrowGaugeTestBridge` is changed to
wait for the coroutine itself before writing its result.

Note: initially suspected this caused two visually-identical screenshots
(`NCustom_fl15` and `NCustom_ltci`), but re-shot both with an explicit 6s
settle delay and got the *same* near-identical result both times - the
real explanation turned out to be that `Nove`, `NCustom_fl15`,
`NCustom_ltci`, and `NCustom_fc97` are all part of the same clustered
DeHart yard installation on the map (confirmed: all four screenshots show
the same yellow station building and largely the same `SCustom_*`
segment labels), so a wide/elevated camera view from any of their
positions legitimately looks similar. `NCustom_fc97`'s screenshot did show
a distinctly different angle (more of the yard, the turntable visible) -
that difference is real, not a settle-delay artifact.

## Screenshots captured and reviewed this turn

All viewed directly (not just checked for `Ok=true`):

- `NCustom_fl15`, `NCustom_ltci` (x2, with and without settle delay - same
  result both times): wide elevated view of the DeHart yard throat, several
  parallel tracks converging, no obviously duplicated frog castings
  distinguishable at this zoom/distance. Inconclusive on the "double frog"
  question - the view is too wide/elevated to see individual frog geometry
  clearly.
- `NCustom_fc97`: a different, more zoomed-toward-yard-building angle,
  showing the turntable and a loading dock. **Possible finding**: small
  white curved fragments visible on/near the lower-middle parallel tracks
  that look disconnected from the main rail lines - candidate visual match
  for the reported defects, but still not a close-up; needs a tighter shot
  or in-game confirmation to call this confirmed.

None of these three screenshots provide a definitive yes/no on the
"double frog" hypothesis from `reviews/plain-and-measured-visual-defect-findings-2026-07-06.md` -
the camera's default elevated framing isn't tight enough to distinguish two
close frog castings from one legitimate compound assembly. Improving camera
framing (a closer default distance/angle in `NarrowGaugeTestBridge`, or a
zoom parameter in the request) would help future turns more than repeating
this same shot.

## Cleanup confirmed (by Claude, directly)

- `tasklist` before and after: no `Railroader.exe` process left running.
- `FUSE.TestBridge/Info.json`: confirmed `"Enabled": false` restored (read
  directly).
- `steam_appid.txt`: removed (confirmed via `ls` failure).
- `ng_goto_request.json`/`ng_goto_result.json`/`ng_test_bridge_enabled`:
  confirmed absent from the deployed `FUSE.NarrowGauge` folder.
- Leftover `test_result_claude*.json` files (created directly by Claude's
  requests, not auto-cleaned by the bridge's own `cleanup` verb, which only
  removes test *saves*) were manually deleted from the `FUSE.TestBridge`
  folder.

## Standing rules (unchanged, plus one addition)

- Do not trust `Player.log`/exports unless the session is confirmed fresh
  AND the automated pipeline's own `mapLoaded`/heartbeat state confirms it.
- Always restore `FUSE.TestBridge/Info.json` to `Enabled: false`, remove
  `steam_appid.txt`, and remove any leftover `test_request_*`/`test_result_*`/
  `ng_goto_*` files after an automated session - confirm each by reading/
  listing, not assuming a cleanup step caught everything.
- Do not force-kill Railroader; use `CloseMainWindow`/`umm close`.
- New: wait several seconds after `ng_goto_result.json` appears before
  requesting a screenshot - `JumpToPoint` is an async coroutine, not
  synchronous.
- New: don't assume two similar-looking screenshots mean stale camera
  positioning - check whether the target nodes are actually near each other
  on the map first (this yard cluster is genuinely dense).

## Next turn

Whoever's turn (Claude or Codex, whichever is available - Codex is blocked
until 1:52 PM today): either (a) improve `NarrowGaugeTestBridge`'s camera
framing (closer/tighter default view) so double-frog and blade-orientation
questions can actually be answered from a screenshot, or (b) continue the
substantive investigation using what static/log data is available
(`SCustom_ttpp`'s cut-source ambiguity is well-suited to a targeted logging
diagnostic rather than a screenshot, per the original investigation file).
Recommend (b) first since it doesn't need another live session, then (a) if
still blocked on visual questions afterward.

## Open questions / blockers

Codex blocked until 1:52 PM (usage limit). The "double frog" and
"wrong-side blade" visual questions remain unresolved - screenshots
gathered this turn were too wide/elevated to confirm or refute either.
`SCustom_ttpp`'s cut-source ambiguity and the "too many rails" symptom are
both still open and don't require a live session to make progress on.
