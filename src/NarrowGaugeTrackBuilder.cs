using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Core;
using HarmonyLib;
using Track;
using UnityEngine;

namespace NarrowGaugeMod
{
    internal static class NarrowGaugeTrackBuilder
    {
        private static readonly ConstructorInfo GaugeConstructor =
            typeof(Gauge).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(float), typeof(float), typeof(float) },
                modifiers: null)
            ?? throw new MissingMethodException("Track.Gauge private constructor not found.");

        internal static readonly Gauge ThreeFootGauge =
            (Gauge)GaugeConstructor.Invoke(new object[] { 0.9144f, Gauge.Standard.HeadWidth, Gauge.Standard.RailHeight });

        // Dual gauge: standard (1.435m) + narrow (0.9144m) sharing the right rail.
        // Three rail positions from centerline: -0.7175m, -0.1969m, +0.7175m.
        // ThirdRailGauge.Inside = 0.3938m so MakeTrackLineSegments gives its left
        // curve at exactly -0.1969m (the narrow inner/middle rail).
        private static readonly Gauge ThirdRailGauge =
            (Gauge)GaugeConstructor.Invoke(new object[] {
                2f * 0.9144f - Gauge.Standard.Inside,   // 0.3938m
                Gauge.Standard.HeadWidth,
                Gauge.Standard.RailHeight });

        private static readonly float BumperScaleX = ThreeFootGauge.Inside / Gauge.Standard.Inside;

        private static readonly Type TrackMeshBuilderType =
            AccessTools.TypeByName("TrackMeshBuilder")
            ?? Type.GetType("TrackMeshBuilder, Assembly-CSharp")
            ?? throw new TypeLoadException("Could not locate TrackMeshBuilder.");

        private static readonly MethodInfo BuildFrogMeshMethod =
            AccessTools.Method(TrackMeshBuilderType, "BuildFrogMesh", new[] { typeof(LinePoint[]), typeof(Gauge) })
            ?? throw new MissingMethodException("TrackMeshBuilder.BuildFrogMesh not found.");

        private static readonly MethodInfo BuildStockRailMeshMethod =
            AccessTools.Method(
                TrackMeshBuilderType,
                "BuildStockRailMesh",
                new[] { typeof(LineCurve), typeof(Vector3), typeof(Gauge), typeof(Func<int, float>) })
            ?? throw new MissingMethodException("TrackMeshBuilder.BuildStockRailMesh not found.");

        private static readonly MethodInfo BuildColliderMeshMethod =
            AccessTools.Method(TrackMeshBuilderType, "BuildColliderMesh", new[] { typeof(BezierCurve), typeof(Gauge) })
            ?? throw new MissingMethodException("TrackMeshBuilder.BuildColliderMesh not found.");

        private static readonly FieldInfo MeshHideFlagsField =
            AccessTools.Field(typeof(TrackObjectBuilder), "_meshHideFlags")
            ?? throw new MissingFieldException("TrackObjectBuilder._meshHideFlags not found.");

        private static readonly FieldInfo TrackLayerField =
            AccessTools.Field(typeof(TrackObjectBuilder), "_trackLayer")
            ?? throw new MissingFieldException("TrackObjectBuilder._trackLayer not found.");

        private static readonly FieldInfo BuilderField =
            AccessTools.Field(typeof(TrackObjectManager), "_builder")
            ?? throw new MissingFieldException("TrackObjectManager._builder not found.");

        private static readonly MethodInfo CreateGeneratedObjectContainerMethod =
            AccessTools.Method(typeof(TrackObjectBuilder), "CreateGeneratedObjectContainer")
            ?? throw new MissingMethodException("TrackObjectBuilder.CreateGeneratedObjectContainer not found.");

        private static readonly MethodInfo CreateMeshObjectMethod =
            AccessTools.Method(typeof(TrackObjectBuilder), "CreateMeshObject")
            ?? throw new MissingMethodException("TrackObjectBuilder.CreateMeshObject not found.");

        private static readonly MethodInfo CreateMeshColliderObjectMethod =
            AccessTools.Method(typeof(TrackObjectBuilder), "CreateMeshColliderObject")
            ?? throw new MissingMethodException("TrackObjectBuilder.CreateMeshColliderObject not found.");

        private static readonly MethodInfo CreateRoadbedMethod =
            AccessTools.Method(typeof(TrackObjectBuilder), "CreateRoadbed")
            ?? throw new MissingMethodException("TrackObjectBuilder.CreateRoadbed not found.");

        private static readonly MethodInfo CreateBumperModelMethod =
            AccessTools.Method(typeof(TrackObjectBuilder), "CreateBumperModel")
            ?? throw new MissingMethodException("TrackObjectBuilder.CreateBumperModel not found.");

        private static readonly MethodInfo CreateSwitchStandMethod =
            AccessTools.Method(typeof(TrackObjectBuilder), "CreateSwitchStand")
            ?? throw new MissingMethodException("TrackObjectBuilder.CreateSwitchStand not found.");

        private static readonly MethodInfo CreateInstancedMeshDrawerMethod =
            AccessTools.Method(
                typeof(TrackObjectBuilder),
                "CreateInstancedMeshDrawer",
                new[] { typeof(Matrix4x4[]), typeof(Vector3), typeof(PrefabInstancer.Prefab), typeof(GameObject) })
            ?? throw new MissingMethodException("TrackObjectBuilder.CreateInstancedMeshDrawer not found.");

        private static readonly HashSet<string> WarnedMixedGaugeSwitches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static bool TryBuild(
            TrackObjectManager manager,
            TrackObjectManager.ITrackDescriptor descriptor,
            out GameObject result)
        {
            result = null!;

            try
            {
                if (manager == null || descriptor == null)
                {
                    return false;
                }

                if (!(BuilderField.GetValue(manager) is TrackObjectBuilder builder))
                {
                    Main.Warn("[Build] TrackObjectBuilder not ready; falling back to standard builder.");
                    return false;
                }

                switch (descriptor.GetType().Name)
                {
                    case "SegmentDescriptor":
                    {
                        SegmentProxy segment = GetFieldValue<SegmentProxy>(descriptor, "segment");

                        if (NarrowGaugeManager.IsDualGauge(segment.Segment))
                        {
                            result = BuildDualGaugeSegment(builder, segment);
                            return result != null;
                        }

                        if (!NarrowGaugeManager.IsNarrowGauge(segment.Segment))
                        {
                            return false;
                        }

                        result = BuildSegment(builder, segment, ThreeFootGauge);
                        return result != null;
                    }

                    case "BumperDescriptor":
                    {
                        TrackNode node = GetFieldValue<TrackNode>(descriptor, "node");
                        if (!IsNarrowBumper(node))
                        {
                            return false;
                        }

                        Vector3 direction = GetFieldValue<Vector3>(descriptor, "direction");
                        TrackSegment.Style style = GetFieldValue<TrackSegment.Style>(descriptor, "style");
                        result = BuildBumper(builder, node, direction, style);
                        return result != null;
                    }

                    case "SwitchDescriptor":
                    {
                        TrackNode node = GetFieldValue<TrackNode>(descriptor, "node");
                        SegmentProxy aProxy = GetFieldValue<SegmentProxy>(descriptor, "aProxy");
                        SegmentProxy bProxy = GetFieldValue<SegmentProxy>(descriptor, "bProxy");

                        bool aNarrow = NarrowGaugeManager.IsNarrowGauge(aProxy.Segment);
                        bool bNarrow = NarrowGaugeManager.IsNarrowGauge(bProxy.Segment);
                        bool aDual   = NarrowGaugeManager.IsDualGauge(aProxy.Segment);
                        bool bDual   = NarrowGaugeManager.IsDualGauge(bProxy.Segment);

                        if (!aNarrow && !bNarrow && !aDual && !bDual)
                        {
                            return false;
                        }

                        SwitchGeometry geometry = GetFieldValue<SwitchGeometry>(descriptor, "geometry");
                        BezierCurve aRoadbedCurve = GetFieldValue<BezierCurve>(descriptor, "aRoadbedCurve");
                        BezierCurve bRoadbedCurve = GetFieldValue<BezierCurve>(descriptor, "bRoadbedCurve");

                        if ((aDual && bDual) || (aDual && bNarrow) || (aNarrow && bDual))
                        {
                            result = BuildDualGaugeSwitch(builder, node, aProxy, bProxy, geometry, aRoadbedCurve, bRoadbedCurve);
                            return result != null;
                        }

                        if (!(aNarrow && bNarrow))
                        {
                            WarnMixedGaugeSwitch(node);
                            return false;
                        }

                        result = BuildSwitch(builder, node, aProxy, bProxy, geometry, aRoadbedCurve, bRoadbedCurve);
                        return result != null;
                    }

                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                Main.Error($"[Build] Narrow gauge replacement failed: {ex}");
                result = null!;
                return false;
            }
        }

        private static GameObject BuildSegment(
            TrackObjectBuilder builder,
            SegmentProxy segment,
            Gauge gauge)
        {
            GameObject container = CreateGeneratedObjectContainer(builder);

            float tieSpacing = 0.55f * (segment.Segment.style == TrackSegment.Style.Bridge ? 0.6f : 1f);
            float tieSpacingJitter = segment.Segment.style == TrackSegment.Style.Bridge ? 0.02f : 0.08f;

            CreateTrackObject(
                builder,
                segment.Curve,
                tieSpacing,
                tieSpacingJitter,
                "seg-" + segment.Segment.id,
                container.transform,
                gauge);

            CreateRoadbed(builder, segment.Curve, container.transform, segment.Segment.style);
            container.SetActive(true);
            return container;
        }

        private static GameObject BuildDualGaugeSegment(
            TrackObjectBuilder builder,
            SegmentProxy segment)
        {
            GameObject container = CreateGeneratedObjectContainer(builder);

            float tieSpacing = 0.55f * (segment.Segment.style == TrackSegment.Style.Bridge ? 0.6f : 1f);
            float tieSpacingJitter = segment.Segment.style == TrackSegment.Style.Bridge ? 0.02f : 0.08f;

            CreateDualGaugeTrackObject(
                builder,
                segment.Curve,
                tieSpacing,
                tieSpacingJitter,
                "seg-" + segment.Segment.id,
                container.transform);

            CreateRoadbed(builder, segment.Curve, container.transform, segment.Segment.style);
            container.SetActive(true);
            return container;
        }

        private static void CreateDualGaugeTrackObject(
            TrackObjectBuilder builder,
            BezierCurve curve,
            float tieSpacing,
            float tieSpacingJitter,
            string trackName,
            Transform parent)
        {
            Vector3 endPoint = curve.EndPoint1;
            BezierCurve localCurve = curve.OffsetBy(-endPoint);

            GameObject root = CreateTrackRoot(builder, trackName, parent);
            root.transform.localPosition = endPoint;

            // Left rail shared. Three rails: left shared (-0.7175m), middle narrow (+0.1969m), right standard (+0.7175m)
            SwitchGeometry.RailLineCurves stdCurves   = SwitchGeometry.MakeTrackLineSegments(localCurve, Gauge.Standard);
            SwitchGeometry.RailLineCurves thirdCurves = SwitchGeometry.MakeTrackLineSegments(localCurve, ThirdRailGauge);

            CreateMeshObject(builder, BuildStockRailMesh(stdCurves.left,    endPoint, Gauge.Standard, _ => 1f), "DualL", root);
            CreateMeshObject(builder, BuildStockRailMesh(thirdCurves.right, endPoint, Gauge.Standard, _ => 1f), "DualM", root);
            CreateMeshObject(builder, BuildStockRailMesh(stdCurves.right,   endPoint, Gauge.Standard, _ => 1f), "DualR", root);

            CreateDualGaugeSegmentTies(builder, localCurve, tieSpacing, tieSpacingJitter, root.transform);
            CreateMeshColliderObject(builder, BuildColliderMesh(localCurve, Gauge.Standard), "Collider", root.transform);
        }

        private static void CreateDualGaugeSegmentTies(
            TrackObjectBuilder builder,
            BezierCurve curve,
            float spacing,
            float tieSpacingJitter,
            Transform parent)
        {
            var ties = new List<PointDirection>();
            var tiePlates = new List<PointDirection>();

            float jitter = tieSpacingJitter / 4f;
            LineCurve lineCurve = new LineCurve(curve.Approximate(1.000005f, 0.5f, 16, 40f), Hand.Left);
            float tieCount = Mathf.Round(lineCurve.Length / spacing);
            if (tieCount == 0f)
            {
                return;
            }

            spacing = lineCurve.Length / tieCount;
            var cursor = lineCurve.CursorAtHead().Skip(spacing / 2f);

            float stdHalf      = Gauge.Standard.Inside / 2f;              // 0.7175m from center
            float thirdRailAbs = ThreeFootGauge.Inside - stdHalf;         // 0.1969m right of center
            float hw           = Gauge.Standard.HeadWidth / 2f;

            for (int i = 0; i < tieCount; i++)
            {
                LinePoint linePoint = cursor.LinePoint();
                Vector3 point = linePoint.point;
                Quaternion rotation = linePoint.Rotation;

                Vector3 tieCenter = point + rotation * Vector3.left * UnityEngine.Random.Range(-jitter, jitter);
                ties.Add(new PointDirection(tieCenter, rotation));

                // Left shared rail
                tiePlates.Add(new PointDirection(point + rotation * Vector3.left  * (stdHalf + hw), rotation));
                // Middle third rail (right of center)
                tiePlates.Add(new PointDirection(point + rotation * Vector3.right * (thirdRailAbs + hw), rotation));
                // Right standard outer rail
                tiePlates.Add(new PointDirection(point + rotation * Vector3.right * (stdHalf + hw), rotation));

                cursor = cursor.Skip(spacing);
            }

            Quaternion tieRotationOffset = Quaternion.Euler(90f, 90f, 0f) * Quaternion.Euler(180f, 0f, 0f);
            Matrix4x4[] tieMatrices = new Matrix4x4[ties.Count];
            for (int i = 0; i < ties.Count; i++)
            {
                PointDirection pd = ties[i];
                float variation = Mathf.PingPong(pd.Position.magnitude, 0.01f);
                Vector3 drop = (-(Gauge.Standard.RailHeight + 0.1f) + variation) * (pd.Rotation * Vector3.up);
                tieMatrices[i] = Matrix4x4.TRS(pd.Position + drop, pd.Rotation * tieRotationOffset, Vector3.one);
            }

            CreateInstancedMeshDrawer(builder, tieMatrices, parent.localPosition, PrefabInstancer.Prefab.Tie, parent.gameObject);

            Quaternion tiePlateRotationOffset = Quaternion.Euler(-90f, 0f, 0f);
            Matrix4x4[] tiePlateMatrices = new Matrix4x4[tiePlates.Count];
            for (int i = 0; i < tiePlates.Count; i++)
            {
                PointDirection pd = tiePlates[i];
                tiePlateMatrices[i] = Matrix4x4.TRS(pd.Position, pd.Rotation * tiePlateRotationOffset, Vector3.one);
            }

            CreateInstancedMeshDrawer(builder, tiePlateMatrices, parent.localPosition, PrefabInstancer.Prefab.TiePlate, parent.gameObject);
        }

        private static GameObject BuildBumper(
            TrackObjectBuilder builder,
            TrackNode node,
            Vector3 direction,
            TrackSegment.Style style)
        {
            GameObject container = CreateGeneratedObjectContainer(builder);
            CreateBumperModel(builder, node, direction, container.transform);

            Transform bumperTransform = container.transform.Find("bumper-" + node.id);
            if (bumperTransform != null)
            {
                Vector3 localScale = bumperTransform.localScale;
                bumperTransform.localScale = new Vector3(BumperScaleX, localScale.y, localScale.z);
            }

            Quaternion rotation = Quaternion.LookRotation(direction);
            Vector3 nodePosition = node.transform.localPosition;
            Vector3 up = node.transform.up;
            BezierCurve curve = new BezierCurve(
                nodePosition,
                nodePosition + rotation * Vector3.forward * 0.1f,
                nodePosition + rotation * Vector3.forward * 1.15f,
                nodePosition + rotation * Vector3.forward * 1.25f,
                up,
                up);

            CreateTrackObject(builder, curve, 0.55f, 0.08f, "bumper-track", container.transform, ThreeFootGauge);
            container.SetActive(true);
            return container;
        }

        private static GameObject BuildDualGaugeSwitch(
            TrackObjectBuilder builder,
            TrackNode node,
            SegmentProxy aProxy,
            SegmentProxy bProxy,
            SwitchGeometry stdGeometry,
            BezierCurve aRoadbedCurve,
            BezierCurve bRoadbedCurve)
        {
            GameObject container = CreateGeneratedObjectContainer(builder);
            container.transform.localPosition = stdGeometry.switchHome;

            bool aDual = NarrowGaugeManager.IsDualGauge(aProxy.Segment);
            bool bDual = NarrowGaugeManager.IsDualGauge(bProxy.Segment);
            bool aNarrowOnly = NarrowGaugeManager.IsNarrowGauge(aProxy.Segment) && !aDual;
            bool bNarrowOnly = NarrowGaugeManager.IsNarrowGauge(bProxy.Segment) && !bDual;

            if (aDual && bNarrowOnly)
            {
                CreateDualGaugeNarrowSplitSwitchRailObjects(builder, stdGeometry, aProxy, bProxy, node, container.transform);
            }
            else
            {
                if (aNarrowOnly && bDual)
                {
                    Main.Warn(
                        $"[Build] Switch '{node.id}' is narrow-through / dual-diverging; using the full dual turnout visual for now.");
                }

                CreateDualGaugeSwitchRailObjects(builder, stdGeometry, aProxy, bProxy, node, container.transform);
            }

            CreateMeshColliderObject(
                builder,
                BuildColliderMesh(aProxy.Curve.OffsetBy(-stdGeometry.switchHome), aDual ? Gauge.Standard : ThreeFootGauge),
                "Collider-a",
                container.transform);
            CreateMeshColliderObject(
                builder,
                BuildColliderMesh(bProxy.Curve.OffsetBy(-stdGeometry.switchHome), bDual ? Gauge.Standard : ThreeFootGauge),
                "Collider-b",
                container.transform);

            CreateRoadbed(builder, aRoadbedCurve, container.transform, TrackSegment.Style.Standard);
            CreateRoadbed(builder, bRoadbedCurve, container.transform, TrackSegment.Style.Standard);

            container.SetActive(true);
            return container;
        }

        private static void CreateDualGaugeSwitchRailObjects(
            TrackObjectBuilder builder,
            SwitchGeometry stdGeometry,
            SegmentProxy aProxy,
            SegmentProxy bProxy,
            TrackNode node,
            Transform parent)
        {
            GameObject root = CreateTrackRoot(builder, "sw-" + node.id, parent);

            CreateMeshObject(builder, BuildStockRailMesh(stdGeometry.leftStockRail, stdGeometry.switchHome, Gauge.Standard, _ => 1f), "StockL", root);
            CreateMeshObject(builder, BuildStockRailMesh(stdGeometry.rightStockRail, stdGeometry.switchHome, Gauge.Standard, _ => 1f), "StockR", root);
            CreateMeshObject(builder, BuildStockRailMesh(stdGeometry.aClosureRail, stdGeometry.switchHome, Gauge.Standard, _ => 1f), "ClosureA", root);
            CreateMeshObject(builder, BuildStockRailMesh(stdGeometry.bClosureRail, stdGeometry.switchHome, Gauge.Standard, _ => 1f), "ClosureB", root);
            CreateMeshObject(builder, BuildStockRailMesh(stdGeometry.leftGuardRail, stdGeometry.switchHome, Gauge.Standard, _ => 1f), "GuardA", root);
            CreateMeshObject(builder, BuildStockRailMesh(stdGeometry.rightGuardRail, stdGeometry.switchHome, Gauge.Standard, _ => 1f), "GuardB", root);

            GameObject stdNormalPoint = CreateDualPointRail(builder, stdGeometry.aPointRail, "PointA", stdGeometry.switchHome, root);
            GameObject stdReversedPoint = CreateDualPointRail(builder, stdGeometry.bPointRail, "PointB", stdGeometry.switchHome, root);

            NarrowGaugeSwitchGeometry.AlignSwitchCurves(aProxy, bProxy, out _, out BezierCurve aAligned, out BezierCurve bAligned);

            SwitchGeometry.RailLineCurves aThirdRails = SwitchGeometry.MakeTrackLineSegments(aAligned, ThirdRailGauge);
            SwitchGeometry.RailLineCurves bThirdRails = SwitchGeometry.MakeTrackLineSegments(bAligned, ThirdRailGauge);

            CreateMeshObject(builder, BuildStockRailMesh(aThirdRails.right, stdGeometry.switchHome, Gauge.Standard, _ => 1f), "ThirdA", root);

            float narrowSplit = (stdGeometry.aPointRail.Length + stdGeometry.bPointRail.Length) * 0.5f;
            LineCurve bMiddle = bThirdRails.right;
            bMiddle.Split(Mathf.Min(narrowSplit, bMiddle.Length * 0.5f), out LineCurve narrowPointCurve, out LineCurve narrowClosure);
            GameObject narrowPoint = CreateDualPointRail(builder, narrowPointCurve, "ThirdBPoint", stdGeometry.switchHome, root);

            CreateMeshObject(builder, BuildStockRailMesh(narrowClosure, stdGeometry.switchHome, Gauge.Standard, _ => 1f), "ThirdBClosure", root);

            GameObject narrowDummy = new GameObject("ThirdBDummy");
            narrowDummy.transform.SetParent(root.transform, false);
            narrowDummy.transform.localPosition = narrowPoint.transform.localPosition;

            CalculatePointRotations(stdGeometry, out float stdNormalRot, out float stdReversedRot);
            root.AddComponent<SwitchPointRails>().Configure(node, stdNormalPoint, stdReversedPoint, stdNormalRot, stdReversedRot);
            root.AddComponent<SwitchPointRails>().Configure(node, narrowDummy, narrowPoint, 0f, stdReversedRot);

            CreateSwitchStand(builder, stdGeometry, node, root.transform);
            CreateSwitchTies(builder, stdGeometry, root.transform, Gauge.Standard);
        }

        private static void CreateDualGaugeNarrowSplitSwitchRailObjects(
            TrackObjectBuilder builder,
            SwitchGeometry stdGeometry,
            SegmentProxy aProxy,
            SegmentProxy bProxy,
            TrackNode node,
            Transform parent)
        {
            GameObject root = CreateTrackRoot(builder, "sw-" + node.id, parent);

            ShadowNarrowGaugeTransition? shadowTransition = NarrowGaugeManager.GetShadowTransition(node);
            NarrowGaugeSwitchGeometry.AlignSwitchCurves(aProxy, bProxy, out _, out BezierCurve aAligned, out BezierCurve bAligned);

            SwitchGeometry.RailLineCurves aStdRails = SwitchGeometry.MakeTrackLineSegments(aAligned, Gauge.Standard);
            SwitchGeometry.RailLineCurves aThirdRails = SwitchGeometry.MakeTrackLineSegments(aAligned, ThirdRailGauge);
            LineCurve branchCenterline = BuildBespokeMixedBranchCenterline(node, stdGeometry.switchHome, shadowTransition, bAligned);
            SwitchGeometry.RailLineCurves bNarrowRails = CreateRailLineCurves(branchCenterline, ThreeFootGauge);
            Vector3 localNodePoint = node.transform.localPosition - stdGeometry.switchHome;

            if (!TryResolveDualGaugeNarrowBranchRails(
                aThirdRails.right,
                bNarrowRails,
                localNodePoint,
                shadowTransition != null,
                out LineCurve branchOuterRail,
                out LineCurve branchRightRail,
                out LinePoint frogIntersection))
            {
                throw new Exception("Could not resolve the narrow diverging rails for the mixed dual switch.");
            }

            branchOuterRail = OrientCurveAwayFromPoint(branchOuterRail, localNodePoint);
            branchRightRail = OrientCurveAwayFromPoint(branchRightRail, localNodePoint);
            LineCurve dualMiddleFromNode = OrientCurveAwayFromPoint(aThirdRails.right, localNodePoint);
            LineCurve leftSharedFromNode = OrientCurveAwayFromPoint(aStdRails.left, localNodePoint);
            CreateMeshObject(builder, BuildStockRailMesh(aStdRails.right, stdGeometry.switchHome, Gauge.Standard, _ => 1f), "DualOuter", root);

            float pointLength = Mathf.Min(
                Mathf.Max(stdGeometry.aPointRail.Length, 0.35f),
                Mathf.Min(
                    Mathf.Max(leftSharedFromNode.Length - 0.15f, 0.35f),
                    Mathf.Max(branchRightRail.Length - 0.15f, 0.35f)));

            leftSharedFromNode.Split(
                pointLength,
                out LineCurve leftSharedPointCurve,
                out LineCurve leftSharedClosure);
            branchRightRail.Split(
                pointLength,
                out LineCurve branchRightPointCurve,
                out LineCurve branchRightClosure);

            // branchOuterRail and dualMiddleFromNode are solid — no blades
            if (branchOuterRail.Points.Any())
            {
                CreateMeshObject(builder, BuildStockRailMesh(branchOuterRail, stdGeometry.switchHome, Gauge.Standard, _ => 1f), "BranchOuter", root);
            }
            if (dualMiddleFromNode.Points.Any())
            {
                CreateMeshObject(builder, BuildStockRailMesh(dualMiddleFromNode, stdGeometry.switchHome, Gauge.Standard, _ => 1f), "DualMiddle", root);
            }
            if (leftSharedClosure.Points.Any())
            {
                CreateMeshObject(builder, BuildStockRailMesh(leftSharedClosure, stdGeometry.switchHome, Gauge.Standard, _ => 1f), "LeftSharedClosure", root);
            }
            if (branchRightClosure.Points.Any())
            {
                CreateMeshObject(builder, BuildStockRailMesh(branchRightClosure, stdGeometry.switchHome, Gauge.Standard, _ => 1f), "BranchRightClosure", root);
            }

            MixedPointProfileTemplate sharedPointTemplate = CreateMixedPointProfileTemplate(
                leftSharedPointCurve,
                tipScale: 0.04f,
                bodyScale: 0.92f);

            GameObject leftSharedPoint = CreateMixedPointRail(
                builder,
                leftSharedPointCurve,
                "LeftSharedPoint",
                stdGeometry.switchHome,
                root,
                sharedPointTemplate);
            GameObject branchRightPoint = CreateMixedPointRail(
                builder,
                branchRightPointCurve,
                "BranchRightPoint",
                stdGeometry.switchHome,
                root,
                sharedPointTemplate);

            // The bespoke dual->narrow turnout reads better without a separate frog mesh here.
            // The crossing geometry is implied by the rails themselves, and skipping the extra
            // frog avoids the stretched/intersecting artifact in the switch throat.

            float leftSharedOpenRot = -CalculateClosedPointRotation(new LineCurve(branchOuterRail.Points.Reverse().ToList(), branchOuterRail.hand), leftSharedPointCurve, Vector3.up) - 0.5f;
            float branchRightOpenRot = CalculateClosedPointRotation(dualMiddleFromNode, branchRightPointCurve, Vector3.up) + 1f;
            root.AddComponent<SwitchPointRails>().Configure(node, leftSharedPoint, branchRightPoint, leftSharedOpenRot, branchRightOpenRot);

            CreateSwitchStand(builder, stdGeometry, node, root.transform);
            CreateSwitchTies(builder, stdGeometry, root.transform, Gauge.Standard);
        }

        private static void CalculateDualGaugeNarrowSplitSlices(
            BezierCurve aAligned,
            BezierCurve bAligned,
            out BezierCurve aSlice,
            out BezierCurve bSlice,
            out LinePoint frogIntersection)
        {
            SwitchGeometry.RailLineCurves aThirdRails = SwitchGeometry.MakeTrackLineSegments(aAligned, ThirdRailGauge);
            SwitchGeometry.RailLineCurves bNarrowRails = SwitchGeometry.MakeTrackLineSegments(bAligned, ThreeFootGauge);

            if (!TryResolveDualGaugeNarrowBranchRails(
                aThirdRails.right,
                bNarrowRails,
                Vector3.zero,
                false,
                out _,
                out _,
                out frogIntersection))
            {
                throw new Exception("Dual gauge / narrow split rails do not intersect.");
            }

            float frogParamA = aAligned.ParameterClosestTo(frogIntersection.point);
            float frogParamB = bAligned.ParameterClosestTo(frogIntersection.point);

            aAligned.Split(frogParamA, out BezierCurve frogApproachA, out _);
            bAligned.Split(frogParamB, out BezierCurve frogApproachB, out _);

            float switchLengthA = frogApproachA.CalculateLength();
            float switchLengthB = frogApproachB.CalculateLength();

            float sliceParamA = aAligned.ParameterForDistance(switchLengthA + 1.5f, 0.01f);
            float sliceParamB = bAligned.ParameterForDistance(switchLengthB + 1.5f, 0.01f);

            aAligned.Split(sliceParamA, out aSlice, out _);
            bAligned.Split(sliceParamB, out bSlice, out _);
        }

        private static bool TryResolveDualGaugeBranchStockRail(
            BezierCurve aAligned,
            BezierCurve bAligned,
            SwitchGeometry stdGeometry,
            out LineCurve dualRouteStockRail,
            out LineCurve branchSideStockRail)
        {
            SwitchGeometry.RailLineCurves aStdRails = SwitchGeometry.MakeTrackLineSegments(aAligned, Gauge.Standard);
            SwitchGeometry.RailLineCurves bStdRails = SwitchGeometry.MakeTrackLineSegments(bAligned, Gauge.Standard);

            if (NarrowGaugeSwitchGeometry.Intersects(aStdRails.left, bStdRails.right, 1.5f, out _))
            {
                dualRouteStockRail = stdGeometry.rightStockRail;
                branchSideStockRail = stdGeometry.leftStockRail;
                return true;
            }

            if (NarrowGaugeSwitchGeometry.Intersects(aStdRails.right, bStdRails.left, 1.5f, out _))
            {
                dualRouteStockRail = stdGeometry.leftStockRail;
                branchSideStockRail = stdGeometry.rightStockRail;
                return true;
            }

            dualRouteStockRail = default!;
            branchSideStockRail = default!;
            return false;
        }

        private static bool TryResolveDualGaugeNarrowBranchRails(
            LineCurve dualMiddleRail,
            SwitchGeometry.RailLineCurves narrowBranchRails,
            Vector3 nodeLocalPoint,
            bool preferShadowResolution,
            out LineCurve branchOuterRail,
            out LineCurve branchRightRail,
            out LinePoint frogIntersection)
        {
            if (preferShadowResolution
                && TryResolveShadowTransitionBranchRails(
                    dualMiddleRail,
                    narrowBranchRails,
                    nodeLocalPoint,
                    out branchOuterRail,
                    out branchRightRail,
                    out frogIntersection))
            {
                return true;
            }

            if (NarrowGaugeSwitchGeometry.Intersects(dualMiddleRail, narrowBranchRails.right, 1.5f, out frogIntersection))
            {
                branchOuterRail = narrowBranchRails.left;
                branchRightRail = narrowBranchRails.right;
                return true;
            }

            if (NarrowGaugeSwitchGeometry.Intersects(dualMiddleRail, narrowBranchRails.left, 1.5f, out frogIntersection))
            {
                branchOuterRail = narrowBranchRails.right;
                branchRightRail = narrowBranchRails.left;
                return true;
            }

            branchOuterRail = default!;
            branchRightRail = default!;
            frogIntersection = default;
            return false;
        }

        private static bool TryResolveShadowTransitionBranchRails(
            LineCurve dualMiddleRail,
            SwitchGeometry.RailLineCurves narrowBranchRails,
            Vector3 nodeLocalPoint,
            out LineCurve branchOuterRail,
            out LineCurve branchRightRail,
            out LinePoint frogIntersection)
        {
            float dualDistance = dualMiddleRail.DistanceTo(nodeLocalPoint);
            LinePoint dualNearPoint = dualMiddleRail.LinePointAtDistance(dualDistance);

            float leftDistance = narrowBranchRails.left.DistanceTo(nodeLocalPoint);
            float rightDistance = narrowBranchRails.right.DistanceTo(nodeLocalPoint);
            LinePoint leftNearPoint = narrowBranchRails.left.LinePointAtDistance(leftDistance);
            LinePoint rightNearPoint = narrowBranchRails.right.LinePointAtDistance(rightDistance);

            float leftToDual = Vector3.Distance(leftNearPoint.point, dualNearPoint.point);
            float rightToDual = Vector3.Distance(rightNearPoint.point, dualNearPoint.point);

            if (leftToDual <= rightToDual)
            {
                branchRightRail = narrowBranchRails.left;
                branchOuterRail = narrowBranchRails.right;
                frogIntersection = LinePoint.Lerp(dualNearPoint, leftNearPoint, 0.5f);
                return true;
            }

            branchRightRail = narrowBranchRails.right;
            branchOuterRail = narrowBranchRails.left;
            frogIntersection = LinePoint.Lerp(dualNearPoint, rightNearPoint, 0.5f);
            return true;
        }

        private static SwitchGeometry.RailLineCurves ResolveMixedBranchRailCurves(
            TrackNode node,
            Vector3 switchHome,
            BezierCurve fallbackCenterCurve)
        {
            ShadowNarrowGaugeTransition? shadowTransition = NarrowGaugeManager.GetShadowTransition(node);
            if (shadowTransition == null)
            {
                return SwitchGeometry.MakeTrackLineSegments(fallbackCenterCurve, ThreeFootGauge);
            }

            Vector3 localNodePoint = node.transform.localPosition - switchHome;
            LineCurve localCenterline = shadowTransition.SampledCurve.Offset(-switchHome);
            float splitDistance = localCenterline.DistanceTo(localNodePoint);
            localCenterline = localCenterline.Skip(splitDistance, false);
            return CreateRailLineCurves(localCenterline, ThreeFootGauge);
        }

        private static LineCurve BuildBespokeMixedBranchCenterline(
            TrackNode node,
            Vector3 switchHome,
            ShadowNarrowGaugeTransition? shadowTransition,
            BezierCurve fallbackCenterCurve)
        {
            LineCurve fallback = ApproximateCurve(fallbackCenterCurve);
            Vector3 localNodePoint = node.transform.localPosition - switchHome;
            if (Vector3.Distance(fallback.Head.point, localNodePoint) > Vector3.Distance(fallback.Tail.point, localNodePoint))
            {
                fallback = fallback.Reverse();
            }

            if (shadowTransition == null)
            {
                return fallback;
            }

            Vector3 startPoint = shadowTransition.DualAnchor.NodePoint.point - switchHome;
            Vector3 endPoint = fallback.Tail.point;
            Vector3 startDirection = (shadowTransition.DualAnchor.NodePoint.point - shadowTransition.DualAnchor.SamplePoint.point).normalized;
            Vector3 endDirection = fallback.Tail.direction.normalized;

            if (startDirection.sqrMagnitude <= 0.0001f)
            {
                startDirection = (fallback.Head.point - startPoint).normalized;
            }

            if (endDirection.sqrMagnitude <= 0.0001f)
            {
                endDirection = (endPoint - fallback.Head.point).normalized;
            }

            float span = Mathf.Max(Vector3.Distance(startPoint, endPoint) * 0.5f, 0.75f);
            var curve = new BezierCurve(
                startPoint,
                startPoint + startDirection * span,
                endPoint - endDirection * span,
                endPoint,
                Vector3.up,
                Vector3.up);

            return new LineCurve(curve.Approximate(1.000005f, 0.25f, 16, 20f), Hand.Left);
        }

        private static SwitchGeometry.RailLineCurves CreateRailLineCurves(LineCurve center, Gauge gauge)
        {
            LineCurve left = center.Parallel(-gauge.Inside / 2f, Hand.Left);
            LineCurve right = center.Parallel(gauge.Inside / 2f, Hand.Right);
            return new SwitchGeometry.RailLineCurves(left, right);
        }

        private static LineCurve ApproximateCurve(BezierCurve curve)
        {
            return new LineCurve(curve.Approximate(1.000005f, 0.25f, 16, 20f), Hand.Left);
        }

        private static LineCurve OrientCurveAwayFromPoint(LineCurve curve, Vector3 point)
        {
            return Vector3.Distance(curve.Head.point, point) <= Vector3.Distance(curve.Tail.point, point)
                ? curve
                : curve.Reverse();
        }

        private static bool TryCreateMixedFrogPoints(
            LineCurve standardThroughRail,
            LineCurve narrowBranchRail,
            LinePoint frogIntersection,
            out LinePoint[] frogPoints)
        {
            float throughCutoff = Mathf.Max(standardThroughRail.DistanceTo(frogIntersection.point) - 0.45f, 0.1f);
            float branchCutoff = Mathf.Max(narrowBranchRail.DistanceTo(frogIntersection.point) - 0.45f, 0.1f);

            LineCurve through = standardThroughRail.Take(throughCutoff);
            LineCurve branch = narrowBranchRail.Take(branchCutoff);

            if (!through.Points.Any() || !branch.Points.Any())
            {
                frogPoints = Array.Empty<LinePoint>();
                return false;
            }

            frogPoints = new[]
            {
                through.Points.Last(),
                frogIntersection,
                branch.Points.Last()
            };

            return true;
        }

        private static LineCurve CreateTransitionRail(LineCurve lowerRail, LineCurve upperRail)
        {
            if (!lowerRail.Points.Any())
            {
                return upperRail;
            }

            if (!upperRail.Points.Any())
            {
                return lowerRail;
            }

            var points = new List<LinePoint>(lowerRail.Points.Count() + upperRail.Points.Count() + 2);
            points.AddRange(lowerRail.Points);

            LinePoint lowerTail = lowerRail.Points.Last();
            LinePoint upperHead = upperRail.Points.First();
            if (Vector3.Distance(lowerTail.point, upperHead.point) > 0.001f)
            {
                points.Add(LinePoint.Lerp(lowerTail, upperHead, 0.33f));
                points.Add(LinePoint.Lerp(lowerTail, upperHead, 0.66f));
            }

            points.AddRange(upperRail.Points);
            return new LineCurve(points, lowerRail.hand);
        }

        private static float CalculateClosedPointRotation(
            LineCurve closedAgainstRail,
            LineCurve movingPointRail,
            Vector3 upAxis)
        {
            Vector3 fixedStart = closedAgainstRail.Points.First().point;
            Vector3 movingStart = movingPointRail.Points.First().point;
            Vector3 movingEnd = movingPointRail.Points.Last().point;

            Vector3 closedLead = (movingStart - fixedStart).normalized * 0.2f + movingStart;

            return Vector3.SignedAngle(
                closedLead - movingEnd,
                movingStart - movingEnd,
                upAxis);
        }

        private static GameObject CreateDualPointRail(
            TrackObjectBuilder builder,
            LineCurve pointRail,
            string objectName,
            Vector3 switchHome,
            GameObject root)
        {
            Vector3 point = pointRail.Points.Last().point;
            Mesh mesh = BuildStockRailMesh(
                ReprofilePointRail(pointRail).Offset(-point),
                switchHome,
                Gauge.Standard,
                i => i == 0 ? 0.1f : 1f);

            GameObject rail = CreateMeshObject(builder, mesh, objectName, root);
            rail.transform.localPosition = point;
            return rail;
        }

        private static GameObject CreateMixedPointRail(
            TrackObjectBuilder builder,
            LineCurve pointRail,
            string objectName,
            Vector3 switchHome,
            GameObject root,
            MixedPointProfileTemplate profileTemplate)
        {
            Vector3 point = pointRail.Points.Last().point;
            LineCurve profile = ReprofileMixedPointRail(
                pointRail,
                profileTemplate.TipTrim,
                profileTemplate.TaperLength);
            int pointCount = profile.Points.Count();
            int taperPoints = Mathf.Clamp(profileTemplate.TaperPoints, 2, Mathf.Max(pointCount, 2));
            Mesh mesh = BuildStockRailMesh(
                profile.Offset(-point),
                switchHome,
                Gauge.Standard,
                i =>
                {
                    if (pointCount <= 1)
                    {
                        return 1f;
                    }

                    if (i >= taperPoints)
                    {
                        return profileTemplate.BodyScale;
                    }

                    float t = taperPoints <= 1
                        ? 1f
                        : (float)i / (taperPoints - 1);
                    return Mathf.Lerp(profileTemplate.TipScale, profileTemplate.BodyScale, t * t);
                });

            GameObject rail = CreateMeshObject(builder, mesh, objectName, root);
            rail.transform.localPosition = point;
            return rail;
        }

        private static void CalculatePointRotations(
            SwitchGeometry geometry,
            out float normalRot,
            out float reversedRot)
        {
            Vector3 pointAEnd   = geometry.aPointRail.Points.Last().point;
            Vector3 pointBEnd   = geometry.bPointRail.Points.Last().point;
            Vector3 pointAStart = geometry.aPointRail.Points.First().point;
            Vector3 pointBStart = geometry.bPointRail.Points.First().point;

            Vector3 normalLead   = (pointAStart - pointBStart).normalized * 0.2f + pointAStart;
            Vector3 reversedLead = (pointBStart - pointAStart).normalized * 0.2f + pointBStart;

            normalRot = Vector3.SignedAngle(
                normalLead - pointAEnd,
                pointAStart - pointAEnd,
                geometry.frogPoints[1].Rotation * Vector3.up);

            reversedRot = Vector3.SignedAngle(
                reversedLead - pointBEnd,
                pointBStart - pointBEnd,
                geometry.frogPoints[1].Rotation * Vector3.up);
        }

        private static GameObject BuildSwitch(
            TrackObjectBuilder builder,
            TrackNode node,
            SegmentProxy aProxy,
            SegmentProxy bProxy,
            SwitchGeometry geometry,
            BezierCurve aRoadbedCurve,
            BezierCurve bRoadbedCurve)
        {
            GameObject container = CreateGeneratedObjectContainer(builder);
            container.transform.localPosition = geometry.switchHome;
            CreateSwitchObject(builder, geometry, node, aProxy.Curve, aRoadbedCurve, bProxy.Curve, bRoadbedCurve, container.transform);
            container.SetActive(true);
            return container;
        }

        private static void CreateSwitchObject(
            TrackObjectBuilder builder,
            SwitchGeometry geometry,
            TrackNode node,
            BezierCurve aCurve,
            BezierCurve aRoadbedCurve,
            BezierCurve bCurve,
            BezierCurve bRoadbedCurve,
            Transform parent)
        {
            CreateSwitchRailObjects(builder, geometry, node, parent);
            CreateMeshColliderObject(builder, BuildColliderMesh(aCurve.OffsetBy(-geometry.switchHome), ThreeFootGauge), "Collider-a", parent);
            CreateMeshColliderObject(builder, BuildColliderMesh(bCurve.OffsetBy(-geometry.switchHome), ThreeFootGauge), "Collider-b", parent);
            CreateRoadbed(builder, aRoadbedCurve, parent, TrackSegment.Style.Standard);
            CreateRoadbed(builder, bRoadbedCurve, parent, TrackSegment.Style.Standard);
        }

        private static void CreateSwitchRailObjects(
            TrackObjectBuilder builder,
            SwitchGeometry geometry,
            TrackNode node,
            Transform parent)
        {
            GameObject root = CreateTrackRoot(builder, "sw-" + node.id, parent);

            CreateMeshObject(builder, BuildFrogMesh(geometry.frogPoints, ThreeFootGauge), "Frog", root);
            CreateMeshObject(builder, BuildStockRailMesh(geometry.leftStockRail, geometry.switchHome, ThreeFootGauge, _ => 1f), "StockL", root);
            CreateMeshObject(builder, BuildStockRailMesh(geometry.rightStockRail, geometry.switchHome, ThreeFootGauge, _ => 1f), "StockR", root);
            CreateMeshObject(builder, BuildStockRailMesh(geometry.aClosureRail, geometry.switchHome, ThreeFootGauge, _ => 1f), "ClosureA", root);
            CreateMeshObject(builder, BuildStockRailMesh(geometry.bClosureRail, geometry.switchHome, ThreeFootGauge, _ => 1f), "ClosureB", root);
            CreateMeshObject(builder, BuildStockRailMesh(geometry.leftGuardRail, geometry.switchHome, ThreeFootGauge, _ => 1f), "GuardA", root);
            CreateMeshObject(builder, BuildStockRailMesh(geometry.rightGuardRail, geometry.switchHome, ThreeFootGauge, _ => 1f), "GuardB", root);

            GameObject normalPointRail = CreatePointRail(builder, geometry.aPointRail, "PointA", geometry, root);
            GameObject reversedPointRail = CreatePointRail(builder, geometry.bPointRail, "PointB", geometry, root);

            Vector3 pointAEnd = geometry.aPointRail.Points.Last().point;
            Vector3 pointBEnd = geometry.bPointRail.Points.Last().point;
            Vector3 pointAStart = geometry.aPointRail.Points.First().point;
            Vector3 pointBStart = geometry.bPointRail.Points.First().point;

            Vector3 normalLead = (pointAStart - pointBStart).normalized * 0.2f + pointAStart;
            Vector3 reversedLead = (pointBStart - pointAStart).normalized * 0.2f + pointBStart;

            float normalRot = Vector3.SignedAngle(
                normalLead - pointAEnd,
                pointAStart - pointAEnd,
                geometry.frogPoints[1].Rotation * Vector3.up);

            float reversedRot = Vector3.SignedAngle(
                reversedLead - pointBEnd,
                pointBStart - pointBEnd,
                geometry.frogPoints[1].Rotation * Vector3.up);

            root.AddComponent<SwitchPointRails>()
                .Configure(node, normalPointRail, reversedPointRail, normalRot, reversedRot);

            CreateSwitchStand(builder, geometry, node, root.transform);
            CreateSwitchTies(builder, geometry, root.transform, ThreeFootGauge);
        }

        private static GameObject CreatePointRail(
            TrackObjectBuilder builder,
            LineCurve pointRail,
            string objectName,
            SwitchGeometry geometry,
            GameObject root)
        {
            Vector3 point = pointRail.Points.Last().point;
            Mesh mesh = BuildStockRailMesh(
                ReprofilePointRail(pointRail).Offset(-point),
                geometry.switchHome,
                ThreeFootGauge,
                i => i == 0 ? 0.1f : 1f);

            GameObject rail = CreateMeshObject(builder, mesh, objectName, root);
            rail.transform.localPosition = point;
            return rail;
        }

        private static void CreateTrackObject(
            TrackObjectBuilder builder,
            BezierCurve curve,
            float tieSpacing,
            float tieSpacingJitter,
            string trackName,
            Transform parent,
            Gauge gauge)
        {
            Vector3 endPoint = curve.EndPoint1;
            BezierCurve localCurve = curve.OffsetBy(-endPoint);

            GameObject root = CreateTrackRoot(builder, trackName, parent);
            root.transform.localPosition = endPoint;

            SwitchGeometry.RailLineCurves railCurves = SwitchGeometry.MakeTrackLineSegments(localCurve, gauge);
            CreateMeshObject(builder, BuildStockRailMesh(railCurves.left, endPoint, gauge, _ => 1f), "L", root);
            CreateMeshObject(builder, BuildStockRailMesh(railCurves.right, endPoint, gauge, _ => 1f), "R", root);
            CreateSegmentTies(builder, localCurve, tieSpacing, tieSpacingJitter, root.transform, gauge);
            CreateMeshColliderObject(builder, BuildColliderMesh(localCurve, gauge), "Collider", root.transform);
        }

        private static void CreateSegmentTies(
            TrackObjectBuilder builder,
            BezierCurve curve,
            float spacing,
            float tieSpacingJitter,
            Transform parent,
            Gauge gauge)
        {
            var ties = new List<PointDirection>();
            var tiePlates = new List<PointDirection>();

            float jitter = tieSpacingJitter / 4f;
            LineCurve lineCurve = new LineCurve(curve.Approximate(1.000005f, 0.5f, 16, 40f), Hand.Left);
            float tieCount = Mathf.Round(lineCurve.Length / spacing);
            if (tieCount == 0f)
            {
                return;
            }

            spacing = lineCurve.Length / tieCount;
            var cursor = lineCurve.CursorAtHead().Skip(spacing / 2f);

            for (int i = 0; i < tieCount; i++)
            {
                LinePoint linePoint = cursor.LinePoint();
                Vector3 point = linePoint.point;
                Quaternion rotation = linePoint.Rotation;

                Vector3 tieCenter = point + rotation * Vector3.left * UnityEngine.Random.Range(-jitter, jitter);
                ties.Add(new PointDirection(tieCenter, rotation));

                float plateOffset = gauge.Inside / 2f + gauge.HeadWidth / 2f;
                tiePlates.Add(new PointDirection(point + rotation * Vector3.right * plateOffset, rotation));
                tiePlates.Add(new PointDirection(point + rotation * Vector3.left * plateOffset, rotation));

                cursor = cursor.Skip(spacing);
            }

            Quaternion tieRotationOffset = Quaternion.Euler(90f, 90f, 0f) * Quaternion.Euler(180f, 0f, 0f);
            Matrix4x4[] tieMatrices = new Matrix4x4[ties.Count];
            for (int i = 0; i < ties.Count; i++)
            {
                PointDirection pointDirection = ties[i];
                float variation = Mathf.PingPong(pointDirection.Position.magnitude, 0.01f);
                Vector3 drop = (-(gauge.RailHeight + 0.1f) + variation) * (pointDirection.Rotation * Vector3.up);
                tieMatrices[i] = Matrix4x4.TRS(pointDirection.Position + drop, pointDirection.Rotation * tieRotationOffset, Vector3.one);
            }

            CreateInstancedMeshDrawer(builder, tieMatrices, parent.localPosition, PrefabInstancer.Prefab.Tie, parent.gameObject);

            Quaternion tiePlateRotationOffset = Quaternion.Euler(-90f, 0f, 0f);
            Matrix4x4[] tiePlateMatrices = new Matrix4x4[tiePlates.Count];
            for (int i = 0; i < tiePlates.Count; i++)
            {
                PointDirection pointDirection = tiePlates[i];
                tiePlateMatrices[i] = Matrix4x4.TRS(pointDirection.Position, pointDirection.Rotation * tiePlateRotationOffset, Vector3.one);
            }

            CreateInstancedMeshDrawer(builder, tiePlateMatrices, parent.localPosition, PrefabInstancer.Prefab.TiePlate, parent.gameObject);
        }

        private static void CreateSwitchTies(
            TrackObjectBuilder builder,
            SwitchGeometry geometry,
            Transform parent,
            Gauge gauge)
        {
            LineCurve straight = new LineCurve(geometry.leftStockRail);
            LineCurve curved = new LineCurve(geometry.rightStockRail);

            float normalizedWidth = gauge.Inside + 1f;
            var ties = new List<Matrix4x4>();

            int index = 0;
            while (straight.Length >= 0.55f && curved.Length >= 0.55f)
            {
                LinePoint straightPoint = straight.Points.First();
                LinePoint curvedPoint = curved.Points.First();
                Vector3 center = Vector3.Lerp(straightPoint.point, curvedPoint.point, 0.5f);
                float zScale = ((straightPoint.point - curvedPoint.point).magnitude + 1f) / normalizedWidth;
                if (index != 0 && index != 1)
                {
                    ties.Add(CreateTieMatrix(center, straightPoint.direction, zScale, gauge));
                }

                straight = straight.Skip(0.55f, false);
                curved = curved.Skip(0.55f, false);
                index++;
            }

            float lateral = gauge.Inside / 2f;
            Vector3 straightEnd = straight.Points.Last().point;
            if (straight.Length < 0.82500005f && straight.Length > 0.18333334f)
            {
                straight = straight.Skip(straight.Length / 2f, false);
                LinePoint linePoint = straight.Points.First();
                Vector3 center = linePoint.point + Vector3.Cross(linePoint.point - straightEnd, Vector3.up).normalized * lateral;
                ties.Add(CreateTieMatrix(center, linePoint.direction, 1f, gauge));
            }

            while (straight.Length > 0f)
            {
                LinePoint linePoint = straight.Points.First();
                Vector3 center = linePoint.point + Vector3.Cross(linePoint.point - straightEnd, Vector3.up).normalized * lateral;
                ties.Add(CreateTieMatrix(center, linePoint.direction, 1f, gauge));
                straight = straight.Skip(0.55f, true);
            }

            Vector3 curvedEnd = curved.Points.Last().point;
            if (curved.Length < 0.82500005f && curved.Length > 0.18333334f)
            {
                curved = curved.Skip(curved.Length / 2f, false);
                LinePoint linePoint = curved.Points.First();
                Vector3 center = linePoint.point + Vector3.Cross(linePoint.point - curvedEnd, Vector3.down).normalized * lateral;
                ties.Add(CreateTieMatrix(center, linePoint.direction, 1f, gauge));
            }

            while (curved.Length > 0f)
            {
                LinePoint linePoint = curved.Points.First();
                Vector3 center = linePoint.point + Vector3.Cross(linePoint.point - curvedEnd, Vector3.down).normalized * lateral;
                ties.Add(CreateTieMatrix(center, linePoint.direction, 1f, gauge));
                curved = curved.Skip(0.55f, true);
            }

            CreateInstancedMeshDrawer(builder, ties.ToArray(), geometry.switchHome, PrefabInstancer.Prefab.Tie, parent.gameObject);
        }

        internal static Matrix4x4 CreateTieMatrix(Vector3 point, Vector3 direction, float zScale, Gauge gauge)
        {
            float variation = Mathf.PingPong(point.magnitude, 0.01f);
            Vector3 position = point + new Vector3(0f, -(gauge.RailHeight + 0.1f) + variation, 0f);
            Quaternion rotation =
                Quaternion.LookRotation(direction) *
                Quaternion.Euler(90f, 90f, 0f) *
                Quaternion.Euler(180f, 0f, 0f);

            return Matrix4x4.TRS(position, rotation, new Vector3(1f, zScale, 1f));
        }

        private static LineCurve ReprofilePointRail(LineCurve curve)
        {
            LineCurve trimmed = curve.Skip(0.2f, false);
            LinePoint head = trimmed.Points.First();
            LineCurve reprofiled = trimmed.Skip(4f, false);
            reprofiled.Insert(0, head);
            return reprofiled;
        }

        private static MixedPointProfileTemplate CreateMixedPointProfileTemplate(
            LineCurve referenceCurve,
            float tipScale,
            float bodyScale)
        {
            if (referenceCurve.Length <= 0.3f)
            {
                return new MixedPointProfileTemplate(0f, 0f, 2, tipScale, bodyScale);
            }

            float tipTrim = Mathf.Min(0.12f, Mathf.Max(referenceCurve.Length - 0.05f, 0.01f));
            LineCurve trimmed = referenceCurve.Skip(tipTrim, false);
            int taperPoints = Mathf.Clamp(trimmed.Points.Count() / 2, 2, 8);
            float taperLength = Mathf.Min(
                Mathf.Max(trimmed.Length * 0.6f, 0.35f),
                Mathf.Max(trimmed.Length - 0.05f, 0.05f));

            return new MixedPointProfileTemplate(tipTrim, taperLength, taperPoints, tipScale, bodyScale);
        }

        private static LineCurve ReprofileMixedPointRail(
            LineCurve curve,
            float tipTrimOverride,
            float taperLengthOverride)
        {
            if (curve.Length <= 0.3f)
            {
                return curve;
            }

            float tipTrim = tipTrimOverride > 0f
                ? Mathf.Min(tipTrimOverride, Mathf.Max(curve.Length - 0.05f, 0.01f))
                : Mathf.Min(0.12f, Mathf.Max(curve.Length - 0.05f, 0.01f));
            LineCurve trimmed = curve.Skip(tipTrim, false);
            if (!trimmed.Points.Any())
            {
                return curve;
            }

            LinePoint head = trimmed.Points.First();
            float taperLength = taperLengthOverride > 0f
                ? Mathf.Min(taperLengthOverride, Mathf.Max(trimmed.Length - 0.05f, 0.05f))
                : Mathf.Min(
                    Mathf.Max(trimmed.Length * 0.6f, 0.35f),
                    Mathf.Max(trimmed.Length - 0.05f, 0.05f));

            LineCurve reprofiled = trimmed.Skip(taperLength, false);
            if (!reprofiled.Points.Any())
            {
                return trimmed;
            }

            reprofiled.Insert(0, head);
            return reprofiled;
        }

        private static bool IsNarrowBumper(TrackNode node)
        {
            return node != null
                && Graph.Shared != null
                && Graph.Shared.SegmentsConnectedTo(node).Any(NarrowGaugeManager.IsNarrowGauge);
        }

        private static void WarnMixedGaugeSwitch(TrackNode node)
        {
            if (node == null || !WarnedMixedGaugeSwitches.Add(node.id ?? string.Empty))
            {
                return;
            }

            Main.Warn($"[Build] Switch '{node.id}' connects mixed gauge segments; leaving its visuals standard.");
        }

        internal static GameObject CreateGeneratedObjectContainer(TrackObjectBuilder builder)
        {
            return (GameObject)CreateGeneratedObjectContainerMethod.Invoke(builder, null);
        }

        internal static GameObject CreateTrackRoot(TrackObjectBuilder builder, string name, Transform parent)
        {
            var gameObject = new GameObject
            {
                hideFlags = (HideFlags)MeshHideFlagsField.GetValue(builder),
                layer = (int)TrackLayerField.GetValue(builder),
                name = name,
                tag = TrackObjectBuilder.TagGenerated
            };

            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        internal static GameObject CreateMeshObject(TrackObjectBuilder builder, Mesh mesh, string objectName, GameObject parent)
        {
            return (GameObject)CreateMeshObjectMethod.Invoke(builder, new object[] { mesh, objectName, parent });
        }

        internal static void CreateMeshColliderObject(TrackObjectBuilder builder, Mesh mesh, string objectName, Transform parent)
        {
            CreateMeshColliderObjectMethod.Invoke(builder, new object[] { mesh, objectName, parent });
        }

        internal static void CreateRoadbed(TrackObjectBuilder builder, BezierCurve curve, Transform parent, TrackSegment.Style style)
        {
            CreateRoadbedMethod.Invoke(builder, new object[] { curve, parent, style });
        }

        private static void CreateBumperModel(TrackObjectBuilder builder, TrackNode node, Vector3 direction, Transform parent)
        {
            CreateBumperModelMethod.Invoke(builder, new object[] { node, direction, parent });
        }

        private static void CreateSwitchStand(TrackObjectBuilder builder, SwitchGeometry geometry, TrackNode node, Transform parent)
        {
            CreateSwitchStandMethod.Invoke(builder, new object[] { geometry, node, parent });
        }

        internal static void CreateInstancedMeshDrawer(
            TrackObjectBuilder builder,
            Matrix4x4[] transforms,
            Vector3 offset,
            PrefabInstancer.Prefab prefab,
            GameObject parent)
        {
            CreateInstancedMeshDrawerMethod.Invoke(builder, new object[] { transforms, offset, prefab, parent });
        }

        private static Mesh BuildFrogMesh(LinePoint[] points, Gauge gauge)
        {
            return (Mesh)BuildFrogMeshMethod.Invoke(null, new object[] { points, gauge });
        }

        internal static Mesh BuildStockRailMesh(LineCurve curve, Vector3 switchHome, Gauge gauge, Func<int, float> profileScale)
        {
            return (Mesh)BuildStockRailMeshMethod.Invoke(null, new object[] { curve, switchHome, gauge, profileScale });
        }

        internal static Mesh BuildColliderMesh(BezierCurve curve, Gauge gauge)
        {
            return (Mesh)BuildColliderMeshMethod.Invoke(null, new object[] { curve, gauge });
        }

        private static T GetFieldValue<T>(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null)
            {
                throw new MissingFieldException(instance.GetType().FullName, fieldName);
            }

            object value = field.GetValue(instance);
            if (value == null)
            {
                return default!;
            }

            return (T)value;
        }

        private readonly struct PointDirection
        {
            public PointDirection(Vector3 position, Quaternion rotation)
            {
                Position = position;
                Rotation = rotation;
            }

            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
        }

        private readonly struct MixedPointProfileTemplate
        {
            public MixedPointProfileTemplate(
                float tipTrim,
                float taperLength,
                int taperPoints,
                float tipScale,
                float bodyScale)
            {
                TipTrim = tipTrim;
                TaperLength = taperLength;
                TaperPoints = taperPoints;
                TipScale = tipScale;
                BodyScale = bodyScale;
            }

            public float TipTrim { get; }
            public float TaperLength { get; }
            public int TaperPoints { get; }
            public float TipScale { get; }
            public float BodyScale { get; }
        }
    }
}
