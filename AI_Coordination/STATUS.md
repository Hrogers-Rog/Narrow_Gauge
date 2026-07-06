# Coordination Status

Last updated by: Claude - 2026-07-06

## Current phase: fixing broken special-work switches

User report: narrow-gauge plain turnouts mostly work in-game; all 14
currently-measured dual-gauge special-work switches were broken in the live
2026-07-05/06 session (`Special-work analysis: objects=14, invalid=14`), with
a visible symptom of a disconnected vertical rail stub floating above
otherwise normal-looking track. `Fuse_geometry_engine` is not the vehicle for
this fix; this repo is being fixed in place. Running autonomously per user
instruction: Claude invokes Codex non-interactively (`codex exec`) between
its own turns, alternating without the user relaying messages, until the
backlog is done or something needs the user (a disagreement, or in-game
verification).

Full evidence and root-cause notes:
`reviews/switch-validation-failures-2026-07-05.md`.

## Switch-fix backlog

1. **`dual.split-standard-narrow` derives zero blades** - code fix landed by
   Codex, reviewed and agreed by Claude (read the actual diff, not just the
   summary). Build succeeds. Needs live in-game verification: `Nove` and
   `NCustom_7n90` should report `valid=True`, and `Player.log` should no
   longer contain the captured `SwitchGeometry.Calculate` failure for
   `fuse-ng:n:Nove`.
2. **`dual.both-diverge` missing `SharedDuplicate` suppression** - code fix
   landed by Claude this turn. Root cause: `RailParticipatesInAcceptedFrog`
   skipped cutting a shared-duplicate interval if the rail participated in
   an accepted frog *anywhere on its length*, not specifically near that
   interval, so unrelated duplicates went uncut. Replaced with an
   interval-scoped check (`RailParticipatesInAcceptedFrogNearInterval`) at
   all 4 call sites. Build succeeds (0 warnings/errors). Needs live
   verification: `NCustom_l4a4`, `NCustom_ltci`, `NCustom_p997`,
   `NCustom_u6n0`, `NDeHartPassing_wqbb`, and `N178` should report
   `valid=True` with no "missing required suppressed interval" or "still
   renders" issues in a fresh `Player.log`.
3. **`dual.standard-branch-joins-main` never attempted** - still open. Both
   instances (`NCustom_fl15`, `NDeHartPassing_33d6`) fall back before
   measured special-work validation ("connects mixed gauge segments; leaving
   its visuals standard"). Likely a classification/discovery gap (why does
   `customAllowed` end up false / why is this preset never reached?), not a
   geometry bug - investigate `SpecialWorkRuntimeDiscovery.cs` and whatever
   sets `customAllowed`.
4. Blade under-build (`N178`, `NCustom_vdlt`, `NCustom_fc97`) and frog
   guard-rail/approach-section gaps (`NCustom_fc97`, `Npv2`) - still open.
   Re-check `N178` after item 2's fix before assuming its blade-count issue
   is unrelated - it had *both* a "still renders" duplicate issue and a
   blade-count issue, so re-verify which (if either) remains.
5. `NCustom_g832` rail-role mismatch - still open, single node, lowest
   leverage.

Do not treat `docs/special-work-turnout-combo-status.md`'s `DONE` markings as
current truth; update it only after live re-verification.

## Verification

Neither agent can launch Railroader from this session - build success plus
static reasoning about the truth-table/validator code is the ceiling for
unattended verification. For each fix, confirm:
`dotnet build NarrowGaugeMod.csproj -p:RailroaderDir="C:\Steam\steamapps\common\Railroader"`
succeeds, and record in `LOG.md` exactly what was and wasn't verified this
way (Codex made a build-command correction turn 00:18 after initially
misreporting which command it ran - be precise here, don't round up).

Batch the actual in-game re-test: ask the user for a fresh `Player.log` once
several backlog items are code-complete, rather than interrupting the loop
after every single one.

## Still open

Items 3, 4, 5 above. Live in-game verification for items 1 and 2.

## Next turn

Codex - pick up backlog item 3 (`dual.standard-branch-joins-main` never
attempted). Read the tail of `LOG.md` first, especially the two most recent
Claude entries reviewing/landing items 1 and 2.

## Open questions / blockers

None yet requiring the user. Will ask for a fresh `Player.log` once the
backlog (or a meaningful chunk of it) is code-complete.
