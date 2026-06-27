using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Core;
using FUSE.Runtime.API;
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

        // Dual gauge: standard (1.435m) + narrow (0.9144m) sharing either
        // standard rail. The middle rail sits 0.1969m from center.
        // ThirdRailGauge.Inside = 0.3938m so MakeTrackLineSegments gives its left
        // curve at exactly -0.1969m (the narrow inner/middle rail).
        internal static readonly float ThirdRailGaugeInside = 2f * 0.9144f - Gauge.Standard.Inside;
        private static readonly Gauge ThirdRailGauge =
            (Gauge)GaugeConstructor.Invoke(new object[] {
                2f * 0.9144f - Gauge.Standard.Inside,   // 0.3938m
                Gauge.Standard.HeadWidth,
                Gauge.Standard.RailHeight });

        private static readonly float BumperScaleX = ThreeFootGauge.Inside / Gauge.Standard.Inside;
        private const float NarrowOnlyTieLength = 6.75f * 0.3048f;
        private const float NarrowOnlyTieSpacing = 26f * 0.0254f;
        private const float NarrowOnlyTieSpacingJitter = 0.06f;
        private static readonly float NarrowOnlyTieLengthScale = NarrowOnlyTieLength / (Gauge.Standard.Inside + 1f);
        private const float DuplicateRailTolerance = 0.055f;
        private const float DuplicateRailSampleSpacing = 0.1f;
        private const float GaugeSeparationFrogMatchTolerance = 0.06f;
        private const float SharedRailFlipMinSpan = 5.0f;
        private const float SharedRailFlipMaxSpan = 7.5f;
        private const float SharedRailFlipMatchTolerance = 0.08f;
        private const float SharedRailTransitionMinSegmentLength = 12.0f;
        private const float SharedRailTransitionMaxSegmentLength = 24.0f;
        private const float SharedRailTransitionLead = 2.5f;
        private const float SharedRailTransitionFlangeway = 0.05f;
        private const float SharedRailTransitionTrimExtension = 2.0f;
        private const float SharedRailTransitionPointTaperLength = 1.0f;
        private const float SharedRailTransitionGuardHalfLength = 2.5f;
        private const float SharedRailTransitionGuardTransitionLength = 5.0f;
        private const float SharedRailTransitionOpposingGuardLength = 3.5f;
        private const float SharedRailTransitionOpposingGuardBackExtension = 0.5f;
        private const float SharedRailTransitionGuardEndFlareLength = 0.35f;
        private const float SharedRailTransitionGuardEndFlareAngle = 10f;

        private sealed class GaugeSeparationRailLayout
        {
            public GaugeSeparationRailLayout(
                TrackNode sourceNode,
                TrackSegment dualSegment,
                TrackSegment standardSegment,
                TrackSegment narrowSegment,
                LineCurve dualLeft,
                LineCurve dualMiddle,
                LineCurve dualRight,
                LineCurve standardLeft,
                LineCurve standardRight,
                LineCurve narrowLeft,
                LineCurve narrowRight)
            {
                SourceNode = sourceNode;
                DualSegment = dualSegment;
                StandardSegment = standardSegment;
                NarrowSegment = narrowSegment;
                DualLeft = dualLeft;
                DualMiddle = dualMiddle;
                DualRight = dualRight;
                StandardLeft = standardLeft;
                StandardRight = standardRight;
                NarrowLeft = narrowLeft;
                NarrowRight = narrowRight;
            }

            public TrackNode SourceNode { get; }
            public TrackSegment DualSegment { get; }
            public TrackSegment StandardSegment { get; }
            public TrackSegment NarrowSegment { get; }
            public LineCurve DualLeft { get; }
            public LineCurve DualMiddle { get; }
            public LineCurve DualRight { get; }
            public LineCurve StandardLeft { get; }
            public LineCurve StandardRight { get; }
            public LineCurve NarrowLeft { get; }
            public LineCurve NarrowRight { get; }
            public IReadOnlyList<LineCurve> DualRails =>
                new[] { DualLeft, DualMiddle, DualRight };
            public IReadOnlyList<LineCurve> StandardRails =>
                new[] { StandardLeft, StandardRight };
            public IReadOnlyList<LineCurve> NarrowRails =>
                new[] { NarrowLeft, NarrowRight };
        }

        internal sealed class GaugeSeparationFrogSite
        {
            public GaugeSeparationFrogSite(
                LineCurve railA,
                RailSide sideA,
                LineCurve railB,
                RailSide sideB,
                LinePoint intersection,
                bool isVee)
            {
                RailA = railA;
                SideA = sideA;
                RailB = railB;
                SideB = sideB;
                Intersection = intersection;
                IsVee = isVee;
                CutHalfLength = SpecialWorkHardwareRenderer.CalculateProceduralFrogCutHalfLength(
                    railA,
                    railB,
                    intersection);
            }

            public LineCurve RailA { get; }
            public RailSide SideA { get; }
            public LineCurve RailB { get; }
            public RailSide SideB { get; }
            public LinePoint Intersection { get; }
            public bool IsVee { get; }
            public float CutHalfLength { get; }
        }

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
        private static readonly HashSet<string> WarnedSourceNodeNarrowBranchSwitches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

                if (IsGeneratedGhostDescriptor(descriptor))
                {
                    result = BuildHiddenDescriptorObject(builder, descriptor);
                    return true;
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
                            if (!SpecialWorkHardwareRenderer.HasValidPlanForSegment(segment.Segment))
                            {
                                return false;
                            }

                            result = BuildSegment(builder, segment, Gauge.Standard);
                            return result != null;
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
                        bool aStandardOnly = !aNarrow && !aDual;
                        bool bStandardOnly = !bNarrow && !bDual;
                        bool hasMeasuredSpecialWork =
                            SpecialWorkHardwareRenderer.HasValidPlan(node);

                        if (!aNarrow && !bNarrow && !aDual && !bDual)
                        {
                            return false;
                        }

                        SwitchGeometry geometry = GetFieldValue<SwitchGeometry>(descriptor, "geometry");
                        BezierCurve aRoadbedCurve = GetFieldValue<BezierCurve>(descriptor, "aRoadbedCurve");
                        BezierCurve bRoadbedCurve = GetFieldValue<BezierCurve>(descriptor, "bRoadbedCurve");

                        if (!hasMeasuredSpecialWork
                            && IsInvalidSourceNodeNarrowBranchSwitch(node, out TrackSegment narrowBranch))
                        {
                            WarnSourceNodeNarrowBranchSwitch(node, narrowBranch);
                            return false;
                        }

                        if ((aDual && bDual)
                            || (aDual && bNarrow)
                            || (aNarrow && bDual)
                            || hasMeasuredSpecialWork
                            && ((aDual && bStandardOnly)
                                || (aStandardOnly && bDual)))
                        {
                            result = BuildDualGaugeSwitch(
                                builder,
                                node,
                                aProxy,
                                bProxy,
                                geometry,
                                aRoadbedCurve,
                                bRoadbedCurve,
                                descriptor.Identifier);
                            return result != null;
                        }

                        if (!(aNarrow && bNarrow))
                        {
                            WarnMixedGaugeSwitch(node);
                            return false;
                        }

                        result = BuildSwitch(
                            builder,
                            node,
                            aProxy,
                            bProxy,
                            geometry,
                            aRoadbedCurve,
                            bRoadbedCurve,
                            descriptor.Identifier);
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

        public static bool TryBuildMask(
            TrackObjectManager manager,
            TrackObjectManager.ITrackDescriptor descriptor,
            out GameObject result)
        {
            result = null!;

            try
            {
                if (manager == null
                    || descriptor == null
                    || !IsGeneratedGhostDescriptor(descriptor)
                    || !(BuilderField.GetValue(manager) is TrackObjectBuilder builder))
                {
                    return false;
                }

                result = BuildHiddenDescriptorObject(builder, descriptor);
                return true;
            }
            catch (Exception ex)
            {
                Main.Error($"[Build] Ghost mask suppression failed: {ex}");
                result = null!;
                return false;
            }
        }

        private static bool IsGeneratedGhostDescriptor(TrackObjectManager.ITrackDescriptor descriptor)
        {
            switch (descriptor.GetType().Name)
            {
                case "SegmentDescriptor":
                {
                    TrackSegment segment = GetFieldValue<SegmentProxy>(descriptor, "segment").Segment;
                    return NarrowGaugeManager.IsGeneratedGhost(segment)
                        || SpecialWorkTopologySynchronizer.IsHiddenControlSegment(segment);
                }

                case "SwitchDescriptor":
                {
                    TrackNode node = GetFieldValue<TrackNode>(descriptor, "node");
                    return NarrowGaugeManager.IsGeneratedGhostNode(node)
                        && !IsVisibleGeneratedTransitionSwitch(node)
                        && !IsRenderableGaugeSeparationControlSwitch(node);
                }

                case "BumperDescriptor":
                {
                    TrackNode node = GetFieldValue<TrackNode>(descriptor, "node");
                    return NarrowGaugeManager.IsGeneratedGhostNode(node)
                        || SpecialWorkTopologySynchronizer.IsDualToNarrowContinuationSourceNode(node)
                        || IsHiddenControlEndNode(node);
                }

                default:
                    return false;
            }
        }

        private static bool IsVisibleGeneratedTransitionSwitch(TrackNode node)
        {
            if (!NarrowGaugeManager.IsGeneratedGhostNode(node) || Graph.Shared == null)
            {
                return false;
            }

            TrackSegment[] connected = Graph.Shared.SegmentsConnectedTo(node)
                .Where(segment => segment != null)
                .ToArray();
            return connected.Length == 3
                && connected.Count(SpecialWorkTopologySynchronizer.IsHiddenControlSegment) == 0
                && connected.Count(IsVisibleRuntimeGeneratedGhostSegment) == 2
                && connected.Count(segment =>
                    NarrowGaugeManager.IsNarrowGauge(segment)
                    && !IsRuntimeGeneratedGhostSegment(segment)
                    && !NarrowGaugeManager.IsDualGauge(segment)) == 1;
        }

        private static bool IsRenderableGaugeSeparationControlSwitch(TrackNode node)
        {
            return IsGaugeSeparationControlSwitch(node);
        }

        private static bool IsGaugeSeparationControlSwitch(TrackNode node)
        {
            if (!NarrowGaugeManager.IsGeneratedGhostNode(node) || Graph.Shared == null)
            {
                return false;
            }

            TrackSegment[] connected = Graph.Shared.SegmentsConnectedTo(node)
                .Where(segment => segment != null)
                .ToArray();
            return connected.Length == 3
                && connected.Count(SpecialWorkTopologySynchronizer.IsHiddenControlSegment) == 1
                && connected.Count(IsVisibleRuntimeGeneratedGhostSegment) == 1
                && connected.Count(IsVisibleRealNarrowOnlySegment) == 1;
        }

        private static bool IsRuntimeGeneratedGhostSegment(TrackSegment segment)
        {
            return segment != null
                && GhostGraphSynchronizer.IsGeneratedGhostSegmentId(segment.id);
        }

        private static bool IsVisibleRuntimeGeneratedGhostSegment(TrackSegment segment)
        {
            return IsRuntimeGeneratedGhostSegment(segment)
                && !SpecialWorkTopologySynchronizer.IsHiddenControlSegment(segment);
        }

        private static bool IsVisibleRealNarrowOnlySegment(TrackSegment segment)
        {
            if (segment == null
                || IsRuntimeGeneratedGhostSegment(segment)
                || SpecialWorkTopologySynchronizer.IsHiddenControlSegment(segment))
            {
                return false;
            }

            var definition = TrackAPI.GetSegmentDefinition(segment.id);
            return GhostGraphSynchronizer.IsNarrowGaugeDefinition(definition)
                && !GhostGraphSynchronizer.IsDualGaugeDefinition(definition);
        }

        private static bool IsHiddenControlEndNode(TrackNode node)
        {
            if (node == null || Graph.Shared == null)
            {
                return false;
            }

            TrackSegment[] connected = Graph.Shared.SegmentsConnectedTo(node)
                .Where(segment => segment != null)
                .ToArray();
            return connected.Length == 1
                && SpecialWorkTopologySynchronizer.IsHiddenControlSegment(connected[0]);
        }

        private static bool IsThreeFootGauge(Gauge gauge)
        {
            return Mathf.Abs(gauge.Inside - ThreeFootGauge.Inside) <= 0.0005f;
        }

        private static GameObject BuildHiddenDescriptorObject(
            TrackObjectBuilder builder,
            TrackObjectManager.ITrackDescriptor descriptor)
        {
            GameObject container = CreateGeneratedObjectContainer(builder);
            container.name = "hidden-" + descriptor.Identifier;
            container.SetActive(true);
            Main.Log($"[Build] Suppressed hidden track descriptor '{descriptor.Identifier}'.");
            return container;
        }

        private static GameObject BuildSegment(
            TrackObjectBuilder builder,
            SegmentProxy segment,
            Gauge gauge)
        {
            GameObject container = CreateGeneratedObjectContainer(builder);

            bool narrowOnly = IsThreeFootGauge(gauge);
            float baseTieSpacing = narrowOnly ? NarrowOnlyTieSpacing : 0.55f;
            float tieSpacing = baseTieSpacing * (segment.Segment.style == TrackSegment.Style.Bridge ? 0.6f : 1f);
            float tieSpacingJitter = segment.Segment.style == TrackSegment.Style.Bridge
                ? 0.02f
                : narrowOnly ? NarrowOnlyTieSpacingJitter : 0.08f;
            float tieLengthScale = narrowOnly ? NarrowOnlyTieLengthScale : 1f;

            CreateTrackObject(
                builder,
                segment.Curve,
                tieSpacing,
                tieSpacingJitter,
                "seg-" + segment.Segment.id,
                container.transform,
                gauge,
                segment.Segment,
                tieLengthScale);

            CreateRoadbed(builder, segment.Curve, container.transform, segment.Segment.style);
            container.SetActive(true);
            return container;
        }

        private static GameObject BuildDualGaugeSegment(
            TrackObjectBuilder builder,
            SegmentProxy segment)
        {
            if (DualGaugeSharedRailRegistry.IsSharedRailTransition(segment.Segment))
            {
                return BuildSharedRailTransitionSegment(builder, segment);
            }

            GameObject container = CreateGeneratedObjectContainer(builder);

            float tieSpacing = 0.55f * (segment.Segment.style == TrackSegment.Style.Bridge ? 0.6f : 1f);
            float tieSpacingJitter = segment.Segment.style == TrackSegment.Style.Bridge ? 0.02f : 0.08f;

            CreateDualGaugeTrackObject(
                builder,
                segment.Curve,
                tieSpacing,
                tieSpacingJitter,
                "seg-" + segment.Segment.id,
                container.transform,
                DualGaugeSharedRailRegistry.SharesRightRail(segment.Segment),
                segment.Segment);

            CreateSharedRailFlipTransitions(builder, segment.Segment, container.transform);
            CreateRoadbed(builder, segment.Curve, container.transform, segment.Segment.style);
            container.SetActive(true);
            return container;
        }

        private static GameObject BuildSharedRailTransitionSegment(
            TrackObjectBuilder builder,
            SegmentProxy segment)
        {
            GameObject container = CreateGeneratedObjectContainer(builder);
            float tieSpacing = 0.55f * (segment.Segment.style == TrackSegment.Style.Bridge ? 0.6f : 1f);
            float tieSpacingJitter = segment.Segment.style == TrackSegment.Style.Bridge ? 0.02f : 0.08f;
            float transitionLength = ApproximateCurveLength(segment.Curve);

            if (transitionLength < SharedRailTransitionMinSegmentLength)
            {
                Main.Warn(
                    $"[SharedRailTransition] Segment '{segment.Segment.id}' is {transitionLength:0.000}m, " +
                    $"below the {SharedRailTransitionMinSegmentLength:0.000}m minimum; " +
                    "rendering a normal dual-gauge fallback.");
                CreateDualGaugeTrackObject(
                    builder,
                    segment.Curve,
                    tieSpacing,
                    tieSpacingJitter,
                    "seg-" + segment.Segment.id,
                    container.transform,
                    DualGaugeSharedRailRegistry.SharesRightRail(segment.Segment),
                    segment.Segment);
            }
            else if (!TryResolveSharedRailTransitionNeighbors(
                    segment.Segment,
                    out TrackSegment aNeighbor,
                    out TrackSegment bNeighbor)
                || !TryGetDualRailsFromNode(
                    aNeighbor,
                    segment.Segment.a,
                    out LineCurve aLeft,
                    out LineCurve aMiddle,
                    out LineCurve aRight)
                || !TryGetDualRailsFromNode(
                    bNeighbor,
                    segment.Segment.b,
                    out LineCurve bLeft,
                    out LineCurve bMiddle,
                    out LineCurve bRight))
            {
                Main.Warn(
                    $"[SharedRailTransition] Segment '{segment.Segment.id}' must be a degree-two " +
                    "DualGauge_T segment between one DualGauge_L and one DualGauge_R segment; " +
                    "rendering a normal dual-gauge fallback.");
                CreateDualGaugeTrackObject(
                    builder,
                    segment.Curve,
                    tieSpacing,
                    tieSpacingJitter,
                    "seg-" + segment.Segment.id,
                    container.transform,
                    DualGaugeSharedRailRegistry.SharesRightRail(segment.Segment),
                    segment.Segment);
            }
            else
            {
                if (transitionLength > SharedRailTransitionMaxSegmentLength)
                {
                    Main.Warn(
                        $"[SharedRailTransition] Segment '{segment.Segment.id}' is {transitionLength:0.000}m, " +
                        $"above the {SharedRailTransitionMaxSegmentLength:0.000}m target maximum; " +
                        "rendering it, but procedural generation should clamp future pieces.");
                }

                CreateSharedRailTransitionTrackObject(
                    builder,
                    segment,
                    tieSpacing,
                    tieSpacingJitter,
                    aNeighbor,
                    aLeft,
                    aMiddle,
                    aRight,
                    bNeighbor,
                    bLeft,
                    bMiddle,
                    bRight,
                    container.transform);
            }

            CreateRoadbed(builder, segment.Curve, container.transform, segment.Segment.style);
            container.SetActive(true);
            return container;
        }

        private static float ApproximateCurveLength(BezierCurve curve)
        {
            return new LineCurve(curve.Approximate(1.000005f, 0.5f, 16, 40f), Hand.Left).Length;
        }

        private static void CreateSharedRailTransitionTrackObject(
            TrackObjectBuilder builder,
            SegmentProxy transition,
            float tieSpacing,
            float tieSpacingJitter,
            TrackSegment aNeighbor,
            LineCurve aLeft,
            LineCurve aMiddle,
            LineCurve aRight,
            TrackSegment bNeighbor,
            LineCurve bLeft,
            LineCurve bMiddle,
            LineCurve bRight,
            Transform parent)
        {
            GameObject root = CreateTrackRoot(
                builder,
                "shared-rail-transition-" + transition.Segment.id,
                parent);
            SwitchGeometry.RailLineCurves standard =
                SwitchGeometry.MakeTrackLineSegments(transition.Curve, Gauge.Standard);
            CreateRailMeshesWithFrogCuts(
                builder,
                standard.left,
                Vector3.zero,
                transition.Segment,
                Gauge.Standard,
                "DualL",
                root);
            CreateRailMeshesWithFrogCuts(
                builder,
                standard.right,
                Vector3.zero,
                transition.Segment,
                Gauge.Standard,
                "DualR",
                root);

            bool aSharesRight = DualGaugeSharedRailRegistry.SharesRightRail(aNeighbor);
            bool bSharesRight = DualGaugeSharedRailRegistry.SharesRightRail(bNeighbor);
            var aNarrow = new[]
            {
                (Rail: aSharesRight ? aMiddle : aLeft, Outer: !aSharesRight),
                (Rail: aSharesRight ? aRight : aMiddle, Outer: aSharesRight)
            };
            var bNarrow = new[]
            {
                (Rail: bSharesRight ? bMiddle : bLeft, Outer: !bSharesRight),
                (Rail: bSharesRight ? bRight : bMiddle, Outer: bSharesRight)
            };
            float directPairing =
                Vector3.Distance(aNarrow[0].Rail.Head.point, bNarrow[0].Rail.Head.point)
                + Vector3.Distance(aNarrow[1].Rail.Head.point, bNarrow[1].Rail.Head.point);
            float crossedPairing =
                Vector3.Distance(aNarrow[0].Rail.Head.point, bNarrow[1].Rail.Head.point)
                + Vector3.Distance(aNarrow[1].Rail.Head.point, bNarrow[0].Rail.Head.point);
            bool usesCrossedPairing = crossedPairing < directPairing;
            if (usesCrossedPairing)
            {
                (bNarrow[0], bNarrow[1]) = (bNarrow[1], bNarrow[0]);
            }

            LineCurve aStandardLeft = OrientCurveAwayFromPoint(
                standard.left,
                transition.Segment.a.transform.localPosition);
            LineCurve aStandardRight = OrientCurveAwayFromPoint(
                standard.right,
                transition.Segment.a.transform.localPosition);
            LineCurve bStandardLeft = OrientCurveAwayFromPoint(
                standard.left,
                transition.Segment.b.transform.localPosition);
            LineCurve bStandardRight = OrientCurveAwayFromPoint(
                standard.right,
                transition.Segment.b.transform.localPosition);
            LineCurve aSharedFromNode = CloserRail(
                aStandardLeft,
                aStandardRight,
                aNarrow.First(item => item.Outer).Rail.Head.point);
            LineCurve aOppositeStandardFromNode = FartherRail(
                aStandardLeft,
                aStandardRight,
                aNarrow.First(item => item.Outer).Rail.Head.point);
            LineCurve bSharedFromNode = CloserRail(
                bStandardLeft,
                bStandardRight,
                bNarrow.First(item => item.Outer).Rail.Head.point);
            LineCurve bOppositeStandardFromNode = FartherRail(
                bStandardLeft,
                bStandardRight,
                bNarrow.First(item => item.Outer).Rail.Head.point);

            LineCurve transitionAStart = aNarrow[0].Outer ? aSharedFromNode : aNarrow[0].Rail;
            LineCurve transitionAEnd = bNarrow[0].Outer ? bSharedFromNode : bNarrow[0].Rail;
            float transitionAStartDistance = aNarrow[0].Outer ? SharedRailTransitionLead : 0f;
            float transitionAEndDistance = bNarrow[0].Outer ? SharedRailTransitionLead : 0f;
            LineCurve transitionA = CreateSharedRailConnectionCurve(
                transitionAStart,
                transitionAEnd,
                transitionAStartDistance,
                transitionAEndDistance,
                aNarrow[0].Outer,
                bNarrow[0].Outer);
            LineCurve transitionBStart = aNarrow[1].Outer ? aSharedFromNode : aNarrow[1].Rail;
            LineCurve transitionBEnd = bNarrow[1].Outer ? bSharedFromNode : bNarrow[1].Rail;
            float transitionBStartDistance = aNarrow[1].Outer ? SharedRailTransitionLead : 0f;
            float transitionBEndDistance = bNarrow[1].Outer ? SharedRailTransitionLead : 0f;
            LineCurve transitionB = CreateSharedRailConnectionCurve(
                transitionBStart,
                transitionBEnd,
                transitionBStartDistance,
                transitionBEndDistance,
                aNarrow[1].Outer,
                bNarrow[1].Outer);
            LineCurve transitionAVisual = AlignSharedRailTransitionVisuals(
                transitionA,
                transitionAStart,
                transitionAEnd,
                transitionAStartDistance,
                transitionAEndDistance);
            LineCurve transitionBVisual = AlignSharedRailTransitionVisuals(
                transitionB,
                transitionBStart,
                transitionBEnd,
                transitionBStartDistance,
                transitionBEndDistance);
            CreateSymmetricStraightSharedRailTransitionVisuals(
                transitionAVisual,
                transitionBVisual,
                aNarrow[0].Outer ? aSharedFromNode : null,
                bNarrow[0].Outer ? bSharedFromNode : null,
                aNarrow[1].Outer ? aSharedFromNode : null,
                bNarrow[1].Outer ? bSharedFromNode : null,
                trimSharedFlangeways: true,
                out transitionAVisual,
                out transitionBVisual);
            CreateSharedRailTransitionRail(
                builder,
                root,
                transitionAVisual,
                aNarrow[0].Outer ? aSharedFromNode : null,
                aNarrow[0].Outer ? aOppositeStandardFromNode : null,
                bNarrow[0].Outer ? bSharedFromNode : null,
                bNarrow[0].Outer ? bOppositeStandardFromNode : null,
                "NarrowA");
            CreateSharedRailTransitionRail(
                builder,
                root,
                transitionBVisual,
                aNarrow[1].Outer ? aSharedFromNode : null,
                aNarrow[1].Outer ? aOppositeStandardFromNode : null,
                bNarrow[1].Outer ? bSharedFromNode : null,
                bNarrow[1].Outer ? bOppositeStandardFromNode : null,
                "NarrowB");
            LineCurve aNeighborSharedFromNode = aNarrow.First(item => item.Outer).Rail;
            LineCurve bNeighborSharedFromNode = bNarrow.First(item => item.Outer).Rail;
            LineCurve aPrimaryTransition = aNarrow[0].Outer
                ? transitionAVisual
                : transitionBVisual;
            LineCurve aOpposingTransition = aNarrow[0].Outer
                ? transitionBVisual
                : transitionAVisual;
            LineCurve bPrimaryTransition = bNarrow[0].Outer
                ? transitionAVisual.Reverse()
                : transitionBVisual.Reverse();
            LineCurve bOpposingTransition = bNarrow[0].Outer
                ? transitionBVisual.Reverse()
                : transitionAVisual.Reverse();
            int guardCount = 0;
            if (CreateSharedRailTransitionGuard(
                builder,
                root,
                aNeighborSharedFromNode,
                FartherRail(aLeft, aRight, aNeighborSharedFromNode.Head.point),
                aSharedFromNode,
                aSharedFromNode,
                transitionGuideAwayFromToward: true,
                includeStandardHalf: true,
                transitionFromFrog: aPrimaryTransition,
                opposingTransitionFromNode: aOpposingTransition,
                name: "GuardA"))
            {
                guardCount++;
            }
            if (CreateSharedRailOpposingTransitionGuard(
                builder,
                root,
                aPrimaryTransition,
                aOpposingTransition,
                "GuardA-Opposing"))
            {
                guardCount++;
            }
            if (CreateSharedRailTransitionGuard(
                builder,
                root,
                bNeighborSharedFromNode,
                FartherRail(bLeft, bRight, bNeighborSharedFromNode.Head.point),
                bSharedFromNode,
                bSharedFromNode,
                transitionGuideAwayFromToward: true,
                includeStandardHalf: true,
                transitionFromFrog: bPrimaryTransition,
                opposingTransitionFromNode: bOpposingTransition,
                name: "GuardB"))
            {
                guardCount++;
            }
            if (CreateSharedRailOpposingTransitionGuard(
                builder,
                root,
                bPrimaryTransition,
                bOpposingTransition,
                "GuardB-Opposing"))
            {
                guardCount++;
            }
            Vector3 transitionADirection =
                transitionAVisual.Tail.point - transitionAVisual.Head.point;
            Vector3 transitionBDirection =
                transitionBVisual.Tail.point - transitionBVisual.Head.point;
            float transitionAcuteAngle = Mathf.Min(
                Vector3.Angle(transitionADirection, transitionBDirection),
                180f - Vector3.Angle(transitionADirection, transitionBDirection));

            CreateDualGaugeSegmentTies(
                builder,
                transition.Curve,
                tieSpacing,
                tieSpacingJitter,
                root.transform,
                aSharesRight,
                transition.Segment);
            CreateMeshColliderObject(
                builder,
                BuildColliderMesh(transition.Curve, Gauge.Standard),
                "Collider",
                root.transform);
            Main.Log(
                $"[SharedRailTransition] Rendered catalog preset " +
                $"'{SpecialWorkPresetIds.DualSharedRailFlip}' for '{transition.Segment.id}' between " +
                $"'{aNeighbor.id}' and '{bNeighbor.id}' with taperedFrogPoints=2, " +
                $"aCurveSharesRight={aSharesRight}, bCurveSharesRight={bSharesRight}, " +
                $"transitionOffsets=" +
                $"{DualGaugeSharedRailRegistry.GetAtoBNarrowCenterOffsetAtNode(transition.Segment, transition.Segment.a):0.000}/" +
                $"{DualGaugeSharedRailRegistry.GetAtoBNarrowCenterOffsetAtNode(transition.Segment, transition.Segment.b):0.000}, " +
                $"pairing={(usesCrossedPairing ? "crossed" : "direct")}, " +
                $"visualLengths={transitionAVisual.Length:0.000}/{transitionBVisual.Length:0.000}, " +
                $"crossingAngle={transitionAcuteAngle:0.000}, guards={guardCount}.");
        }

        private static LineCurve CloserRail(
            LineCurve first,
            LineCurve second,
            Vector3 point)
        {
            return Vector3.Distance(first.Head.point, point)
                <= Vector3.Distance(second.Head.point, point)
                    ? first
                    : second;
        }

        private static LineCurve FartherRail(
            LineCurve first,
            LineCurve second,
            Vector3 point)
        {
            return Vector3.Distance(first.Head.point, point)
                >= Vector3.Distance(second.Head.point, point)
                    ? first
                    : second;
        }

        private static bool TryResolveSharedRailTransitionNeighbors(
            TrackSegment transition,
            out TrackSegment aNeighbor,
            out TrackSegment bNeighbor)
        {
            aNeighbor = null!;
            bNeighbor = null!;
            if (transition?.a == null
                || transition.b == null
                || Graph.Shared == null
                || !DualGaugeSharedRailRegistry.IsSharedRailTransition(transition))
            {
                return false;
            }

            return TryResolveAtNode(transition.a, out aNeighbor)
                && TryResolveAtNode(transition.b, out bNeighbor)
                && aNeighbor != bNeighbor
                && HasOppositeExplicitSharedRailSides(aNeighbor, bNeighbor);

            bool TryResolveAtNode(TrackNode node, out TrackSegment neighbor)
            {
                neighbor = null!;
                TrackSegment[] connected = Graph.Shared.SegmentsConnectedTo(node)
                    .Where(candidate =>
                        candidate != null
                        && !NarrowGaugeManager.IsGeneratedGhost(candidate)
                        && !SpecialWorkTopologySynchronizer.IsHiddenControlSegment(candidate))
                    .ToArray();
                if (connected.Length != 2 || !connected.Contains(transition))
                {
                    return false;
                }

                neighbor = connected.First(candidate => candidate != transition);
                return NarrowGaugeManager.IsDualGauge(neighbor)
                    && !DualGaugeSharedRailRegistry.IsSharedRailTransition(neighbor);
            }
        }

        private static bool HasOppositeExplicitSharedRailSides(
            TrackSegment first,
            TrackSegment second)
        {
            string firstGauge = TrackAPI.GetSegmentDefinition(first.id)?.Gauge ?? string.Empty;
            string secondGauge = TrackAPI.GetSegmentDefinition(second.id)?.Gauge ?? string.Empty;
            return (IsGauge(firstGauge, "DualGauge_L") && IsGauge(secondGauge, "DualGauge_R"))
                || (IsGauge(firstGauge, "DualGauge_R") && IsGauge(secondGauge, "DualGauge_L"));
        }

        private static bool IsGauge(string actual, string expected)
        {
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static void CreateDualGaugeTrackObject(
            TrackObjectBuilder builder,
            BezierCurve curve,
            float tieSpacing,
            float tieSpacingJitter,
            string trackName,
            Transform parent,
            bool sharesRightRail,
            TrackSegment sourceSegment)
        {
            Vector3 endPoint = curve.EndPoint1;
            BezierCurve localCurve = curve.OffsetBy(-endPoint);

            GameObject root = CreateTrackRoot(builder, trackName, parent);
            root.transform.localPosition = endPoint;

            SwitchGeometry.RailLineCurves stdCurves   = SwitchGeometry.MakeTrackLineSegments(localCurve, Gauge.Standard);
            SwitchGeometry.RailLineCurves thirdCurves = SwitchGeometry.MakeTrackLineSegments(localCurve, ThirdRailGauge);
            LineCurve middleRail = sharesRightRail ? thirdCurves.left : thirdCurves.right;

            CreateRailMeshesWithFrogCuts(builder, stdCurves.left, endPoint, sourceSegment, Gauge.Standard, "DualL", root);
            CreateRailMeshesWithFrogCuts(builder, middleRail, endPoint, sourceSegment, Gauge.Standard, "DualM", root);
            CreateRailMeshesWithFrogCuts(builder, stdCurves.right, endPoint, sourceSegment, Gauge.Standard, "DualR", root);

            CreateDualGaugeSegmentTies(
                builder,
                localCurve,
                tieSpacing,
                tieSpacingJitter,
                root.transform,
                sharesRightRail,
                sourceSegment);
            CreateMeshColliderObject(builder, BuildColliderMesh(localCurve, Gauge.Standard), "Collider", root.transform);
        }

        private static void CreateSharedRailFlipTransitions(
            TrackObjectBuilder builder,
            TrackSegment sourceSegment,
            Transform parent)
        {
            if (sourceSegment == null || Graph.Shared == null)
            {
                return;
            }

            foreach (TrackNode node in new[] { sourceSegment.a, sourceSegment.b }.Where(item => item != null))
            {
                if (!TryResolveSharedRailFlip(sourceSegment, node, out TrackSegment other)
                    || string.Compare(
                        sourceSegment.id,
                        other.id,
                        StringComparison.OrdinalIgnoreCase) > 0)
                {
                    continue;
                }

                CreateSharedRailFlipTransition(builder, node, sourceSegment, other, parent);
            }
        }

        private static void CreateSharedRailFlipTransition(
            TrackObjectBuilder builder,
            TrackNode node,
            TrackSegment segmentA,
            TrackSegment segmentB,
            Transform parent)
        {
            if (!TryGetDualRailsFromNode(
                    segmentA,
                    node,
                    out LineCurve aLeft,
                    out LineCurve aMiddle,
                    out LineCurve aRight)
                || !TryGetDualRailsFromNode(
                    segmentB,
                    node,
                    out LineCurve bLeft,
                    out LineCurve bMiddle,
                    out LineCurve bRight))
            {
                return;
            }

            float availableSpan = Mathf.Min(aMiddle.Length - 0.1f, bMiddle.Length - 0.1f);
            float span = Mathf.Min(SharedRailFlipMaxSpan, availableSpan);
            if (span < SharedRailFlipMinSpan)
            {
                Main.Warn(
                    $"[SharedRailFlip] Node '{node.id}' only has {availableSpan:0.000}m adjoining rail length; " +
                    $"needs at least {SharedRailFlipMinSpan:0.000}m.");
                return;
            }

            bool aSharesRight = DualGaugeSharedRailRegistry.SharesRightRail(segmentA);
            bool bSharesRight = DualGaugeSharedRailRegistry.SharesRightRail(segmentB);
            var aNarrow = new[]
            {
                (Rail: aSharesRight ? aMiddle : aLeft, Outer: !aSharesRight),
                (Rail: aSharesRight ? aRight : aMiddle, Outer: aSharesRight)
            };
            var bNarrow = new[]
            {
                (Rail: bSharesRight ? bMiddle : bLeft, Outer: !bSharesRight),
                (Rail: bSharesRight ? bRight : bMiddle, Outer: bSharesRight)
            };
            float directPairing =
                Vector3.Distance(
                    aNarrow[0].Rail.LinePointAtDistance(span).point,
                    bNarrow[0].Rail.LinePointAtDistance(span).point)
                + Vector3.Distance(
                    aNarrow[1].Rail.LinePointAtDistance(span).point,
                    bNarrow[1].Rail.LinePointAtDistance(span).point);
            float crossedPairing =
                Vector3.Distance(
                    aNarrow[0].Rail.LinePointAtDistance(span).point,
                    bNarrow[1].Rail.LinePointAtDistance(span).point)
                + Vector3.Distance(
                    aNarrow[1].Rail.LinePointAtDistance(span).point,
                    bNarrow[0].Rail.LinePointAtDistance(span).point);
            if (crossedPairing < directPairing)
            {
                (bNarrow[0], bNarrow[1]) = (bNarrow[1], bNarrow[0]);
            }

            GameObject root = CreateTrackRoot(
                builder,
                "shared-rail-flip-" + node.id,
                parent);
            CreateSharedRailFlipRail(
                builder,
                root,
                aNarrow[0].Rail,
                bNarrow[0].Rail,
                span,
                taperAtStart: aNarrow[0].Outer,
                taperAtEnd: bNarrow[0].Outer,
                "NarrowA");
            CreateSharedRailFlipRail(
                builder,
                root,
                aNarrow[1].Rail,
                bNarrow[1].Rail,
                span,
                taperAtStart: aNarrow[1].Outer,
                taperAtEnd: bNarrow[1].Outer,
                "NarrowB");

            Main.Log(
                $"[SharedRailFlip] Rendered fixed shared-rail flip at '{node.id}' " +
                $"between '{segmentA.id}' and '{segmentB.id}' span={span:0.000}.");
        }

        private static void CreateSharedRailFlipRail(
            TrackObjectBuilder builder,
            GameObject root,
            LineCurve railAFromNode,
            LineCurve railBFromNode,
            float span,
            bool taperAtStart,
            bool taperAtEnd,
            string name)
        {
            CreateSharedRailConnectionRail(
                builder,
                root,
                railAFromNode,
                railBFromNode,
                span,
                span,
                taperAtStart,
                taperAtEnd,
                name);
        }

        private static void CreateSharedRailConnectionRail(
            TrackObjectBuilder builder,
            GameObject root,
            LineCurve railAFromNode,
            LineCurve railBFromNode,
            float startDistance,
            float endDistance,
            bool taperAtStart,
            bool taperAtEnd,
            string name)
        {
            CreateSharedRailConnectionRail(
                builder,
                root,
                CreateSharedRailConnectionCurve(
                    railAFromNode,
                    railBFromNode,
                    startDistance,
                    endDistance),
                taperAtStart,
                taperAtEnd,
                name);
        }

        private static LineCurve CreateSharedRailConnectionCurve(
            LineCurve railAFromNode,
            LineCurve railBFromNode,
            float startDistance,
            float endDistance,
            bool startInsideTransition = false,
            bool endInsideTransition = false)
        {
            LinePoint start = railAFromNode.LinePointAtDistance(startDistance);
            LinePoint end = railBFromNode.LinePointAtDistance(endDistance);
            Vector3 startDirection = startInsideTransition ? start.direction : -start.direction;
            Vector3 endDirection = endInsideTransition ? -end.direction : end.direction;
            startDirection.y = 0f;
            endDirection.y = 0f;
            if (startDirection.sqrMagnitude <= 0.0001f
                || endDirection.sqrMagnitude <= 0.0001f)
            {
                return new LineCurve(Array.Empty<LinePoint>(), railAFromNode.Reverse().hand);
            }

            startDirection.Normalize();
            endDirection.Normalize();
            float controlLength = Mathf.Max(
                Vector3.Distance(start.point, end.point) * 0.32f,
                0.75f);
            var bezier = new BezierCurve(
                start.point,
                start.point + startDirection * controlLength,
                end.point - endDirection * controlLength,
                end.point,
                Vector3.up,
                Vector3.up);
            return new LineCurve(
                bezier.Approximate(1.000005f, 0.2f, 16, 30f),
                railAFromNode.Reverse().hand);
        }

        private static void CreateSharedRailConnectionRail(
            TrackObjectBuilder builder,
            GameObject root,
            LineCurve transition,
            bool taperAtStart,
            bool taperAtEnd,
            string name)
        {
            LineCurve sampled = transition.Subdivide(0.12f);
            LinePoint[] points = sampled.Points.ToArray();
            int pointCount = points.Length;
            if (pointCount < 2)
            {
                return;
            }

            var distances = new float[pointCount];
            for (int index = 1; index < pointCount; index++)
            {
                distances[index] = distances[index - 1]
                    + Vector3.Distance(points[index - 1].point, points[index].point);
            }
            float totalLength = distances[pointCount - 1];
            float taperLength = Mathf.Min(
                SharedRailTransitionPointTaperLength,
                totalLength * 0.45f);

            Mesh mesh = BuildStockRailMesh(
                sampled,
                Vector3.zero,
                Gauge.Standard,
                index =>
                {
                    int pointIndex = Mathf.Clamp(index, 0, pointCount - 1);
                    float distanceFromPoint = float.MaxValue;
                    if (taperAtStart)
                    {
                        distanceFromPoint = Mathf.Min(
                            distanceFromPoint,
                            distances[pointIndex]);
                    }
                    if (taperAtEnd)
                    {
                        distanceFromPoint = Mathf.Min(
                            distanceFromPoint,
                            totalLength - distances[pointIndex]);
                    }

                    return distanceFromPoint == float.MaxValue
                        ? 1f
                        : Mathf.SmoothStep(
                            0.025f,
                            1f,
                            Mathf.Clamp01(distanceFromPoint / taperLength));
                });
            CreateMeshObject(builder, mesh, name, root);
        }

        private static void CreateSharedRailTransitionFrogExtensions(
            TrackObjectBuilder builder,
            GameObject root,
            LineCurve extended,
            LineCurve trimmed,
            LineCurve? startSharedFromNode,
            LineCurve? startOppositeStandardFromNode,
            LineCurve? endSharedFromNode,
            LineCurve? endOppositeStandardFromNode,
            string name)
        {
            if (extended.Points.Count() < 2
                || trimmed.Points.Count() < 2
                || extended.Length < 0.12f
                || trimmed.Length < 0.12f)
            {
                return;
            }

            if (startSharedFromNode != null && startOppositeStandardFromNode != null)
            {
                float trimmedStart = Mathf.Clamp(
                    extended.DistanceTo(trimmed.Head.point),
                    0f,
                    extended.Length);
                if (trimmedStart >= 0.12f)
                {
                    LineCurve pocket = extended.Take(trimmedStart);
                    CreateSharedRailTransitionPocketRail(
                        builder,
                        root,
                        pocket,
                        pocket,
                        startSharedFromNode,
                        startOppositeStandardFromNode,
                        name + "-Start");
                }
            }

            if (endSharedFromNode != null && endOppositeStandardFromNode != null)
            {
                float trimmedEnd = Mathf.Clamp(
                    extended.DistanceTo(trimmed.Tail.point),
                    0f,
                    extended.Length);
                float pocketLength = extended.Length - trimmedEnd;
                if (pocketLength >= 0.12f)
                {
                    LineCurve pocket = extended.Skip(trimmedEnd, true).Take(pocketLength);
                    CreateSharedRailTransitionPocketRail(
                        builder,
                        root,
                        pocket,
                        pocket.Reverse(),
                        endSharedFromNode,
                        endOppositeStandardFromNode,
                        name + "-End");
                }
            }
        }

        private static void CreateSharedRailTransitionRail(
            TrackObjectBuilder builder,
            GameObject root,
            LineCurve transition,
            LineCurve? startSharedFromNode,
            LineCurve? startOppositeStandardFromNode,
            LineCurve? endSharedFromNode,
            LineCurve? endOppositeStandardFromNode,
            string name)
        {
            if (transition.Points.Count() < 2 || transition.Length < 0.12f)
            {
                return;
            }

            float startPocketLength =
                startSharedFromNode != null && startOppositeStandardFromNode != null
                    ? FindSharedRailTransitionPocketLength(
                        transition,
                        startSharedFromNode,
                        fromStart: true)
                    : 0f;
            float endPocketLength =
                endSharedFromNode != null && endOppositeStandardFromNode != null
                    ? FindSharedRailTransitionPocketLength(
                        transition,
                        endSharedFromNode,
                        fromStart: false)
                    : 0f;
            float availableForPockets = transition.Length - 0.12f;
            if (startPocketLength + endPocketLength > availableForPockets
                && startPocketLength + endPocketLength > 0.001f)
            {
                float scale = Mathf.Max(0f, availableForPockets)
                    / (startPocketLength + endPocketLength);
                startPocketLength *= scale;
                endPocketLength *= scale;
            }

            if (startPocketLength < 0.12f && endPocketLength < 0.12f)
            {
                CreateSharedRailConnectionRail(
                    builder,
                    root,
                    transition,
                    taperAtStart: false,
                    taperAtEnd: false,
                    name);
                return;
            }

            int pieceIndex = 0;
            if (startPocketLength >= 0.12f
                && startSharedFromNode != null
                && startOppositeStandardFromNode != null)
            {
                LineCurve startPocket = transition.Take(startPocketLength);
                CreateSharedRailTransitionPocketRail(
                    builder,
                    root,
                    startPocket,
                    startPocket,
                    startSharedFromNode,
                    startOppositeStandardFromNode,
                    name + "-PocketStart");
                pieceIndex++;
            }

            float middleStart = startPocketLength >= 0.12f ? startPocketLength : 0f;
            float middleEnd = transition.Length - (endPocketLength >= 0.12f ? endPocketLength : 0f);
            if (middleEnd - middleStart >= 0.12f)
            {
                CreateSharedRailConnectionRail(
                    builder,
                    root,
                    transition.Skip(middleStart, true).Take(middleEnd - middleStart),
                    taperAtStart: false,
                    taperAtEnd: false,
                    pieceIndex == 0 ? name : name + "-Body");
                pieceIndex++;
            }

            if (endPocketLength >= 0.12f
                && endSharedFromNode != null
                && endOppositeStandardFromNode != null)
            {
                LineCurve endPocket = transition
                    .Skip(transition.Length - endPocketLength, true)
                    .Take(endPocketLength);
                CreateSharedRailTransitionPocketRail(
                    builder,
                    root,
                    endPocket,
                    endPocket.Reverse(),
                    endSharedFromNode,
                    endOppositeStandardFromNode,
                name + "-PocketEnd");
            }
        }

        private static void CreateSharedRailTransitionPocketRail(
            TrackObjectBuilder builder,
            GameObject root,
            LineCurve pocketToRender,
            LineCurve pocketFromFrog,
            LineCurve sharedFromNode,
            LineCurve oppositeStandardFromNode,
            string name)
        {
            var flangewayCuts = new List<(LineCurve Center, Vector3 KeepPoint)>();
            AddSharedRailTransitionFlangewayCuts(
                pocketFromFrog,
                sharedFromNode,
                oppositeStandardFromNode,
                flangewayCuts,
                out bool hasStandardFlangeway,
                out bool hasTransitionFlangeway);
            Main.Log(
                $"[SharedRailTransitionCut] {name} pocket={pocketToRender.Length:0.000} " +
                $"fromFrog={pocketFromFrog.Length:0.000} cuts={flangewayCuts.Count} " +
                $"standard={hasStandardFlangeway} transition={hasTransitionFlangeway}.");
            if (flangewayCuts.Count == 0)
            {
                CreateSharedRailConnectionRail(
                    builder,
                    root,
                    pocketToRender,
                    taperAtStart: false,
                    taperAtEnd: false,
                    name);
                return;
            }

            SpecialWorkHardwareRenderer.CreateFlangewayCutRail(
                builder,
                root,
                pocketToRender,
                flangewayCuts,
                SharedRailTransitionFlangeway,
                Vector3.zero,
                name);
        }

        private static float FindSharedRailTransitionPocketLength(
            LineCurve transition,
            LineCurve sharedRail,
            bool fromStart)
        {
            if (transition.Length < 0.2f || sharedRail.Length < 0.2f)
            {
                return 0f;
            }

            float flangewayTrim = FindSharedRailFlangewayTrim(
                transition,
                sharedRail,
                fromStart);
            float requested = Mathf.Max(
                flangewayTrim,
                Mathf.Min(SharedRailTransitionLead, transition.Length * 0.3f));
            return Mathf.Clamp(
                requested,
                0f,
                Mathf.Min(
                    SharedRailTransitionGuardHalfLength,
                    transition.Length * 0.45f));
        }

        private static void AddSharedRailTransitionFlangewayCuts(
            LineCurve transitionFromFrog,
            LineCurve sharedFromNode,
            LineCurve oppositeStandardFromNode,
            ICollection<(LineCurve Center, Vector3 KeepPoint)> cuts,
            out bool hasStandardFlangeway,
            out bool hasTransitionFlangeway)
        {
            hasStandardFlangeway = false;
            hasTransitionFlangeway = false;
            if (transitionFromFrog.Length < 0.2f
                || sharedFromNode.Length < 0.2f
                || oppositeStandardFromNode.Length < 0.2f)
            {
                return;
            }

            float standardLength = Mathf.Min(
                SharedRailTransitionGuardHalfLength,
                Mathf.Min(sharedFromNode.Length - 0.05f, oppositeStandardFromNode.Length - 0.05f));
            float transitionLength = Mathf.Min(
                SharedRailTransitionGuardHalfLength,
                transitionFromFrog.Length - 0.05f);
            if (standardLength < 0.2f || transitionLength < 0.2f)
            {
                return;
            }

            LinePoint keepLinePoint = transitionFromFrog.LinePointAtDistance(
                Mathf.Clamp(transitionLength - 0.02f, 0f, transitionFromFrog.Length));
            Vector3 keepPoint = StockRailProfileCenter(keepLinePoint, transitionFromFrog.hand);

            if (TryBuildFlangeGuideCurve(
                    sharedFromNode,
                    oppositeStandardFromNode,
                    standardLength,
                    Gauge.Standard.HeadWidth * 0.5f,
                    awayFromToward: false,
                    out LineCurve standardFlangeway))
            {
                cuts.Add((standardFlangeway, keepPoint));
                hasStandardFlangeway = true;
            }

            // Transition-side cutter intentionally disabled while isolating
            // the standard-gauge flangeway cut on this frog pocket.
        }

        private static bool TryBuildFlangeGuideCurve(
            LineCurve reference,
            LineCurve toward,
            float length,
            float separation,
            bool awayFromToward,
            out LineCurve center)
        {
            center = default!;
            if (length < 0.2f
                || reference.Points.Count() < 2
                || toward.Points.Count() < 2)
            {
                return false;
            }

            int pointCount = Mathf.Max(2, Mathf.CeilToInt(length / 0.12f) + 1);
            var positions = new Vector3[pointCount];
            for (int index = 0; index < pointCount; index++)
            {
                float distance = length * index / (pointCount - 1);
                if (!TryFlangewayGuidePoint(
                        reference,
                        toward,
                        distance,
                        separation,
                        awayFromToward,
                        out Vector3 point))
                {
                    return false;
                }

                positions[index] = point;
            }

            var points = new LinePoint[pointCount];
            for (int index = 0; index < pointCount; index++)
            {
                Vector3 tangent = index == 0
                    ? positions[1] - positions[0]
                    : index == pointCount - 1
                        ? positions[index] - positions[index - 1]
                        : positions[index + 1] - positions[index - 1];
                tangent.y = 0f;
                Quaternion rotation = tangent.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(tangent.normalized, Vector3.up)
                    : reference.LinePointAtDistance(length * index / (pointCount - 1)).Rotation;
                points[index] = new LinePoint(positions[index], rotation);
            }

            center = new LineCurve(points, reference.hand);
            return true;
        }

        private static bool CreateSharedRailTransitionGuard(
            TrackObjectBuilder builder,
            GameObject root,
            LineCurve neighborSharedFromNode,
            LineCurve neighborOppositeFromNode,
            LineCurve transitionAlignmentFromNode,
            LineCurve transitionGuideTowardFromFrog,
            bool transitionGuideAwayFromToward,
            bool includeStandardHalf,
            LineCurve transitionFromFrog,
            LineCurve opposingTransitionFromNode,
            string name)
        {
            if (neighborSharedFromNode.Length < 0.2f
                || neighborOppositeFromNode.Length < 0.2f
                || transitionAlignmentFromNode.Length < 0.2f
                || transitionGuideTowardFromFrog.Length < 0.2f
                || transitionFromFrog.Length < 0.2f
                || opposingTransitionFromNode.Length < 0.2f)
            {
                return false;
            }

            LineCurve sharedProfile = StockRailProfileCenterCurve(neighborSharedFromNode);
            LineCurve oppositeProfile = StockRailProfileCenterCurve(neighborOppositeFromNode);
            LineCurve transitionProfile = StockRailProfileCenterCurve(transitionFromFrog);
            LineCurve transitionGuideProfile =
                StockRailProfileCenterCurve(transitionGuideTowardFromFrog);
            Vector3 opposingTransitionStart = StockRailProfileCenter(
                opposingTransitionFromNode.Head,
                opposingTransitionFromNode.hand);
            float standardDistance = Mathf.Clamp(
                sharedProfile.DistanceTo(opposingTransitionStart),
                0f,
                Mathf.Min(
                    sharedProfile.Length - 0.05f,
                    oppositeProfile.Length - 0.05f));
            float transitionDistance = Mathf.Min(
                SharedRailTransitionGuardTransitionLength,
                transitionFromFrog.Length - 0.05f);
            if (transitionDistance < 0.2f)
            {
                return false;
            }
            Main.Log(
                $"[SharedRailTransitionGuard] {name} standardWing={standardDistance:0.000} " +
                $"transitionWing={transitionDistance:0.000}.");

            float guardSeparation =
                Gauge.Standard.HeadWidth + SharedRailTransitionFlangeway;
            if (!TryFlangewayGuidePoint(
                    sharedProfile,
                    oppositeProfile,
                    0f,
                    guardSeparation,
                    awayFromToward: false,
                    out Vector3 standardNear)
                || !TryFlangewayGuidePoint(
                    sharedProfile,
                    oppositeProfile,
                    standardDistance,
                    guardSeparation,
                    awayFromToward: false,
                    out Vector3 standardFar)
                || !TryFlangewayGuidePoint(
                    transitionProfile,
                    transitionGuideProfile,
                    0f,
                    guardSeparation,
                    awayFromToward: transitionGuideAwayFromToward,
                    out Vector3 transitionNear)
                || !TryFlangewayGuidePoint(
                    transitionProfile,
                    transitionGuideProfile,
                    transitionDistance,
                    guardSeparation,
                    awayFromToward: transitionGuideAwayFromToward,
                    out Vector3 transitionFar))
            {
                return false;
            }

            Vector3 kink = transitionNear;
            LineCurve guard = CreateKinkedStockRailVisualCurve(
                standardFar,
                kink,
                transitionFar,
                Hand.Left);
            return CreateSharedRailGuardRail(
                builder,
                root,
                guard,
                flareAtStart: includeStandardHalf,
                flareAtEnd: true,
                name);
        }

        private static LineCurve CreateKinkedStockRailVisualCurve(
            Vector3 profileStart,
            Vector3 profileKink,
            Vector3 profileEnd,
            Hand hand)
        {
            Vector3 firstDirection = profileKink - profileStart;
            Vector3 secondDirection = profileEnd - profileKink;
            firstDirection.y = 0f;
            secondDirection.y = 0f;
            if (firstDirection.sqrMagnitude <= 0.0001f
                || secondDirection.sqrMagnitude <= 0.0001f)
            {
                return CreateStraightStockRailVisualCurve(profileStart, profileEnd, hand);
            }

            firstDirection.Normalize();
            secondDirection.Normalize();
            Vector3 kinkDirection = firstDirection + secondDirection;
            if (kinkDirection.sqrMagnitude <= 0.0001f)
            {
                kinkDirection = secondDirection;
            }
            else
            {
                kinkDirection.Normalize();
            }

            Quaternion startRotation = Quaternion.LookRotation(firstDirection, Vector3.up);
            Quaternion kinkRotation = Quaternion.LookRotation(kinkDirection, Vector3.up);
            Quaternion endRotation = Quaternion.LookRotation(secondDirection, Vector3.up);
            float profileCenterOffset = hand == Hand.Left
                ? -Gauge.Standard.HeadWidth * 0.5f
                : Gauge.Standard.HeadWidth * 0.5f;
            return new LineCurve(
                new[]
                {
                    new LinePoint(
                        profileStart - startRotation * Vector3.right * profileCenterOffset,
                        startRotation),
                    new LinePoint(
                        profileKink - kinkRotation * Vector3.right * profileCenterOffset,
                        kinkRotation),
                    new LinePoint(
                        profileEnd - endRotation * Vector3.right * profileCenterOffset,
                        endRotation)
                },
                hand);
        }

        private static bool CreateSharedRailOpposingTransitionGuard(
            TrackObjectBuilder builder,
            GameObject root,
            LineCurve primaryTransitionFromFrog,
            LineCurve opposingTransitionFromFrog,
            string name)
        {
            if (primaryTransitionFromFrog.Length < 0.2f
                || opposingTransitionFromFrog.Length < 0.2f)
            {
                return false;
            }

            LineCurve primaryProfile =
                StockRailProfileCenterCurve(primaryTransitionFromFrog);
            LineCurve opposingProfile =
                StockRailProfileCenterCurve(opposingTransitionFromFrog);
            const float guardStartDistance = 0f;
            float guardEndDistance = Mathf.Min(
                guardStartDistance + SharedRailTransitionOpposingGuardLength,
                opposingProfile.Length - 0.05f);
            float guardLength = guardEndDistance - guardStartDistance;
            float guardSeparation =
                Gauge.Standard.HeadWidth
                + SharedRailTransitionFlangeway;
            if (guardLength < 0.2f)
            {
                return false;
            }

            Vector3 direction = opposingProfile.Tail.point - opposingProfile.Head.point;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            direction.Normalize();
            Vector3 towardPrimary =
                primaryProfile.LinePointAtDistance(
                    primaryProfile.DistanceTo(opposingProfile.Head.point)).point
                - opposingProfile.Head.point;
            towardPrimary.y = 0f;
            if (towardPrimary.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            Vector3 perpendicular = Vector3.Cross(Vector3.up, direction).normalized;
            if (Vector3.Dot(perpendicular, towardPrimary) < 0f)
            {
                perpendicular = -perpendicular;
            }

            Vector3 anchor =
                opposingProfile.Head.point + perpendicular * guardSeparation;
            Vector3 guardNear =
                anchor - direction * SharedRailTransitionOpposingGuardBackExtension;
            float renderedLength =
                guardLength + SharedRailTransitionOpposingGuardBackExtension;
            Quaternion rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            Vector3 primaryPoint = primaryProfile.LinePointAtDistance(
                primaryProfile.DistanceTo(anchor)).point;
            Vector3 inward = primaryPoint - anchor;
            inward.y = 0f;
            Hand inwardHand = Vector3.Dot(inward, rotation * Vector3.right) >= 0f
                ? Hand.Right
                : Hand.Left;
            Vector3 guardFar = anchor + direction * guardLength;
            LineCurve guard = new LineCurve(
                new[]
                {
                    new LinePoint(guardNear, rotation),
                    new LinePoint(anchor, rotation),
                    new LinePoint(guardFar, rotation)
                },
                inwardHand);
            Main.Log(
                $"[SharedRailTransitionGuard] {name} opposingStart={guardStartDistance:0.000} " +
                $"opposingLength={renderedLength:0.000} back=" +
                $"{SharedRailTransitionOpposingGuardBackExtension:0.000} " +
                $"forward={guardLength:0.000} separation={guardSeparation:0.000} " +
                $"inwardHand={inwardHand}.");
            return CreateSharedRailGuardRail(
                builder,
                root,
                guard,
                flareAtStart: true,
                flareAtEnd: true,
                name);
        }

        private static float FindSharedRailTransitionStartDistance(
            LineCurve transitionFromFrog,
            LineCurve sharedFromNode)
        {
            if (transitionFromFrog.Length < 0.1f || sharedFromNode.Length < 0.1f)
            {
                return 0f;
            }

            return Mathf.Clamp(
                FindSharedRailFlangewayTrim(
                    transitionFromFrog,
                    sharedFromNode,
                    fromStart: true),
                0f,
                transitionFromFrog.Length);
        }

        private static bool CreateSharedRailGuardRail(
            TrackObjectBuilder builder,
            GameObject root,
            LineCurve guard,
            bool flareAtStart,
            bool flareAtEnd,
            string name)
        {
            if (guard.Length < 0.12f || guard.Points.Count() < 2)
            {
                return false;
            }

            LineCurve flared = FlareSharedRailGuardEnds(guard, flareAtStart, flareAtEnd)
                .Subdivide(0.12f);
            if (flared.Length < 0.12f || flared.Points.Count() < 2)
            {
                return false;
            }

            Mesh mesh = BuildStockRailMesh(
                flared,
                Vector3.zero,
                Gauge.Standard,
                _ => 1f);
            CreateMeshObject(builder, mesh, name, root);
            return true;
        }

        private static LineCurve FlareSharedRailGuardEnds(
            LineCurve guard,
            bool flareAtStart,
            bool flareAtEnd)
        {
            if ((!flareAtStart && !flareAtEnd)
                || guard.Length <= SharedRailTransitionGuardEndFlareLength * 2f)
            {
                return guard;
            }

            LineCurve flared = guard;
            if (flareAtStart)
            {
                flared = FlareSharedRailGuardStart(flared);
            }
            if (flareAtEnd)
            {
                flared = FlareSharedRailGuardStart(flared.Reverse()).Reverse();
            }

            return flared;
        }

        private static LineCurve FlareSharedRailGuardStart(LineCurve guard)
        {
            if (guard.Length <= SharedRailTransitionGuardEndFlareLength * 2f)
            {
                return guard;
            }

            float flareLength = SharedRailTransitionGuardEndFlareLength;
            float lateral =
                Mathf.Tan(SharedRailTransitionGuardEndFlareAngle * Mathf.Deg2Rad)
                * flareLength;
            Vector3 flareSide = guard.hand == Hand.Left
                ? Vector3.left
                : Vector3.right;
            float signedAngle = guard.hand == Hand.Left
                ? SharedRailTransitionGuardEndFlareAngle
                : -SharedRailTransitionGuardEndFlareAngle;
            Quaternion flareRotation = Quaternion.Euler(0f, signedAngle, 0f);

            LinePoint head = guard.Head;
            LineCurve body = guard.Skip(flareLength, false);
            body.Insert(
                0,
                new LinePoint(
                    head.point + head.Rotation * flareSide * lateral,
                    flareRotation * head.Rotation));
            return body;
        }

        private static bool TryResolveSharedRailGuardKink(
            Vector3 standardNear,
            Vector3 standardFar,
            Vector3 transitionNear,
            Vector3 transitionFar,
            out Vector3 kink)
        {
            if (TryLineIntersectionXZ(
                    standardNear,
                    standardFar,
                    transitionNear,
                    transitionFar,
                    out kink)
                && Vector3.Distance(kink, standardNear)
                    <= SharedRailTransitionGuardHalfLength * 2f
                && Vector3.Distance(kink, transitionNear)
                    <= SharedRailTransitionGuardHalfLength * 2f)
            {
                return true;
            }

            // When the two offset guide lines are nearly parallel, the exact
            // intersection falls far outside the transition. Keep the guard
            // visible and make the transition half honor the flangeway.
            kink = transitionNear;
            return Vector3.Distance(kink, standardFar) > 0.1f
                && Vector3.Distance(kink, transitionFar) > 0.1f;
        }

        private static bool TryFlangewayGuidePoint(
            LineCurve reference,
            LineCurve toward,
            float distance,
            float separation,
            bool awayFromToward,
            out Vector3 point)
        {
            LinePoint referencePoint = reference.LinePointAtDistance(
                Mathf.Clamp(distance, 0f, reference.Length));
            LinePoint towardPoint = toward.LinePointAtDistance(
                toward.DistanceTo(referencePoint.point));
            Vector3 offset = towardPoint.point - referencePoint.point;
            offset.y = 0f;
            if (offset.sqrMagnitude <= 0.0001f)
            {
                if (!TryResolveNearbyFlangewayOffset(
                        reference,
                        toward,
                        distance,
                        out offset))
                {
                    point = Vector3.zero;
                    return false;
                }
            }

            Vector3 direction = awayFromToward
                ? -offset.normalized
                : offset.normalized;
            point = referencePoint.point + direction * separation;
            return true;
        }

        private static bool TryResolveNearbyFlangewayOffset(
            LineCurve reference,
            LineCurve toward,
            float distance,
            out Vector3 offset)
        {
            const float sampleStep = 0.12f;
            float maxSearch = Mathf.Min(
                SharedRailTransitionGuardHalfLength,
                Mathf.Max(reference.Length, sampleStep));
            for (float radius = sampleStep; radius <= maxSearch + sampleStep * 0.5f; radius += sampleStep)
            {
                if (TryResolveFlangewayOffset(reference, toward, distance + radius, out offset)
                    || TryResolveFlangewayOffset(reference, toward, distance - radius, out offset))
                {
                    return true;
                }
            }

            offset = Vector3.zero;
            return false;
        }

        private static bool TryResolveFlangewayOffset(
            LineCurve reference,
            LineCurve toward,
            float distance,
            out Vector3 offset)
        {
            LinePoint referencePoint = reference.LinePointAtDistance(
                Mathf.Clamp(distance, 0f, reference.Length));
            LinePoint towardPoint = toward.LinePointAtDistance(
                toward.DistanceTo(referencePoint.point));
            offset = towardPoint.point - referencePoint.point;
            offset.y = 0f;
            return offset.sqrMagnitude > 0.0001f;
        }

        private static bool TryLineIntersectionXZ(
            Vector3 a0,
            Vector3 a1,
            Vector3 b0,
            Vector3 b1,
            out Vector3 intersection)
        {
            Vector3 a = a1 - a0;
            Vector3 b = b1 - b0;
            float determinant = a.x * b.z - a.z * b.x;
            if (Mathf.Abs(determinant) <= 0.000001f)
            {
                intersection = Vector3.zero;
                return false;
            }

            Vector3 delta = b0 - a0;
            float parameter = (delta.x * b.z - delta.z * b.x) / determinant;
            intersection = a0 + a * parameter;
            intersection.y = (a0.y + b0.y) * 0.5f;
            return true;
        }

        private static LineCurve AlignSharedRailTransitionVisuals(
            LineCurve transition,
            LineCurve startReference,
            LineCurve endReference,
            float startDistance,
            float endDistance)
        {
            LinePoint[] points = transition.Points.ToArray();
            if (points.Length < 2)
            {
                return transition;
            }

            Vector3 startCorrection =
                StockRailProfileCenter(startReference.LinePointAtDistance(startDistance), startReference.hand)
                - StockRailProfileCenter(points[0], transition.hand);
            Vector3 endCorrection =
                StockRailProfileCenter(endReference.LinePointAtDistance(endDistance), endReference.hand)
                - StockRailProfileCenter(points[points.Length - 1], transition.hand);

            var corrected = new LinePoint[points.Length];
            for (int index = 0; index < points.Length; index++)
            {
                float t = (float)index / (points.Length - 1);
                corrected[index] = new LinePoint(
                    points[index].point + Vector3.Lerp(startCorrection, endCorrection, t),
                    points[index].Rotation);
            }

            return new LineCurve(corrected, transition.hand);
        }

        private static void CreateSymmetricStraightSharedRailTransitionVisuals(
            LineCurve transitionA,
            LineCurve transitionB,
            LineCurve? aStartSharedRail,
            LineCurve? aEndSharedRail,
            LineCurve? bStartSharedRail,
            LineCurve? bEndSharedRail,
            bool trimSharedFlangeways,
            out LineCurve visualA,
            out LineCurve visualB)
        {
            Vector3 aStart = StockRailProfileCenter(transitionA.Head, transitionA.hand);
            Vector3 aEnd = StockRailProfileCenter(transitionA.Tail, transitionA.hand);
            Vector3 bStart = StockRailProfileCenter(transitionB.Head, transitionB.hand);
            Vector3 bEnd = StockRailProfileCenter(transitionB.Tail, transitionB.hand);
            visualA = CreateStraightStockRailVisualCurve(
                aStart,
                aEnd,
                transitionA.hand);
            visualB = CreateStraightStockRailVisualCurve(
                bStart,
                bEnd,
                transitionB.hand);
            if (!trimSharedFlangeways)
            {
                return;
            }

            float aStartTrim = aStartSharedRail != null
                ? FindSharedRailFlangewayTrim(visualA, aStartSharedRail, fromStart: true)
                : 0f;
            float aEndTrim = aEndSharedRail != null
                ? FindSharedRailFlangewayTrim(visualA, aEndSharedRail, fromStart: false)
                : 0f;
            float bStartTrim = bStartSharedRail != null
                ? FindSharedRailFlangewayTrim(visualB, bStartSharedRail, fromStart: true)
                : 0f;
            float bEndTrim = bEndSharedRail != null
                ? FindSharedRailFlangewayTrim(visualB, bEndSharedRail, fromStart: false)
                : 0f;
            float sharedTrim = Mathf.Max(
                Mathf.Max(aStartTrim, aEndTrim),
                Mathf.Max(bStartTrim, bEndTrim));
            sharedTrim = Mathf.Max(
                0f,
                sharedTrim - SharedRailTransitionTrimExtension);

            visualA = TrimSharedRailTransitionVisual(
                visualA,
                aStartSharedRail != null ? sharedTrim : 0f,
                aEndSharedRail != null ? sharedTrim : 0f);
            visualB = TrimSharedRailTransitionVisual(
                visualB,
                bStartSharedRail != null ? sharedTrim : 0f,
                bEndSharedRail != null ? sharedTrim : 0f);
        }

        private static LineCurve CreateStraightStockRailVisualCurve(
            Vector3 profileStart,
            Vector3 profileEnd,
            Hand hand)
        {
            Vector3 direction = profileEnd - profileStart;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return new LineCurve(Array.Empty<LinePoint>(), hand);
            }

            Quaternion rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            float profileCenterOffset = hand == Hand.Left
                ? -Gauge.Standard.HeadWidth * 0.5f
                : Gauge.Standard.HeadWidth * 0.5f;
            Vector3 centerlineOffset =
                -(rotation * Vector3.right) * profileCenterOffset;
            Vector3 start = profileStart + centerlineOffset;
            Vector3 end = profileEnd + centerlineOffset;
            int pointCount = Mathf.Max(
                2,
                Mathf.CeilToInt(Vector3.Distance(start, end) / 0.2f) + 1);
            var points = new LinePoint[pointCount];
            for (int index = 0; index < pointCount; index++)
            {
                float t = (float)index / (pointCount - 1);
                points[index] = new LinePoint(Vector3.Lerp(start, end, t), rotation);
            }

            return new LineCurve(points, hand);
        }

        private static LineCurve TrimSharedRailTransitionVisual(
            LineCurve transition,
            float startTrim,
            float endTrim)
        {
            float retainedLength = transition.Length - startTrim - endTrim;
            return retainedLength >= 0.1f
                ? transition.Skip(startTrim, true).Take(retainedLength)
                : new LineCurve(Array.Empty<LinePoint>(), transition.hand);
        }

        private static float FindSharedRailFlangewayTrim(
            LineCurve transition,
            LineCurve sharedRail,
            bool fromStart)
        {
            LineCurve transitionProfile = StockRailProfileCenterCurve(transition);
            LineCurve sharedProfile = StockRailProfileCenterCurve(sharedRail);
            float requiredSeparation = Gauge.Standard.HeadWidth + SharedRailTransitionFlangeway;
            const float sampleSpacing = 0.025f;
            float previous = 0f;

            for (float trim = 0f; trim <= transitionProfile.Length; trim += sampleSpacing)
            {
                float distance = fromStart
                    ? trim
                    : transitionProfile.Length - trim;
                Vector3 point = transitionProfile.LinePointAtDistance(distance).point;
                if (DistancePointToCurve(point, sharedProfile) < requiredSeparation)
                {
                    previous = trim;
                    continue;
                }

                float low = previous;
                float high = trim;
                for (int iteration = 0; iteration < 8; iteration++)
                {
                    float middle = (low + high) * 0.5f;
                    float middleDistance = fromStart
                        ? middle
                        : transitionProfile.Length - middle;
                    Vector3 middlePoint =
                        transitionProfile.LinePointAtDistance(middleDistance).point;
                    if (DistancePointToCurve(middlePoint, sharedProfile) < requiredSeparation)
                    {
                        low = middle;
                    }
                    else
                    {
                        high = middle;
                    }
                }

                return high;
            }

            return 0f;
        }

        private static LineCurve StockRailProfileCenterCurve(LineCurve curve)
        {
            return new LineCurve(
                curve.Points.Select(point =>
                    new LinePoint(
                        StockRailProfileCenter(point, curve.hand),
                        point.Rotation)),
                curve.hand);
        }

        private static Vector3 StockRailProfileCenter(LinePoint point, Hand hand)
        {
            float profileCenterOffset = hand == Hand.Left
                ? -Gauge.Standard.HeadWidth * 0.5f
                : Gauge.Standard.HeadWidth * 0.5f;
            return point.point + point.Rotation * Vector3.right * profileCenterOffset;
        }

        private static bool TryGetDualRailsFromNode(
            TrackSegment segment,
            TrackNode node,
            out LineCurve left,
            out LineCurve middle,
            out LineCurve right)
        {
            left = null!;
            middle = null!;
            right = null!;
            if (segment == null || node == null || !NarrowGaugeManager.IsDualGauge(segment))
            {
                return false;
            }

            SwitchGeometry.RailLineCurves standard =
                SwitchGeometry.MakeTrackLineSegments(segment.Curve, Gauge.Standard);
            SwitchGeometry.RailLineCurves third =
                SwitchGeometry.MakeTrackLineSegments(segment.Curve, ThirdRailGauge);
            left = OrientCurveAwayFromPoint(standard.left, node.transform.localPosition);
            middle = OrientCurveAwayFromPoint(
                DualGaugeSharedRailRegistry.SharesRightRail(segment)
                    ? third.left
                    : third.right,
                node.transform.localPosition);
            right = OrientCurveAwayFromPoint(standard.right, node.transform.localPosition);
            return left.Length > 0.1f && middle.Length > 0.1f && right.Length > 0.1f;
        }

        private static bool TryResolveSharedRailFlip(
            TrackSegment sourceSegment,
            TrackNode node,
            out TrackSegment other)
        {
            other = null!;
            return false;
        }

        private static IEnumerable<(float Start, float End)> SharedRailFlipMiddleCuts(
            LineCurve worldRail,
            TrackSegment sourceSegment)
        {
            if (sourceSegment == null || Graph.Shared == null)
            {
                yield break;
            }

            foreach (TrackNode node in new[] { sourceSegment.a, sourceSegment.b }.Where(item => item != null))
            {
                if (!TryResolveSharedRailFlip(sourceSegment, node, out TrackSegment other)
                    || !TryGetDualRailsFromNode(sourceSegment, node, out _, out LineCurve middle, out _)
                    || !TryGetDualRailsFromNode(other, node, out _, out LineCurve otherMiddle, out _)
                    || DistancePointToCurve(middle.Head.point, worldRail) > SharedRailFlipMatchTolerance)
                {
                    continue;
                }

                float nodeDistance = Mathf.Clamp(
                    worldRail.DistanceTo(middle.Head.point),
                    0f,
                    worldRail.Length);
                float span = Mathf.Min(
                    SharedRailFlipMaxSpan,
                    Mathf.Min(middle.Length - 0.1f, otherMiddle.Length - 0.1f));
                if (span < SharedRailFlipMinSpan)
                {
                    continue;
                }

                bool nodeNearHead = nodeDistance <= worldRail.Length * 0.5f;
                yield return nodeNearHead
                    ? (0f, span)
                    : (Mathf.Max(0f, worldRail.Length - span), worldRail.Length);
            }
        }

        private static void CreateRailMeshesWithFrogCuts(
            TrackObjectBuilder builder,
            LineCurve localRail,
            Vector3 segmentOffset,
            TrackSegment sourceSegment,
            Gauge gauge,
            string objectName,
            GameObject root)
        {
            LineCurve worldRail = localRail.Offset(segmentOffset);
            (float Start, float End)[] cuts = MergeCutIntervals(
                SpecialWorkHardwareRenderer.OwnershipCuts(worldRail, sourceSegment)
                    .Concat(GaugeSeparationFrogCuts(worldRail, sourceSegment))
                    .Concat(objectName == "DualM"
                        ? SharedRailFlipMiddleCuts(worldRail, sourceSegment)
                        : Enumerable.Empty<(float Start, float End)>()))
                .ToArray();
            if (cuts.Length == 0)
            {
                CreateMeshObject(
                    builder,
                    BuildStockRailMesh(localRail, segmentOffset, gauge, _ => 1f),
                    objectName,
                    root);
                return;
            }

            Main.Log(
                $"[SpecialWorkSegmentClip] segment={sourceSegment.id} rail={objectName} " +
                $"gaugeInside={gauge.Inside:0.000} cuts=" +
                string.Join(",", cuts.Select(cut => $"{cut.Start:0.000}-{cut.End:0.000}")));

            float cursor = 0f;
            int pieceIndex = 0;
            foreach ((float start, float end) in cuts)
            {
                CreatePiece(cursor, start);
                cursor = Mathf.Max(cursor, end);
            }

            CreatePiece(cursor, localRail.Length);

            void CreatePiece(float start, float end)
            {
                if (end - start < 0.06f)
                {
                    return;
                }

                LineCurve piece = localRail.Skip(start, true).Take(end - start);
                CreateMeshObject(
                    builder,
                    BuildStockRailMesh(piece, segmentOffset, gauge, _ => 1f),
                    objectName + "-" + pieceIndex++,
                    root);
            }
        }

        private static void CreateRailOutsideMeasuredOwnership(
            TrackObjectBuilder builder,
            LineCurve localRail,
            Vector3 switchHome,
            Gauge gauge,
            string objectName,
            GameObject root,
            TrackNode node)
        {
            (float Start, float End)[] cuts = MergeCutIntervals(
                SpecialWorkHardwareRenderer.OwnershipCutsForNode(
                    localRail.Offset(switchHome),
                    node))
                .ToArray();
            if (cuts.Length == 0)
            {
                CreateMeshObject(
                    builder,
                    BuildStockRailMesh(localRail, switchHome, gauge, _ => 1f),
                    objectName,
                    root);
                return;
            }

            float cursor = 0f;
            int pieceIndex = 0;
            foreach ((float start, float end) in cuts)
            {
                CreatePiece(cursor, start);
                cursor = Mathf.Max(cursor, end);
            }

            CreatePiece(cursor, localRail.Length);

            void CreatePiece(float start, float end)
            {
                if (end - start < 0.06f)
                {
                    return;
                }

                CreateMeshObject(
                    builder,
                    BuildStockRailMesh(
                        localRail.Skip(start, true).Take(end - start),
                        switchHome,
                        gauge,
                        _ => 1f),
                    objectName + "-" + pieceIndex++,
                    root);
            }
        }

        private static IEnumerable<(float Start, float End)> MergeCutIntervals(
            IEnumerable<(float Start, float End)> cuts)
        {
            (float Start, float End)[] ordered = cuts
                .Where(cut => cut.End > cut.Start)
                .OrderBy(cut => cut.Start)
                .ToArray();
            if (ordered.Length == 0)
            {
                yield break;
            }

            float start = ordered[0].Start;
            float end = ordered[0].End;
            for (int index = 1; index < ordered.Length; index++)
            {
                if (ordered[index].Start <= end + 0.02f)
                {
                    end = Mathf.Max(end, ordered[index].End);
                    continue;
                }

                yield return (start, end);
                start = ordered[index].Start;
                end = ordered[index].End;
            }

            yield return (start, end);
        }

        private static bool TryDistanceAlongCurve(
            LineCurve curve,
            Vector3 point,
            out float distanceAlong,
            out float separation)
        {
            distanceAlong = 0f;
            separation = float.MaxValue;
            float traversed = 0f;
            bool found = false;
            foreach ((int _, LineSegment segment) in curve.Segments)
            {
                Vector3 delta = segment.b.point - segment.a.point;
                float length = delta.magnitude;
                if (length <= 0.00001f)
                {
                    continue;
                }

                float t = Mathf.Clamp01(
                    Vector3.Dot(point - segment.a.point, delta) / delta.sqrMagnitude);
                Vector3 closest = segment.a.point + delta * t;
                float candidateSeparation = Vector3.Distance(point, closest);
                if (candidateSeparation < separation)
                {
                    separation = candidateSeparation;
                    distanceAlong = traversed + length * t;
                    found = true;
                }

                traversed += length;
            }

            return found;
        }

        private static void CreateDualGaugeSegmentTies(
            TrackObjectBuilder builder,
            BezierCurve curve,
            float spacing,
            float tieSpacingJitter,
            Transform parent,
            bool sharesRightRail,
            TrackSegment sourceSegment)
        {
            var ties = new List<PointDirection>();
            var tiePlates = new List<PointDirection>();

            float jitter = tieSpacingJitter / 4f;
            LineCurve lineCurve = new LineCurve(curve.Approximate(1.000005f, 0.5f, 16, 40f), Hand.Left);
            (float Start, float End)[] tieCuts = MergeCutIntervals(
                SpecialWorkHardwareRenderer.TieOwnershipCuts(
                    lineCurve.Offset(parent.localPosition),
                    sourceSegment))
                .ToArray();
            LogSpecialWorkTieCuts(sourceSegment, tieCuts);
            float tieCount = Mathf.Round(lineCurve.Length / spacing);
            if (tieCount == 0f)
            {
                return;
            }

            spacing = lineCurve.Length / tieCount;
            var cursor = lineCurve.CursorAtHead().Skip(spacing / 2f);

            float stdHalf      = Gauge.Standard.Inside / 2f;              // 0.7175m from center
            float thirdRailAbs = ThreeFootGauge.Inside - stdHalf;         // 0.1969m left of center
            float hw           = Gauge.Standard.HeadWidth / 2f;

            for (int i = 0; i < tieCount; i++)
            {
                float tieDistance = spacing / 2f + spacing * i;
                LinePoint linePoint = cursor.LinePoint();
                Vector3 point = linePoint.point;
                Quaternion rotation = linePoint.Rotation;

                if (IsDistanceInsideAnyCut(tieDistance, tieCuts))
                {
                    cursor = cursor.Skip(spacing);
                    continue;
                }

                Vector3 tieCenter = point + rotation * Vector3.left * UnityEngine.Random.Range(-jitter, jitter);
                ties.Add(new PointDirection(tieCenter, rotation));

                tiePlates.Add(new PointDirection(point + rotation * Vector3.left  * (stdHalf + hw), rotation));
                tiePlates.Add(new PointDirection(
                    point + (sharesRightRail ? rotation * Vector3.left : rotation * Vector3.right) * (thirdRailAbs + hw),
                    rotation));
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

        private static bool IsDistanceInsideAnyCut(
            float distance,
            IEnumerable<(float Start, float End)> cuts)
        {
            foreach ((float start, float end) in cuts)
            {
                if (distance >= start && distance <= end)
                {
                    return true;
                }
            }

            return false;
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
            BezierCurve bRoadbedCurve,
            string descriptorId)
        {
            GameObject container = CreateGeneratedObjectContainer(builder);
            container.transform.localPosition = stdGeometry.switchHome;

            bool aDual = NarrowGaugeManager.IsDualGauge(aProxy.Segment);
            bool bDual = NarrowGaugeManager.IsDualGauge(bProxy.Segment);
            bool aNarrowOnly = NarrowGaugeManager.IsNarrowGauge(aProxy.Segment) && !aDual;
            bool bNarrowOnly = NarrowGaugeManager.IsNarrowGauge(bProxy.Segment) && !bDual;

            if (aDual && bNarrowOnly)
            {
                CreateDualGaugeNarrowSplitSwitchRailObjects(
                    builder,
                    stdGeometry,
                    aProxy,
                    bProxy,
                    node,
                    container.transform,
                    descriptorId);
            }
            else
            {
                if (aNarrowOnly && bDual)
                {
                    Main.Warn(
                        $"[Build] Switch '{node.id}' is narrow-through / dual-diverging; using the full dual turnout visual for now.");
                }

                CreateDualGaugeSwitchRailObjects(
                    builder,
                    stdGeometry,
                    aProxy,
                    bProxy,
                    node,
                    container.transform,
                    descriptorId);
            }

            CreateMeshColliderObject(
                builder,
                BuildColliderMesh(
                    aProxy.Curve.OffsetBy(-stdGeometry.switchHome),
                    GaugeForSwitchCollider(aProxy.Segment)),
                "Collider-a",
                container.transform);
            CreateMeshColliderObject(
                builder,
                BuildColliderMesh(
                    bProxy.Curve.OffsetBy(-stdGeometry.switchHome),
                    GaugeForSwitchCollider(bProxy.Segment)),
                "Collider-b",
                container.transform);

            CreateRoadbed(builder, aRoadbedCurve, container.transform, TrackSegment.Style.Standard);
            CreateRoadbed(builder, bRoadbedCurve, container.transform, TrackSegment.Style.Standard);

            SpecialWorkHardwareRenderer.AddAdditionalHardware(
                builder,
                node,
                stdGeometry,
                container.transform);
            SpecialWorkHardwareRenderer.LogBuiltObjectCounts(
                node,
                descriptorId,
                nameof(BuildDualGaugeSwitch),
                container.transform);
            container.SetActive(true);
            return container;
        }

        private static Gauge GaugeForSwitchCollider(TrackSegment segment)
        {
            return NarrowGaugeManager.IsNarrowGauge(segment)
                && !NarrowGaugeManager.IsDualGauge(segment)
                    ? ThreeFootGauge
                    : Gauge.Standard;
        }

        private static void CreateDualGaugeSwitchRailObjects(
            TrackObjectBuilder builder,
            SwitchGeometry stdGeometry,
            SegmentProxy aProxy,
            SegmentProxy bProxy,
            TrackNode node,
            Transform parent,
            string descriptorId)
        {
            GameObject root = CreateTrackRoot(builder, "sw-" + node.id, parent);
            bool suppressLegacyFixedRails =
                SpecialWorkHardwareRenderer.ShouldSuppressLegacySpecialWorkRails(node);
            bool renderPointHardware =
                SpecialWorkHardwareRenderer.CanRenderLegacyPointHardware(node);
            if (suppressLegacyFixedRails)
            {
                SpecialWorkHardwareRenderer.LogVanillaSuppression(
                    node,
                    descriptorId,
                    nameof(CreateDualGaugeSwitchRailObjects),
                    vanillaSwitchObjects: 1,
                    vanillaRailObjects: renderPointHardware ? 10 : 8,
                    vanillaTieObjects: renderPointHardware ? 1 : 0);
            }

            if (!suppressLegacyFixedRails)
            {
                CreateRailOutsideMeasuredOwnership(builder, stdGeometry.leftStockRail, stdGeometry.switchHome, Gauge.Standard, "StockL", root, node);
                CreateRailOutsideMeasuredOwnership(builder, stdGeometry.rightStockRail, stdGeometry.switchHome, Gauge.Standard, "StockR", root, node);
                CreateRailOutsideMeasuredOwnership(builder, stdGeometry.aClosureRail, stdGeometry.switchHome, Gauge.Standard, "ClosureA", root, node);
                CreateRailOutsideMeasuredOwnership(builder, stdGeometry.bClosureRail, stdGeometry.switchHome, Gauge.Standard, "ClosureB", root, node);
                CreateMeshObject(builder, BuildStockRailMesh(stdGeometry.leftGuardRail, stdGeometry.switchHome, Gauge.Standard, _ => 1f), "GuardA", root);
                CreateMeshObject(builder, BuildStockRailMesh(stdGeometry.rightGuardRail, stdGeometry.switchHome, Gauge.Standard, _ => 1f), "GuardB", root);
            }

            if (!renderPointHardware)
            {
                CreateSwitchStand(builder, stdGeometry, node, root.transform);
                return;
            }

            GameObject stdNormalPoint = CreateDualPointRail(builder, stdGeometry.aPointRail, "PointA", stdGeometry.switchHome, root);
            GameObject stdReversedPoint = CreateDualPointRail(builder, stdGeometry.bPointRail, "PointB", stdGeometry.switchHome, root);

            NarrowGaugeSwitchGeometry.AlignSwitchCurves(aProxy, bProxy, out _, out BezierCurve aAligned, out BezierCurve bAligned);

            SwitchGeometry.RailLineCurves aThirdRails = SwitchGeometry.MakeTrackLineSegments(aAligned, ThirdRailGauge);
            SwitchGeometry.RailLineCurves bThirdRails = SwitchGeometry.MakeTrackLineSegments(bAligned, ThirdRailGauge);

            LineCurve aMiddle = DualGaugeSharedRailRegistry.SharesRightRail(aProxy.Segment)
                ? aThirdRails.left
                : aThirdRails.right;
            LineCurve bMiddle = DualGaugeSharedRailRegistry.SharesRightRail(bProxy.Segment)
                ? bThirdRails.left
                : bThirdRails.right;
            if (!suppressLegacyFixedRails)
            {
                CreateRailOutsideMeasuredOwnership(builder, aMiddle, stdGeometry.switchHome, Gauge.Standard, "ThirdA", root, node);
            }

            float narrowSplit = (stdGeometry.aPointRail.Length + stdGeometry.bPointRail.Length) * 0.5f;
            bMiddle.Split(Mathf.Min(narrowSplit, bMiddle.Length * 0.5f), out LineCurve narrowPointCurve, out LineCurve narrowClosure);
            GameObject narrowPoint = CreateDualPointRail(builder, narrowPointCurve, "ThirdBPoint", stdGeometry.switchHome, root);

            if (!suppressLegacyFixedRails)
            {
                CreateRailOutsideMeasuredOwnership(builder, narrowClosure, stdGeometry.switchHome, Gauge.Standard, "ThirdBClosure", root, node);
            }

            GameObject narrowDummy = new GameObject("ThirdBDummy");
            narrowDummy.transform.SetParent(root.transform, false);
            narrowDummy.transform.localPosition = narrowPoint.transform.localPosition;

            CalculatePointRotations(stdGeometry, out float stdNormalRot, out float stdReversedRot);
            root.AddComponent<SwitchPointRails>().Configure(node, stdNormalPoint, stdReversedPoint, stdNormalRot, stdReversedRot);
            root.AddComponent<SwitchPointRails>().Configure(node, narrowDummy, narrowPoint, 0f, stdReversedRot);

            CreateSwitchStand(builder, stdGeometry, node, root.transform);
            if (!suppressLegacyFixedRails)
            {
                CreateSwitchTies(builder, stdGeometry, root.transform, Gauge.Standard);
            }
        }

        private static void CreateDualGaugeNarrowSplitSwitchRailObjects(
            TrackObjectBuilder builder,
            SwitchGeometry stdGeometry,
            SegmentProxy aProxy,
            SegmentProxy bProxy,
            TrackNode node,
            Transform parent,
            string descriptorId)
        {
            GameObject root = CreateTrackRoot(builder, "sw-" + node.id, parent);
            bool suppressLegacyFixedRails =
                SpecialWorkHardwareRenderer.ShouldSuppressLegacySpecialWorkRails(node);
            bool renderPointHardware =
                SpecialWorkHardwareRenderer.CanRenderLegacyPointHardware(node);
            if (suppressLegacyFixedRails)
            {
                SpecialWorkHardwareRenderer.LogVanillaSuppression(
                    node,
                    descriptorId,
                    nameof(CreateDualGaugeNarrowSplitSwitchRailObjects),
                    vanillaSwitchObjects: 1,
                    vanillaRailObjects: renderPointHardware ? 7 : 5,
                    vanillaTieObjects: renderPointHardware ? 1 : 0);
            }

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
            if (!suppressLegacyFixedRails)
            {
                CreateRailOutsideMeasuredOwnership(builder, aStdRails.right, stdGeometry.switchHome, Gauge.Standard, "DualOuter", root, node);
            }

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
            if (!suppressLegacyFixedRails && branchOuterRail.Points.Any())
            {
                CreateRailOutsideMeasuredOwnership(builder, branchOuterRail, stdGeometry.switchHome, Gauge.Standard, "BranchOuter", root, node);
            }
            if (!suppressLegacyFixedRails && dualMiddleFromNode.Points.Any())
            {
                CreateRailOutsideMeasuredOwnership(builder, dualMiddleFromNode, stdGeometry.switchHome, Gauge.Standard, "DualMiddle", root, node);
            }
            if (!suppressLegacyFixedRails && leftSharedClosure.Points.Any())
            {
                CreateRailOutsideMeasuredOwnership(builder, leftSharedClosure, stdGeometry.switchHome, Gauge.Standard, "LeftSharedClosure", root, node);
            }
            if (!suppressLegacyFixedRails && branchRightClosure.Points.Any())
            {
                CreateRailOutsideMeasuredOwnership(builder, branchRightClosure, stdGeometry.switchHome, Gauge.Standard, "BranchRightClosure", root, node);
            }

            if (!renderPointHardware)
            {
                CreateSwitchStand(builder, stdGeometry, node, root.transform);
                return;
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
            if (!suppressLegacyFixedRails)
            {
                CreateSwitchTies(builder, stdGeometry, root.transform, Gauge.Standard);
            }
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
            BezierCurve bRoadbedCurve,
            string descriptorId)
        {
            GameObject container = CreateGeneratedObjectContainer(builder);
            container.transform.localPosition = geometry.switchHome;
            bool isGaugeSeparationControl = IsGaugeSeparationControlSwitch(node);
            if (isGaugeSeparationControl)
            {
                CreateGaugeSeparationControlSwitchObject(
                    builder,
                    geometry,
                    node,
                    aProxy,
                    bProxy,
                    container.transform,
                    descriptorId);
            }
            else if (IsVisibleGeneratedTransitionSwitch(node))
            {
                CreateTransitionSwitchObject(
                    builder,
                    geometry,
                    node,
                    aProxy.Curve,
                    aRoadbedCurve,
                    bProxy.Curve,
                    bRoadbedCurve,
                    container.transform,
                    descriptorId);
            }
            else
            {
                CreateSwitchObject(
                    builder,
                    geometry,
                    node,
                    aProxy.Curve,
                    aRoadbedCurve,
                    bProxy.Curve,
                    bRoadbedCurve,
                    container.transform,
                    descriptorId);
            }

            SpecialWorkHardwareRenderer.AddAdditionalHardware(builder, node, geometry, container.transform);
            SpecialWorkHardwareRenderer.LogBuiltObjectCounts(
                node,
                descriptorId,
                nameof(BuildSwitch),
                container.transform);
            container.SetActive(true);
            return container;
        }

        private static void CreateGaugeSeparationControlSwitchObject(
            TrackObjectBuilder builder,
            SwitchGeometry geometry,
            TrackNode node,
            SegmentProxy aProxy,
            SegmentProxy bProxy,
            Transform parent,
            string descriptorId)
        {
            bool aHidden = SpecialWorkTopologySynchronizer.IsHiddenControlSegment(aProxy.Segment);
            bool bHidden = SpecialWorkTopologySynchronizer.IsHiddenControlSegment(bProxy.Segment);
            if (aHidden == bHidden)
            {
                Main.Warn(
                    $"[Build] Gauge-separation switch '{node.id}' could not identify exactly one hidden control route; " +
                    "rendering control shell only.");
                CreateGaugeSeparationControlShell(
                    builder,
                    geometry,
                    node,
                    parent,
                    descriptorId,
                    nameof(CreateGaugeSeparationControlSwitchObject));
                return;
            }

            CreateGaugeSeparationControlShell(
                builder,
                geometry,
                node,
                parent,
                descriptorId,
                nameof(CreateGaugeSeparationControlSwitchObject));
        }

        private static void CreateGaugeSeparationControlShell(
            TrackObjectBuilder builder,
            SwitchGeometry geometry,
            TrackNode node,
            Transform parent,
            string descriptorId,
            string sourceBuilder)
        {
            GameObject root = CreateTrackRoot(builder, "sw-" + node.id, parent);
            SpecialWorkHardwareRenderer.LogVanillaSuppression(
                node,
                descriptorId,
                sourceBuilder,
                vanillaSwitchObjects: 1,
                vanillaRailObjects: 16,
                vanillaTieObjects: 1);
            CreateSwitchStand(builder, geometry, node, root.transform);
            if (!SpecialWorkHardwareRenderer.HasValidPlan(node))
            {
                CreateGaugeSeparationFallbackHardware(
                    builder,
                    node,
                    parent,
                    geometry.switchHome);
            }
            Main.Log(
                $"[Build] Gauge-separation switch '{node.id}' rendered control shell only; " +
                "measured special-work owns all turnout rails, blades, frogs, guards, and ties.");
        }

        private static void CreateGaugeSeparationFallbackHardware(
            TrackObjectBuilder builder,
            TrackNode ghostNode,
            Transform parent,
            Vector3 switchHome)
        {
            if (!TryResolveGaugeSeparationRailLayout(
                    ghostNode,
                    out GaugeSeparationRailLayout layout))
            {
                Main.Warn(
                    $"[Build] Gauge-separation switch '{ghostNode?.id ?? "<null>"}' " +
                    "could not resolve physical rail layout for fallback hardware.");
                return;
            }

            IReadOnlyList<GaugeSeparationFrogSite> sites = GaugeSeparationFrogSites(layout);
            if (sites.Count == 0)
            {
                Main.Warn(
                    $"[Build] Gauge-separation switch '{ghostNode.id}' " +
                    "found no procedural frog sites for fallback hardware.");
                return;
            }

            GameObject root = CreateTrackRoot(
                builder,
                "gauge-separation-special-work-" + ghostNode.id,
                parent);

            int frogIndex = 0;
            foreach (GaugeSeparationFrogSite site in sites.OrderByDescending(site => site.IsVee))
            {
                CreateGaugeSeparationFallbackFrog(
                    builder,
                    root,
                    ghostNode,
                    site,
                    switchHome,
                    "GaugeSeparationFrog-" + frogIndex++);
            }

            bool bladeCreated = TryCreateGaugeSeparationFallbackBlade(
                builder,
                root,
                ghostNode,
                layout,
                sites,
                switchHome);
            Main.Log(
                $"[Build] Gauge-separation fallback hardware '{ghostNode.id}': " +
                $"frogs={sites.Count}, blade={(bladeCreated ? 1 : 0)}.");
        }

        private static void CreateGaugeSeparationFallbackFrog(
            TrackObjectBuilder builder,
            GameObject root,
            TrackNode ghostNode,
            GaugeSeparationFrogSite site,
            Vector3 switchHome,
            string name)
        {
            Vector3 towardSwitch =
                ghostNode.transform.localPosition - site.Intersection.point;
            towardSwitch.y = 0f;
            if (towardSwitch.sqrMagnitude <= 0.0001f)
            {
                towardSwitch = site.Intersection.direction;
                towardSwitch.y = 0f;
            }

            towardSwitch = towardSwitch.sqrMagnitude > 0.0001f
                ? towardSwitch.normalized
                : Vector3.forward;

            if (site.IsVee)
            {
                LinePoint heelA = GaugeSeparationFallbackHeel(
                    site.RailA,
                    site.Intersection,
                    site.CutHalfLength,
                    towardSwitch);
                LinePoint heelB = GaugeSeparationFallbackHeel(
                    site.RailB,
                    site.Intersection,
                    site.CutHalfLength,
                    towardSwitch);
                LinePoint[] points =
                {
                    new LinePoint(heelA.point - switchHome, heelA.Rotation),
                    new LinePoint(
                        site.Intersection.point - switchHome,
                        Quaternion.LookRotation(towardSwitch, Vector3.up)),
                    new LinePoint(heelB.point - switchHome, heelB.Rotation)
                };
                CreateMeshObject(
                    builder,
                    BuildFrogMesh(points, Gauge.Standard),
                    name + "-Vee",
                    root);
                return;
            }

            CreateGaugeSeparationCrossingPointRails(
                builder,
                root,
                site.RailA,
                site.Intersection,
                site.CutHalfLength,
                switchHome,
                name + "-A");
            CreateGaugeSeparationCrossingPointRails(
                builder,
                root,
                site.RailB,
                site.Intersection,
                site.CutHalfLength,
                switchHome,
                name + "-B");
        }

        private static LinePoint GaugeSeparationFallbackHeel(
            LineCurve rail,
            LinePoint intersection,
            float cutHalfLength,
            Vector3 towardSwitch)
        {
            float center = Mathf.Clamp(
                rail.DistanceTo(intersection.point),
                0f,
                rail.Length);
            LinePoint before = rail.LinePointAtDistance(
                Mathf.Max(0f, center - cutHalfLength));
            LinePoint after = rail.LinePointAtDistance(
                Mathf.Min(rail.Length, center + cutHalfLength));
            Vector3 beforeDirection = before.point - intersection.point;
            Vector3 afterDirection = after.point - intersection.point;
            return Vector3.Dot(beforeDirection, towardSwitch)
                <= Vector3.Dot(afterDirection, towardSwitch)
                    ? before
                    : after;
        }

        private static void CreateGaugeSeparationCrossingPointRails(
            TrackObjectBuilder builder,
            GameObject root,
            LineCurve rail,
            LinePoint intersection,
            float cutHalfLength,
            Vector3 switchHome,
            string name)
        {
            float center = Mathf.Clamp(
                rail.DistanceTo(intersection.point),
                0f,
                rail.Length);
            float pointSetback = Mathf.Clamp(
                cutHalfLength * 0.22f,
                0.08f,
                0.24f);
            CreateGaugeSeparationTaperedRail(
                builder,
                root,
                SliceRail(
                    rail,
                    center - cutHalfLength,
                    center - pointSetback),
                switchHome,
                name + "-Before",
                taperAtStart: false);
            CreateGaugeSeparationTaperedRail(
                builder,
                root,
                SliceRail(
                    rail,
                    center + pointSetback,
                    center + cutHalfLength),
                switchHome,
                name + "-After",
                taperAtStart: true);
        }

        private static bool TryCreateGaugeSeparationFallbackBlade(
            TrackObjectBuilder builder,
            GameObject root,
            TrackNode ghostNode,
            GaugeSeparationRailLayout layout,
            IReadOnlyList<GaugeSeparationFrogSite> sites,
            Vector3 switchHome)
        {
            var candidates = new[]
            {
                (Rail: layout.NarrowLeft, Side: RailSide.Left),
                (Rail: layout.NarrowRight, Side: RailSide.Right)
            }
                .Select(candidate =>
                {
                    float tip = Mathf.Clamp(
                        candidate.Rail.DistanceTo(ghostNode.transform.localPosition),
                        0f,
                        candidate.Rail.Length);
                    LinePoint tipPoint = candidate.Rail.LinePointAtDistance(tip);
                    (LineCurve Stock, float Separation) stock =
                        ClosestGaugeSeparationStockRail(
                            tipPoint.point,
                            layout.StandardLeft,
                            layout.StandardRight);
                    float frogDistance = sites
                        .Where(site => site.RailA == candidate.Rail || site.RailB == candidate.Rail)
                        .Select(site => site.RailA == candidate.Rail
                            ? site.RailA.DistanceTo(site.Intersection.point)
                            : site.RailB.DistanceTo(site.Intersection.point))
                        .OrderBy(distance => Mathf.Abs(distance - tip))
                        .FirstOrDefault();
                    return (
                        candidate.Rail,
                        candidate.Side,
                        Tip: tip,
                        Stock: stock.Stock,
                        stock.Separation,
                        FrogDistance: frogDistance);
                })
                .Where(candidate => candidate.FrogDistance > 0f)
                .OrderBy(candidate => candidate.Separation)
                .ToArray();

            if (candidates.Length == 0)
            {
                return false;
            }

            var selected = candidates[0];
            float sign = selected.FrogDistance >= selected.Tip ? 1f : -1f;
            float rootDistance = Mathf.Clamp(
                selected.Tip + sign * 3.2f,
                0f,
                selected.Rail.Length);
            if (Mathf.Abs(rootDistance - selected.Tip) < 0.5f)
            {
                return false;
            }

            LineCurve bladeCurve = SliceRail(
                selected.Rail,
                selected.Tip,
                rootDistance);
            if (bladeCurve.Length < 0.5f)
            {
                return false;
            }

            GameObject blade = CreateGaugeSeparationPointBlade(
                builder,
                root,
                bladeCurve,
                switchHome,
                "GaugeSeparationBlade-" + selected.Side);
            GameObject dummy = new GameObject("GaugeSeparationBladeDummy");
            dummy.transform.SetParent(root.transform, false);
            dummy.transform.localPosition = blade.transform.localPosition;

            float openRotation = CalculateGaugeSeparationBladeOpenRotation(
                selected.Stock,
                bladeCurve);
            root.AddComponent<SwitchPointRails>().Configure(
                ghostNode,
                dummy,
                blade,
                0f,
                openRotation);
            return true;
        }

        private static (LineCurve Stock, float Separation) ClosestGaugeSeparationStockRail(
            Vector3 point,
            LineCurve left,
            LineCurve right)
        {
            float leftDistance = DistancePointToCurve(point, left);
            float rightDistance = DistancePointToCurve(point, right);
            return leftDistance <= rightDistance
                ? (left, leftDistance)
                : (right, rightDistance);
        }

        private static GameObject CreateGaugeSeparationPointBlade(
            TrackObjectBuilder builder,
            GameObject root,
            LineCurve worldCurve,
            Vector3 switchHome,
            string name)
        {
            LineCurve local = worldCurve
                .Offset(-switchHome)
                .Subdivide(0.08f);
            LinePoint[] points = local.Points.ToArray();
            Vector3 pivot = points.Last().point;
            LineCurve pivoted = new LineCurve(
                local.Offset(-pivot).Points.ToArray(),
                local.hand);
            float totalLength = Mathf.Max(local.Length, 0.06f);
            Mesh mesh = BuildStockRailMesh(
                pivoted,
                switchHome,
                Gauge.Standard,
                index =>
                {
                    float t = points.Length <= 1
                        ? 1f
                        : Mathf.Clamp01((float)index / (points.Length - 1));
                    return Mathf.Lerp(0.04f, 1f, t * t);
                });
            GameObject blade = CreateMeshObject(builder, mesh, name, root);
            blade.transform.localPosition = pivot;
            return blade;
        }

        private static float CalculateGaugeSeparationBladeOpenRotation(
            LineCurve stockRail,
            LineCurve bladeCurve)
        {
            Vector3 tip = bladeCurve.Head.point;
            Vector3 root = bladeCurve.Tail.point;
            Vector3 closedTipVector = tip - root;
            closedTipVector.y = 0f;
            if (closedTipVector.sqrMagnitude <= 0.0001f)
            {
                return 0f;
            }

            float stockTipDistance = Mathf.Clamp(
                stockRail.DistanceTo(tip),
                0f,
                stockRail.Length);
            float stockRootDistance = Mathf.Clamp(
                stockRail.DistanceTo(root),
                0f,
                stockRail.Length);
            Vector3 stockTip = stockRail.LinePointAtDistance(stockTipDistance).point;
            Vector3 stockRoot = stockRail.LinePointAtDistance(stockRootDistance).point;
            Vector3 awayFromStock = root - stockRoot;
            awayFromStock.y = 0f;
            if (awayFromStock.sqrMagnitude <= 0.0001f)
            {
                awayFromStock = tip - stockTip;
                awayFromStock.y = 0f;
            }

            if (awayFromStock.sqrMagnitude <= 0.0001f)
            {
                return 0f;
            }

            Vector3 openTip = stockTip
                + awayFromStock.normalized
                * (Gauge.Standard.HeadWidth + 0.05f);
            Vector3 openTipVector = openTip - root;
            openTipVector.y = 0f;
            return openTipVector.sqrMagnitude <= 0.0001f
                ? 0f
                : Vector3.SignedAngle(
                    closedTipVector,
                    openTipVector,
                    Vector3.up);
        }

        private static void CreateGaugeSeparationTaperedRail(
            TrackObjectBuilder builder,
            GameObject root,
            LineCurve worldCurve,
            Vector3 switchHome,
            string name,
            bool taperAtStart)
        {
            if (worldCurve.Points.Count() < 2 || worldCurve.Length < 0.06f)
            {
                return;
            }

            int pointCount = worldCurve.Points.Count();
            Mesh mesh = BuildStockRailMesh(
                worldCurve.Offset(-switchHome),
                switchHome,
                Gauge.Standard,
                index =>
                {
                    int tipIndex = taperAtStart ? 0 : pointCount - 1;
                    int shoulderIndex = taperAtStart ? 1 : pointCount - 2;
                    if (index == tipIndex)
                    {
                        return 0.04f;
                    }

                    return index == shoulderIndex ? 0.65f : 1f;
                });
            CreateMeshObject(builder, mesh, name, root);
        }

        private static LineCurve SliceRail(LineCurve rail, float start, float end)
        {
            float clampedStart = Mathf.Clamp(start, 0f, rail.Length);
            float clampedEnd = Mathf.Clamp(end, 0f, rail.Length);
            LineCurve curve = rail
                .Skip(Mathf.Min(clampedStart, clampedEnd), true)
                .Take(Mathf.Abs(clampedEnd - clampedStart));
            return clampedStart <= clampedEnd ? curve : curve.Reverse();
        }

        private static LineCurve GaugeSeparationSharedRail(
            GaugeSeparationRailLayout layout)
        {
            return DualGaugeSharedRailRegistry.SharesRightRail(layout.DualSegment)
                ? layout.DualRight
                : layout.DualLeft;
        }

        private static IEnumerable<(float Start, float End)> GaugeSeparationFrogCuts(
            LineCurve worldRail,
            TrackSegment sourceSegment)
        {
            if (!TryResolveGaugeSeparationRailLayout(sourceSegment, out GaugeSeparationRailLayout layout))
            {
                yield break;
            }

            foreach ((float Start, float End) cut in GaugeSeparationFrogCuts(worldRail, layout))
            {
                yield return cut;
            }
        }

        private static IEnumerable<(float Start, float End)> GaugeSeparationFrogCuts(
            LineCurve worldRail,
            TrackNode ghostNode)
        {
            if (!TryResolveGaugeSeparationRailLayout(ghostNode, out GaugeSeparationRailLayout layout))
            {
                yield break;
            }

            foreach ((float Start, float End) cut in GaugeSeparationFrogCuts(worldRail, layout))
            {
                yield return cut;
            }
        }

        private static IEnumerable<(float Start, float End)> GaugeSeparationFrogCuts(
            LineCurve worldRail,
            GaugeSeparationRailLayout layout)
        {
            foreach (GaugeSeparationFrogSite site in GaugeSeparationFrogSites(layout))
            {
                if (DistancePointToCurve(site.Intersection.point, worldRail)
                    > GaugeSeparationFrogMatchTolerance)
                {
                    continue;
                }

                float distance = Mathf.Clamp(
                    worldRail.DistanceTo(site.Intersection.point),
                    0f,
                    worldRail.Length);
                yield return (
                    Mathf.Max(0f, distance - site.CutHalfLength),
                    Mathf.Min(worldRail.Length, distance + site.CutHalfLength));
            }
        }

        internal static IReadOnlyList<GaugeSeparationFrogSite> GaugeSeparationFrogSites(
            TrackNode ghostNode)
        {
            return TryResolveGaugeSeparationRailLayout(
                ghostNode,
                out GaugeSeparationRailLayout layout)
                    ? GaugeSeparationFrogSites(layout)
                    : Array.Empty<GaugeSeparationFrogSite>();
        }

        private static IReadOnlyList<GaugeSeparationFrogSite> GaugeSeparationFrogSites(
            GaugeSeparationRailLayout layout)
        {
            foreach ((LineCurve standardRail, RailSide standardSide) in new[]
            {
                (layout.StandardLeft, RailSide.Left),
                (layout.StandardRight, RailSide.Right)
            })
            {
                var intersections = new Dictionary<RailSide, (LineCurve Rail, LinePoint Point)>();
                foreach ((LineCurve narrowRail, RailSide narrowSide) in new[]
                {
                    (layout.NarrowLeft, RailSide.Left),
                    (layout.NarrowRight, RailSide.Right)
                })
                {
                    if (NarrowGaugeSwitchGeometry.Intersects(
                        standardRail,
                        narrowRail,
                        1.5f,
                        out LinePoint intersection)
                        && ProceduralFrogAcuteAngle(
                            standardRail,
                            narrowRail,
                            intersection) >= 1f)
                    {
                        intersections[narrowSide] = (narrowRail, intersection);
                    }
                }

                // Resolve the arrangement by physical position so it remains
                // valid when authored segment directions are reversed:
                // farther from the switch node = upper V frog;
                // nearer to the switch node = double frog.
                if (intersections.Count != 2)
                {
                    continue;
                }

                var ordered = intersections
                    .Select(item => (
                        Side: item.Key,
                        Rail: item.Value.Rail,
                        Point: item.Value.Point,
                        Distance: Vector3.Distance(
                            layout.SourceNode.transform.localPosition,
                            item.Value.Point.point)))
                    .OrderBy(item => item.Distance)
                    .ToArray();
                var crossing = ordered[0];
                var vee = ordered[1];
                return new[]
                {
                    new GaugeSeparationFrogSite(
                        standardRail,
                        standardSide,
                        vee.Rail,
                        vee.Side,
                        vee.Point,
                        isVee: true),
                    new GaugeSeparationFrogSite(
                        standardRail,
                        standardSide,
                        crossing.Rail,
                        crossing.Side,
                        crossing.Point,
                        isVee: false)
                };
            }

            return Array.Empty<GaugeSeparationFrogSite>();
        }

        private static float ProceduralFrogAcuteAngle(
            LineCurve railA,
            LineCurve railB,
            LinePoint intersection)
        {
            Vector3 tangentA = railA.LinePointAtDistance(
                Mathf.Clamp(railA.DistanceTo(intersection.point), 0f, railA.Length)).direction;
            Vector3 tangentB = railB.LinePointAtDistance(
                Mathf.Clamp(railB.DistanceTo(intersection.point), 0f, railB.Length)).direction;
            tangentA.y = 0f;
            tangentB.y = 0f;
            float angle = Vector3.Angle(tangentA, tangentB);
            return Mathf.Min(angle, 180f - angle);
        }

        private static bool TryResolveGaugeSeparationRailLayout(
            TrackSegment sourceSegment,
            out GaugeSeparationRailLayout layout)
        {
            layout = null!;
            if (sourceSegment == null || Graph.Shared == null)
            {
                return false;
            }

            foreach (TrackNode sourceNode in Graph.Shared.Nodes.Where(
                SpecialWorkTopologySynchronizer.IsGaugeSeparationSourceNode))
            {
                if (TryResolveGaugeSeparationRailLayoutFromSource(sourceNode, out GaugeSeparationRailLayout candidate)
                    && (candidate.DualSegment == sourceSegment
                        || candidate.StandardSegment == sourceSegment
                        || candidate.NarrowSegment == sourceSegment))
                {
                    layout = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveGaugeSeparationRailLayout(
            TrackNode ghostNode,
            out GaugeSeparationRailLayout layout)
        {
            layout = null!;
            if (ghostNode == null
                || Graph.Shared == null
                || !GhostGraphSynchronizer.IsGeneratedGhostNodeId(ghostNode.id))
            {
                return false;
            }

            string sourceNodeId =
                ghostNode.id.Substring(GhostGraphSynchronizer.GeneratedNodePrefix.Length);
            return TryResolveGaugeSeparationRailLayoutFromSource(
                Graph.Shared.GetNode(sourceNodeId),
                out layout);
        }

        private static bool TryResolveGaugeSeparationRailLayoutFromSource(
            TrackNode sourceNode,
            out GaugeSeparationRailLayout layout)
        {
            layout = null!;
            if (sourceNode == null
                || Graph.Shared == null
                || !SpecialWorkTopologySynchronizer.IsGaugeSeparationSourceNode(sourceNode))
            {
                return false;
            }

            TrackSegment[] physical = Graph.Shared.SegmentsConnectedTo(sourceNode)
                .Where(segment =>
                    segment != null
                    && !NarrowGaugeManager.IsGeneratedGhost(segment)
                    && !SpecialWorkTopologySynchronizer.IsHiddenControlSegment(segment))
                .ToArray();
            TrackSegment? dual = physical.SingleOrDefault(NarrowGaugeManager.IsDualGauge);
            TrackSegment? standard = physical.SingleOrDefault(segment =>
                !NarrowGaugeManager.IsDualGauge(segment)
                && !NarrowGaugeManager.IsNarrowGauge(segment));
            TrackNode ghostNode = Graph.Shared.GetNode(
                GhostGraphSynchronizer.GetGhostNodeId(sourceNode.id));
            TrackSegment? narrow = ghostNode == null
                ? null
                : Graph.Shared.SegmentsConnectedTo(ghostNode)
                    .SingleOrDefault(IsVisibleRealNarrowOnlySegment);
            if (dual == null || standard == null || narrow == null)
            {
                return false;
            }

            SwitchGeometry.RailLineCurves dualStandard =
                SwitchGeometry.MakeTrackLineSegments(dual.Curve, Gauge.Standard);
            SwitchGeometry.RailLineCurves dualThird =
                SwitchGeometry.MakeTrackLineSegments(dual.Curve, ThirdRailGauge);
            LineCurve dualMiddle = DualGaugeSharedRailRegistry.SharesRightRail(dual)
                ? dualThird.left
                : dualThird.right;
            SwitchGeometry.RailLineCurves standardRails =
                SwitchGeometry.MakeTrackLineSegments(standard.Curve, Gauge.Standard);
            SwitchGeometry.RailLineCurves narrowRails =
                SwitchGeometry.MakeTrackLineSegments(narrow.Curve, ThreeFootGauge);

            layout = new GaugeSeparationRailLayout(
                sourceNode,
                dual,
                standard,
                narrow,
                dualStandard.left,
                dualMiddle,
                dualStandard.right,
                standardRails.left,
                standardRails.right,
                narrowRails.left,
                narrowRails.right);
            return true;
        }

        private static void CreateTransitionSwitchObject(
            TrackObjectBuilder builder,
            SwitchGeometry geometry,
            TrackNode node,
            BezierCurve aCurve,
            BezierCurve aRoadbedCurve,
            BezierCurve bCurve,
            BezierCurve bRoadbedCurve,
            Transform parent,
            string descriptorId)
        {
            CreateTransitionSwitchRailObjects(builder, geometry, node, parent, descriptorId);
            CreateMeshColliderObject(
                builder,
                BuildColliderMesh(aCurve.OffsetBy(-geometry.switchHome), ThreeFootGauge),
                "Collider-a",
                parent);
            CreateMeshColliderObject(
                builder,
                BuildColliderMesh(bCurve.OffsetBy(-geometry.switchHome), ThreeFootGauge),
                "Collider-b",
                parent);
            CreateRoadbed(builder, aRoadbedCurve, parent, TrackSegment.Style.Standard);
            CreateRoadbed(builder, bRoadbedCurve, parent, TrackSegment.Style.Standard);
        }

        private static void CreateTransitionSwitchRailObjects(
            TrackObjectBuilder builder,
            SwitchGeometry geometry,
            TrackNode node,
            Transform parent,
            string descriptorId)
        {
            GameObject root = CreateTrackRoot(builder, "sw-" + node.id, parent);
            if (SpecialWorkHardwareRenderer.ShouldSuppressLegacySpecialWorkRails(node))
            {
                SpecialWorkHardwareRenderer.LogVanillaSuppression(
                    node,
                    descriptorId,
                    nameof(CreateTransitionSwitchRailObjects),
                    vanillaSwitchObjects: 1,
                    vanillaRailObjects: 8,
                    vanillaTieObjects: 1);
                CreateSwitchStand(builder, geometry, node, root.transform);
                return;
            }

            CreateNonDuplicatePieces(geometry.leftStockRail, "StockL");
            CreateNonDuplicatePieces(geometry.rightStockRail, "StockR");
            CreateNonDuplicatePieces(geometry.aClosureRail, "ClosureA");
            CreateNonDuplicatePieces(geometry.bClosureRail, "ClosureB");

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

            void CreateNonDuplicatePieces(LineCurve curve, string name)
            {
                LineCurve[] visibleRails = VisibleDualRailsForGhostNode(node).ToArray();
                if (visibleRails.Length == 0)
                {
                    CreateMeshObject(
                        builder,
                        BuildStockRailMesh(curve, geometry.switchHome, ThreeFootGauge, _ => 1f),
                        name,
                        root);
                    return;
                }

                LineCurve worldCurve = curve.Offset(geometry.switchHome);
                var cuts = new List<(float Start, float End)>(
                    SpecialWorkHardwareRenderer.OwnershipCutsForNode(
                        worldCurve,
                        node));
                float sampleSpacing = Mathf.Min(DuplicateRailSampleSpacing, Mathf.Max(worldCurve.Length / 8f, 0.025f));
                bool inDuplicate = false;
                float duplicateStart = 0f;

                for (float distance = 0f; distance <= worldCurve.Length + sampleSpacing * 0.5f; distance += sampleSpacing)
                {
                    float clamped = Mathf.Min(distance, worldCurve.Length);
                    Vector3 point = worldCurve.LinePointAtDistance(clamped).point;
                    bool duplicate = visibleRails.Any(visible =>
                        DistancePointToCurve(point, visible) <= DuplicateRailTolerance);

                    if (duplicate && !inDuplicate)
                    {
                        duplicateStart = Mathf.Max(0f, clamped - sampleSpacing * 0.5f);
                        inDuplicate = true;
                    }
                    else if (!duplicate && inDuplicate)
                    {
                        cuts.Add((
                            duplicateStart,
                            Mathf.Min(worldCurve.Length, clamped + sampleSpacing * 0.5f)));
                        inDuplicate = false;
                    }
                }

                if (inDuplicate)
                {
                    cuts.Add((duplicateStart, worldCurve.Length));
                }

                float cursor = 0f;
                int pieceIndex = 0;
                foreach ((float start, float end) in MergeCutIntervals(cuts))
                {
                    CreatePiece(cursor, start);
                    cursor = Mathf.Max(cursor, end);
                }

                CreatePiece(cursor, curve.Length);

                void CreatePiece(float start, float end)
                {
                    if (end - start < 0.1f)
                    {
                        return;
                    }

                    LineCurve piece = curve.Skip(start, true).Take(end - start);
                    int pointCount = piece.Points.Count();
                    bool taperHead = start > 0.02f;
                    bool taperTail = end < curve.Length - 0.02f;
                    CreateMeshObject(
                        builder,
                        BuildStockRailMesh(
                            piece,
                            geometry.switchHome,
                            ThreeFootGauge,
                            index =>
                                (taperHead && index == 0)
                                || (taperTail && index == pointCount - 1)
                                    ? 0.12f
                                    : 1f),
                        name + "-" + pieceIndex++,
                        root);
                }
            }
        }

        private static bool CurveDuplicatesVisibleDualRail(
            TrackNode ghostNode,
            LineCurve localCurve,
            Vector3 switchHome)
        {
            if (ghostNode == null
                || localCurve == null
                || Graph.Shared == null
                || !GhostGraphSynchronizer.IsGeneratedGhostNodeId(ghostNode.id))
            {
                return false;
            }

            LineCurve worldCurve = localCurve.Offset(switchHome);
            return VisibleDualRailsForGhostNode(ghostNode).Any(visibleRail =>
                CurvesOverlap(worldCurve, visibleRail));
        }

        private static IEnumerable<LineCurve> VisibleDualRailsForGhostNode(TrackNode ghostNode)
        {
            if (ghostNode == null
                || Graph.Shared == null
                || string.IsNullOrEmpty(ghostNode.id)
                || !GhostGraphSynchronizer.IsGeneratedGhostNodeId(ghostNode.id))
            {
                yield break;
            }

            string sourceNodeId =
                ghostNode.id.Substring(GhostGraphSynchronizer.GeneratedNodePrefix.Length);
            TrackNode sourceNode = Graph.Shared.GetNode(sourceNodeId);
            if (sourceNode == null)
            {
                yield break;
            }

            foreach (TrackSegment dual in Graph.Shared.SegmentsConnectedTo(sourceNode)
                .Where(NarrowGaugeManager.IsDualGauge))
            {
                SwitchGeometry.RailLineCurves standard =
                    SwitchGeometry.MakeTrackLineSegments(dual.Curve, Gauge.Standard);
                SwitchGeometry.RailLineCurves third =
                    SwitchGeometry.MakeTrackLineSegments(dual.Curve, ThirdRailGauge);

                yield return standard.left;
                yield return DualGaugeSharedRailRegistry.SharesRightRail(dual)
                    ? third.left
                    : third.right;
                yield return standard.right;
            }
        }

        private static bool CurvesOverlap(LineCurve candidate, LineCurve visible)
        {
            if (candidate.Points.Count() < 2 || visible.Points.Count() < 2)
            {
                return false;
            }

            Vector3[] samples =
            {
                candidate.Head.point,
                candidate.LinePointAtDistance(candidate.Length * 0.5f).point,
                candidate.Tail.point
            };
            return samples.All(point => DistancePointToCurve(point, visible) <= 0.055f);
        }

        private static float DistancePointToCurve(Vector3 point, LineCurve curve)
        {
            float best = float.MaxValue;
            foreach ((int _, LineSegment segment) in curve.Segments)
            {
                Vector3 delta = segment.b.point - segment.a.point;
                if (delta.sqrMagnitude <= 0.000001f)
                {
                    best = Mathf.Min(best, Vector3.Distance(point, segment.a.point));
                    continue;
                }

                float t = Mathf.Clamp01(
                    Vector3.Dot(point - segment.a.point, delta) / delta.sqrMagnitude);
                best = Mathf.Min(
                    best,
                    Vector3.Distance(point, segment.a.point + delta * t));
            }

            return best;
        }

        private static void CreateSwitchObject(
            TrackObjectBuilder builder,
            SwitchGeometry geometry,
            TrackNode node,
            BezierCurve aCurve,
            BezierCurve aRoadbedCurve,
            BezierCurve bCurve,
            BezierCurve bRoadbedCurve,
            Transform parent,
            string descriptorId)
        {
            CreateSwitchRailObjects(builder, geometry, node, parent, descriptorId);
            CreateMeshColliderObject(builder, BuildColliderMesh(aCurve.OffsetBy(-geometry.switchHome), ThreeFootGauge), "Collider-a", parent);
            CreateMeshColliderObject(builder, BuildColliderMesh(bCurve.OffsetBy(-geometry.switchHome), ThreeFootGauge), "Collider-b", parent);
            CreateRoadbed(builder, aRoadbedCurve, parent, TrackSegment.Style.Standard);
            CreateRoadbed(builder, bRoadbedCurve, parent, TrackSegment.Style.Standard);
        }

        private static void CreateSwitchRailObjects(
            TrackObjectBuilder builder,
            SwitchGeometry geometry,
            TrackNode node,
            Transform parent,
            string descriptorId)
        {
            GameObject root = CreateTrackRoot(builder, "sw-" + node.id, parent);
            if (SpecialWorkHardwareRenderer.ShouldSuppressLegacySpecialWorkRails(node))
            {
                SpecialWorkHardwareRenderer.LogVanillaSuppression(
                    node,
                    descriptorId,
                    nameof(CreateSwitchRailObjects),
                    vanillaSwitchObjects: 1,
                    vanillaRailObjects: 10,
                    vanillaTieObjects: 1);
                CreateSwitchStand(builder, geometry, node, root.transform);
                return;
            }

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
            Gauge gauge,
            TrackSegment? sourceSegment = null,
            float tieLengthScale = 1f)
        {
            Vector3 endPoint = curve.EndPoint1;
            BezierCurve localCurve = curve.OffsetBy(-endPoint);

            GameObject root = CreateTrackRoot(builder, trackName, parent);
            root.transform.localPosition = endPoint;

            SwitchGeometry.RailLineCurves railCurves = SwitchGeometry.MakeTrackLineSegments(localCurve, gauge);
            if (sourceSegment == null)
            {
                CreateMeshObject(builder, BuildStockRailMesh(railCurves.left, endPoint, gauge, _ => 1f), "L", root);
                CreateMeshObject(builder, BuildStockRailMesh(railCurves.right, endPoint, gauge, _ => 1f), "R", root);
            }
            else
            {
                CreateRailMeshesWithFrogCuts(builder, railCurves.left, endPoint, sourceSegment, gauge, "L", root);
                CreateRailMeshesWithFrogCuts(builder, railCurves.right, endPoint, sourceSegment, gauge, "R", root);
            }

            CreateSegmentTies(
                builder,
                localCurve,
                tieSpacing,
                tieSpacingJitter,
                root.transform,
                gauge,
                sourceSegment,
                tieLengthScale);
            CreateMeshColliderObject(builder, BuildColliderMesh(localCurve, gauge), "Collider", root.transform);
        }

        private static void CreateSegmentTies(
            TrackObjectBuilder builder,
            BezierCurve curve,
            float spacing,
            float tieSpacingJitter,
            Transform parent,
            Gauge gauge,
            TrackSegment? sourceSegment = null,
            float tieLengthScale = 1f)
        {
            var ties = new List<PointDirection>();
            var tiePlates = new List<PointDirection>();

            float jitter = tieSpacingJitter / 4f;
            LineCurve lineCurve = new LineCurve(curve.Approximate(1.000005f, 0.5f, 16, 40f), Hand.Left);
            (float Start, float End)[] tieCuts = sourceSegment == null
                ? Array.Empty<(float Start, float End)>()
                : MergeCutIntervals(
                    SpecialWorkHardwareRenderer.TieOwnershipCuts(
                        lineCurve.Offset(parent.localPosition),
                        sourceSegment))
                    .ToArray();
            LogSpecialWorkTieCuts(sourceSegment, tieCuts);
            float tieCount = Mathf.Round(lineCurve.Length / spacing);
            if (tieCount == 0f)
            {
                return;
            }

            spacing = lineCurve.Length / tieCount;
            var cursor = lineCurve.CursorAtHead().Skip(spacing / 2f);

            for (int i = 0; i < tieCount; i++)
            {
                float tieDistance = spacing / 2f + spacing * i;
                LinePoint linePoint = cursor.LinePoint();
                Vector3 point = linePoint.point;
                Quaternion rotation = linePoint.Rotation;

                if (IsDistanceInsideAnyCut(tieDistance, tieCuts))
                {
                    cursor = cursor.Skip(spacing);
                    continue;
                }

                Vector3 tieCenter = point + rotation * Vector3.left * UnityEngine.Random.Range(-jitter, jitter);
                ties.Add(new PointDirection(tieCenter, rotation));

                float plateOffset = gauge.Inside / 2f + gauge.HeadWidth / 2f;
                tiePlates.Add(new PointDirection(point + rotation * Vector3.right * plateOffset, rotation));
                tiePlates.Add(new PointDirection(point + rotation * Vector3.left * plateOffset, rotation));

                cursor = cursor.Skip(spacing);
            }

            Quaternion tieRotationOffset = Quaternion.Euler(90f, 90f, 0f) * Quaternion.Euler(180f, 0f, 0f);
            Matrix4x4[] tieMatrices = new Matrix4x4[ties.Count];
            Vector3 tieScale = new Vector3(1f, tieLengthScale, 1f);
            for (int i = 0; i < ties.Count; i++)
            {
                PointDirection pointDirection = ties[i];
                float variation = Mathf.PingPong(pointDirection.Position.magnitude, 0.01f);
                Vector3 drop = (-(gauge.RailHeight + 0.1f) + variation) * (pointDirection.Rotation * Vector3.up);
                tieMatrices[i] = Matrix4x4.TRS(pointDirection.Position + drop, pointDirection.Rotation * tieRotationOffset, tieScale);
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

        private static void LogSpecialWorkTieCuts(
            TrackSegment? sourceSegment,
            IReadOnlyList<(float Start, float End)> cuts)
        {
            if (sourceSegment == null || cuts.Count == 0)
            {
                return;
            }

            Main.Log(
                $"[SpecialWorkTieClip] segment={sourceSegment.id} cuts=" +
                string.Join(",", cuts.Select(cut => $"{cut.Start:0.000}-{cut.End:0.000}")));
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

        internal static void CreateSpecialWorkTies(
            TrackObjectBuilder builder,
            SpecialWorkAnalysis analysis,
            Transform parent,
            Vector3 switchHome)
        {
            SpecialWorkMeshPlan? plan = analysis?.MeshPlan;
            WheelPath? guide = analysis?.WheelPaths.FirstOrDefault(path =>
                string.Equals(path.RouteId, "standard-through", StringComparison.OrdinalIgnoreCase))
                ?? analysis?.WheelPaths.FirstOrDefault();
            if (plan?.IsValid != true || guide == null || plan.WorkIntervals.Count == 0)
            {
                Main.Log(
                    $"[SpecialWorkTies] Skipped: valid={plan?.IsValid} guide={guide?.RouteId ?? "<null>"} " +
                    $"intervals={plan?.WorkIntervals.Count ?? 0}");
                return;
            }

            float start = guide.Centerline.Length;
            float end = 0f;
            foreach (RailWorkInterval work in plan.WorkIntervals)
            {
                start = Mathf.Min(
                    start,
                    guide.Centerline.DistanceTo(
                        work.Rail.Curve.LinePointAtDistance(work.StartDistance).point));
                end = Mathf.Max(
                    end,
                    guide.Centerline.DistanceTo(
                        work.Rail.Curve.LinePointAtDistance(work.EndDistance).point));
            }

            const float spacing = 0.55f;
            start = Mathf.Clamp(start - spacing, 0f, guide.Centerline.Length);
            end = Mathf.Clamp(end + spacing, start, guide.Centerline.Length);
            Main.Log(
                $"[SpecialWorkTies] guide={guide.RouteId} start={start:0.000} end={end:0.000} " +
                $"span={end - start:0.000} intervals={plan.WorkIntervals.Count} switchHome={switchHome}");
            if (end - start < 0.55f)
            {
                Main.Log("[SpecialWorkTies] Span too short, skipping ties.");
                return;
            }

            float tieCount = Mathf.Max(1f, Mathf.Round((end - start) / spacing));
            float measuredSpacing = (end - start) / tieCount;
            float normalizedWidth = Gauge.Standard.Inside + 1f;
            var ties = new List<Matrix4x4>();
            for (int index = 0; index < tieCount; index++)
            {
                float distance = start + measuredSpacing * (index + 0.5f);
                LinePoint centerPoint = guide.Centerline.LinePointAtDistance(distance);
                Vector3 right = centerPoint.Rotation * Vector3.right;
                right.y = 0f;
                if (right.sqrMagnitude <= 0.0001f)
                {
                    right = Vector3.Cross(Vector3.up, centerPoint.direction);
                }

                right.Normalize();
                float minimumOffset = 0f;
                float maximumOffset = 0f;
                foreach (RailWorkInterval work in plan.WorkIntervals)
                {
                    float railDistance = work.Rail.Curve.DistanceTo(centerPoint.point);
                    if (railDistance < work.StartDistance - 0.4f
                        || railDistance > work.EndDistance + 0.4f)
                    {
                        continue;
                    }

                    Vector3 railPoint = work.Rail.Curve.LinePointAtDistance(railDistance).point;
                    float offset = Vector3.Dot(railPoint - centerPoint.point, right);
                    minimumOffset = Mathf.Min(minimumOffset, offset);
                    maximumOffset = Mathf.Max(maximumOffset, offset);
                }

                float middleOffset = (minimumOffset + maximumOffset) * 0.5f;
                float tieWidth = maximumOffset - minimumOffset + 1f;
                if (tieWidth < normalizedWidth)
                {
                    middleOffset = 0f;
                    tieWidth = normalizedWidth;
                }

                ties.Add(CreateTieMatrix(
                    centerPoint.point + right * middleOffset - switchHome,
                    centerPoint.direction,
                    tieWidth / normalizedWidth,
                    Gauge.Standard));
            }

            Main.Log($"[SpecialWorkTies] Created {ties.Count} ties.");
            CreateInstancedMeshDrawer(
                builder,
                ties.ToArray(),
                switchHome,
                PrefabInstancer.Prefab.Tie,
                parent.gameObject);
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

        private static bool IsInvalidSourceNodeNarrowBranchSwitch(
            TrackNode node,
            out TrackSegment narrowBranch)
        {
            narrowBranch = null!;
            if (node == null
                || Graph.Shared == null
                || NarrowGaugeManager.IsGeneratedGhostNode(node))
            {
                return false;
            }

            TrackSegment[] connected = Graph.Shared.SegmentsConnectedTo(node)
                .Where(segment =>
                    segment != null
                    && !NarrowGaugeManager.IsGeneratedGhost(segment)
                    && !SpecialWorkTopologySynchronizer.IsHiddenControlSegment(segment))
                .ToArray();

            if (!connected.Any(NarrowGaugeManager.IsDualGauge))
            {
                return false;
            }

            narrowBranch = connected.FirstOrDefault(segment =>
                NarrowGaugeManager.IsNarrowGauge(segment)
                && !NarrowGaugeManager.IsDualGauge(segment));
            return narrowBranch != null;
        }

        private static void WarnSourceNodeNarrowBranchSwitch(TrackNode node, TrackSegment narrowBranch)
        {
            string nodeId = node?.id ?? string.Empty;
            if (string.IsNullOrWhiteSpace(nodeId)
                || !WarnedSourceNodeNarrowBranchSwitches.Add(nodeId))
            {
                return;
            }

            Main.Warn(
                $"[Build] Switch '{nodeId}' has narrow-only segment '{narrowBranch?.id ?? "<null>"}' " +
                $"attached to the source node. Expected that branch to touch " +
                $"'{GhostGraphSynchronizer.GetGhostNodeId(nodeId)}' or an explicit transition segment. " +
                "Refusing generated dual-gauge switch build for this invalid topology.");
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

        internal static Mesh BuildFrogMesh(LinePoint[] points, Gauge gauge)
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
