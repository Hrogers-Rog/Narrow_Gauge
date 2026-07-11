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
                    // A gauge-separation control also satisfies the generic
                    // narrow-branch shape (ghost + narrow + hidden). Recognize the
                    // exact topology first so the hidden blocked leg cannot become
                    // a second measured route and blade.
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

                if (dualCount == 1
                    && narrowOnlyCount == 1
                    && standardOnlyCount == 1
                    && TryBuildAuthoredDualStandardNarrowJoin(
                        graph,
                        node,
                        connectedSources,
                        allSegments,
                        out SpecialWorkDefinition split))
                {
                    definitions.Add(split);
                }
                else if (dualCount == 3
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

        private static bool TryBuildAuthoredDualStandardNarrowJoin(
            Graph graph,
            TrackNode sourceNode,
            IReadOnlyCollection<TrackSegment> connectedSources,
            IReadOnlyCollection<TrackSegment> allSegments,
            out SpecialWorkDefinition definition)
        {
            definition = null!;
            TrackSegment? dual = connectedSources.SingleOrDefault(
                NarrowGaugeManager.IsDualGauge);
            TrackSegment? standard = connectedSources.SingleOrDefault(segment =>
                !NarrowGaugeManager.IsDualGauge(segment)
                && !NarrowGaugeManager.IsNarrowGauge(segment));
            TrackSegment? narrowBranch = connectedSources.SingleOrDefault(IsRealNarrowOnly);
            TrackNode? ghostNode = graph.GetNode(
                GhostGraphSynchronizer.GetGhostNodeId(sourceNode.id));
            TrackSegment? ghostDual = dual == null
                ? null
                : allSegments.SingleOrDefault(segment =>
                    string.Equals(
                        segment.id,
                        GhostGraphSynchronizer.GetGhostSegmentId(dual.id),
                        StringComparison.OrdinalIgnoreCase));
            if (dual == null
                || standard == null
                || narrowBranch == null
                || ghostNode == null
                || ghostDual == null
                || !TryBuildFixedRoute(
                    dual,
                    standard,
                    sourceNode,
                    GaugeGraphFamily.Standard,
                    "standard-through",
                    out LogicalRoute standardRoute))
            {
                return false;
            }

            string narrowState = ResolveRouteStateId(
                graph,
                sourceNode,
                dual,
                narrowBranch) ?? "reversed";
            if (!TryBuildNarrowJoinRouteFromShadowTransition(
                    sourceNode,
                    ghostDual,
                    narrowBranch,
                    narrowState,
                    out LogicalRoute narrowRoute))
            {
                return false;
            }

            definition = new SpecialWorkDefinition(
                "special-work:" + sourceNode.id,
                SpecialWorkPresetCatalog.Get(SpecialWorkPresetIds.DualSplit),
                BuildDualBranchPorts(
                    sourceNode,
                    new[] { dual, standard },
                    narrowBranch),
                new[] { standardRoute, narrowRoute },
                new[]
                {
                    new SpecialWorkSwitchGroup(
                        "narrow-separation",
                        new[] { sourceNode.id },
                        new[] { "normal", "reversed" })
                },
                new[] { sourceNode.id });
            Main.Log(
                $"[SpecialWorkDiscovery] Authored dual/standard/narrow join '{sourceNode.id}' " +
                $"uses two real routes and one shared-rail blade; narrowState={narrowState}.");
            return true;
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
            TrackSegment? dualMain = standardMain.SingleOrDefault(
                NarrowGaugeManager.IsDualGauge);
            TrackSegment? standardOnly = standardMain.SingleOrDefault(segment =>
                !NarrowGaugeManager.IsDualGauge(segment)
                && !NarrowGaugeManager.IsNarrowGauge(segment));
            if (standardMain.Length != 2
                || dualMain == null
                || standardOnly == null
                || !TryBuildFixedRoute(
                    dualMain,
                    standardOnly,
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
                    out LogicalRoute narrowDivergeRoute))
            {
                return false;
            }

            string narrowState = ResolveRouteStateId(
                graph,
                ghostControlNode,
                ghostDual,
                narrowBranch) ?? "reversed";
            var narrowRoute = new LogicalRoute(
                narrowDivergeRoute.Id,
                narrowDivergeRoute.Family,
                narrowDivergeRoute.Centerline,
                narrowDivergeRoute.SourceSegmentIds,
                "narrow-separation",
                narrowState);

            definition = new SpecialWorkDefinition(
                "special-work:" + sourceNode.id,
                SpecialWorkPresetCatalog.Get(SpecialWorkPresetIds.DualSplit),
                BuildDualBranchPorts(sourceNode, standardMain, narrowBranch),
                new[] { standardRoute, narrowRoute },
                new[]
                {
                    new SpecialWorkSwitchGroup(
                        "narrow-separation",
                        new[] { ghostControlNode.id },
                        new[] { "normal", "reversed" })
                },
                new[] { ghostControlNode.id });
            Main.Log(
                $"[SpecialWorkDiscovery] Graph-connected dual/standard/narrow join " +
                $"'{sourceNode.id}' uses ghost switch '{ghostControlNode.id}', two real " +
                $"routes, and one blade; narrowState={narrowState}. Hidden control " +
                "is topology-only.");
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
            int ghostOrControlCount = connected.Count(segment =>
                NarrowGaugeManager.IsGeneratedGhost(segment)
                || SpecialWorkTopologySynchronizer.IsHiddenControlSegment(segment));
            if (connected.Length != 3
                || ghostOrControlCount < 2
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

        private static bool TryBuildNarrowJoinRouteFromShadowTransition(
            TrackNode sourceNode,
            TrackSegment ghostDual,
            TrackSegment narrowBranch,
            string requiredStateId,
            out LogicalRoute route)
        {
            route = null!;
            try
            {
                ShadowNarrowGaugeTransition? transition =
                    NarrowGaugeManager.GetShadowTransition(sourceNode);
                if (transition == null)
                {
                    Main.Warn(
                        $"Failed to build narrow join route at '{sourceNode?.id}': " +
                        "no dual-to-narrow shadow transition was available.");
                    return false;
                }

                // The generated narrow node is deliberately offset from the authored
                // standard centerline by the dual-gauge third-rail centerline offset
                // (0.260m at NCustom_7n90). The shared point rail must stay on that
                // offset all the way to the switch toe, then peel gradually onto the
                // authored narrow branch - the same physical throat pattern used by
                // N178/vdlt. The generic shadow preview is symmetric around the source
                // node (2m on each side); using it here puts the route halfway between
                // the two centerlines at the toe, creates the visible kink, and reaches
                // blade-root separation in only 0.5m. Build the join asymmetrically:
                // full offset dual approach to the toe, then a long transition solely
                // on the narrow-branch side.
                // Use the generated ghost segment as the authoritative narrow
                // centerline of the dual leg. The shadow transition's dual anchor
                // is derived from the authored source segment and Nove reaches the
                // node from its opposite authored end; its plain Reverse() retained
                // the wrong frame and live geometry arrived on the standard
                // centerline (0.271m from its stock rail). The generated segment
                // already contains the correct physical third-rail offset for both
                // authored traversal directions.
                string ghostNodeId = GhostGraphSynchronizer.GetGhostNodeId(sourceNode.id);
                TrackNode? ghostNode = new[] { ghostDual.a, ghostDual.b }
                    .FirstOrDefault(node => node != null
                        && string.Equals(
                            node.id,
                            ghostNodeId,
                            StringComparison.OrdinalIgnoreCase));
                if (ghostNode == null)
                {
                    Main.Warn(
                        $"Failed to build narrow join route at '{sourceNode?.id}': " +
                        $"generated dual segment '{ghostDual?.id}' does not touch " +
                        $"its expected ghost node '{ghostNodeId}'.");
                    return false;
                }

                LineCurve dualApproach = OrientedSegmentCurve(
                    ghostDual,
                    ghostNode,
                    towardNode: true);
                LineCurve fullNarrowDeparture = transition.NarrowAnchor.OrientedCurve;
                // Use one continuous development curve from the authored toe to
                // the narrow branch. The previous 2m straight lead followed by a
                // 7m cubic only moved the same nine-metre handoff into two pieces:
                // it did not close the post-frog render gap, and the curvature
                // break between those pieces produced the visible S-kink.
                const float narrowTransitionSpan = 9f;
                float narrowTransitionLength = Mathf.Min(
                    narrowTransitionSpan,
                    Mathf.Max(fullNarrowDeparture.Length - 0.05f, 0.05f));
                LinePoint narrowTransitionEnd =
                    fullNarrowDeparture.LinePointAtDistance(narrowTransitionLength);

                Vector3 startPoint = dualApproach.Tail.point;
                float toeCenterOffset = Vector3.Distance(
                    startPoint,
                    sourceNode.transform.localPosition);
                // Do NOT trust LinePoint.direction here: the shadow anchors are
                // oriented with a plain base-game Reverse() (ShadowNarrowGaugeGraph
                // TryGetOrientedCurve), which flips point order but leaves each
                // point's stored direction facing the ORIGINAL way. A backwards
                // tangent puts the Bezier handle on the wrong side of the joint -
                // live-confirmed at NCustom_7n90 as a kink through the blade/toe and
                // a ballooned transition that compressed the whole development.
                // Chords of the curve itself are orientation-proof.
                Vector3 startDirection = (dualApproach.Tail.point
                    - dualApproach.LinePointAtDistance(
                        Mathf.Max(0f, dualApproach.Length - 0.5f)).point).normalized;
                float startDot = Vector3.Dot(
                    startDirection,
                    dualApproach.Tail.direction.normalized);
                if (startDirection.sqrMagnitude <= 0.0001f)
                {
                    startDirection = (narrowTransitionEnd.point - startPoint).normalized;
                }

                Vector3 endPoint = narrowTransitionEnd.point;
                // Chord across the departure at the transition end, for the same
                // orientation-safety reason as startDirection above.
                Vector3 endDirection = (fullNarrowDeparture.LinePointAtDistance(
                        Mathf.Min(fullNarrowDeparture.Length, narrowTransitionLength + 0.5f)).point
                    - fullNarrowDeparture.LinePointAtDistance(
                        Mathf.Max(0f, narrowTransitionLength - 0.5f)).point).normalized;
                float endDot = Vector3.Dot(
                    endDirection,
                    narrowTransitionEnd.direction.normalized);
                if (endDirection.sqrMagnitude <= 0.0001f)
                {
                    endDirection = (endPoint - startPoint).normalized;
                }

                Vector3 joinChord = endPoint - startPoint;
                float joinChordLength = joinChord.magnitude;
                Vector3 joinChordDirection = joinChordLength > 0.0001f
                    ? joinChord / joinChordLength
                    : startDirection;
                // A reversed tangent would place its control handle behind the
                // endpoint and create a loop. Keep the curve monotone along the
                // actual toe-to-branch chord while retaining each authored tangent.
                if (Vector3.Dot(startDirection, joinChordDirection) < 0f)
                {
                    startDirection = -startDirection;
                }
                if (Vector3.Dot(endDirection, joinChordDirection) < 0f)
                {
                    endDirection = -endDirection;
                }
                float startChordDot = Vector3.Dot(startDirection, joinChordDirection);
                float endChordDot = Vector3.Dot(endDirection, joinChordDirection);
                Main.Log(
                    $"[NarrowJoinTangents] node={sourceNode.id} " +
                    $"toeCenterOffset={toeCenterOffset:0.000} " +
                    $"storedStartDot={startDot:0.000} storedEndDot={endDot:0.000} " +
                    $"chordStartDot={startChordDot:0.000} chordEndDot={endChordDot:0.000} " +
                    "(negative = stored LinePoint.direction faced backwards vs traversal chord)");

                // One-third chord handles are the cubic-Hermite baseline. The old
                // half-chord handles over-drove the lateral movement and visibly
                // bowed back before reaching the authored narrow route.
                float handleLength = Mathf.Max(
                    joinChordLength / 3f,
                    0.75f);
                var joinCurve = new BezierCurve(
                    startPoint,
                    startPoint + startDirection * handleLength,
                    endPoint - endDirection * handleLength,
                    endPoint,
                    Vector3.up,
                    Vector3.up);
                var transitionCurve = new LineCurve(
                    joinCurve.Approximate(1.000005f, 0.25f, 16, 20f),
                    Hand.Left);
                LineCurve narrowDeparture =
                    fullNarrowDeparture.Skip(narrowTransitionLength, true);
                float approachGap = Vector3.Distance(
                    dualApproach.Tail.point,
                    transitionCurve.Head.point);
                float departureGap = Vector3.Distance(
                    transitionCurve.Tail.point,
                    narrowDeparture.Head.point);
                if (approachGap > 0.05f || departureGap > 0.05f)
                {
                    Main.Warn(
                        $"Failed to build narrow join route at '{sourceNode?.id}': " +
                        $"splice gaps are {approachGap:0.000}m/" +
                        $"{departureGap:0.000}m.");
                    return false;
                }

                var points = dualApproach.Points.ToList();
                points.AddRange(transitionCurve.Points.Skip(1));
                points.AddRange(narrowDeparture.Points.Skip(1));
                route = new LogicalRoute(
                    // Keep the established internal route id for compatibility with
                    // the split-frog and one-blade catalogs. Physically this is the
                    // narrow JOIN into dual gauge, not a second narrow turnout route.
                    "narrow-diverge",
                    GaugeGraphFamily.Narrow,
                    new LineCurve(points, Hand.Left),
                    new[] { ghostDual.id, narrowBranch.id },
                    "narrow-separation",
                    requiredStateId);
                Main.Log(
                    $"[SpecialWorkDiscovery] Narrow join '{sourceNode.id}' assembled from " +
                    $"dual={dualApproach.Length:0.000}m " +
                    $"transition={transitionCurve.Length:0.000}m " +
                    $"narrow={narrowDeparture.Length:0.000}m.");
                return true;
            }
            catch (Exception ex)
            {
                Main.Warn(
                    $"Failed to build narrow join route at '{sourceNode?.id}': {ex.Message}");
                return false;
            }
        }

        private static string? ResolveRouteStateId(
            Graph graph,
            TrackNode node,
            TrackSegment first,
            TrackSegment second)
        {
            try
            {
                if (!graph.DecodeSwitchAt(
                        node,
                        out TrackSegment enter,
                        out TrackSegment normal,
                        out TrackSegment reversed))
                {
                    return null;
                }

                if (RoutePairMatches(enter, normal, first, second))
                {
                    return "normal";
                }

                return RoutePairMatches(enter, reversed, first, second)
                    ? "reversed"
                    : null;
            }
            catch (Exception ex)
            {
                Main.Warn(
                    $"Failed to resolve native route state at '{node?.id}': {ex.Message}");
                return null;
            }
        }

        private static bool RoutePairMatches(
            TrackSegment firstPair,
            TrackSegment secondPair,
            TrackSegment firstRoute,
            TrackSegment secondRoute)
        {
            return (SameSegment(firstPair, firstRoute) && SameSegment(secondPair, secondRoute))
                || (SameSegment(firstPair, secondRoute) && SameSegment(secondPair, firstRoute));
        }

        private static bool SameSegment(TrackSegment first, TrackSegment second)
        {
            return first != null
                && second != null
                && string.Equals(first.id, second.id, StringComparison.OrdinalIgnoreCase);
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
