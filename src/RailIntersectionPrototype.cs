using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Track;
using UnityEngine;

namespace NarrowGaugeMod
{
    internal sealed class RailIntersectionPrototypeResult
    {
        public RailIntersectionPrototypeResult(
            RailProjectionFrame frame,
            IEnumerable<SharedRailInterval> shared,
            IEnumerable<RailIntersection> intersections)
        {
            Frame = frame;
            Shared = (shared ?? Enumerable.Empty<SharedRailInterval>()).ToArray();
            Intersections = (intersections ?? Enumerable.Empty<RailIntersection>()).ToArray();
        }

        public RailProjectionFrame Frame { get; }
        public IReadOnlyList<SharedRailInterval> Shared { get; }
        public IReadOnlyList<RailIntersection> Intersections { get; }
    }

    internal static class RailIntersectionPrototype
    {
        private const float SampleSpacing = 0.2f;
        private const float OrientationEpsilon = 0.0001f;
        private const float SharedDistanceTolerance = 0.045f;
        private const float SharedAngleTolerance = 1.5f;
        private const float MinimumSharedLength = 0.12f;
        private const float EndpointJoinTolerance = 0.12f;
        private const float BladeZoneRadius = 1.1f;
        private const float MinimumCrossingAngle = 3f;
        private const float ClusterTolerance = 0.1f;
        private const float HeightTolerance = 0.3f;

        public static RailIntersectionPrototypeResult Analyze(
            Graph graph,
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails)
        {
            RailProjectionFrame frame = BuildFrame(graph, definition);
            foreach (RailCenterline rail in rails)
            {
                ProjectRail(rail, frame);
            }

            SharedRailInterval[] shared = FindSharedIntervals(rails).ToArray();
            RailIntersection[] intersections =
                FindIntersections(graph, definition, rails, shared).ToArray();
            return new RailIntersectionPrototypeResult(frame, shared, intersections);
        }

        private static RailProjectionFrame BuildFrame(
            Graph graph,
            SpecialWorkDefinition definition)
        {
            TrackNode? anchor = definition.NativeSwitchNodeIds
                .Select(graph.GetNode)
                .FirstOrDefault(node => node != null);
            Vector3 origin = anchor != null
                ? anchor.transform.localPosition
                : definition.Ports.FirstOrDefault()?.Position
                    ?? definition.Routes.FirstOrDefault()?.Centerline.Head.point
                    ?? Vector3.zero;

            LogicalRoute? route = definition.Routes.FirstOrDefault();
            Vector3 forward = Vector3.forward;
            if (route != null && route.Centerline.Points.Count() >= 2)
            {
                float distance = route.Centerline.DistanceTo(origin);
                LinePoint point = route.Centerline.LinePointAtDistance(distance);
                forward = point.direction;
            }

            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.0001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            return new RailProjectionFrame(origin, right, forward);
        }

        private static void ProjectRail(RailCenterline rail, RailProjectionFrame frame)
        {
            var samples = new List<RailSample>();
            float length = rail.Curve.Length;
            int count = Mathf.Max(2, Mathf.CeilToInt(length / SampleSpacing) + 1);
            for (int index = 0; index < count; index++)
            {
                float distance = index == count - 1
                    ? length
                    : Mathf.Min(length, index * SampleSpacing);
                Vector3 worldPoint = rail.Curve.LinePointAtDistance(distance).point;
                samples.Add(new RailSample(
                    index,
                    distance,
                    frame.Project(worldPoint),
                    worldPoint));
            }

            var segments = new List<RailSegment2D>();
            for (int index = 0; index + 1 < samples.Count; index++)
            {
                if (Vector2.Distance(samples[index].LocalPoint, samples[index + 1].LocalPoint)
                    > 0.0001f)
                {
                    segments.Add(new RailSegment2D(
                        rail,
                        index,
                        samples[index],
                        samples[index + 1]));
                }
            }

            rail.SetProjection(samples, segments);
        }

