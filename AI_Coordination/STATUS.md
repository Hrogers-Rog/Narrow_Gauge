# Coordination Status

Last updated by: Codex - 2026-08-02 10:15

## Current phase: diamond V wings rebuilt as straight flange paths

The user's `10:03/10:04` close-ups show that uncrossing the wing targets was
necessary but did not correct their construction. The renderer still cut each
incoming source rail at the generic fixed 0.45 m setback and adjusted only the
far wing endpoint. The resulting working wing was an arbitrary chord between
that fixed cutoff and the corrected heel target, rather than a straight rail
parallel to the frog's flangeway. The leading rail consequently ended too soon
and the paired throats remained too far apart.

The diamond-only profile solver now builds the full physical relationship:

- It defines a straight rendered-profile flange line through the accepted
  0.126 m heel target, parallel to the corresponding frog leg.
- It finds the true intersection of that line with the incoming source rail's
  rendered profile by sampled bracketing and bisection.
- It extends the incoming source slice to that solved intersection instead of
  using the fixed 0.45 m cutoff.
- It gives the outgoing bend station and blunt endpoint one identical frame on
  the flange line, making the entire working wing exactly straight.

The non-crossing outside-side assignment, 0.050 m clear flangeway, zero-shift
frog heel-frame correction, and exact +0.500-degree V opening are retained.
Generic switch, compound V, and narrow-branch paths keep their prior fixed
setback behavior. Fresh `[VeeWingGap]` diagnostics should report
`side=outside`, `profileSeparation=0.126m`, `visibleFlangeway=0.050m`, the newly
solved `bendSetback`, `straightWing=1`, and `straightError=0.0000m`.

`dotnet build .\NarrowGaugeMod.csproj /p:EnableModDeploy=true` succeeded with
0 warnings and 0 errors. Output and deployed DLL SHA-256 hashes both equal
`AF7A97800D5B9B164AEFD55E32D87B5F11F6F1028781CC99BCFCB27EBC5B7A46`.
Railroader PID 37144 began at 10:01:19, before this 10:14 deployment, so a full
restart is required before visual verification.

## Next turn

User: fully restart Railroader and inspect both acute V frogs. Confirm that the
incoming rails extend to later bends, each complete working wing is straight
along the frog flangeway, and the two wings remain uncrossed.

Claude: review the rendered-profile line/intersection solve and the two-frame
hard bend in `proposals/standard-diamond-crossing.md`, especially its isolation
to diamond V frogs.

## Open questions / blockers

- The straight-flange-line V-wing build awaits full-restart visual/log
  verification.
- The second discovered diamond `crossing:SDillsYard2_rdhn:Setp` (18.68 deg)
  still derives only 3 of 4 frogs and falls back to generic crossing points.
- Prior Nove/7n90, vdlt/g832, culling, and related manual-verification items
  remain recorded in `LOG.md` and were not changed by this fix.
