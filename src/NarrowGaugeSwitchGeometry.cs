using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Track;
using UnityEngine;

namespace NarrowGaugeMod
{
    internal static class NarrowGaugeSwitchGeometry
    {
        public static SwitchGeometry Calculate(
            TrackNode node,
            SegmentProxy a,
            SegmentProxy b,
            Gauge gauge,
            out SegmentProxy sliceA,
            out SegmentProxy sliceB,
            out List<SegmentProxy> remainder)
        {
            remainder = new List<SegmentProxy>();

            var switchGeometry = new SwitchGeometry
            {
                frogPoints = new LinePoint[3],
                leftStockRail = null,
                rightStockRail = null
            };

            AlignSwitchCurves(a, b, out var origin, out var centerA, out var centerB);
            switchGeometry.switchHome = origin;

            var fullRailsA = SwitchGeometry.MakeTrackLineSegments(centerA, gauge);
            var fullRailsB = SwitchGeometry.MakeTrackLineSegments(centerB, gauge);

            LinePoint frogIntersection;
            bool leftHandedFrog;
            if (Intersects(fullRailsA.left, fullRailsB.right, 1.5f, out frogIntersection))
            {
                leftHandedFrog = true;
            }
            else if (Intersects(fullRailsA.right, fullRailsB.left, 1.5f, out frogIntersection))
            {
                leftHandedFrog = false;
            }
            else
            {
                throw new Exception($"Switch tracks do not intersect: {a.Segment.id} and {b.Segment.id}");
            }

            float frogParamA = centerA.ParameterClosestTo(frogIntersection.point);
            float frogParamB = centerB.ParameterClosestTo(frogIntersection.point);

            centerA.Split(frogParamA, out var frogApproachA, out _);
            centerB.Split(frogParamB, out var frogApproachB, out _);

            float switchLengthA = frogApproachA.CalculateLength();
            float switchLengthB = frogApproachB.CalculateLength();

            float sliceParamA = centerA.ParameterForDistance(switchLengthA + 1.5f, 0.01f);
            float sliceParamB = centerB.ParameterForDistance(switchLengthB + 1.5f, 0.01f);

            centerA.Split(sliceParamA, out var sliceCurveA, out var tailCurveA);
            centerB.Split(sliceParamB, out var sliceCurveB, out var tailCurveB);

            sliceA = a.WithCurve(sliceCurveA);
            sliceB = b.WithCurve(sliceCurveB);
            remainder.Add(a.WithCurve(tailCurveA.OffsetBy(origin)));
            remainder.Add(b.WithCurve(tailCurveB.OffsetBy(origin)));

            var slicedRailsA = SwitchGeometry.MakeTrackLineSegments(sliceCurveA, gauge);
            var slicedRailsB = SwitchGeometry.MakeTrackLineSegments(sliceCurveB, gauge);

            LineCurve pointSourceA;
            LineCurve pointSourceB;
            if (leftHandedFrog)
            {
                switchGeometry.leftStockRail = slicedRailsB.left;
                switchGeometry.rightStockRail = slicedRailsA.right;
                switchGeometry.frogPoints[0] = slicedRailsA.left.Points.Last();
                switchGeometry.frogPoints[1] = frogIntersection;
                switchGeometry.frogPoints[2] = slicedRailsB.right.Points.Last();
                pointSourceA = fullRailsA.left;
                pointSourceB = fullRailsB.right;
            }
            else
            {
                switchGeometry.leftStockRail = slicedRailsA.left;
                switchGeometry.rightStockRail = slicedRailsB.right;
                switchGeometry.frogPoints[0] = slicedRailsA.right.Points.Last();
                switchGeometry.frogPoints[1] = frogIntersection;
                switchGeometry.frogPoints[2] = slicedRailsB.left.Points.Last();
                pointSourceA = fullRailsA.right;
                pointSourceB = fullRailsB.left;
            }

            switchGeometry.leftGuardRail = MakeGuardRail(switchGeometry.leftStockRail, frogIntersection);
            switchGeometry.rightGuardRail = MakeGuardRail(switchGeometry.rightStockRail, frogIntersection);

            float cutoffA = pointSourceA.DistanceTo(frogIntersection.point) - 0.45f;
            float cutoffB = pointSourceB.DistanceTo(frogIntersection.point) - 0.45f;

            LineCurve pointRailA = pointSourceA.Take(cutoffA);
            LineCurve pointRailB = pointSourceB.Take(cutoffB);

            Quaternion frogExitRotation = switchGeometry.frogPoints[2].Rotation;
            Quaternion frogEntryRotation = switchGeometry.frogPoints[0].Rotation;

            pointRailA.Add(new LinePoint(
                switchGeometry.frogPoints[2].point +
                frogExitRotation * ((pointRailA.hand == Hand.Left) ? Vector3.left : Vector3.right) * 0.1f,
                frogExitRotation));

            pointRailB.Add(new LinePoint(
                switchGeometry.frogPoints[0].point +
                frogEntryRotation * ((pointRailB.hand == Hand.Left) ? Vector3.left : Vector3.right) * 0.1f,
                frogEntryRotation));

            float pointSplit = Mathf.Lerp(pointRailA.Length, pointRailB.Length, 0.5f) / 2f;
            pointRailA.Split(pointSplit, out switchGeometry.aPointRail, out switchGeometry.aClosureRail);
            pointRailB.Split(pointSplit, out switchGeometry.bPointRail, out switchGeometry.bClosureRail);

            float standParam = centerA.ParameterForDistance(0.4f, 0.1f);
            switchGeometry.standRailCenter = centerA.GetPoint(standParam);

            Vector3 standDirection = centerA.GetDirection(standParam);
            if (node.flipSwitchStand)
            {
                standDirection = -standDirection;
            }

            switchGeometry.standRotation = Quaternion.LookRotation(standDirection);
            switchGeometry.standPosition =
                switchGeometry.standRailCenter +
                switchGeometry.standRotation * new Vector3(0f, -gauge.RailHeight, 0f);

            return switchGeometry;
        }