        private static IEnumerable<SharedRailInterval> FindSharedIntervals(
            IReadOnlyList<RailCenterline> rails)
        {
            var raw = new List<SharedRailInterval>();
            for (int railAIndex = 0; railAIndex < rails.Count; railAIndex++)
            {
                RailCenterline railA = rails[railAIndex];
                for (int railBIndex = railAIndex + 1; railBIndex < rails.Count; railBIndex++)
                {
                    RailCenterline railB = rails[railBIndex];
                    if (SharesRoute(railA, railB))
                    {
                        continue;
                    }

                    foreach (RailSegment2D segmentA in railA.Segments2D)
                    {
                        foreach (RailSegment2D segmentB in railB.Segments2D)
                        {
                            if (TrySharedOverlap(
                                segmentA,
                                segmentB,
                                out Vector2 localStart,
                                out Vector2 localEnd,
                                out Vector3 worldStart,
                                out Vector3 worldEnd))
                            {
                                raw.Add(new SharedRailInterval(
                                    railA.Id,
                                    railB.Id,
                                    localStart,
                                    localEnd,
                                    worldStart,
                                    worldEnd));
                            }
                        }
                    }
                }
            }

            return MergeSharedIntervals(raw);
        }

        private static IEnumerable<SharedRailInterval> MergeSharedIntervals(
            IEnumerable<SharedRailInterval> intervals)
        {
            var merged = new List<SharedRailInterval>();
            foreach (SharedRailInterval interval in intervals)
            {
                int existingIndex = merged.FindIndex(existing =>
                    SameRailPair(existing, interval)
                    && (Vector2.Distance(existing.LocalEnd, interval.LocalStart) <= 0.08f
                        || Vector2.Distance(existing.LocalStart, interval.LocalEnd) <= 0.08f));
                if (existingIndex < 0)
                {
                    merged.Add(interval);
                    continue;
                }

                SharedRailInterval existing = merged[existingIndex];
                bool append =
                    Vector2.Distance(existing.LocalEnd, interval.LocalStart)
                    <= Vector2.Distance(existing.LocalStart, interval.LocalEnd);
                merged[existingIndex] = append
                    ? new SharedRailInterval(
                        existing.RailAId,
                        existing.RailBId,
                        existing.LocalStart,
                        interval.LocalEnd,
                        existing.Start,
                        interval.End)
                    : new SharedRailInterval(
                        existing.RailAId,
                        existing.RailBId,
                        interval.LocalStart,
                        existing.LocalEnd,
                        interval.Start,
                        existing.End);
            }

            return merged;
        }

