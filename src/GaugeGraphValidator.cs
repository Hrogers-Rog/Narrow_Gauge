using System;
using System.Collections.Generic;
using System.Linq;
using Track;

namespace NarrowGaugeMod
{
    public sealed class GaugeGraphValidationReport
    {
        internal GaugeGraphValidationReport(IEnumerable<string> issues)
        {
            Issues = issues.ToArray();
        }

        public IReadOnlyList<string> Issues { get; }
        public bool IsValid => Issues.Count == 0;
    }

    public static class GaugeGraphValidator
    {
        public static GaugeGraphValidationReport Validate(Graph graph)
        {
            var issues = new List<string>();
            if (graph == null)
            {
                issues.Add("Track graph is not available.");
                return new GaugeGraphValidationReport(issues);
            }

            foreach (TrackSegment dual in graph.Segments.Where(NarrowGaugeManager.IsDualGauge))
            {
                if (!DualGaugeLinkRegistry.TryGetLink(dual.id, out _))
                {
                    issues.Add($"Dual-gauge segment '{dual.id}' has no generated narrow counterpart link.");
                }
            }

            foreach (TrackSegment ghost in graph.Segments.Where(NarrowGaugeManager.IsGeneratedGhost))
            {
                if (SpecialWorkTopologySynchronizer.IsHiddenControlSegment(ghost))
                {
                    continue;
                }

                if (!DualGaugeLinkRegistry.TryGetLink(ghost.id, out _))
                {
                    issues.Add($"Generated narrow segment '{ghost.id}' is currently unlinked, usually because occupied obsolete track could not be removed.");
                }
            }

            foreach (DualGaugeSegmentLink link in DualGaugeLinkRegistry.Links)
            {
                TrackSegment standard = graph.GetSegment(link.StandardSegmentId);
                TrackSegment narrow = graph.GetSegment(link.NarrowSegmentId);

                if (standard == null)
                {
                    issues.Add($"Dual-gauge link references missing standard segment '{link.StandardSegmentId}'.");
                    continue;
                }

                if (narrow == null)
                {
                    issues.Add($"Dual-gauge link references missing narrow ghost segment '{link.NarrowSegmentId}'.");
                    continue;
                }

                if (!NarrowGaugeManager.IsDualGauge(standard))
                {
                    issues.Add($"Linked standard segment '{standard.id}' is not marked DualGauge.");
                }

                if (!NarrowGaugeManager.IsGeneratedGhost(narrow))
                {
                    issues.Add($"Linked narrow segment '{narrow.id}' is not a generated ghost.");
                }

                if (!string.Equals(narrow.a?.id, GhostGraphSynchronizer.GetGhostNodeId(standard.a?.id ?? string.Empty), StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(narrow.b?.id, GhostGraphSynchronizer.GetGhostNodeId(standard.b?.id ?? string.Empty), StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add($"Dual-gauge link '{standard.id}' has mismatched generated endpoints.");
                }

                if (standard.a == narrow.a
                    || standard.a == narrow.b
                    || standard.b == narrow.a
                    || standard.b == narrow.b)
                {
                    issues.Add($"Dual-gauge link '{standard.id}' shares a native node across graph families.");
                }
            }

            foreach (TrackNode node in graph.Nodes.Where(NarrowGaugeManager.IsGeneratedGhostNode))
            {
                int degree = graph.SegmentsConnectedTo(node).Count();
                if (degree > 3)
                {
                    issues.Add($"Generated narrow node '{node.id}' has degree {degree}; native switch routing supports at most 3.");
                }
            }

            foreach (TrackNode node in graph.Nodes
                .Where(node => node != null && !NarrowGaugeManager.IsGeneratedGhostNode(node)))
            {
                TrackSegment[] connected = graph.SegmentsConnectedTo(node).ToArray();
                if (connected.Length != 3 || connected.Any(segment => !NarrowGaugeManager.IsDualGauge(segment)))
                {
                    continue;
                }

                if (!DualGaugeSwitchSynchronizer.TryResolveCounterpartState(node, false, out _, out _)
                    || !DualGaugeSwitchSynchronizer.TryResolveCounterpartState(node, true, out _, out _))
                {
                    issues.Add($"Fully dual-gauge switch '{node.id}' cannot map both routes to its generated narrow counterpart.");
                }
            }

            foreach (TrackSegment narrowBranch in graph.Segments
                .Where(segment => segment != null && !NarrowGaugeManager.IsGeneratedGhost(segment)))
            {
                string sourceNodeId = SpecialWorkTopologySynchronizer.GetTaggedSourceNodeId(narrowBranch);
                if (string.IsNullOrEmpty(sourceNodeId))
                {
                    continue;
                }

                TrackNode sourceNode = graph.GetNode(sourceNodeId);
                TrackNode ghostNode = graph.GetNode(GhostGraphSynchronizer.GetGhostNodeId(sourceNodeId));
                if (sourceNode == null)
                {
                    issues.Add(
                        $"Special-work narrow branch '{narrowBranch.id}' references missing source node '{sourceNodeId}'.");
                    continue;
                }

                if (ghostNode == null)
                {
                    issues.Add(
                        $"Special-work narrow branch '{narrowBranch.id}' references missing generated narrow node for '{sourceNodeId}'.");
                    continue;
                }

                if (narrowBranch.a != ghostNode && narrowBranch.b != ghostNode)
                {
                    issues.Add(
                        $"Special-work narrow branch '{narrowBranch.id}' is not connected to generated narrow node '{ghostNode.id}'.");
                }

                if (narrowBranch.a == sourceNode || narrowBranch.b == sourceNode)
                {
                    issues.Add(
                        $"Special-work narrow branch '{narrowBranch.id}' still touches standard-family node '{sourceNode.id}'.");
                }

                int sourceDegree = graph.SegmentsConnectedTo(sourceNode).Count;
                int ghostDegree = graph.SegmentsConnectedTo(ghostNode).Count;
                TrackSegment[] sourceSegments = graph.SegmentsConnectedTo(sourceNode).ToArray();
                TrackSegment[] ghostSegments = graph.SegmentsConnectedTo(ghostNode).ToArray();
                string presetId = SpecialWorkTopologySynchronizer.GetTaggedPresetId(narrowBranch);
                bool isDualToNarrowContinuation = string.Equals(
                    presetId,
                    SpecialWorkPresetIds.DualSplit,
                    StringComparison.OrdinalIgnoreCase);
                if (isDualToNarrowContinuation)
                {
                    if (sourceSegments.Count(NarrowGaugeManager.IsDualGauge) != 1)
                    {
                        issues.Add(
                            $"Dual-to-narrow continuation source '{sourceNode.id}' should have exactly one dual-gauge leg.");
                    }

                    if (sourceDegree != 1)
                    {
                        issues.Add(
                            $"Dual-to-narrow continuation source '{sourceNode.id}' has degree {sourceDegree}; expected fixed degree 1.");
                    }

                    if (ghostDegree != 2)
                    {
                        issues.Add(
                            $"Dual-to-narrow continuation node '{ghostNode.id}' has degree {ghostDegree}; expected degree 2.");
                    }

                    continue;
                }

                bool isGaugeSeparation = sourceSegments.Count(NarrowGaugeManager.IsDualGauge) == 1
                    && sourceSegments.Count(segment =>
                        !NarrowGaugeManager.IsDualGauge(segment)
                        && !NarrowGaugeManager.IsNarrowGauge(segment)
                        && !NarrowGaugeManager.IsGeneratedGhost(segment)) == 1;
                if (sourceDegree != 2)
                {
                    issues.Add(
                        $"Special-work standard through node '{sourceNode.id}' has degree {sourceDegree}; expected fixed degree 2.");
                }

                int expectedGhostDegree = isGaugeSeparation
                    && !ghostSegments.Any(SpecialWorkTopologySynchronizer.IsHiddenControlSegment)
                        ? 2
                        : 3;
                if (ghostDegree != expectedGhostDegree)
                {
                    issues.Add(
                        $"Special-work narrow node '{ghostNode.id}' has degree {ghostDegree}; expected degree {expectedGhostDegree}.");
                }
            }

            return new GaugeGraphValidationReport(issues);
        }
    }
}
