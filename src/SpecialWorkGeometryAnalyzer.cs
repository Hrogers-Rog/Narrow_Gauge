using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Track;
using UnityEngine;

namespace NarrowGaugeMod
{
    internal static class SpecialWorkGeometryAnalyzer
    {
        private const float IntersectionTolerance = 0.025f;
        private const float SharedTolerance = 0.035f;
        private const float DuplicatePointTolerance = 0.08f;
        private const float BladeZoneRadius = 1.0f;
        private const float MinimumCrossingAngle = 3f;
        private const float DebugBladeLength = 4f;
        private const float GuardHalfLength = 0.9f;
        private const float GuardOffset = 0.15f;
        private const float GuardDuplicateTolerance = 0.35f;

        public static SpecialWorkAnalysis Analyze(Graph graph, SpecialWorkDefinition definition)
        {
            if (SectionedSpecialWorkBuilder.TryAnalyze(graph, definition, out SpecialWorkAnalysis sectionedAnalysis))
            {
                return sectionedAnalysis;
            }

            WheelPath[] wheelPaths = BuildWheelPaths(definition).ToArray();
            RailCenterline[] rails = BuildPhysicalRails(wheelPaths).ToArray();
            RailIntersectionPrototypeResult prototype =
                RailIntersectionPrototype.Analyze(graph, definition, rails);
            SharedRailInterval[] shared = prototype.Shared.ToArray();
            RailIntersection[] intersections = prototype.Intersections.ToArray();
            SpecialWorkMeshPlan meshPlan =
                SpecialWorkGeometryBuilder.Build(graph, definition, wheelPaths, rails, prototype);
            FrogCandidate[] frogs = meshPlan.Frogs.ToArray();
            GuardRailCandidate[] guards = Array.Empty<GuardRailCandidate>();
            SwitchBladeCandidate[] blades = BuildBlades(graph, definition).ToArray();
            string[] issues = Validate(
                    graph,
                    definition,
                    shared,
                    intersections,
                    frogs,
                    guards,
                    meshPlan.SwitchBlades.Count)
                .Concat(meshPlan.ValidationIssues)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new SpecialWorkAnalysis(
                definition,
                wheelPaths,
                rails,
                shared,
                intersections,
                frogs,
                guards,
                blades,
                issues,
                prototype.Frame,
                meshPlan);
        }

        private static IEnumerable<WheelPath> BuildWheelPaths(SpecialWorkDefinition definition)
        {
            foreach (LogicalRoute route in definition.Routes)
            {
                Gauge gauge = route.Family == GaugeGraphFamily.Narrow
                    ? NarrowGaugeTrackBuilder.ThreeFootGauge
                    : Gauge.Standard;
                string startPort = FindNearestPort(definition, route.Centerline.Head.point, route.Family);
                string endPort = FindNearestPort(definition, route.Centerline.Tail.point, route.Family);
                string leftRailId = route.Id + ":left";
                string rightRailId = route.Id + ":right";
                float flangeGuideOffset = Mathf.Max(
                    0f,
                    gauge.Inside / 2f - Gauge.Standard.HeadWidth * 0.5f);

                yield return new WheelPath(
                    "wheel:" + route.Id,
                    route.Id,
                    route.Family,
                    startPort,
                    endPort,
                    route.Centerline,
                    route.Centerline.Parallel(-flangeGuideOffset, Hand.Left),
                    route.Centerline.Parallel(flangeGuideOffset, Hand.Right),
                    leftRailId,
                    rightRailId,
                    route.SwitchGroupId,
                    route.RequiredStateId);
            }
        }

        private static IEnumerable<RailCenterline> BuildPhysicalRails(
            IEnumerable<WheelPath> wheelPaths)
        {
            foreach (WheelPath path in wheelPaths)
            {
                Gauge gauge = path.Family == GaugeGraphFamily.Narrow
                    ? NarrowGaugeTrackBuilder.ThreeFootGauge
                    : Gauge.Standard;

                yield return new RailCenterline(
                    path.LeftRailId,
                    path.Family,
                    RailSide.Left,
                    path.Centerline.Parallel(-gauge.Inside / 2f, Hand.Left),
                    new[] { path.RouteId },
                    wheelPathId: path.Id,
                    startPortId: path.StartPortId,
                    endPortId: path.EndPortId);
                yield return new RailCenterline(
                    path.RightRailId,
                    path.Family,
                    RailSide.Right,
                    path.Centerline.Parallel(gauge.Inside / 2f, Hand.Right),
                    new[] { path.RouteId },
                    wheelPathId: path.Id,
                    startPortId: path.StartPortId,
                    endPortId: path.EndPortId);
            }
        }

