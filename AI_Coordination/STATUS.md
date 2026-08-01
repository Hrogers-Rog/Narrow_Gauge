# Coordination Status

Last updated by: Codex - 2026-07-31

## Current phase: EF&A standard-gauge diamond design

The user's screenshot is an ordinary standard-gauge fixed diamond for an EF&A
interlock. It requires **zero ghost nodes**. The correct model is four logical
ports and two continuous, graph-disconnected routes; their geometric crossing
must not become a shared native node.

C_L_B.DKW's KRE implementation confirms the pattern: explicitly bind two
existing segments, detect their centerline and physical rail intersections,
replace only the render proxies inside the work area, and preserve the native
route graph. See `proposals/standard-diamond-crossing.md` (Draft).

The current `crossing.diamond` catalog entry remains TODO and is not an
implemented/accepted runtime path. `SpecialWorkAuthoring` only supports one
`anchorNode`; a generic diamond needs explicit segment-pair authoring and a
segment-pair discovery/compile adapter.

## Next turn

Claude: review `proposals/standard-diamond-crossing.md` against the current
special-work compiler and either agree or record a disagreement. If agreed,
implementation should begin with generic segment-pair authoring/discovery and
use the EF&A crossing as the first measured fixture. Do not add EF&A node-ID
special cases.

## Open questions / blockers

- The exact EF&A segment IDs for the pictured interlock have not yet been
  identified. They are needed for implementation/live verification, not for
  settling the topology.
- Decide the backward-compatible field names for the two authored segment
  references (`segmentA`/`segmentB` or equivalent).
- Prior Nove/7n90, vdlt/g832, culling, and related manual-verification items
  remain in `LOG.md`; this design-only turn did not alter their deployed code.
