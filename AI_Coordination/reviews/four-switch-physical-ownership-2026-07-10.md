# Four-switch physical ownership review - 2026-07-10

Scope: `NCustom_vdlt`, `NDeHartPassing_wqbb`, `N178`, and
`NCustom_u6n0`. This review records the measured ownership evidence before
the combined implementation.

## vdlt WingB

The user rejected every replacement/offset construction and clarified that
the original `VeeFrog-0-WingB` position, length, side, and endpoints were
correct. Only its angle was wrong. Restore the ordinary Vee wing construction
and rigidly rotate that completed curve about its original head until its axis
matches the paired `standard-through:right` fixed rail. Do not translate,
reslice, extend, or shorten the wing.

## wqbb center narrow blade

The fresh runtime log reports three successful blade-distance candidates, but
the final plan renders only the two standard blades. The missing candidate is
the narrow switch group's right-side blade. `DeduplicateBlades` currently
discards any blade whose curve overlaps a previously accepted blade by more
than 0.2 m, without considering switch-group ownership. On wqbb the standard
and narrow candidates are intentionally close/overlapping physical blades, so
the narrow candidate is deleted. Deduplication must remain within one
`SwitchGroupId`; a standard-group blade cannot delete a narrow-group blade.

## N178 mirrored blades

The current accepted Vee is the measured pair
`standard-through:left x narrow-reversed:left`, but the selected
`DualGauge_NarrowBranch_Left` table plus shared-side filter leaves only
`movable=narrow-normal:left`. That matches the reported wrong mirror. For this
physical same-left Vee anatomy, the complementary assignment is the
`DualGauge_NarrowBranch_Right` pair: left movable on `narrow-reversed:left`
and right movable on `narrow-normal:right` (the shared standard-through
position). Both physical blades are required, so the one-blade shared-side
filter must not run for this anatomy. Crossing-frog narrow-branch layouts and
Nove's different Vee anatomy remain unchanged.

## u6n0 mirrored double frog

The accepted synthetic double frog is
`standard-reversed:left x narrow-normal:left`, with suppressions
`standard-reversed:left 84.158-87.158` and
`narrow-normal:left 84.146-87.146`. This is the left-side mirror of the
already proven fc97 cutter case. The existing physical narrow-through cutter
is incorrectly gated to `narrow-normal:right`, so u6n0 falls back to two
semantic flange-guide half-plane cuts. Use the actual paired physical rail as
the cutter on either side, at `RailHeadWidth + FlangewayWidth`: narrow-normal
cuts the standard-through frog and standard-reversed cuts the narrow-reversed
frog. The adjustment rebuild path must reproduce the same cutter metadata.

## Implementation and build

Implemented the four anatomy-scoped changes described above. wqbb blade
deduplication now compares overlap only inside the same switch group. N178's
same-left accepted Vee mirrors the selected table's side assignments to the
`DualGauge_NarrowBranch_Right` physical pairing and retains both complementary
blades. The both-diverge physical narrow-through cutter is now side-neutral,
and the narrow-reversed frog has the symmetric standard-reversed physical
cutter with matching adjustment metadata.

The combined project build and deploy completed with 0 warnings and 0 errors.
Built and deployed DLLs both have timestamp 2026-07-10 06:43:44, size 749,056
bytes, and SHA-256
`FF1FEC03B1C2BD4D5D5B5159FB96702240C4E4E8CDE89C0842FD39855F16352A`.
No game process was launched or controlled. A full Railroader process restart
is required before live verification.

## First live blade verification rejected

The restart at approximately 02:37 rejected both initial blade corrections.
Fresh runtime evidence explains why:

- wqbb did generate three blades, but the added narrow blade was
  `narrow-reversed:right` and its tip exactly coincided with the standard-right
  blade. The measured `standard-reversed:right x narrow-normal:left` frog is
  the real ownership cue: `narrow-normal:left` is stock and
  `narrow-reversed:left` is the missing center movable blade.
- N178 remained at one blade because the decisive prototype intersection is
  `narrow-normal:right x narrow-reversed:left` (5.75 degrees). The first gate
  looked for the later remapped standard/narrow pair and therefore never ran.

The follow-up implementation now reads those exact pre-classification
physical pairs. wqbb uses the complementary standard/narrow frog as a fallback
stock-rail owner when the intersection has not yet been classified as a
crossing. N178 keys directly on its measured narrow-right-through /
narrow-left-diverge Vee pair, mirrors both truth-table side assignments, and
disables the one-blade filter for that anatomy.

## wqbb Fixed-15 correction