        private static string FindNearestPort(
            SpecialWorkDefinition definition,
            Vector3 point,
            GaugeGraphFamily family)
        {
            GaugeAvailability availability = family == GaugeGraphFamily.Narrow
                ? GaugeAvailability.Narrow
                : GaugeAvailability.Standard;
            SpecialWorkPort? port = definition.Ports
                .Where(item => (item.AvailableFamilies & availability) != 0)
                .OrderBy(item => Vector3.Distance(item.Position, point))
                .FirstOrDefault();
            return port?.Id ?? string.Empty;
        }

        private static IEnumerable<SharedRailInterval> FindSharedIntervals(
            IReadOnlyList<RailCenterline> rails)
        {
            var result = new List<SharedRailInterval>();
            for (int railAIndex = 0; railAIndex < rails.Count; railAIndex++)
            {
                RailCenterline railA = rails[railAIndex];
                foreach ((int _, LineSegment segmentA) in railA.Curve.Segments)
                {
                    if (segmentA.Length < 0.1f)
                    {
                        continue;
                    }

                    for (int railBIndex = railAIndex + 1; railBIndex < rails.Count; railBIndex++)
                    {
                        RailCenterline railB = rails[railBIndex];
                        if (railA.SourceRouteIds.Intersect(railB.SourceRouteIds, StringComparer.OrdinalIgnoreCase).Any())
                        {
                            continue;
                        }

                        foreach ((int _, LineSegment segmentB) in railB.Curve.Segments)
                        {
                            bool sameDirection =
                                Vector3.Distance(segmentA.a.point, segmentB.a.point) <= SharedTolerance
                                && Vector3.Distance(segmentA.b.point, segmentB.b.point) <= SharedTolerance;
                            bool reverseDirection =
                                Vector3.Distance(segmentA.a.point, segmentB.b.point) <= SharedTolerance
                                && Vector3.Distance(segmentA.b.point, segmentB.a.point) <= SharedTolerance;
                            if (!sameDirection && !reverseDirection)
                            {
                                continue;
                            }

                            Vector3 start = sameDirection
                                ? Vector3.Lerp(segmentA.a.point, segmentB.a.point, 0.5f)
                                : Vector3.Lerp(segmentA.a.point, segmentB.b.point, 0.5f);
                            Vector3 end = sameDirection
                                ? Vector3.Lerp(segmentA.b.point, segmentB.b.point, 0.5f)
                                : Vector3.Lerp(segmentA.b.point, segmentB.a.point, 0.5f);

                            if (!result.Any(existing =>
                                Vector3.Distance((existing.Start + existing.End) * 0.5f, (start + end) * 0.5f)
                                <= DuplicatePointTolerance))
                            {
                                result.Add(new SharedRailInterval(start, end));
                            }
                        }
                    }
                }
            }

            return result;
        }