        private static IEnumerable<RailIntersection> FindIntersections(
            Graph graph,
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<SharedRailInterval> shared)
        {
            var result = new List<RailIntersection>();
            Vector3[] bladeNodes = definition.NativeSwitchNodeIds
                .Select(graph.GetNode)
                .Where(node => node != null)
                .Select(node => node!.transform.localPosition)
                .ToArray();

            foreach (SharedRailInterval interval in shared)
            {
                RailCenterline? railA = rails.FirstOrDefault(rail => rail.Id == interval.RailAId);
                RailCenterline? railB = rails.FirstOrDefault(rail => rail.Id == interval.RailBId);
                if (railA == null || railB == null)
                {
                    continue;
                }

                Vector3 position = Vector3.Lerp(interval.Start, interval.End, 0.5f);
                Vector2 local = Vector2.Lerp(interval.LocalStart, interval.LocalEnd, 0.5f);
                AddClustered(result, CreateIntersection(
                    railA,
                    railB,
                    railA.Curve.DistanceTo(position),
                    railB.Curve.DistanceTo(position),
                    local,
                    position,
                    (interval.LocalEnd - interval.LocalStart).normalized,
                    (interval.LocalEnd - interval.LocalStart).normalized,
                    RailIntersectionKind.SharedOverlap));
            }

            for (int railAIndex = 0; railAIndex < rails.Count; railAIndex++)
            {
                RailCenterline railA = rails[railAIndex];
                for (int railBIndex = railAIndex + 1; railBIndex < rails.Count; railBIndex++)
                {
                    RailCenterline railB = rails[railBIndex];
                    if (SharesRoute(railA, railB))
                    {
                        continue;
                    }

                    foreach (RailSegment2D segmentA in railA.Segments2D)
                    {
                        foreach (RailSegment2D segmentB in railB.Segments2D)
                        {
                            if (!BoundsOverlap(segmentA, segmentB)
                                || !TryProperIntersection(
                                    segmentA,
                                    segmentB,
                                    out float tA,
                                    out float tB,
                                    out Vector2 localPoint))
                            {
                                continue;
                            }

                            float distanceA = Mathf.Lerp(
                                segmentA.Start.Distance,
                                segmentA.End.Distance,
                                tA);
                            float distanceB = Mathf.Lerp(
                                segmentB.Start.Distance,
                                segmentB.End.Distance,
                                tB);
                            Vector3 worldA = Vector3.Lerp(
                                segmentA.Start.WorldPoint,
                                segmentA.End.WorldPoint,
                                tA);
                            Vector3 worldB = Vector3.Lerp(
                                segmentB.Start.WorldPoint,
                                segmentB.End.WorldPoint,
                                tB);
                            if (Mathf.Abs(worldA.y - worldB.y) > HeightTolerance
                                || shared.Any(interval =>
                                    SameRailPair(interval, railA.Id, railB.Id)
                                    && DistancePointToSegment(
                                        localPoint,
                                        interval.LocalStart,
                                        interval.LocalEnd) <= SharedDistanceTolerance))
                            {
                                continue;
                            }

                            Vector3 worldPoint = Vector3.Lerp(worldA, worldB, 0.5f);
                            Vector2 tangentA = segmentA.Direction;
                            Vector2 tangentB = segmentB.Direction;
                            RailCenterline physicalRailA = ResolvePhysicalOwner(
                                railA,
                                localPoint,
                                rails,
                                shared);
                            RailCenterline physicalRailB = ResolvePhysicalOwner(
                                railB,
                                localPoint,
                                rails,
                                shared);
                            if (physicalRailA == physicalRailB)
                            {
                                continue;
                            }

                            float angle = AcuteAngle(tangentA, tangentB);
                            bool endpointJoin =
                                IsRailEndpoint(
                                    physicalRailA.Curve.DistanceTo(worldPoint),
                                    physicalRailA.Curve.Length)
                                && IsRailEndpoint(
                                    physicalRailB.Curve.DistanceTo(worldPoint),
                                    physicalRailB.Curve.Length);
                            RailIntersectionKind kind;
                            if (bladeNodes.Any(node =>
                                Vector3.Distance(Flatten(node), Flatten(worldPoint))
                                <= BladeZoneRadius))
                            {
                                kind = RailIntersectionKind.BladeConvergence;
                            }
                            else if (endpointJoin)
                            {
                                kind = RailIntersectionKind.RouteJoin;
                            }
                            else if (angle < MinimumCrossingAngle)
                            {
                                kind = RailIntersectionKind.InvalidShallowCrossing;
                            }
                            else if (definition.Preset.Category == SpecialWorkCategory.Crossing
                                || definition.Preset.Category == SpecialWorkCategory.Slip
                                || physicalRailA.Side == physicalRailB.Side)
                            {
                                kind = RailIntersectionKind.CrossingFrogCandidate;
                            }
                            else
                            {
                                kind = RailIntersectionKind.VeeFrogCandidate;
                            }

                            AddClustered(result, CreateIntersection(
                                physicalRailA,
                                physicalRailB,
                                physicalRailA.Curve.DistanceTo(worldPoint),
                                physicalRailB.Curve.DistanceTo(worldPoint),
                                localPoint,
                                worldPoint,
                                tangentA,
                                tangentB,
                                kind));
                        }
                    }
                }
            }

            return result.Select((intersection, index) => new RailIntersection(
                "intersection:" + index,
                intersection.RailA,
                intersection.RailB,
                intersection.DistanceA,
                intersection.DistanceB,
                intersection.LocalPoint,
                intersection.Position,
                intersection.TangentA2D,
                intersection.TangentB2D,
                intersection.TangentA,
                intersection.TangentB,
                intersection.AcuteAngleDegrees,
                intersection.Kind));
        }