The next restart confirms N178 now renders both requested blades. wqbb still
places its third blade at the far-right stock rail. The live plan anatomy maps
`Fixed-15` to `narrow-reversed:left`, while the blade log shows the generic
crossing lookup selected `narrow-reversed:right` as stock and therefore built
`narrow-normal:right` movable at exactly the standard-right blade position.

For both-diverge layouts the physical narrow blade belongs opposite the shared
rail side. Prefer a classified crossing owner only when its narrow rail is on
that opposite side; otherwise use the complementary-state standard/narrow Vee
on the opposite side. wqbb has shared side Right, so its measured
`standard-reversed:right x narrow-normal:left` Vee yields
`stock=narrow-normal:left`, `movable=narrow-reversed:left`, continuing into
Fixed-15. Existing fc97/p997 classified owners already lie opposite their
shared sides and remain unchanged.

The user corrected the final ownership wording: Fixed-15 is the stock rail,
not the movable rail. Therefore wqbb must preserve the classified crossing's
`narrow-reversed` stock route while correcting only its bad Right side label
to Left. The resulting assignment is `movable=narrow-normal:left` closing
against `stock=narrow-reversed:left` / Fixed-15. The earlier fallback that
treated `narrow-normal:left` as stock was rejected.

Fresh runtime evidence after the next rebuild still showed the inverse result,
because the generic first-crossing lookup changed which narrow route it
returned. The stable anatomy is the complete outer Vee pair plus shared side:
`standard-reversed:right x narrow-normal:left`, shared side Right. This now
explicitly resolves `stock=narrow-reversed:left` (Fixed-15) and
`movable=narrow-normal:left`, independent of intersection enumeration order.

The same screenshot also exposes the swapped-state double-frog cutter case.
fc97/u6n0 use `standard-reversed x narrow-normal`; wqbb uses
`standard-normal:left x narrow-reversed:left`. The physical full-width cutter
gate now accepts either complementary normal/reversed pairing on the same
physical side, for both the standard and narrow frog-point pieces. Adjustment
rebuild metadata uses the same generalized gate.

## Npv2/u6n0 fourth-blade regression

Fresh runtime coordinates confirm the fourth blade on both layouts is a true
cross-gauge duplicate. On u6n0 the standard-right and narrow-right blade heads
are both `(1805.19, 587.44, 1185.19)`; Npv2 likewise has identical
standard-right/narrow-right heads `(1835.07, 586.02, 1057.03)`. Preserving
overlapping blades across different switch groups therefore retained a fourth
blade and allowed its suppression to interfere with the required standard
right diverging rail.

Now that wqbb's intended narrow blade is correctly assigned to the distinct
center rail (its head is about 0.51 m from the standard-left head, far outside
the 0.085 m overlap corridor), the cross-group exception is no longer needed.
Restore global geometric blade deduplication: enumeration keeps the standard
right blade first and removes only the coincident narrow-right duplicate.

## N178 Fixed-1 through Blade-1

The user identified the remaining objects directly. `Fixed-1` is
`standard-through:left`; it is the unwanted rail rendering through Blade-1.
`Fixed-8` is `narrow-reversed:right`; it is the dedicated diverging stock rail
that Blade-1 (`narrow-normal:right`) closes against and it must remain
continuous to the switch end.

The general narrow-branch stock-corridor rule preserved any route-derived rail
that overlapped the stock corridor. Near N178's point, Fixed-1 overlaps both
the stock and blade corridors, so that broad exception incorrectly preserved
it under Blade-1. For the measured right-through/right-diverging blade anatomy,
stock ownership is now exact: only `narrow-reversed:right` is protected.
Other overlapping rails, including `standard-through:left`, receive the normal
blade-corridor ownership cut. Validation uses the same exact-stock rule.

Live verification shows the ownership is now correct but Fixed-1 ends slightly
short. `FindCurveOverlaps` expands both ends of every detected interval by one
`BladeSampleSpacing` (0.10 m). The visible Fixed-1 gap matches that conservative
padding. For N178 Blade-1's `standard-through:left` ownership cut only, remove
the 0.10 m pad from the Fixed-1 boundary (`cutStart += 0.10`). Keep the actual
overlap suppressed, and leave the other boundary and Fixed-8 untouched.

Fixed-8 was separately shortened by its `shared duplicate` suppression, which
extended from curve start through 26.388 m even though Blade-1's stock-tip
projection is roughly 23.25 m. When a shared interval marks the exact dedicated
stock (`narrow-reversed:right`) as the loser, trim that suppression to the
Blade-1 tip. The duplicate owner remains suppressed before the point, while
Fixed-8 now starts at the blade tip and runs continuously to the switch end.
