using System;
using System.Collections.Generic;
using System.Linq;

namespace NarrowGaugeMod
{
    internal static class SpecialWorkPresetIds
    {
        public const string StandardLeft = "turnout.standard.left";
        public const string StandardRight = "turnout.standard.right";
        public const string StandardWye = "turnout.standard.wye";
        public const string NarrowLeft = "turnout.narrow.left";
        public const string NarrowRight = "turnout.narrow.right";
        public const string NarrowWye = "turnout.narrow.wye";
        public const string DualNarrowBranch = "dual.narrow-branch-joins-main";
        public const string DualStandardBranch = "dual.standard-branch-joins-main";
        public const string DualBothDiverge = "dual.both-diverge";
        public const string DualSplit = "dual.split-standard-narrow";
        public const string DualSharedRailFlip = "dual.shared-rail-flip";
        public const string CrossingDiamond = "crossing.diamond";
        public const string CrossingArbitrary = "crossing.arbitrary-angle";
        public const string CrossingNinety = "crossing.90-degree";
        public const string SlipSingle = "slip.single";
        public const string SlipDouble = "slip.double";
        public const string StubLeft = "stub.left";
        public const string StubRight = "stub.right";
        public const string StubThreeWay = "stub.three-way";
        public const string ThreeWayStandard = "three-way.standard";
        public const string ThreeWayNarrow = "three-way.narrow";
        public const string ThreeWayDual = "three-way.dual";
    }

    internal static class SpecialWorkPresetCatalog
    {
        private static readonly Dictionary<string, SpecialWorkPresetDefinition> ById =
            BuildCatalog();

        public static IReadOnlyCollection<SpecialWorkPresetDefinition> Presets =>
            ById.Values.Distinct().OrderBy(preset => preset.Id, StringComparer.OrdinalIgnoreCase).ToArray();

        public static bool TryGet(string id, out SpecialWorkPresetDefinition preset)
        {
            return ById.TryGetValue(id ?? string.Empty, out preset);
        }

        public static SpecialWorkPresetDefinition Get(string id)
        {
            if (!TryGet(id, out SpecialWorkPresetDefinition preset))
            {
                throw new KeyNotFoundException($"Unknown special-work preset '{id}'.");
            }

            return preset;
        }

        public static IReadOnlyList<string> ValidateCatalog()
        {
            var issues = new List<string>();
            foreach (SpecialWorkPresetDefinition preset in Presets)
            {
                if (string.IsNullOrWhiteSpace(preset.Id))
                {
                    issues.Add("A special-work preset has an empty ID.");
                }

                if (preset.NativeSwitchNodes < 0 || preset.LogicalRoutes < 1)
                {
                    issues.Add($"Preset '{preset.Id}' has invalid topology counts.");
                }

                if (preset.Geometry.ExpectedMovableAssemblies > 0
                    && preset.NativeSwitchNodes == 0)
                {
                    issues.Add($"Preset '{preset.Id}' has movable assemblies but no native switch nodes.");
                }

                if (preset.Topology == NativeTopologyRecipe.FixedTransition
                    && preset.AnchorKind != SpecialWorkAnchorKind.Segment)
                {
                    issues.Add($"Fixed transition preset '{preset.Id}' must use a segment anchor.");
                }
            }

            return issues;
        }