        private static IEnumerable<RailIntersection> FindIntersections(
            Graph graph,
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<SharedRailInterval> sharedIntervals)
        {
            var result = new List<RailIntersection>();
            Vector3[] bladeNodes = definition.NativeSwitchNodeIds
                .Select(id => graph.GetNode(id))
                .Where(node => node != null)
                .Select(node => node!.transform.localPosition)
                .ToArray();

            for (int aIndex = 0; aIndex < rails.Count; aIndex++)
            {
                RailCenterline railA = rails[aIndex];
                var aSegments = railA.Curve.Segments.ToArray();
                float aDistance = 0f;

                for (int aSegmentIndex = 0; aSegmentIndex < aSegments.Length; aSegmentIndex++)
                {
                    LineSegment segmentA = aSegments[aSegmentIndex].Item2;
                    for (int bIndex = aIndex + 1; bIndex < rails.Count; bIndex++)
                    {
                        RailCenterline railB = rails[bIndex];
                        if (railA.SourceRouteIds.Intersect(railB.SourceRouteIds, StringComparer.OrdinalIgnoreCase).Any())
                        {
                            continue;
                        }

                        var bSegments = railB.Curve.Segments.ToArray();
                        float bDistance = 0f;
                        for (int bSegmentIndex = 0; bSegmentIndex < bSegments.Length; bSegmentIndex++)
                        {
                            LineSegment segmentB = bSegments[bSegmentIndex].Item2;
                            if (!TryIntersect2D(segmentA, segmentB, out Vector3 point, out float tA, out float tB))
                            {
                                bDistance += segmentB.Length;
                                continue;
                            }

                            if (sharedIntervals.Any(interval => DistancePointToSegment(point, interval.Start, interval.End) <= SharedTolerance))
                            {
                                bDistance += segmentB.Length;
                                continue;
                            }

                            Vector3 tangentA = Flatten(segmentA.b.point - segmentA.a.point).normalized;
                            Vector3 tangentB = Flatten(segmentB.b.point - segmentB.a.point).normalized;
                            float angle = Mathf.Acos(Mathf.Clamp(Mathf.Abs(Vector3.Dot(tangentA, tangentB)), -1f, 1f))
                                * Mathf.Rad2Deg;

                            RailIntersectionKind kind;
                            if (bladeNodes.Any(node => Vector3.Distance(Flatten(node), Flatten(point)) <= BladeZoneRadius))
                            {
                                kind = RailIntersectionKind.BladeConvergence;
                            }
                            else if (angle < MinimumCrossingAngle)
                            {
                                kind = RailIntersectionKind.InvalidShallowCrossing;
                            }
                            else if (definition.Preset.Category == SpecialWorkCategory.Crossing
                                || definition.Preset.Category == SpecialWorkCategory.Slip
                                || railA.Family != railB.Family)
                            {
                                kind = RailIntersectionKind.CrossingFrog;
                            }
                            else
                            {
                                kind = RailIntersectionKind.VeeFrog;
                            }

                            if (!result.Any(existing =>
                                existing.RailA == railA
                                && existing.RailB == railB
                                && Vector3.Distance(existing.Position, point) <= DuplicatePointTolerance))
                            {
                                result.Add(new RailIntersection(
                                    $"intersection:{result.Count}",
                                    railA,
                                    railB,
                                    aDistance + segmentA.Length * tA,
                                    bDistance + segmentB.Length * tB,
                                    point,
                                    tangentA,
                                    tangentB,
                                    angle,
                                    kind));
                            }

                            bDistance += segmentB.Length;
                        }
                    }

                    aDistance += segmentA.Length;
                }
            }

            return result;
        }

        private static IEnumerable<FrogCandidate> BuildFrogs(
            IEnumerable<RailIntersection> intersections)
        {
            int index = 0;
            foreach (RailIntersection intersection in intersections.Where(item =>
                item.Kind == RailIntersectionKind.VeeFrogCandidate
                || item.Kind == RailIntersectionKind.CrossingFrogCandidate))
            {
                Vector3 a = intersection.TangentA;
                Vector3 b = intersection.TangentB;
                if (Vector3.Dot(a, b) < 0f)
                {
                    b = -b;
                }

                Vector3 bisector = (a + b).normalized;
                if (bisector.sqrMagnitude <= 0.0001f)
                {
                    bisector = a;
                }

                yield return new FrogCandidate("frog:" + index++, intersection, bisector);
            }
        }

        private static IEnumerable<SwitchBladeCandidate> BuildBlades(
            Graph graph,
            SpecialWorkDefinition definition)
        {
            foreach (string nodeId in definition.NativeSwitchNodeIds)
            {
                TrackNode? node = graph.GetNode(nodeId);
                if (node == null
                    || !graph.DecodeSwitchAt(
                        node,
                        out _,
                        out TrackSegment normal,
                        out TrackSegment reversed))
                {
                    continue;
                }

                var curves = new[]
                {
                    TakeFromHead(
                        SpecialWorkRuntimeDiscovery.OrientedSegmentCurve(normal, node, towardNode: false),
                        DebugBladeLength),
                    TakeFromHead(
                        SpecialWorkRuntimeDiscovery.OrientedSegmentCurve(reversed, node, towardNode: false),
                        DebugBladeLength)
                };

                yield return new SwitchBladeCandidate(
                    "blades:" + nodeId,
                    definition.Preset.Category == SpecialWorkCategory.StubSwitch
                        ? MovableAssemblyKind.StubEnd
                        : MovableAssemblyKind.PointBlade,
                    definition.SwitchGroups.FirstOrDefault(group =>
                        group.NativeNodeIds.Contains(nodeId, StringComparer.OrdinalIgnoreCase))?.Id,
                    curves);
            }
        }

