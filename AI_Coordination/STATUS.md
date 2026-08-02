# Coordination Status

Last updated by: Codex - 2026-08-02 09:19

## Current phase: diamond V profiles aligned at heels and flangeways

The user's post-restart close-ups showed two remaining acute V-frog defects:
a small lateral step where the frog casting meets its adjoining rail, and a
wing flangeway still visibly wider than a base-game switch despite the 0.100 m
curve-point offset.

Both came from treating the hidden curve point as the rendered railhead center.
`MakeRailOnlyProfile` offsets the asymmetric standard rail profile 0.038 m
along the station frame's local X. At the frog heel,
`TrackMeshBuilder.BuildFrogMesh` discards the source heel rotation and rebuilds
it from the heel-to-nose chord, while the adjoining stock mesh retains the
measured source tangent. At the wing throat, curve hand and endpoint frame can
put the 0.038 m profile offset on a different side than the frog. Coincident or
nominally separated curve points therefore did not produce matching rendered
profiles.

The diamond path now opts into two render-profile solves:

- Each frog heel reproduces `BuildFrogMesh`'s actual winding/frame, then shifts
  only its render center enough for the frog railhead center to match the
  adjoining railhead center. This iterates with the nose solver so the final V
  still opens by exactly +0.500 degrees.
- Each wing endpoint targets a rendered profile center exactly 0.100 m beyond
  the corresponding frog profile center. `ReheadRenderFrame` preserves the
  measured wing's profile side while its endpoint and direction converge.
  With a 0.076 m railhead this yields the intended 0.024 m visible gap.

Generic switch, compound V, and narrow-branch paths receive the new opposite
rail argument but do not opt into either compensation, so their geometry is
unchanged. Diamond diagnostics now include `profileAligned=1`,
`[VeeFrogHeelAlignment] maxCorrection=... renderAngle=...`, and
`[VeeWingGap] profileSeparation=0.100m visibleFlangeway=0.024m`.

`dotnet build .\NarrowGaugeMod.csproj /p:EnableModDeploy=true` succeeded with
0 warnings and 0 errors. Output and deployed DLL SHA-256 hashes both equal
`FACD55AB258D5492298738B7F4BEAA618CAD52F510EB74F3F939CBE7D5FF8F21`.
The running Railroader process began at 08:40:36, before this 09:19 deployment,
so a full restart is required.

## Next turn

User: fully restart Railroader and inspect both acute V frogs. Confirm that
both frog-to-running-rail heel seams are flush and both wing slots match a
base-game switch. Send a close-up if either differs.

Claude: review the profile-center solvers in
`proposals/standard-diamond-crossing.md`, especially preservation of the
+0.500-degree angle and isolation from generic V-frog paths.

## Open questions / blockers

- The profile-aligned V-frog build awaits full-restart visual/log verification.
- The second discovered diamond `crossing:SDillsYard2_rdhn:Setp` (18.68 deg)
  still derives only 3 of 4 frogs and falls back to generic crossing points.
- Prior Nove/7n90, vdlt/g832, culling, and related manual-verification items
  remain recorded in `LOG.md` and were not changed by this fix.
