# Coordination Status

Last updated by: Claude - 2026-07-06

## Current phase: fresh session confirms fixes hold; diagnostic tool validated; one mapping correction

User fully quit and relaunched Railroader (confirmed genuinely fresh:
`Player.log` deployed-DLL timestamp 01:59 vs. session log activity starting
after that and continuing to 02:39+, exactly one `Initialize engine version`
+ one mod-load line). This is the first live test this session that can
actually be trusted end-to-end.

## Confirmed in the fresh session

- `Special-work analysis: objects=14, invalid=0` - all measured items 1/2
  fixes (split-standard-narrow, both-diverge SharedDuplicate) and the
  narrow-branch rendering-gap fix (Codex, two turns) still hold with the two
  previously-relaxed checks now hard failures again. Not a fluke of a stale
  process this time - this is a genuinely fresh read.
- The `GeometryContinuity` diagnostic fix (FrogPieces coverage + Guard
  exclusion) works as intended: 9 of 14 switches now report *zero* isolated
  pieces (`NCustom_fc97`, `NCustom_fl15`, `NCustom_l4a4`, `NCustom_ltci`,
  `NCustom_p997`, `NCustom_u6n0`, `NDeHartPassing_33d6`,
  `NDeHartPassing_wqbb`, `Npv2`), down from every single switch showing
  multiple false positives before the fix.

## New finding: the remaining ISOLATED pattern is very likely a diagnostic blind spot, not a bug

All 5 `dual.narrow-branch-joins-main` switches (`N178`, `NCustom_7n90`,
`NCustom_g832`, `NCustom_vdlt`, `Nove`) - and only those - each show exactly
one `ISOLATED` fixed piece, ~18-32m long. Investigated why: in these
switches, the outer `standard-through` rail is built as **one single
unsubdivided piece** spanning the whole switch zone, with both ends at the
measured-zone boundary (legitimately meeting ordinary track outside the
plan, not a gap). Compared against a clean `dual.both-diverge` switch
(`NCustom_l4a4`): the equivalent outer rail there is chopped into 3 pieces
that chain end-to-end within the plan (`SharedRunning` -> `FixedRunning` ->
`SharedRunning`), so even though its outermost boundaries also exit to
unmeasured track, each piece still has at least one *internal* neighbor and
never reads as fully isolated.

This means my `GeometryContinuity` diagnostic can't currently distinguish
"genuinely disconnected floating fragment" from "a single rail piece whose
boundaries are the edge of the measured zone, not a real gap." Do not treat
this specific pattern as a confirmed defect. A future refinement could
special-case a piece whose endpoint sits at a route's authored start/end
distance, the same way `Guard` was special-cased - not done this turn, since
it's not blocking anything and needs more thought about how to reliably
detect "this is the zone boundary" from the plan data available.

## Correction: the `aThirdRails.right` fix does not touch `Nove`

`Nove` is one of the 14 measured special-work switches - its blade geometry
comes from `SectionedSpecialWorkBuilder`/`SpecialWorkHardwareRenderer`, not
`CreateDualGaugeNarrowSplitSwitchRailObjects` (which only renders switches
*outside* the measured system - confirmed via `vanillaRailObjects=0` for
measured nodes per Codex's investigation, meaning the plain/legacy pipeline
is suppressed there). The `aThirdRails.right` fix is still real and worth
keeping (it fixes a genuine one-directional bug in the plain pipeline), but
it's very unlikely to explain the Nove blade-orientation symptom the user
reported (blade running toward the switch center instead of away from it).
If that's still visible in the fresh session, it's a separate bug in the
measured system's blade-rotation logic
(`SpecialWorkHardwareRenderer.CalculateBladeOpenRotation`/`CreatePointBlade`),
not something this turn's fix touches.

Asked the user to check two things in the fresh session (Nove's blade
specifically, and a plain mixed switch outside the 14-name list to see the
`aThirdRails.right` fix's actual effect) - no answer yet, don't assume
either way until they report back.

## Standing rules

- Do not trust `Player.log`/exports unless the session is confirmed fresh
  (one engine-init + one mod-load line, AND timing after the last deploy -
  a stale single-session log also shows exactly one of each, so timing
  matters too, not just the count).
- Do not relax validation to hide geometry defects.
- Do not patch all four originally-reported symptoms together - map each to
  a node/system/code path first.
- Always deploy with `-p:EnableModDeploy=true` AND confirm a genuine
  relaunch before trusting a live test.
- Don't assume a fix for one system (measured vs. plain pipeline) explains a
  symptom reported for a node that belongs to the other system - confirm
  which system a specific node/switch actually uses before crediting a fix.

## Next turn

Waiting on the user to report back on Nove's blade and a plain-switch
example. Once that's in: if Nove's blade issue persists, investigate
`SpecialWorkHardwareRenderer.CalculateBladeOpenRotation`/`CreatePointBlade`
for the measured narrow-branch preset specifically (Codex or Claude). Still
open from the broader investigation: `SCustom_ttpp`'s cut-source ambiguity,
double-frog mapping (`NCustom_fl15`, `NCustom_ltci`, `NCustom_fc97` render 3
frogs), and the unmapped "too many rails" symptom.

## Open questions / blockers

Waiting on user confirmation of Nove's blade orientation and a plain-switch
example before further code changes. Do not guess at a fix for either
without that.
