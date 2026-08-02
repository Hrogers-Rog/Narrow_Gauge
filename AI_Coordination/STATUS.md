# Coordination Status

Last updated by: Codex - 2026-08-02 08:27

## Current phase: EF&A acute V frogs use the base-game switch flangeway

The user's current close-up shows both wing-rail slots at an acute V frog are
wider than those on a native Railroader switch. The decompiled authoritative
implementation in `Assembly-CSharp/Track/SwitchGeometry.cs` uses a literal
0.100 m lateral center separation between each frog point and its corresponding
wing/point curve. `Gauge.Standard.HeadWidth` is 0.076 m, so this produces an
approximately 0.024 m visible railhead-to-railhead slot.

`CreateVeeFrogAssembly` already defaults to that same 0.100 m value for normal
switches. Only `CreateDiamondAcuteFrogAssembly` overrode it with 0.126 m
(`RailHeadWidth + FlangewayWidth`), opening the pictured diamond slot 26 mm
wider at the centerline level than the base-game construction. The diamond now
uses a named `BaseGameSwitchFrogRailSeparation = 0.1f` constant for the call and
both shared V-frog defaults. Its diagnostic now reports
`wingSeparation=0.100 visibleFlangeway=0.024`.

This change affects only the lateral set-out of the two acute wing rails. It
does not alter the inward V-frog orientation, the exact +0.500-degree acute
opening, the hardened wing joint, or any K-frog stock/guard geometry.

The current working tree also contains Claude's accumulated, previously
deployed EF&A K-frog and render-frame corrections documented in `LOG.md`:
profile-side-preserving hard knuckles, inner-wing-referenced K guards, the
guard half-turn, guard/stock tangent locking, and 0.126 m K flangeway set-out.
They build together with this V-frog correction.

`dotnet build .\NarrowGaugeMod.csproj /p:EnableModDeploy=true` succeeded with
0 warnings and 0 errors. Output and deployed DLL SHA-256 hashes both equal
`E5EA223758EE239BC3B2D9BD243699FA33AB5F62F9421DF26BA52A83D6D9A5BF`.

The current `Player.log` is not a verification run. `Railroader.exe` started
at 07:55:27, before the corrected DLL was deployed at 08:25:09, and its two
acute records still report the old `wingSeparation=0.126 flangeway=0.050`
format. This proves the running process still holds the preceding assembly in
memory. Codex did not close it because doing so could discard the user's live
session.

## Next turn

User: fully restart Railroader and inspect both acute V frogs. Confirm their
wing-to-frog slots now match a base-game switch. The fresh log should contain
two `[DiamondAcuteFrog]` records with
`wingSeparation=0.100 visibleFlangeway=0.024 wingHardKink=1`.

Claude: review the 0.100 m base-game calibration recorded in
`proposals/standard-diamond-crossing.md` and the accumulated implementation
diff now that the turn collision has ended.

## Open questions / blockers

- The requested V-frog clearance has source/decompile/build evidence but still
  requires a full-restart in-game visual check before it is considered proven.
- The second discovered diamond `crossing:SDillsYard2_rdhn:Setp` (18.68 deg)
  still derives only 3 of 4 frogs and falls back to generic crossing points.
- Automatic discovery intentionally rejects ambiguous compound zones. An
  explicit `segmentA`/`segmentB` override remains a possible later feature.
- Prior Nove/7n90, vdlt/g832, culling, and related manual-verification items
  remain recorded in `LOG.md` and were not changed by this calibration.
