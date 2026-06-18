using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Track;
using UnityEngine;

namespace NarrowGaugeMod
{
    internal enum SpecialWorkHandedness
    {
        Left,
        Right
    }

    internal sealed class SpecialWorkAnatomyPlan
    {
        public SpecialWorkAnatomyPlan(
            IEnumerable<SpecialWorkAnatomySwitch> switches,
            IEnumerable<SpecialWorkRailRoleAssignment> railRoles,
            IEnumerable<string> issues)
        {
            Switches = (switches ?? Enumerable.Empty<SpecialWorkAnatomySwitch>()).ToArray();
            RailRoles = (railRoles ?? Enumerable.Empty<SpecialWorkRailRoleAssignment>()).ToArray();
            Issues = (issues ?? Enumerable.Empty<string>()).ToArray();
        }

        public IReadOnlyList<SpecialWorkAnatomySwitch> Switches { get; }
        public IReadOnlyList<SpecialWorkRailRoleAssignment> RailRoles { get; }
        public IReadOnlyList<string> Issues { get; }
    }

    internal sealed class SpecialWorkAnatomySwitch
    {
        public SpecialWorkAnatomySwitch(
            string switchGroupId,
            string? nativeNodeId,
            GaugeGraphFamily family,
            LogicalRoute throughRoute,
            LogicalRoute divergingRoute,
            SpecialWorkHandedness handedness,
            RailSide movableSide)
        {
            SwitchGroupId = switchGroupId;
            NativeNodeId = nativeNodeId;
            Family = family;
            ThroughRoute = throughRoute;
            DivergingRoute = divergingRoute;
            Handedness = handedness;
            MovableSide = movableSide;
        }

        public string SwitchGroupId { get; }
        public string? NativeNodeId { get; }
        public GaugeGraphFamily Family { get; }
        public LogicalRoute ThroughRoute { get; }
        public LogicalRoute DivergingRoute { get; }
        public SpecialWorkHandedness Handedness { get; }
        public RailSide MovableSide { get; }
        public RailSide StockSide => MovableSide;
        public RailSide OppositeSide => MovableSide == RailSide.Left ? RailSide.Right : RailSide.Left;
    }

    internal sealed class SpecialWorkRailRoleAssignment
    {
        public SpecialWorkRailRoleAssignment(
            string routeId,
            RailSide side,
            RailRole role,
            string reason)
        {
            RouteId = routeId;
            Side = side;
            Role = role;
            Reason = reason;
        }

        public string RouteId { get; }
        public RailSide Side { get; }
        public RailRole Role { get; }
        public string Reason { get; }
    }

    internal static class SpecialWorkAnatomyCompiler
    {
        public static bool TryCompile(
            Graph graph,
            SpecialWorkDefinition definition,
            IReadOnlyList<WheelPath> wheelPaths,
            IReadOnlyList<RailCenterline> rails,
            out SpecialWorkAnatomyPlan plan)
        {
            var switches = new List<SpecialWorkAnatomySwitch>();
            var roles = new List<SpecialWorkRailRoleAssignment>();
            var issues = new List<string>();

            switch (definition.Preset.Id)
            {
                case SpecialWorkPresetIds.DualNarrowBranch:
                    TryCompileDualBranch(
                        graph,
                        definition,
                        GaugeGraphFamily.Narrow,
                        "narrow",
                        IsRealNarrowOnly,
                        switches,
                        roles,
                        issues);
                    break;

                case SpecialWorkPresetIds.DualStandardBranch:
                    TryCompileDualBranch(
                        graph,
                        definition,
                        GaugeGraphFamily.Standard,
                        "standard",
                        IsStandardOnly,
                        switches,
                        roles,
                        issues);
                    break;

            }

            plan = new SpecialWorkAnatomyPlan(switches, roles, issues);
            return switches.Count > 0;
        }

