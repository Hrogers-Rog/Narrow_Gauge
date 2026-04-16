using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Track;
using UnityEngine;

namespace NarrowGaugeMod
{
    internal enum ShadowSegmentKind
    {
        NarrowOnly,
        DualGauge
    }

    internal sealed class ShadowNarrowGaugeNode
    {
        public ShadowNarrowGaugeNode(string id, TrackNode sourceNode)
        {
            Id = id;
            SourceNode = sourceNode;
            SourcePosition = sourceNode.transform.position;
            SourceRotation = sourceNode.transform.rotation;
            Position = SourcePosition;
            Rotation = SourceRotation;
            ConnectedSegments = new List<ShadowNarrowGaugeSegment>();
        }

        public string Id { get; }
        public TrackNode SourceNode { get; }
        public Vector3 SourcePosition { get; }
        public Quaternion SourceRotation { get; }
        public Vector3 Position { get; private set; }
        public Quaternion Rotation { get; private set; }
        public List<ShadowNarrowGaugeSegment> ConnectedSegments { get; }

        public bool HasDualGaugeConnection => ConnectedSegments.Any(s => s.Kind == ShadowSegmentKind.DualGauge);
        public bool HasNarrowOnlyConnection => ConnectedSegments.Any(s => s.Kind == ShadowSegmentKind.NarrowOnly);
        public bool RequiresTransition => HasDualGaugeConnection && HasNarrowOnlyConnection;

        internal void SetResolvedTransform(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }
    }

    internal sealed class ShadowNarrowGaugeSegment
    {
        public ShadowNarrowGaugeSegment(
            string id,
            TrackSegment sourceSegment,
            ShadowNarrowGaugeNode a,
            ShadowNarrowGaugeNode b,
            ShadowSegmentKind kind,
            float startOffset,
            float endOffset)
        {
            Id = id;
            SourceSegment = sourceSegment;
            A = a;
            B = b;
            Kind = kind;
            StartOffset = startOffset;
            EndOffset = endOffset;
        }

        public string Id { get; }
        public TrackSegment SourceSegment { get; }
        public ShadowNarrowGaugeNode A { get; }
        public ShadowNarrowGaugeNode B { get; }
        public ShadowSegmentKind Kind { get; }
        public float StartOffset { get; }
        public float EndOffset { get; }

        public bool RequiresTransition => !Mathf.Approximately(StartOffset, EndOffset);
    }

    internal sealed class ShadowNarrowGaugeGraph
    {
        public ShadowNarrowGaugeGraph(
            IReadOnlyDictionary<string, ShadowNarrowGaugeNode> nodes,
            IReadOnlyDictionary<string, ShadowNarrowGaugeSegment> segments,
            IReadOnlyDictionary<string, ShadowNarrowGaugeTransition> transitions)
        {
            Nodes = nodes;
            Segments = segments;
            Transitions = transitions;
        }

        public IReadOnlyDictionary<string, ShadowNarrowGaugeNode> Nodes { get; }
        public IReadOnlyDictionary<string, ShadowNarrowGaugeSegment> Segments { get; }
        public IReadOnlyDictionary<string, ShadowNarrowGaugeTransition> Transitions { get; }

        public int DualSegmentCount => Segments.Values.Count(s => s.Kind == ShadowSegmentKind.DualGauge);
        public int NarrowOnlySegmentCount => Segments.Values.Count(s => s.Kind == ShadowSegmentKind.NarrowOnly);
        public int TransitionNodeCount => Transitions.Count;
    }

    internal sealed class ShadowTransitionAnchor
    {
        public ShadowTransitionAnchor(
            ShadowNarrowGaugeSegment segment,
            bool atStart,
            bool towardNode,
            LineCurve orientedCurve,
            LinePoint nodePoint,
            LinePoint samplePoint)
        {
            Segment = segment;
            AtStart = atStart;
            TowardNode = towardNode;
            OrientedCurve = orientedCurve;
            NodePoint = nodePoint;
            SamplePoint = samplePoint;
        }

        public ShadowNarrowGaugeSegment Segment { get; }
        public bool AtStart { get; }
        public bool TowardNode { get; }
        public LineCurve OrientedCurve { get; }
        public LinePoint NodePoint { get; }
        public LinePoint SamplePoint { get; }
    }

    internal sealed class ShadowNarrowGaugeTransition
    {
        public ShadowNarrowGaugeTransition(
            string id,
            ShadowNarrowGaugeNode node,
            ShadowTransitionAnchor dualAnchor,
            ShadowTransitionAnchor narrowAnchor,
            BezierCurve curve,
            LineCurve sampledCurve)
        {
            Id = id;
            Node = node;
            DualAnchor = dualAnchor;
            NarrowAnchor = narrowAnchor;
            Curve = curve;
            SampledCurve = sampledCurve;
        }

        public string Id { get; }
        public ShadowNarrowGaugeNode Node { get; }
        public ShadowTransitionAnchor DualAnchor { get; }
        public ShadowTransitionAnchor NarrowAnchor { get; }
        public BezierCurve Curve { get; }
        public LineCurve SampledCurve { get; }
    }

