# Coordination Status

Last updated by: Codex - 2026-08-02 09:46

## Current phase: diamond V throats moved inward and heel centerlines restored

The user's fresh `09:35` screenshot falsified the first rendered-profile wing
solve. Its 0.100 m distance was correct only numerically: the target was placed
beyond the opposite frog heel, so the two wing bends spread outward. The
`09:36` prototype establishes that both bends belong between the frog heels,
converging close beside the V point. The user also specified that these wing
slots use the same 0.050 m visible flangeway as the guards, not the native
switch's 0.024 m slot.

The diamond-only wing solver now walks from the opposite frog profile back
toward the source profile. It targets `RailHeadWidth + FlangewayWidth` =
0.076 + 0.050 = 0.126 m between rendered profile centers. Generic switch,
compound V, and narrow-branch paths retain their existing 0.100 m default and
do not opt into this solve. Fresh diagnostics should report
`[VeeWingGap] side=inward profileSeparation=0.126m visibleFlangeway=0.050m`
and the same dimensions in `[DiamondAcuteFrog]`.

The user's `09:39` heel close-up also shows that shifting the frog heel to
compensate for `BuildFrogMesh`'s chord frame created a centerline step where the
V casting meets the stock rail. Both V heel points are now left exactly on the
source stock-rail centerlines. A diamond-only post-mesh pass rotates the two
terminal rail-profile rings and their end caps about those fixed heel points
from the frog chord frames into the actual stock-rail render frames. The nose
ring is untouched, preserving the exact +0.500-degree V opening. The expected
diagnostic is `[VeeFrogHeelAlignment] centerShift=0.0000m ...`.

`dotnet build .\NarrowGaugeMod.csproj /p:EnableModDeploy=true` succeeded with
0 warnings and 0 errors. Output and deployed DLL SHA-256 hashes both equal
`4C7D751128A64B78467FEC4F582F7421AECC53BF829A04026805BD04FF3EADC0`.
Railroader PID 41584 began at 09:31:56, before this 09:48 deployment, so a full
restart is required before visual verification.

## Next turn

User: fully restart Railroader and inspect both acute V frogs. Confirm that the
paired wing bends now converge beside each V point, their clear slots match the
K guards, and both frog-to-stock heel seams are centered and flush.

Claude: review the inward rendered-profile target and terminal mesh-ring frame
correction in `proposals/standard-diamond-crossing.md`, particularly that the
mesh edit is diamond-only and leaves the nose ring/+0.500-degree V angle intact.

## Open questions / blockers

- The inward-throat/zero-center-shift build awaits full-restart visual and log
  verification.
- The second discovered diamond `crossing:SDillsYard2_rdhn:Setp` (18.68 deg)
  still derives only 3 of 4 frogs and falls back to generic crossing points.
- Prior Nove/7n90, vdlt/g832, culling, and related manual-verification items
  remain recorded in `LOG.md` and were not changed by this fix.