        public static SwitchGeometry CalculateControlShell(
            TrackNode node,
            SegmentProxy a,
            SegmentProxy b,
            out SegmentProxy sliceA,
            out SegmentProxy sliceB,
            out List<SegmentProxy> remainder)
        {
            sliceA = a;
            sliceB = b;
            remainder = new List<SegmentProxy>();

            Vector3 origin = node.transform.localPosition;
            Vector3 localOrigin = Vector3.zero;
            Vector3 standDirection = DirectionAwayFromNode(
                SpecialWorkTopologySynchronizer.IsHiddenControlSegment(a.Segment) ? b : a,
                node);
            if (standDirection.sqrMagnitude <= 0.0001f)
            {
                standDirection = node.transform.localRotation * Vector3.forward;
            }

            if (node.flipSwitchStand)
            {
                standDirection = -standDirection;
            }

            if (standDirection.sqrMagnitude <= 0.0001f)
            {
                standDirection = Vector3.forward;
            }

            Quaternion standRotation = Quaternion.LookRotation(standDirection.normalized, Vector3.up);
            return new SwitchGeometry
            {
                frogPoints = new[]
                {
                    new LinePoint(localOrigin, standRotation),
                    new LinePoint(localOrigin + standDirection.normalized, standRotation),
                    new LinePoint(localOrigin + standDirection.normalized * 2f, standRotation)
                },
                switchHome = origin,
                standRailCenter = localOrigin,
                standRotation = standRotation,
                standPosition = localOrigin + standRotation * new Vector3(
                    0f,
                    -NarrowGaugeTrackBuilder.ThreeFootGauge.RailHeight,
                    0f)
            };
        }

