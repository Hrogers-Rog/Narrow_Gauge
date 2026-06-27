using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Track;
using UnityEngine;

namespace NarrowGaugeMod
{
    internal static class SpecialWorkRuntimeRegistry
    {
        private static readonly List<SpecialWorkAnalysis> CurrentAnalyses =
            new List<SpecialWorkAnalysis>();

        public static IReadOnlyList<SpecialWorkAnalysis> Analyses => CurrentAnalyses;

        public static SpecialWorkAnalysis? FindByNativeNodeId(string? nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return null;
            }

            return CurrentAnalyses.FirstOrDefault(analysis =>
                analysis.Definition.NativeSwitchNodeIds.Contains(
                    nodeId,
                    StringComparer.OrdinalIgnoreCase));
        }

        public static void Replace(IEnumerable<SpecialWorkAnalysis> analyses)
        {
            CurrentAnalyses.Clear();
            CurrentAnalyses.AddRange(analyses ?? Enumerable.Empty<SpecialWorkAnalysis>());
        }

        public static void Clear()
        {
            CurrentAnalyses.Clear();
        }
    }

    internal static class SpecialWorkRuntimeDiscovery
    {
        public static IReadOnlyList<SpecialWorkDefinition> Discover(Graph graph)
        {
            var definitions = new List<SpecialWorkDefinition>();
            if (graph == null)
            {
                return definitions;
            }

            TrackSegment[] allSegments = graph.Segments
                .Where(segment => segment != null)
                .ToArray();

            foreach (TrackNode node in graph.Nodes
                .Where(node => node != null)
                .OrderBy(node => node.id, StringComparer.OrdinalIgnoreCase))
            {
                if (NarrowGaugeManager.IsGeneratedGhostNode(node))
                {
                    if (TryBuildGaugeSeparation(
                        graph,
                        node,
                        allSegments,
                        out SpecialWorkDefinition separation))
                    {
                        definitions.Add(separation);
                    }
                    else if (TryBuildNarrowBranchTransition(
                        graph,
                        node,
                        allSegments,
                        out SpecialWorkDefinition transition))
                    {
                        definitions.Add(transition);
                    }

                    continue;
                }

                TrackSegment[] connectedSources = Connected(node, allSegments)
                    .Where(segment => !NarrowGaugeManager.IsGeneratedGhost(segment))
                    .ToArray();

                if (connectedSources.Length != 3
                    || !connectedSources.Any(segment =>
                        NarrowGaugeManager.IsNarrowGauge(segment)
                        || NarrowGaugeManager.IsDualGauge(segment)))
                {
                    continue;
                }

                int dualCount = connectedSources.Count(NarrowGaugeManager.IsDualGauge);
                int narrowOnlyCount = connectedSources.Count(IsRealNarrowOnly);
                int standardOnlyCount = connectedSources.Length - dualCount - narrowOnlyCount;

                if (dualCount == 3
                    && TryBuildDualBothDiverge(graph, node, allSegments, out SpecialWorkDefinition dual))
                {
                    definitions.Add(dual);
                }
                else if (dualCount == 2
                    && standardOnlyCount == 1
                    && TryBuildStandardBranchTransition(graph, node, allSegments, out SpecialWorkDefinition standardBranch))
                {
                    definitions.Add(standardBranch);
                }
                else if (narrowOnlyCount == 3
                    && TryBuildSingleFamilySwitch(
                        graph,
                        node,
                        GaugeGraphFamily.Narrow,
                        ResolveBinaryPreset(graph, node, GaugeGraphFamily.Narrow),
                        out SpecialWorkDefinition narrow))
                {
                    definitions.Add(narrow);
                }
            }

            return definitions;
        }

        private static bool TryBuildGaugeSeparation(
            Graph graph,
            TrackNode ghostControlNode,
            IReadOnlyCollection<TrackSegment> allSegments,
            out SpecialWorkDefinition definition)
        {
            definition = null!;
            TrackSegment[] connected = Connected(ghostControlNode, allSegments).ToArray();
            TrackSegment? hiddenControl = connected.FirstOrDefault(
                SpecialWorkTopologySynchronizer.IsHiddenControlSegment);
            TrackSegment? ghostDual = connected.FirstOrDefault(segment =>
                NarrowGaugeManager.IsGeneratedGhost(segment)
                && !SpecialWorkTopologySynchronizer.IsHiddenControlSegment(segment));
            TrackSegment? narrowBranch = connected.FirstOrDefault(IsRealNarrowOnly);
            if (connected.Length != 3
                || hiddenControl == null
                || ghostDual == null
                || narrowBranch == null
                || !TryGetSourceNodeForGhost(ghostControlNode, graph, out TrackNode sourceNode))
            {
                return false;
            }

            TrackSegment[] standardMain = Connected(sourceNode, allSegments)
                .Where(segment => !NarrowGaugeManager.IsGeneratedGhost(segment))
                .Where(segment => !IsRealNarrowOnly(segment))
                .ToArray();
            if (standardMain.Length != 2
                || standardMain.Count(NarrowGaugeManager.IsDualGauge) != 1
                || !TryBuildFixedRoute(
                    standardMain[0],
                    standardMain[1],
                    sourceNode,
                    GaugeGraphFamily.Standard,
                    "standard-through",
                    out LogicalRoute standardRoute)
                || !TryBuildFixedRoute(
                    ghostDual,
                    narrowBranch,
                    ghostControlNode,
                    GaugeGraphFamily.Narrow,
                    "narrow-diverge",
                    out LogicalRoute narrowDivergeRoute)
                || !TryBuildRoute(
                    ghostDual,
                    hiddenControl,
                    ghostControlNode,
                    GaugeGraphFamily.Narrow,
                    "narrow-through",
                    "narrow-separation",
                    "normal",
                    out LogicalRoute narrowThroughRoute))
            {
                return false;
            }

            definition = new SpecialWorkDefinition(
                "special-work:" + sourceNode.id,
                SpecialWorkPresetCatalog.Get(SpecialWorkPresetIds.DualSplit),
                BuildDualBranchPorts(sourceNode, standardMain, narrowBranch),
                new[]
                {
                    standardRoute,
                    narrowThroughRoute,
                    new LogicalRoute(
                        narrowDivergeRoute.Id,
                        narrowDivergeRoute.Family,
                        narrowDivergeRoute.Centerline,
                        narrowDivergeRoute.SourceSegmentIds,
                        "narrow-separation",
                        "reversed")
                },
                new[]
                {
                    new SpecialWorkSwitchGroup(
                        "narrow-separation",
                        new[] { ghostControlNode.id },
                        new[] { "normal", "reversed" })
                },
                new[] { ghostControlNode.id });
            return true;
        }

        private static bool TryBuildNarrowBranchTransition(
            Graph graph,
            TrackNode ghostSwitchNode,
            IReadOnlyCollection<TrackSegment> allSegments,
            out SpecialWorkDefinition definition)
        {
            definition = null!;
            TrackSegment[] connected = Connected(ghostSwitchNode, allSegments).ToArray();
            if (connected.Length != 3
                || connected.Count(NarrowGaugeManager.IsGeneratedGhost) != 2
                || connected.Count(IsRealNarrowOnly) != 1
                || !TryGetSourceNodeForGhost(ghostSwitchNode, graph, out TrackNode sourceNode))
            {
                return false;
            }

            TrackSegment[] standardMain = Connected(sourceNode, allSegments)
                .Where(segment => !NarrowGaugeManager.IsGeneratedGhost(segment))
                .Where(segment => !IsRealNarrowOnly(segment))
                .ToArray();
            if (standardMain.Length != 2
                || !standardMain.Any(NarrowGaugeManager.IsDualGauge)
                || !TryBuildFixedRoute(
                    standardMain[0],
                    standardMain[1],
                    sourceNode,
                    GaugeGraphFamily.Standard,
                    "standard-through",
                    out LogicalRoute standardRoute)
                || !TryBuildSwitchRoutes(
                    graph,
                    ghostSwitchNode,
                    GaugeGraphFamily.Narrow,
                    "narrow",
                    out LogicalRoute[] narrowRoutes))
            {
                return false;
            }

            TrackSegment narrowBranch = connected.First(IsRealNarrowOnly);
            var routes = new List<LogicalRoute> { standardRoute };
            routes.AddRange(narrowRoutes);

            definition = new SpecialWorkDefinition(
                "special-work:" + sourceNode.id,
                SpecialWorkPresetCatalog.Get(SpecialWorkPresetIds.DualNarrowBranch),
                BuildDualBranchPorts(sourceNode, standardMain, narrowBranch),
                routes,
                new[]
                {
                    new SpecialWorkSwitchGroup(
                        "narrow",
                        new[] { ghostSwitchNode.id },
                        new[] { "normal", "reversed" })
                },
                new[] { ghostSwitchNode.id });
            return true;
        }

        private static bool TryBuildStandardBranchTransition(
            Graph graph,
            TrackNode standardSwitchNode,
            IReadOnlyCollection<TrackSegment> allSegments,
            out SpecialWorkDefinition definition)
        {
            definition = null!;
            TrackSegment[] connected = Connected(standardSwitchNode, allSegments)
                .Where(segment => !NarrowGaugeManager.IsGeneratedGhost(segment))
                .ToArray();
            TrackSegment[] dualMain = connected.Where(NarrowGaugeManager.IsDualGauge).ToArray();
            TrackSegment? standardBranch = connected.FirstOrDefault(segment =>
                !NarrowGaugeManager.IsDualGauge(segment)
                && !NarrowGaugeManager.IsNarrowGauge(segment));
            TrackNode? ghostNode = graph.GetNode(GhostGraphSynchronizer.GetGhostNodeId(standardSwitchNode.id));
            TrackSegment[] ghostMain = ghostNode == null
                ? Array.Empty<TrackSegment>()
                : Connected(ghostNode, allSegments).Where(NarrowGaugeManager.IsGeneratedGhost).ToArray();

            if (dualMain.Length != 2
                || standardBranch == null
                || ghostNode == null
                || ghostMain.Length != 2
                || !TryBuildSwitchRoutes(
                    graph,
                    standardSwitchNode,
                    GaugeGraphFamily.Standard,
                    "standard",
                    out LogicalRoute[] standardRoutes)
                || !TryBuildFixedRoute(
                    ghostMain[0],
                    ghostMain[1],
                    ghostNode,
                    GaugeGraphFamily.Narrow,
                    "narrow-through",
                    out LogicalRoute narrowRoute))
            {
                return false;
            }

            var routes = new List<LogicalRoute>(standardRoutes) { narrowRoute };
            definition = new SpecialWorkDefinition(
                "special-work:" + standardSwitchNode.id,
                SpecialWorkPresetCatalog.Get(SpecialWorkPresetIds.DualStandardBranch),
                BuildDualBranchPorts(standardSwitchNode, dualMain, standardBranch),
                routes,
                new[]
                {
                    new SpecialWorkSwitchGroup(
                        "standard",
                        new[] { standardSwitchNode.id },
                        new[] { "normal", "reversed" })
                },
                new[] { standardSwitchNode.id });
            return true;
        }

        private static bool TryBuildDualBothDiverge(
            Graph graph,
            TrackNode standardSwitchNode,
            IReadOnlyCollection<TrackSegment> allSegments,
            out SpecialWorkDefinition definition)
        {
            definition = null!;
            TrackNode narrowSwitchNode = graph.GetNode(GhostGraphSynchronizer.GetGhostNodeId(standardSwitchNode.id));
            if (narrowSwitchNode == null
                || !TryBuildSwitchRoutes(
                    graph,
                    standardSwitchNode,
                    GaugeGraphFamily.Standard,
                    "standard",
                    out LogicalRoute[] standardRoutes)
                || !TryBuildSwitchRoutes(
                    graph,
                    narrowSwitchNode,
                    GaugeGraphFamily.Narrow,
                    "narrow",
                    out LogicalRoute[] narrowRoutes))
            {
                return false;
            }

            TrackSegment[] connected = Connected(standardSwitchNode, allSegments)
                .Where(NarrowGaugeManager.IsDualGauge)
                .ToArray();
            var routes = new List<LogicalRoute>(standardRoutes);
            routes.AddRange(narrowRoutes);

            definition = new SpecialWorkDefinition(
                "special-work:" + standardSwitchNode.id,
                SpecialWorkPresetCatalog.Get(SpecialWorkPresetIds.DualBothDiverge),
                connected.Select((segment, index) => new SpecialWorkPort(
                    "dual-" + index,
                    GaugeAvailability.Dual,
                    segment.GetOtherNode(standardSwitchNode))),
                routes,
                new[]
                {
                    new SpecialWorkSwitchGroup(
                        "standard",
                        new[] { standardSwitchNode.id },
                        new[] { "normal", "reversed" }),
                    new SpecialWorkSwitchGroup(
                        "narrow",
                        new[] { narrowSwitchNode.id },
                        new[] { "normal", "reversed" })
                },
                new[] { standardSwitchNode.id, narrowSwitchNode.id });
            return true;
        }

        private static bool TryBuildSingleFamilySwitch(
            Graph graph,
            TrackNode switchNode,
            GaugeGraphFamily family,
            SpecialWorkPresetDefinition preset,
            out SpecialWorkDefinition definition)
        {
            definition = null!;
            if (!TryBuildSwitchRoutes(graph, switchNode, family, "switch", out LogicalRoute[] routes))
            {
                return false;
            }

            TrackSegment[] connected = graph.SegmentsConnectedTo(switchNode).ToArray();
            GaugeAvailability availability = family == GaugeGraphFamily.Narrow
                ? GaugeAvailability.Narrow
                : GaugeAvailability.Standard;

            definition = new SpecialWorkDefinition(
                "special-work:" + switchNode.id,
                preset,
                connected.Select((segment, index) => new SpecialWorkPort(
                    "port-" + index,
                    availability,
                    segment.GetOtherNode(switchNode))),
                routes,
                new[]
                {
                    new SpecialWorkSwitchGroup(
                        "switch",
                        new[] { switchNode.id },
                        new[] { "normal", "reversed" })
                },
                new[] { switchNode.id });
            return true;
        }

        private static SpecialWorkPresetDefinition ResolveBinaryPreset(
            Graph graph,
            TrackNode node,
            GaugeGraphFamily family)
        {
            if (!graph.DecodeSwitchAt(node, out TrackSegment enter, out TrackSegment normal, out TrackSegment reversed))
            {
                return SpecialWorkPresetCatalog.Get(
                    family == GaugeGraphFamily.Narrow
                        ? SpecialWorkPresetIds.NarrowRight
                        : SpecialWorkPresetIds.StandardRight);
            }

            Vector3 entryDirection = DirectionAwayFromNode(enter, node);
            Vector3 reversedDirection = DirectionAwayFromNode(reversed, node);
            float signed = Vector3.SignedAngle(entryDirection, reversedDirection, Vector3.up);
            string id;
            if (Mathf.Abs(Vector3.SignedAngle(
                    DirectionAwayFromNode(normal, node),
                    reversedDirection,
                    Vector3.up)) > 15f
                && Mathf.Abs(Vector3.SignedAngle(entryDirection, DirectionAwayFromNode(normal, node), Vector3.up)) > 10f)
            {
                id = family == GaugeGraphFamily.Narrow
                    ? SpecialWorkPresetIds.NarrowWye
                    : SpecialWorkPresetIds.StandardWye;
            }
            else if (signed < 0f)
            {
                id = family == GaugeGraphFamily.Narrow
                    ? SpecialWorkPresetIds.NarrowLeft
                    : SpecialWorkPresetIds.StandardLeft;
            }
            else
            {
                id = family == GaugeGraphFamily.Narrow
                    ? SpecialWorkPresetIds.NarrowRight
                    : SpecialWorkPresetIds.StandardRight;
            }

            return SpecialWorkPresetCatalog.Get(id);
        }

        private static IEnumerable<SpecialWorkPort> BuildDualBranchPorts(
            TrackNode sourceNode,
            IReadOnlyList<TrackSegment> dualMain,
            TrackSegment branch)
        {
            yield return new SpecialWorkPort(
                "dual-west",
                PortAvailability(dualMain[0]),
                dualMain[0].GetOtherNode(sourceNode));
            yield return new SpecialWorkPort(
                "dual-east",
                PortAvailability(dualMain[1]),
                dualMain[1].GetOtherNode(sourceNode));
            yield return new SpecialWorkPort(
                "branch",
                PortAvailability(branch),
                branch.a == sourceNode || NarrowGaugeManager.IsGeneratedGhostNode(branch.a)
                    ? branch.b
                    : branch.a);
        }

        private static GaugeAvailability PortAvailability(TrackSegment segment)
        {
            if (NarrowGaugeManager.IsDualGauge(segment))
            {
                return GaugeAvailability.Dual;
            }

            return IsRealNarrowOnly(segment)
                ? GaugeAvailability.Narrow
                : GaugeAvailability.Standard;
        }

        private static bool TryBuildSwitchRoutes(
            Graph graph,
            TrackNode node,
            GaugeGraphFamily family,
            string groupId,
            out LogicalRoute[] routes)
        {
            routes = Array.Empty<LogicalRoute>();
            if (!graph.DecodeSwitchAt(node, out TrackSegment enter, out TrackSegment normal, out TrackSegment reversed)
                || !TryBuildRoute(enter, normal, node, family, groupId + "-normal", groupId, "normal", out LogicalRoute normalRoute)
                || !TryBuildRoute(enter, reversed, node, family, groupId + "-reversed", groupId, "reversed", out LogicalRoute reversedRoute))
            {
                return false;
            }

            routes = new[] { normalRoute, reversedRoute };
            return true;
        }

        private static bool TryBuildFixedRoute(
            TrackSegment first,
            TrackSegment second,
            TrackNode node,
            GaugeGraphFamily family,
            string id,
            out LogicalRoute route)
        {
            return TryBuildRoute(first, second, node, family, id, null, null, out route);
        }

        private static bool TryBuildRoute(
            TrackSegment incoming,
            TrackSegment outgoing,
            TrackNode sharedNode,
            GaugeGraphFamily family,
            string id,
            string? switchGroupId,
            string? requiredStateId,
            out LogicalRoute route)
        {
            route = null!;
            try
            {
                LineCurve incomingCurve = OrientedSegmentCurve(incoming, sharedNode, towardNode: true);
                LineCurve outgoingCurve = OrientedSegmentCurve(outgoing, sharedNode, towardNode: false);
                var points = incomingCurve.Points.ToList();
                points.AddRange(outgoingCurve.Points.Skip(1));

                route = new LogicalRoute(
                    id,
                    family,
                    new LineCurve(points, Hand.Left),
                    new[] { incoming.id, outgoing.id },
                    switchGroupId,
                    requiredStateId);
                return true;
            }
            catch (Exception ex)
            {
                Main.Warn($"Failed to build logical route '{id}' at '{sharedNode?.id}': {ex.Message}");
                return false;
            }
        }

        internal static LineCurve OrientedSegmentCurve(
            TrackSegment segment,
            TrackNode node,
            bool towardNode)
        {
            var curve = new LineCurve(
                segment.Curve.Approximate(1.000005f, 0.25f, 16, 20f),
                Hand.Left);
            bool curveRunsAtoB = DualGaugeSharedRailRegistry.CurveRunsAtoB(segment);
            bool nodeAtCurveStart = segment.a == node
                ? curveRunsAtoB
                : !curveRunsAtoB;
            bool naturalTowardNode = !nodeAtCurveStart;
            if (naturalTowardNode == towardNode)
            {
                return curve;
            }

            LinePoint[] reversed = curve.Points.Reverse().ToArray();
            for (int i = 0; i < reversed.Length; i++)
            {
                Vector3 tangent;
                if (i == 0 && reversed.Length > 1)
                    tangent = reversed[1].point - reversed[0].point;
                else if (i == reversed.Length - 1 && reversed.Length > 1)
                    tangent = reversed[i].point - reversed[i - 1].point;
                else if (reversed.Length > 2)
                    tangent = reversed[i + 1].point - reversed[i - 1].point;
                else
                    tangent = Vector3.forward;

                tangent.y = 0f;
                if (tangent.sqrMagnitude > 0.0001f)
                    reversed[i] = new LinePoint(reversed[i].point,
                        Quaternion.LookRotation(tangent.normalized, Vector3.up));
            }

            return new LineCurve(reversed, curve.hand);
        }

        private static Vector3 DirectionAwayFromNode(TrackSegment segment, TrackNode node)
        {
            LineCurve curve = OrientedSegmentCurve(segment, node, towardNode: false);
            Vector3 direction = curve.Points.Skip(1).First().point - curve.Head.point;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        }

        private static IEnumerable<TrackSegment> Connected(
            TrackNode node,
            IEnumerable<TrackSegment> segments)
        {
            return segments.Where(segment => segment.a == node || segment.b == node);
        }

        private static bool TryGetSourceNodeForGhost(
            TrackNode ghostNode,
            Graph graph,
            out TrackNode sourceNode)
        {
            sourceNode = null!;
            if (ghostNode == null
                || string.IsNullOrEmpty(ghostNode.id)
                || !GhostGraphSynchronizer.IsGeneratedGhostNodeId(ghostNode.id))
            {
                return false;
            }

            string sourceId = ghostNode.id.Substring(GhostGraphSynchronizer.GeneratedNodePrefix.Length);
            sourceNode = graph.GetNode(sourceId);
            return sourceNode != null;
        }

        private static bool IsRealNarrowOnly(TrackSegment segment)
        {
            return segment != null
                && NarrowGaugeManager.IsNarrowGauge(segment)
                && !NarrowGaugeManager.IsGeneratedGhost(segment)
                && !NarrowGaugeManager.IsDualGauge(segment);
        }
    }
}