        private static RailCenterline ResolvePhysicalOwner(
            RailCenterline rail,
            Vector2 localPoint,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<SharedRailInterval> shared)
        {
            var candidates = new List<RailCenterline> { rail };
            bool added;
            do
            {
                added = false;
                foreach (SharedRailInterval interval in shared.Where(interval =>
                    DistancePointToSegment(
                        localPoint,
                        interval.LocalStart,
                        interval.LocalEnd) <= SharedDistanceTolerance
                    && (candidates.Any(candidate => candidate.Id == interval.RailAId)
                        || candidates.Any(candidate => candidate.Id == interval.RailBId))))
                {
                    foreach (string id in new[] { interval.RailAId, interval.RailBId })
                    {
                        RailCenterline? candidate = rails.FirstOrDefault(item => item.Id == id);
                        if (candidate != null && !candidates.Contains(candidate))
                        {
                            candidates.Add(candidate);
                            added = true;
                        }
                    }
                }
            }
            while (added);

            return candidates
                .OrderBy(candidate =>
                    candidate.Family == GaugeGraphFamily.Standard ? 0 : 1)
                .ThenBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
                .First();
        }

        private static RailIntersection CreateIntersection(
            RailCenterline railA,
            RailCenterline railB,
            float distanceA,
            float distanceB,
            Vector2 localPoint,
            Vector3 worldPoint,
            Vector2 tangentA,
            Vector2 tangentB,
            RailIntersectionKind kind)
        {
            Vector3 tangentA3 = new Vector3(tangentA.x, 0f, tangentA.y);
            Vector3 tangentB3 = new Vector3(tangentB.x, 0f, tangentB.y);
            return new RailIntersection(
                string.Empty,
                railA,
                railB,
                distanceA,
                distanceB,
                localPoint,
                worldPoint,
                tangentA,
                tangentB,
                tangentA3,
                tangentB3,
                AcuteAngle(tangentA, tangentB),
                kind);
        }

        private static void AddClustered(
            ICollection<RailIntersection> result,
            RailIntersection candidate)
        {
            if (result.Any(existing =>
                SameRailPair(existing, candidate)
                && Vector2.Distance(existing.LocalPoint, candidate.LocalPoint)
                    <= ClusterTolerance))
            {
                return;
            }

            result.Add(candidate);
        }

        private static bool TrySharedOverlap(
            RailSegment2D a,
            RailSegment2D b,
            out Vector2 localStart,
            out Vector2 localEnd,
            out Vector3 worldStart,
            out Vector3 worldEnd)
        {
            localStart = Vector2.zero;
            localEnd = Vector2.zero;
            worldStart = Vector3.zero;
            worldEnd = Vector3.zero;
            if (a.Length <= 0.0001f
                || b.Length <= 0.0001f
                || AcuteAngle(a.Direction, b.Direction) > SharedAngleTolerance
                || DistancePointToLine(b.Start.LocalPoint, a.Start.LocalPoint, a.Direction)
                    > SharedDistanceTolerance
                || DistancePointToLine(b.End.LocalPoint, a.Start.LocalPoint, a.Direction)
                    > SharedDistanceTolerance)
            {
                return false;
            }

            float bStart = Vector2.Dot(b.Start.LocalPoint - a.Start.LocalPoint, a.Direction);
            float bEnd = Vector2.Dot(b.End.LocalPoint - a.Start.LocalPoint, a.Direction);
            float overlapStart = Mathf.Max(0f, Mathf.Min(bStart, bEnd));
            float overlapEnd = Mathf.Min(a.Length, Mathf.Max(bStart, bEnd));
            if (overlapEnd - overlapStart < MinimumSharedLength)
            {
                return false;
            }

            float tStart = overlapStart / a.Length;
            float tEnd = overlapEnd / a.Length;
            localStart = Vector2.Lerp(a.Start.LocalPoint, a.End.LocalPoint, tStart);
            localEnd = Vector2.Lerp(a.Start.LocalPoint, a.End.LocalPoint, tEnd);
            worldStart = Vector3.Lerp(a.Start.WorldPoint, a.End.WorldPoint, tStart);
            worldEnd = Vector3.Lerp(a.Start.WorldPoint, a.End.WorldPoint, tEnd);
            return true;
        }

