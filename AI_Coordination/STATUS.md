# Coordination Status

Last updated by: Codex - 2026-08-02 08:48

## Current phase: base-game V gap loaded; slight frog-heel seam diagnosed

The deployed EF&A diamond uses the base game's 0.100 m switch-frog center
separation instead of the former 0.126 m override. With the 0.076 m standard
railhead, this is the approximately 0.024 m visible flangeway requested by the
user. The current Railroader process started at 08:40:36, after the corrected
DLL was deployed at 08:25:09, and the user's new 08:43/08:44 screenshots are
from that restarted build.

The new close-ups expose a separate, very small step where a V-frog heel meets
the adjoining ordinary rail. The two center points are sourced from the same
rail location; the discrepancy is their extrusion frame:

- `TrackMeshBuilder.BuildFrogMesh` discards each supplied heel rotation and
  rebuilds it from the heel-to-rendered-nose chord.
- The adjoining `BuildStockRailMesh` retains the source curve's measured
  tangent/frame.
- The diamond's +0.500-degree nose opening makes those directions differ.
- `MakeRailOnlyProfile` offsets the rail profile 0.038 m along local X, turning
  that small frame-angle difference into the visible lateral railhead step.

No source was changed for this diagnosis. A proper fix requires either
profile-center compensation at the two frog heels or a custom frog mesh path
that accepts explicit heel frames. Merely changing `heelA.Rotation` or
`heelB.Rotation` will not work because the base mesh builder ignores them.

The deployed DLL remains SHA-256
`E5EA223758EE239BC3B2D9BD243699FA33AB5F62F9421DF26BA52A83D6D9A5BF`.

## Next turn

User: decide whether to implement the small frog-heel seam correction. If so,
Codex/Claude should preserve the now-correct 0.100 m flangeway and
+0.500-degree V opening and compensate only the two rendered heel profiles.

Claude: review the frame diagnosis in
`proposals/standard-diamond-crossing.md`, particularly whether profile-center
compensation or a custom frog extrusion is the safer implementation.

## Open questions / blockers

- The slight V-frog heel step is diagnosed but not changed because the user
  asked for its cause rather than requesting implementation.
- The second discovered diamond `crossing:SDillsYard2_rdhn:Setp` (18.68 deg)
  still derives only 3 of 4 frogs and falls back to generic crossing points.
- Prior Nove/7n90, vdlt/g832, culling, and related manual-verification items
  remain recorded in `LOG.md` and were not changed by this diagnosis.
