# Coordination Status

Last updated by: Codex - 2026-08-02 10:47

## Current phase: diamond V-frog heel profiles exactly matched

The user accepted the rebuilt straight V wings as perfect. Fresh runtime
diagnostics preserve the accepted geometry on all four wings:

- `side=outside`
- `profileSeparation=0.126m` / `visibleFlangeway=0.050m`
- solved `bendSetback=0.196-0.199m`
- `straightWing=1` / `straightError=0.0000-0.0004m`
- exact +0.500-degree diamond V opening

The remaining `10:27` close-up isolated a slight lateral step where the V-frog
heel met the adjoining fixed stock rail. The previous post-mesh frame rotation
reproduced the stock orientation mathematically, but did not guarantee an
identical finished terminal cross-section.

The diamond-only heel pass now builds a temporary two-station reference with
the base game's actual `BuildStockRailMesh` path and copies its exact heel
profile ring, end-cap vertices, and normals onto each frog terminal. It does
not move the frog centers or nose and does not touch the accepted wing paths.
The full-restart `10:45` screenshot shows the lateral railhead step removed;
only the thin boundary between the separate stock and frog end caps remains.

`dotnet build .\NarrowGaugeMod.csproj /p:EnableModDeploy=true` succeeded with
0 warnings and 0 errors. Output and deployed DLL SHA-256 hashes both equal
`E352BFC06FF60844DF0F993CEE0C210E2AC18922A2522683C31E43A6BAA23C09`.
Railroader editor PID 44044 started at 10:42:31 after deployment. Fresh logs
confirm `exactStockProfile=1` for both acute frogs and retain all accepted wing
diagnostics.

## Next turn

Claude: review the exact stock-profile terminal copy and the accepted straight
V-wing implementation in `proposals/standard-diamond-crossing.md`.

User: confirm whether the remaining hairline end-cap boundary is acceptable.
If it should be invisible too, the next isolated change is internal-cap
suppression/overlap, not another centerline or wing adjustment.

## Open questions / blockers

- The separate frog/stock meshes still show a thin transverse end-cap boundary;
  the lateral terminal-profile misalignment is corrected.
- The second discovered diamond `crossing:SDillsYard2_rdhn:Setp` (18.68 deg)
  still derives only 3 of 4 frogs and falls back to generic crossing points.
- Prior Nove/7n90, vdlt/g832, culling, and related manual-verification items
  remain recorded in `LOG.md` and were not changed by this fix.
