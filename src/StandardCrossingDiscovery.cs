using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Track;
using UnityEngine;

namespace NarrowGaugeMod
{
    /// <summary>
    /// Discovers isolated, same-grade crossings between ordinary standard-gauge
    /// segments. A crossing is physical special work only: the two source graph
    /// routes remain continuous and disconnected.
    /// </summary>
    internal static class StandardCrossingDiscovery
    {
        private const float HeightTolerance = 0.25f;
        private const float MinimumAutomaticCrossingAngle = 8f;
        private const float EndpointClearance = 3.5f;
        private const float CrossingHardwareLead = 3.85f;
        private const float IntersectionClusterTolerance = 0.25f;
        private const float CompoundZoneClearance = 8f;
        private const float BoundsTolerance = 0.25f;

        public static IReadOnlyList<SpecialWorkDefinition> Discover(
            Graph graph,
            IEnumerable<TrackSegment> segments)
        {
            if (graph == null)
            {
                return Array.Empty<SpecialWorkDefinition>();
            }

            CrossingRouteCandidate[] routes = (segments ?? Enumerable.Empty<TrackSegment>())
                .Where(IsOrdinaryStandardSegment)
                .Select(TryCreateRouteCandidate)
                .Where(candidate => candidate != null)
                .Cast<CrossingRouteCandidate>()
                .OrderBy(candidate => candidate.MinimumX)
                .ThenBy(candidate => candidate.Segment.id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var raw = new List<CrossingPairCandidate>();

            for (int firstIndex = 0; firstIndex < routes.Length; firstIndex++)
            {
                CrossingRouteCandidate first = routes[firstIndex];
                for (int secondIndex = firstIndex + 1; secondIndex < routes.Length; secondIndex++)
                {
                    CrossingRouteCandidate second = routes[secondIndex];
                    if (second.MinimumX > first.MaximumX + BoundsTolerance)
                    {
                        break;
                    }

                    if (!BoundsOverlap(first, second)
                        || SharesEndpoint(first.Segment, second.Segment)
                        || !TryFindSingleInteriorIntersection(
                            first.Curve,
                            second.Curve,
                            out CenterlineIntersection intersection))
                    {
                        continue;
                    }

                    raw.Add(new CrossingPairCandidate(first, second, intersection));
                }
            }

            var accepted = new List<SpecialWorkDefinition>();
            foreach (CrossingPairCandidate candidate in raw
                .OrderBy(item => item.First.Segment.id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Second.Segment.id, StringComparer.OrdinalIgnoreCase))
            {
                CrossingPairCandidate? conflict = raw.FirstOrDefault(other =>
                    other != candidate && IsOverlappingCompoundZone(candidate, other));
                if (conflict != null)
                {
                    Main.Warn(
                        $"[CrossingDiscovery] Skipped compound/overlapping crossing " +
                        $"'{candidate.First.Segment.id}' x '{candidate.Second.Segment.id}' near " +
                        $"({candidate.Intersection.Position.x:0.00}," +
                        $"{candidate.Intersection.Position.z:0.00}); nearby pair " +
                        $"'{conflict.First.Segment.id}' x '{conflict.Second.Segment.id}' needs a " +
                        "compound special-work preset.");
                    continue;
                }

                SpecialWorkDefinition definition = BuildDefinition(candidate);
                accepted.Add(definition);
                Main.Log(
                    $"[CrossingDiscovery] id={definition.Id} " +
                    $"segments={candidate.First.Segment.id},{candidate.Second.Segment.id} " +
                    $"angle={candidate.Intersection.AcuteAngleDegrees:0.00} " +
                    $"position=({candidate.Intersection.Position.x:0.00}," +
                    $"{candidate.Intersection.Position.y:0.00}," +
                    $"{candidate.Intersection.Position.z:0.00}) graphConnected=false.");
            }

            return accepted;
        }

        private static bool IsOrdinaryStandardSegment(TrackSegment? segment)
        {
            return segment != null
                && segment.a != null
                && segment.b != null
                && !string.IsNullOrWhiteSpace(segment.id)
                && !NarrowGaugeManager.IsNarrowGauge(segment)
                && !NarrowGaugeManager.IsDualGauge(segment)
                && !NarrowGaugeManager.IsGeneratedGhost(segment)
                && !SpecialWorkTopologySynchronizer.IsHiddenControlSegment(segment);
        }

        private static CrossingRouteCandidate? TryCreateRouteCandidate(TrackSegment segment)
        {
            try
            {
                LineCurve curve = SpecialWorkRuntimeDiscovery.OrientedSegmentCurve(
                    segment,
                    segment.a,
                    towardNode: false);
                if (curve.Length < EndpointClearance * 2f + 0.5f)
                {
                    return null;
                }

                Vector3[] points = curve.Points.Select(point => point.point).ToArray();
                if (points.Length < 2)
                {
                    return null;
                }

                return new CrossingRouteCandidate(
                    segment,
                    curve,
                    points.Min(point => point.x),
                    points.Max(point => point.x),
                    points.Min(point => point.z),
                    points.Max(point => point.z));
            }
            catch (Exception ex)
            {
                Main.Warn(
                    $"[CrossingDiscovery] Could not measure standard segment " +
                    $"'{segment?.id ?? "<null>"}': {ex.Message}");
                return null;
            }
        }

        private static bool BoundsOverlap(
            CrossingRouteCandidate first,
            CrossingRouteCandidate second)
        {
            return first.MinimumZ <= second.MaximumZ + BoundsTolerance
                && first.MaximumZ + BoundsTolerance >= second.MinimumZ;
        }

        private static bool SharesEndpoint(TrackSegment first, TrackSegment second)
        {
            return first.a == second.a
                || first.a == second.b
                || first.b == second.a
                || first.b == second.b;
        }

        private static bool TryFindSingleInteriorIntersection(
            LineCurve first,
            LineCurve second,
            out CenterlineIntersection intersection)
        {
            intersection = default;
            var found = new List<CenterlineIntersection>();
            float firstBaseDistance = 0f;
            foreach ((int _, LineSegment firstSegment) in first.Segments)
            {
                float secondBaseDistance = 0f;
                foreach ((int _, LineSegment secondSegment) in second.Segments)
                {
                    if (TryIntersectSegments(
                        firstSegment,
                        secondSegment,
                        out float firstT,
                        out float secondT,
                        out Vector3 position,
                        out float angle))
                    {
                        float firstDistance = firstBaseDistance + firstSegment.Length * firstT;
                        float secondDistance = secondBaseDistance + secondSegment.Length * secondT;
                        if (firstDistance >= EndpointClearance
                            && firstDistance <= first.Length - EndpointClearance
                            && secondDistance >= EndpointClearance
                            && secondDistance <= second.Length - EndpointClearance)
                        {
                            bool clustered = found.Any(item =>
                                HorizontalDistance(item.Position, position)
                                <= IntersectionClusterTolerance);
                            if (!clustered)
                            {
                                found.Add(new CenterlineIntersection(
                                    position,
                                    firstDistance,
                                    secondDistance,
                                    angle));
                            }
                        }
                    }

                    secondBaseDistance += secondSegment.Length;
                }

                firstBaseDistance += firstSegment.Length;
            }

            if (found.Count != 1)
            {
                return false;
            }

            intersection = found[0];
            if (intersection.AcuteAngleDegrees < MinimumAutomaticCrossingAngle)
            {
                return false;
            }

            float halfAngleRadians = Mathf.Max(
                intersection.AcuteAngleDegrees * 0.5f * Mathf.Deg2Rad,
                0.01f);
            float physicalRailSpread = Gauge.Standard.Inside * 0.5f
                / Mathf.Tan(halfAngleRadians);
            float requiredClearance = physicalRailSpread + CrossingHardwareLead;
            return intersection.FirstDistance >= requiredClearance
                && intersection.FirstDistance <= first.Length - requiredClearance
                && intersection.SecondDistance >= requiredClearance
                && intersection.SecondDistance <= second.Length - requiredClearance;
        }

        private static bool TryIntersectSegments(
            LineSegment first,
            LineSegment second,
            out float firstT,
            out float secondT,
            out Vector3 position,
            out float acuteAngle)
        {
            Vector2 p = new Vector2(first.a.point.x, first.a.point.z);
            Vector2 r = new Vector2(
                first.b.point.x - first.a.point.x,
                first.b.point.z - first.a.point.z);
            Vector2 q = new Vector2(second.a.point.x, second.a.point.z);
            Vector2 s = new Vector2(
                second.b.point.x - second.a.point.x,
                second.b.point.z - second.a.point.z);
            float denominator = Cross(r, s);
            if (Mathf.Abs(denominator) <= 0.00001f)
            {
                firstT = 0f;
                secondT = 0f;
                position = Vector3.zero;
                acuteAngle = 0f;
                return false;
            }

            Vector2 delta = q - p;
            firstT = Cross(delta, s) / denominator;
            secondT = Cross(delta, r) / denominator;
            if (firstT < -0.0001f
                || firstT > 1.0001f
                || secondT < -0.0001f
                || secondT > 1.0001f)
            {
                position = Vector3.zero;
                acuteAngle = 0f;
                return false;
            }

            firstT = Mathf.Clamp01(firstT);
            secondT = Mathf.Clamp01(secondT);
            Vector3 firstPoint = Vector3.Lerp(first.a.point, first.b.point, firstT);
            Vector3 secondPoint = Vector3.Lerp(second.a.point, second.b.point, secondT);
            if (Mathf.Abs(firstPoint.y - secondPoint.y) > HeightTolerance)
            {
                position = Vector3.zero;
                acuteAngle = 0f;
                return false;
            }

            Vector2 firstDirection = r.normalized;
            Vector2 secondDirection = s.normalized;
            acuteAngle = Mathf.Acos(Mathf.Clamp(
                Mathf.Abs(Vector2.Dot(firstDirection, secondDirection)),
                -1f,
                1f)) * Mathf.Rad2Deg;
            position = Vector3.Lerp(firstPoint, secondPoint, 0.5f);
            return true;
        }

        private static bool IsOverlappingCompoundZone(
            CrossingPairCandidate first,
            CrossingPairCandidate second)
        {
            foreach (TrackSegment shared in new[]
            {
                first.First.Segment,
                first.Second.Segment
            })
            {
                if (!second.Contains(shared))
                {
                    continue;
                }

                float firstDistance = first.DistanceAlong(shared);
                float secondDistance = second.DistanceAlong(shared);
                if (Mathf.Abs(firstDistance - secondDistance) < CompoundZoneClearance)
                {
                    return true;
                }
            }

            return false;
        }

        private static SpecialWorkDefinition BuildDefinition(CrossingPairCandidate candidate)
        {
            CrossingRouteCandidate[] ordered = new[]
            {
                candidate.First,
                candidate.Second
            }
            .OrderBy(item => item.Segment.id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
            CrossingRouteCandidate routeA = ordered[0];
            CrossingRouteCandidate routeB = ordered[1];

            return new SpecialWorkDefinition(
                $"crossing:{routeA.Segment.id}:{routeB.Segment.id}",
                SpecialWorkPresetCatalog.Get(SpecialWorkPresetIds.CrossingDiamond),
                new[]
                {
                    new SpecialWorkPort("A0", GaugeAvailability.Standard, routeA.Segment.a),
                    new SpecialWorkPort("A1", GaugeAvailability.Standard, routeA.Segment.b),
                    new SpecialWorkPort("B0", GaugeAvailability.Standard, routeB.Segment.a),
                    new SpecialWorkPort("B1", GaugeAvailability.Standard, routeB.Segment.b)
                },
                new[]
                {
                    new LogicalRoute(
                        "crossing-a",
                        GaugeGraphFamily.Standard,
                        routeA.Curve,
                        new[] { routeA.Segment.id }),
                    new LogicalRoute(
                        "crossing-b",
                        GaugeGraphFamily.Standard,
                        routeB.Curve,
                        new[] { routeB.Segment.id })
                },
                Array.Empty<SpecialWorkSwitchGroup>(),
                Array.Empty<string>());
        }

        private static float HorizontalDistance(Vector3 first, Vector3 second)
        {
            first.y = 0f;
            second.y = 0f;
            return Vector3.Distance(first, second);
        }

        private static float Cross(Vector2 first, Vector2 second)
        {
            return first.x * second.y - first.y * second.x;
        }

        private sealed class CrossingRouteCandidate
        {
            public CrossingRouteCandidate(
                TrackSegment segment,
                LineCurve curve,
                float minimumX,
                float maximumX,
                float minimumZ,
                float maximumZ)
            {
                Segment = segment;
                Curve = curve;
                MinimumX = minimumX;
                MaximumX = maximumX;
                MinimumZ = minimumZ;
                MaximumZ = maximumZ;
            }

            public TrackSegment Segment { get; }
            public LineCurve Curve { get; }
            public float MinimumX { get; }
            public float MaximumX { get; }
            public float MinimumZ { get; }
            public float MaximumZ { get; }
        }

        private sealed class CrossingPairCandidate
        {
            public CrossingPairCandidate(
                CrossingRouteCandidate first,
                CrossingRouteCandidate second,
                CenterlineIntersection intersection)
            {
                First = first;
                Second = second;
                Intersection = intersection;
            }

            public CrossingRouteCandidate First { get; }
            public CrossingRouteCandidate Second { get; }
            public CenterlineIntersection Intersection { get; }

            public bool Contains(TrackSegment segment)
            {
                return First.Segment == segment || Second.Segment == segment;
            }

            public float DistanceAlong(TrackSegment segment)
            {
                return First.Segment == segment
                    ? Intersection.FirstDistance
                    : Intersection.SecondDistance;
            }
        }

        private readonly struct CenterlineIntersection
        {
            public CenterlineIntersection(
                Vector3 position,
                float firstDistance,
                float secondDistance,
                float acuteAngleDegrees)
            {
                Position = position;
                FirstDistance = firstDistance;
                SecondDistance = secondDistance;
                AcuteAngleDegrees = acuteAngleDegrees;
            }

            public Vector3 Position { get; }
            public float FirstDistance { get; }
            public float SecondDistance { get; }
            public float AcuteAngleDegrees { get; }
        }
    }
}