        private static IEnumerable<GuardRailCandidate> BuildGuards(
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<FrogCandidate> frogs)
        {
            var result = new List<GuardRailCandidate>();
            foreach (FrogCandidate frog in frogs)
            {
                foreach (RailCenterline crossingRail in new[]
                    {
                        frog.Intersection.RailA,
                        frog.Intersection.RailB
                    })
                {
                    foreach (string routeId in crossingRail.SourceRouteIds)
                    {
                        RailCenterline opposite = rails.FirstOrDefault(rail =>
                            rail != crossingRail
                            && rail.SourceRouteIds.Contains(routeId, StringComparer.OrdinalIgnoreCase)
                            && rail.Side != crossingRail.Side);
                        if (opposite == null)
                        {
                            continue;
                        }

                        float centerDistance = opposite.Curve.DistanceTo(frog.Intersection.Position);
                        float start = Mathf.Max(0f, centerDistance - GuardHalfLength);
                        float end = Mathf.Min(opposite.Curve.Length, centerDistance + GuardHalfLength);
                        if (end - start < 0.25f)
                        {
                            continue;
                        }

                        LineCurve segment = opposite.Curve.Skip(start, true).Take(end - start);
                        float offset = opposite.Side == RailSide.Left ? GuardOffset : -GuardOffset;
                        LineCurve guardCurve = MakeGuardRail(segment.Parallel(offset));
                        Vector3 midpoint = guardCurve.LinePointAtDistance(guardCurve.Length * 0.5f).point;
                        if (result.Any(existing =>
                            CurvesRepresentSamePhysicalGuard(existing.Curve, guardCurve, midpoint)))
                        {
                            continue;
                        }

                        result.Add(new GuardRailCandidate(
                            "guard:" + result.Count,
                            frog.Id,
                            routeId,
                            guardCurve));
                    }
                }
            }

            return result;
        }

        private static LineCurve MakeGuardRail(LineCurve guardRail)
        {
            if (guardRail.Points.Count() < 2 || guardRail.Length < 0.55f)
            {
                return guardRail;
            }

            float taperLength = Mathf.Min(0.25f, guardRail.Length * 0.2f);
            float offsetAmount = Mathf.Tan(10f * Mathf.Deg2Rad) * taperLength;
            Vector3 lateral = guardRail.hand == Hand.Left ? Vector3.right : Vector3.left;
            Quaternion taperRotation = Quaternion.Euler(
                0f,
                10f * (guardRail.hand == Hand.Left ? -1f : 1f),
                0f);

            LinePoint head = guardRail.Head;
            guardRail = guardRail.Skip(taperLength, false);
            guardRail.Insert(
                0,
                new LinePoint(
                    head.point + head.Rotation * lateral * offsetAmount,
                    taperRotation * head.Rotation));

            guardRail = guardRail.Reverse();
            head = guardRail.Head;
            guardRail = guardRail.Skip(taperLength, false);
            guardRail.Insert(
                0,
                new LinePoint(
                    head.point + head.Rotation * lateral * offsetAmount,
                    taperRotation * head.Rotation));
            return guardRail.Reverse();
        }

        private static bool CurvesRepresentSamePhysicalGuard(
            LineCurve existing,
            LineCurve candidate,
            Vector3 candidateMidpoint)
        {
            Vector3 existingMidpoint =
                existing.LinePointAtDistance(existing.Length * 0.5f).point;
            if (Vector3.Distance(existingMidpoint, candidateMidpoint) > GuardDuplicateTolerance)
            {
                return false;
            }

            Vector3 existingDirection = Flatten(existing.Tail.point - existing.Head.point).normalized;
            Vector3 candidateDirection = Flatten(candidate.Tail.point - candidate.Head.point).normalized;
            return Mathf.Abs(Vector3.Dot(existingDirection, candidateDirection)) >= 0.98f;
        }