    internal static class ShadowNarrowGaugeGraphBuilder
    {
        private const float TransitionPreviewSpan = 2f;

        public static ShadowNarrowGaugeGraph Build(Graph graph)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));

            var nodesById = new Dictionary<string, ShadowNarrowGaugeNode>(StringComparer.OrdinalIgnoreCase);
            var segmentsById = new Dictionary<string, ShadowNarrowGaugeSegment>(StringComparer.OrdinalIgnoreCase);
            var transitionsById = new Dictionary<string, ShadowNarrowGaugeTransition>(StringComparer.OrdinalIgnoreCase);

            foreach (TrackSegment segment in graph.Segments.Where(ShouldIncludeInShadowGraph))
            {
                if (segment.a == null || segment.b == null
                    || string.IsNullOrEmpty(segment.a.id)
                    || string.IsNullOrEmpty(segment.b.id)
                    || string.IsNullOrEmpty(segment.id))
                {
                    continue;
                }

                ShadowNarrowGaugeNode aNode = GetOrCreateNode(nodesById, segment.a);
                ShadowNarrowGaugeNode bNode = GetOrCreateNode(nodesById, segment.b);
                ShadowSegmentKind kind = NarrowGaugeManager.IsDualGauge(segment)
                    ? ShadowSegmentKind.DualGauge
                    : ShadowSegmentKind.NarrowOnly;

                float offset = kind == ShadowSegmentKind.DualGauge
                    ? GetDualGaugeNarrowCenterOffset()
                    : 0f;

                var shadowSegment = new ShadowNarrowGaugeSegment(
                    segment.id,
                    segment,
                    aNode,
                    bNode,
                    kind,
                    offset,
                    offset);

                segmentsById[shadowSegment.Id] = shadowSegment;
                aNode.ConnectedSegments.Add(shadowSegment);
                bNode.ConnectedSegments.Add(shadowSegment);
            }

            foreach (ShadowNarrowGaugeNode node in nodesById.Values)
            {
                ResolveNodeTransform(node);
            }

            foreach (ShadowNarrowGaugeNode node in nodesById.Values.Where(n => n.RequiresTransition))
            {
                if (TryBuildTransition(node, out ShadowNarrowGaugeTransition transition))
                {
                    transitionsById[transition.Id] = transition;
                }
            }

            return new ShadowNarrowGaugeGraph(nodesById, segmentsById, transitionsById);
        }

        private static bool ShouldIncludeInShadowGraph(TrackSegment segment)
        {
            return NarrowGaugeManager.IsNarrowGauge(segment)
                || NarrowGaugeManager.IsDualGauge(segment);
        }

        private static ShadowNarrowGaugeNode GetOrCreateNode(
            IDictionary<string, ShadowNarrowGaugeNode> nodesById,
            TrackNode sourceNode)
        {
            if (!nodesById.TryGetValue(sourceNode.id, out ShadowNarrowGaugeNode shadowNode))
            {
                shadowNode = new ShadowNarrowGaugeNode(sourceNode.id, sourceNode);
                nodesById.Add(sourceNode.id, shadowNode);
            }

            return shadowNode;
        }

        private static void ResolveNodeTransform(ShadowNarrowGaugeNode node)
        {
            if (node.ConnectedSegments.Count == 0)
            {
                node.SetResolvedTransform(node.SourcePosition, node.SourceRotation);
                return;
            }

            var candidatePositions = new List<Vector3>(node.ConnectedSegments.Count);
            Vector3 forwardSum = Vector3.zero;

            foreach (ShadowNarrowGaugeSegment segment in node.ConnectedSegments)
            {
                if (!TryGetOffsetCandidate(segment, node.SourceNode, out Vector3 position, out Vector3 forward))
                    continue;

                candidatePositions.Add(position);
                forwardSum += forward;
            }

            if (candidatePositions.Count == 0)
            {
                node.SetResolvedTransform(node.SourcePosition, node.SourceRotation);
                return;
            }

            Vector3 averagePosition = candidatePositions.Aggregate(Vector3.zero, (sum, next) => sum + next) / candidatePositions.Count;
            Vector3 averageForward = forwardSum.sqrMagnitude > 0.0001f
                ? forwardSum.normalized
                : node.SourceNode.transform.forward;

            if (averageForward.sqrMagnitude <= 0.0001f)
                averageForward = Vector3.forward;

            node.SetResolvedTransform(averagePosition, Quaternion.LookRotation(averageForward, Vector3.up));
        }

        private static bool TryBuildTransition(
            ShadowNarrowGaugeNode node,
            out ShadowNarrowGaugeTransition transition)
        {
            ShadowNarrowGaugeSegment? dualSegment = node.ConnectedSegments
                .FirstOrDefault(s => s.Kind == ShadowSegmentKind.DualGauge);
            ShadowNarrowGaugeSegment? narrowSegment = node.ConnectedSegments
                .FirstOrDefault(s => s.Kind == ShadowSegmentKind.NarrowOnly);

            if (dualSegment == null
                || narrowSegment == null
                || !TryCreateAnchor(dualSegment, node.SourceNode, towardNode: true, out ShadowTransitionAnchor dualAnchor)
                || !TryCreateAnchor(narrowSegment, node.SourceNode, towardNode: false, out ShadowTransitionAnchor narrowAnchor))
            {
                transition = null!;
                return false;
            }

            Vector3 p0 = dualAnchor.SamplePoint.point;
            Vector3 p3 = narrowAnchor.SamplePoint.point;
            Vector3 dualDirection = dualAnchor.SamplePoint.direction.normalized;
            Vector3 narrowDirection = narrowAnchor.SamplePoint.direction.normalized;

            float span = Mathf.Max(Vector3.Distance(p0, p3) * 0.5f, 0.75f);
            Vector3 p1 = p0 + dualDirection * span;
            Vector3 p2 = p3 - narrowDirection * span;

            var curve = new BezierCurve(
                p0,
                p1,
                p2,
                p3,
                Vector3.up,
                Vector3.up);

            var sampledCurve = new LineCurve(
                curve.Approximate(1.000005f, 0.25f, 16, 20f),
                Hand.Left);

            transition = new ShadowNarrowGaugeTransition(
                node.Id,
                node,
                dualAnchor,
                narrowAnchor,
                curve,
                sampledCurve);

            return true;
        }

        private static bool TryCreateAnchor(
            ShadowNarrowGaugeSegment segment,
            TrackNode node,
            bool towardNode,
            out ShadowTransitionAnchor anchor)
        {
            if (!TryGetOrientedCurve(segment, node, towardNode, out LineCurve orientedCurve, out bool atStart))
            {
                anchor = null!;
                return false;
            }

            float sampleDistance = Mathf.Min(TransitionPreviewSpan, Mathf.Max(orientedCurve.Length - 0.05f, 0.05f));
            LinePoint nodePoint = towardNode
                ? orientedCurve.Tail
                : orientedCurve.Head;
            float sampleDistanceFromHead = towardNode
                ? Mathf.Max(orientedCurve.Length - sampleDistance, 0f)
                : sampleDistance;
            LinePoint samplePoint = orientedCurve.LinePointAtDistance(sampleDistanceFromHead);

            anchor = new ShadowTransitionAnchor(
                segment,
                atStart,
                towardNode,
                orientedCurve,
                nodePoint,
                samplePoint);

            return true;
        }

        private static bool TryGetOrientedCurve(
            ShadowNarrowGaugeSegment segment,
            TrackNode node,
            bool towardNode,
            out LineCurve orientedCurve,
            out bool atStart)
        {
            try
            {
                LineCurve baseCurve = new LineCurve(
                    segment.SourceSegment.Curve.Approximate(1.000005f, 0.5f, 16, 40f),
                    Hand.Left);

                float offset = GetSegmentOffsetForNode(segment, node);
                if (!Mathf.Approximately(offset, 0f))
                {
                    baseCurve = baseCurve.Parallel(offset);
                }

                atStart = segment.SourceSegment.a == node;
                bool naturalDirectionMatchesTowardNode = !atStart;
                bool shouldReverse = towardNode != naturalDirectionMatchesTowardNode;
                orientedCurve = shouldReverse ? baseCurve.Reverse() : baseCurve;
                return true;
            }
            catch
            {
                orientedCurve = null!;
                atStart = false;
                return false;
            }
        }

        private static float GetSegmentOffsetForNode(
            ShadowNarrowGaugeSegment segment,
            TrackNode node)
        {
            bool atStart = segment.SourceSegment.a == node;
            return atStart ? segment.StartOffset : segment.EndOffset;
        }

        private static bool TryGetOffsetCandidate(
            ShadowNarrowGaugeSegment segment,
            TrackNode sourceNode,
            out Vector3 position,
            out Vector3 forward)
        {
            try
            {
                LineCurve lineCurve = new LineCurve(
                    segment.SourceSegment.Curve.Approximate(1.000005f, 0.5f, 16, 40f),
                    Hand.Left);

                List<LinePoint> points = lineCurve.Points.ToList();
                if (points.Count < 2)
                {
                    position = sourceNode.transform.position;
                    forward = sourceNode.transform.forward;
                    return false;
                }

                bool atStart = segment.SourceSegment.a == sourceNode;
                LinePoint anchor = atStart ? points[0] : points[points.Count - 1];
                LinePoint neighbor = atStart ? points[1] : points[points.Count - 2];

                forward = atStart
                    ? (neighbor.point - anchor.point).normalized
                    : (neighbor.point - anchor.point).normalized;

                if (forward.sqrMagnitude <= 0.0001f)
                    forward = sourceNode.transform.forward;

                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                float offset = atStart ? segment.StartOffset : segment.EndOffset;
                position = anchor.point + right * offset;
                return true;
            }
            catch
            {
                position = sourceNode.transform.position;
                forward = sourceNode.transform.forward;
                return false;
            }
        }

        private static float GetDualGaugeNarrowCenterOffset()
        {
            return -(Gauge.Standard.Inside - NarrowGaugeTrackBuilder.ThreeFootGauge.Inside) * 0.5f;
        }
    }
}