        private static Dictionary<string, SpecialWorkPresetDefinition> BuildCatalog()
        {
            var presets = new[]
            {
                Turnout(SpecialWorkPresetIds.StandardLeft, "Standard Left Turnout", GaugeAvailability.Standard),
                Turnout(SpecialWorkPresetIds.StandardRight, "Standard Right Turnout", GaugeAvailability.Standard),
                Turnout(SpecialWorkPresetIds.StandardWye, "Standard Wye Turnout", GaugeAvailability.Standard),
                Turnout(SpecialWorkPresetIds.NarrowLeft, "Narrow Left Turnout", GaugeAvailability.Narrow),
                Turnout(SpecialWorkPresetIds.NarrowRight, "Narrow Right Turnout", GaugeAvailability.Narrow),
                Turnout(SpecialWorkPresetIds.NarrowWye, "Narrow Wye Turnout", GaugeAvailability.Narrow),
                DualBranch(
                    SpecialWorkPresetIds.DualNarrowBranch,
                    "Narrow Branch Joins/Leaves Dual Main",
                    "dual.narrow-diverges"),
                DualBranch(
                    SpecialWorkPresetIds.DualStandardBranch,
                    "Standard Branch Joins/Leaves Dual Main",
                    "dual.standard-diverges"),
                new SpecialWorkPresetDefinition(
                    SpecialWorkPresetIds.DualBothDiverge,
                    "Dual Gauge Both Families Diverge",
                    SpecialWorkCategory.DualGauge,
                    NativeTopologyRecipe.TwoFamilyBinarySwitches,
                    2,
                    4,
                    GaugeAvailability.Dual,
                    true,
                    Expect(2, null, 2, null, SharedRailRequirement.RouteAndGaugeShared, 2)),
                new SpecialWorkPresetDefinition(
                    SpecialWorkPresetIds.DualSplit,
                    "Dual Splits to Standard and Narrow",
                    SpecialWorkCategory.DualGauge,
                    NativeTopologyRecipe.BinarySwitch,
                    1,
                    3,
                    GaugeAvailability.Dual,
                    true,
                    Expect(0, null, 0, null, SharedRailRequirement.GaugeShared, 1)),
                new SpecialWorkPresetDefinition(
                    SpecialWorkPresetIds.DualSharedRailFlip,
                    "Dual-Gauge Shared-Rail Side Transition",
                    SpecialWorkCategory.Transition,
                    NativeTopologyRecipe.FixedTransition,
                    0,
                    2,
                    GaugeAvailability.Dual,
                    true,
                    SpecialWorkAnchorKind.Segment,
                    Expect(2, 2, 4, 4, SharedRailRequirement.GaugeShared, 0)),
                Crossing(SpecialWorkPresetIds.CrossingDiamond, "Diamond Crossing"),
                Crossing(SpecialWorkPresetIds.CrossingArbitrary, "Arbitrary-Angle Crossing"),
                Crossing(SpecialWorkPresetIds.CrossingNinety, "90-Degree Crossing"),
                new SpecialWorkPresetDefinition(
                    SpecialWorkPresetIds.SlipSingle,
                    "Single Slip",
                    SpecialWorkCategory.Slip,
                    NativeTopologyRecipe.SingleSlip,
                    2,
                    3,
                    GaugeAvailability.Standard,
                    false,
                    Expect(4, null, 4, null, SharedRailRequirement.RouteShared, 2)),
                new SpecialWorkPresetDefinition(
                    SpecialWorkPresetIds.SlipDouble,
                    "Double Slip",
                    SpecialWorkCategory.Slip,
                    NativeTopologyRecipe.DoubleSlip,
                    4,
                    4,
                    GaugeAvailability.Standard,
                    false,
                    Expect(4, null, 6, null, SharedRailRequirement.RouteShared, 4)),
                Stub(SpecialWorkPresetIds.StubLeft, "Left Stub Turnout", 1, 2, 1),
                Stub(SpecialWorkPresetIds.StubRight, "Right Stub Turnout", 1, 2, 1),
                Stub(SpecialWorkPresetIds.StubThreeWay, "Three-Way Stub Turnout", 2, 3, 2),
                ThreeWay(SpecialWorkPresetIds.ThreeWayStandard, "Standard Three-Way", GaugeAvailability.Standard),
                ThreeWay(SpecialWorkPresetIds.ThreeWayNarrow, "Narrow Three-Way", GaugeAvailability.Narrow),
                new SpecialWorkPresetDefinition(
                    SpecialWorkPresetIds.ThreeWayDual,
                    "Dual-Gauge Three-Way",
                    SpecialWorkCategory.ThreeWay,
                    NativeTopologyRecipe.TwoFamilyStagedThreeWay,
                    4,
                    6,
                    GaugeAvailability.Dual,
                    true,
                    Expect(4, null, 4, null, SharedRailRequirement.RouteAndGaugeShared, 4))
            };

            var result = new Dictionary<string, SpecialWorkPresetDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (SpecialWorkPresetDefinition preset in presets)
            {
                result.Add(preset.Id, preset);
                foreach (string alias in preset.Aliases.Where(alias => !string.IsNullOrWhiteSpace(alias)))
                {
                    result.Add(alias, preset);
                }
            }

            return result;
        }

        private static SpecialWorkPresetDefinition Turnout(
            string id,
            string displayName,
            GaugeAvailability family)
        {
            return new SpecialWorkPresetDefinition(
                id,
                displayName,
                SpecialWorkCategory.Turnout,
                NativeTopologyRecipe.BinarySwitch,
                1,
                2,
                family,
                false,
                Expect(1, 1, 2, 2, SharedRailRequirement.RouteShared, 1));
        }

        private static SpecialWorkPresetDefinition DualBranch(
            string id,
            string displayName,
            params string[] aliases)
        {
            return new SpecialWorkPresetDefinition(
                id,
                displayName,
                SpecialWorkCategory.DualGauge,
                NativeTopologyRecipe.BinarySwitch,
                1,
                3,
                GaugeAvailability.Dual,
                true,
                Expect(1, null, 2, null, SharedRailRequirement.RouteAndGaugeShared, 2),
                aliases);
        }

        private static SpecialWorkPresetDefinition Crossing(string id, string displayName)
        {
            return new SpecialWorkPresetDefinition(
                id,
                displayName,
                SpecialWorkCategory.Crossing,
                NativeTopologyRecipe.PlainCrossing,
                0,
                2,
                GaugeAvailability.Standard,
                false,
                Expect(4, 4, 4, null, SharedRailRequirement.None, 0));
        }

        private static SpecialWorkPresetDefinition Stub(
            string id,
            string displayName,
            int nodes,
            int routes,
            int frogs)
        {
            return new SpecialWorkPresetDefinition(
                id,
                displayName,
                SpecialWorkCategory.StubSwitch,
                nodes == 1 ? NativeTopologyRecipe.BinarySwitch : NativeTopologyRecipe.StagedThreeWay,
                nodes,
                routes,
                GaugeAvailability.Standard,
                false,
                Expect(frogs, frogs, frogs * 2, null, SharedRailRequirement.RouteShared, nodes));
        }

        private static SpecialWorkPresetDefinition ThreeWay(
            string id,
            string displayName,
            GaugeAvailability family)
        {
            return new SpecialWorkPresetDefinition(
                id,
                displayName,
                SpecialWorkCategory.ThreeWay,
                NativeTopologyRecipe.StagedThreeWay,
                2,
                3,
                family,
                false,
                Expect(2, 2, 4, null, SharedRailRequirement.RouteShared, 2));
        }

        private static GeometryExpectation Expect(
            int frogMinimum,
            int? frogMaximum,
            int guardMinimum,
            int? guardMaximum,
            SharedRailRequirement shared,
            int movable)
        {
            return new GeometryExpectation(
                new CountRange(frogMinimum, frogMaximum),
                new CountRange(guardMinimum, guardMaximum),
                shared,
                deriveFrogsFromIntersections: true,
                movable);
        }
    }
}
