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

        private const float PositionTolerance = 0.001f;
        private const float RotationToleranceDegrees = 0.05f;

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
                Vector3 right = Vector3.Cross(localUp, directionAtoB).normalized;
                if (right.sqrMagnitude <= 0.000001f)
                {
                    right = Vector3.Cross(Vector3.up, directionAtoB).normalized;
                }

                candidate = new GhostNodeCandidate(
                    anchor + right * DualGaugeSharedRailRegistry.GetAtoBNarrowCenterOffsetAtNode(
                        source,
                        sourceNode),
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

        private static FuseNode ResolveNodeDefinition(
            Graph graph,
            string sourceNodeId,
            IReadOnlyCollection<GhostNodeCandidate> candidates)
        {
            Vector3 position = candidates.Aggregate(Vector3.zero, (sum, next) => sum + next.Position)
                / Mathf.Max(candidates.Count, 1);
            GhostNodeCandidate first = candidates.First();

            float maxDeviation = candidates.Max(candidate => Vector3.Distance(candidate.Position, position));
            if (maxDeviation > 0.05f)
            {
                TrackNode existing = graph.GetNode(GetGhostNodeId(sourceNodeId));
                if (existing != null)
                {
                    position = existing.transform.localPosition;
                }

                Main.Warn(
                    $"Dual-gauge node '{sourceNodeId}' generated narrow endpoints disagree by up to {maxDeviation:F3}m; " +
                    (existing == null
                        ? "V1 averaged them. This junction will need an explicit special-trackwork definition."
                        : "preserving its established generated control point."));
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
                .Where(segment => !SpecialWorkTopologySynchronizer.IsHiddenControlSegment(segment))
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

        private readonly struct GhostNodeCandidate
        {
            public GhostNodeCandidate(Vector3 position, Vector3 rotation, bool flipSwitchStand)
            {
                Position = position;
                Rotation = rotation;
                FlipSwitchStand = flipSwitchStand;
            }

            public Vector3 Position { get; }
            public Vector3 Rotation { get; }
            public bool FlipSwitchStand { get; }
        }
    }
}
