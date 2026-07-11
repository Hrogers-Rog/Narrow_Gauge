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
    internal sealed class GhostGraphSynchronizationResult
    {
        public int AddedNodes { get; set; }
        public int UpdatedNodes { get; set; }
        public int RemovedNodes { get; set; }
        public int AddedSegments { get; set; }
        public int UpdatedSegments { get; set; }
        public int RemovedSegments { get; set; }
        public int DualGaugeSources { get; set; }

        public bool HasChanges =>
            AddedNodes > 0
            || UpdatedNodes > 0
            || RemovedNodes > 0
            || AddedSegments > 0
            || UpdatedSegments > 0
            || RemovedSegments > 0;

        public override string ToString()
        {
            return $"sources={DualGaugeSources} " +
                   $"nodes(+{AddedNodes}/~{UpdatedNodes}/-{RemovedNodes}) " +
                   $"segments(+{AddedSegments}/~{UpdatedSegments}/-{RemovedSegments})";
        }
    }

    internal static class GhostGraphSynchronizer
    {
        internal const string GeneratedNodePrefix = "fuse-ng:n:";
        internal const string GeneratedSegmentPrefix = "fuse-ng:s:";
        internal const string GeneratedTag = "fuse-ng:generated";
        internal const string GhostGauge = "Narrow";
        internal const string RouteJoinTag = "fuse-ng:ghost-route-join";

        private const float PositionTolerance = 0.001f;
        private const float RotationToleranceDegrees = 0.05f;

        private static readonly Dictionary<string, float> GhostAtoBOffsets =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        public static bool IsGeneratedGhostSegmentId(string? id)
        {
            return !string.IsNullOrEmpty(id)
                && id!.StartsWith(GeneratedSegmentPrefix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsGeneratedGhostNodeId(string? id)
        {
            return !string.IsNullOrEmpty(id)
                && id!.StartsWith(GeneratedNodePrefix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsDualGaugeDefinition(FuseSegment? definition)
        {
            string gauge = definition?.Gauge ?? string.Empty;
            return gauge.Equals("DualGauge", StringComparison.OrdinalIgnoreCase)
                || gauge.Equals("DualGauge_L", StringComparison.OrdinalIgnoreCase)
                || gauge.Equals("DualGauge_R", StringComparison.OrdinalIgnoreCase)
                || gauge.Equals("DualGauge_T", StringComparison.OrdinalIgnoreCase)
                || gauge.Equals("Dual", StringComparison.OrdinalIgnoreCase)
                || gauge.Equals("Mixed", StringComparison.OrdinalIgnoreCase)
                || gauge.Equals("MixedGauge", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSharedRailTransitionDefinition(FuseSegment? definition)
        {
            return string.Equals(
                definition?.Gauge,
                "DualGauge_T",
                StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsNarrowGaugeDefinition(FuseSegment? definition)
        {
            string gauge = definition?.Gauge ?? string.Empty;
            return gauge.Equals("Narrow", StringComparison.OrdinalIgnoreCase)
                || gauge.Equals("3ft", StringComparison.OrdinalIgnoreCase)
                || gauge.Equals("3 ft", StringComparison.OrdinalIgnoreCase)
                || gauge.Equals("ThreeFoot", StringComparison.OrdinalIgnoreCase)
                || gauge.Equals("Three Foot", StringComparison.OrdinalIgnoreCase);
        }

        public static GhostGraphSynchronizationResult Synchronize(Graph graph)
        {
            var result = new GhostGraphSynchronizationResult();
            if (graph == null)
            {
                return result;
            }

            TrackSegment[] sourceSegments = TrackAPI.GetAllSegments()
                .Where(segment => IsUsableSourceSegment(segment))
                .Where(segment => IsDualGaugeDefinition(TrackAPI.GetSegmentDefinition(segment.id)))
                .OrderBy(segment => segment.id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            result.DualGaugeSources = sourceSegments.Length;
            DualGaugeSharedRailRegistry.Rebuild(graph, sourceSegments);
            RebuildGhostOffsets(graph, sourceSegments);

            var nodeCandidates = new Dictionary<string, List<GhostNodeCandidate>>(StringComparer.OrdinalIgnoreCase);
            var expectedSegments = new Dictionary<string, FuseSegment>(StringComparer.OrdinalIgnoreCase);
            var links = new List<DualGaugeSegmentLink>(sourceSegments.Length);

            foreach (TrackSegment source in sourceSegments)
            {
                if (!TryCreateNodeCandidate(source, source.a, atStart: true, out GhostNodeCandidate aCandidate)
                    || !TryCreateNodeCandidate(source, source.b, atStart: false, out GhostNodeCandidate bCandidate))
                {
                    Main.Warn(
                        $"Skipped generated narrow route for dual-gauge segment '{source.id}' " +
                        "because one or both endpoint offsets could not be calculated.");
                    continue;
                }

                AddNodeCandidate(nodeCandidates, source.a.id, aCandidate);
                AddNodeCandidate(nodeCandidates, source.b.id, bCandidate);

                string ghostSegmentId = GetGhostSegmentId(source.id);
                expectedSegments[ghostSegmentId] = CreateGhostSegmentDefinition(source);
                links.Add(new DualGaugeSegmentLink(source.id, ghostSegmentId));
            }

            Dictionary<string, FuseNode> expectedNodes = nodeCandidates.ToDictionary(
                pair => GetGhostNodeId(pair.Key),
                pair => ResolveNodeDefinition(graph, pair.Key, pair.Value),
                StringComparer.OrdinalIgnoreCase);

            RemoveObsoleteSegments(expectedSegments, result);
            RemoveObsoleteNodes(expectedNodes, result);
            ApplyNodes(expectedNodes, result);
            ApplySegments(expectedSegments, result);

            DualGaugeLinkRegistry.Replace(links);
            return result;
        }

        private static bool IsUsableSourceSegment(TrackSegment segment)
        {
            return segment != null
                && segment.a != null
                && segment.b != null
                && !string.IsNullOrWhiteSpace(segment.id)
                && !string.IsNullOrWhiteSpace(segment.a.id)
                && !string.IsNullOrWhiteSpace(segment.b.id)
                && !IsGeneratedGhostSegmentId(segment.id);
        }

        private static void AddNodeCandidate(
            IDictionary<string, List<GhostNodeCandidate>> candidates,
            string sourceNodeId,
            GhostNodeCandidate candidate)
        {
            if (!candidates.TryGetValue(sourceNodeId, out List<GhostNodeCandidate> values))
            {
                values = new List<GhostNodeCandidate>();
                candidates.Add(sourceNodeId, values);
            }

            values.Add(candidate);
        }

        private static bool TryCreateNodeCandidate(
            TrackSegment source,
            TrackNode sourceNode,
            bool atStart,
            out GhostNodeCandidate candidate)
        {
            candidate = default;

            try
            {
                if (!DualGaugeSharedRailRegistry.TryGetAtoBFrameAtNode(
                    source,
                    sourceNode,
                    out Vector3 anchor,
                    out Vector3 directionAtoB))
                {
                    return false;
                }

                Vector3 localUp = sourceNode.transform.localRotation * Vector3.up;
                Vector3 rightOfAtoB = Vector3.Cross(localUp, directionAtoB).normalized;
                if (rightOfAtoB.sqrMagnitude <= 0.000001f)
                {
                    rightOfAtoB = Vector3.Cross(Vector3.up, directionAtoB).normalized;
                }

                float offset = GhostAtoBOffsets.TryGetValue(source.id, out float propagated)
                    ? propagated
                    : GetAuthoredGhostOffset(source);

                candidate = new GhostNodeCandidate(
                    source.id,
                    anchor + rightOfAtoB * offset,
                    sourceNode.transform.localEulerAngles,
                    sourceNode.flipSwitchStand);
                return true;
            }
            catch (Exception ex)
            {
                Main.Warn($"Generated narrow node calculation failed for '{source?.id ?? "<null>"}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Compiles one physical shared-rail choice for the functional graph.
        /// The visible renderer retains its own registry. Here, a three-leg
        /// switch's coincident majority is the physical anchor; that point is
        /// propagated through every ordinary connected node and converted back
        /// into each segment's local A-to-B sign. Explicit DualGauge_T pieces
        /// remain component boundaries because they intentionally change sides.
        /// </summary>
        private static void RebuildGhostOffsets(
            Graph graph,
            IReadOnlyCollection<TrackSegment> sourceSegments)
        {
            GhostAtoBOffsets.Clear();
            TrackSegment[] ordinary = sourceSegments
                .Where(segment => !DualGaugeSharedRailRegistry.IsSharedRailTransition(segment))
                .ToArray();
            var byNode = ordinary
                .SelectMany(segment => new[]
                {
                    new { Node = segment.a, Segment = segment },
                    new { Node = segment.b, Segment = segment }
                })
                .Where(item => item.Node != null)
                .GroupBy(item => item.Node.id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(item => item.Segment).Distinct().ToArray(),
                    StringComparer.OrdinalIgnoreCase);

            var queue = new Queue<TrackSegment>();
            foreach (KeyValuePair<string, TrackSegment[]> pair in byNode
                .Where(item => item.Value.Length == 3)
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (pair.Value.All(segment => GhostAtoBOffsets.ContainsKey(segment.id)))
                {
                    continue;
                }

                TrackNode node = graph.GetNode(pair.Key);
                if (node == null || !TryResolveSwitchAnchor(node, pair.Value, out Vector3 target))
                {
                    continue;
                }

                AssignConnectedAtNode(pair.Value, node, target, queue);
                PropagateGhostOffsets(byNode, queue);
            }

            foreach (TrackSegment segment in ordinary
                .OrderBy(item => item.id, StringComparer.OrdinalIgnoreCase))
            {
                if (GhostAtoBOffsets.ContainsKey(segment.id))
                {
                    continue;
                }

                GhostAtoBOffsets[segment.id] = GetAuthoredGhostOffset(segment);
                queue.Enqueue(segment);
                PropagateGhostOffsets(byNode, queue);
            }

            foreach (TrackSegment transition in sourceSegments
                .Where(DualGaugeSharedRailRegistry.IsSharedRailTransition))
            {
                GhostAtoBOffsets[transition.id] =
                    DualGaugeSharedRailRegistry.GetAtoBNarrowCenterOffset(transition);
            }
        }

        private static bool TryResolveSwitchAnchor(
            TrackNode node,
            IReadOnlyCollection<TrackSegment> connected,
            out Vector3 target)
        {
            target = Vector3.zero;
            var candidates = new List<Vector3>();
            foreach (TrackSegment segment in connected)
            {
                if (TryGetGhostOffsetPosition(
                    segment,
                    node,
                    GetAuthoredGhostOffset(segment),
                    out Vector3 position))
                {
                    candidates.Add(position);
                }
            }

            if (candidates.Count == 0)
            {
                return false;
            }

            const float clusterTolerance = 0.05f;
            var clusters = new List<List<Vector3>>();
            foreach (Vector3 candidate in candidates)
            {
                List<Vector3> cluster = clusters.FirstOrDefault(existing =>
                    existing.Any(position =>
                        Vector3.Distance(position, candidate) <= clusterTolerance));
                if (cluster == null)
                {
                    cluster = new List<Vector3>();
                    clusters.Add(cluster);
                }

                cluster.Add(candidate);
            }

            List<Vector3> selected = clusters
                .OrderByDescending(cluster => cluster.Count)
                .ThenBy(cluster => cluster[0].x)
                .ThenBy(cluster => cluster[0].z)
                .First();
            target = selected.Aggregate(Vector3.zero, (sum, position) => sum + position)
                / selected.Count;
            return true;
        }

        private static void AssignConnectedAtNode(
            IEnumerable<TrackSegment> connected,
            TrackNode node,
            Vector3 target,
            Queue<TrackSegment> queue)
        {
            foreach (TrackSegment segment in connected)
            {
                float chosen = ChooseGhostOffsetClosestTo(segment, node, target);
                if (!GhostAtoBOffsets.TryGetValue(segment.id, out float existing))
                {
                    GhostAtoBOffsets[segment.id] = chosen;
                    queue.Enqueue(segment);
                }
                else if (Mathf.Sign(existing) != Mathf.Sign(chosen))
                {
                    Main.Warn(
                        $"[GhostSharedSideConflict] Switch/node '{node.id}' wants '{segment.id}' " +
                        "on the opposite physical side from the already-propagated component; " +
                        "keeping the established shared rail.");
                }
            }
        }

        private static void PropagateGhostOffsets(
            IReadOnlyDictionary<string, TrackSegment[]> byNode,
            Queue<TrackSegment> queue)
        {
            while (queue.Count > 0)
            {
                TrackSegment current = queue.Dequeue();
                float offset = GhostAtoBOffsets[current.id];
                foreach (TrackNode node in new[] { current.a, current.b }.Where(item => item != null))
                {
                    if (!byNode.TryGetValue(node.id, out TrackSegment[] connected)
                        || !TryGetGhostOffsetPosition(current, node, offset, out Vector3 target))
                    {
                        continue;
                    }

                    AssignConnectedAtNode(connected, node, target, queue);
                }
            }
        }

        private static float ChooseGhostOffsetClosestTo(
            TrackSegment segment,
            TrackNode node,
            Vector3 target)
        {
            bool hasPositive = TryGetGhostOffsetPosition(
                segment,
                node,
                DualGaugeSharedRailRegistry.OffsetMagnitude,
                out Vector3 positive);
            bool hasNegative = TryGetGhostOffsetPosition(
                segment,
                node,
                -DualGaugeSharedRailRegistry.OffsetMagnitude,
                out Vector3 negative);
            if (!hasPositive)
            {
                return -DualGaugeSharedRailRegistry.OffsetMagnitude;
            }

            if (!hasNegative)
            {
                return DualGaugeSharedRailRegistry.OffsetMagnitude;
            }

            return Vector3.Distance(positive, target) <= Vector3.Distance(negative, target)
                ? DualGaugeSharedRailRegistry.OffsetMagnitude
                : -DualGaugeSharedRailRegistry.OffsetMagnitude;
        }

        private static bool TryGetGhostOffsetPosition(
            TrackSegment segment,
            TrackNode node,
            float offset,
            out Vector3 position)
        {
            position = Vector3.zero;
            if (!DualGaugeSharedRailRegistry.TryGetAtoBFrameAtNode(
                segment,
                node,
                out Vector3 anchor,
                out Vector3 directionAtoB))
            {
                return false;
            }

            Vector3 up = node.transform.localRotation * Vector3.up;
            Vector3 right = Vector3.Cross(up, directionAtoB).normalized;
            if (right.sqrMagnitude <= 0.000001f)
            {
                right = Vector3.Cross(Vector3.up, directionAtoB).normalized;
            }

            position = anchor + right * offset;
            return true;
        }

        private static float GetAuthoredGhostOffset(TrackSegment segment)
        {
            FuseSegment definition = TrackAPI.GetSegmentDefinition(segment.id);
            if (string.Equals(definition?.Gauge, "DualGauge_R", StringComparison.OrdinalIgnoreCase))
            {
                return DualGaugeSharedRailRegistry.OffsetMagnitude;
            }

            if (string.Equals(definition?.Gauge, "DualGauge_L", StringComparison.OrdinalIgnoreCase))
            {
                return -DualGaugeSharedRailRegistry.OffsetMagnitude;
            }

            return DualGaugeSharedRailRegistry.GetAtoBNarrowCenterOffset(segment);
        }

        private static FuseNode ResolveNodeDefinition(
            Graph graph,
            string sourceNodeId,
            IReadOnlyCollection<GhostNodeCandidate> candidates)
        {
            const float coincidentTolerance = 0.05f;
            GhostNodeCandidate[] orderedCandidates = candidates
                .OrderBy(candidate => candidate.SourceSegmentId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var clusters = new List<List<GhostNodeCandidate>>();
            foreach (GhostNodeCandidate candidate in orderedCandidates)
            {
                List<GhostNodeCandidate> cluster = clusters.FirstOrDefault(existing =>
                    existing.Any(member =>
                        Vector3.Distance(member.Position, candidate.Position) <= coincidentTolerance));
                if (cluster == null)
                {
                    cluster = new List<GhostNodeCandidate>();
                    clusters.Add(cluster);
                }

                cluster.Add(candidate);
            }

            List<GhostNodeCandidate> selectedCluster = clusters
                .OrderByDescending(cluster => cluster.Count)
                .ThenBy(
                    cluster => cluster.Min(candidate => candidate.SourceSegmentId),
                    StringComparer.OrdinalIgnoreCase)
                .First();
            Vector3 position = selectedCluster
                .Aggregate(Vector3.zero, (sum, next) => sum + next.Position)
                / Mathf.Max(selectedCluster.Count, 1);
            GhostNodeCandidate first = selectedCluster[0];

            float maxDeviation = candidates.Max(candidate => Vector3.Distance(candidate.Position, position));
            if (maxDeviation > 0.05f)
            {
                Main.Warn(
                    $"Dual-gauge node '{sourceNodeId}' generated narrow endpoints disagree by up to {maxDeviation:F3}m; " +
                    $"selected full-offset cluster {selectedCluster.Count}/{candidates.Count} from " +
                    $"[{string.Join(",", selectedCluster.Select(candidate => candidate.SourceSegmentId))}] " +
                    "instead of averaging toward the standard center. " +
                    " Rendered-center candidates: " +
                    string.Join(
                        ", ",
                        candidates.Select(candidate =>
                            $"{candidate.SourceSegmentId}@" +
                            $"({candidate.Position.x:F3},{candidate.Position.y:F3},{candidate.Position.z:F3})")));
            }

            return new FuseNode
            {
                Position = position,
                Rotation = first.Rotation,
                FlipSwitchStand = first.FlipSwitchStand,
                Tags = new[] { GeneratedTag, "fuse-ng:source-node=" + sourceNodeId }
            };
        }

        private static FuseSegment CreateGhostSegmentDefinition(TrackSegment source)
        {
            return new FuseSegment
            {
                StartNodeId = GetGhostNodeId(source.a.id),
                EndNodeId = GetGhostNodeId(source.b.id),
                Style = source.style.ToString(),
                TrackClass = source.trackClass == TrackClass.Mainline ? "main" : source.trackClass.ToString(),
                SpeedLimit = source.speedLimit,
                Priority = source.priority,
                GroupId = source.groupId,
                Gauge = GhostGauge,
                Tags = new[] { GeneratedTag, "fuse-ng:source-segment=" + source.id }
            };
        }

        private static void RemoveObsoleteSegments(
            IReadOnlyDictionary<string, FuseSegment> expected,
            GhostGraphSynchronizationResult result)
        {
            foreach (TrackSegment segment in TrackAPI.GetAllSegments()
                .Where(segment => segment != null && IsGeneratedGhostSegmentId(segment.id))
                .Where(segment =>
                    !SpecialWorkTopologySynchronizer.IsHiddenControlSegment(segment)
                    || IsRouteJoinSegment(segment))
                .Where(segment => !expected.ContainsKey(segment.id))
                .ToArray())
            {
                if (IsSegmentOccupied(segment))
                {
                    Main.Warn(
                        $"Deferred removal of obsolete generated narrow segment '{segment.id}' " +
                        "because rolling stock currently occupies it.");
                    continue;
                }

                TrackAPI.RemoveSegment(segment.id);
                result.RemovedSegments++;
            }
        }

        private static void RemoveObsoleteNodes(
            IReadOnlyDictionary<string, FuseNode> expected,
            GhostGraphSynchronizationResult result)
        {
            foreach (TrackNode node in TrackAPI.GetAllNodes()
                .Where(node => node != null && IsGeneratedGhostNodeId(node.id))
                .Where(node => !expected.ContainsKey(node.id))
                .Where(node => !TrackAPI.GetAllSegments().Any(segment => segment.a == node || segment.b == node))
                .ToArray())
            {
                TrackAPI.RemoveNode(node.id);
                result.RemovedNodes++;
            }
        }

        private static void ApplyNodes(
            IReadOnlyDictionary<string, FuseNode> expected,
            GhostGraphSynchronizationResult result)
        {
            foreach (KeyValuePair<string, FuseNode> pair in expected.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                TrackNode existing = TrackAPI.GetNode(pair.Key);
                if (existing == null)
                {
                    TrackAPI.AddNode(pair.Key, pair.Value);
                    result.AddedNodes++;
                }
                else if (NodeNeedsUpdate(existing, pair.Value))
                {
                    TrackAPI.UpdateNode(pair.Key, pair.Value);
                    result.UpdatedNodes++;
                }
            }
        }

        private static void ApplySegments(
            IReadOnlyDictionary<string, FuseSegment> expected,
            GhostGraphSynchronizationResult result)
        {
            foreach (KeyValuePair<string, FuseSegment> pair in expected.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                TrackSegment existing = TrackAPI.GetSegment(pair.Key);
                if (existing == null)
                {
                    TrackAPI.AddSegment(pair.Key, pair.Value);
                    result.AddedSegments++;
                    continue;
                }

                if (!string.Equals(existing.a?.id, pair.Value.StartNodeId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(existing.b?.id, pair.Value.EndNodeId, StringComparison.OrdinalIgnoreCase))
                {
                    if (IsSegmentOccupied(existing))
                    {
                        Main.Warn(
                            $"Deferred topology update for generated narrow segment '{existing.id}' " +
                            "because rolling stock currently occupies it.");
                        continue;
                    }

                    TrackAPI.RemoveSegment(pair.Key);
                    TrackAPI.AddSegment(pair.Key, pair.Value);
                    result.UpdatedSegments++;
                    continue;
                }

                if (SegmentNeedsUpdate(existing, pair.Value))
                {
                    TrackAPI.UpdateSegment(pair.Key, pair.Value);
                    result.UpdatedSegments++;
                }
            }
        }

        private static bool NodeNeedsUpdate(TrackNode existing, FuseNode expected)
        {
            return Vector3.Distance(existing.transform.localPosition, expected.Position) > PositionTolerance
                || Quaternion.Angle(existing.transform.localRotation, Quaternion.Euler(expected.Rotation)) > RotationToleranceDegrees
                || existing.flipSwitchStand != expected.FlipSwitchStand;
        }

        private static bool SegmentNeedsUpdate(TrackSegment existing, FuseSegment expected)
        {
            FuseSegment current = TrackAPI.GetSegmentDefinition(existing.id);
            return existing.style.ToString() != expected.Style
                || existing.trackClass.ToString() != expected.TrackClass
                    && !(existing.trackClass == TrackClass.Mainline && expected.TrackClass == "main")
                || existing.speedLimit != expected.SpeedLimit
                || existing.priority != expected.Priority
                || !string.Equals(existing.groupId, expected.GroupId, StringComparison.Ordinal)
                || !string.Equals(current?.Gauge, expected.Gauge, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSegmentOccupied(TrackSegment segment)
        {
            TrainController controller = TrainController.Shared;
            return controller != null
                && controller.Cars.Any(car =>
                    car != null
                    && (car.LocationF.segment == segment
                        || car.LocationR.segment == segment
                        || car.WheelBoundsF.segment == segment
                        || car.WheelBoundsR.segment == segment));
        }

        internal static string GetGhostNodeId(string sourceNodeId)
        {
            return GeneratedNodePrefix + sourceNodeId;
        }

        internal static string GetGhostSegmentId(string sourceSegmentId)
        {
            return GeneratedSegmentPrefix + sourceSegmentId;
        }

        private static bool IsRouteJoinSegment(TrackSegment? segment)
        {
            FuseSegment? definition = segment == null ? null : TrackAPI.GetSegmentDefinition(segment.id);
            return definition?.Tags?.Any(tag =>
                string.Equals(tag, RouteJoinTag, StringComparison.OrdinalIgnoreCase)) == true;
        }

        private readonly struct GhostNodeCandidate
        {
            public GhostNodeCandidate(
                string sourceSegmentId,
                Vector3 position,
                Vector3 rotation,
                bool flipSwitchStand)
            {
                SourceSegmentId = sourceSegmentId;
                Position = position;
                Rotation = rotation;
                FlipSwitchStand = flipSwitchStand;
            }

            public string SourceSegmentId { get; }
            public Vector3 Position { get; }
            public Vector3 Rotation { get; }
            public bool FlipSwitchStand { get; }
        }
    }
}
