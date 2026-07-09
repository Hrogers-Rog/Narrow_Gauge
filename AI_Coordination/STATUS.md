# Coordination Status

Last updated by: Codex - 2026-07-09 19:34 EDT

## Critical testing constraints

- `NarrowGaugeMod.dll` is loaded once at full Railroader process startup. A
  save reload does not load a newly deployed DLL; every live check below needs
  a full game quit and restart.
- The user retired the automated Railroader/TestBridge pipeline. Do not launch
  or drive Railroader. Build/deploy is allowed; live verification is manual by
  the user.

## Current phase: G832 blade pair and overlaid third rail fixed, awaiting manual verification

The user's current live report is specific: `NCustom_g832` now renders the
correct left blade and frog hardware, but lacks the right blade, while an
ordinary full-length narrow-gauge left through rail renders over and clips
through that correct measured hardware.

The fresh `Player.log` (2026-07-09 19:28) provided direct evidence for both:

1. G832 selects the two-entry `DualGauge_NarrowBranch_Left` blade truth table,
   but the crossing-frog correction rewrites the original right blade onto the
   left pairing and the one-blade shared-side filter skips the table's other
   entry. Final summary: `valid=True ... blades=1`.
2. G832 emits ownership claims on its authored dual-gauge through segments
   (`SCustom_snvo`, `SCustom_6wx3`) only for `standard-through` rails. The
   corresponding narrow routes traverse deterministic ghost ids
   `fuse-ng:s:<source>`, so the source-route ownership filter excludes their
   work intervals and leaves the ordinary third rail uncut over the measured
   assembly.

Implemented two general fixes:

- Truth-matched narrow-branch layouts with an accepted standard x narrow
  crossing retain the truth table's complementary left+right blade pair and do
  not apply the single-crossing-rail rewrite to both entries. Simple no-crossing
  narrow-branch layouts (`N178`/`Nove`) retain their existing shared-side
  one-blade rule. `NCustom_7n90`'s measured fallback path is unchanged.
- `SpecialWorkHardwareRenderer.OwnershipCuts` treats an authored dual-gauge
  source id and `fuse-ng:s:<source>` as the same physical source corridor for
  route eligibility. This admits the narrow-through interval needed to clip
  the third rail while preserving the source-route boundary filter.

Full investigation: `reviews/g832-blade-and-through-rail-2026-07-09.md`.

Built and deployed against the real Railroader install: 0 warnings, 0 errors.
The deployed DLL timestamp is 2026-07-09 19:34:25. No game process was launched
or controlled.

## Next turn

Next: Claude review, after the user's manual verification.

1. Fully quit and restart Railroader, load the save, and inspect G832. Expected:
   two blades, no full-length third rail over the left blade/frogs.
2. Check `Player.log`: G832 should report `blades=2`; its through source
   segments should gain `narrow-normal` ownership claims/cuts where the third
   rail overlaps the measured work interval.
3. Spot-check `NCustom_vdlt`, the mirror crossing anatomy; it should also have
   two blades. `N178` and `Nove` should remain at one blade. `NCustom_7n90`
   should remain unchanged.
4. Claude should read the actual diff plus the new review file and agree or
   raise a disagreement under the coordination protocol.

## Open questions / blockers

- Manual live verification is required; static build cannot prove visual rail
  clipping or blade animation.
- `NCustom_ltci` / `SCustom_ttpp` neighboring ownership overlap remains open
  and is not addressed by this physical ghost/source counterpart fix.
