# Coordination Status

Last updated by: Codex - 2026-08-02 11:56

## Current phase: fixed-running diamond handoff render-matched

The user's debug-labeled `11:23` close-up corrected the target of the reported
misalignment. It is not a V-frog heel seam. It is the outer ownership handoff
between custom `FixedRunning crossing-b:right` and the normal stock rail on
`SDillsYard2_uvlz`.

The cause was two incompatible render curves for one authored Bezier:

- automatic crossing discovery approximated the world-space curve with its
  finer analysis settings and rebuilt reversed frames from horizontal chords;
- the normal segment renderer subtracts `EndPoint1`, approximates locally with
  the base game's `0.5/16/40` settings, and retains the Bezier frames.

At the roughly 21.7-km map coordinate, the resulting parallel rail offsets did
not have identical terminal positions/frames. This was especially visible on
route B because its authored curve runs B-to-A and the discovery path had
reconstructed its reversed frames.

`StandardCrossingDiscovery` now builds automatic-diamond routes from the exact
local-origin approximation used by `SwitchGeometry.MakeTrackLineSegments`. If
the authored curve is reversed, each stored rotation receives an exact local
180-degree yaw instead of being reconstructed from a chord. Logical right then
coincides exactly with the normal renderer's physical left rail (and logical
left with physical right). No frog, wing, guard, or flangeway renderer code was
changed this turn.

`dotnet build .\NarrowGaugeMod.csproj /p:EnableModDeploy=true` succeeded with
0 warnings and 0 errors. Output and deployed DLL SHA-256 hashes both equal
`54A6B5FACFA252BDF274AAA7493835C34679C674E321ABF1DDCAB32D7895EE59`.
Railroader editor PID 12032 started at 11:46:52 after deployment. Fresh logs
confirm the target diamond remains valid with 12 fixed rails and four frogs.
All four accepted wings retain `side=outside`, 0.126/0.050 m separation,
`straightWing=1`, and 0.0000-0.0004 m straight error.

The user visually inspected the fresh build and confirmed the
`FixedRunning crossing-b:right` handoff is fixed. The editor was closed after
that successful check.

## Next turn

Claude: review the render-matched automatic-crossing curve in
`StandardCrossingDiscovery.cs` and the corrected diagnosis in
`proposals/standard-diamond-crossing.md`.

## Open questions / blockers

- The second discovered diamond `crossing:SDillsYard2_rdhn:Setp` (now measured
  near 18.7 degrees) still derives only 3 of 4 frogs and falls back to generic
  crossing points.
- Prior Nove/7n90, vdlt/g832, culling, and related manual-verification items
  remain recorded in `LOG.md` and were not changed by this fix.
