using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Track;
using UnityEngine;

namespace NarrowGaugeMod
{
    internal static class SpecialWorkGeometryBuilder
    {
        private const float WingTipClearance = 0.06f;
        private const float MinimumPieceLength = 0.06f;
        private const float BladeSampleSpacing = 0.1f;
        private const float GuardCollisionTolerance = 0.09f;
        private const float HardwareAttachmentTolerance = 0.09f;
        private const float CutCoverageTolerance = 0.02f;
        private const float WorkEnvelopeMargin = 1.5f;
        private const float WorkEventAssociationTolerance = 1.55f;
        private const float MaximumBladeTipSeparation = 0.45f;
        private const float OwnershipCorridorTolerance = 0.08f;
        private const float OwnershipSampleSpacing = 0.1f;
        private const float MinimumOwnershipIntervalLength = 0.18f;

        private static readonly SpecialWorkGeometryParameters Parameters =
            new SpecialWorkGeometryParameters(
                railHeadWidth: Gauge.Standard.HeadWidth,
                flangewayWidth: 0.05f,
                minimumFrogSetback: 0.16f,
                maximumFrogSetback: 2.5f,
                guardCenterOffset: Gauge.Standard.HeadWidth + 0.05f,
                guardLeadLength: 0.9f,
                guardTrailLength: 0.9f,
                bladeDivergenceThreshold: 0.045f,
                bladeRootSeparation: 0.18f,
                maximumBladeLength: 4f);

        public static SpecialWorkMeshPlan Build(
            Graph graph,
            SpecialWorkDefinition definition,
            IReadOnlyList<WheelPath> wheelPaths,
            IReadOnlyList<RailCenterline> rails,
            RailIntersectionPrototypeResult prototype)
        {
            SpecialWorkAnatomyPlan? anatomy =
                SpecialWorkAnatomyCompiler.TryCompile(
                    graph,
                    definition,
                    wheelPaths,
                    rails,
                    out SpecialWorkAnatomyPlan compiledAnatomy)
                    ? compiledAnatomy
                    : null;
            var cuts = new List<RailCut>();
            AssignInitialRailRoles(definition, rails, anatomy);
            var frogs = BuildFrogs(prototype)
                .Where(frog => IsAcceptedFrogCandidate(frog))
                .ToArray();

            AddFrogCuts(rails, frogs, cuts);
            SwitchBladePlan[] switchBlades =
                BuildSwitchBlades(graph, definition, wheelPaths, rails, frogs, cuts, anatomy).ToArray();
            switchBlades = ConsolidateDualBothDivergeBlades(definition, switchBlades, cuts);
            AddStockBladeClearanceCuts(definition, rails, switchBlades, cuts);
            RailWorkInterval[] workIntervals =
                BuildWorkIntervals(definition, rails, prototype.Shared, frogs, switchBlades).ToArray();
            AddSharedRailCuts(
                definition,
                rails,
                prototype.Shared,
                workIntervals,
                frogs,
                switchBlades,
                cuts);
            SuppressDualBothDivergeDuplicateRail(definition, rails, workIntervals, frogs, cuts);
            AssignRailRoles(definition, rails, prototype.Shared, cuts, frogs, switchBlades, anatomy);
            RailOwnershipInterval[] ownershipIntervals =
                BuildRailOwnershipIntervals(
                    definition,
                    rails,
                    prototype.Shared,
                    workIntervals,
                    cuts,
                    switchBlades).ToArray();
            if (!IsDualBothDivergePreset(definition))
            {
                AddOwnershipConflictCuts(rails, ownershipIntervals, switchBlades, cuts);
            }
            RailPiece[] fixedRails =
                BuildFixedRailPieces(
                    rails,
                    prototype.Shared,
                    ownershipIntervals,
                    cuts,
                    switchBlades).ToArray();
            WingRailPlan[] wings =
                BuildWingRails(frogs, fixedRails, workIntervals).ToArray();
            RailPiece[] frogPieces = BuildFrogPieces(frogs).ToArray();
            GuardRailPlan[] guards =
                BuildGuardRails(definition, rails, frogs, switchBlades).ToArray();
            GeometryDebugLabel[] labels = BuildDebugLabels(
                rails,
                fixedRails,
                cuts,
                frogs,
                wings,
                guards,
                switchBlades).ToArray();
            string[] geometryIssues = Validate(
                definition,
                rails,
                fixedRails,
                workIntervals,
                cuts,
                frogs,
                wings,
                guards,
                switchBlades).ToArray();
            string[] truthIssues = SpecialWorkTruthTableValidator.Validate(
                definition,
                rails,
                prototype.Shared,
                fixedRails,
                workIntervals,
                cuts,
                frogs,
                wings,
                guards,
                switchBlades).ToArray();
            string[] issues = geometryIssues
                .Concat(truthIssues)
                .Concat(anatomy?.Issues ?? Array.Empty<string>())
                .ToArray();

            return new SpecialWorkMeshPlan(
                Parameters,
                workIntervals,
                ownershipIntervals,
                new RailOwnershipPlan(
                    rails,
                    rails.SelectMany(rail => rail.Sections),
                    rails.SelectMany(rail => rail.Suppressions)),
                fixedRails,
                cuts,
                frogs,
                frogPieces,
                wings,
                guards,
                switchBlades,
                labels,
                issues);
        }

        private static IEnumerable<FrogCandidate> BuildFrogs(
            RailIntersectionPrototypeResult prototype)
        {
            int index = 0;
            foreach (RailIntersection intersection in prototype.Intersections.Where(item =>
                item.Kind == RailIntersectionKind.VeeFrogCandidate
                || item.Kind == RailIntersectionKind.CrossingFrogCandidate))
            {
                Vector2 tangentA = intersection.TangentA2D.normalized;
                Vector2 tangentB = intersection.TangentB2D.normalized;
                if (Vector2.Dot(tangentA, tangentB) < 0f)
                {
                    tangentB = -tangentB;
                }

                Vector2 bisector = (tangentA + tangentB).normalized;
                if (bisector.sqrMagnitude <= 0.0001f)
                {
                    bisector = tangentA;
                }

                float halfAngle = Mathf.Max(
                    intersection.AcuteAngleDegrees * 0.5f * Mathf.Deg2Rad,
                    0.01f);
                float railHeadSetback =
                    Parameters.RailHeadWidth / Mathf.Tan(halfAngle);
                float flangewaySetback =
                    Parameters.FlangewayWidth / Mathf.Sin(halfAngle);
                float minimumWingSetback =
                    flangewaySetback + WingTipClearance + MinimumPieceLength;
                float cutHalfLength = Mathf.Clamp(
                    Mathf.Max(
                        railHeadSetback + Parameters.RailHeadWidth * 0.5f,
                        minimumWingSetback),
                    Parameters.MinimumFrogSetback,
                    Parameters.MaximumFrogSetback);
                float handednessSign =
                    Cross(intersection.TangentA2D, intersection.TangentB2D);

                Vector3 nose = prototype.Frame.RestoreDirection(bisector);
                yield return new FrogCandidate(
                    "frog:" + index++,
                    intersection,
                    nose,
                    -nose,
                    handednessSign >= 0f
                        ? FrogHandedness.Left
                        : FrogHandedness.Right,
                    railHeadSetback,
                    flangewaySetback,
                    cutHalfLength);
            }
        }

        private static void AddSharedRailCuts(
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<SharedRailInterval> shared,
            IReadOnlyList<RailWorkInterval> workIntervals,
            IReadOnlyList<FrogCandidate> frogs,
            IReadOnlyList<SwitchBladePlan> blades,
            ICollection<RailCut> cuts)
        {
            foreach (SharedRailInterval interval in shared)
            {
                RailCenterline? railA = rails.FirstOrDefault(rail => rail.Id == interval.RailAId);
                RailCenterline? railB = rails.FirstOrDefault(rail => rail.Id == interval.RailBId);
                if (railA == null || railB == null)
                {
                    continue;
                }

                RailCenterline loser = ChooseSharedOwner(railA, railB, blades) == railA
                    ? railB
                    : railA;
                if (IsDualBothDivergePreset(definition)
                    && RailParticipatesInAcceptedFrog(loser, frogs))
                {
                    continue;
                }

                RailWorkInterval? work = workIntervals.FirstOrDefault(item => item.Rail == loser);
                if (work == null)
                {
                    continue;
                }

                float start = loser.Curve.DistanceTo(interval.Start);
                float end = loser.Curve.DistanceTo(interval.End);
                float cutStart = Mathf.Max(Mathf.Min(start, end), work.StartDistance);
                float cutEnd = Mathf.Min(Mathf.Max(start, end), work.EndDistance);
                if (cutStart - work.StartDistance < MinimumOwnershipIntervalLength)
                {
                    cutStart = work.StartDistance;
                }

                if (work.EndDistance - cutEnd < MinimumOwnershipIntervalLength)
                {
                    cutEnd = work.EndDistance;
                }

                AddCut(
                    cuts,
                    loser,
                    cutStart,
                    cutEnd,
                    RailCutKind.SharedDuplicate,
                    $"shared:{interval.RailAId}:{interval.RailBId}");
            }
        }

        private static IEnumerable<RailWorkInterval> BuildWorkIntervals(
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<SharedRailInterval> shared,
            IReadOnlyList<FrogCandidate> frogs,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            var intervals = new List<RailWorkInterval>();
            foreach (RailCenterline rail in rails)
            {
                List<float> distances = BuildRailWorkDistances(rail, shared, frogs, blades)
                    .GroupBy(distance => Mathf.RoundToInt(distance * 100f))
                    .Select(group => group.First())
                    .ToList();
                if (distances.Count == 0)
                {
                    continue;
                }

                intervals.Add(new RailWorkInterval(
                    rail,
                    Mathf.Max(0f, distances.Min() - WorkEnvelopeMargin),
                    Mathf.Min(rail.Curve.Length, distances.Max() + WorkEnvelopeMargin)));
            }

            ExpandDualStandardBranchThroughIntervals(definition, rails, intervals);
            ExpandGaugeSeparationOppositeClosureInterval(
                definition,
                rails,
                blades,
                intervals);
            TrimGaugeSeparationStandardCrossingInterval(
                definition,
                rails,
                frogs,
                intervals);
            foreach (RailWorkInterval interval in intervals)
            {
                yield return interval;
            }
        }

