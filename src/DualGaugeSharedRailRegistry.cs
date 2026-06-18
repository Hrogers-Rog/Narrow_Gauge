using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using FUSE.Authoring.Data;
using FUSE.Runtime.API;
using Track;
using UnityEngine;

namespace NarrowGaugeMod
{
    /// <summary>
    /// Resolves a continuous physical narrow-gauge centerline through authored
    /// dual-gauge segments. The stored sign is relative to each segment's A-to-B
    /// direction, so reversed segment definitions do not flip the visible rail.
    /// </summary>
    internal static class DualGaugeSharedRailRegistry
    {
        internal const string SharedRailSideTagPrefix = "fuse-ng:shared-rail=";

        private static readonly Dictionary<string, float> NarrowCenterOffsets =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        public static float OffsetMagnitude =>
            (Gauge.Standard.Inside - NarrowGaugeTrackBuilder.ThreeFootGauge.Inside) * 0.5f;

        public static void Rebuild(Graph graph, IReadOnlyCollection<TrackSegment> dualSegments)
        {
            NarrowCenterOffsets.Clear();
            if (graph == null || dualSegments == null || dualSegments.Count == 0)
            {
                return;
            }

            TrackSegment[] ordered = dualSegments
                .Where(segment => segment?.a != null && segment.b != null)
                .OrderBy(segment => segment.id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            TrackSegment[] sideSegments = ordered
                .Where(segment => !IsSharedRailTransition(segment))
                .ToArray();
            var byNode = sideSegments
                .SelectMany(segment => new[]
                {
                    new { Node = segment.a, Segment = segment },
                    new { Node = segment.b, Segment = segment }
                })
                .GroupBy(item => item.Node.id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(item => item.Segment).Distinct().ToArray(),
                    StringComparer.OrdinalIgnoreCase);

            var queue = new Queue<TrackSegment>();
            SeedExplicitOverrides(sideSegments, queue);
            SeedFromNarrowBranches(graph, sideSegments, byNode, queue);
            Propagate(byNode, queue);

            foreach (TrackSegment segment in sideSegments)
            {
                if (NarrowCenterOffsets.ContainsKey(segment.id))
                {
                    continue;
                }

                Assign(segment, OffsetMagnitude, queue);
                Propagate(byNode, queue);
            }
        }

        private static void SeedExplicitOverrides(
            IEnumerable<TrackSegment> dualSegments,
            Queue<TrackSegment> queue)
        {
            foreach (TrackSegment segment in dualSegments)
            {
                FuseSegment definition = TrackAPI.GetSegmentDefinition(segment.id);
                if (TryGetGaugeSharedRailSide(definition?.Gauge, out bool gaugeSharesRight))
                {
                    Assign(
                        segment,
                        ChooseExplicitSharedRailOffset(segment, gaugeSharesRight),
                        queue);
                    continue;
                }

                string? tag = definition?.Tags?.FirstOrDefault(value =>
                    value != null
                    && value.StartsWith(SharedRailSideTagPrefix, StringComparison.OrdinalIgnoreCase));
                if (tag == null)
                {
                    continue;
                }

                string side = tag.Substring(SharedRailSideTagPrefix.Length);
                if (side.Equals("left", StringComparison.OrdinalIgnoreCase))
                {
                    Assign(segment, ChooseExplicitSharedRailOffset(segment, false), queue);
                }
                else if (side.Equals("right", StringComparison.OrdinalIgnoreCase))
                {
                    Assign(segment, ChooseExplicitSharedRailOffset(segment, true), queue);
                }
            }
        }

        private static bool TryGetGaugeSharedRailSide(string? gauge, out bool sharesRight)
        {
            if (string.Equals(gauge, "DualGauge_L", StringComparison.OrdinalIgnoreCase))
            {
                sharesRight = false;
                return true;
            }

            if (string.Equals(gauge, "DualGauge_R", StringComparison.OrdinalIgnoreCase))
            {
                sharesRight = true;
                return true;
            }

            sharesRight = false;
            return false;
        }

        private static float ChooseExplicitSharedRailOffset(
            TrackSegment segment,
            bool sharesRight)
        {
            float positiveScore = 0f;
            float negativeScore = 0f;
            int samples = 0;
            foreach (TrackNode node in new[] { segment.a, segment.b }.OfType<TrackNode>())
            {
                if (!TryGetAtoBFrameAtNode(segment, node, out Vector3 anchor, out _)
                    || !TryOffsetPosition(segment, node, OffsetMagnitude, out Vector3 positive)
                    || !TryOffsetPosition(segment, node, -OffsetMagnitude, out Vector3 negative))
                {
                    continue;
                }

                Vector3 physicalRight = node.transform.localRotation * Vector3.right;
                if (physicalRight.sqrMagnitude <= 0.0001f)
                {
                    continue;
                }

                Vector3 target = anchor
                    + physicalRight.normalized
                    * (sharesRight ? OffsetMagnitude : -OffsetMagnitude);
                positiveScore += Vector3.Distance(positive, target);
                negativeScore += Vector3.Distance(negative, target);
                samples++;
            }

            if (samples == 0)
            {
                return sharesRight ? OffsetMagnitude : -OffsetMagnitude;
            }

            return positiveScore <= negativeScore ? OffsetMagnitude : -OffsetMagnitude;
        }

        public static float GetNarrowCenterOffset(TrackSegment? sourceSegment)
        {
            float offset = GetAtoBNarrowCenterOffset(sourceSegment);
            return CurveRunsAtoB(sourceSegment) ? offset : -offset;
        }

        public static bool SharesRightRail(TrackSegment? sourceSegment)
        {
            return GetNarrowCenterOffset(sourceSegment) >= 0f;
        }

        internal static float GetAtoBNarrowCenterOffset(TrackSegment? sourceSegment)
        {
            return sourceSegment != null
                && !string.IsNullOrEmpty(sourceSegment.id)
                && NarrowCenterOffsets.TryGetValue(sourceSegment.id, out float offset)
                    ? offset
                    : OffsetMagnitude;
        }

        internal static float GetAtoBNarrowCenterOffsetAtNode(
            TrackSegment? sourceSegment,
            TrackNode? node)
        {
            float fallback = GetAtoBNarrowCenterOffset(sourceSegment);
            if (sourceSegment == null
                || node == null
                || !IsSharedRailTransition(sourceSegment)
                || !TryGetSharedRailTransitionNeighbor(sourceSegment, node, out TrackSegment neighbor)
                || !TryOffsetPosition(
                    neighbor,
                    node,
                    GetAtoBNarrowCenterOffset(neighbor),
                    out Vector3 target))
            {
                return fallback;
            }

            return ChooseOffsetClosestTo(sourceSegment, node, target);
        }

        internal static bool IsSharedRailTransition(TrackSegment? segment)
        {
            return segment != null
                && GhostGraphSynchronizer.IsSharedRailTransitionDefinition(
                    TrackAPI.GetSegmentDefinition(segment.id));
        }

        internal static bool CurveRunsAtoB(TrackSegment? segment)
        {
            if (segment?.a == null || segment.b == null)
            {
                return true;
            }

            try
            {
                LinePoint[] points = segment.Curve
                    .Approximate(1.000005f, 0.25f, 16, 40f)
                    .ToArray();
                if (points.Length < 2)
                {
                    return true;
                }

                Vector3 start = points[0].point;
                Vector3 end = points[points.Length - 1].point;
                float aToBScore =
                    Vector3.Distance(start, segment.a.transform.localPosition)
                    + Vector3.Distance(end, segment.b.transform.localPosition);
                float bToAScore =
                    Vector3.Distance(start, segment.b.transform.localPosition)
                    + Vector3.Distance(end, segment.a.transform.localPosition);
                return aToBScore <= bToAScore;
            }
            catch
            {
                return true;
            }
        }

        internal static bool TryGetAtoBFrameAtNode(
            TrackSegment segment,
            TrackNode node,
            out Vector3 anchor,
            out Vector3 directionAtoB)
        {
            anchor = Vector3.zero;
            directionAtoB = Vector3.zero;
            if (segment?.a == null
                || segment.b == null
                || node == null
                || segment.a != node && segment.b != node)
            {
                return false;
            }

            LinePoint[] points = segment.Curve
                .Approximate(1.000005f, 0.25f, 16, 40f)
                .ToArray();
            if (points.Length < 2)
            {
                return false;
            }

            bool curveRunsAtoB = CurveRunsAtoB(segment);
            bool nodeAtCurveStart = segment.a == node
                ? curveRunsAtoB
                : !curveRunsAtoB;
            anchor = nodeAtCurveStart ? points[0].point : points[points.Length - 1].point;
            Vector3 naturalDirection = nodeAtCurveStart
                ? points[1].point - points[0].point
                : points[points.Length - 1].point - points[points.Length - 2].point;
            if (naturalDirection.sqrMagnitude <= 0.000001f)
            {
                return false;
            }

            naturalDirection.Normalize();
            directionAtoB = curveRunsAtoB ? naturalDirection : -naturalDirection;
            return true;
        }

        public static void Clear()
        {
            NarrowCenterOffsets.Clear();
        }

        private static void SeedFromNarrowBranches(
            Graph graph,
            IReadOnlyCollection<TrackSegment> dualSegments,
            IReadOnlyDictionary<string, TrackSegment[]> byNode,
            Queue<TrackSegment> queue)
        {
            foreach (TrackSegment branch in TrackAPI.GetAllSegments()
                .Where(IsRealNarrowOnly)
                .OrderBy(segment => segment.id, StringComparer.OrdinalIgnoreCase))
            {
                string sourceNodeId = SpecialWorkTopologySynchronizer.GetTaggedSourceNodeId(branch);
                if (string.IsNullOrEmpty(sourceNodeId))
                {
                    sourceNodeId = new[] { branch.a, branch.b }
                        .Where(node => node != null)
                        .Select(node => node.id)
                        .FirstOrDefault(id =>
                            byNode.TryGetValue(id, out TrackSegment[] connected)
                            && (connected.Length == 2
                                || IsSupportedSharedRailSeedSource(
                                    graph,
                                    graph.GetNode(id),
                                    connected,
                                    branch)))
                        ?? string.Empty;
                }

                TrackNode sourceNode = graph.GetNode(sourceNodeId);
                if (sourceNode == null
                    || !byNode.TryGetValue(sourceNodeId, out TrackSegment[] connectedDual)
                    || !IsSupportedSharedRailSeedSource(graph, sourceNode, connectedDual, branch)
                    || !TryGetBranchDirection(branch, sourceNodeId, out Vector3 branchDirection))
                {
                    continue;
                }

                // The leg opposite the narrow branch is the approach into the
                // transition. V1 seeds the shared rail on the engineer's left
                // while looking from that approach toward the switch.
                TrackSegment approachLeg = connectedDual
                    .OrderBy(segment =>
                        TryDirectionAwayFromNode(segment, sourceNode, out Vector3 direction)
                            ? Vector3.Dot(direction, branchDirection)
                            : 2f)
                    .First();
                if (!TryDirectionAwayFromNode(approachLeg, sourceNode, out Vector3 awayFromSwitch))
                {
                    continue;
                }

                Vector3 towardSwitch = -awayFromSwitch;
                Vector3 up = sourceNode.transform.localRotation * Vector3.up;
                Vector3 right = Vector3.Cross(up, towardSwitch).normalized;
                if (right.sqrMagnitude <= 0.0001f)
                {
                    right = Vector3.Cross(Vector3.up, towardSwitch).normalized;
                }

                Vector3 target = sourceNode.transform.localPosition - right * OffsetMagnitude;

                foreach (TrackSegment segment in connectedDual)
                {
                    float offset = ChooseOffsetClosestTo(segment, sourceNode, target);
                    Assign(segment, offset, queue);
                }
            }
        }

        private static bool IsSupportedSharedRailSeedSource(
            Graph graph,
            TrackNode sourceNode,
            IReadOnlyCollection<TrackSegment> connectedDual,
            TrackSegment branch)
        {
            if (connectedDual.Count == 2)
            {
                return true;
            }

            if (connectedDual.Count != 1 || graph == null || sourceNode == null)
            {
                return false;
            }

            TrackSegment[] physical = graph.SegmentsConnectedTo(sourceNode)
                .Where(segment =>
                    segment != null
                    && !GhostGraphSynchronizer.IsGeneratedGhostSegmentId(segment.id))
                .ToArray();
            int dualCount = physical.Count(segment =>
                    GhostGraphSynchronizer.IsDualGaugeDefinition(
                        TrackAPI.GetSegmentDefinition(segment.id)));

            return dualCount == 1
                && (physical.Count(IsStandardOnly) == 1
                    || physical.Count(IsRealNarrowOnly) == 1
                    || BranchTouchesSourceOrGhost(branch, sourceNode.id));
        }

        private static bool BranchTouchesSourceOrGhost(
            TrackSegment branch,
            string sourceNodeId)
        {
            if (branch == null || string.IsNullOrWhiteSpace(sourceNodeId))
            {
                return false;
            }

            string ghostNodeId = GhostGraphSynchronizer.GetGhostNodeId(sourceNodeId);
            return new[] { branch.a, branch.b }
                .Where(node => node != null)
                .Any(node =>
                    string.Equals(node.id, sourceNodeId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(node.id, ghostNodeId, StringComparison.OrdinalIgnoreCase));
        }

        private static void Propagate(
            IReadOnlyDictionary<string, TrackSegment[]> byNode,
            Queue<TrackSegment> queue)
        {
            while (queue.Count > 0)
            {
                TrackSegment current = queue.Dequeue();
                float currentOffset = GetAtoBNarrowCenterOffset(current);

                foreach (TrackNode node in new[] { current.a, current.b })
                {
                    if (node == null
                        || !byNode.TryGetValue(node.id, out TrackSegment[] neighbors)
                        || !TryOffsetPosition(current, node, currentOffset, out Vector3 target))
                    {
                        continue;
                    }

                    foreach (TrackSegment neighbor in neighbors)
                    {
                        if (neighbor == current || NarrowCenterOffsets.ContainsKey(neighbor.id))
                        {
                            continue;
                        }

                        Assign(neighbor, ChooseOffsetClosestTo(neighbor, node, target), queue);
                    }
                }
            }
        }

        private static void Assign(TrackSegment segment, float offset, Queue<TrackSegment> queue)
        {
            if (segment == null
                || string.IsNullOrEmpty(segment.id)
                || NarrowCenterOffsets.ContainsKey(segment.id))
            {
                return;
            }

            NarrowCenterOffsets[segment.id] = offset >= 0f ? OffsetMagnitude : -OffsetMagnitude;
            queue.Enqueue(segment);
        }

        private static float ChooseOffsetClosestTo(
            TrackSegment segment,
            TrackNode node,
            Vector3 target)
        {
            bool hasPositive = TryOffsetPosition(segment, node, OffsetMagnitude, out Vector3 positive);
            bool hasNegative = TryOffsetPosition(segment, node, -OffsetMagnitude, out Vector3 negative);
            if (!hasPositive)
            {
                return -OffsetMagnitude;
            }

            if (!hasNegative)
            {
                return OffsetMagnitude;
            }

            return Vector3.Distance(positive, target) <= Vector3.Distance(negative, target)
                ? OffsetMagnitude
                : -OffsetMagnitude;
        }

        private static bool TryOffsetPosition(
            TrackSegment segment,
            TrackNode node,
            float offset,
            out Vector3 position)
        {
            position = Vector3.zero;
            if (!TryGetAtoBFrameAtNode(segment, node, out Vector3 anchor, out Vector3 directionAtoB))
            {
                return false;
            }

            Vector3 up = node.transform.localRotation * Vector3.up;
            Vector3 right = Vector3.Cross(up, directionAtoB).normalized;
            if (right.sqrMagnitude <= 0.0001f)
            {
                right = Vector3.Cross(Vector3.up, directionAtoB).normalized;
            }

            position = anchor + right * offset;
            return true;
        }

        private static bool TryDirectionAwayFromNode(
            TrackSegment segment,
            TrackNode node,
            out Vector3 direction)
        {
            direction = Vector3.zero;
            if (!TryGetAtoBFrameAtNode(segment, node, out _, out Vector3 directionAtoB))
            {
                return false;
            }

            direction = segment.a == node ? directionAtoB : -directionAtoB;
            return true;
        }

        private static bool TryGetBranchDirection(
            TrackSegment branch,
            string sourceNodeId,
            out Vector3 direction)
        {
            direction = Vector3.zero;
            TrackNode endpoint = new[] { branch.a, branch.b }
                .FirstOrDefault(node =>
                    node != null
                    && (string.Equals(node.id, sourceNodeId, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(
                            node.id,
                            GhostGraphSynchronizer.GetGhostNodeId(sourceNodeId),
                            StringComparison.OrdinalIgnoreCase)));
            return endpoint != null && TryDirectionAwayFromNode(branch, endpoint, out direction);
        }

        private static bool TryGetSharedRailTransitionNeighbor(
            TrackSegment transition,
            TrackNode node,
            out TrackSegment neighbor)
        {
            neighbor = null!;
            if (transition == null || node == null || Graph.Shared == null)
            {
                return false;
            }

            TrackSegment[] connected = Graph.Shared.SegmentsConnectedTo(node)
                .Where(segment =>
                    segment != null
                    && segment != transition
                    && !GhostGraphSynchronizer.IsGeneratedGhostSegmentId(segment.id)
                    && !SpecialWorkTopologySynchronizer.IsHiddenControlSegment(segment)
                    && GhostGraphSynchronizer.IsDualGaugeDefinition(
                        TrackAPI.GetSegmentDefinition(segment.id))
                    && !IsSharedRailTransition(segment))
                .ToArray();
            if (connected.Length != 1)
            {
                return false;
            }

            neighbor = connected[0];
            return true;
        }

        private static bool IsRealNarrowOnly(TrackSegment segment)
        {
            if (segment == null || GhostGraphSynchronizer.IsGeneratedGhostSegmentId(segment.id))
            {
                return false;
            }

            FuseSegment definition = TrackAPI.GetSegmentDefinition(segment.id);
            return GhostGraphSynchronizer.IsNarrowGaugeDefinition(definition)
                && !GhostGraphSynchronizer.IsDualGaugeDefinition(definition);
        }

        private static bool IsStandardOnly(TrackSegment segment)
        {
            if (segment == null || GhostGraphSynchronizer.IsGeneratedGhostSegmentId(segment.id))
            {
                return false;
            }

            FuseSegment definition = TrackAPI.GetSegmentDefinition(segment.id);
            return !GhostGraphSynchronizer.IsNarrowGaugeDefinition(definition)
                && !GhostGraphSynchronizer.IsDualGaugeDefinition(definition);
        }
    }
}