        private static void TryCompileDualBranch(
            Graph graph,
            SpecialWorkDefinition definition,
            GaugeGraphFamily family,
            string switchGroupId,
            Func<TrackSegment?, bool> branchSegmentPredicate,
            ICollection<SpecialWorkAnatomySwitch> switches,
            ICollection<SpecialWorkRailRoleAssignment> roles,
            ICollection<string> issues)
        {
            LogicalRoute[] familyRoutes = definition.Routes
                .Where(route =>
                    route.Family == family
                    && string.Equals(route.SwitchGroupId, switchGroupId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (familyRoutes.Length < 2)
            {
                issues.Add($"No route pair found for switch group '{switchGroupId}'.");
                return;
            }

            LogicalRoute? diverging = familyRoutes.FirstOrDefault(route =>
                route.SourceSegmentIds
                    .Select(id => SafeGetSegment(graph, id))
                    .Any(branchSegmentPredicate));
            LogicalRoute? through = familyRoutes.FirstOrDefault(route => route != diverging);
            if (diverging == null || through == null)
            {
                ChooseNormalReversed(familyRoutes, out through, out diverging);
            }

            if (through == null || diverging == null)
            {
                issues.Add($"Could not resolve through/diverging routes for '{switchGroupId}'.");
                return;
            }

            AddSwitch(
                graph,
                definition,
                family,
                switchGroupId,
                through,
                diverging,
                switches,
                roles);
        }

        private static void TryCompileBinaryFamily(
            Graph graph,
            SpecialWorkDefinition definition,
            GaugeGraphFamily family,
            string switchGroupId,
            ICollection<SpecialWorkAnatomySwitch> switches,
            ICollection<SpecialWorkRailRoleAssignment> roles,
            ICollection<string> issues)
        {
            LogicalRoute[] familyRoutes = definition.Routes
                .Where(route =>
                    route.Family == family
                    && string.Equals(route.SwitchGroupId, switchGroupId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            ChooseNormalReversed(familyRoutes, out LogicalRoute? through, out LogicalRoute? diverging);
            if (through == null || diverging == null)
            {
                issues.Add($"Could not resolve normal/reversed routes for '{switchGroupId}'.");
                return;
            }

            AddSwitch(
                graph,
                definition,
                family,
                switchGroupId,
                through,
                diverging,
                switches,
                roles);
        }

        private static void AddSwitch(
            Graph graph,
            SpecialWorkDefinition definition,
            GaugeGraphFamily family,
            string switchGroupId,
            LogicalRoute through,
            LogicalRoute diverging,
            ICollection<SpecialWorkAnatomySwitch> switches,
            ICollection<SpecialWorkRailRoleAssignment> roles)
        {
            string? nativeNodeId = FindNativeNodeId(definition, family, switchGroupId);
            Vector3 switchPoint = ResolveSwitchPoint(graph, nativeNodeId, through, diverging);
            SpecialWorkHandedness handedness = DetermineHandedness(
                through.Centerline,
                diverging.Centerline,
                switchPoint);
            RailSide movableSide = handedness == SpecialWorkHandedness.Right
                ? RailSide.Left
                : RailSide.Right;

            switches.Add(new SpecialWorkAnatomySwitch(
                switchGroupId,
                nativeNodeId,
                family,
                through,
                diverging,
                handedness,
                movableSide));

            roles.Add(new SpecialWorkRailRoleAssignment(
                through.Id,
                movableSide,
                RailRole.StockRail,
                "through stock rail for point blade"));
            roles.Add(new SpecialWorkRailRoleAssignment(
                diverging.Id,
                movableSide,
                RailRole.PointBlade,
                "diverging point/closure rail"));
            roles.Add(new SpecialWorkRailRoleAssignment(
                diverging.Id,
                Opposite(movableSide),
                RailRole.StockRail,
                "diverging outside stock rail"));
            roles.Add(new SpecialWorkRailRoleAssignment(
                through.Id,
                Opposite(movableSide),
                RailRole.StockRail,
                "through outside stock rail"));
        }

        private static void ChooseNormalReversed(
            IReadOnlyList<LogicalRoute> routes,
            out LogicalRoute? normal,
            out LogicalRoute? reversed)
        {
            normal = routes.FirstOrDefault(route =>
                string.Equals(route.RequiredStateId, "normal", StringComparison.OrdinalIgnoreCase));
            reversed = routes.FirstOrDefault(route =>
                string.Equals(route.RequiredStateId, "reversed", StringComparison.OrdinalIgnoreCase));
            if (normal != null && reversed != null)
            {
                return;
            }

            normal = routes.FirstOrDefault();
            reversed = routes.Skip(1).FirstOrDefault();
        }

        private static string? FindNativeNodeId(
            SpecialWorkDefinition definition,
            GaugeGraphFamily family,
            string switchGroupId)
        {
            SpecialWorkSwitchGroup? group = definition.SwitchGroups.FirstOrDefault(item =>
                string.Equals(item.Id, switchGroupId, StringComparison.OrdinalIgnoreCase));
            string? fromGroup = group?.NativeNodeIds.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(fromGroup))
            {
                return fromGroup;
            }

            return definition.NativeSwitchNodeIds.FirstOrDefault(id =>
                family == GaugeGraphFamily.Narrow
                    ? GhostGraphSynchronizer.IsGeneratedGhostNodeId(id)
                    : !GhostGraphSynchronizer.IsGeneratedGhostNodeId(id))
                ?? definition.NativeSwitchNodeIds.FirstOrDefault();
        }

        private static Vector3 ResolveSwitchPoint(
            Graph graph,
            string? nativeNodeId,
            LogicalRoute through,
            LogicalRoute diverging)
        {
            TrackNode? node = string.IsNullOrWhiteSpace(nativeNodeId)
                ? null
                : graph.GetNode(nativeNodeId);
            if (node != null)
            {
                return node.transform.localPosition;
            }

            return ClosestApproach(through.Centerline, diverging.Centerline);
        }

        private static SpecialWorkHandedness DetermineHandedness(
            LineCurve through,
            LineCurve diverging,
            Vector3 switchPoint)
        {
            Vector3 throughDirection = DirectionAfter(through, switchPoint);
            Vector3 divergingDirection = DirectionAfter(diverging, switchPoint);
            float signed = Vector3.SignedAngle(throughDirection, divergingDirection, Vector3.up);
            return signed < 0f ? SpecialWorkHandedness.Left : SpecialWorkHandedness.Right;
        }

        private static Vector3 DirectionAfter(LineCurve curve, Vector3 switchPoint)
        {
            float distance = Mathf.Clamp(curve.DistanceTo(switchPoint), 0f, curve.Length);
            float ahead = Mathf.Min(curve.Length, distance + 1f);
            Vector3 start = curve.LinePointAtDistance(distance).point;
            Vector3 end = curve.LinePointAtDistance(ahead).point;
            Vector3 direction = end - start;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
            {
                return direction.normalized;
            }

            direction = curve.LinePointAtDistance(distance).direction;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector3.forward;
        }

        private static Vector3 ClosestApproach(LineCurve a, LineCurve b)
        {
            Vector3 best = a.Head.point;
            float bestDistance = float.MaxValue;
            int samples = Mathf.Max(2, Mathf.CeilToInt(a.Length / 0.5f));
            for (int index = 0; index <= samples; index++)
            {
                float distance = Mathf.Lerp(0f, a.Length, (float)index / samples);
                Vector3 point = a.LinePointAtDistance(distance).point;
                float otherDistance = b.DistanceTo(point);
                Vector3 other = b.LinePointAtDistance(otherDistance).point;
                float separation = Vector3.Distance(point, other);
                if (separation < bestDistance)
                {
                    bestDistance = separation;
                    best = Vector3.Lerp(point, other, 0.5f);
                }
            }

            return best;
        }

        private static TrackSegment? SafeGetSegment(Graph graph, string segmentId)
        {
            if (graph == null || string.IsNullOrWhiteSpace(segmentId))
            {
                return null;
            }

            try
            {
                return graph.GetSegment(segmentId);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsRealNarrowOnly(TrackSegment? segment)
        {
            return segment != null
                && NarrowGaugeManager.IsNarrowGauge(segment)
                && !NarrowGaugeManager.IsGeneratedGhost(segment)
                && !NarrowGaugeManager.IsDualGauge(segment);
        }

        private static bool IsStandardOnly(TrackSegment? segment)
        {
            return segment != null
                && !NarrowGaugeManager.IsNarrowGauge(segment)
                && !NarrowGaugeManager.IsGeneratedGhost(segment)
                && !NarrowGaugeManager.IsDualGauge(segment);
        }

        private static RailSide Opposite(RailSide side)
        {
            return side == RailSide.Left ? RailSide.Right : RailSide.Left;
        }
    }
}