        private static void TrimGaugeSeparationStandardCrossingInterval(
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<FrogCandidate> frogs,
            IList<RailWorkInterval> intervals)
        {
            if (!string.Equals(
                    definition.Preset.Id,
                    SpecialWorkPresetIds.DualSplit,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            (RailCenterline Rail, (float Start, float End)[] Spans) standardCrossing =
                rails
                    .Where(rail =>
                        rail.SourceRouteIds.Contains(
                            "standard-through",
                            StringComparer.OrdinalIgnoreCase))
                    .Select(rail => (
                        Rail: rail,
                        Spans: frogs
                            .Where(frog =>
                                frog.Intersection.RailA == rail
                                || frog.Intersection.RailB == rail)
                            .Select(frog =>
                            {
                                float center = frog.Intersection.RailA == rail
                                    ? frog.Intersection.DistanceA
                                    : frog.Intersection.DistanceB;
                                return (
                                    Mathf.Max(0f, center - frog.CutHalfLength),
                                    Mathf.Min(rail.Curve.Length, center + frog.CutHalfLength));
                            })
                            .ToArray()))
                    .OrderByDescending(candidate => candidate.Spans.Length)
                    .FirstOrDefault();
            (float Start, float End)[] frogSpans = standardCrossing.Spans;
            if (frogSpans.Length < 2)
            {
                return;
            }

            for (int index = 0; index < intervals.Count; index++)
            {
                if (intervals[index].Rail != standardCrossing.Rail)
                {
                    continue;
                }

                intervals[index] = new RailWorkInterval(
                    standardCrossing.Rail,
                    frogSpans.Min(span => span.Start),
                    frogSpans.Max(span => span.End));
                return;
            }
        }

        private static void ExpandGaugeSeparationOppositeClosureInterval(
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<SwitchBladePlan> blades,
            IList<RailWorkInterval> intervals)
        {
            if (!TryGetGaugeSeparationOppositeClosure(
                definition,
                rails,
                blades,
                out SwitchBladePlan blade,
                out RailCenterline closureRail))
            {
                return;
            }

            float tipDistance = closureRail.Curve.DistanceTo(blade.BladeCurve.Head.point);
            float rootDistance = closureRail.Curve.DistanceTo(blade.BladeCurve.Tail.point);
            float startDistance = Mathf.Min(tipDistance, rootDistance);
            float endDistance = Mathf.Max(tipDistance, rootDistance);
            int existingIndex = -1;
            for (int index = 0; index < intervals.Count; index++)
            {
                if (intervals[index].Rail != closureRail)
                {
                    continue;
                }

                existingIndex = index;
                startDistance = Mathf.Min(startDistance, intervals[index].StartDistance);
                endDistance = Mathf.Max(endDistance, intervals[index].EndDistance);
                break;
            }

            var expanded = new RailWorkInterval(
                closureRail,
                Mathf.Max(0f, startDistance),
                Mathf.Min(closureRail.Curve.Length, endDistance));
            if (existingIndex >= 0)
            {
                intervals[existingIndex] = expanded;
            }
            else
            {
                intervals.Add(expanded);
            }
        }

        private static void ExpandDualStandardBranchThroughIntervals(
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IList<RailWorkInterval> intervals)
        {
            if (!IsDualStandardBranchPreset(definition))
            {
                return;
            }

            Vector3[] standardThroughBounds = intervals
                .Where(interval => IsStandardDualThroughRail(interval.Rail))
                .SelectMany(interval => new[]
                {
                    interval.Rail.Curve.LinePointAtDistance(interval.StartDistance).point,
                    interval.Rail.Curve.LinePointAtDistance(interval.EndDistance).point
                })
                .ToArray();
            if (standardThroughBounds.Length == 0)
            {
                return;
            }

            foreach (RailCenterline rail in rails.Where(IsNarrowThroughRail))
            {
                List<float> distances = standardThroughBounds
                    .Select(rail.Curve.DistanceTo)
                    .ToList();
                int existingIndex = -1;
                for (int index = 0; index < intervals.Count; index++)
                {
                    if (intervals[index].Rail == rail)
                    {
                        existingIndex = index;
                        distances.Add(intervals[index].StartDistance);
                        distances.Add(intervals[index].EndDistance);
                        break;
                    }
                }

                if (distances.Count == 0)
                {
                    continue;
                }

                var expanded = new RailWorkInterval(
                    rail,
                    distances.Min(),
                    distances.Max());
                if (existingIndex >= 0)
                {
                    intervals[existingIndex] = expanded;
                }
                else
                {
                    intervals.Add(expanded);
                }
            }
        }

        private static bool IsStandardDualThroughRail(RailCenterline rail)
        {
            return rail.Family == GaugeGraphFamily.Standard
                && rail.SourceRouteIds.Any(routeId =>
                    routeId.StartsWith("standard-", StringComparison.OrdinalIgnoreCase))
                && IsDualPort(rail.StartPortId)
                && IsDualPort(rail.EndPortId);
        }

        private static bool IsNarrowThroughRail(RailCenterline rail)
        {
            return rail.Family == GaugeGraphFamily.Narrow
                && rail.SourceRouteIds.Contains("narrow-through", StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsDualPort(string portId)
        {
            return !string.IsNullOrWhiteSpace(portId)
                && portId.StartsWith("dual-", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<float> BuildRailWorkDistances(
            RailCenterline rail,
            IReadOnlyList<SharedRailInterval> shared,
            IReadOnlyList<FrogCandidate> frogs,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            foreach (SharedRailInterval interval in shared)
            {
                if (string.Equals(interval.RailAId, rail.Id, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(interval.RailBId, rail.Id, StringComparison.OrdinalIgnoreCase))
                {
                    yield return rail.Curve.DistanceTo(interval.Start);
                    yield return rail.Curve.DistanceTo(interval.End);
                }
            }

            foreach (FrogCandidate frog in frogs)
            {
                if (frog.Intersection.RailA == rail)
                {
                    yield return frog.Intersection.DistanceA;
                    continue;
                }

                if (frog.Intersection.RailB == rail)
                {
                    yield return frog.Intersection.DistanceB;
                    continue;
                }

                float distance = rail.Curve.DistanceTo(frog.Intersection.Position);
                Vector3 closest = rail.Curve.LinePointAtDistance(distance).point;
                if (HorizontalDistance(closest, frog.Intersection.Position)
                    <= WorkEventAssociationTolerance)
                {
                    yield return distance;
                }
            }

            foreach (SwitchBladePlan blade in blades)
            {
                if (blade.MovableRail == rail)
                {
                    yield return blade.TipDistance;
                    yield return blade.RootDistance;
                    continue;
                }

                if (blade.StockRail == rail)
                {
                    yield return rail.Curve.DistanceTo(blade.BladeCurve.Head.point);
                    yield return rail.Curve.DistanceTo(blade.BladeCurve.Tail.point);
                }
            }
        }

        private static void AddStockBladeClearanceCuts(
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IEnumerable<SwitchBladePlan> blades,
            ICollection<RailCut> cuts)
        {
            if (definition.Preset.Category != SpecialWorkCategory.DualGauge)
            {
                return;
            }

            foreach (SwitchBladePlan blade in blades)
            {
                if (!BladeRunsThroughCurve(blade.StockRail.Curve, blade.BladeCurve))
                {
                    continue;
                }

                bool preserveGaugeSeparationStock = string.Equals(
                    definition.Preset.Id,
                    SpecialWorkPresetIds.DualSplit,
                    StringComparison.OrdinalIgnoreCase);
                if (!preserveGaugeSeparationStock)
                {
                    AddBladeClearanceCut(
                        cuts,
                        blade.StockRail,
                        blade,
                        blade.Id + ":stock-clearance");
                }

                foreach (RailCenterline sharedOwner in rails.Where(rail =>
                    rail != blade.StockRail
                    && rail != blade.MovableRail
                    && BladeRunsThroughCurve(rail.Curve, blade.BladeCurve)))
                {
                    AddBladeClearanceCut(
                        cuts,
                        sharedOwner,
                        blade,
                        blade.Id + ":stock-clearance:shared-owner");
                }
            }
        }

        private static void AddBladeClearanceCut(
            ICollection<RailCut> cuts,
            RailCenterline rail,
            SwitchBladePlan blade,
            string sourceId)
        {
            float tip = rail.Curve.DistanceTo(blade.BladeCurve.Head.point);
            float root = rail.Curve.DistanceTo(blade.BladeCurve.Tail.point);
            AddCut(
                cuts,
                rail,
                tip,
                root,
                RailCutKind.SwitchBlade,
                sourceId);
        }

        private static bool BladeRunsThroughCurve(
            LineCurve stock,
            LineCurve movable)
        {
            int sampleCount = Mathf.Max(
                3,
                Mathf.CeilToInt(movable.Length / BladeSampleSpacing) + 1);
            float closeSamples = 0f;

            for (int index = 0; index < sampleCount; index++)
            {
                float distance = index == sampleCount - 1
                    ? movable.Length
                    : Mathf.Min(movable.Length, index * BladeSampleSpacing);
                Vector3 point = movable.LinePointAtDistance(distance).point;
                if (DistancePointToCurve(point, stock)
                    <= Parameters.BladeRootSeparation + Parameters.RailHeadWidth)
                {
                    closeSamples++;
                }
            }

            return closeSamples / sampleCount >= 0.5f;
        }

        private static RailCenterline ChooseSharedOwner(
            RailCenterline railA,
            RailCenterline railB)
        {
            return ChooseSharedOwner(railA, railB, Array.Empty<SwitchBladePlan>());
        }

        private static RailCenterline ChooseSharedOwner(
            RailCenterline railA,
            RailCenterline railB,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            bool aBladeStock = blades.Any(blade => blade.StockRail == railA);
            bool bBladeStock = blades.Any(blade => blade.StockRail == railB);
            if (aBladeStock != bBladeStock)
            {
                return aBladeStock ? railA : railB;
            }

            bool aBladeMovable = blades.Any(blade => blade.MovableRail == railA);
            bool bBladeMovable = blades.Any(blade => blade.MovableRail == railB);
            if (aBladeMovable != bBladeMovable)
            {
                return aBladeMovable ? railB : railA;
            }

            if (railA.Family != railB.Family)
            {
                return railA.Family == GaugeGraphFamily.Standard ? railA : railB;
            }

            bool aShared = railA.Role == RailRole.SharedRail;
            bool bShared = railB.Role == RailRole.SharedRail;
            if (aShared != bShared)
            {
                return aShared ? railA : railB;
            }

            bool aStock = railA.Role == RailRole.StockRail;
            bool bStock = railB.Role == RailRole.StockRail;
            if (aStock != bStock)
            {
                return aStock ? railA : railB;
            }

            bool aMovable = railA.Role == RailRole.PointBlade
                || railA.Role == RailRole.MovablePointBlade;
            bool bMovable = railB.Role == RailRole.PointBlade
                || railB.Role == RailRole.MovablePointBlade;
            if (aMovable != bMovable)
            {
                return aMovable ? railB : railA;
            }

            return string.Compare(
                railA.Id,
                railB.Id,
                StringComparison.OrdinalIgnoreCase) <= 0
                ? railA
                : railB;
        }

        private static void AssignInitialRailRoles(
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            SpecialWorkAnatomyPlan? anatomy)
        {
            foreach (RailCenterline rail in rails)
            {
                LogicalRoute? route = definition.Routes.FirstOrDefault(item =>
                    rail.SourceRouteIds.Contains(item.Id, StringComparer.OrdinalIgnoreCase));
                rail.SetRole(route?.SwitchGroupId == null
                    ? RailRole.StockRail
                    : RailRole.ClosureRail);
            }

            if (anatomy == null)
            {
                return;
            }

            foreach (SpecialWorkRailRoleAssignment assignment in anatomy.RailRoles)
            {
                RailCenterline? rail = FindRouteRail(rails, assignment.RouteId, assignment.Side);
                rail?.SetRole(assignment.Role);
            }
        }

        private static bool IsAcceptedFrogCandidate(FrogCandidate frog)
        {
            return RailCanOwnFrog(frog.Intersection.RailA)
                && RailCanOwnFrog(frog.Intersection.RailB)
                && frog.Intersection.AcuteAngleDegrees >= 1f;

            static bool RailCanOwnFrog(RailCenterline rail)
            {
                return rail.SourceRouteIds.Count > 0
                    && rail.Role != RailRole.Unknown
                    && rail.Role != RailRole.SuppressedRail
                    && rail.Role != RailRole.GuardRail
                    && rail.Role != RailRole.WingRail;
            }
        }

        private static void AssignRailRoles(
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<SharedRailInterval> shared,
            IReadOnlyList<RailCut> cuts,
            IReadOnlyList<FrogCandidate> frogs,
            IReadOnlyList<SwitchBladePlan> blades,
            SpecialWorkAnatomyPlan? anatomy)
        {
            foreach (RailCenterline rail in rails)
            {
                rail.SetRole(RailRole.Unknown);
            }

            foreach (RailCenterline rail in rails)
            {
                LogicalRoute? route = definition.Routes.FirstOrDefault(item =>
                    rail.SourceRouteIds.Contains(item.Id, StringComparer.OrdinalIgnoreCase));
                rail.SetRole(route?.SwitchGroupId == null
                    ? RailRole.StockRail
                    : RailRole.ClosureRail);
            }

            if (anatomy != null)
            {
                foreach (SpecialWorkRailRoleAssignment assignment in anatomy.RailRoles)
                {
                    RailCenterline? rail = FindRouteRail(rails, assignment.RouteId, assignment.Side);
                    rail?.SetRole(assignment.Role);
                }
            }

            foreach (SharedRailInterval interval in shared)
            {
                RailCenterline? railA = rails.FirstOrDefault(rail => rail.Id == interval.RailAId);
                RailCenterline? railB = rails.FirstOrDefault(rail => rail.Id == interval.RailBId);
                if (railA == null || railB == null)
                {
                    continue;
                }

                RailCenterline owner = ChooseSharedOwner(railA, railB, blades);
                owner.SetRole(RailRole.SharedRail);
            }

            foreach (FrogCandidate frog in frogs)
            {
                MarkFrogRail(frog.Intersection.RailA);
                MarkFrogRail(frog.Intersection.RailB);
            }

            foreach (SwitchBladePlan blade in blades)
            {
                if (blade.StockRail.Role != RailRole.SharedRail)
                {
                    blade.StockRail.SetRole(RailRole.StockRail);
                }

                blade.MovableRail.SetRole(RailRole.PointBlade);
            }

            void MarkFrogRail(RailCenterline rail)
            {
                if (rail.Role == RailRole.Unknown || rail.Role == RailRole.StockRail)
                {
                    rail.SetRole(RailRole.FrogRail);
                }
            }
        }

        private static void AddFrogCuts(
            IReadOnlyList<RailCenterline> rails,
            IEnumerable<FrogCandidate> frogs,
            ICollection<RailCut> cuts)
        {
            foreach (FrogCandidate frog in frogs)
            {
                float cutHalfLength = frog.CutHalfLength;
                AddCut(
                    cuts,
                    frog.Intersection.RailA,
                    frog.Intersection.DistanceA - cutHalfLength,
                    frog.Intersection.DistanceA + cutHalfLength,
                    RailCutKind.FrogGap,
                    frog.Id);
                AddCut(
                    cuts,
                    frog.Intersection.RailB,
                    frog.Intersection.DistanceB - cutHalfLength,
                    frog.Intersection.DistanceB + cutHalfLength,
                    RailCutKind.FrogGap,
                    frog.Id);

                foreach (RailCenterline rail in rails)
                {
                    if (rail == frog.Intersection.RailA
                        || rail == frog.Intersection.RailB)
                    {
                        continue;
                    }

                    float distance = rail.Curve.DistanceTo(frog.Intersection.Position);
                    Vector3 closest = rail.Curve.LinePointAtDistance(distance).point;
                    if (HorizontalDistance(closest, frog.Intersection.Position)
                        > Parameters.RailHeadWidth + Parameters.FlangewayWidth)
                    {
                        continue;
                    }

                    AddCut(
                        cuts,
                        rail,
                        distance - cutHalfLength,
                        distance + cutHalfLength,
                        RailCutKind.FrogGap,
                        frog.Id + ":envelope");
                }
            }
        }

        private static IEnumerable<SwitchBladePlan> BuildSwitchBlades(
            Graph graph,
            SpecialWorkDefinition definition,
            IReadOnlyList<WheelPath> wheelPaths,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<FrogCandidate> frogs,
            ICollection<RailCut> cuts,
            SpecialWorkAnatomyPlan? anatomy)
        {
            if (TryBuildGaugeSeparationBlade(
                graph,
                definition,
                rails,
                frogs,
                cuts,
                out SwitchBladePlan? gaugeSeparationBlade))
            {
                yield return gaugeSeparationBlade!;
                yield break;
            }

            if (anatomy != null)
            {
                foreach (SwitchBladePlan blade in BuildAnatomySwitchBlades(
                    graph,
                    definition,
                    wheelPaths,
                    rails,
                    anatomy,
                    cuts))
                {
                    yield return blade;
                }

                yield break;
            }

            int index = 0;
            foreach (IGrouping<string, LogicalRoute> routeGroup in definition.Routes
                .Where(route => !string.IsNullOrWhiteSpace(route.SwitchGroupId))
                .GroupBy(route => route.SwitchGroupId!, StringComparer.OrdinalIgnoreCase))
            {
                LogicalRoute? normalRoute = routeGroup.FirstOrDefault(route =>
                    string.Equals(route.RequiredStateId, "normal", StringComparison.OrdinalIgnoreCase));
                LogicalRoute? reversedRoute = routeGroup.FirstOrDefault(route =>
                    string.Equals(route.RequiredStateId, "reversed", StringComparison.OrdinalIgnoreCase));
                if (normalRoute == null || reversedRoute == null)
                {
                    continue;
                }

                WheelPath? normalPath = wheelPaths.FirstOrDefault(path =>
                    string.Equals(path.RouteId, normalRoute.Id, StringComparison.OrdinalIgnoreCase));
                WheelPath? reversedPath = wheelPaths.FirstOrDefault(path =>
                    string.Equals(path.RouteId, reversedRoute.Id, StringComparison.OrdinalIgnoreCase));
                RailCenterline[] groupRails = rails
                    .Where(rail =>
                        rail.SourceRouteIds.Contains(normalRoute.Id, StringComparer.OrdinalIgnoreCase)
                        || rail.SourceRouteIds.Contains(reversedRoute.Id, StringComparer.OrdinalIgnoreCase))
                    .ToArray();
                RailCenterline[] veeRails = frogs
                    .Where(frog =>
                        frog.Intersection.Kind == RailIntersectionKind.VeeFrogCandidate
                        && groupRails.Contains(frog.Intersection.RailA)
                        && groupRails.Contains(frog.Intersection.RailB))
                    .SelectMany(frog => new[] { frog.Intersection.RailA, frog.Intersection.RailB })
                    .Distinct()
                    .ToArray();
                TrackNode? switchNode = FindSwitchNode(graph, definition, routeGroup.Key);
                Vector3? switchPoint = switchNode?.transform.localPosition;
                var usedMovableRails = new HashSet<RailCenterline>();
                var yielded = new List<SwitchBladePlan>();

                if (normalPath != null && reversedPath != null && switchPoint.HasValue)
                {
                    foreach (RailSide side in new[] { RailSide.Left, RailSide.Right })
                    {
                        RailCenterline? normalRail = FindRailById(rails, normalPath.RailId(side));
                        RailCenterline? reversedRail = FindRailById(rails, reversedPath.RailId(side));
                        if (normalRail == null
                            || reversedRail == null
                            || usedMovableRails.Contains(reversedRail)
                            || !TryWheelPathBladeDistances(
                                normalPath,
                                reversedPath,
                                side,
                                switchPoint.Value,
                                normalRail,
                                reversedRail,
                                out float movableTip,
                                out float movableRoot))
                        {
                            continue;
                        }

                        usedMovableRails.Add(reversedRail);
                        AddCut(
                            cuts,
                            reversedRail,
                            movableTip,
                            movableRoot,
                            RailCutKind.SwitchBlade,
                            $"blade:{routeGroup.Key}:{side}:wheel");

                        yielded.Add(new SwitchBladePlan(
                            "blade:" + index++,
                            switchNode?.id,
                            routeGroup.Key,
                            normalRail,
                            reversedRail,
                            movableTip,
                            movableRoot,
                            Slice(reversedRail.Curve, movableTip, movableRoot),
                            Slice(reversedRail.Curve, movableRoot, reversedRail.Curve.Length)));
                    }

                    if (yielded.Count > 0)
                    {
                        foreach (SwitchBladePlan blade in yielded)
                        {
                            yield return blade;
                        }

                        continue;
                    }
                }

                if (veeRails.Length >= 2 && switchPoint.HasValue)
                {
                    foreach (RailCenterline movableRail in veeRails)
                    {
                        if (usedMovableRails.Contains(movableRail)
                            || !TryFindStockRailForMovable(
                                groupRails,
                                veeRails,
                                movableRail,
                                switchPoint.Value,
                                out RailCenterline stockRail)
                            || !TryFindBladeDistancesFromSwitchPoint(
                                stockRail.Curve,
                                movableRail.Curve,
                                switchPoint.Value,
                                out float stockTip,
                                out float movableTip,
                                out float movableRoot))
                        {
                            continue;
                        }

                        usedMovableRails.Add(movableRail);
                        AddCut(
                            cuts,
                            movableRail,
                            movableTip,
                            movableRoot,
                            RailCutKind.SwitchBlade,
                            $"blade:{routeGroup.Key}:{movableRail.Id}");

                        yielded.Add(new SwitchBladePlan(
                            "blade:" + index++,
                            switchNode?.id,
                            routeGroup.Key,
                            stockRail,
                            movableRail,
                            movableTip,
                            movableRoot,
                            Slice(movableRail.Curve, movableTip, movableRoot),
                            Slice(movableRail.Curve, movableRoot, movableRail.Curve.Length)));
                    }

                    if (yielded.Count > 0)
                    {
                        foreach (SwitchBladePlan blade in yielded)
                        {
                            yield return blade;
                        }

                        continue;
                    }
                }

                foreach (RailSide side in new[] { RailSide.Left, RailSide.Right })
                {
                    RailCenterline? normalRail = FindRouteRail(rails, normalRoute.Id, side);
                    RailCenterline? reversedRail = FindRouteRail(rails, reversedRoute.Id, side);
                    if (normalRail == null
                        || reversedRail == null
                        || !TryResolveBladePair(
                            normalRail,
                            reversedRail,
                            frogs,
                            out RailCenterline stockRail,
                            out RailCenterline movableRail)
                        || !TryFindBladeDistances(
                            stockRail.Curve,
                            movableRail.Curve,
                            out float stockTip,
                            out float movableTip,
                            out float movableRoot))
                    {
                        continue;
                    }

                    float bladeLength = movableRoot - movableTip;
                    if (bladeLength < MinimumPieceLength)
                    {
                        continue;
                    }

                    LineCurve blade = Slice(movableRail.Curve, movableTip, movableRoot);
                    LineCurve closure = Slice(
                        movableRail.Curve,
                        movableRoot,
                        movableRail.Curve.Length);
                    AddCut(
                        cuts,
                        movableRail,
                        movableTip,
                        movableRoot,
                        RailCutKind.SwitchBlade,
                        $"blade:{routeGroup.Key}:{side}");

                    yield return new SwitchBladePlan(
                        "blade:" + index++,
                        switchNode?.id,
                        routeGroup.Key,
                        stockRail,
                        movableRail,
                        movableTip,
                        movableRoot,
                        blade,
                        closure);
                }
            }
        }

        private static bool TryBuildGaugeSeparationBlade(
            Graph graph,
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<FrogCandidate> frogs,
            ICollection<RailCut> cuts,
            out SwitchBladePlan? blade)
        {
            blade = null;
            if (!string.Equals(
                    definition.Preset.Id,
                    SpecialWorkPresetIds.DualSplit,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            SpecialWorkSwitchGroup? group = definition.SwitchGroups.FirstOrDefault(item =>
                string.Equals(
                    item.Id,
                    "narrow-separation",
                    StringComparison.OrdinalIgnoreCase));
            string? nodeId = group?.NativeNodeIds.FirstOrDefault()
                ?? definition.NativeSwitchNodeIds.FirstOrDefault();
            TrackNode? switchNode = string.IsNullOrWhiteSpace(nodeId)
                ? null
                : graph.GetNode(nodeId);
            if (switchNode == null
                || !TryResolveGaugeSeparationBladePair(
                    rails,
                    frogs,
                    switchNode.transform.localPosition,
                    out RailCenterline stockRail,
                    out RailCenterline movableRail,
                    out RailSide bladeSide,
                    out float frogDistance))
            {
                return false;
            }

            if (!TryFindBladeDistancesTowardFrog(
                stockRail.Curve,
                movableRail.Curve,
                switchNode.transform.localPosition,
                frogDistance,
                out float movableTip,
                out float movableRoot,
                out bool tipAtHigherDistance))
            {
                return false;
            }

            AddCut(
                cuts,
                movableRail,
                movableTip,
                movableRoot,
                RailCutKind.SwitchBlade,
                $"blade:narrow-separation:{bladeSide.ToString().ToLowerInvariant()}");
            LineCurve bladeCurve = Slice(movableRail.Curve, movableTip, movableRoot);
            LineCurve closureCurve = Slice(movableRail.Curve, movableRoot, movableRail.Curve.Length);
            if (tipAtHigherDistance)
            {
                bladeCurve = bladeCurve.Reverse();
                closureCurve = Slice(movableRail.Curve, 0f, movableTip).Reverse();
            }

            blade = new SwitchBladePlan(
                $"blade:narrow-separation:{bladeSide.ToString().ToLowerInvariant()}",
                switchNode.id,
                group?.Id,
                stockRail,
                movableRail,
                movableTip,
                movableRoot,
                bladeCurve,
                closureCurve);
            return true;
        }

        private static bool TryResolveGaugeSeparationBladePair(
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<FrogCandidate> frogs,
            Vector3 switchPoint,
            out RailCenterline stockRail,
            out RailCenterline movableRail,
            out RailSide bladeSide,
            out float frogDistance)
        {
            stockRail = null!;
            movableRail = null!;
            bladeSide = RailSide.Left;
            frogDistance = 0f;
            float bestSeparation = float.MaxValue;

            foreach (RailSide side in new[] { RailSide.Left, RailSide.Right })
            {
                RailCenterline? standard =
                    FindRouteRail(rails, "standard-through", side);
                RailCenterline? narrow =
                    FindRouteRail(rails, "narrow-diverge", side);
                if (standard == null || narrow == null)
                {
                    continue;
                }

                FrogCandidate[] standardFrogs = frogs
                    .Where(frog =>
                        frog.Intersection.RailA == standard
                        || frog.Intersection.RailB == standard)
                    .ToArray();
                FrogCandidate[] narrowFrogs = frogs
                    .Where(frog =>
                        frog.Intersection.RailA == narrow
                        || frog.Intersection.RailB == narrow)
                    .ToArray();
                if ((standardFrogs.Length > 0) == (narrowFrogs.Length > 0))
                {
                    continue;
                }

                RailCenterline candidateMovable =
                    standardFrogs.Length > 0 ? standard : narrow;
                RailCenterline candidateStock =
                    candidateMovable == standard ? narrow : standard;
                FrogCandidate[] candidateFrogs =
                    candidateMovable == standard ? standardFrogs : narrowFrogs;
                float movableAtSwitch = candidateMovable.Curve.DistanceTo(switchPoint);
                Vector3 movablePoint =
                    candidateMovable.Curve.LinePointAtDistance(movableAtSwitch).point;
                float separation = DistancePointToCurve(movablePoint, candidateStock.Curve);
                if (separation > MaximumBladeTipSeparation * 1.5f
                    || separation >= bestSeparation)
                {
                    continue;
                }

                FrogCandidate nearestFrog = candidateFrogs
                    .OrderBy(frog => HorizontalDistance(
                        frog.Intersection.Position,
                        switchPoint))
                    .First();
                stockRail = candidateStock;
                movableRail = candidateMovable;
                bladeSide = side;
                frogDistance = nearestFrog.Intersection.RailA == candidateMovable
                    ? nearestFrog.Intersection.DistanceA
                    : nearestFrog.Intersection.DistanceB;
                bestSeparation = separation;
            }

            return stockRail != null && movableRail != null;
        }

        private static bool IsDualNarrowBranchPreset(SpecialWorkDefinition definition)
        {
            return string.Equals(
                definition.Preset.Id,
                SpecialWorkPresetIds.DualNarrowBranch,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDualStandardBranchPreset(SpecialWorkDefinition definition)
        {
            return string.Equals(
                definition.Preset.Id,
                SpecialWorkPresetIds.DualStandardBranch,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDualBothDivergePreset(SpecialWorkDefinition definition)
        {
            return string.Equals(
                definition.Preset.Id,
                SpecialWorkPresetIds.DualBothDiverge,
                StringComparison.OrdinalIgnoreCase);
        }

        private static void SuppressDualBothDivergeDuplicateRail(
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<RailWorkInterval> workIntervals,
            IReadOnlyList<FrogCandidate> frogs,
            ICollection<RailCut> cuts)
        {
            if (!IsDualBothDivergePreset(definition))
            {
                return;
            }

            RailCenterline? duplicate = rails.FirstOrDefault(rail =>
                string.Equals(
                    rail.Id,
                    "narrow-reversed:left",
                    StringComparison.OrdinalIgnoreCase));
            RailWorkInterval? work = workIntervals.FirstOrDefault(interval =>
                interval.Rail == duplicate);
            if (duplicate == null || work == null)
            {
                return;
            }

            if (RailParticipatesInAcceptedFrog(duplicate, frogs))
            {
                return;
            }

            AddCut(
                cuts,
                duplicate,
                work.StartDistance,
                work.EndDistance,
                RailCutKind.SharedDuplicate,
                "dual-both-diverge:duplicate-shared-blade-rail");
        }

        private static SwitchBladePlan[] ConsolidateDualBothDivergeBlades(
            SpecialWorkDefinition definition,
            IEnumerable<SwitchBladePlan> blades,
            List<RailCut> cuts)
        {
            SwitchBladePlan[] candidates = blades.ToArray();
            if (!IsDualBothDivergePreset(definition))
            {
                return candidates;
            }

            var retained = new List<SwitchBladePlan>(candidates.Length - 1);
            foreach (SwitchBladePlan candidate in candidates)
            {
                bool isDuplicateSharedBlade =
                    string.Equals(
                        candidate.SwitchGroupId,
                        "narrow",
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        candidate.MovableRail.Id,
                        "narrow-reversed:left",
                        StringComparison.OrdinalIgnoreCase);
                if (!isDuplicateSharedBlade)
                {
                    retained.Add(candidate);
                    continue;
                }

                RemoveBladeCut(cuts, candidate);
            }

            return retained.ToArray();
        }

        private static bool RailParticipatesInAcceptedFrog(
            RailCenterline rail,
            IEnumerable<FrogCandidate> frogs)
        {
            return frogs.Any(frog =>
                frog.Intersection.RailA == rail
                || frog.Intersection.RailB == rail);
        }

        private static void RemoveBladeCut(List<RailCut> cuts, SwitchBladePlan blade)
        {
            cuts.RemoveAll(cut =>
                cut.Kind == RailCutKind.SwitchBlade
                && cut.Rail == blade.MovableRail
                && Mathf.Abs(cut.StartDistance - blade.TipDistance) < CutCoverageTolerance
                && Mathf.Abs(cut.EndDistance - blade.RootDistance) < CutCoverageTolerance);
        }

        private static IEnumerable<SwitchBladePlan> BuildAnatomySwitchBlades(
            Graph graph,
            SpecialWorkDefinition definition,
            IReadOnlyList<WheelPath> wheelPaths,
            IReadOnlyList<RailCenterline> rails,
            SpecialWorkAnatomyPlan anatomy,
            ICollection<RailCut> cuts)
        {
            int index = 0;
            if (SpecialWorkTruthTableCatalog.TryGet(definition.Preset.Id, out TurnoutTruthTable truth)
                && truth.Blades.Length > 0)
            {
                foreach (TruthBlade rule in truth.Blades)
                {
                    if (TryBuildTruthTableBlade(
                        graph,
                        definition,
                        wheelPaths,
                        rails,
                        anatomy,
                        rule,
                        index,
                        cuts,
                        out SwitchBladePlan? blade))
                    {
                        index++;
                        yield return blade!;
                    }
                }

                yield break;
            }

            foreach (SpecialWorkAnatomySwitch switchSpec in anatomy.Switches)
            {
                TrackNode? switchNode = string.IsNullOrWhiteSpace(switchSpec.NativeNodeId)
                    ? null
                    : graph.GetNode(switchSpec.NativeNodeId);
                WheelPath? throughPath = wheelPaths.FirstOrDefault(path =>
                    string.Equals(path.RouteId, switchSpec.ThroughRoute.Id, StringComparison.OrdinalIgnoreCase));
                WheelPath? divergingPath = wheelPaths.FirstOrDefault(path =>
                    string.Equals(path.RouteId, switchSpec.DivergingRoute.Id, StringComparison.OrdinalIgnoreCase));

                if (TryBuildAnatomyBlade(
                    rails,
                    switchSpec,
                    switchNode,
                    switchSpec.ThroughRoute,
                    switchSpec.DivergingRoute,
                    throughPath,
                    divergingPath,
                    switchSpec.MovableSide,
                    "diverging",
                    index,
                    cuts,
                    out SwitchBladePlan? divergingBlade))
                {
                    index++;
                    yield return divergingBlade!;
                }

                if (TryBuildAnatomyBlade(
                    rails,
                    switchSpec,
                    switchNode,
                    switchSpec.DivergingRoute,
                    switchSpec.ThroughRoute,
                    divergingPath,
                    throughPath,
                    switchSpec.OppositeSide,
                    "normal",
                    index,
                    cuts,
                    out SwitchBladePlan? normalBlade))
                {
                    index++;
                    yield return normalBlade!;
                }
            }
        }

        private static bool TryBuildTruthTableBlade(
            Graph graph,
            SpecialWorkDefinition definition,
            IReadOnlyList<WheelPath> wheelPaths,
            IReadOnlyList<RailCenterline> rails,
            SpecialWorkAnatomyPlan anatomy,
            TruthBlade rule,
            int index,
            ICollection<RailCut> cuts,
            out SwitchBladePlan? blade)
        {
            blade = null;
            if (!Enum.TryParse(rule.MovableSide, ignoreCase: true, out RailSide movableSide)
                || !Enum.TryParse(rule.StockSide, ignoreCase: true, out RailSide stockSide)
                || movableSide != stockSide)
            {
                return false;
            }

            RailCenterline? stockRail = FindRouteRail(rails, rule.StockRouteId, stockSide);
            RailCenterline? movableRail = FindRouteRail(rails, rule.MovableRouteId, movableSide);
            if (stockRail == null || movableRail == null)
            {
                return false;
            }

            LogicalRoute? stockRoute = definition.Routes.FirstOrDefault(route =>
                string.Equals(route.Id, rule.StockRouteId, StringComparison.OrdinalIgnoreCase));
            LogicalRoute? movableRoute = definition.Routes.FirstOrDefault(route =>
                string.Equals(route.Id, rule.MovableRouteId, StringComparison.OrdinalIgnoreCase));
            SpecialWorkAnatomySwitch? switchSpec = anatomy.Switches.FirstOrDefault(item =>
                RouteMatches(item.ThroughRoute, stockRoute, movableRoute)
                || RouteMatches(item.DivergingRoute, stockRoute, movableRoute));
            TrackNode? switchNode = switchSpec == null || string.IsNullOrWhiteSpace(switchSpec.NativeNodeId)
                ? null
                : graph.GetNode(switchSpec.NativeNodeId);
            Vector3 switchPoint = switchNode != null
                ? switchNode.transform.localPosition
                : movableRail.Curve.LinePointAtDistance(
                    movableRail.Curve.DistanceTo(stockRail.Curve.Head.point)).point;
            WheelPath? stockPath = wheelPaths.FirstOrDefault(path =>
                string.Equals(path.RouteId, rule.StockRouteId, StringComparison.OrdinalIgnoreCase));
            WheelPath? movablePath = wheelPaths.FirstOrDefault(path =>
                string.Equals(path.RouteId, rule.MovableRouteId, StringComparison.OrdinalIgnoreCase));

            if (!TryFindAnatomyBladeDistances(
                stockPath,
                movablePath,
                movableSide,
                switchPoint,
                stockRail,
                movableRail,
                out float movableTip,
                out float movableRoot)
                || movableRoot - movableTip < MinimumPieceLength)
            {
                return false;
            }

            AddCut(
                cuts,
                movableRail,
                movableTip,
                movableRoot,
                RailCutKind.SwitchBlade,
                $"blade:truth:{rule.Label}:{movableSide}");

            blade = new SwitchBladePlan(
                "blade:truth:" + index,
                switchNode?.id,
                switchSpec?.SwitchGroupId,
                stockRail,
                movableRail,
                movableTip,
                movableRoot,
                Slice(movableRail.Curve, movableTip, movableRoot),
                Slice(movableRail.Curve, movableRoot, movableRail.Curve.Length));
            return true;

            static bool RouteMatches(
                LogicalRoute anatomyRoute,
                LogicalRoute? stockRoute,
                LogicalRoute? movableRoute)
            {
                return (stockRoute != null
                        && string.Equals(
                            anatomyRoute.SwitchGroupId,
                            stockRoute.SwitchGroupId,
                            StringComparison.OrdinalIgnoreCase))
                    || (movableRoute != null
                        && string.Equals(
                            anatomyRoute.SwitchGroupId,
                            movableRoute.SwitchGroupId,
                            StringComparison.OrdinalIgnoreCase));
            }
        }

        private static bool TryBuildAnatomyBlade(
            IReadOnlyList<RailCenterline> rails,
            SpecialWorkAnatomySwitch switchSpec,
            TrackNode? switchNode,
            LogicalRoute stockRoute,
            LogicalRoute movableRoute,
            WheelPath? stockPath,
            WheelPath? movablePath,
            RailSide side,
            string stateLabel,
            int index,
            ICollection<RailCut> cuts,
            out SwitchBladePlan? blade)
        {
            blade = null;
            RailCenterline? stockRail = FindRouteRail(rails, stockRoute.Id, side);
            RailCenterline? movableRail = FindRouteRail(rails, movableRoute.Id, side);
            if (stockRail == null || movableRail == null)
            {
                return false;
            }

            Vector3 switchPoint = switchNode != null
                ? switchNode.transform.localPosition
                : movableRail.Curve.LinePointAtDistance(
                    movableRail.Curve.DistanceTo(stockRail.Curve.Head.point)).point;
            if (!TryFindAnatomyBladeDistances(
                stockPath,
                movablePath,
                side,
                switchPoint,
                stockRail,
                movableRail,
                out float movableTip,
                out float movableRoot)
                || movableRoot - movableTip < MinimumPieceLength)
            {
                return false;
            }

            AddCut(
                cuts,
                movableRail,
                movableTip,
                movableRoot,
                RailCutKind.SwitchBlade,
                $"blade:anatomy:{switchSpec.SwitchGroupId}:{stateLabel}:{side}");

            blade = new SwitchBladePlan(
                "blade:anatomy:" + index,
                switchNode?.id,
                switchSpec.SwitchGroupId,
                stockRail,
                movableRail,
                movableTip,
                movableRoot,
                Slice(movableRail.Curve, movableTip, movableRoot),
                Slice(movableRail.Curve, movableRoot, movableRail.Curve.Length));
            return true;
        }

        private static bool TryFindAnatomyBladeDistances(
            WheelPath? stockPath,
            WheelPath? movablePath,
            RailSide side,
            Vector3 switchPoint,
            RailCenterline stockRail,
            RailCenterline movableRail,
            out float movableTip,
            out float movableRoot)
        {
            if (stockPath != null
                && movablePath != null
                && TryWheelPathBladeDistances(
                    stockPath,
                    movablePath,
                    side,
                    switchPoint,
                    stockRail,
                    movableRail,
                    out movableTip,
                    out movableRoot))
            {
                return true;
            }

            if (TryFindBladeDistancesFromSwitchPoint(
                stockRail.Curve,
                movableRail.Curve,
                switchPoint,
                out _,
                out movableTip,
                out movableRoot))
            {
                return true;
            }

            if (TryFindBladeDistances(
                stockRail.Curve,
                movableRail.Curve,
                out _,
                out movableTip,
                out movableRoot))
            {
                return true;
            }

            movableTip = Mathf.Clamp(
                movableRail.Curve.DistanceTo(switchPoint),
                0f,
                movableRail.Curve.Length);
            movableRoot = movableTip;
            float maximumRoot = Mathf.Min(
                movableRail.Curve.Length,
                movableTip + Parameters.MaximumBladeLength);
            for (float root = movableTip + BladeSampleSpacing;
                root <= maximumRoot + BladeSampleSpacing * 0.5f;
                root += BladeSampleSpacing)
            {
                float rootDistance = Mathf.Min(root, maximumRoot);
                Vector3 movablePoint = movableRail.Curve.LinePointAtDistance(rootDistance).point;
                if (DistancePointToCurve(movablePoint, stockRail.Curve)
                    >= Parameters.BladeRootSeparation
                    || rootDistance >= maximumRoot)
                {
                    movableRoot = rootDistance;
                    break;
                }
            }

            return movableRoot - movableTip >= MinimumPieceLength;
        }

        private static bool TryResolveBladePair(
            RailCenterline normalRail,
            RailCenterline reversedRail,
            IReadOnlyList<FrogCandidate> frogs,
            out RailCenterline stockRail,
            out RailCenterline movableRail)
        {
            FrogCandidate? sameFamilyVee = frogs.FirstOrDefault(frog =>
                frog.Intersection.Kind == RailIntersectionKind.VeeFrogCandidate
                && frog.Intersection.RailA.Family == normalRail.Family
                && frog.Intersection.RailB.Family == normalRail.Family
                && (frog.Intersection.RailA == normalRail
                    || frog.Intersection.RailB == normalRail
                    || frog.Intersection.RailA == reversedRail
                    || frog.Intersection.RailB == reversedRail));

            if (sameFamilyVee != null)
            {
                bool normalFeedsVee =
                    sameFamilyVee.Intersection.RailA == normalRail
                    || sameFamilyVee.Intersection.RailB == normalRail;
                bool reversedFeedsVee =
                    sameFamilyVee.Intersection.RailA == reversedRail
                    || sameFamilyVee.Intersection.RailB == reversedRail;
                if (normalFeedsVee != reversedFeedsVee)
                {
                    movableRail = normalFeedsVee ? normalRail : reversedRail;
                    stockRail = normalFeedsVee ? reversedRail : normalRail;
                    return true;
                }
            }

            stockRail = normalRail;
            movableRail = reversedRail;
            return true;
        }

        private static RailCenterline? FindRailById(
            IEnumerable<RailCenterline> rails,
            string railId)
        {
            return rails.FirstOrDefault(rail =>
                string.Equals(rail.Id, railId, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryWheelPathBladeDistances(
            WheelPath normalPath,
            WheelPath reversedPath,
            RailSide side,
            Vector3 switchPoint,
            RailCenterline stockRail,
            RailCenterline movableRail,
            out float movableTip,
            out float movableRoot)
        {
            movableTip = movableRail.Curve.DistanceTo(switchPoint);
            movableRoot = movableTip;

            LineCurve normalGuide = normalPath.FlangeGuide(side);
            LineCurve reversedGuide = reversedPath.FlangeGuide(side);
            float reversedGuideTip = reversedGuide.DistanceTo(switchPoint);
            Vector3 guideTipPoint = reversedGuide.LinePointAtDistance(reversedGuideTip).point;
            if (DistancePointToCurve(guideTipPoint, normalGuide)
                > MaximumBladeTipSeparation)
            {
                return false;
            }

            Vector3 movableTipPoint = movableRail.Curve.LinePointAtDistance(movableTip).point;
            if (DistancePointToCurve(movableTipPoint, stockRail.Curve)
                > MaximumBladeTipSeparation * 1.5f)
            {
                return false;
            }

            float maximumGuideRoot = Mathf.Min(
                reversedGuide.Length,
                reversedGuideTip + Parameters.MaximumBladeLength);
            float chosenGuideRoot = reversedGuideTip;
            for (float root = reversedGuideTip + BladeSampleSpacing;
                root <= maximumGuideRoot + BladeSampleSpacing * 0.5f;
                root += BladeSampleSpacing)
            {
                float rootDistance = Mathf.Min(root, maximumGuideRoot);
                Vector3 reversedPoint =
                    reversedGuide.LinePointAtDistance(rootDistance).point;
                if (DistancePointToCurve(reversedPoint, normalGuide)
                    >= Parameters.BladeRootSeparation
                    || rootDistance >= maximumGuideRoot)
                {
                    chosenGuideRoot = rootDistance;
                    break;
                }
            }

            if (chosenGuideRoot <= reversedGuideTip + MinimumPieceLength)
            {
                return false;
            }

            Vector3 rootPoint = reversedGuide.LinePointAtDistance(chosenGuideRoot).point;
            movableRoot = movableRail.Curve.DistanceTo(rootPoint);
            if (movableRoot < movableTip)
            {
                float swap = movableTip;
                movableTip = movableRoot;
                movableRoot = swap;
            }

            return movableRoot - movableTip >= MinimumPieceLength;
        }

        private static TrackNode? FindSwitchNode(
            Graph graph,
            SpecialWorkDefinition definition,
            string switchGroupId)
        {
            SpecialWorkSwitchGroup? group = definition.SwitchGroups.FirstOrDefault(item =>
                string.Equals(item.Id, switchGroupId, StringComparison.OrdinalIgnoreCase));
            string? nodeId = group?.NativeNodeIds.FirstOrDefault()
                ?? definition.NativeSwitchNodeIds.FirstOrDefault();
            return string.IsNullOrWhiteSpace(nodeId) ? null : graph.GetNode(nodeId);
        }

        private static bool TryFindStockRailForMovable(
            IReadOnlyList<RailCenterline> groupRails,
            IReadOnlyCollection<RailCenterline> veeRails,
            RailCenterline movableRail,
            Vector3 switchPoint,
            out RailCenterline stockRail)
        {
            stockRail = null!;
            Vector3 movableTip =
                movableRail.Curve.LinePointAtDistance(
                    movableRail.Curve.DistanceTo(switchPoint)).point;
            RailCenterline? candidate = groupRails
                .Where(rail =>
                    rail != movableRail
                    && rail.Family == movableRail.Family
                    && !veeRails.Contains(rail))
                .OrderBy(rail => HorizontalDistance(
                    movableTip,
                    rail.Curve.LinePointAtDistance(
                        rail.Curve.DistanceTo(movableTip)).point))
                .FirstOrDefault();
            if (candidate == null)
            {
                return false;
            }

            float tipSeparation = HorizontalDistance(
                movableTip,
                candidate.Curve.LinePointAtDistance(
                    candidate.Curve.DistanceTo(movableTip)).point);
            if (tipSeparation > MaximumBladeTipSeparation)
            {
                return false;
            }

            stockRail = candidate;
            return true;
        }

        private static bool TryFindBladeDistancesFromSwitchPoint(
            LineCurve stock,
            LineCurve movable,
            Vector3 switchPoint,
            out float stockTip,
            out float movableTip,
            out float movableRoot)
        {
            movableTip = movable.DistanceTo(switchPoint);
            Vector3 movableTipPoint = movable.LinePointAtDistance(movableTip).point;
            stockTip = stock.DistanceTo(movableTipPoint);
            movableRoot = movableTip;

            if (HorizontalDistance(
                    movableTipPoint,
                    stock.LinePointAtDistance(stockTip).point)
                > MaximumBladeTipSeparation)
            {
                return false;
            }

            float maximumRoot = Mathf.Min(movable.Length, movableTip + Parameters.MaximumBladeLength);
            for (float root = movableTip + BladeSampleSpacing;
                root <= maximumRoot + BladeSampleSpacing * 0.5f;
                root += BladeSampleSpacing)
            {
                float rootDistance = Mathf.Min(root, maximumRoot);
                Vector3 movablePoint = movable.LinePointAtDistance(rootDistance).point;
                if (DistancePointToCurve(movablePoint, stock) >= Parameters.BladeRootSeparation
                    || rootDistance >= maximumRoot)
                {
                    movableRoot = rootDistance;
                    return movableRoot - movableTip >= MinimumPieceLength;
                }
            }

            return false;
        }

        private static bool TryFindBladeDistancesTowardFrog(
            LineCurve stock,
            LineCurve movable,
            Vector3 switchPoint,
            float frogDistance,
            out float bladeStart,
            out float bladeEnd,
            out bool tipAtHigherDistance)
        {
            float tipDistance = Mathf.Clamp(movable.DistanceTo(switchPoint), 0f, movable.Length);
            tipAtHigherDistance = frogDistance < tipDistance;
            bladeStart = tipDistance;
            bladeEnd = tipDistance;
            Vector3 tipPoint = movable.LinePointAtDistance(tipDistance).point;
            if (DistancePointToCurve(tipPoint, stock) > MaximumBladeTipSeparation * 1.5f)
            {
                return false;
            }

            float direction = tipAtHigherDistance ? -1f : 1f;
            float rootLimit = Mathf.Clamp(
                tipDistance + direction * Parameters.MaximumBladeLength,
                0f,
                movable.Length);
            for (float root = tipDistance + direction * BladeSampleSpacing;
                direction > 0f
                    ? root <= rootLimit + BladeSampleSpacing * 0.5f
                    : root >= rootLimit - BladeSampleSpacing * 0.5f;
                root += direction * BladeSampleSpacing)
            {
                float rootDistance = direction > 0f
                    ? Mathf.Min(root, rootLimit)
                    : Mathf.Max(root, rootLimit);
                Vector3 rootPoint = movable.LinePointAtDistance(rootDistance).point;
                if (DistancePointToCurve(rootPoint, stock) >= Parameters.BladeRootSeparation
                    || Mathf.Abs(rootDistance - rootLimit) <= BladeSampleSpacing * 0.5f)
                {
                    bladeStart = Mathf.Min(tipDistance, rootDistance);
                    bladeEnd = Mathf.Max(tipDistance, rootDistance);
                    return bladeEnd - bladeStart >= MinimumPieceLength;
                }
            }

            return false;
        }

        private static bool TryFindBladeDistances(
            LineCurve stock,
            LineCurve movable,
            out float stockTip,
            out float movableTip,
            out float movableRoot)
        {
            stockTip = 0f;
            movableTip = 0f;
            movableRoot = 0f;
            float maximum = Mathf.Min(stock.Length, movable.Length);
            bool foundShared = false;
            float lastSharedDistance = 0f;

            for (float distance = 0f; distance <= maximum; distance += BladeSampleSpacing)
            {
                float clamped = Mathf.Min(distance, maximum);
                Vector3 stockPoint = stock.LinePointAtDistance(clamped).point;
                Vector3 movablePoint = movable.LinePointAtDistance(clamped).point;
                float separation = HorizontalDistance(stockPoint, movablePoint);
                if (separation <= Parameters.BladeDivergenceThreshold)
                {
                    foundShared = true;
                    lastSharedDistance = clamped;
                    continue;
                }

                if (!foundShared)
                {
                    continue;
                }

                stockTip = lastSharedDistance;
                movableTip = lastSharedDistance;
                float maximumRoot = Mathf.Min(maximum, movableTip + Parameters.MaximumBladeLength);
                for (float root = movableTip + BladeSampleSpacing;
                    root <= maximumRoot + BladeSampleSpacing * 0.5f;
                    root += BladeSampleSpacing)
                {
                    float rootDistance = Mathf.Min(root, maximumRoot);
                    if (HorizontalDistance(
                        stock.LinePointAtDistance(rootDistance).point,
                        movable.LinePointAtDistance(rootDistance).point)
                        >= Parameters.BladeRootSeparation
                        || rootDistance >= maximumRoot)
                    {
                        movableRoot = rootDistance;
                        return movableRoot - movableTip >= MinimumPieceLength;
                    }
                }
            }

            return false;
        }

        private static RailCenterline? FindRouteRail(
            IEnumerable<RailCenterline> rails,
            string routeId,
            RailSide side)
        {
            return rails.FirstOrDefault(rail =>
                rail.Side == side
                && rail.SourceRouteIds.Contains(routeId, StringComparer.OrdinalIgnoreCase));
        }

        private static IEnumerable<RailOwnershipInterval> BuildRailOwnershipIntervals(
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<SharedRailInterval> shared,
            IReadOnlyList<RailWorkInterval> workIntervals,
            IReadOnlyList<RailCut> cuts,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            int index = 0;
            var bladeRails = new HashSet<RailCenterline>(blades.Select(blade => blade.MovableRail));
            foreach (RailCenterline rail in rails)
            {
                RailWorkInterval? work = workIntervals.FirstOrDefault(item => item.Rail == rail);
                if (work == null
                    || work.Length < MinimumPieceLength
                    || rail.Role == RailRole.SuppressedRail)
                {
                    continue;
                }

                SwitchBladePlan? blade = blades.FirstOrDefault(item => item.MovableRail == rail);
                string ownerRouteId = rail.SourceRouteIds.FirstOrDefault() ?? string.Empty;
                if (blade != null)
                {
                    yield return new RailOwnershipInterval(
                        "owner:" + index++,
                        rail,
                        ownerRouteId,
                        rail.Family,
                        RailRole.PointBlade,
                        RailPieceKind.MovableBlade,
                        blade.TipDistance,
                        blade.RootDistance,
                        blade.Id);

                    bool hasFrogBeforeBlade = cuts.Any(cut =>
                        cut.Rail == rail
                        && cut.Kind == RailCutKind.FrogGap
                        && cut.EndDistance < blade.TipDistance + CutCoverageTolerance);
                    if (hasFrogBeforeBlade
                        && blade.TipDistance - work.StartDistance >= MinimumPieceLength)
                    {
                        yield return new RailOwnershipInterval(
                            "owner:" + index++,
                            rail,
                            ownerRouteId,
                            rail.Family,
                            RailRole.ClosureRail,
                            RailPieceKind.ClosureRail,
                            work.StartDistance,
                            blade.TipDistance,
                            blade.Id + ":pre-blade-closure");
                    }

                    float closureEnd = FindClosureEndDistance(rail, work, cuts, blade.RootDistance);
                    if (closureEnd - blade.RootDistance >= MinimumPieceLength)
                    {
                        yield return new RailOwnershipInterval(
                            "owner:" + index++,
                            rail,
                            ownerRouteId,
                            rail.Family,
                            RailRole.ClosureRail,
                            RailPieceKind.ClosureRail,
                            blade.RootDistance,
                            closureEnd,
                            blade.Id + ":closure");
                    }

                    if (work.EndDistance - closureEnd >= MinimumPieceLength)
                    {
                        yield return new RailOwnershipInterval(
                            "owner:" + index++,
                            rail,
                            ownerRouteId,
                            rail.Family,
                            RailRole.FrogRail,
                            RailPieceKind.FixedRunning,
                            closureEnd,
                            work.EndDistance,
                            blade.Id + ":post-frog");
                    }

                    continue;
                }

                if (bladeRails.Contains(rail))
                {
                    continue;
                }

                if (TryGetGaugeSeparationOppositeClosure(
                    definition,
                    rails,
                    blades,
                    out SwitchBladePlan pairedBlade,
                    out RailCenterline oppositeClosure)
                    && rail == oppositeClosure)
                {
                    float closureEnd = FindClosureEndDistance(
                        rail,
                        work,
                        cuts,
                        work.StartDistance);
                    if (closureEnd - work.StartDistance >= MinimumPieceLength)
                    {
                        yield return new RailOwnershipInterval(
                            "owner:" + index++,
                            rail,
                            ownerRouteId,
                            rail.Family,
                            RailRole.ClosureRail,
                            RailPieceKind.ClosureRail,
                            work.StartDistance,
                            closureEnd,
                            pairedBlade.Id + ":opposite-closure");
                    }

                    if (work.EndDistance - closureEnd >= MinimumPieceLength)
                    {
                        yield return new RailOwnershipInterval(
                            "owner:" + index++,
                            rail,
                            ownerRouteId,
                            rail.Family,
                            RailRole.FrogRail,
                            RailPieceKind.FixedRunning,
                            closureEnd,
                            work.EndDistance,
                            pairedBlade.Id + ":opposite-post-frog");
                    }

                    continue;
                }

                bool isSharedOwner = IsSharedOwnerRail(rail, rails, shared, blades);
                yield return new RailOwnershipInterval(
                    "owner:" + index++,
                    rail,
                    ownerRouteId,
                    rail.Family,
                    rail.Role,
                    isSharedOwner ? RailPieceKind.SharedRunning : RailPieceKind.FixedRunning,
                    work.StartDistance,
                    work.EndDistance,
                    "work:" + rail.Id);
            }
        }

        private static bool TryGetGaugeSeparationOppositeClosure(
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<SwitchBladePlan> blades,
            out SwitchBladePlan blade,
            out RailCenterline closureRail)
        {
            blade = null!;
            closureRail = null!;
            if (!string.Equals(
                    definition.Preset.Id,
                    SpecialWorkPresetIds.DualSplit,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            SwitchBladePlan? foundBlade = blades.FirstOrDefault(item =>
                item.MovableRail.SourceRouteIds.Contains(
                    "narrow-diverge",
                    StringComparer.OrdinalIgnoreCase)
                || item.StockRail.SourceRouteIds.Contains(
                    "narrow-diverge",
                    StringComparer.OrdinalIgnoreCase));
            RailCenterline? narrowReference =
                foundBlade?.MovableRail.SourceRouteIds.Contains(
                    "narrow-diverge",
                    StringComparer.OrdinalIgnoreCase) == true
                    ? foundBlade.MovableRail
                    : foundBlade?.StockRail;
            RailCenterline? foundClosure =
                FindRouteRail(
                    rails,
                    "narrow-diverge",
                    narrowReference?.Side == RailSide.Right
                        ? RailSide.Left
                        : RailSide.Right);
            if (foundBlade == null || foundClosure == null)
            {
                return false;
            }

            blade = foundBlade;
            closureRail = foundClosure;
            return true;
        }

        private static float FindClosureEndDistance(
            RailCenterline rail,
            RailWorkInterval work,
            IReadOnlyList<RailCut> cuts,
            float bladeRoot)
        {
            RailCut? firstFrogCut = cuts
                .Where(cut =>
                    cut.Rail == rail
                    && cut.Kind == RailCutKind.FrogGap
                    && cut.StartDistance > bladeRoot + CutCoverageTolerance)
                .OrderBy(cut => cut.StartDistance)
                .FirstOrDefault();
            return firstFrogCut?.StartDistance ?? work.EndDistance;
        }

        private static bool IsSharedOwnerRail(
            RailCenterline rail,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<SharedRailInterval> shared,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            foreach (SharedRailInterval interval in shared)
            {
                if (interval.RailAId != rail.Id && interval.RailBId != rail.Id)
                {
                    continue;
                }

                RailCenterline? railA = rails.FirstOrDefault(item => item.Id == interval.RailAId);
                RailCenterline? railB = rails.FirstOrDefault(item => item.Id == interval.RailBId);
                if (railA != null
                    && railB != null
                    && ChooseSharedOwner(railA, railB, blades) == rail)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddOwnershipConflictCuts(
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<RailOwnershipInterval> ownership,
            IReadOnlyList<SwitchBladePlan> blades,
            ICollection<RailCut> cuts)
        {
            RailOwnershipInterval[] protectedIntervals = ownership
                .Where(interval =>
                    interval.PieceKind == RailPieceKind.MovableBlade
                    || interval.PieceKind == RailPieceKind.ClosureRail)
                .ToArray();
            foreach (RailOwnershipInterval owner in protectedIntervals)
            {
                LineCurve ownedCurve = Slice(
                    owner.Rail.Curve,
                    owner.StartDistance,
                    owner.EndDistance);
                foreach (RailCenterline candidate in rails)
                {
                    if (candidate == owner.Rail
                        || blades.Any(blade =>
                            blade.MovableRail == owner.Rail
                            && blade.StockRail == candidate)
                        || candidate.Role == RailRole.SuppressedRail)
                    {
                        continue;
                    }

                    foreach ((float start, float end) in FindCorridorOverlaps(
                        candidate.Curve,
                        ownedCurve))
                    {
                        AddCut(
                            cuts,
                            candidate,
                            start,
                            end,
                            RailCutKind.OwnershipConflict,
                            owner.Id + ":" + owner.Role + ":" + owner.Rail.Id);
                    }
                }
            }
        }

        private static IEnumerable<(float Start, float End)> FindCorridorOverlaps(
            LineCurve candidate,
            LineCurve owner)
        {
            var matches = new List<float>();
            int count = Mathf.Max(
                2,
                Mathf.CeilToInt(owner.Length / OwnershipSampleSpacing) + 1);
            for (int index = 0; index < count; index++)
            {
                float ownerDistance = index == count - 1
                    ? owner.Length
                    : Mathf.Min(owner.Length, index * OwnershipSampleSpacing);
                Vector3 point = owner.LinePointAtDistance(ownerDistance).point;
                float candidateDistance = candidate.DistanceTo(point);
                Vector3 candidatePoint = candidate.LinePointAtDistance(candidateDistance).point;
                if (HorizontalDistance(point, candidatePoint) <= OwnershipCorridorTolerance)
                {
                    matches.Add(candidateDistance);
                }
            }

            if (matches.Count < 2)
            {
                yield break;
            }

            matches.Sort();
            float start = matches[0];
            float previous = matches[0];
            for (int index = 1; index < matches.Count; index++)
            {
                if (matches[index] - previous <= OwnershipSampleSpacing * 2.5f)
                {
                    previous = matches[index];
                    continue;
                }

                if (previous - start >= MinimumOwnershipIntervalLength)
                {
                    yield return (
                        Mathf.Max(0f, start - OwnershipSampleSpacing),
                        Mathf.Min(candidate.Length, previous + OwnershipSampleSpacing));
                }

                start = matches[index];
                previous = matches[index];
            }

            if (previous - start >= MinimumOwnershipIntervalLength)
            {
                yield return (
                    Mathf.Max(0f, start - OwnershipSampleSpacing),
                    Mathf.Min(candidate.Length, previous + OwnershipSampleSpacing));
            }
        }

        private static IEnumerable<RailPiece> BuildFixedRailPieces(
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<SharedRailInterval> shared,
            IReadOnlyList<RailOwnershipInterval> ownershipIntervals,
            IReadOnlyList<RailCut> cuts,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            int index = 0;
            foreach (RailOwnershipInterval ownership in ownershipIntervals)
            {
                if (ownership.Length < MinimumPieceLength
                    || ownership.PieceKind == RailPieceKind.MovableBlade
                    || ownership.Role == RailRole.SuppressedRail)
                {
                    continue;
                }

                RailCenterline rail = ownership.Rail;
                float cursor = ownership.StartDistance;
                foreach ((float start, float end) in MergeCuts(
                    cuts.Where(cut =>
                            cut.Rail == rail
                            && cut.EndDistance > ownership.StartDistance
                            && cut.StartDistance < ownership.EndDistance)
                        .Select(cut => new RailCut(
                            cut.Id,
                            cut.Rail,
                            Mathf.Max(cut.StartDistance, ownership.StartDistance),
                            Mathf.Min(cut.EndDistance, ownership.EndDistance),
                            cut.Kind,
                            cut.SourceId))))
                {
                    foreach (RailPiece piece in CreatePiece(cursor, start))
                    {
                        yield return piece;
                    }

                    cursor = Mathf.Max(cursor, end);
                }

                foreach (RailPiece piece in CreatePiece(cursor, ownership.EndDistance))
                {
                    yield return piece;
                }

                IEnumerable<RailPiece> CreatePiece(float start, float end)
                {
                    if (end - start < MinimumPieceLength)
                    {
                        yield break;
                    }

                    yield return new RailPiece(
                        "fixed:" + index++,
                        rail.Id,
                        ownership.PieceKind == RailPieceKind.SharedRunning
                            ? RailPieceKind.SharedRunning
                            : ResolvePieceKind(rail, start, end, ownership, blades),
                        Slice(rail.Curve, start, end),
                        start,
                        end,
                        ownership.Id);
                }
            }
        }

        private static RailPieceKind ResolvePieceKind(
            RailCenterline rail,
            float start,
            float end,
            RailOwnershipInterval ownership,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            if (ownership.PieceKind == RailPieceKind.ClosureRail)
            {
                return RailPieceKind.ClosureRail;
            }

            SwitchBladePlan? blade = blades.FirstOrDefault(item => item.MovableRail == rail);
            if (blade != null && start >= blade.RootDistance - CutCoverageTolerance)
            {
                return RailPieceKind.ClosureRail;
            }

            return rail.Role == RailRole.ClosureRail || rail.Role == RailRole.PointBlade
                ? RailPieceKind.ClosureRail
                : RailPieceKind.FixedRunning;
        }

        private static IEnumerable<WingRailPlan> BuildWingRails(
            IEnumerable<FrogCandidate> frogs,
            IReadOnlyList<RailPiece> fixedRails,
            IReadOnlyList<RailWorkInterval> workIntervals)
        {
            int index = 0;
            foreach (FrogCandidate frog in frogs)
            {
                foreach ((RailCenterline rail, float distance) in new[]
                    {
                        (frog.Intersection.RailA, frog.Intersection.DistanceA),
                        (frog.Intersection.RailB, frog.Intersection.DistanceB)
                    })
                {
                    float tipClearance = frog.FlangewaySetback + WingTipClearance;
                    if (frog.CutHalfLength - tipClearance < MinimumPieceLength)
                    {
                        continue;
                    }
                    float approachStart = distance - frog.CutHalfLength;
                    float approachEnd = distance - tipClearance;
                    float trailStart = distance + tipClearance;
                    float trailEnd = distance + frog.CutHalfLength;

                    if (approachEnd - approachStart >= MinimumPieceLength)
                    {
                        LineCurve approach = Slice(
                            rail.Curve,
                            approachStart,
                            approachEnd);
                        if (TouchesRunningRail(
                            approach.Head.point,
                            rail,
                            fixedRails,
                            workIntervals))
                        {
                            yield return new WingRailPlan(
                                "wing:" + index++,
                                frog.Id,
                                rail,
                                approach,
                                approachSide: true);
                        }
                    }

                    if (trailEnd - trailStart >= MinimumPieceLength)
                    {
                        LineCurve trail = Slice(
                            rail.Curve,
                            trailStart,
                            trailEnd);
                        if (TouchesRunningRail(
                            trail.Tail.point,
                            rail,
                            fixedRails,
                            workIntervals))
                        {
                            yield return new WingRailPlan(
                                "wing:" + index++,
                                frog.Id,
                                rail,
                                trail,
                                approachSide: false);
                        }
                    }
                }
            }
        }

        private static bool TouchesRunningRail(
            Vector3 point,
            RailCenterline rail,
            IEnumerable<RailPiece> fixedRails,
            IEnumerable<RailWorkInterval> workIntervals)
        {
            if (fixedRails.Any(piece =>
                DistancePointToCurve(point, piece.Curve)
                    <= HardwareAttachmentTolerance))
            {
                return true;
            }

            RailWorkInterval? work =
                workIntervals.FirstOrDefault(interval => interval.Rail == rail);
            return work != null
                && (HorizontalDistance(
                        point,
                        rail.Curve.LinePointAtDistance(work.StartDistance).point)
                        <= HardwareAttachmentTolerance
                    || HorizontalDistance(
                        point,
                        rail.Curve.LinePointAtDistance(work.EndDistance).point)
                        <= HardwareAttachmentTolerance);
        }

        private static IEnumerable<RailPiece> BuildFrogPieces(
            IEnumerable<FrogCandidate> frogs)
        {
            int index = 0;
            foreach (FrogCandidate frog in frogs)
            {
                float tipClearance = frog.FlangewaySetback + WingTipClearance;
                var rays = new[]
                {
                    BuildFrogRay(
                        frog.Intersection.RailA,
                        frog.Intersection.DistanceA - tipClearance,
                        frog.Intersection.Position),
                    BuildFrogRay(
                        frog.Intersection.RailA,
                        frog.Intersection.DistanceA + tipClearance,
                        frog.Intersection.Position),
                    BuildFrogRay(
                        frog.Intersection.RailB,
                        frog.Intersection.DistanceB - tipClearance,
                        frog.Intersection.Position),
                    BuildFrogRay(
                        frog.Intersection.RailB,
                        frog.Intersection.DistanceB + tipClearance,
                        frog.Intersection.Position)
                }
                .OrderBy(ray => Mathf.Atan2(ray.Direction.z, ray.Direction.x))
                .ToArray();

                IEnumerable<(FrogRay First, FrogRay Second)> pairs =
                    frog.Intersection.Kind == RailIntersectionKind.CrossingFrogCandidate
                        ? AdjacentPairs(rays)
                        : new[] { BestVeePair(rays, frog.NoseDirection) };

                foreach ((FrogRay first, FrogRay second) in pairs)
                {
                    Vector3 bisector = (first.Direction + second.Direction).normalized;
                    if (bisector.sqrMagnitude <= 0.0001f)
                    {
                        continue;
                    }

                    Vector3 nose = frog.Intersection.Position
                        + bisector * (Parameters.FlangewayWidth * 0.5f);
                    LineCurve vee = MakeVee(first.Point, nose, second.Point);
                    if (vee.Length < MinimumPieceLength)
                    {
                        continue;
                    }

                    yield return new RailPiece(
                        "frog-piece:" + index++,
                        $"{first.Rail.Id}+{second.Rail.Id}",
                        RailPieceKind.FrogNose,
                        vee,
                        0f,
                        vee.Length,
                        frog.Id);
                }
            }
        }

        private static FrogRay BuildFrogRay(
            RailCenterline rail,
            float distance,
            Vector3 intersection)
        {
            Vector3 point = rail.Curve.LinePointAtDistance(
                Mathf.Clamp(distance, 0f, rail.Curve.Length)).point;
            Vector3 direction = point - intersection;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = rail.Curve.LinePointAtDistance(
                    Mathf.Clamp(distance, 0f, rail.Curve.Length)).direction;
                direction.y = 0f;
            }

            return new FrogRay(rail, point, direction.normalized);
        }

        private static IEnumerable<(FrogRay First, FrogRay Second)> AdjacentPairs(
            IReadOnlyList<FrogRay> rays)
        {
            for (int index = 0; index < rays.Count; index++)
            {
                yield return (rays[index], rays[(index + 1) % rays.Count]);
            }
        }

        private static (FrogRay First, FrogRay Second) BestVeePair(
            IReadOnlyList<FrogRay> rays,
            Vector3 noseDirection)
        {
            Vector3 target = noseDirection;
            target.y = 0f;
            target.Normalize();

            (FrogRay First, FrogRay Second) best = (rays[0], rays[1]);
            float bestScore = float.MinValue;
            foreach ((FrogRay first, FrogRay second) in AdjacentPairs(rays))
            {
                Vector3 bisector = (first.Direction + second.Direction).normalized;
                float score = Vector3.Dot(bisector, target);
                if (score > bestScore)
                {
                    best = (first, second);
                    bestScore = score;
                }
            }

            return best;
        }

        private static IEnumerable<GuardRailPlan> BuildGuardRails(
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IEnumerable<FrogCandidate> frogs,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            var result = new List<GuardRailPlan>();
            foreach (FrogCandidate frog in frogs)
            {
                foreach (RailCenterline crossingRail in new[]
                    {
                        frog.Intersection.RailA,
                        frog.Intersection.RailB
                    })
                {
                    foreach (string routeId in crossingRail.SourceRouteIds)
                    {
                        RailCenterline? opposite = rails.FirstOrDefault(rail =>
                            rail != crossingRail
                            && rail.Side != crossingRail.Side
                            && rail.SourceRouteIds.Contains(routeId, StringComparer.OrdinalIgnoreCase));
                        if (opposite == null)
                        {
                            continue;
                        }

                        if (ShouldReplaceDualStandardBranchCrossingGuard(
                            definition,
                            frog,
                            opposite))
                        {
                            continue;
                        }

                        float center = opposite.Curve.DistanceTo(frog.Intersection.Position);
                        LineCurve source = Slice(
                            opposite.Curve,
                            center - Parameters.GuardLeadLength,
                            center + Parameters.GuardTrailLength);
                        float offset = opposite.Side == RailSide.Left
                            ? Parameters.GuardCenterOffset
                            : -Parameters.GuardCenterOffset;
                        LineCurve guard = TaperGuard(source.Parallel(offset));
                        Vector3 midpoint =
                            guard.LinePointAtDistance(guard.Length * 0.5f).point;
                        if (rails.Any(rail =>
                            rail != opposite
                            && DistancePointToCurve(midpoint, rail.Curve)
                                < GuardCollisionTolerance)
                            || result.Any(existing =>
                                DistancePointToCurve(midpoint, existing.Curve) < 0.1f))
                        {
                            continue;
                        }

                        result.Add(new GuardRailPlan(
                            "guard:" + result.Count,
                            frog.Id,
                            routeId,
                            opposite,
                            guard));
                    }
                }

                if (TryBuildDualStandardBranchKinkedCrossingGuard(
                    definition,
                    frog,
                    blades,
                    out RailCenterline guardOwner,
                    out LineCurve kinkedGuard))
                {
                    result.Add(new GuardRailPlan(
                        "guard:" + result.Count,
                        frog.Id,
                        guardOwner.SourceRouteIds.FirstOrDefault() ?? string.Empty,
                        guardOwner,
                        TaperGuard(kinkedGuard)));
                }

                if (TryBuildGaugeSeparationKCrossingGuard(
                    definition,
                    frog,
                    blades,
                    out RailCenterline kGuardOwner,
                    out LineCurve kGuard))
                {
                    result.Add(new GuardRailPlan(
                        "guard:" + result.Count,
                        frog.Id,
                        kGuardOwner.SourceRouteIds.FirstOrDefault() ?? string.Empty,
                        kGuardOwner,
                        TaperGuardAwayFrom(kGuard, frog.Intersection.Position)));
                }
            }

            return result;
        }

        private static bool TryBuildGaugeSeparationKCrossingGuard(
            SpecialWorkDefinition definition,
            FrogCandidate frog,
            IReadOnlyList<SwitchBladePlan> blades,
            out RailCenterline guardOwner,
            out LineCurve guardCurve)
        {
            guardOwner = null!;
            guardCurve = null!;
            if (!string.Equals(
                    definition.Preset.Id,
                    SpecialWorkPresetIds.DualSplit,
                    StringComparison.OrdinalIgnoreCase)
                || frog.Intersection.Kind != RailIntersectionKind.CrossingFrogCandidate)
            {
                return false;
            }

            (RailCenterline Rail, float Distance)[] crossingRails =
            {
                (frog.Intersection.RailA, frog.Intersection.DistanceA),
                (frog.Intersection.RailB, frog.Intersection.DistanceB)
            };
            (RailCenterline Rail, float Distance) narrow = crossingRails.FirstOrDefault(item =>
                item.Rail.Family == GaugeGraphFamily.Narrow);
            (RailCenterline Rail, float Distance) standard = crossingRails.FirstOrDefault(item =>
                item.Rail.Family == GaugeGraphFamily.Standard);
            if (narrow.Rail == null || standard.Rail == null)
            {
                return false;
            }

            guardOwner = narrow.Rail;
            Vector3 bladeDirection = DirectionTowardBlades(frog, blades);
            float standardBladeSide = SideTowardDirection(
                standard.Rail,
                standard.Distance,
                frog.Intersection.Position,
                bladeDirection);
            float narrowBladeSide = SideTowardDirection(
                narrow.Rail,
                narrow.Distance,
                frog.Intersection.Position,
                bladeDirection);
            LineCurve guardCenterPath = BuildKinkedHandoff(
                PointAtSignedOffset(
                    standard.Rail,
                    standard.Distance,
                    -standardBladeSide * frog.CutHalfLength),
                PointAtSignedOffset(
                    narrow.Rail,
                    narrow.Distance,
                    narrowBladeSide * frog.CutHalfLength),
                frog.Intersection.Position);
            LineCurve stockHandoff = BuildKinkedHandoff(
                PointAtSignedOffset(
                    standard.Rail,
                    standard.Distance,
                    standardBladeSide * frog.CutHalfLength),
                PointAtSignedOffset(
                    narrow.Rail,
                    narrow.Distance,
                    -narrowBladeSide * frog.CutHalfLength),
                frog.Intersection.Position);

            LineCurve positive = guardCenterPath.Parallel(Parameters.GuardCenterOffset);
            LineCurve negative = guardCenterPath.Parallel(-Parameters.GuardCenterOffset);
            Vector3 positiveMiddle = positive.LinePointAtDistance(positive.Length * 0.5f).point;
            Vector3 negativeMiddle = negative.LinePointAtDistance(negative.Length * 0.5f).point;
            guardCurve = DistancePointToCurve(positiveMiddle, stockHandoff)
                >= DistancePointToCurve(negativeMiddle, stockHandoff)
                    ? positive
                    : negative;
            return true;
        }

        private static bool ShouldReplaceDualStandardBranchCrossingGuard(
            SpecialWorkDefinition definition,
            FrogCandidate frog,
            RailCenterline opposite)
        {
            return string.Equals(
                    definition.Preset.Id,
                    SpecialWorkPresetIds.DualStandardBranch,
                    StringComparison.OrdinalIgnoreCase)
                && frog.Intersection.Kind == RailIntersectionKind.CrossingFrogCandidate
                && opposite.Family == GaugeGraphFamily.Standard
                && new[] { frog.Intersection.RailA, frog.Intersection.RailB }.Any(rail =>
                    rail.Family == GaugeGraphFamily.Narrow)
                && new[] { frog.Intersection.RailA, frog.Intersection.RailB }.Any(rail =>
                    rail.Family == GaugeGraphFamily.Standard
                    && rail.Side != opposite.Side
                    && rail.SourceRouteIds.Intersect(
                        opposite.SourceRouteIds,
                        StringComparer.OrdinalIgnoreCase).Any());
        }

        private static bool TryBuildDualStandardBranchKinkedCrossingGuard(
            SpecialWorkDefinition definition,
            FrogCandidate frog,
            IReadOnlyList<SwitchBladePlan> blades,
            out RailCenterline guardOwner,
            out LineCurve guardCurve)
        {
            guardOwner = null!;
            guardCurve = null!;
            if (!string.Equals(
                    definition.Preset.Id,
                    SpecialWorkPresetIds.DualStandardBranch,
                    StringComparison.OrdinalIgnoreCase)
                || frog.Intersection.Kind != RailIntersectionKind.CrossingFrogCandidate)
            {
                return false;
            }

            (RailCenterline Rail, float Distance)[] crossingRails =
            {
                (frog.Intersection.RailA, frog.Intersection.DistanceA),
                (frog.Intersection.RailB, frog.Intersection.DistanceB)
            };
            (RailCenterline Rail, float Distance) standard = crossingRails.FirstOrDefault(item =>
                item.Rail.Family == GaugeGraphFamily.Standard);
            (RailCenterline Rail, float Distance) narrow = crossingRails.FirstOrDefault(item =>
                item.Rail.Family == GaugeGraphFamily.Narrow);
            if (standard.Rail == null || narrow.Rail == null)
            {
                return false;
            }

            guardOwner = standard.Rail;
            Vector3 bladeDirection = DirectionTowardBlades(frog, blades);
            float standardBladeSide = SideTowardDirection(
                standard.Rail,
                standard.Distance,
                frog.Intersection.Position,
                bladeDirection);
            float narrowBladeSide = SideTowardDirection(
                narrow.Rail,
                narrow.Distance,
                frog.Intersection.Position,
                bladeDirection);
            LineCurve guardCenterPath = BuildKinkedHandoff(
                PointAtSignedOffset(
                    standard.Rail,
                    standard.Distance,
                    -standardBladeSide * frog.CutHalfLength),
                PointAtSignedOffset(
                    narrow.Rail,
                    narrow.Distance,
                    narrowBladeSide * frog.CutHalfLength),
                frog.Intersection.Position);
            LineCurve stockHandoff = BuildKinkedHandoff(
                PointAtSignedOffset(
                    standard.Rail,
                    standard.Distance,
                    standardBladeSide * frog.CutHalfLength),
                PointAtSignedOffset(
                    narrow.Rail,
                    narrow.Distance,
                    -narrowBladeSide * frog.CutHalfLength),
                frog.Intersection.Position);

            LineCurve positive = guardCenterPath.Parallel(Parameters.GuardCenterOffset);
            LineCurve negative = guardCenterPath.Parallel(-Parameters.GuardCenterOffset);
            Vector3 positiveMiddle = positive.LinePointAtDistance(positive.Length * 0.5f).point;
            Vector3 negativeMiddle = negative.LinePointAtDistance(negative.Length * 0.5f).point;
            guardCurve = DistancePointToCurve(positiveMiddle, stockHandoff)
                >= DistancePointToCurve(negativeMiddle, stockHandoff)
                    ? positive
                    : negative;
            return true;
        }

        private static Vector3 DirectionTowardBlades(
            FrogCandidate frog,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            Vector3 bladeCenter = blades.Count == 0
                ? frog.Intersection.Position + frog.NoseDirection
                : blades
                    .Select(blade => blade.BladeCurve.Head.point)
                    .Aggregate(Vector3.zero, (sum, point) => sum + point) / blades.Count;
            Vector3 direction = bladeCenter - frog.Intersection.Position;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : frog.NoseDirection.normalized;
        }

        private static float SideTowardDirection(
            RailCenterline rail,
            float centerDistance,
            Vector3 center,
            Vector3 direction)
        {
            const float sampleDistance = 0.35f;
            LinePoint before = rail.Curve.LinePointAtDistance(
                Mathf.Max(0f, centerDistance - sampleDistance));
            LinePoint after = rail.Curve.LinePointAtDistance(
                Mathf.Min(rail.Curve.Length, centerDistance + sampleDistance));
            return Vector3.Dot(after.point - center, direction)
                >= Vector3.Dot(before.point - center, direction)
                    ? 1f
                    : -1f;
        }

        private static LinePoint PointAtSignedOffset(
            RailCenterline rail,
            float centerDistance,
            float signedOffset)
        {
            return rail.Curve.LinePointAtDistance(
                Mathf.Clamp(centerDistance + signedOffset, 0f, rail.Curve.Length));
        }

        private static LineCurve BuildKinkedHandoff(
            LinePoint start,
            LinePoint end,
            Vector3 crossing)
        {
            Vector3 span = end.point - start.point;
            Vector3 startDirection = start.direction;
            Vector3 endDirection = end.direction;
            span.y = 0f;
            startDirection.y = 0f;
            endDirection.y = 0f;
            if (Vector3.Dot(startDirection, span) < 0f)
            {
                startDirection = -startDirection;
            }
            if (Vector3.Dot(endDirection, span) < 0f)
            {
                endDirection = -endDirection;
            }

            startDirection = startDirection.sqrMagnitude > 0.0001f
                ? startDirection.normalized
                : span.normalized;
            endDirection = endDirection.sqrMagnitude > 0.0001f
                ? endDirection.normalized
                : span.normalized;
            Vector3 kinkDirection = (startDirection + endDirection).normalized;
            if (kinkDirection.sqrMagnitude <= 0.0001f)
            {
                kinkDirection = span.normalized;
            }

            return new LineCurve(
                new[]
                {
                    new LinePoint(start.point, Quaternion.LookRotation(startDirection, Vector3.up)),
                    new LinePoint(crossing, Quaternion.LookRotation(kinkDirection, Vector3.up)),
                    new LinePoint(end.point, Quaternion.LookRotation(endDirection, Vector3.up))
                },
                Hand.Left);
        }

        private static IEnumerable<GeometryDebugLabel> BuildDebugLabels(
            IEnumerable<RailCenterline> rails,
            IEnumerable<RailPiece> fixedRails,
            IEnumerable<RailCut> cuts,
            IEnumerable<FrogCandidate> frogs,
            IEnumerable<WingRailPlan> wings,
            IEnumerable<GuardRailPlan> guards,
            IEnumerable<SwitchBladePlan> blades)
        {
            foreach (RailCenterline rail in rails)
            {
                string ownerRouteId = rail.SourceRouteIds.FirstOrDefault() ?? "<none>";
                string ownerRoute = OwnerRouteLabel(rail, ownerRouteId);
                string sourceCurve = SourceCurveLabel(ownerRouteId);
                yield return new GeometryDebugLabel(
                    $"Role={rail.Role} OwnerRoute={ownerRoute} SourceCurve={sourceCurve}",
                    rail.Curve.LinePointAtDistance(rail.Curve.Length * 0.5f).point,
                    RoleColor(rail.Role),
                    rail.Curve);
            }

            foreach (RailPiece piece in fixedRails)
            {
                yield return new GeometryDebugLabel(
                    $"{piece.Kind} {piece.SourceRailId}",
                    piece.Curve.LinePointAtDistance(piece.Curve.Length * 0.5f).point,
                    PieceColor(piece.Kind),
                    piece.Curve);
            }

            foreach (RailCut cut in cuts)
            {
                yield return new GeometryDebugLabel(
                    $"{cut.Kind} {cut.Rail.Id} {cut.StartDistance:0.00}-{cut.EndDistance:0.00}",
                    cut.Rail.Curve.LinePointAtDistance(
                        (cut.StartDistance + cut.EndDistance) * 0.5f).point,
                    Color.yellow,
                    cut.Rail.Curve
                        .Skip(cut.StartDistance, true)
                        .Take(cut.EndDistance - cut.StartDistance));
            }

            foreach (FrogCandidate frog in frogs)
            {
                yield return new GeometryDebugLabel(
                    $"{frog.Id} {frog.Intersection.AcuteAngleDegrees:0.0} deg {frog.Handedness}",
                    frog.Intersection.Position,
                    Color.red);
            }

            foreach (WingRailPlan wing in wings)
            {
                yield return new GeometryDebugLabel(
                    wing.Id,
                    wing.Curve.LinePointAtDistance(wing.Curve.Length * 0.5f).point,
                    new Color(1f, 0.55f, 0f, 1f),
                    wing.Curve);
            }

            foreach (GuardRailPlan guard in guards)
            {
                yield return new GeometryDebugLabel(
                    guard.Id,
                    guard.Curve.LinePointAtDistance(guard.Curve.Length * 0.5f).point,
                    Color.green,
                    guard.Curve);
            }

            foreach (SwitchBladePlan blade in blades)
            {
                yield return new GeometryDebugLabel(
                    blade.Id,
                    blade.BladeCurve.LinePointAtDistance(
                        blade.BladeCurve.Length * 0.5f).point,
                    new Color(0.7f, 0.15f, 1f, 1f),
                    blade.BladeCurve);
            }
        }

        private static Color PieceColor(RailPieceKind kind)
        {
            switch (kind)
            {
                case RailPieceKind.SharedRunning:
                    return new Color(0.35f, 1f, 0.1f, 1f);
                case RailPieceKind.ClosureRail:
                    return new Color(1f, 0.55f, 0f, 1f);
                case RailPieceKind.FrogNose:
                    return Color.red;
                case RailPieceKind.WingRail:
                    return new Color(1f, 0.35f, 0.75f, 1f);
                case RailPieceKind.GuardRail:
                    return Color.magenta;
                case RailPieceKind.MovableBlade:
                    return Color.yellow;
                default:
                    return Color.green;
            }
        }

        private static string SourceCurveLabel(string routeId)
        {
            if (routeId.IndexOf("reversed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "DivergingRoute";
            }

            if (routeId.IndexOf("normal", StringComparison.OrdinalIgnoreCase) >= 0
                || routeId.IndexOf("through", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "ThroughRoute";
            }

            return "RouteCenterline";
        }

        private static string OwnerRouteLabel(RailCenterline rail, string routeId)
        {
            bool diverging = routeId.IndexOf("reversed", StringComparison.OrdinalIgnoreCase) >= 0;
            bool through =
                routeId.IndexOf("normal", StringComparison.OrdinalIgnoreCase) >= 0
                || routeId.IndexOf("through", StringComparison.OrdinalIgnoreCase) >= 0;

            if (rail.Family == GaugeGraphFamily.Narrow)
            {
                if (diverging)
                {
                    return "NarrowDiverging";
                }

                if (through)
                {
                    return "NarrowThrough";
                }

                return "NarrowRoute";
            }

            if (diverging)
            {
                return "StandardDiverging";
            }

            if (through)
            {
                return "StandardThrough";
            }

            return "StandardRoute";
        }

        private static Color RoleColor(RailRole role)
        {
            switch (role)
            {
                case RailRole.StockRail:
                    return Color.green;
                case RailRole.PointBlade:
                    return Color.yellow;
                case RailRole.ClosureRail:
                    return new Color(1f, 0.55f, 0f, 1f);
                case RailRole.FrogRail:
                case RailRole.CrossingRail:
                    return Color.red;
                case RailRole.WingRail:
                    return new Color(1f, 0.35f, 0.75f, 1f);
                case RailRole.GuardRail:
                    return Color.magenta;
                case RailRole.SharedRail:
                    return new Color(0.35f, 1f, 0.1f, 1f);
                case RailRole.SuppressedRail:
                    return Color.gray;
                default:
                    return new Color(1f, 0f, 0f, 1f);
            }
        }

        private static IEnumerable<string> Validate(
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<RailPiece> fixedRails,
            IReadOnlyList<RailWorkInterval> workIntervals,
            IReadOnlyList<RailCut> cuts,
            IReadOnlyList<FrogCandidate> frogs,
            IReadOnlyList<WingRailPlan> wings,
            IReadOnlyList<GuardRailPlan> guards,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            foreach (RailCenterline rail in rails)
            {
                RailWorkInterval? work = workIntervals.FirstOrDefault(item => item.Rail == rail);
                if (work == null)
                {
                    yield return $"Rail '{rail.Id}' has no special-work ownership interval.";
                    continue;
                }

                bool hasFixedPiece = fixedRails.Any(piece => piece.SourceRailId == rail.Id);
                bool fullySuppressed = IntervalIsCovered(
                    work.StartDistance,
                    work.EndDistance,
                    cuts.Where(cut => cut.Rail == rail));
                if (!hasFixedPiece && !fullySuppressed)
                {
                    yield return
                        $"Rail '{rail.Id}' has no fixed pieces and its work interval is not fully suppressed.";
                }
            }

            foreach (FrogCandidate frog in frogs)
            {
                float requiredCutHalfLength = Mathf.Max(
                    frog.RailHeadSetback,
                    frog.FlangewaySetback + WingTipClearance + MinimumPieceLength);
                if (frog.CutHalfLength + CutCoverageTolerance < requiredCutHalfLength)
                {
                    yield return
                        $"Frog '{frog.Id}' requires {requiredCutHalfLength:0.000}m setback " +
                        $"but the configured maximum permits only {frog.CutHalfLength:0.000}m.";
                }

                foreach ((RailCenterline rail, float center) in new[]
                    {
                        (frog.Intersection.RailA, frog.Intersection.DistanceA),
                        (frog.Intersection.RailB, frog.Intersection.DistanceB)
                    })
                {
                    if (!cuts.Any(cut =>
                        cut.Rail == rail
                        && cut.Kind == RailCutKind.FrogGap
                        && cut.StartDistance <= center - frog.CutHalfLength + CutCoverageTolerance
                        && cut.EndDistance >= center + frog.CutHalfLength - CutCoverageTolerance))
                    {
                        yield return $"Frog '{frog.Id}' does not fully cut rail '{rail.Id}'.";
                    }
                }

                // Wing and guard counts are advisory — complex multi-frog
                // zones may legitimately produce fewer per-frog pieces.
            }

            if (definition.Preset.Category == SpecialWorkCategory.DualGauge
                && !definition.Preset.SupportsGhostGraph
                && !cuts.Any(cut => cut.Kind == RailCutKind.SharedDuplicate))
            {
                yield return "Dual-gauge plan did not suppress any duplicate shared rail interval.";
            }

            if (definition.SwitchGroups.Count > 0 && blades.Count == 0)
            {
                yield return "Switch groups exist but no route-divergence blade plans were derived.";
            }

            foreach (SwitchBladePlan blade in blades)
            {
                if (!cuts.Any(cut =>
                    cut.Rail == blade.MovableRail
                    && cut.Kind == RailCutKind.SwitchBlade
                    && cut.StartDistance <= blade.TipDistance + CutCoverageTolerance
                    && cut.EndDistance >= blade.RootDistance - CutCoverageTolerance))
                {
                    yield return $"Blade '{blade.Id}' does not suppress its fixed movable-rail interval.";
                }

                float stockTip = blade.StockRail.Curve.DistanceTo(blade.BladeCurve.Head.point);
                float stockRoot = blade.StockRail.Curve.DistanceTo(blade.BladeCurve.Tail.point);
                if (cuts.Any(cut =>
                    cut.Rail == blade.StockRail
                    && cut.Kind == RailCutKind.OwnershipConflict
                    && IntervalsOverlap(
                        cut.StartDistance,
                        cut.EndDistance,
                        Mathf.Min(stockTip, stockRoot),
                        Mathf.Max(stockTip, stockRoot),
                        CutCoverageTolerance)))
                {
                    yield return $"Blade '{blade.Id}' ownership conflict cuts its stock rail '{blade.StockRail.Id}'.";
                }
            }

            if (frogs.Count > 0 && guards.Count == 0)
            {
                yield return "Frog candidates exist but no collision-free guard rail plans were derived.";
            }

            foreach (RailCut cut in cuts.Where(cut => cut.Kind == RailCutKind.SharedDuplicate))
            {
                if (fixedRails.Any(piece =>
                    piece.SourceRailId == cut.Rail.Id
                    && IntervalsOverlap(
                        piece.StartDistance,
                        piece.EndDistance,
                        cut.StartDistance,
                        cut.EndDistance,
                        CutCoverageTolerance)))
                {
                    yield return $"Shared duplicate cut '{cut.Id}' still overlaps a fixed rail piece.";
                }
            }

            foreach (RailWorkInterval work in workIntervals)
            {
                if (work.Length < MinimumPieceLength)
                {
                    yield return $"Rail '{work.Rail.Id}' has an empty special-work ownership interval.";
                }
            }
        }

        private static void AddCut(
            ICollection<RailCut> cuts,
            RailCenterline rail,
            float start,
            float end,
            RailCutKind kind,
            string sourceId)
        {
            var cut = new RailCut(
                "cut:" + cuts.Count,
                rail,
                start,
                end,
                kind,
                sourceId);
            if (cut.Length >= MinimumPieceLength
                && !cuts.Any(existing =>
                    existing.Rail == rail
                    && existing.Kind == kind
                    && Mathf.Abs(existing.StartDistance - cut.StartDistance) < 0.02f
                    && Mathf.Abs(existing.EndDistance - cut.EndDistance) < 0.02f))
            {
                cuts.Add(cut);
            }
        }

        private static IEnumerable<(float Start, float End)> MergeCuts(
            IEnumerable<RailCut> cuts)
        {
            RailCut[] ordered = cuts
                .Where(cut => cut.Length > 0f)
                .OrderBy(cut => cut.StartDistance)
                .ToArray();
            if (ordered.Length == 0)
            {
                yield break;
            }

            float start = ordered[0].StartDistance;
            float end = ordered[0].EndDistance;
            for (int index = 1; index < ordered.Length; index++)
            {
                if (ordered[index].StartDistance <= end + 0.01f)
                {
                    end = Mathf.Max(end, ordered[index].EndDistance);
                    continue;
                }

                yield return (start, end);
                start = ordered[index].StartDistance;
                end = ordered[index].EndDistance;
            }

            yield return (start, end);
        }

        private static LineCurve Slice(LineCurve curve, float start, float end)
        {
            float clampedStart = Mathf.Clamp(start, 0f, curve.Length);
            float clampedEnd = Mathf.Clamp(end, clampedStart, curve.Length);
            return curve.Skip(clampedStart, true).Take(clampedEnd - clampedStart);
        }

        private static LineCurve MakeVee(Vector3 start, Vector3 nose, Vector3 end)
        {
            Vector3 approach = (nose - start).normalized;
            Vector3 departure = (end - nose).normalized;
            Vector3 noseDirection = (approach + departure).normalized;
            if (noseDirection.sqrMagnitude <= 0.0001f)
            {
                noseDirection = departure;
            }

            return new LineCurve(
                new[]
                {
                    new LinePoint(start, approach),
                    new LinePoint(nose, noseDirection),
                    new LinePoint(end, departure)
                },
                Hand.Left);
        }

        private static LineCurve TaperGuard(LineCurve guard)
        {
            if (guard.Length < 0.55f)
            {
                return guard;
            }

            float taperLength = Mathf.Min(0.25f, guard.Length * 0.2f);
            float offsetAmount = Mathf.Tan(10f * Mathf.Deg2Rad) * taperLength;
            Vector3 lateral = guard.hand == Hand.Left ? Vector3.right : Vector3.left;
            Quaternion taperRotation = Quaternion.Euler(
                0f,
                10f * (guard.hand == Hand.Left ? -1f : 1f),
                0f);

            LinePoint head = guard.Head;
            guard = guard.Skip(taperLength, false);
            guard.Insert(0, new LinePoint(
                head.point + head.Rotation * lateral * offsetAmount,
                taperRotation * head.Rotation));

            guard = guard.Reverse();
            head = guard.Head;
            guard = guard.Skip(taperLength, false);
            guard.Insert(0, new LinePoint(
                head.point + head.Rotation * lateral * offsetAmount,
                taperRotation * head.Rotation));
            return guard.Reverse();
        }

        private static LineCurve TaperGuardAwayFrom(
            LineCurve guard,
            Vector3 avoidPoint)
        {
            if (guard.Length < 0.55f)
            {
                return guard;
            }

            float taperLength = Mathf.Min(0.25f, guard.Length * 0.2f);
            float offsetAmount = Mathf.Tan(10f * Mathf.Deg2Rad) * taperLength;

            LinePoint head = guard.Head;
            guard = guard.Skip(taperLength, false);
            guard.Insert(0, BuildOutwardGuardEnd(head, avoidPoint, offsetAmount));

            guard = guard.Reverse();
            head = guard.Head;
            guard = guard.Skip(taperLength, false);
            guard.Insert(0, BuildOutwardGuardEnd(head, avoidPoint, offsetAmount));
            return guard.Reverse();
        }

        private static LinePoint BuildOutwardGuardEnd(
            LinePoint end,
            Vector3 avoidPoint,
            float offsetAmount)
        {
            Vector3 right = end.Rotation * Vector3.right;
            Vector3 left = end.Rotation * Vector3.left;
            bool useRight =
                Vector3.SqrMagnitude(end.point + right * offsetAmount - avoidPoint)
                >= Vector3.SqrMagnitude(end.point + left * offsetAmount - avoidPoint);
            Vector3 side = useRight ? Vector3.right : Vector3.left;
            float angle = useRight ? 10f : -10f;
            return new LinePoint(
                end.point + end.Rotation * side * offsetAmount,
                Quaternion.Euler(0f, angle, 0f) * end.Rotation);
        }

        private static float DistancePointToCurve(Vector3 point, LineCurve curve)
        {
            float best = float.MaxValue;
            foreach ((int _, LineSegment segment) in curve.Segments)
            {
                best = Mathf.Min(
                    best,
                    DistancePointToSegment(point, segment.a.point, segment.b.point));
            }

            return best;
        }

        private static float DistancePointToSegment(
            Vector3 point,
            Vector3 start,
            Vector3 end)
        {
            Vector3 delta = end - start;
            if (delta.sqrMagnitude <= 0.000001f)
            {
                return Vector3.Distance(point, start);
            }

            float t = Mathf.Clamp01(Vector3.Dot(point - start, delta) / delta.sqrMagnitude);
            return Vector3.Distance(point, start + delta * t);
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private static bool IntervalsOverlap(
            float startA,
            float endA,
            float startB,
            float endB,
            float tolerance)
        {
            return Mathf.Min(endA, endB) - Mathf.Max(startA, startB) > tolerance;
        }

        private static bool IntervalIsCovered(
            float start,
            float end,
            IEnumerable<RailCut> cuts)
        {
            float cursor = start;
            foreach ((float cutStart, float cutEnd) in MergeCuts(cuts))
            {
                if (cutEnd <= cursor + CutCoverageTolerance)
                {
                    continue;
                }

                if (cutStart > cursor + CutCoverageTolerance)
                {
                    return false;
                }

                cursor = Mathf.Max(cursor, cutEnd);
                if (cursor >= end - CutCoverageTolerance)
                {
                    return true;
                }
            }

            return cursor >= end - CutCoverageTolerance;
        }

        private readonly struct FrogRay
        {
            public FrogRay(
                RailCenterline rail,
                Vector3 point,
                Vector3 direction)
            {
                Rail = rail;
                Point = point;
                Direction = direction;
            }

            public RailCenterline Rail { get; }
            public Vector3 Point { get; }
            public Vector3 Direction { get; }
        }
    }
}
