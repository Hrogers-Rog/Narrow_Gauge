# Coordination Status

Last updated by: Codex - 2026-07-09 21:24 EDT

## Critical testing constraints

- `NarrowGaugeMod.dll` is loaded only at full Railroader process startup. A
  save reload does not load this deployment.
- The user retired the automated Railroader/TestBridge pipeline. Do not launch
  or drive Railroader. Build/deploy is allowed; live verification is manual.

## Current phase: inward-facing both-diverge crossing points deployed

The measured-hand build corrected the continuous handoff/guard path, but the
user's new fc97 screenshot (`211109`) shows one complementary crossing point
still failing to project inward into the frog. The fresh 21:13 runtime log
confirms fc97 remains a valid 18-fixed/3-frog plan, ruling out a missing plan
piece.

The complementary flangeway-cut point objects retained ordinary running-rail
hands. Ordinary rail profiles project outside their gauge-face curves; a frog
point made from that same path must project to the opposite, inward side. The
deployed renderer now reverses the profile hand only for both-diverge:

- `StandardThroughFrog`;
- `NarrowThroughFrog`;
- `NarrowReversedFrog`.

The continuous stock handoff, ordinary outside stock rails, curve points,
rotations, spans, flangeway centers, and keep-side logic are unchanged. Each
point reverses its own measured hand, so mirrored and reversed route curves do
not require a node-id or fixed-left/fixed-right case.

Built and deployed: 0 warnings, 0 errors. Built/deployed DLL timestamp
2026-07-09 21:24:00, size 737,792 bytes, SHA-256
`BD512634D9931B3288F773120328D0F32467021E031D60DFA14816FB1B411078` on both
copies. No game process was launched or controlled. Full evidence:
`reviews/frog-direction-gap-frame-investigation-2026-07-09.md`.

## Next turn

1. Fully quit and restart Railroader; a save reload is insufficient.
2. At fc97, repeat the `211109` view. Both complementary point heads should
   now project inward into the crossing instead of remaining on the outside
   gauge-face side.
3. Confirm the continuous handoff and guard retain their corrected positions;
   this change does not touch either path.
4. Spot-check a mirror-hand both-diverge switch such as p997/ltci and a second
   right-side switch such as l4a4.
5. Confirm original point spans and flangeway cuts remain intact.

## Open questions / blockers

- Manual live verification is required; the screenshots prove the intended
  side but static compilation cannot prove final mesh clipping.
- Other narrow-branch issues (`N178`, `vdlt`, `7n90`) remain separate from
  this both-diverge flangeway correction.
- `NCustom_ltci` / `SCustom_ttpp` neighboring ownership overlap remains open.