        private static IEnumerable<string> Validate(
            Graph graph,
            SpecialWorkDefinition definition,
            IReadOnlyList<SharedRailInterval> shared,
            IReadOnlyList<RailIntersection> intersections,
            IReadOnlyList<FrogCandidate> frogs,
            IReadOnlyList<GuardRailCandidate> guards,
            int bladeCount)
        {
            if (definition.Routes.Count != definition.Preset.LogicalRoutes)
            {
                yield return
                    $"Expected {definition.Preset.LogicalRoutes} logical routes but derived {definition.Routes.Count}.";
            }

            if (definition.NativeSwitchNodeIds.Count != definition.Preset.NativeSwitchNodes)
            {
                yield return
                    $"Expected {definition.Preset.NativeSwitchNodes} native switch nodes but derived {definition.NativeSwitchNodeIds.Count}.";
            }

            if (!definition.Preset.Geometry.ExpectedFrogs.Contains(frogs.Count))
            {
                yield return
                    $"Expected frog count {definition.Preset.Geometry.ExpectedFrogs} but derived {frogs.Count}.";
            }

            if (definition.Preset.Geometry.SharedRails != SharedRailRequirement.None
                && shared.Count == 0
                && !definition.Preset.SupportsGhostGraph)
            {
                yield return "Preset requires shared rail intervals but none were derived.";
            }

            if (bladeCount < definition.Preset.Geometry.ExpectedMovableAssemblies
                && definition.SwitchGroups.Count > 0
                && bladeCount == 0)
            {
                yield return
                    "Switch groups exist but no route-divergence blade plans were derived.";
            }

            foreach (RailIntersection intersection in intersections.Where(item =>
                item.Kind == RailIntersectionKind.Unclassified
                || item.Kind == RailIntersectionKind.InvalidShallowCrossing))
            {
                yield return
                    $"Intersection '{intersection.Id}' is {intersection.Kind} at {intersection.Position}.";
            }

            foreach (string nodeId in definition.NativeSwitchNodeIds)
            {
                TrackNode? node = graph.GetNode(nodeId);
                int degree = node == null ? 0 : graph.SegmentsConnectedTo(node).Count;
                if (degree != 3)
                {
                    yield return $"Native switch node '{nodeId}' has degree {degree}, expected 3.";
                }
            }
        }

        private static LineCurve TakeFromHead(LineCurve curve, float length)
        {
            return curve.Take(Mathf.Min(length, Mathf.Max(curve.Length, 0f)));
        }

        private static bool TryIntersect2D(
            LineSegment a,
            LineSegment b,
            out Vector3 point,
            out float tA,
            out float tB)
        {
            Vector2 p = new Vector2(a.a.point.x, a.a.point.z);
            Vector2 r = new Vector2(a.b.point.x - a.a.point.x, a.b.point.z - a.a.point.z);
            Vector2 q = new Vector2(b.a.point.x, b.a.point.z);
            Vector2 s = new Vector2(b.b.point.x - b.a.point.x, b.b.point.z - b.a.point.z);
            float cross = Cross(r, s);
            if (Mathf.Abs(cross) <= 0.00001f)
            {
                point = Vector3.zero;
                tA = 0f;
                tB = 0f;
                return false;
            }

            Vector2 qMinusP = q - p;
            tA = Cross(qMinusP, s) / cross;
            tB = Cross(qMinusP, r) / cross;
            if (tA < -IntersectionTolerance
                || tA > 1f + IntersectionTolerance
                || tB < -IntersectionTolerance
                || tB > 1f + IntersectionTolerance)
            {
                point = Vector3.zero;
                return false;
            }

            tA = Mathf.Clamp01(tA);
            tB = Mathf.Clamp01(tB);
            Vector3 pointA = Vector3.Lerp(a.a.point, a.b.point, tA);
            Vector3 pointB = Vector3.Lerp(b.a.point, b.b.point, tB);
            if (Mathf.Abs(pointA.y - pointB.y) > 0.25f)
            {
                point = Vector3.zero;
                return false;
            }

            point = Vector3.Lerp(pointA, pointB, 0.5f);
            return true;
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private static Vector3 Flatten(Vector3 point)
        {
            return new Vector3(point.x, 0f, point.z);
        }

        private static float DistancePointToSegment(Vector3 point, Vector3 start, Vector3 end)
        {
            Vector3 delta = end - start;
            if (delta.sqrMagnitude <= 0.000001f)
            {
                return Vector3.Distance(point, start);
            }

            float t = Mathf.Clamp01(Vector3.Dot(point - start, delta) / delta.sqrMagnitude);
            return Vector3.Distance(point, start + delta * t);
        }
    }
}
