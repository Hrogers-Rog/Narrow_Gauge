# Coordination Status

Last updated by: Codex - 2026-08-02 09:59

## Current phase: crossed diamond V wings restored to their outside sides

The user's `09:53` full-restart screenshot proves the preceding inward-target
correction crossed WingA and WingB through one another. Fresh diagnostics
confirm that build otherwise loaded as intended: both acute frogs retained
their exact +0.500-degree openings, zero-shift heel mesh correction, and
0.126 m rendered wing separation / 0.050 m visible flangeway.

The crossed result came from overlooking that the two source rails have already
exchanged physical sides at the acute intersection. Each wing approaches on
the outside side associated with the opposite frog heel. Moving its target from
that heel back between the two heels makes the source paths exchange sides a
second time. The diamond-only target is therefore restored beyond the opposite
frog profile, away from the source profile. This preserves each wing's outside
approach side while retaining the user-specified 0.126/0.050 m profile spacing.
Fresh logs should report `[VeeWingGap] side=outside` for all four wings.

The terminal V-frog mesh-frame correction is unchanged. Both frog heel centers
remain exactly on their stock-rail centerlines, the first/last profile rings and
end caps rotate into the stock render frames, and the nose ring is untouched.

`dotnet build .\NarrowGaugeMod.csproj /p:EnableModDeploy=true` succeeded with
0 warnings and 0 errors. Output and deployed DLL SHA-256 hashes both equal
`597CEF376F02A8D24224A4A85DEE7375D8F596E472E900760C160EB4538B4C09`.
Railroader PID 23660 began at 09:50:46, before this 09:58 deployment, so a full
restart is required before visual verification.

## Next turn

User: fully restart Railroader and confirm that both V-wing pairs remain on
their outside sides without crossing, while retaining the guard-matched wing
flangeway and flush frog-to-stock heel seams.

Claude: review the corrected outside-side target in
`proposals/standard-diamond-crossing.md` and the unchanged terminal mesh-ring
frame correction.

## Open questions / blockers

- The non-crossing outside-target build awaits full-restart visual/log
  verification.
- The second discovered diamond `crossing:SDillsYard2_rdhn:Setp` (18.68 deg)
  still derives only 3 of 4 frogs and falls back to generic crossing points.
- Prior Nove/7n90, vdlt/g832, culling, and related manual-verification items
  remain recorded in `LOG.md` and were not changed by this fix.