        private static Vector3 DirectionAwayFromNode(SegmentProxy proxy, TrackNode node)
        {
            if (node == null)
            {
                return Vector3.zero;
            }

            Vector3 nodePoint = node.transform.localPosition;
            Vector3 first = proxy.Curve.EndPoint1;
            Vector3 second = proxy.Curve.EndPoint2;
            Vector3 direction = Vector3.Distance(first, nodePoint) <= Vector3.Distance(second, nodePoint)
                ? proxy.Curve.GetDirection(0f)
                : -proxy.Curve.GetDirection(1f);
            direction.y = 0f;
            return direction;
        }

        private static LineCurve MakeGuardRail(LineCurve stockRail, LinePoint frogPoint)
        {
            float offset = (stockRail.hand == Hand.Left ? 1f : -1f) * 0.15f;
            LineCurve guardRail = stockRail.Parallel(offset);

            for (int i = 0; i < 100; i++)
            {
                float distance = Vector3.Distance(guardRail.Head.point, frogPoint.point);
                if (distance < 1.5f || Mathf.Abs(1.5f - distance) < 0.0015f)
                {
                    break;
                }

                guardRail = guardRail.Skip(distance - 1.5f, false);
            }

            float offsetAmount = Mathf.Tan(10f * Mathf.Deg2Rad) * 0.25f;
            Vector3 lateral = guardRail.hand == Hand.Left ? Vector3.right : Vector3.left;
            Quaternion taperRotation = Quaternion.Euler(0f, 10f * (guardRail.hand == Hand.Left ? -1f : 1f), 0f);

            LinePoint head = guardRail.Head;
            guardRail = guardRail.Skip(0.25f, false);
            guardRail.Insert(0, new LinePoint(head.point + head.Rotation * lateral * offsetAmount, taperRotation * head.Rotation));

            guardRail = guardRail.Reverse();
            head = guardRail.Head;
            guardRail = guardRail.Skip(0.25f, false);
            guardRail.Insert(0, new LinePoint(head.point + head.Rotation * lateral * offsetAmount, taperRotation * head.Rotation));

            return guardRail.Reverse();
        }

        internal static void AlignSwitchCurves(
            SegmentProxy a,
            SegmentProxy b,
            out Vector3 origin,
            out BezierCurve aCurve,
            out BezierCurve bCurve)
        {
            if (a.Curve.EndPoint1 == b.Curve.EndPoint1)
            {
                aCurve = a.Curve;
                bCurve = b.Curve;
            }
            else if (a.Curve.EndPoint1 == b.Curve.EndPoint2)
            {
                aCurve = a.Curve;
                bCurve = b.Curve.Reversed();
            }
            else if (a.Curve.EndPoint2 == b.Curve.EndPoint2)
            {
                aCurve = a.Curve.Reversed();
                bCurve = b.Curve.Reversed();
            }
            else if (a.Curve.EndPoint2 == b.Curve.EndPoint1)
            {
                aCurve = a.Curve.Reversed();
                bCurve = b.Curve;
            }
            else
            {
                throw new Exception($"a {a.Segment.id} and b {b.Segment.id} don't share common endpoint");
            }

            origin = aCurve.EndPoint1;
            aCurve = aCurve.OffsetBy(-origin);
            bCurve = bCurve.OffsetBy(-origin);
        }

        internal static bool Intersects(LineCurve aCurve, LineCurve bCurve, float frogDepth, out LinePoint intersection)
        {
            foreach (var aSegment in aCurve.Segments)
            {
                foreach (var bSegment in bCurve.Segments)
                {
                    if (LineSegment.Intersects(aSegment.Item2, bSegment.Item2, out var point, 0.02f))
                    {
                        LineSegment source = aSegment.Item2;
                        if (source.Length == 0f)
                        {
                            intersection = source.a;
                            return true;
                        }

                        intersection = LinePoint.Lerp(source.a, source.b, (source.a.point - point).magnitude / source.Length);
                        return true;
                    }
                }
            }

            intersection = default;
            return false;
        }
    }
}