        private static bool TryProperIntersection(
            RailSegment2D a,
            RailSegment2D b,
            out float tA,
            out float tB,
            out Vector2 point)
        {
            Vector2 p = a.Start.LocalPoint;
            Vector2 p2 = a.End.LocalPoint;
            Vector2 q = b.Start.LocalPoint;
            Vector2 q2 = b.End.LocalPoint;
            float o1 = Orientation(p, p2, q);
            float o2 = Orientation(p, p2, q2);
            float o3 = Orientation(q, q2, p);
            float o4 = Orientation(q, q2, p2);
            if (!OppositeSigns(o1, o2) || !OppositeSigns(o3, o4))
            {
                tA = 0f;
                tB = 0f;
                point = Vector2.zero;
                return false;
            }

            Vector2 r = p2 - p;
            Vector2 s = q2 - q;
            float denominator = Cross(r, s);
            if (Mathf.Abs(denominator) <= OrientationEpsilon)
            {
                tA = 0f;
                tB = 0f;
                point = Vector2.zero;
                return false;
            }

            Vector2 qMinusP = q - p;
            tA = Cross(qMinusP, s) / denominator;
            tB = Cross(qMinusP, r) / denominator;
            point = p + r * tA;
            return true;
        }

        private static bool BoundsOverlap(RailSegment2D a, RailSegment2D b)
        {
            float minAx = Mathf.Min(a.Start.LocalPoint.x, a.End.LocalPoint.x);
            float maxAx = Mathf.Max(a.Start.LocalPoint.x, a.End.LocalPoint.x);
            float minAy = Mathf.Min(a.Start.LocalPoint.y, a.End.LocalPoint.y);
            float maxAy = Mathf.Max(a.Start.LocalPoint.y, a.End.LocalPoint.y);
            float minBx = Mathf.Min(b.Start.LocalPoint.x, b.End.LocalPoint.x);
            float maxBx = Mathf.Max(b.Start.LocalPoint.x, b.End.LocalPoint.x);
            float minBy = Mathf.Min(b.Start.LocalPoint.y, b.End.LocalPoint.y);
            float maxBy = Mathf.Max(b.Start.LocalPoint.y, b.End.LocalPoint.y);
            return maxAx + OrientationEpsilon >= minBx
                && maxBx + OrientationEpsilon >= minAx
                && maxAy + OrientationEpsilon >= minBy
                && maxBy + OrientationEpsilon >= minAy;
        }

        private static bool SharesRoute(RailCenterline a, RailCenterline b)
        {
            return a.SourceRouteIds.Intersect(
                b.SourceRouteIds,
                StringComparer.OrdinalIgnoreCase).Any();
        }

        private static bool SameRailPair(
            SharedRailInterval a,
            SharedRailInterval b)
        {
            return SameRailPair(a, b.RailAId, b.RailBId);
        }

        private static bool SameRailPair(
            SharedRailInterval interval,
            string railAId,
            string railBId)
        {
            return (interval.RailAId == railAId && interval.RailBId == railBId)
                || (interval.RailAId == railBId && interval.RailBId == railAId);
        }

        private static bool SameRailPair(
            RailIntersection a,
            RailIntersection b)
        {
            return (a.RailA == b.RailA && a.RailB == b.RailB)
                || (a.RailA == b.RailB && a.RailB == b.RailA);
        }

        private static bool IsRailEndpoint(float distance, float length)
        {
            return distance <= EndpointJoinTolerance
                || length - distance <= EndpointJoinTolerance;
        }

        private static float AcuteAngle(Vector2 a, Vector2 b)
        {
            return Mathf.Acos(Mathf.Clamp(
                Mathf.Abs(Vector2.Dot(a.normalized, b.normalized)),
                -1f,
                1f)) * Mathf.Rad2Deg;
        }

        private static float Orientation(Vector2 a, Vector2 b, Vector2 c)
        {
            return Cross(b - a, c - a);
        }

        private static bool OppositeSigns(float a, float b)
        {
            return (a > OrientationEpsilon && b < -OrientationEpsilon)
                || (a < -OrientationEpsilon && b > OrientationEpsilon);
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private static float DistancePointToLine(
            Vector2 point,
            Vector2 linePoint,
            Vector2 direction)
        {
            return Mathf.Abs(Cross(point - linePoint, direction.normalized));
        }

        private static float DistancePointToSegment(
            Vector2 point,
            Vector2 start,
            Vector2 end)
        {
            Vector2 delta = end - start;
            if (delta.sqrMagnitude <= 0.000001f)
            {
                return Vector2.Distance(point, start);
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - start, delta) / delta.sqrMagnitude);
            return Vector2.Distance(point, start + delta * t);
        }

        private static Vector3 Flatten(Vector3 point)
        {
            return new Vector3(point.x, 0f, point.z);
        }
    }
}
