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
    internal sealed class SpecialWorkTopologySynchronizationResult
    {
        public int NarrowBranchTransitions { get; set; }
        public int DualToNarrowContinuations { get; set; }
        public int RewiredSegments { get; set; }
        public int RestoredSegments { get; set; }
        public List<string> Issues { get; } = new List<string>();

        public bool HasChanges => RewiredSegments > 0 || RestoredSegments > 0;

        public override string ToString()
        {
            return $"narrowBranchTransitions={NarrowBranchTransitions} " +
                   $"dualToNarrowContinuations={DualToNarrowContinuations} " +
                   $"rewired={RewiredSegments} restored={RestoredSegments} issues={Issues.Count}";
        }
    }

    /// <summary>
    /// Compiles gauge-family boundaries that cannot be represented by leaving
    /// every authored segment attached to one native node.
    /// </summary>
    internal static class SpecialWorkTopologySynchronizer
    {
        internal const string SourceNodeTagPrefix = "fuse-ng:special-work-source-node=";
        internal const string PresetTagPrefix = "fuse-ng:special-work-preset=";
        internal const string HiddenControlTag = "fuse-ng:hidden-control";
        internal const string GhostControlTag = "fuse-ng:ghost-ng-control";
        internal const string GaugeSeparationNodeTag = "fuse-ng:gauge-separation=narrow-controlled";

        private const float GhostControlLength = 5f;

        private static readonly HashSet<string> HiddenControlSegmentIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> HiddenControlSourceNodeIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static SpecialWorkTopologySynchronizationResult Synchronize(Graph graph)
        {
            var result = new SpecialWorkTopologySynchronizationResult();
            if (graph == null)
            {
                return result;
            }

            TrackSegment[] allSegments = TrackAPI.GetAllSegments()
                .Where(segment => segment != null)
                .ToArray();
            RebuildHiddenControlIndex(allSegments);

            foreach (TrackSegment branch in allSegments
                .Where(IsRealNarrowOnly)
                .OrderBy(segment => segment.id, StringComparer.OrdinalIgnoreCase)
                .ToArray())
            {
                string sourceNodeId = GetTaggedSourceNodeId(branch);
                string presetId = GetTaggedPresetId(branch);
                if (string.IsNullOrEmpty(sourceNodeId))
                {
                    sourceNodeId = FindImplicitNarrowBranchSourceNodeId(branch, allSegments);
                    if (!string.IsNullOrEmpty(sourceNodeId))
                    {
                        presetId = SpecialWorkPresetIds.DualNarrowBranch;
                    }
                }

                if (string.IsNullOrEmpty(sourceNodeId))
                {
                    sourceNodeId = FindImplicitDualToNarrowContinuationSourceNodeId(branch, allSegments);
                    if (!string.IsNullOrEmpty(sourceNodeId))
                    {
                        presetId = SpecialWorkPresetIds.DualSplit;
                    }
                }

                if (string.IsNullOrEmpty(sourceNodeId))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(presetId))
                {
                    presetId = InferPresetId(sourceNodeId, branch, allSegments);
                }

                TrackNode sourceNode = graph.GetNode(sourceNodeId);
                string ghostNodeId = GhostGraphSynchronizer.GetGhostNodeId(sourceNodeId);
                TrackNode ghostNode = graph.GetNode(ghostNodeId);
                bool sourceStillHasNarrowTransition = sourceNode != null
                    && SupportsNarrowTransition(sourceNode, branch, allSegments, presetId);
                bool isContinuation = string.Equals(
                    presetId,
                    SpecialWorkPresetIds.DualSplit,
                    StringComparison.OrdinalIgnoreCase);

                if (!sourceStillHasNarrowTransition)
                {
                    if (sourceNode != null && SegmentTouchesNode(branch, ghostNodeId))
                    {
                        if (TryRewireSegment(
                            branch,
                            ghostNodeId,
                            sourceNodeId,
                            sourceNodeId,
                            presetId,
                            transitionActive: false,
                            out string issue))
                        {
                            result.RestoredSegments++;
                        }
                        else if (!string.IsNullOrEmpty(issue))
                        {
                            result.Issues.Add(issue);
                        }
                    }

                    continue;
                }

                if (isContinuation)
                {
                    result.DualToNarrowContinuations++;
                }
                else
                {
                    result.NarrowBranchTransitions++;
                }

                if (ghostNode == null)
                {
                    result.Issues.Add(
                        $"Narrow branch transition at '{sourceNodeId}' has no generated narrow node '{ghostNodeId}'.");
                    continue;
                }

                if (SegmentTouchesNode(branch, ghostNodeId))
                {
                    continue;
                }

                if (!SegmentTouchesNode(branch, sourceNodeId))
                {
                    result.Issues.Add(
                        $"Narrow branch '{branch.id}' declares transition source '{sourceNodeId}' " +
                        "but touches neither the source nor generated narrow node.");
                    continue;
                }

                if (TryRewireSegment(
                    branch,
                    sourceNodeId,
                    ghostNodeId,
                    sourceNodeId,
                    presetId,
                    transitionActive: true,
                    out string rewireIssue))
                {
                    result.RewiredSegments++;
                }
                else if (!string.IsNullOrEmpty(rewireIssue))
                {
                    result.Issues.Add(rewireIssue);
                }
            }

            EnsureRuntimeGaugeSeparationControls(graph);
            RebuildHiddenControlIndex(TrackAPI.GetAllSegments().Where(segment => segment != null));
            return result;
        }

        internal static string GetTaggedSourceNodeId(TrackSegment segment)
        {
            FuseSegment? definition = segment != null
                ? TrackAPI.GetSegmentDefinition(segment.id)
                : null;

            string? tag = definition?.Tags?.FirstOrDefault(value =>
                value != null
                && value.StartsWith(SourceNodeTagPrefix, StringComparison.OrdinalIgnoreCase));

            return tag == null ? string.Empty : tag.Substring(SourceNodeTagPrefix.Length);
        }

        internal static string GetTaggedPresetId(TrackSegment segment)
        {
            FuseSegment? definition = segment != null
                ? TrackAPI.GetSegmentDefinition(segment.id)
                : null;

            string? tag = definition?.Tags?.FirstOrDefault(value =>
                value != null
                && value.StartsWith(PresetTagPrefix, StringComparison.OrdinalIgnoreCase));

            return tag == null ? string.Empty : tag.Substring(PresetTagPrefix.Length);
        }

        internal static bool IsHiddenControlSegment(TrackSegment? segment)
        {
            if (segment == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(segment.id)
                && HiddenControlSegmentIds.Contains(segment.id))
            {
                return true;
            }

            return HasHiddenControlTag(segment);
        }

        internal static bool IsGaugeSeparationSourceNode(TrackNode? node)
        {
            return node != null
                && !string.IsNullOrEmpty(node.id)
                && (HiddenControlSourceNodeIds.Contains(node.id)
                    || HasGaugeSeparationNodeTag(node));
        }

        internal static bool IsDualToNarrowContinuationSourceNode(TrackNode? node)
        {
            if (node == null
                || Graph.Shared == null
                || string.IsNullOrEmpty(node.id)
                || GhostGraphSynchronizer.IsGeneratedGhostNodeId(node.id))
            {
                return false;
            }

            TrackSegment[] sourceSegments = Graph.Shared.SegmentsConnectedTo(node)
                .Where(segment =>
                    segment != null
                    && !NarrowGaugeManager.IsGeneratedGhost(segment)
                    && !IsHiddenControlSegment(segment))
                .ToArray();
            if (sourceSegments.Count(IsDualSource) != 1
                || sourceSegments.Count(IsRealNarrowOnly) != 0
                || sourceSegments.Count(IsStandardOnly) != 0)
            {
                return false;
            }

            string ghostNodeId = GhostGraphSynchronizer.GetGhostNodeId(node.id);
            TrackNode ghostNode = Graph.Shared.GetNode(ghostNodeId);
            if (ghostNode == null)
            {
                return false;
            }

            return Graph.Shared.SegmentsConnectedTo(ghostNode).Any(segment =>
                IsRealNarrowOnly(segment)
                && string.Equals(
                    GetTaggedSourceNodeId(segment),
                    node.id,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    GetTaggedPresetId(segment),
                    SpecialWorkPresetIds.DualSplit,
                    StringComparison.OrdinalIgnoreCase));
        }

        internal static bool IsGaugeSeparationControlNode(TrackNode? node)
        {
            if (node == null
                || Graph.Shared == null
                || !GhostGraphSynchronizer.IsGeneratedGhostNodeId(node.id))
            {
                return false;
            }

            TrackSegment[] connected = Graph.Shared.SegmentsConnectedTo(node)
                .Where(segment => segment != null)
                .ToArray();
            return connected.Length == 3
                && connected.Count(IsHiddenControlSegment) == 1
                && connected.Count(segment =>
                    GhostGraphSynchronizer.IsGeneratedGhostSegmentId(segment.id)
                    && !IsHiddenControlSegment(segment)) == 1
                && connected.Count(IsRealNarrowOnly) == 1;
        }

        private static bool HasGaugeSeparationNodeTag(TrackNode node)
        {
            FuseNode? definition = node != null
                ? TrackAPI.GetNodeDefinition(node.id)
                : null;
            return definition?.Tags?.Any(tag =>
                string.Equals(tag, GaugeSeparationNodeTag, StringComparison.OrdinalIgnoreCase)) == true;
        }

        private static void RebuildHiddenControlIndex(IEnumerable<TrackSegment> allSegments)
        {
            HiddenControlSegmentIds.Clear();
            HiddenControlSourceNodeIds.Clear();

            foreach (TrackSegment segment in allSegments.Where(HasHiddenControlTag))
            {
                if (!string.IsNullOrEmpty(segment.id))
                {
                    HiddenControlSegmentIds.Add(segment.id);
                }

                string sourceNodeId = GetTaggedSourceNodeId(segment);
                if (!string.IsNullOrEmpty(sourceNodeId))
                {
                    HiddenControlSourceNodeIds.Add(sourceNodeId);
                }
            }
        }

        private static bool HasHiddenControlTag(TrackSegment? segment)
        {
            FuseSegment? definition = segment != null
                ? TrackAPI.GetSegmentDefinition(segment.id)
                : null;
            return definition?.Tags?.Any(tag =>
                string.Equals(tag, HiddenControlTag, StringComparison.OrdinalIgnoreCase)) == true;
        }

        private static void EnsureRuntimeGaugeSeparationControls(Graph graph)
        {
            TrackSegment[] allSegments = TrackAPI.GetAllSegments()
                .Where(segment => segment != null)
                .ToArray();

            foreach (TrackNode sourceNode in TrackAPI.GetAllNodes()
                .Where(node => IsGaugeSeparationTopology(node, allSegments))
                .ToArray())
            {
                string sourceNodeId = sourceNode.id;
                string ghostNodeId = GhostGraphSynchronizer.GetGhostNodeId(sourceNodeId);
                TrackNode ghostNode = graph.GetNode(ghostNodeId);
                TrackSegment? branch = allSegments.FirstOrDefault(segment =>
                    !IsHiddenControlSegment(segment)
                    && IsRealNarrowOnly(segment)
                    && (SegmentTouchesNode(segment, sourceNodeId)
                        || SegmentTouchesNode(segment, ghostNodeId)
                        || string.Equals(
                            GetTaggedSourceNodeId(segment),
                            sourceNodeId,
                            StringComparison.OrdinalIgnoreCase)));
                TrackSegment? ghostDual = allSegments.FirstOrDefault(segment =>
                    GhostGraphSynchronizer.IsGeneratedGhostSegmentId(segment.id)
                    && !IsHiddenControlSegment(segment)
                    && SegmentTouchesNode(segment, ghostNodeId));
                TrackSegment? standardContinuation = allSegments.FirstOrDefault(segment =>
                    IsStandardOnly(segment)
                    && SegmentTouchesNode(segment, sourceNodeId));
                if (ghostNode == null || branch == null || ghostDual == null || standardContinuation == null)
                {
                    Main.Warn(
                        $"Gauge-separation source '{sourceNodeId}' could not create its runtime control: " +
                        $"ghostNode={(ghostNode != null)}, narrowBranch={(branch != null)}, " +
                        $"ghostDual={(ghostDual != null)}, standardContinuation={(standardContinuation != null)}.");
                    continue;
                }

                string controlNodeId = GhostGraphSynchronizer.GeneratedNodePrefix
                    + sourceNodeId
                    + ":control";
                string controlSegmentId = GhostGraphSynchronizer.GeneratedSegmentPrefix
                    + sourceNodeId
                    + ":control";
                BuildGhostControlPose(
                    sourceNode,
                    ghostNode,
                    standardContinuation,
                    out Vector3 controlPosition,
                    out Vector3 controlRotation);

                var controlNodeDef = new FuseNode
                {
                    Position = controlPosition,
                    Rotation = controlRotation,
                    FlipSwitchStand = ghostNode.flipSwitchStand,
                    Tags = new[] { HiddenControlTag, GhostControlTag }
                };

                TrackNode existingControl = TrackAPI.GetNode(controlNodeId);
                if (existingControl == null)
                {
                    TrackAPI.AddNode(controlNodeId, controlNodeDef);
                    Main.Log(
                        $"Created runtime-only gauge-separation control node '{controlNodeId}' " +
                        $"for '{sourceNodeId}'.");
                }
                else if (Vector3.Distance(existingControl.transform.localPosition, controlPosition) > 0.01f)
                {
                    TrackAPI.UpdateNode(controlNodeId, controlNodeDef);
                    Main.Log(
                        $"Updated runtime-only gauge-separation control node '{controlNodeId}' " +
                        $"position for '{sourceNodeId}'.");
                }

                var definition = new FuseSegment
                {
                    StartNodeId = ghostNodeId,
                    EndNodeId = controlNodeId,
                    Style = branch.style.ToString(),
                    TrackClass = branch.trackClass == TrackClass.Mainline
                        ? "main"
                        : branch.trackClass.ToString(),
                    SpeedLimit = branch.speedLimit,
                    Priority = branch.priority,
                    GroupId = branch.groupId,
                    Gauge = GhostGraphSynchronizer.GhostGauge,
                    Tags = new[]
                    {
                        HiddenControlTag,
                        GhostControlTag,
                        SourceNodeTagPrefix + sourceNodeId,
                        PresetTagPrefix + SpecialWorkPresetIds.DualSplit
                    }
                };

                TrackSegment existing = TrackAPI.GetSegment(controlSegmentId);
                if (existing == null)
                {
                    TrackAPI.AddSegment(controlSegmentId, definition);
                    Main.Log(
                        $"Created runtime-only gauge-separation control segment '{controlSegmentId}' " +
                        $"from '{ghostNodeId}' to '{controlNodeId}'.");
                }
                else if (!SegmentTouchesNode(existing, ghostNodeId)
                    || !SegmentTouchesNode(existing, controlNodeId))
                {
                    TrackAPI.RemoveSegment(controlSegmentId);
                    TrackAPI.AddSegment(controlSegmentId, definition);
                }
            }
        }

        private static void BuildGhostControlPose(
            TrackNode sourceNode,
            TrackNode ghostNode,
            TrackSegment standardContinuation,
            out Vector3 controlPosition,
            out Vector3 controlRotation)
        {
            Vector3 origin = ghostNode.transform.localPosition;
            Vector3 direction = DirectionFromNodeAlongSegment(standardContinuation, sourceNode);
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = ghostNode.transform.localRotation * Vector3.forward;
                direction.y = 0f;
            }

            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = Vector3.forward;
            }

            direction.Normalize();
            controlPosition = origin + direction * GhostControlLength;
            controlRotation = Quaternion.LookRotation(direction, Vector3.up).eulerAngles;
        }

        private static Vector3 DirectionFromNodeAlongSegment(TrackSegment segment, TrackNode node)
        {
            LineCurve curve = new LineCurve(
                segment.Curve.Approximate(1.000005f, 0.25f, 16, 20f),
                Hand.Left);
            bool curveRunsAtoB = DualGaugeSharedRailRegistry.CurveRunsAtoB(segment);
            bool nodeAtCurveStart = string.Equals(segment.a?.id, node.id, StringComparison.OrdinalIgnoreCase)
                ? curveRunsAtoB
                : !curveRunsAtoB;
            LinePoint first = nodeAtCurveStart ? curve.Head : curve.Tail;
            LinePoint second = nodeAtCurveStart
                ? curve.LinePointAtDistance(Mathf.Min(curve.Length, 0.5f))
                : curve.LinePointAtDistance(Mathf.Max(0f, curve.Length - 0.5f));
            Vector3 direction = nodeAtCurveStart
                ? second.point - first.point
                : first.point - second.point;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector3.forward;
        }

        private static bool IsGaugeSeparationTopology(
            TrackNode? sourceNode,
            IReadOnlyCollection<TrackSegment> allSegments)
        {
            if (sourceNode == null
                || GhostGraphSynchronizer.IsGeneratedGhostNodeId(sourceNode.id))
            {
                return false;
            }

            TrackSegment[] physical = allSegments
                .Where(segment => !GhostGraphSynchronizer.IsGeneratedGhostSegmentId(segment.id))
                .Where(segment => SegmentTouchesNode(segment, sourceNode.id))
                .ToArray();
            if (physical.Count(IsDualSource) != 1
                || physical.Count(IsStandardOnly) != 1
                || physical.Length != 2)
            {
                return false;
            }

            string ghostNodeId = GhostGraphSynchronizer.GetGhostNodeId(sourceNode.id);
            int narrowBranchCount = allSegments.Count(segment =>
                IsRealNarrowOnly(segment)
                && !IsHiddenControlSegment(segment)
                && SegmentTouchesNode(segment, ghostNodeId));
            if (narrowBranchCount != 1)
            {
                return false;
            }

            int ghostSegmentCount = allSegments.Count(segment =>
                GhostGraphSynchronizer.IsGeneratedGhostSegmentId(segment.id)
                && !IsHiddenControlSegment(segment)
                && SegmentTouchesNode(segment, ghostNodeId));
            if (ghostSegmentCount >= 2)
            {
                return false;
            }

            return true;
        }

        private static bool HasHiddenControlLeg(
            string sourceNodeId,
            IEnumerable<TrackSegment> allSegments)
        {
            string ghostNodeId = GhostGraphSynchronizer.GetGhostNodeId(sourceNodeId);
            return allSegments.Any(segment =>
                IsHiddenControlSegment(segment)
                && (SegmentTouchesNode(segment, sourceNodeId)
                    || SegmentTouchesNode(segment, ghostNodeId)));
        }

        private static string FindImplicitNarrowBranchSourceNodeId(
            TrackSegment branch,
            IReadOnlyCollection<TrackSegment> allSegments)
        {
            foreach (TrackNode endpoint in new[] { branch.a, branch.b }.Where(node => node != null))
            {
                TrackSegment[] connectedSources = allSegments
                    .Where(segment => !NarrowGaugeManager.IsGeneratedGhost(segment))
                    .Where(segment => segment.a == endpoint || segment.b == endpoint)
                    .ToArray();

                int dualCount = connectedSources.Count(IsDualSource);
                int standardCount = connectedSources.Count(IsStandardOnly);
                if (connectedSources.Length == 3
                    && connectedSources.Count(IsRealNarrowOnly) == 1
                    && (dualCount == 2
                        || dualCount == 1 && standardCount == 1))
                {
                    return endpoint.id;
                }
            }

            return string.Empty;
        }

        private static string FindImplicitDualToNarrowContinuationSourceNodeId(
            TrackSegment branch,
            IReadOnlyCollection<TrackSegment> allSegments)
        {
            foreach (TrackNode endpoint in new[] { branch.a, branch.b }.Where(node => node != null))
            {
                if (GhostGraphSynchronizer.IsGeneratedGhostNodeId(endpoint.id))
                {
                    string sourceNodeId = endpoint.id.Substring(GhostGraphSynchronizer.GeneratedNodePrefix.Length);
                    if (IsDualToNarrowContinuationSource(sourceNodeId, branch, allSegments))
                    {
                        return sourceNodeId;
                    }

                    continue;
                }

                TrackSegment[] connectedSources = allSegments
                    .Where(segment =>
                        !NarrowGaugeManager.IsGeneratedGhost(segment)
                        && !IsHiddenControlSegment(segment))
                    .Where(segment => segment.a == endpoint || segment.b == endpoint)
                    .ToArray();

                if (connectedSources.Length == 2
                    && connectedSources.Count(IsDualSource) == 1
                    && connectedSources.Count(IsRealNarrowOnly) == 1
                    && connectedSources.Count(IsStandardOnly) == 0)
                {
                    return endpoint.id;
                }
            }

            return string.Empty;
        }

        private static string InferPresetId(
            string sourceNodeId,
            TrackSegment branch,
            IReadOnlyCollection<TrackSegment> allSegments)
        {
            return IsDualToNarrowContinuationSource(sourceNodeId, branch, allSegments)
                ? SpecialWorkPresetIds.DualSplit
                : SpecialWorkPresetIds.DualNarrowBranch;
        }

        private static bool SupportsNarrowTransition(
            TrackNode node,
            TrackSegment branch,
            IReadOnlyCollection<TrackSegment> allSegments,
            string presetId)
        {
            if (string.Equals(presetId, SpecialWorkPresetIds.DualSplit, StringComparison.OrdinalIgnoreCase))
            {
                return IsDualToNarrowContinuationSource(node.id, branch, allSegments);
            }

            TrackSegment[] connected = allSegments
                .Where(segment => !NarrowGaugeManager.IsGeneratedGhost(segment))
                .Where(segment => segment.a == node || segment.b == node)
                .ToArray();
            int dualCount = connected.Count(IsDualSource);
            int standardCount = connected.Count(IsStandardOnly);

            return dualCount == 2
                || dualCount == 1 && standardCount == 1;
        }

        private static bool IsDualToNarrowContinuationSource(
            string sourceNodeId,
            TrackSegment branch,
            IReadOnlyCollection<TrackSegment> allSegments)
        {
            if (string.IsNullOrWhiteSpace(sourceNodeId))
            {
                return false;
            }

            string ghostNodeId = GhostGraphSynchronizer.GetGhostNodeId(sourceNodeId);
            TrackSegment[] connected = allSegments
                .Where(segment =>
                    segment != null
                    && !NarrowGaugeManager.IsGeneratedGhost(segment)
                    && !IsHiddenControlSegment(segment))
                .Where(segment => SegmentTouchesNode(segment, sourceNodeId))
                .ToArray();

            int dualCount = connected.Count(IsDualSource);
            int standardCount = connected.Count(IsStandardOnly);
            int narrowCount = connected.Count(IsRealNarrowOnly);
            bool branchTouchesSource = connected.Contains(branch);
            bool branchTouchesGhost = SegmentTouchesNode(branch, ghostNodeId);
            bool branchTaggedForSource = string.Equals(
                GetTaggedSourceNodeId(branch),
                sourceNodeId,
                StringComparison.OrdinalIgnoreCase);

            return dualCount == 1
                && standardCount == 0
                && narrowCount <= 1
                && (branchTouchesSource || branchTouchesGhost || branchTaggedForSource);
        }

        private static bool IsDualSource(TrackSegment segment)
        {
            return segment != null
                && !GhostGraphSynchronizer.IsGeneratedGhostSegmentId(segment.id)
                && GhostGraphSynchronizer.IsDualGaugeDefinition(TrackAPI.GetSegmentDefinition(segment.id));
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

        private static bool SegmentTouchesNode(TrackSegment segment, string nodeId)
        {
            return segment != null
                && (string.Equals(segment.a?.id, nodeId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(segment.b?.id, nodeId, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryRewireSegment(
            TrackSegment segment,
            string oldNodeId,
            string newNodeId,
            string sourceNodeId,
            string presetId,
            bool transitionActive,
            out string issue)
        {
            string startId = segment?.a?.id ?? "<none>";
            string endId = segment?.b?.id ?? "<none>";
            string segmentId = segment?.id ?? string.Empty;
            issue = string.Empty;

            if (segment == null || string.IsNullOrWhiteSpace(segmentId))
            {
                issue =
                    $"Skipped special-work topology rewrite for authored segment '{segmentId ?? "<null>"}' " +
                    "because the runtime segment was not available.";
                return false;
            }

            if (IsSegmentOccupied(segment))
            {
                issue =
                    $"Deferred special-work topology rewrite for authored segment '{segmentId}' " +
                    $"endpoints {startId}->{endId} because rolling stock currently occupies it.";
                return false;
            }

            if (TrackAPI.GetNode(newNodeId) == null)
            {
                issue =
                    $"Skipped special-work topology rewrite for authored segment '{segmentId}' " +
                    $"endpoints {startId}->{endId}. Expected node '{newNodeId}' was not found.";
                return false;
            }

            bool replaceStart = string.Equals(startId, oldNodeId, StringComparison.OrdinalIgnoreCase);
            bool replaceEnd = string.Equals(endId, oldNodeId, StringComparison.OrdinalIgnoreCase);
            if (replaceStart == replaceEnd)
            {
                issue =
                    $"Skipped special-work topology rewrite for authored segment '{segmentId}' " +
                    $"endpoints {startId}->{endId}. Expected exactly one endpoint to be '{oldNodeId}'.";
                return false;
            }

            FuseSegment definition = CloneSegmentDefinition(TrackAPI.GetSegmentDefinition(segmentId));
            definition.StartNodeId = replaceStart ? newNodeId : startId;
            definition.EndNodeId = replaceEnd ? newNodeId : endId;
            if (string.Equals(definition.StartNodeId, definition.EndNodeId, StringComparison.OrdinalIgnoreCase))
            {
                issue =
                    $"Skipped special-work topology rewrite for authored segment '{segmentId}' " +
                    $"because it would collapse both endpoints onto '{newNodeId}'.";
                return false;
            }

            if (transitionActive)
            {
                definition.Tags = UpsertSpecialWorkTags(definition.Tags, sourceNodeId, presetId);
            }

            TrackAPI.RemoveSegment(segmentId);
            TrackAPI.AddSegment(segmentId, definition);
            Main.Log(
                $"Rewired special-work narrow segment '{segmentId}' " +
                $"{startId}->{endId} to {definition.StartNodeId}->{definition.EndNodeId} " +
                $"for source '{sourceNodeId}' ({presetId}).");
            return true;
        }

        private static FuseSegment CloneSegmentDefinition(FuseSegment? source)
        {
            return new FuseSegment
            {
                StartNodeId = source?.StartNodeId,
                EndNodeId = source?.EndNodeId,
                Style = source?.Style ?? "standard",
                TrackClass = source?.TrackClass ?? "main",
                SpeedLimit = source?.SpeedLimit ?? 45,
                Priority = source?.Priority ?? 0,
                GroupId = source?.GroupId,
                Tags = source?.Tags?.ToArray(),
                Gauge = source?.Gauge,
                Partial = source?.Partial ?? false,
                PreserveStyle = source?.PreserveStyle ?? false,
                PreserveTrackClass = source?.PreserveTrackClass ?? false,
                PreserveSpeedLimit = source?.PreserveSpeedLimit ?? false,
                PreservePriority = source?.PreservePriority ?? false,
                PreserveGroupId = source?.PreserveGroupId ?? false
            };
        }

        private static string[] UpsertSpecialWorkTags(
            IEnumerable<string>? tags,
            string sourceNodeId,
            string presetId)
        {
            var result = (tags ?? Enumerable.Empty<string>())
                .Where(tag =>
                    !string.IsNullOrWhiteSpace(tag)
                    && !tag.StartsWith(SourceNodeTagPrefix, StringComparison.OrdinalIgnoreCase)
                    && !tag.StartsWith(PresetTagPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            result.Add(SourceNodeTagPrefix + sourceNodeId);
            if (!string.IsNullOrWhiteSpace(presetId))
            {
                result.Add(PresetTagPrefix + presetId);
            }

            return result.ToArray();
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
    }
}
