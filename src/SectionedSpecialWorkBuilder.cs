using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using FUSE.Runtime.API;
using Track;
using UnityEngine;

namespace NarrowGaugeMod
{
    internal static class SectionedSpecialWorkBuilder
    {
        private const float MinimumPieceLength = 0.35f;
        private const float WorkEnvelopeMargin = 3.0f;
        private const float BladeSampleSpacing = 0.1f;
        private const float BladeRootSeparation = 0.18f;
        private const float MaximumBladeLength = 7f;
        private const float BaseGamePointRailFrogCutoff = 0.45f;
        private const float BaseGamePointClosureSplitRatio = 0.5f;
        private const float RailHeadWidth = 0.076f;
        private const float FlangewayWidth = 0.05f;
        private const float MinimumFrogSetback = 0.16f;
        private const float MaximumFrogSetback = 2.5f;
        private const float CorridorTolerance = 0.085f;
        private const float MinimumFrogAngle = 3f;

        private static readonly SpecialWorkGeometryParameters Parameters =
            new SpecialWorkGeometryParameters(
                railHeadWidth: RailHeadWidth,
                flangewayWidth: FlangewayWidth,
                minimumFrogSetback: MinimumFrogSetback,
                maximumFrogSetback: MaximumFrogSetback,
                guardCenterOffset: RailHeadWidth + FlangewayWidth,
                guardLeadLength: 0.9f,
                guardTrailLength: 0.9f,
                bladeDivergenceThreshold: 0.045f,
                bladeRootSeparation: BladeRootSeparation,
                maximumBladeLength: MaximumBladeLength);

        private static bool IsDualNarrowBranchPreset(SpecialWorkDefinition definition)
        {
            return string.Equals(
                definition.Preset.Id,
                SpecialWorkPresetIds.DualNarrowBranch,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDualBothDivergePreset(SpecialWorkDefinition definition)
        {
            return string.Equals(
                definition.Preset.Id,
                SpecialWorkPresetIds.DualBothDiverge,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDualSplitPreset(SpecialWorkDefinition definition)
        {
            return string.Equals(
                definition.Preset.Id,
                SpecialWorkPresetIds.DualSplit,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDualStandardBranchPreset(SpecialWorkDefinition definition)
        {
            return string.Equals(
                definition.Preset.Id,
                SpecialWorkPresetIds.DualStandardBranch,
                StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryAnalyze(
            Graph graph,
            SpecialWorkDefinition definition,
            out SpecialWorkAnalysis analysis)
        {
            analysis = null!;
            if (definition.Preset.Category != SpecialWorkCategory.DualGauge
                || !definition.Preset.SupportsGhostGraph)
            {
                return false;
            }

            SpecialWorkBuildResult result = BuildSectionedDualGaugeSpecialWork(graph, definition);
            analysis = new SpecialWorkAnalysis(
                definition,
                result.WheelPaths,
                result.Rails,
                result.Shared,
                result.Intersections,
                result.MeshPlan.Frogs,
                Array.Empty<GuardRailCandidate>(),
                Array.Empty<SwitchBladeCandidate>(),
                result.Validation.Issues,
                result.ProjectionFrame,
                result.MeshPlan);
            return true;
        }

        private static SpecialWorkBuildResult BuildSectionedDualGaugeSpecialWork(
            Graph graph,
            SpecialWorkDefinition definition)
        {
            WheelPath[] wheelPaths = BuildWheelPaths(definition).ToArray();
            RailCenterline[] rails = BuildPhysicalRails(
                graph,
                definition,
                wheelPaths).ToArray();
            RailIntersectionPrototypeResult prototype =
                RailIntersectionPrototype.Analyze(graph, definition, rails);
            SharedRailInterval[] shared = prototype.Shared.ToArray();

            var cuts = new List<RailCut>();
            var suppressions = new List<RailSuppressionInterval>();
            SwitchBladePlan[] blades = DeduplicateBlades(
                BuildDualNarrowBranchBlades(
                    graph,
                    definition,
                    wheelPaths,
                    rails,
                    shared,
                    prototype.Intersections));
            AddBladeCutsAndSuppressions(blades, cuts, suppressions);

            FrogCandidate[] frogs =
                BuildAcceptedFrogs(
                    graph,
                    definition,
                    rails,
                    shared,
                    prototype.Intersections,
                    blades).ToArray();
            if (IsDualBothDivergePreset(definition))
            {
                frogs = AddMissingCrossFamilyCrossingFrogs(rails, frogs);
            }
            if (SpecialWorkHardwareProfileCatalog.ShouldUseNoveSplitFrogCatalog(definition))
            {
                frogs = AddNoveCatalogCrossingFrog(rails, frogs);
            }

            AddSharedSuppressions(definition, rails, shared, blades, frogs, cuts, suppressions);
            AddCrossFamilySharedSuppressions(definition, rails, blades, frogs, cuts, suppressions);
            AddBladeCorridorSuppressions(definition, rails, blades, cuts, suppressions);
            AddFrogSuppressions(rails, frogs, cuts, suppressions);
            if (IsDualBothDivergePreset(definition))
            {
                AddCollapsedVeeFrogGaps(
                    rails,
                    prototype.Intersections,
                    frogs,
                    blades,
                    cuts,
                    suppressions);
            }

            RailWorkInterval[] workIntervals =
                BuildWorkIntervals(definition, rails, shared, frogs, blades).ToArray();
            SuppressDualBothDivergeFrogDuplicate(
                definition,
                rails,
                workIntervals,
                frogs,
                cuts,
                suppressions);
            RailRoleSection[] sections =
                BuildRoleSections(
                    definition,
                    rails,
                    shared,
                    workIntervals,
                    cuts,
                    suppressions,
                    frogs,
                    blades).ToArray();
            ApplySections(rails, sections, suppressions);

            RailOwnershipPlan ownershipPlan =
                new RailOwnershipPlan(rails, sections, suppressions);
            RailOwnershipInterval[] ownershipIntervals =
                sections
                    .Where(section => CanRenderRole(section.Role))
                    .Select((section, index) => new RailOwnershipInterval(
                        "section-owner:" + index,
                        section.Rail,
                        section.OwnerRouteId,
                        section.OwnerFamily,
                        section.Role,
                        PieceKindForRole(section.Role),
                        section.StartDistance,
                        section.EndDistance,
                        section.Id))
                    .ToArray();
            RailPiece[] fixedPieces = BuildFixedPieces(sections).ToArray();
            RailPiece[] frogPieces = BuildFrogPieces(frogs).ToArray();
            WingRailPlan[] wings = BuildWingRails(frogs).ToArray();
            GuardRailPlan[] guards = BuildGuardRails(definition, rails, frogs, blades).ToArray();
            GeometryDebugLabel[] labels =
                BuildDebugLabels(
                    rails,
                    sections,
                    suppressions,
                    cuts,
                    fixedPieces,
                    frogPieces,
                    wings,
                    guards,
                    blades,
                    frogs).ToArray();
            string[] issues = ValidateSectionedDualGaugeSpecialWork(
                graph,
                definition,
                rails,
                sections,
                suppressions,
                fixedPieces,
                frogPieces,
                wings,
                guards,
                shared,
                cuts,
                frogs,
                blades).ToArray();

            var meshPlan = new SpecialWorkMeshPlan(
                Parameters,
                workIntervals,
                ownershipIntervals,
                ownershipPlan,
                fixedPieces,
                cuts,
                frogs,
                frogPieces,
                wings,
                guards,
                blades,
                labels,
                issues);
            var validation = new SpecialWorkValidationResult(issues);
            var topology = new NativeTopologyPlan(
                definition.NativeSwitchNodeIds,
                ValidateTopology(graph, definition));
            return new SpecialWorkBuildResult(
                wheelPaths,
                rails,
                shared,
                prototype.Intersections,
                meshPlan,
                prototype.Frame,
                validation,
                topology);
        }

        private static IEnumerable<WheelPath> BuildWheelPaths(SpecialWorkDefinition definition)
        {
            foreach (LogicalRoute route in definition.Routes)
            {
                Gauge gauge = route.Family == GaugeGraphFamily.Narrow
                    ? NarrowGaugeTrackBuilder.ThreeFootGauge
                    : Gauge.Standard;
                float flangeGuideOffset = Mathf.Max(
                    0f,
                    gauge.Inside / 2f - Gauge.Standard.HeadWidth * 0.5f);

                yield return new WheelPath(
                    "wheel:" + route.Id,
                    route.Id,
                    route.Family,
                    FindNearestPort(definition, route.Centerline.Head.point, route.Family),
                    FindNearestPort(definition, route.Centerline.Tail.point, route.Family),
                    route.Centerline,
                    route.Centerline.Parallel(-flangeGuideOffset, Hand.Left),
                    route.Centerline.Parallel(flangeGuideOffset, Hand.Right),
                    route.Id + ":left",
                    route.Id + ":right",
                    route.SwitchGroupId,
                    route.RequiredStateId);
            }
        }

        private static IEnumerable<RailCenterline> BuildPhysicalRails(
            Graph graph,
            SpecialWorkDefinition definition,
            IEnumerable<WheelPath> wheelPaths)
        {
            WheelPath[] paths = wheelPaths.ToArray();
            bool isDualGauge = definition.Preset.Category == SpecialWorkCategory.DualGauge;
            RailSide? sharedSide = isDualGauge
                ? DetectSharedSide(definition)
                : null;

            foreach (WheelPath path in paths)
            {
                if (isDualGauge
                    && path.Family == GaugeGraphFamily.Narrow
                    && sharedSide.HasValue)
                {
                    WheelPath? standardPair = FindMatchingStandardRoute(path, paths);
                    if (standardPair != null)
                    {
                        foreach (RailCenterline rail in BuildNarrowRailsFromStandardCenterline(
                            path, standardPair.Centerline, sharedSide.Value))
                        {
                            yield return rail;
                        }

                        continue;
                    }
                }

                Gauge gauge = path.Family == GaugeGraphFamily.Narrow
                    ? NarrowGaugeTrackBuilder.ThreeFootGauge
                    : Gauge.Standard;
                LineCurve leftCurve = path.Centerline.Parallel(-gauge.Inside / 2f, Hand.Left);
                LineCurve rightCurve = path.Centerline.Parallel(gauge.Inside / 2f, Hand.Right);
                yield return new RailCenterline(
                    path.LeftRailId,
                    path.Family,
                    RailSide.Left,
                    leftCurve,
                    new[] { path.RouteId },
                    wheelPathId: path.Id,
                    startPortId: path.StartPortId,
                    endPortId: path.EndPortId);
                yield return new RailCenterline(
                    path.RightRailId,
                    path.Family,
                    RailSide.Right,
                    rightCurve,
                    new[] { path.RouteId },
                    wheelPathId: path.Id,
                    startPortId: path.StartPortId,
                    endPortId: path.EndPortId);
            }
        }

        private static RailSide? DetectSharedSide(SpecialWorkDefinition definition)
        {
            foreach (LogicalRoute route in definition.Routes)
            {
                foreach (string segmentId in route.SourceSegmentIds)
                {
                    TrackSegment? segment = TrackAPI.GetSegment(segmentId);
                    if (segment != null && NarrowGaugeManager.IsDualGauge(segment))
                    {
                        return DualGaugeSharedRailRegistry.SharesRightRail(segment)
                            ? RailSide.Right
                            : RailSide.Left;
                    }
                }
            }

            return null;
        }

        private static WheelPath? FindMatchingStandardRoute(
            WheelPath narrowPath,
            IReadOnlyList<WheelPath> allPaths)
        {
            string narrowRouteId = narrowPath.RouteId ?? string.Empty;
            string suffix = narrowRouteId.IndexOf('-') >= 0
                ? narrowRouteId.Substring(narrowRouteId.IndexOf('-'))
                : string.Empty;
            string standardRouteId = "standard" + suffix;
            return allPaths.FirstOrDefault(path =>
                path.Family == GaugeGraphFamily.Standard
                && string.Equals(path.RouteId, standardRouteId, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<RailCenterline> BuildNarrowRailsFromStandardCenterline(
            WheelPath narrowPath,
            LineCurve standardCenterline,
            RailSide sharedSide)
        {
            float stdHalf = Gauge.Standard.Inside / 2f;
            float thirdRailHalf = NarrowGaugeTrackBuilder.ThirdRailGaugeInside / 2f;

            LineCurve sharedCurve = sharedSide == RailSide.Right
                ? standardCenterline.Parallel(stdHalf, Hand.Right)
                : standardCenterline.Parallel(-stdHalf, Hand.Left);
            LineCurve thirdCurve = sharedSide == RailSide.Right
                ? standardCenterline.Parallel(-thirdRailHalf, Hand.Left)
                : standardCenterline.Parallel(thirdRailHalf, Hand.Right);

            yield return new RailCenterline(
                narrowPath.LeftRailId,
                narrowPath.Family,
                RailSide.Left,
                sharedSide == RailSide.Right ? thirdCurve : sharedCurve,
                new[] { narrowPath.RouteId },
                wheelPathId: narrowPath.Id,
                startPortId: narrowPath.StartPortId,
                endPortId: narrowPath.EndPortId);
            yield return new RailCenterline(
                narrowPath.RightRailId,
                narrowPath.Family,
                RailSide.Right,
                sharedSide == RailSide.Right ? sharedCurve : thirdCurve,
                new[] { narrowPath.RouteId },
                wheelPathId: narrowPath.Id,
                startPortId: narrowPath.StartPortId,
                endPortId: narrowPath.EndPortId);
        }

        private static IEnumerable<SwitchBladePlan> BuildDualNarrowBranchBlades(
            Graph graph,
            SpecialWorkDefinition definition,
            IReadOnlyList<WheelPath> wheelPaths,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<SharedRailInterval> shared,
            IReadOnlyList<RailIntersection> intersections)
        {
            foreach (BladeSpec spec in BuildBladeSpecs(graph, definition, rails, intersections))
            {
                RailCenterline? movable = FindRail(rails, spec.MovableRouteId, spec.MovableSide);
                RailCenterline? stock = FindRail(rails, spec.StockRouteId, spec.StockSide);
                LogicalRoute? movableRoute = definition.Routes.FirstOrDefault(route =>
                    string.Equals(route.Id, spec.MovableRouteId, StringComparison.OrdinalIgnoreCase));
                WheelPath? movablePath = wheelPaths.FirstOrDefault(path =>
                    string.Equals(path.RouteId, spec.MovableRouteId, StringComparison.OrdinalIgnoreCase));
                WheelPath? stockPath = wheelPaths.FirstOrDefault(path =>
                    string.Equals(path.RouteId, spec.StockRouteId, StringComparison.OrdinalIgnoreCase));
                if (movable == null
                    || stock == null
                    || movableRoute == null
                    || movablePath == null
                    || stockPath == null)
                {
                    continue;
                }

                string switchGroupId = movableRoute.SwitchGroupId ?? "narrow";
                TrackNode? switchNode = definition.SwitchGroups
                    .Where(group => string.Equals(
                        group.Id,
                        switchGroupId,
                        StringComparison.OrdinalIgnoreCase))
                    .SelectMany(group => group.NativeNodeIds)
                    .Select(graph.GetNode)
                    .FirstOrDefault(node => node != null);
                Vector3 switchPoint = switchNode != null
                    ? switchNode.transform.localPosition
                    : movable.Curve.Head.point;
                bool foundBlade = TryFindBladeDistances(
                    stock,
                    movable,
                    switchPoint,
                    intersections,
                    out float tip,
                    out float root);
                if (!foundBlade)
                {
                    continue;
                }

                float switchDist = movable.Curve.DistanceTo(switchPoint);
                bool bladeExtendsForward = root > switchDist;
                LineCurve closureCurve = bladeExtendsForward
                    ? Slice(movable.Curve, root, movable.Curve.Length)
                    : Slice(movable.Curve, 0f, tip);

                yield return new SwitchBladePlan(
                    "v2-blade:" + spec.Label,
                    switchNode?.id,
                    switchGroupId,
                    stock,
                    movable,
                    tip,
                    root,
                    Slice(movable.Curve, tip, root),
                    closureCurve);
            }
        }

        private static void AddBladeCutsAndSuppressions(
            IReadOnlyList<SwitchBladePlan> blades,
            ICollection<RailCut> cuts,
            ICollection<RailSuppressionInterval> suppressions)
        {
            foreach (SwitchBladePlan blade in blades)
            {
                AddCut(cuts, blade.MovableRail, blade.TipDistance, blade.RootDistance,
                    RailCutKind.SwitchBlade, blade.Id);
                AddSuppression(suppressions, blade.MovableRail, blade.TipDistance,
                    blade.RootDistance, "movable blade rendered separately");
            }
        }

        private static SwitchBladePlan[] DeduplicateBlades(
            IEnumerable<SwitchBladePlan> blades)
        {
            var deduped = new List<SwitchBladePlan>();
            foreach (SwitchBladePlan blade in blades)
            {
                if (!deduped.Any(existing =>
                    CurveOverlapLength(existing.BladeCurve, blade.BladeCurve) > 0.2f))
                {
                    deduped.Add(blade);
                }
            }

            return deduped.ToArray();
        }

        private static IEnumerable<BladeSpec> BuildBladeSpecs(
            Graph graph,
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<RailIntersection> intersections)
        {
            if (SpecialWorkTruthTableCatalog.TryGet(
                    definition.Preset.Id,
                    rails,
                    intersections,
                    out TurnoutTruthTable truth)
                && truth.Blades.Length > 0)
            {
                Main.Log($"[BladeSpecs] Using truth table '{truth.Id}' blades ({truth.Blades.Length})");
                foreach (TruthBlade blade in truth.Blades)
                {
                    if (Enum.TryParse(blade.MovableSide, ignoreCase: true, out RailSide movSide)
                        && Enum.TryParse(blade.StockSide, ignoreCase: true, out RailSide stkSide))
                    {
                        yield return new BladeSpec(
                            blade.Label,
                            blade.MovableRouteId,
                            movSide,
                            blade.StockRouteId,
                            stkSide);
                    }
                }

                yield break;
            }

            foreach (SpecialWorkSwitchGroup group in definition.SwitchGroups)
            {
                LogicalRoute? normalRoute = definition.Routes.FirstOrDefault(route =>
                    string.Equals(route.SwitchGroupId, group.Id, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(route.RequiredStateId, "normal", StringComparison.OrdinalIgnoreCase));
                LogicalRoute? reversedRoute = definition.Routes.FirstOrDefault(route =>
                    string.Equals(route.SwitchGroupId, group.Id, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(route.RequiredStateId, "reversed", StringComparison.OrdinalIgnoreCase));

                if (normalRoute == null || reversedRoute == null)
                {
                    continue;
                }

                TrackNode? switchNode = group.NativeNodeIds
                    .Select(graph.GetNode)
                    .FirstOrDefault(node => node != null);
                Vector3 switchPoint = switchNode != null
                    ? switchNode.transform.localPosition
                    : Vector3.zero;

                RailCenterline? normalLeft = FindRail(rails, normalRoute.Id, RailSide.Left);
                RailCenterline? reversedLeft = FindRail(rails, reversedRoute.Id, RailSide.Left);
                bool leftHandTurnout = false;
                if (normalLeft != null && reversedLeft != null)
                {
                    float normalDist = normalLeft.Curve.DistanceTo(switchPoint);
                    float reversedDist = reversedLeft.Curve.DistanceTo(switchPoint);
                    Vector3 normalDir = normalLeft.Curve.LinePointAtDistance(
                        Mathf.Min(normalLeft.Curve.Length, normalDist + 2f)).point
                        - normalLeft.Curve.LinePointAtDistance(normalDist).point;
                    Vector3 reversedDir = reversedLeft.Curve.LinePointAtDistance(
                        Mathf.Min(reversedLeft.Curve.Length, reversedDist + 2f)).point
                        - reversedLeft.Curve.LinePointAtDistance(reversedDist).point;
                    normalDir.y = 0f;
                    reversedDir.y = 0f;
                    leftHandTurnout = Vector3.Cross(normalDir, reversedDir).y > 0f;
                }

                foreach (RailSide side in new[] { RailSide.Left, RailSide.Right })
                {
                    RailCenterline? normalRail = FindRail(rails, normalRoute.Id, side);
                    RailCenterline? reversedRail = FindRail(rails, reversedRoute.Id, side);
                    if (normalRail == null || reversedRail == null)
                    {
                        continue;
                    }

                    bool normalIsMovable = (side == RailSide.Left) != leftHandTurnout;

                    yield return new BladeSpec(
                        group.Id + ":" + side,
                        normalIsMovable ? normalRoute.Id : reversedRoute.Id,
                        side,
                        normalIsMovable ? reversedRoute.Id : normalRoute.Id,
                        side);
                }
            }
        }

        private static float MeasureBladeDivergence(
            LineCurve movable,
            LineCurve stock,
            Vector3 switchPoint)
        {
            float tipDist = movable.DistanceTo(switchPoint);
            Vector3 tipPoint = movable.LinePointAtDistance(tipDist).point;
            float stockAtTip = stock.DistanceTo(tipPoint);
            float tipSeparation = Vector3.Distance(
                tipPoint,
                stock.LinePointAtDistance(stockAtTip).point);
            if (tipSeparation > 0.2f)
            {
                return -1f;
            }

            float forwardDist = Mathf.Min(movable.Length, tipDist + MaximumBladeLength);
            float backwardDist = Mathf.Max(0f, tipDist - MaximumBladeLength);
            Vector3 forwardPoint = movable.LinePointAtDistance(forwardDist).point;
            Vector3 backwardPoint = movable.LinePointAtDistance(backwardDist).point;
            float forwardSeparation = Vector3.Distance(
                forwardPoint,
                stock.LinePointAtDistance(stock.DistanceTo(forwardPoint)).point);
            float backwardSeparation = Vector3.Distance(
                backwardPoint,
                stock.LinePointAtDistance(stock.DistanceTo(backwardPoint)).point);
            return Mathf.Max(forwardSeparation, backwardSeparation) - tipSeparation;
        }

        private static int CountAcceptedFrogIntersections(
            RailCenterline rail,
            IEnumerable<RailIntersection> intersections)
        {
            return intersections.Count(intersection =>
                (intersection.Kind == RailIntersectionKind.VeeFrogCandidate
                    || intersection.Kind == RailIntersectionKind.CrossingFrogCandidate)
                && intersection.AcuteAngleDegrees >= MinimumFrogAngle
                && (intersection.RailA == rail || intersection.RailB == rail));
        }

        private static bool DualBothDivergeUsesLeftHandBladeSet(
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<RailIntersection> intersections)
        {
            RailCenterline? leftHandFrogRail = FindRail(rails, "narrow-reversed", RailSide.Left);
            if (leftHandFrogRail == null)
            {
                return false;
            }

            int leftHandFrogCount = intersections.Count(intersection =>
                intersection.Kind == RailIntersectionKind.VeeFrogCandidate
                && (intersection.RailA == leftHandFrogRail
                    || intersection.RailB == leftHandFrogRail));
            return leftHandFrogCount >= 2;
        }

        private static IEnumerable<FrogCandidate> BuildAcceptedFrogs(
            Graph graph,
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<SharedRailInterval> shared,
            IEnumerable<RailIntersection> intersections,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            int index = 0;
            RailIntersection[] accepted = intersections.Where(item =>
                    (item.Kind == RailIntersectionKind.VeeFrogCandidate
                        || item.Kind == RailIntersectionKind.CrossingFrogCandidate)
                    && item.AcuteAngleDegrees >= MinimumFrogAngle
                    && !InsideBladeZone(item, blades))
                .ToArray();
            if (IsDualBothDivergePreset(definition))
            {
                foreach (RailIntersection item in accepted)
                {
                    item.Kind = item.RailA.Side == item.RailB.Side
                        ? RailIntersectionKind.CrossingFrogCandidate
                        : RailIntersectionKind.VeeFrogCandidate;
                }
            }

            bool useNoveSplitFrogCatalog =
                SpecialWorkHardwareProfileCatalog.ShouldUseNoveSplitFrogCatalog(definition);
            if (useNoveSplitFrogCatalog)
            {
                ApplyNoveSplitFrogCatalog(accepted);
                accepted = accepted.Where(IsFrogCandidateIntersection).ToArray();
            }

            if (IsDualBothDivergePreset(definition))
            {
                accepted = CollapseDualBothDivergeDuplicateVees(accepted, blades).ToArray();
            }

            foreach (RailIntersection intersection in accepted)
            {

                Main.Log(
                    $"[FrogKindOverride] {intersection.Id} railA={intersection.RailA.Id}({intersection.RailA.Side}) " +
                    $"railB={intersection.RailB.Id}({intersection.RailB.Side}) " +
                    $"sameSide={intersection.RailA.Side == intersection.RailB.Side} → {intersection.Kind}");
                if (!TryResolveFrogOwnership(
                    intersection,
                    out string ownerRoute,
                    out string crossingRoute,
                    out string protectedRoute))
                {
                    continue;
                }

                float halfAngle = Mathf.Max(
                    intersection.AcuteAngleDegrees * 0.5f * Mathf.Deg2Rad,
                    0.01f);
                float railHeadSetback = RailHeadWidth / Mathf.Tan(halfAngle);
                float flangewaySetback = FlangewayWidth / Mathf.Sin(halfAngle);
                float headMargin = intersection.Kind == RailIntersectionKind.CrossingFrogCandidate
                    ? RailHeadWidth * 3f
                    : RailHeadWidth * 0.5f;
                float cutHalfLength = Mathf.Clamp(
                    Mathf.Max(railHeadSetback + headMargin, flangewaySetback + 0.06f),
                    MinimumFrogSetback,
                    MaximumFrogSetback);
                Vector3 tangentA = intersection.TangentA;
                Vector3 tangentB = Vector3.Dot(tangentA, intersection.TangentB) < 0f
                    ? -intersection.TangentB
                    : intersection.TangentB;
                Vector3 nose = (tangentA + tangentB).normalized;
                if (nose.sqrMagnitude <= 0.0001f)
                {
                    nose = tangentA.normalized;
                }

                if (intersection.Kind == RailIntersectionKind.VeeFrogCandidate)
                {
                    nose = OrientVeeNoseTowardBlades(nose, intersection.Position, blades);
                    if (useNoveSplitFrogCatalog
                        && (IntersectionUsesRoutePair(
                                intersection,
                                "standard-through",
                                "narrow-diverge")))
                    {
                        nose = -nose;
                        Main.Log(
                            $"[NoveFrogCatalog] {intersection.Id} reversed V nose.");
                    }
                }

                Main.Log(
                    $"[FrogAccepted] v2-frog:{index} railA={intersection.RailA.Id} " +
                    $"railB={intersection.RailB.Id} kind={intersection.Kind} " +
                    $"angle={intersection.AcuteAngleDegrees:0.00} cutHalf={cutHalfLength:0.000} " +
                    $"pos=({intersection.Position.x:0.00},{intersection.Position.z:0.00})");
                yield return new FrogCandidate(
                    "v2-frog:" + index++,
                    intersection,
                    nose,
                    -nose,
                    Vector3.Cross(tangentA, tangentB).y >= 0f
                        ? FrogHandedness.Left
                        : FrogHandedness.Right,
                    railHeadSetback,
                    flangewaySetback,
                    cutHalfLength,
                    ownerRoute,
                    crossingRoute,
                    protectedRoute);
            }
        }

        private static void ApplyNoveSplitFrogCatalog(
            IReadOnlyList<RailIntersection> intersections)
        {
            foreach (RailIntersection intersection in intersections)
            {
                RailIntersectionKind? catalogKind = null;
                if (IntersectionUsesRoutePair(
                        intersection,
                        "standard-through",
                        "narrow-diverge"))
                {
                    catalogKind = RailIntersectionKind.VeeFrogCandidate;
                }
                else if (IntersectionUsesRoutePair(
                             intersection,
                             "narrow-through",
                             "narrow-diverge"))
                {
                    catalogKind = RailIntersectionKind.InvalidShallowCrossing;
                }

                if (!catalogKind.HasValue)
                {
                    continue;
                }

                RailIntersectionKind previous = intersection.Kind;
                intersection.Kind = catalogKind.Value;
                Main.Log(
                    $"[NoveFrogCatalog] {intersection.Id} " +
                    $"{intersection.RailA.Id}/{intersection.RailB.Id}: " +
                    $"{previous} => {intersection.Kind}");
            }
        }

        private static bool IntersectionUsesRoutePair(
            RailIntersection intersection,
            string firstRouteId,
            string secondRouteId)
        {
            return RailUsesRoute(intersection.RailA, firstRouteId)
                    && RailUsesRoute(intersection.RailB, secondRouteId)
                || RailUsesRoute(intersection.RailA, secondRouteId)
                    && RailUsesRoute(intersection.RailB, firstRouteId);
        }

        private static IEnumerable<RailIntersection> CollapseDualBothDivergeDuplicateVees(
            IReadOnlyList<RailIntersection> intersections,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            var grouped = new HashSet<RailIntersection>();
            for (int i = 0; i < intersections.Count; i++)
            {
                if (grouped.Contains(intersections[i]))
                {
                    continue;
                }

                var group = new List<RailIntersection> { intersections[i] };
                bool changed;
                do
                {
                    changed = false;
                    for (int j = 0; j < intersections.Count; j++)
                    {
                        RailIntersection candidate = intersections[j];
                        if (group.Contains(candidate)
                            || candidate.Kind != RailIntersectionKind.VeeFrogCandidate
                            || !group.Any(item => IsDuplicateSharedVee(item, candidate)))
                        {
                            continue;
                        }

                        group.Add(candidate);
                        changed = true;
                    }
                }
                while (changed);

                foreach (RailIntersection item in group)
                {
                    grouped.Add(item);
                }

                yield return group
                    .OrderByDescending(item => DualBothDivergeVeePreference(item, group, blades))
                    .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                    .First();
            }
        }

        private static bool IsDuplicateSharedVee(
            RailIntersection first,
            RailIntersection second)
        {
            if (first.Kind != RailIntersectionKind.VeeFrogCandidate
                || second.Kind != RailIntersectionKind.VeeFrogCandidate
                || Vector3.Distance(first.Position, second.Position) > CorridorTolerance * 2f
                || !TryResolveSharedVeeRails(
                    first,
                    second,
                    out _,
                    out RailCenterline firstOuter,
                    out RailCenterline secondOuter)
                || firstOuter.Side != secondOuter.Side)
            {
                return false;
            }

            float overlapTolerance = Parameters.RailHeadWidth + Parameters.FlangewayWidth;
            return DistancePointToCurve(first.Position, secondOuter.Curve) <= overlapTolerance
                && DistancePointToCurve(second.Position, firstOuter.Curve) <= overlapTolerance;
        }

        private static bool TryResolveSharedVeeRails(
            RailIntersection first,
            RailIntersection second,
            out RailCenterline common,
            out RailCenterline firstOuter,
            out RailCenterline secondOuter)
        {
            common = null!;
            firstOuter = null!;
            secondOuter = null!;

            if (first.RailA == second.RailA || first.RailA == second.RailB)
            {
                common = first.RailA;
                firstOuter = first.RailB;
                secondOuter = second.RailA == common ? second.RailB : second.RailA;
                return true;
            }

            if (first.RailB == second.RailA || first.RailB == second.RailB)
            {
                common = first.RailB;
                firstOuter = first.RailA;
                secondOuter = second.RailA == common ? second.RailB : second.RailA;
                return true;
            }

            return false;
        }

        private static int DualBothDivergeVeePreference(
            RailIntersection intersection,
            IReadOnlyList<RailIntersection> duplicateGroup,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            int score = DualBothDivergeFamilyVeePreference(intersection);
            foreach (RailIntersection other in duplicateGroup)
            {
                if (other == intersection
                    || !TryResolveSharedVeeRails(
                        intersection,
                        other,
                        out _,
                        out RailCenterline candidateOuter,
                        out RailCenterline otherOuter))
                {
                    continue;
                }

                RailCenterline owner = ChooseSharedOwner(candidateOuter, otherOuter, blades);
                if (owner == candidateOuter)
                {
                    score += 1000;
                }
                else if (owner == otherOuter)
                {
                    score -= 1000;
                }
            }

            return score;
        }

        private static int DualBothDivergeFamilyVeePreference(RailIntersection intersection)
        {
            bool sameFamily = intersection.RailA.Family == intersection.RailB.Family;
            if (!sameFamily)
            {
                return 0;
            }

            return intersection.RailA.Family == GaugeGraphFamily.Standard ? 120 : 110;
        }

        private static Vector3 OrientVeeNoseTowardBlades(
            Vector3 nose,
            Vector3 intersection,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            if (blades.Count == 0)
            {
                return nose;
            }

            Vector3 bladeCenter = blades
                .Select(blade => blade.BladeCurve.Head.point)
                .Aggregate(Vector3.zero, (sum, point) => sum + point) / blades.Count;
            Vector3 towardBlades = bladeCenter - intersection;
            towardBlades.y = 0f;
            if (towardBlades.sqrMagnitude <= 0.0001f)
            {
                return nose;
            }

            return Vector3.Dot(nose, towardBlades) >= 0f
                ? nose
                : -nose;
        }

        private static bool SameRailPair(
            SharedRailInterval interval,
            string firstRailId,
            string secondRailId)
        {
            return string.Equals(interval.RailAId, firstRailId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(interval.RailBId, secondRailId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(interval.RailAId, secondRailId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(interval.RailBId, firstRailId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool RailUsesRoute(RailCenterline rail, string routeId)
        {
            return rail.SourceRouteIds.Contains(routeId, StringComparer.OrdinalIgnoreCase);
        }

        private static bool TryResolveFrogOwnership(
            RailIntersection intersection,
            out string ownerRoute,
            out string crossingRoute,
            out string protectedRoute)
        {
            ownerRoute = intersection.RailA.SourceRouteIds.FirstOrDefault() ?? string.Empty;
            crossingRoute = intersection.RailB.SourceRouteIds.FirstOrDefault() ?? string.Empty;
            protectedRoute = string.Empty;
            if (string.IsNullOrWhiteSpace(ownerRoute)
                || string.IsNullOrWhiteSpace(crossingRoute)
                || string.Equals(ownerRoute, crossingRoute, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            protectedRoute = intersection.RailA.Family == GaugeGraphFamily.Narrow
                ? ownerRoute
                : intersection.RailB.Family == GaugeGraphFamily.Narrow
                    ? crossingRoute
                    : ownerRoute;
            return !string.IsNullOrWhiteSpace(protectedRoute);
        }

        private static void AddFrogSuppressions(
            IReadOnlyList<RailCenterline> rails,
            IEnumerable<FrogCandidate> frogs,
            ICollection<RailCut> cuts,
            ICollection<RailSuppressionInterval> suppressions)
        {
            foreach (FrogCandidate frog in frogs)
            {
                Main.Log(
                    $"[FrogGapDirect] {frog.Id} kind={frog.Intersection.Kind} " +
                    $"railA={frog.Intersection.RailA.Id}@{frog.Intersection.DistanceA:0.000} " +
                    $"railB={frog.Intersection.RailB.Id}@{frog.Intersection.DistanceB:0.000} " +
                    $"cutHalf={frog.CutHalfLength:0.000}");
                foreach ((RailCenterline rail, float distance) in new[]
                {
                    (frog.Intersection.RailA, frog.Intersection.DistanceA),
                    (frog.Intersection.RailB, frog.Intersection.DistanceB)
                })
                {
                    float start = distance - frog.CutHalfLength;
                    float end = distance + frog.CutHalfLength;
                    AddCut(cuts, rail, start, end, RailCutKind.FrogGap, frog.Id);
                    AddSuppression(suppressions, rail, start, end, "frog gap " + frog.Id);
                }

                foreach (RailCenterline rail in rails)
                {
                    if (rail == frog.Intersection.RailA
                        || rail == frog.Intersection.RailB)
                    {
                        continue;
                    }

                    float distance = rail.Curve.DistanceTo(frog.Intersection.Position);
                    Vector3 closest = rail.Curve.LinePointAtDistance(distance).point;
                    float separation = Vector3.Distance(closest, frog.Intersection.Position);
                    if (rail.Id.IndexOf("standard-reversed", StringComparison.OrdinalIgnoreCase) >= 0
                        && rail.Side == RailSide.Right)
                    {
                        Main.Log(
                            $"[FrogNearbyCheck] {frog.Id} rail={rail.Id} distance={distance:0.000} " +
                            $"separation={separation:0.000} tolerance={RailHeadWidth + FlangewayWidth:0.000} " +
                            $"accepted={separation <= RailHeadWidth + FlangewayWidth}");
                    }

                    if (separation > RailHeadWidth + FlangewayWidth)
                    {
                        continue;
                    }

                    float start = distance - frog.CutHalfLength;
                    float end = distance + frog.CutHalfLength;
                    AddCut(cuts, rail, start, end, RailCutKind.FrogGap, frog.Id);
                    AddSuppression(suppressions, rail, start, end, "frog gap " + frog.Id);
                }
            }
        }

        private static FrogCandidate[] AddNoveCatalogCrossingFrog(
            IReadOnlyList<RailCenterline> rails,
            FrogCandidate[] existing)
        {
            RailCenterline? standardThroughRight = FindRail(rails, "standard-through", RailSide.Right);
            RailCenterline? narrowDivergeLeft = FindRail(rails, "narrow-diverge", RailSide.Left);
            if (standardThroughRight == null || narrowDivergeLeft == null)
            {
                return existing;
            }

            bool alreadyHasFrog = existing.Any(frog =>
                (frog.Intersection.RailA == standardThroughRight
                    && frog.Intersection.RailB == narrowDivergeLeft)
                || (frog.Intersection.RailA == narrowDivergeLeft
                    && frog.Intersection.RailB == standardThroughRight));
            if (alreadyHasFrog)
            {
                return existing;
            }

            if (!TryCreateSyntheticCrossingFrog(
                    standardThroughRight,
                    narrowDivergeLeft,
                    existing.Length,
                    "nove-catalog",
                    Gauge.Standard.Inside,
                    1.5f,
                    out FrogCandidate frog,
                    out string rejectionReason))
            {
                Main.Log(
                    "[NoveFrogCatalog] Could not add bottom-left K frog for " +
                    $"{standardThroughRight.Id}/{narrowDivergeLeft.Id}: " +
                    rejectionReason);
                return existing;
            }

            Main.Log(
                $"[NoveFrogCatalog] Added bottom-left K frog {frog.Id}: " +
                $"{standardThroughRight.Id}/{narrowDivergeLeft.Id} " +
                $"angle={frog.Intersection.AcuteAngleDegrees:0.00} " +
                $"cutHalf={frog.CutHalfLength:0.000}.");
            return existing.Concat(new[] { frog }).ToArray();
        }

        private static bool TryCreateSyntheticCrossingFrog(
            RailCenterline standardRail,
            RailCenterline narrowRail,
            int index,
            string idPrefix,
            float maximumSeparation,
            float maximumCutHalf,
            out FrogCandidate frog,
            out string rejectionReason)
        {
            frog = null!;
            rejectionReason = string.Empty;
            float bestSeparation = float.MaxValue;
            float bestStandardDistance = 0f;
            float bestNarrowDistance = 0f;
            Vector3 bestPoint = Vector3.zero;
            int samples = Mathf.Max(10, Mathf.CeilToInt(standardRail.Curve.Length / 0.5f));
            for (int i = 0; i < samples; i++)
            {
                float standardDistance = standardRail.Curve.Length * i / (samples - 1);
                Vector3 standardPoint = standardRail.Curve.LinePointAtDistance(standardDistance).point;
                float narrowDistance = narrowRail.Curve.DistanceTo(standardPoint);
                Vector3 narrowPoint = narrowRail.Curve.LinePointAtDistance(narrowDistance).point;
                float separation = Vector3.Distance(standardPoint, narrowPoint);
                if (separation < bestSeparation)
                {
                    bestSeparation = separation;
                    bestStandardDistance = standardDistance;
                    bestNarrowDistance = narrowDistance;
                    bestPoint = (standardPoint + narrowPoint) * 0.5f;
                }
            }

            if (bestSeparation > maximumSeparation)
            {
                rejectionReason =
                    $"closest separation {bestSeparation:0.000} exceeds {maximumSeparation:0.000}.";
                return false;
            }

            Vector3 tangentA = standardRail.Curve.LinePointAtDistance(bestStandardDistance).direction;
            Vector3 tangentB = narrowRail.Curve.LinePointAtDistance(bestNarrowDistance).direction;
            if (Vector3.Dot(tangentA, tangentB) < 0f)
            {
                tangentB = -tangentB;
            }

            float angle = Vector3.Angle(tangentA, tangentB);
            if (angle < MinimumFrogAngle)
            {
                rejectionReason =
                    $"angle {angle:0.00} is below minimum {MinimumFrogAngle:0.00}.";
                return false;
            }

            float halfAngle = Mathf.Max(angle * 0.5f * Mathf.Deg2Rad, 0.01f);
            float railHeadSetback = RailHeadWidth / Mathf.Tan(halfAngle);
            float flangewaySetback = FlangewayWidth / Mathf.Sin(halfAngle);
            float cutHalfLength = Mathf.Clamp(
                Mathf.Max(railHeadSetback + RailHeadWidth, flangewaySetback + 0.06f),
                MinimumFrogSetback,
                maximumCutHalf);
            Vector3 nose = (tangentA + tangentB).normalized;
            if (nose.sqrMagnitude <= 0.0001f)
            {
                nose = tangentA.normalized;
            }

            var intersection = new RailIntersection(
                idPrefix + "-crossing:" + index,
                standardRail,
                narrowRail,
                bestStandardDistance,
                bestNarrowDistance,
                bestPoint,
                tangentA,
                tangentB,
                angle,
                RailIntersectionKind.CrossingFrogCandidate);

            frog = new FrogCandidate(
                "v2-frog:" + idPrefix + ":" + index,
                intersection,
                nose,
                -nose,
                Vector3.Cross(tangentA, tangentB).y >= 0f
                    ? FrogHandedness.Left
                    : FrogHandedness.Right,
                railHeadSetback,
                flangewaySetback,
                cutHalfLength,
                standardRail.SourceRouteIds.FirstOrDefault() ?? string.Empty,
                narrowRail.SourceRouteIds.FirstOrDefault() ?? string.Empty,
                narrowRail.SourceRouteIds.FirstOrDefault() ?? string.Empty);
            return true;
        }

        private static FrogCandidate[] AddMissingCrossFamilyCrossingFrogs(
            IReadOnlyList<RailCenterline> rails,
            FrogCandidate[] existing)
        {
            var result = new List<FrogCandidate>(existing);
            int index = existing.Length;
            foreach (RailCenterline stdRail in rails.Where(r => r.Family == GaugeGraphFamily.Standard))
            {
                foreach (RailCenterline nrwRail in rails.Where(r =>
                    r.Family == GaugeGraphFamily.Narrow && r.Side == stdRail.Side))
                {
                    bool alreadyHasFrog = existing.Any(f =>
                        (f.Intersection.RailA == stdRail && f.Intersection.RailB == nrwRail)
                        || (f.Intersection.RailA == nrwRail && f.Intersection.RailB == stdRail));
                    if (alreadyHasFrog)
                    {
                        continue;
                    }

                    float bestSep = float.MaxValue;
                    float bestDistA = 0f;
                    float bestDistB = 0f;
                    Vector3 bestPoint = Vector3.zero;
                    int samples = Mathf.Max(10, Mathf.CeilToInt(stdRail.Curve.Length / 0.5f));
                    for (int i = 0; i < samples; i++)
                    {
                        float d = stdRail.Curve.Length * i / (samples - 1);
                        Vector3 p = stdRail.Curve.LinePointAtDistance(d).point;
                        float dB = nrwRail.Curve.DistanceTo(p);
                        Vector3 pB = nrwRail.Curve.LinePointAtDistance(dB).point;
                        float sep = Vector3.Distance(p, pB);
                        if (sep < bestSep)
                        {
                            bestSep = sep;
                            bestDistA = d;
                            bestDistB = dB;
                            bestPoint = (p + pB) * 0.5f;
                        }
                    }

                    if (bestSep > Gauge.Standard.Inside * 0.5f)
                    {
                        continue;
                    }

                    Vector3 tangentA = stdRail.Curve.LinePointAtDistance(bestDistA).direction;
                    Vector3 tangentB = nrwRail.Curve.LinePointAtDistance(bestDistB).direction;
                    if (Vector3.Dot(tangentA, tangentB) < 0f)
                    {
                        tangentB = -tangentB;
                    }

                    float angle = Vector3.Angle(tangentA, tangentB);
                    if (angle < MinimumFrogAngle)
                    {
                        continue;
                    }

                    float halfAngle = Mathf.Max(angle * 0.5f * Mathf.Deg2Rad, 0.01f);
                    float railHeadSetback = RailHeadWidth / Mathf.Tan(halfAngle);
                    float flangewaySetback = FlangewayWidth / Mathf.Sin(halfAngle);
                    float cutHalfLength = Mathf.Clamp(
                        Mathf.Max(railHeadSetback + RailHeadWidth, flangewaySetback + 0.06f),
                        MinimumFrogSetback,
                        1.5f);
                    Vector3 nose = (tangentA + tangentB).normalized;
                    if (nose.sqrMagnitude <= 0.0001f)
                    {
                        nose = tangentA.normalized;
                    }

                    var intersection = new RailIntersection(
                        "synth-crossing:" + index,
                        stdRail,
                        nrwRail,
                        bestDistA,
                        bestDistB,
                        bestPoint,
                        tangentA,
                        tangentB,
                        angle,
                        RailIntersectionKind.CrossingFrogCandidate);

                    result.Add(new FrogCandidate(
                        "v2-frog:synth:" + index++,
                        intersection,
                        nose,
                        -nose,
                        Vector3.Cross(tangentA, tangentB).y >= 0f
                            ? FrogHandedness.Left
                            : FrogHandedness.Right,
                        railHeadSetback,
                        flangewaySetback,
                        cutHalfLength,
                        stdRail.SourceRouteIds.FirstOrDefault() ?? "",
                        nrwRail.SourceRouteIds.FirstOrDefault() ?? "",
                        nrwRail.SourceRouteIds.FirstOrDefault() ?? ""));
                    Main.Log(
                        $"[SynthCrossing] Created missing K-frog: {stdRail.Id} x {nrwRail.Id} " +
                        $"angle={angle:0.00} sep={bestSep:0.000} cutHalf={cutHalfLength:0.000}");
                }
            }

            return result.ToArray();
        }

        private static void AddCollapsedVeeFrogGaps(
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<RailIntersection> allIntersections,
            IReadOnlyList<FrogCandidate> survivingFrogs,
            IReadOnlyList<SwitchBladePlan> blades,
            ICollection<RailCut> cuts,
            ICollection<RailSuppressionInterval> suppressions)
        {
            var survivingRailPairs = new HashSet<string>(
                survivingFrogs.Select(frog =>
                    PairKey(frog.Intersection.RailA.Id, frog.Intersection.RailB.Id)),
                StringComparer.OrdinalIgnoreCase);
            foreach (RailIntersection intersection in allIntersections)
            {
                if ((intersection.Kind != RailIntersectionKind.VeeFrogCandidate
                        && intersection.Kind != RailIntersectionKind.CrossingFrogCandidate)
                    || intersection.AcuteAngleDegrees < MinimumFrogAngle
                    || InsideBladeZone(intersection, blades)
                    || survivingRailPairs.Contains(
                        PairKey(intersection.RailA.Id, intersection.RailB.Id)))
                {
                    continue;
                }

                float halfAngle = Mathf.Max(
                    intersection.AcuteAngleDegrees * 0.5f * Mathf.Deg2Rad, 0.01f);
                float cutHalfLength = Mathf.Clamp(
                    Mathf.Max(
                        RailHeadWidth / Mathf.Tan(halfAngle) + RailHeadWidth * 0.5f,
                        FlangewayWidth / Mathf.Sin(halfAngle) + 0.06f),
                    MinimumFrogSetback,
                    MaximumFrogSetback);

                foreach ((RailCenterline rail, float distance) in new[]
                {
                    (intersection.RailA, intersection.DistanceA),
                    (intersection.RailB, intersection.DistanceB)
                })
                {
                    bool alreadyCovered = cuts.Any(cut =>
                        cut.Rail == rail
                        && cut.Kind == RailCutKind.FrogGap
                        && cut.StartDistance <= distance + 0.5f
                        && cut.EndDistance >= distance - 0.5f);
                    if (alreadyCovered)
                    {
                        continue;
                    }

                    float start = distance - cutHalfLength;
                    float end = distance + cutHalfLength;
                    AddCut(cuts, rail, start, end, RailCutKind.FrogGap,
                        "collapsed-vee:" + intersection.Id);
                    AddSuppression(suppressions, rail, start, end,
                        "collapsed vee frog gap");
                }
            }
        }

        private static string PairKey(string a, string b)
        {
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase) <= 0
                ? a + "|" + b
                : b + "|" + a;
        }

        private static void AddSharedSuppressions(
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IEnumerable<SharedRailInterval> shared,
            IReadOnlyList<SwitchBladePlan> blades,
            IReadOnlyList<FrogCandidate> frogs,
            ICollection<RailCut> cuts,
            ICollection<RailSuppressionInterval> suppressions)
        {
            SharedRailInterval[] intervals = shared.ToArray();
            IReadOnlyDictionary<string, RailCenterline>? corridorOwners =
                IsDualBothDivergePreset(definition)
                    ? BuildSharedCorridorOwners(rails, intervals, blades)
                    : null;
            foreach (SharedRailInterval interval in intervals)
            {
                RailCenterline? railA = rails.FirstOrDefault(rail => rail.Id == interval.RailAId);
                RailCenterline? railB = rails.FirstOrDefault(rail => rail.Id == interval.RailBId);
                if (railA == null || railB == null)
                {
                    continue;
                }

                RailCenterline owner = corridorOwners != null
                    ? corridorOwners[railA.Id]
                    : ChooseSharedOwner(railA, railB, blades);
                if (owner != railA && owner != railB)
                {
                    continue;
                }

                RailCenterline loser = owner == railA ? railB : railA;
                if (IsDualBothDivergePreset(definition)
                    && RailParticipatesInAcceptedFrog(loser, frogs))
                {
                    continue;
                }

                float start = loser.Curve.DistanceTo(interval.Start);
                float end = loser.Curve.DistanceTo(interval.End);
                AddCut(cuts, loser, start, end, RailCutKind.SharedDuplicate, "shared duplicate");
                AddSuppression(suppressions, loser, start, end, "shared duplicate");
            }
        }

        private static void AddCrossFamilySharedSuppressions(
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<SwitchBladePlan> blades,
            IReadOnlyList<FrogCandidate> frogs,
            ICollection<RailCut> cuts,
            ICollection<RailSuppressionInterval> suppressions)
        {
            RailSide? sharedSide = null;
            foreach (LogicalRoute route in definition.Routes)
            {
                foreach (string segmentId in route.SourceSegmentIds)
                {
                    TrackSegment? segment = TrackAPI.GetSegment(segmentId);
                    if (segment == null || !NarrowGaugeManager.IsDualGauge(segment))
                    {
                        continue;
                    }

                    sharedSide = DualGaugeSharedRailRegistry.SharesRightRail(segment)
                        ? RailSide.Right
                        : RailSide.Left;
                    break;
                }

                if (sharedSide.HasValue)
                {
                    break;
                }
            }

            if (!sharedSide.HasValue)
            {
                return;
            }

            RailCenterline[] standardShared = rails
                .Where(rail => rail.Family == GaugeGraphFamily.Standard && rail.Side == sharedSide.Value)
                .ToArray();
            RailCenterline[] narrowShared = rails
                .Where(rail => rail.Family == GaugeGraphFamily.Narrow && rail.Side == sharedSide.Value)
                .ToArray();
            foreach (RailCenterline narrowRail in narrowShared)
            {
                foreach (RailCenterline standardRail in standardShared)
                {
                    float overlapLength = CurveOverlapLength(narrowRail.Curve, standardRail.Curve);
                    if (overlapLength < MinimumPieceLength)
                    {
                        continue;
                    }

                    RailCenterline loser = ChooseSharedOwner(standardRail, narrowRail, blades) == standardRail
                        ? narrowRail
                        : standardRail;
                    if (RailParticipatesInAcceptedFrog(loser, frogs))
                    {
                        continue;
                    }

                    foreach ((float start, float end) in FindCurveOverlaps(loser.Curve, standardRail == loser ? narrowRail.Curve : standardRail.Curve))
                    {
                        AddCut(cuts, loser, start, end, RailCutKind.SharedDuplicate, "cross-family shared duplicate");
                        AddSuppression(suppressions, loser, start, end, "shared duplicate");
                    }
                }
            }
        }

        private static IReadOnlyDictionary<string, RailCenterline> BuildSharedCorridorOwners(
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<SharedRailInterval> shared,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            var owners = new Dictionary<string, RailCenterline>(StringComparer.OrdinalIgnoreCase);
            var unvisited = new HashSet<string>(
                shared.SelectMany(interval => new[] { interval.RailAId, interval.RailBId }),
                StringComparer.OrdinalIgnoreCase);
            while (unvisited.Count > 0)
            {
                string seed = unvisited.First();
                var component = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { seed };
                bool changed;
                do
                {
                    changed = false;
                    foreach (SharedRailInterval interval in shared)
                    {
                        if (!component.Contains(interval.RailAId)
                            && !component.Contains(interval.RailBId))
                        {
                            continue;
                        }

                        changed |= component.Add(interval.RailAId);
                        changed |= component.Add(interval.RailBId);
                    }
                }
                while (changed);

                RailCenterline owner = rails
                    .Where(rail => component.Contains(rail.Id))
                    .OrderByDescending(rail => blades.Any(blade => blade.StockRail == rail))
                    .ThenBy(rail => rail.Family == GaugeGraphFamily.Standard ? 0 : 1)
                    .ThenBy(rail => rail.Id, StringComparer.OrdinalIgnoreCase)
                    .First();
                foreach (string railId in component)
                {
                    owners[railId] = owner;
                    unvisited.Remove(railId);
                }
            }

            return owners;
        }

        private static void SuppressDualBothDivergeFrogDuplicate(
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<RailWorkInterval> workIntervals,
            IReadOnlyList<FrogCandidate> frogs,
            ICollection<RailCut> cuts,
            ICollection<RailSuppressionInterval> suppressions)
        {
            if (!IsDualBothDivergePreset(definition))
            {
                return;
            }

            RailCenterline? duplicate = rails.FirstOrDefault(rail =>
                string.Equals(
                    rail.Id,
                    "narrow-normal:left",
                    StringComparison.OrdinalIgnoreCase));
            RailWorkInterval? work = workIntervals.FirstOrDefault(interval =>
                interval.Rail == duplicate);
            if (duplicate == null || work == null)
            {
                return;
            }

            if (RailParticipatesInAcceptedFrog(duplicate, frogs))
            {
                return;
            }

            AddCut(
                cuts,
                duplicate,
                work.StartDistance,
                work.EndDistance,
                RailCutKind.SharedDuplicate,
                "dual-both-diverge:vee-frog-shared-duplicate");
            AddSuppression(
                suppressions,
                duplicate,
                work.StartDistance,
                work.EndDistance,
                "shared duplicate through vee frogs");
        }

        private static void AddBladeCorridorSuppressions(
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IEnumerable<SwitchBladePlan> blades,
            ICollection<RailCut> cuts,
            ICollection<RailSuppressionInterval> suppressions)
        {
            foreach (SwitchBladePlan blade in blades)
            {
                LineCurve bladeCurve = Slice(
                    blade.MovableRail.Curve,
                    blade.TipDistance,
                    blade.RootDistance);
                float stockTip = blade.StockRail.Curve.DistanceTo(blade.BladeCurve.Head.point);
                float stockRoot = blade.StockRail.Curve.DistanceTo(blade.BladeCurve.Tail.point);
                LineCurve stockBladeCorridor = Slice(
                    blade.StockRail.Curve,
                    Mathf.Min(stockTip, stockRoot),
                    Mathf.Max(stockTip, stockRoot));
                foreach (RailCenterline rail in rails)
                {
                    if (rail == blade.StockRail || rail == blade.MovableRail)
                    {
                        continue;
                    }

                    // A shared physical owner of the valid stock rail must remain.
                    // It is the stock rail even when route ownership lives on another
                    // route-derived RailCenterline.
                    if (!IsDualBothDivergePreset(definition)
                        && CurveOverlapLength(rail.Curve, stockBladeCorridor) > 0.2f)
                    {
                        continue;
                    }

                    foreach ((float start, float end) in FindCurveOverlaps(rail.Curve, bladeCurve))
                    {
                        AddCut(cuts, rail, start, end, RailCutKind.OwnershipConflict, blade.Id);
                        AddSuppression(
                            suppressions,
                            rail,
                            start,
                            end,
                            "fixed rail under blade " + blade.Id);
                    }
                }
            }
        }

        private static IEnumerable<RailWorkInterval> BuildWorkIntervals(
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<SharedRailInterval> shared,
            IReadOnlyList<FrogCandidate> frogs,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            // Shared rails commonly continue across the complete adjoining
            // segments. They describe physical ownership, not the boundary of
            // the special-work object. Only actual switchwork events define
            // the measured replacement territory.
            Vector3[] eventPoints = frogs
                .Select(frog => frog.Intersection.Position)
                .Concat(blades.SelectMany(blade => new[]
                {
                    blade.BladeCurve.Head.point,
                    blade.BladeCurve.Tail.point
                }))
                .ToArray();
            if (eventPoints.Length == 0)
            {
                yield break;
            }

            foreach (RailCenterline rail in rails)
            {
                float[] distances = eventPoints
                    .Select(rail.Curve.DistanceTo)
                    .ToArray();

                yield return new RailWorkInterval(
                    rail,
                    Mathf.Max(0f, distances.Min() - WorkEnvelopeMargin),
                    Mathf.Min(rail.Curve.Length, distances.Max() + WorkEnvelopeMargin));
            }
        }

        private static IEnumerable<RailRoleSection> BuildRoleSections(
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<SharedRailInterval> shared,
            IReadOnlyList<RailWorkInterval> workIntervals,
            IReadOnlyList<RailCut> cuts,
            IReadOnlyList<RailSuppressionInterval> suppressions,
            IReadOnlyList<FrogCandidate> frogs,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            int index = 0;
            foreach (RailCenterline rail in rails)
            {
                RailWorkInterval? work = workIntervals.FirstOrDefault(item => item.Rail == rail);
                if (work == null)
                {
                    continue;
                }

                float[] boundaries = BuildBoundaries(rail, work, cuts, suppressions, frogs, blades);
                for (int i = 0; i + 1 < boundaries.Length; i++)
                {
                    float start = boundaries[i];
                    float end = boundaries[i + 1];
                    if (end - start < MinimumPieceLength)
                    {
                        continue;
                    }

                    RailRole role = ResolveRole(
                        definition,
                        rail,
                        start,
                        end,
                        shared,
                        suppressions,
                        frogs,
                        blades);
                    yield return new RailRoleSection(
                        "section:" + index++,
                        rail,
                        role,
                        start,
                        end,
                        rail.SourceRouteIds.FirstOrDefault() ?? string.Empty,
                        rail.Family,
                        SourceCurveKind(rail.SourceRouteIds.FirstOrDefault() ?? string.Empty),
                        RoleReason(role));
                }
            }
        }

        private static RailRole ResolveRole(
            SpecialWorkDefinition definition,
            RailCenterline rail,
            float start,
            float end,
            IReadOnlyList<SharedRailInterval> shared,
            IReadOnlyList<RailSuppressionInterval> suppressions,
            IReadOnlyList<FrogCandidate> frogs,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            float middle = (start + end) * 0.5f;
            if (suppressions.Any(item =>
                item.Rail == rail && IntervalsOverlap(start, end, item.StartDistance, item.EndDistance)))
            {
                return RailRole.SuppressedRail;
            }

            SwitchBladePlan? movableBlade = blades.FirstOrDefault(blade =>
                blade.MovableRail == rail
                && middle >= blade.TipDistance - 0.01f
                && middle <= blade.RootDistance + 0.01f);
            if (movableBlade != null)
            {
                return RailRole.MovablePointBlade;
            }

            if (blades.Any(blade =>
                blade.StockRail == rail
                && middle >= blade.TipDistance - 0.01f
                && middle <= blade.RootDistance + 0.01f))
            {
                return RailRole.StockRail;
            }

            if (IsSharedOwnerAt(rail, shared, middle))
            {
                return RailRole.SharedRail;
            }

            FrogCandidate? frog = frogs.FirstOrDefault(item =>
                (item.Intersection.RailA == rail
                    && Mathf.Abs(item.Intersection.DistanceA - middle) <= item.CutHalfLength + 0.75f)
                || (item.Intersection.RailB == rail
                    && Mathf.Abs(item.Intersection.DistanceB - middle) <= item.CutHalfLength + 0.75f));
            if (frog != null)
            {
                return RailRole.FrogApproachRail;
            }

            if (blades.Any(blade => blade.MovableRail == rail && middle > blade.RootDistance))
            {
                return RailRole.ClosureRail;
            }

            if (blades.Any(blade => blade.StockRail == rail))
            {
                return RailRole.FixedRunningRail;
            }

            return RailRole.FixedRunningRail;
        }

        private static IEnumerable<RailPiece> BuildFixedPieces(IEnumerable<RailRoleSection> sections)
        {
            int index = 0;
            foreach (RailRoleSection section in sections.Where(section => CanRenderRole(section.Role)))
            {
                yield return new RailPiece(
                    "v2-fixed:" + index++,
                    section.Rail.Id,
                    PieceKindForRole(section.Role),
                    Slice(section.Rail.Curve, section.StartDistance, section.EndDistance),
                    section.StartDistance,
                    section.EndDistance,
                    section.Id);
            }
        }

        private static IEnumerable<RailPiece> BuildFrogPieces(IEnumerable<FrogCandidate> frogs)
        {
            int index = 0;
            foreach (FrogCandidate frog in frogs.Where(frog =>
                frog.Intersection.Kind == RailIntersectionKind.VeeFrogCandidate))
            {
                foreach ((RailCenterline rail, float distance) in new[]
                {
                    (frog.Intersection.RailA, frog.Intersection.DistanceA),
                    (frog.Intersection.RailB, frog.Intersection.DistanceB)
                })
                {
                    bool positiveHeel = IsPositiveHeelSide(rail, distance, frog);
                    float start = positiveHeel ? distance : distance - frog.CutHalfLength;
                    float end = positiveHeel ? distance + frog.CutHalfLength : distance;
                    yield return new RailPiece(
                        "v2-frog-piece:" + index++,
                        rail.Id,
                        RailPieceKind.FrogNose,
                        Slice(rail.Curve, start, end),
                        start,
                        end,
                        frog.Id);
                }
            }
        }

        private static IEnumerable<WingRailPlan> BuildWingRails(IEnumerable<FrogCandidate> frogs)
        {
            int index = 0;
            foreach (FrogCandidate frog in frogs)
            {
                foreach ((RailCenterline rail, float distance) in new[]
                {
                    (frog.Intersection.RailA, frog.Intersection.DistanceA),
                    (frog.Intersection.RailB, frog.Intersection.DistanceB)
                })
                {
                    if (frog.Intersection.Kind == RailIntersectionKind.CrossingFrogCandidate)
                    {
                        float gap = Mathf.Min(
                            frog.CutHalfLength - MinimumPieceLength,
                            Mathf.Max(frog.FlangewaySetback, FlangewayWidth));
                        yield return new WingRailPlan(
                            "v2-wing:" + index++,
                            frog.Id,
                            rail,
                            Slice(rail.Curve, distance - frog.CutHalfLength, distance - gap),
                            true);
                        yield return new WingRailPlan(
                            "v2-wing:" + index++,
                            frog.Id,
                            rail,
                            Slice(rail.Curve, distance + gap, distance + frog.CutHalfLength),
                            false);
                        continue;
                    }

                    bool positiveHeel = IsPositiveHeelSide(rail, distance, frog);
                    yield return new WingRailPlan(
                        "v2-wing:" + index++,
                        frog.Id,
                        rail,
                        positiveHeel
                            ? Slice(rail.Curve, distance - frog.CutHalfLength, distance)
                            : Slice(rail.Curve, distance, distance + frog.CutHalfLength),
                        !positiveHeel);
                }
            }
        }

        private static bool IsPositiveHeelSide(
            RailCenterline rail,
            float distance,
            FrogCandidate frog)
        {
            Vector3 before = rail.Curve
                .LinePointAtDistance(Mathf.Max(0f, distance - frog.CutHalfLength))
                .point - frog.Intersection.Position;
            Vector3 after = rail.Curve
                .LinePointAtDistance(Mathf.Min(rail.Curve.Length, distance + frog.CutHalfLength))
                .point - frog.Intersection.Position;
            return Vector3.Dot(after, frog.HeelDirection) >= Vector3.Dot(before, frog.HeelDirection);
        }

        private static IEnumerable<GuardRailPlan> BuildGuardRails(
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IEnumerable<FrogCandidate> frogs,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            FrogCandidate[] frogList = frogs.ToArray();
            var guards = new List<GuardRailPlan>();
            int index = 0;
            foreach (FrogCandidate frog in frogList)
            {
                foreach (RailCenterline crossingRail in new[]
                {
                    frog.Intersection.RailA,
                    frog.Intersection.RailB
                })
                {
                    string routeId = crossingRail.SourceRouteIds.FirstOrDefault() ?? string.Empty;
                    RailCenterline? opposite = rails.FirstOrDefault(rail =>
                        rail != crossingRail
                        && rail.Side != crossingRail.Side
                        && rail.SourceRouteIds.Contains(routeId, StringComparer.OrdinalIgnoreCase));
                    if (opposite == null)
                    {
                        continue;
                    }

                    float center = opposite.Curve.DistanceTo(frog.Intersection.Position);
                    float offset = opposite.Side == RailSide.Left
                        ? Parameters.GuardCenterOffset
                        : -Parameters.GuardCenterOffset;
                    float guardLead = index == 6
                        ? Parameters.GuardLeadLength * 0.765f
                        : Parameters.GuardLeadLength;
                    float guardTrail = index == 6
                        ? Parameters.GuardTrailLength * 0.765f
                        : Parameters.GuardTrailLength;
                    LineCurve guardCurve = Slice(
                        opposite.Curve,
                        center - guardLead,
                        center + guardTrail)
                        .Parallel(offset);
                    guards.Add(new GuardRailPlan(
                        "v2-guard:" + index++,
                        frog.Id,
                        routeId,
                        opposite,
                        FlareGuardRailEnds(guardCurve)));
                }

                if (frog.Intersection.Kind == RailIntersectionKind.CrossingFrogCandidate
                    && TryBuildLocalCrossingGuard(
                        frog,
                        blades,
                        out RailCenterline guardOwner,
                        out LineCurve localGuard))
                {
                    guards.Add(new GuardRailPlan(
                        "v2-guard:" + index++,
                        frog.Id,
                        guardOwner.SourceRouteIds.FirstOrDefault() ?? string.Empty,
                        guardOwner,
                        FlareGuardRailEndsAwayFrom(localGuard, frog.Intersection.Position)));
                }
            }

            AddDualBothDivergeSupplementalGuards(definition, rails, frogList, guards, ref index);
            return guards;
        }

        private static void AddDualBothDivergeSupplementalGuards(
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<FrogCandidate> frogs,
            ICollection<GuardRailPlan> guards,
            ref int index)
        {
            if (!IsDualBothDivergePreset(definition))
            {
                return;
            }

            RailCenterline? leftHandFrogRail = FindRail(rails, "narrow-reversed", RailSide.Left);
            FrogCandidate[] leftHandFrogs = leftHandFrogRail == null
                ? Array.Empty<FrogCandidate>()
                : frogs
                    .Where(frog =>
                        frog.Intersection.Kind == RailIntersectionKind.VeeFrogCandidate
                        && (frog.Intersection.RailA == leftHandFrogRail
                            || frog.Intersection.RailB == leftHandFrogRail))
                    .ToArray();
            if (leftHandFrogs.Length >= 2)
            {
                AddSupplementalGuardPair(
                    FindRail(rails, "standard-reversed", RailSide.Right),
                    leftHandFrogs,
                    guards,
                    ref index,
                    invertOffset: true);
                return;
            }

            RailCenterline? narrowRight = FindRail(rails, "narrow-reversed", RailSide.Right);
            RailCenterline? standardRight = FindRail(rails, "standard-reversed", RailSide.Right);
            FrogCandidate[] rightHandFrogs = frogs
                .Where(frog =>
                    frog.Intersection.Kind == RailIntersectionKind.VeeFrogCandidate
                    && (narrowRight != null
                        && (frog.Intersection.RailA == narrowRight
                            || frog.Intersection.RailB == narrowRight)
                        || standardRight != null
                        && (frog.Intersection.RailA == standardRight
                            || frog.Intersection.RailB == standardRight)))
                .ToArray();
            AddSupplementalGuardPair(
                FindRail(rails, "narrow-normal", RailSide.Right),
                rightHandFrogs,
                guards,
                ref index,
                invertOffset: true);
        }

        private static void AddSupplementalGuardPair(
            RailCenterline? guardRail,
            IEnumerable<FrogCandidate> frogs,
            ICollection<GuardRailPlan> guards,
            ref int index,
            bool invertOffset)
        {
            if (guardRail == null)
            {
                return;
            }

            foreach (FrogCandidate frog in frogs
                .GroupBy(frog => frog.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(frog => guardRail.Curve.DistanceTo(frog.Intersection.Position))
                .Take(2))
            {
                float center = guardRail.Curve.DistanceTo(frog.Intersection.Position);
                float offset = GuardOffsetAwayFromPoint(
                    guardRail,
                    center,
                    frog.Intersection.Position);
                if (invertOffset)
                {
                    offset = -offset;
                }

                LineCurve guardCurve = Slice(
                    guardRail.Curve,
                    center - Parameters.GuardLeadLength,
                    center + Parameters.GuardTrailLength)
                    .Parallel(offset);
                guards.Add(new GuardRailPlan(
                    "v2-guard:" + index++,
                    frog.Id,
                    guardRail.SourceRouteIds.FirstOrDefault() ?? string.Empty,
                    guardRail,
                    FlareGuardRailEnds(guardCurve, reverseFlare: offset > 0f)));
            }
        }

        private static bool TryBuildLocalCrossingGuard(
            FrogCandidate frog,
            IReadOnlyList<SwitchBladePlan> blades,
            out RailCenterline guardOwner,
            out LineCurve guardCurve)
        {
            guardOwner = null!;
            guardCurve = null!;
            RailCenterline[] crossingRails =
            {
                frog.Intersection.RailA,
                frog.Intersection.RailB
            };
            RailCenterline? selectedGuardOwner = crossingRails.FirstOrDefault(rail =>
                rail.SourceRouteIds.Contains(
                    "narrow-reversed",
                    StringComparer.OrdinalIgnoreCase))
                ?? crossingRails.FirstOrDefault(rail =>
                    rail.Family == GaugeGraphFamily.Narrow);
            RailCenterline? other = crossingRails.FirstOrDefault(rail => rail != selectedGuardOwner);
            if (selectedGuardOwner == null || other == null)
            {
                return false;
            }

            guardOwner = selectedGuardOwner;
            float narrowCenter = guardOwner == frog.Intersection.RailA
                ? frog.Intersection.DistanceA
                : frog.Intersection.DistanceB;
            float standardCenter = other == frog.Intersection.RailA
                ? frog.Intersection.DistanceA
                : frog.Intersection.DistanceB;
            Vector3 bladeDirection = DirectionTowardBlades(frog, blades);
            float standardBladeSide = SideTowardDirection(
                other,
                standardCenter,
                frog.Intersection.Position,
                bladeDirection);
            float narrowBladeSide = SideTowardDirection(
                guardOwner,
                narrowCenter,
                frog.Intersection.Position,
                bladeDirection);
            LinePoint standardGuardBoundary = PointAtSignedOffset(
                other,
                standardCenter,
                -standardBladeSide * frog.CutHalfLength);
            LinePoint narrowGuardBoundary = PointAtSignedOffset(
                guardOwner,
                narrowCenter,
                narrowBladeSide * frog.CutHalfLength);
            LineCurve guardCenterPath = BuildKinkedHandoff(
                standardGuardBoundary,
                narrowGuardBoundary,
                frog.Intersection.Position);

            LinePoint standardStockBoundary = PointAtSignedOffset(
                other,
                standardCenter,
                standardBladeSide * frog.CutHalfLength);
            LinePoint narrowStockBoundary = PointAtSignedOffset(
                guardOwner,
                narrowCenter,
                -narrowBladeSide * frog.CutHalfLength);
            LineCurve stockHandoff = BuildKinkedHandoff(
                standardStockBoundary,
                narrowStockBoundary,
                frog.Intersection.Position);

            LineCurve positive = guardCenterPath.Parallel(Parameters.GuardCenterOffset);
            LineCurve negative = guardCenterPath.Parallel(-Parameters.GuardCenterOffset);
            Vector3 positiveMiddle = positive.LinePointAtDistance(positive.Length * 0.5f).point;
            Vector3 negativeMiddle = negative.LinePointAtDistance(negative.Length * 0.5f).point;

            // The special K-frog check rail follows the opposite crossing
            // diagonal from the bent stock handoff. Pick the offset that opens
            // away from the handoff corridor so the flangeway stays clear.
            LineCurve selectedGuard = DistancePointToCurve(positiveMiddle, stockHandoff)
                >= DistancePointToCurve(negativeMiddle, stockHandoff)
                ? positive
                : negative;
            float guardShift = DistancePointToCurve(positiveMiddle, stockHandoff)
                >= DistancePointToCurve(negativeMiddle, stockHandoff)
                ? -RailHeadWidth
                : RailHeadWidth;
            guardCurve = selectedGuard.Parallel(guardShift);
            return true;
        }

        private static LineCurve BuildKinkedHandoff(
            LinePoint start,
            LinePoint end,
            Vector3 crossing)
        {
            Vector3 span = end.point - start.point;
            Vector3 startDirection = start.direction;
            Vector3 endDirection = end.direction;
            span.y = 0f;
            startDirection.y = 0f;
            endDirection.y = 0f;
            if (Vector3.Dot(startDirection, span) < 0f)
            {
                startDirection = -startDirection;
            }
            if (Vector3.Dot(endDirection, span) < 0f)
            {
                endDirection = -endDirection;
            }

            startDirection = startDirection.sqrMagnitude > 0.0001f
                ? startDirection.normalized
                : span.normalized;
            endDirection = endDirection.sqrMagnitude > 0.0001f
                ? endDirection.normalized
                : span.normalized;
            Vector3 kinkDirection = (startDirection + endDirection).normalized;
            if (kinkDirection.sqrMagnitude <= 0.0001f)
            {
                kinkDirection = span.normalized;
            }

            return new LineCurve(
                new[]
                {
                    new LinePoint(start.point, Quaternion.LookRotation(startDirection, Vector3.up)),
                    new LinePoint(crossing, Quaternion.LookRotation(kinkDirection, Vector3.up)),
                    new LinePoint(end.point, Quaternion.LookRotation(endDirection, Vector3.up))
                },
                Hand.Left);
        }

        private static Vector3 DirectionTowardBlades(
            FrogCandidate frog,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            Vector3 bladeCenter = blades.Count == 0
                ? frog.Intersection.Position + frog.NoseDirection
                : blades
                    .Select(blade => blade.BladeCurve.Head.point)
                    .Aggregate(Vector3.zero, (sum, point) => sum + point) / blades.Count;
            Vector3 direction = bladeCenter - frog.Intersection.Position;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : frog.NoseDirection.normalized;
        }

        private static float SideTowardDirection(
            RailCenterline rail,
            float centerDistance,
            Vector3 center,
            Vector3 direction)
        {
            const float sampleDistance = 0.35f;
            LinePoint before = rail.Curve.LinePointAtDistance(
                Mathf.Max(0f, centerDistance - sampleDistance));
            LinePoint after = rail.Curve.LinePointAtDistance(
                Mathf.Min(rail.Curve.Length, centerDistance + sampleDistance));
            return Vector3.Dot(after.point - center, direction)
                >= Vector3.Dot(before.point - center, direction)
                ? 1f
                : -1f;
        }

        private static LinePoint PointAtSignedOffset(
            RailCenterline rail,
            float centerDistance,
            float signedOffset)
        {
            return rail.Curve.LinePointAtDistance(
                Mathf.Clamp(centerDistance + signedOffset, 0f, rail.Curve.Length));
        }

        private static float GuardOffsetAwayFromPoint(
            RailCenterline rail,
            float centerDistance,
            Vector3 avoidPoint)
        {
            LinePoint center = rail.Curve.LinePointAtDistance(
                Mathf.Clamp(centerDistance, 0f, rail.Curve.Length));
            Vector3 rightPoint = center.point + center.Rotation * Vector3.right * Parameters.GuardCenterOffset;
            Vector3 leftPoint = center.point + center.Rotation * Vector3.left * Parameters.GuardCenterOffset;
            return Vector3.SqrMagnitude(rightPoint - avoidPoint)
                >= Vector3.SqrMagnitude(leftPoint - avoidPoint)
                    ? Parameters.GuardCenterOffset
                    : -Parameters.GuardCenterOffset;
        }

        private static LineCurve FlareGuardRailEnds(
            LineCurve curve,
            bool reverseFlare = false)
        {
            const float flareLength = 0.25f;
            const float flareAngle = 10f;
            if (curve.Length <= flareLength * 2f)
            {
                return curve;
            }

            float lateral = Mathf.Tan(flareAngle * Mathf.Deg2Rad) * flareLength;
            Vector3 flareSide = curve.hand == Hand.Left ? Vector3.right : Vector3.left;
            float signedAngle = curve.hand == Hand.Left ? -flareAngle : flareAngle;
            if (reverseFlare)
            {
                flareSide = -flareSide;
                signedAngle = -signedAngle;
            }

            Quaternion flareRotation = Quaternion.Euler(0f, signedAngle, 0f);

            LinePoint head = curve.Head;
            LineCurve flared = curve.Skip(flareLength, false);
            flared.Insert(
                0,
                new LinePoint(
                    head.point + head.Rotation * flareSide * lateral,
                    flareRotation * head.Rotation));

            flared = flared.Reverse();
            head = flared.Head;
            flared = flared.Skip(flareLength, false);
            flared.Insert(
                0,
                new LinePoint(
                    head.point + head.Rotation * flareSide * lateral,
                    flareRotation * head.Rotation));
            return flared.Reverse();
        }

        private static LineCurve FlareGuardRailEndsAwayFrom(
            LineCurve curve,
            Vector3 avoidPoint)
        {
            const float flareLength = 0.25f;
            const float flareAngle = 10f;
            if (curve.Length <= flareLength * 2f)
            {
                return curve;
            }

            float lateral = Mathf.Tan(flareAngle * Mathf.Deg2Rad) * flareLength;

            LinePoint head = curve.Head;
            LineCurve flared = curve.Skip(flareLength, false);
            flared.Insert(0, BuildOutwardFlarePoint(head, avoidPoint, lateral, flareAngle));

            flared = flared.Reverse();
            head = flared.Head;
            flared = flared.Skip(flareLength, false);
            flared.Insert(0, BuildOutwardFlarePoint(head, avoidPoint, lateral, flareAngle));
            return flared.Reverse();
        }

        private static LinePoint BuildOutwardFlarePoint(
            LinePoint end,
            Vector3 avoidPoint,
            float lateral,
            float flareAngle)
        {
            Vector3 right = end.Rotation * Vector3.right;
            Vector3 left = end.Rotation * Vector3.left;
            Vector3 rightPoint = end.point + right * lateral;
            Vector3 leftPoint = end.point + left * lateral;
            bool useRight = Vector3.SqrMagnitude(rightPoint - avoidPoint)
                >= Vector3.SqrMagnitude(leftPoint - avoidPoint);
            Vector3 flareSide = useRight ? Vector3.right : Vector3.left;
            float signedAngle = useRight ? flareAngle : -flareAngle;
            return new LinePoint(
                end.point + end.Rotation * flareSide * lateral,
                Quaternion.Euler(0f, signedAngle, 0f) * end.Rotation);
        }

        private static IEnumerable<string> ValidateSectionedDualGaugeSpecialWork(
            Graph graph,
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<RailRoleSection> sections,
            IReadOnlyList<RailSuppressionInterval> suppressions,
            IReadOnlyList<RailPiece> fixedPieces,
            IReadOnlyList<RailPiece> frogPieces,
            IReadOnlyList<WingRailPlan> wings,
            IReadOnlyList<GuardRailPlan> guards,
            IReadOnlyList<SharedRailInterval> shared,
            IReadOnlyList<RailCut> cuts,
            IReadOnlyList<FrogCandidate> frogs,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            foreach (string issue in ValidateTopology(graph, definition))
            {
                yield return issue;
            }

            if (definition.SwitchGroups.Count > 0 && blades.Count == 0)
            {
                Main.Warn(
                    $"[Validation] Switch groups exist but no route-divergence blade plans were derived for '{definition.Id}'. Rendering anyway.");
            }

            foreach (SwitchBladePlan blade in blades)
            {
                if (IsDualNarrowBranchPreset(definition)
                    && blade.MovableRail.Family != GaugeGraphFamily.Narrow)
                {
                    yield return $"Unexpected standard-family blade '{blade.Id}'.";
                }

                if (blade.StockRail == null)
                {
                    yield return $"Movable blade '{blade.Id}' has no stock rail.";
                }

                if (blade.RootDistance <= blade.TipDistance + MinimumPieceLength)
                {
                    yield return
                        $"Movable blade '{blade.Id}' has invalid tip/root distances " +
                        $"{blade.TipDistance:0.000}-{blade.RootDistance:0.000}.";
                }

                if (!HasApproachSection(
                    sections,
                    shared,
                    blade.MovableRail,
                    blade.RootDistance,
                    before: false))
                {
                    Main.Warn(
                        $"[Validation] Blade '{blade.Id}' does not connect into a rendered closure/fixed section " +
                        $"after root {blade.RootDistance:0.000}. Rendering anyway.");
                }
            }

            if (IsDualNarrowBranchPreset(definition))
            {
                RailCenterline? divergingFixed = ResolveDivergingFixedStockRail(
                    definition,
                    rails,
                    blades);
                if (divergingFixed == null)
                {
                    yield return "Missing fixed diverging narrow stock/running rail from truth/anatomy.";
                }
                else
                {
                    if (!sections.Any(section =>
                        section.Rail == divergingFixed
                        && section.Role != RailRole.SuppressedRail
                        && section.StartDistance < section.EndDistance))
                    {
                        Main.Warn("[Validation] Fixed diverging narrow stock/running rail has no renderable role sections. Rendering anyway.");
                    }

                    RailCut? firstFrogCut = cuts
                        .Where(cut => cut.Rail == divergingFixed && cut.Kind == RailCutKind.FrogGap)
                        .OrderBy(cut => cut.StartDistance)
                        .FirstOrDefault();
                    SwitchBladePlan? straightBlade = blades.FirstOrDefault(blade => blade.StockRail == divergingFixed);
                    if (firstFrogCut != null
                        && straightBlade != null
                        && !HasContinuousResolvedSectionChain(
                            sections,
                            divergingFixed,
                            straightBlade.TipDistance,
                            firstFrogCut.StartDistance))
                    {
                        yield return
                            $"Fixed diverging narrow stock/running rail is not continuous to frog cut " +
                            $"{firstFrogCut.StartDistance:0.000}.";
                    }
                }
            }

            foreach (SwitchBladePlan blade in blades)
            {
                foreach (RailPiece piece in fixedPieces.Where(piece =>
                    !IsValidStockCorridorPiece(definition, piece, blade)
                    && CurveOverlapLength(piece.Curve, blade.BladeCurve) > 0.2f))
                {
                    yield return
                        $"Fixed rail '{piece.SourceRailId}' renders under blade '{blade.Id}'.";
                }
            }

            foreach (RailPiece piece in fixedPieces)
            {
                RailRoleSection? section = sections.FirstOrDefault(item => item.Id == piece.SourcePlanId);
                if (section == null || section.Role == RailRole.Unknown || section.Role == RailRole.SuppressedRail)
                {
                    yield return $"Unknown/suppressed section rendered as piece '{piece.Id}'.";
                }
            }

            foreach (RailRoleSection section in sections.Where(section => section.Role == RailRole.Unknown))
            {
                yield return
                    $"Unknown role section remains unresolved: rail={section.Rail.Id} " +
                    $"{section.StartDistance:0.000}-{section.EndDistance:0.000}.";
            }

            foreach (RailCut cut in cuts.Where(cut => cut.Kind == RailCutKind.FrogGap))
            {
                foreach (RailPiece piece in fixedPieces.Where(piece =>
                    string.Equals(piece.SourceRailId, cut.Rail.Id, StringComparison.OrdinalIgnoreCase)
                    && IntervalsOverlap(
                        piece.StartDistance,
                        piece.EndDistance,
                        cut.StartDistance,
                        cut.EndDistance)))
                {
                    yield return
                        $"Rail rendered through frog cut zone: rail={cut.Rail.Id} " +
                        $"piece={piece.Id} cut={cut.StartDistance:0.000}-{cut.EndDistance:0.000}.";
                }
            }

            IReadOnlyDictionary<string, RailCenterline>? corridorOwners =
                IsDualBothDivergePreset(definition)
                    ? BuildSharedCorridorOwners(rails, shared, blades)
                    : null;
            foreach (SharedRailInterval interval in shared)
            {
                RailCenterline? railA = rails.FirstOrDefault(rail => rail.Id == interval.RailAId);
                RailCenterline? railB = rails.FirstOrDefault(rail => rail.Id == interval.RailBId);
                if (railA == null || railB == null)
                {
                    continue;
                }

                RailCenterline owner = corridorOwners != null
                    ? corridorOwners[railA.Id]
                    : ChooseSharedOwner(railA, railB, blades);
                if (owner != railA && owner != railB)
                {
                    continue;
                }

                RailCenterline loser = owner == railA ? railB : railA;
                if (IsDualBothDivergePreset(definition)
                    && RailParticipatesInAcceptedFrog(loser, frogs))
                {
                    continue;
                }

                if (!definition.Preset.SupportsGhostGraph
                    && fixedPieces.Any(piece => piece.SourceRailId == loser.Id
                        && DistancePointToSegment(
                            piece.Curve.LinePointAtDistance(piece.Curve.Length * 0.5f).point,
                            interval.Start,
                            interval.End) <= CorridorTolerance))
                {
                    yield return $"Shared duplicate rail '{loser.Id}' still renders.";
                }
            }

            foreach (FrogCandidate frog in frogs)
            {
                if (string.IsNullOrWhiteSpace(frog.OwnerRouteId)
                    || string.IsNullOrWhiteSpace(frog.CrossingRouteId))
                {
                    yield return $"Frog candidate '{frog.Id}' has no owner route.";
                }

                if (string.IsNullOrWhiteSpace(frog.ProtectedRouteId))
                {
                    yield return $"Frog candidate '{frog.Id}' has no protected wheel path.";
                }

                if (!cuts.Any(cut => cut.Rail == frog.Intersection.RailA && cut.Kind == RailCutKind.FrogGap)
                    || !cuts.Any(cut => cut.Rail == frog.Intersection.RailB && cut.Kind == RailCutKind.FrogGap))
                {
                    yield return $"Frog candidate '{frog.Id}' is missing required rail cuts.";
                }
            }

            foreach (string issue in ValidateFrogReplacementPlans(
                sections,
                shared,
                cuts,
                frogs,
                frogPieces,
                wings,
                guards))
            {
                yield return issue;
            }

            foreach (string issue in ValidateSuppressionCoverage(
                suppressions,
                fixedPieces,
                frogPieces,
                wings,
                guards,
                blades,
                frogs))
            {
                yield return issue;
            }
        }

        private static bool HasContinuousResolvedSectionChain(
            IEnumerable<RailRoleSection> sections,
            RailCenterline rail,
            float requiredStart,
            float requiredEnd)
        {
            float start = Mathf.Min(requiredStart, requiredEnd);
            float end = Mathf.Max(requiredStart, requiredEnd);
            RailRoleSection[] candidates = sections
                .Where(section =>
                    section.Rail == rail
                    && section.Role != RailRole.Unknown
                    && IntervalsOverlap(section.StartDistance, section.EndDistance, start, end))
                .OrderBy(section => section.StartDistance)
                .ToArray();
            if (candidates.Length == 0 || candidates[0].StartDistance > start + 0.15f)
            {
                return false;
            }

            float coveredTo = candidates[0].EndDistance;
            foreach (RailRoleSection section in candidates.Skip(1))
            {
                if (section.StartDistance > coveredTo + 0.08f)
                {
                    return false;
                }

                coveredTo = Mathf.Max(coveredTo, section.EndDistance);
                if (coveredTo >= end - 0.15f)
                {
                    return true;
                }
            }

            return coveredTo >= end - 0.15f;
        }

        private static IEnumerable<string> ValidateSharedRailContinuity(
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<SharedRailInterval> shared,
            IReadOnlyList<RailRoleSection> sections,
            IReadOnlyList<RailPiece> fixedPieces,
            IReadOnlyList<RailCut> cuts,
            IReadOnlyList<RailSuppressionInterval> suppressions,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            foreach (SharedRailInterval interval in shared)
            {
                RailCenterline? railA = rails.FirstOrDefault(rail => rail.Id == interval.RailAId);
                RailCenterline? railB = rails.FirstOrDefault(rail => rail.Id == interval.RailBId);
                if (railA == null || railB == null)
                {
                    yield return
                        $"Shared rail interval references missing rails '{interval.RailAId}'/'{interval.RailBId}'.";
                    continue;
                }

                RailCenterline preferredOwner = ChooseSharedOwner(railA, railB, blades);
                RailCenterline loser = preferredOwner == railA ? railB : railA;
                float ownerStart = preferredOwner.Curve.DistanceTo(interval.Start);
                float ownerEnd = preferredOwner.Curve.DistanceTo(interval.End);
                float loserStart = loser.Curve.DistanceTo(interval.Start);
                float loserEnd = loser.Curve.DistanceTo(interval.End);
                float loserMin = Mathf.Min(loserStart, loserEnd);
                float loserMax = Mathf.Max(loserStart, loserEnd);
                if (!suppressions.Any(item =>
                    item.Rail == loser
                    && item.Reason.IndexOf("shared duplicate", StringComparison.OrdinalIgnoreCase) >= 0
                    && item.StartDistance <= loserMin + CorridorTolerance
                    && item.EndDistance >= loserMax - CorridorTolerance))
                {
                    yield return
                        $"Shared duplicate rail '{loser.Id}' is not suppressed for interval " +
                        $"{loserMin:0.000}-{loserMax:0.000}.";
                }

                RailRoleSection[] ownerSections = sections
                    .Where(section => section.Rail == preferredOwner)
                    .ToArray();
                if (ownerSections.Length == 0)
                {
                    continue;
                }

                float start = Mathf.Max(
                    Mathf.Min(ownerStart, ownerEnd),
                    ownerSections.Min(section => section.StartDistance));
                float end = Mathf.Min(
                    Mathf.Max(ownerStart, ownerEnd),
                    ownerSections.Max(section => section.EndDistance));
                if (end - start < MinimumPieceLength)
                {
                    continue;
                }

                for (float distance = start; distance <= end + 0.025f; distance += 0.1f)
                {
                    float sample = Mathf.Min(distance, end);
                    Vector3 point = preferredOwner.Curve.LinePointAtDistance(sample).point;
                    bool covered = fixedPieces.Any(piece =>
                        DistancePointToCurve(point, piece.Curve) <= CorridorTolerance);
                    bool validFrogGap = cuts.Any(cut =>
                        cut.Kind == RailCutKind.FrogGap
                        && DistancePointToCurve(
                            point,
                            Slice(cut.Rail.Curve, cut.StartDistance, cut.EndDistance))
                            <= CorridorTolerance);
                    if (!covered && !validFrogGap)
                    {
                        yield return
                            $"Shared physical corridor '{railA.Id}'/'{railB.Id}' loses continuity " +
                            $"near {sample:0.000} on preferred owner '{preferredOwner.Id}' " +
                            $"without a frog/crossing cut.";
                        break;
                    }
                }
            }
        }

        private static IEnumerable<string> ValidateFrogReplacementPlans(
            IReadOnlyList<RailRoleSection> sections,
            IReadOnlyList<SharedRailInterval> shared,
            IReadOnlyList<RailCut> cuts,
            IReadOnlyList<FrogCandidate> frogs,
            IReadOnlyList<RailPiece> frogPieces,
            IReadOnlyList<WingRailPlan> wings,
            IReadOnlyList<GuardRailPlan> guards)
        {
            foreach (FrogCandidate frog in frogs)
            {
                if (frog.Intersection.Kind == RailIntersectionKind.VeeFrogCandidate
                    && frogPieces.Count(piece => piece.SourcePlanId == frog.Id) < 2)
                {
                    yield return $"Frog '{frog.Id}' has no complete frog replacement pieces.";
                }

                foreach ((RailCenterline rail, float distance) in new[]
                {
                    (frog.Intersection.RailA, frog.Intersection.DistanceA),
                    (frog.Intersection.RailB, frog.Intersection.DistanceB)
                })
                {
                    RailCut? cut = cuts.FirstOrDefault(item =>
                        item.Rail == rail
                        && item.Kind == RailCutKind.FrogGap
                        && item.StartDistance <= distance
                        && item.EndDistance >= distance);
                    if (cut == null)
                    {
                        yield return $"Frog '{frog.Id}' does not cut rail '{rail.Id}'.";
                        continue;
                    }

                    if (!HasApproachSection(sections, shared, rail, cut.StartDistance, before: true))
                    {
                        yield return
                            $"Frog '{frog.Id}' rail '{rail.Id}' has no rendered approach section before cut " +
                            $"{cut.StartDistance:0.000}.";
                    }

                    if (!HasApproachSection(sections, shared, rail, cut.EndDistance, before: false))
                    {
                        yield return
                            $"Frog '{frog.Id}' rail '{rail.Id}' has no rendered exit section after cut " +
                            $"{cut.EndDistance:0.000}.";
                    }
                }
            }
        }

        private static bool HasApproachSection(
            IReadOnlyList<RailRoleSection> sections,
            IReadOnlyList<SharedRailInterval> shared,
            RailCenterline rail,
            float boundary,
            bool before)
        {
            bool HasRenderedSection(RailRoleSection section) =>
                section.Role != RailRole.Unknown
                && section.Role != RailRole.SuppressedRail;

            if (sections.Any(section =>
                section.Rail == rail
                && HasRenderedSection(section)
                && (before
                    ? section.EndDistance >= boundary - 0.15f && section.StartDistance < boundary
                    : section.StartDistance <= boundary + 0.15f && section.EndDistance > boundary)))
            {
                return true;
            }

            if (IsSharedDuplicateAtBoundary(sections, rail, boundary, before))
            {
                return true;
            }

            float sampleDistance = Mathf.Clamp(
                boundary + (before ? -0.1f : 0.1f),
                0f,
                rail.Curve.Length);
            Vector3 samplePoint = rail.Curve.LinePointAtDistance(sampleDistance).point;
            foreach (SharedRailInterval interval in shared.Where(item =>
                (string.Equals(item.RailAId, rail.Id, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.RailBId, rail.Id, StringComparison.OrdinalIgnoreCase))
                && DistancePointToSegment(samplePoint, item.Start, item.End) <= CorridorTolerance))
            {
                string pairedRailId = string.Equals(
                    interval.RailAId,
                    rail.Id,
                    StringComparison.OrdinalIgnoreCase)
                        ? interval.RailBId
                        : interval.RailAId;
                if (sections.Any(section =>
                    string.Equals(section.Rail.Id, pairedRailId, StringComparison.OrdinalIgnoreCase)
                    && HasRenderedSection(section)
                    && DistancePointToCurve(
                        samplePoint,
                        Slice(section.Rail.Curve, section.StartDistance, section.EndDistance))
                        <= CorridorTolerance))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSharedDuplicateAtBoundary(
            IReadOnlyList<RailRoleSection> sections,
            RailCenterline rail,
            float boundary,
            bool before)
        {
            return sections.Any(section =>
                section.Rail == rail
                && section.Role == RailRole.SuppressedRail
                && (before
                    ? section.EndDistance >= boundary - 0.15f && section.StartDistance < boundary
                    : section.StartDistance <= boundary + 0.15f && section.EndDistance > boundary));
        }

        private static RailCenterline? ResolveDivergingFixedStockRail(
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            return blades
                .Where(blade => blade.StockRail.Family == GaugeGraphFamily.Narrow)
                .Select(blade => blade.StockRail)
                .FirstOrDefault();
        }

        private static IEnumerable<string> ValidateSuppressionCoverage(
            IReadOnlyList<RailSuppressionInterval> suppressions,
            IReadOnlyList<RailPiece> fixedPieces,
            IReadOnlyList<RailPiece> frogPieces,
            IReadOnlyList<WingRailPlan> wings,
            IReadOnlyList<GuardRailPlan> guards,
            IReadOnlyList<SwitchBladePlan> blades,
            IReadOnlyList<FrogCandidate> frogs)
        {
            foreach (RailSuppressionInterval suppression in suppressions)
            {
                string expected = ExpectedReplacementType(suppression);
                string[] actual = ActualReplacementIds(
                    suppression,
                    fixedPieces,
                    frogPieces,
                    wings,
                    guards,
                    blades,
                    frogs).ToArray();
                string detail =
                    $"RailId={suppression.Rail.Id} " +
                    $"StartDistance={suppression.StartDistance:0.000} " +
                    $"EndDistance={suppression.EndDistance:0.000} " +
                    $"SuppressionReason={suppression.Reason} " +
                    $"ExpectedReplacementType={expected} " +
                    $"ActualReplacementPieceIds={(actual.Length == 0 ? "<none>" : string.Join(",", actual))}";

                if (expected == "EmptyBladeClearance"
                    || expected == "EmptySharedDuplicate"
                    || expected == "EmptyOmittedRail")
                {
                    continue;
                }

                if (actual.Length == 0)
                {
                    yield return "Suppressed interval missing replacement piece: " + detail;
                }
            }
        }

        private static string ExpectedReplacementType(RailSuppressionInterval suppression)
        {
            string reason = suppression.Reason ?? string.Empty;
            if (reason.IndexOf("movable blade", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "MovablePointBlade";
            }

            if (reason.IndexOf("frog gap", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "FrogRail/WingRail/GuardRail";
            }

            if (reason.IndexOf("shared duplicate", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "EmptySharedDuplicate";
            }

            if (reason.IndexOf("fixed rail under blade", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "EmptyBladeClearance";
            }

            return "ReplacementPiece";
        }

        private static IEnumerable<string> ActualReplacementIds(
            RailSuppressionInterval suppression,
            IReadOnlyList<RailPiece> fixedPieces,
            IReadOnlyList<RailPiece> frogPieces,
            IReadOnlyList<WingRailPlan> wings,
            IReadOnlyList<GuardRailPlan> guards,
            IReadOnlyList<SwitchBladePlan> blades,
            IReadOnlyList<FrogCandidate> frogs)
        {
            foreach (SwitchBladePlan blade in blades.Where(blade =>
                blade.MovableRail == suppression.Rail
                && IntervalsOverlap(
                    suppression.StartDistance,
                    suppression.EndDistance,
                    blade.TipDistance,
                    blade.RootDistance)))
            {
                yield return blade.Id;
            }

            foreach (RailPiece piece in frogPieces.Where(piece =>
                string.Equals(piece.SourceRailId, suppression.Rail.Id, StringComparison.OrdinalIgnoreCase)
                && IntervalsOverlap(
                    suppression.StartDistance,
                    suppression.EndDistance,
                    piece.StartDistance,
                    piece.EndDistance)))
            {
                yield return piece.Id;
            }

            foreach (WingRailPlan wing in wings.Where(wing =>
                wing.SourceRail == suppression.Rail
                && CurveOverlapLength(
                    wing.Curve,
                    Slice(suppression.Rail.Curve, suppression.StartDistance, suppression.EndDistance)) > MinimumPieceLength))
            {
                yield return wing.Id;
            }

            foreach (FrogCandidate frog in frogs.Where(frog =>
                string.Equals("frog gap " + frog.Id, suppression.Reason, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (GuardRailPlan guard in guards.Where(guard => guard.FrogId == frog.Id))
                {
                    yield return guard.Id;
                }
            }

            if (ExpectedReplacementType(suppression) == "EmptySharedDuplicate")
            {
                LineCurve suppressedCurve =
                    Slice(suppression.Rail.Curve, suppression.StartDistance, suppression.EndDistance);
                foreach (RailPiece piece in fixedPieces.Where(piece =>
                    !string.Equals(piece.SourceRailId, suppression.Rail.Id, StringComparison.OrdinalIgnoreCase)
                    && CurveOverlapLength(piece.Curve, suppressedCurve) > MinimumPieceLength))
                {
                    yield return piece.Id;
                }
            }
        }

        private static IEnumerable<GeometryDebugLabel> BuildDebugLabels(
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<RailRoleSection> sections,
            IReadOnlyList<RailSuppressionInterval> suppressions,
            IReadOnlyList<RailCut> cuts,
            IReadOnlyList<RailPiece> fixedPieces,
            IReadOnlyList<RailPiece> frogPieces,
            IReadOnlyList<WingRailPlan> wings,
            IReadOnlyList<GuardRailPlan> guards,
            IReadOnlyList<SwitchBladePlan> blades,
            IReadOnlyList<FrogCandidate> frogs)
        {
            foreach (RailCenterline rail in rails)
            {
                yield return new GeometryDebugLabel(
                    $"RailId={rail.Id} Owner={string.Join(",", rail.SourceRouteIds)} Side={rail.Side}",
                    rail.Curve.LinePointAtDistance(rail.Curve.Length * 0.5f).point,
                    Color.white,
                    rail.Curve);
            }

            foreach (RailRoleSection section in sections)
            {
                LineCurve reference = Slice(
                    section.Rail.Curve,
                    section.StartDistance,
                    section.EndDistance);
                yield return new GeometryDebugLabel(
                    $"{section.Role} {section.Rail.Id} {section.StartDistance:0.00}-{section.EndDistance:0.00} {section.SourceCurveKind}",
                    section.Rail.Curve.LinePointAtDistance((section.StartDistance + section.EndDistance) * 0.5f).point,
                    RoleColor(section.Role),
                    reference);
            }

            foreach (RailSuppressionInterval suppression in suppressions)
            {
                LineCurve reference = Slice(
                    suppression.Rail.Curve,
                    suppression.StartDistance,
                    suppression.EndDistance);
                yield return new GeometryDebugLabel(
                    $"Suppressed {suppression.Rail.Id} {suppression.StartDistance:0.00}-{suppression.EndDistance:0.00} {suppression.Reason}",
                    suppression.Rail.Curve.LinePointAtDistance((suppression.StartDistance + suppression.EndDistance) * 0.5f).point,
                    Color.gray,
                    reference);
            }

            foreach (RailCut cut in cuts)
            {
                LineCurve reference = Slice(
                    cut.Rail.Curve,
                    cut.StartDistance,
                    cut.EndDistance);
                yield return new GeometryDebugLabel(
                    $"{cut.Kind} {cut.Rail.Id} {cut.SourceId}",
                    cut.Rail.Curve.LinePointAtDistance((cut.StartDistance + cut.EndDistance) * 0.5f).point,
                    Color.yellow,
                    reference);
            }

            foreach (RailPiece piece in fixedPieces.Concat(frogPieces))
            {
                yield return new GeometryDebugLabel(
                    $"ReplacementPiece {piece.Id} kind={piece.Kind} rail={piece.SourceRailId}",
                    piece.Curve.LinePointAtDistance(piece.Curve.Length * 0.5f).point,
                    piece.Kind == RailPieceKind.FrogNose ? Color.red : Color.white,
                    piece.Curve);
            }

            foreach (WingRailPlan wing in wings)
            {
                yield return new GeometryDebugLabel(
                    $"ReplacementPiece {wing.Id} kind=WingRail frog={wing.FrogId}",
                    wing.Curve.LinePointAtDistance(wing.Curve.Length * 0.5f).point,
                    new Color(1f, 0.35f, 0.75f, 1f),
                    wing.Curve);
            }

            foreach (GuardRailPlan guard in guards)
            {
                yield return new GeometryDebugLabel(
                    $"ReplacementPiece {guard.Id} kind=GuardRail frog={guard.FrogId} route={guard.ProtectedRouteId}",
                    guard.Curve.LinePointAtDistance(guard.Curve.Length * 0.5f).point,
                    Color.magenta,
                    guard.Curve);
            }

            foreach (SwitchBladePlan blade in blades)
            {
                yield return new GeometryDebugLabel(
                    $"BladeStock {blade.Id} movable={blade.MovableRail.Id} stock={blade.StockRail.Id}",
                    blade.BladeCurve.LinePointAtDistance(blade.BladeCurve.Length * 0.5f).point,
                    Color.yellow,
                    blade.BladeCurve);
            }

            foreach (FrogCandidate frog in frogs)
            {
                yield return new GeometryDebugLabel(
                    $"FrogOwnership {frog.Id} owner={frog.OwnerRouteId} crossing={frog.CrossingRouteId} protected={frog.ProtectedRouteId}",
                    frog.Intersection.Position,
                    Color.red);
            }
        }

        private static void ApplySections(
            IEnumerable<RailCenterline> rails,
            IEnumerable<RailRoleSection> sections,
            IEnumerable<RailSuppressionInterval> suppressions)
        {
            foreach (RailCenterline rail in rails)
            {
                RailRoleSection[] railSections = sections.Where(section => section.Rail == rail).ToArray();
                rail.SetSections(railSections);
                rail.SetSuppressions(suppressions.Where(suppression => suppression.Rail == rail));
                rail.SetRole(railSections.FirstOrDefault(section => section.Role != RailRole.SuppressedRail)?.Role
                    ?? RailRole.Unknown);
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

        private static RailCenterline? FindRail(
            IEnumerable<RailCenterline> rails,
            string routeId,
            RailSide side)
        {
            return rails.FirstOrDefault(rail =>
                rail.Side == side
                && rail.SourceRouteIds.Contains(routeId, StringComparer.OrdinalIgnoreCase));
        }

        private static bool TryFindBladeDistances(
            RailCenterline stock,
            RailCenterline movable,
            Vector3 switchPoint,
            IReadOnlyList<RailIntersection> intersections,
            out float tip,
            out float root)
        {
            LineCurve stockCurve = stock.Curve;
            LineCurve movableCurve = movable.Curve;
            float tipDistance = movableCurve.DistanceTo(switchPoint);
            Vector3 tipPoint = movableCurve.LinePointAtDistance(tipDistance).point;
            float stockTip = stockCurve.DistanceTo(tipPoint);
            if (Vector3.Distance(tipPoint, stockCurve.LinePointAtDistance(stockTip).point)
                > Parameters.RailHeadWidth + Parameters.BladeDivergenceThreshold)
            {
                tip = tipDistance;
                root = tipDistance;
                return false;
            }

            float probe = Mathf.Min(2f, Mathf.Max(0.5f, movableCurve.Length * 0.25f));
            float forwardStockDist = StockSeparation(
                stockCurve,
                movableCurve.LinePointAtDistance(Mathf.Min(movableCurve.Length, tipDistance + probe)).point);
            float backwardStockDist = StockSeparation(
                stockCurve,
                movableCurve.LinePointAtDistance(Mathf.Max(0f, tipDistance - probe)).point);

            int direction = forwardStockDist >= backwardStockDist ? 1 : -1;
            float separationRoot = WalkToSeparation(
                stockCurve,
                movableCurve,
                tipDistance,
                direction,
                Parameters.BladeRootSeparation);
            float bladeLength = Mathf.Abs(separationRoot - tipDistance);

            if (TryFindBaseGameBladeLengthFromFrog(
                    movable,
                    tipDistance,
                    direction,
                    intersections,
                    out float baseGameLength,
                    out float maximumFrogLimitedLength))
            {
                bladeLength = Mathf.Clamp(
                    Mathf.Max(bladeLength, baseGameLength),
                    MinimumPieceLength,
                    maximumFrogLimitedLength);
            }

            float endpoint = Mathf.Clamp(
                tipDistance + direction * bladeLength,
                0f,
                movableCurve.Length);
            if (direction > 0)
            {
                tip = tipDistance;
                root = endpoint;
            }
            else
            {
                tip = endpoint;
                root = tipDistance;
            }

            return root - tip >= MinimumPieceLength;
        }

        private static bool TryFindBaseGameBladeLengthFromFrog(
            RailCenterline rail,
            float switchDistance,
            int direction,
            IEnumerable<RailIntersection> intersections,
            out float preferredLength,
            out float maximumLength)
        {
            preferredLength = 0f;
            maximumLength = 0f;
            if (!TryFindNearestFrogDistance(
                    rail,
                    switchDistance,
                    direction,
                    intersections,
                    out float distanceToFrog))
            {
                return false;
            }

            maximumLength = distanceToFrog - BaseGamePointRailFrogCutoff;
            if (maximumLength < MinimumPieceLength)
            {
                return false;
            }

            preferredLength = Mathf.Max(
                MinimumPieceLength,
                maximumLength * BaseGamePointClosureSplitRatio);
            return true;
        }

        private static bool TryFindNearestFrogDistance(
            RailCenterline rail,
            float switchDistance,
            int direction,
            IEnumerable<RailIntersection> intersections,
            out float distanceToFrog)
        {
            distanceToFrog = 0f;
            float bestDelta = float.PositiveInfinity;
            foreach (RailIntersection intersection in intersections)
            {
                if (!IsFrogCandidateIntersection(intersection)
                    || intersection.AcuteAngleDegrees < MinimumFrogAngle)
                {
                    continue;
                }

                float distance;
                if (intersection.RailA == rail)
                {
                    distance = intersection.DistanceA;
                }
                else if (intersection.RailB == rail)
                {
                    distance = intersection.DistanceB;
                }
                else
                {
                    continue;
                }

                float delta = (distance - switchDistance) * direction;
                if (delta <= MinimumPieceLength || delta >= bestDelta)
                {
                    continue;
                }

                bestDelta = delta;
            }

            if (float.IsPositiveInfinity(bestDelta))
            {
                return false;
            }

            distanceToFrog = bestDelta;
            return true;
        }

        private static bool IsFrogCandidateIntersection(RailIntersection intersection)
        {
            return intersection.Kind == RailIntersectionKind.VeeFrogCandidate
                || intersection.Kind == RailIntersectionKind.CrossingFrogCandidate;
        }

        private static float StockSeparation(LineCurve stock, Vector3 point)
        {
            return Vector3.Distance(
                point,
                stock.LinePointAtDistance(stock.DistanceTo(point)).point);
        }

        private static float WalkToSeparation(
            LineCurve stock,
            LineCurve movable,
            float startDistance,
            int direction,
            float targetSeparation)
        {
            const float step = 0.1f;
            float distance = startDistance;
            float walked = 0f;
            while (walked < MaximumBladeLength)
            {
                float next = Mathf.Clamp(distance + direction * step, 0f, movable.Length);
                if (Mathf.Approximately(next, distance))
                {
                    break;
                }

                distance = next;
                walked += step;
                if (StockSeparation(stock, movable.LinePointAtDistance(distance).point)
                    >= targetSeparation)
                {
                    break;
                }
            }

            return distance;
        }

        private static float[] BuildBoundaries(
            RailCenterline rail,
            RailWorkInterval work,
            IReadOnlyList<RailCut> cuts,
            IReadOnlyList<RailSuppressionInterval> suppressions,
            IReadOnlyList<FrogCandidate> frogs,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            var values = new List<float> { work.StartDistance, work.EndDistance };
            values.AddRange(cuts.Where(cut => cut.Rail == rail).SelectMany(cut =>
                new[] { cut.StartDistance, cut.EndDistance }));
            values.AddRange(suppressions.Where(item => item.Rail == rail).SelectMany(item =>
                new[] { item.StartDistance, item.EndDistance }));
            values.AddRange(blades.Where(blade => blade.MovableRail == rail || blade.StockRail == rail)
                .SelectMany(blade => new[] { blade.TipDistance, blade.RootDistance }));
            foreach (FrogCandidate frog in frogs)
            {
                if (frog.Intersection.RailA == rail)
                {
                    values.Add(frog.Intersection.DistanceA);
                }
                else if (frog.Intersection.RailB == rail)
                {
                    values.Add(frog.Intersection.DistanceB);
                }
            }

            return values
                .Select(value => Mathf.Clamp(value, work.StartDistance, work.EndDistance))
                .GroupBy(value => Mathf.RoundToInt(value * 100f))
                .Select(group => group.First())
                .OrderBy(value => value)
                .ToArray();
        }

        private static bool CanRenderRole(RailRole role)
        {
            return role != RailRole.Unknown
                && role != RailRole.SuppressedRail
                && role != RailRole.MovablePointBlade
                && role != RailRole.PointBlade
                && role != RailRole.WingRail
                && role != RailRole.GuardRail;
        }

        private static RailPieceKind PieceKindForRole(RailRole role)
        {
            switch (role)
            {
                case RailRole.SharedRail:
                    return RailPieceKind.SharedRunning;
                case RailRole.ClosureRail:
                case RailRole.FrogApproachRail:
                    return RailPieceKind.ClosureRail;
                case RailRole.FrogRail:
                    return RailPieceKind.FrogNose;
                default:
                    return RailPieceKind.FixedRunning;
            }
        }

        private static string SourceCurveKind(string routeId)
        {
            return routeId.IndexOf("reversed", StringComparison.OrdinalIgnoreCase) >= 0
                ? "DivergingRoute"
                : "ThroughRoute";
        }

        private static string RoleReason(RailRole role)
        {
            return role.ToString();
        }

        private static bool IsSharedOwnerAt(
            RailCenterline rail,
            IEnumerable<SharedRailInterval> shared,
            float distance)
        {
            Vector3 point = rail.Curve.LinePointAtDistance(distance).point;
            return shared.Any(interval =>
                (interval.RailAId == rail.Id || interval.RailBId == rail.Id)
                && DistancePointToSegment(point, interval.Start, interval.End) <= CorridorTolerance);
        }

        private static RailCenterline ChooseSharedOwner(RailCenterline railA, RailCenterline railB)
        {
            return ChooseSharedOwner(railA, railB, Array.Empty<SwitchBladePlan>());
        }

        private static RailCenterline ChooseSharedOwner(
            RailCenterline railA,
            RailCenterline railB,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            if (railA.Family != railB.Family)
            {
                return railA.Family == GaugeGraphFamily.Standard ? railA : railB;
            }

            bool aStock = blades.Any(blade => blade.StockRail == railA);
            bool bStock = blades.Any(blade => blade.StockRail == railB);
            if (aStock != bStock)
            {
                return aStock ? railA : railB;
            }

            bool aMovable = blades.Any(blade => blade.MovableRail == railA);
            bool bMovable = blades.Any(blade => blade.MovableRail == railB);
            if (aMovable != bMovable)
            {
                return aMovable ? railB : railA;
            }

            bool aDivergingRightStock = IsNarrowDivergingRightRail(railA);
            bool bDivergingRightStock = IsNarrowDivergingRightRail(railB);
            if (aDivergingRightStock != bDivergingRightStock)
            {
                return aDivergingRightStock ? railA : railB;
            }

            return string.Compare(railA.Id, railB.Id, StringComparison.OrdinalIgnoreCase) <= 0
                ? railA
                : railB;
        }

        private static bool RailParticipatesInAcceptedFrog(
            RailCenterline rail,
            IEnumerable<FrogCandidate> frogs)
        {
            return frogs.Any(frog =>
                frog.Intersection.RailA == rail
                || frog.Intersection.RailB == rail);
        }

        private static bool IsNarrowDivergingRightRail(RailCenterline rail)
        {
            return rail.Family == GaugeGraphFamily.Narrow
                && rail.Side == RailSide.Right
                && rail.SourceRouteIds.Contains(
                    "narrow-reversed",
                    StringComparer.OrdinalIgnoreCase);
        }

        private static bool InsideBladeZone(RailIntersection intersection, IEnumerable<SwitchBladePlan> blades)
        {
            return blades.Any(blade =>
                (intersection.RailA == blade.MovableRail || intersection.RailA == blade.StockRail)
                && (intersection.RailB == blade.MovableRail || intersection.RailB == blade.StockRail));
        }

        private static IEnumerable<(float Start, float End)> FindCurveOverlaps(
            LineCurve target,
            LineCurve owner)
        {
            var hits = new List<float>();
            int count = Mathf.Max(2, Mathf.CeilToInt(owner.Length / BladeSampleSpacing) + 1);
            for (int i = 0; i < count; i++)
            {
                float distance = i == count - 1 ? owner.Length : Mathf.Min(owner.Length, i * BladeSampleSpacing);
                Vector3 point = owner.LinePointAtDistance(distance).point;
                float targetDistance = target.DistanceTo(point);
                if (Vector3.Distance(point, target.LinePointAtDistance(targetDistance).point) <= CorridorTolerance)
                {
                    hits.Add(targetDistance);
                }
            }

            if (hits.Count < 2)
            {
                yield break;
            }

            hits.Sort();
            yield return (Mathf.Max(0f, hits.First() - BladeSampleSpacing), Mathf.Min(target.Length, hits.Last() + BladeSampleSpacing));
        }

        private static void AddCut(
            ICollection<RailCut> cuts,
            RailCenterline rail,
            float start,
            float end,
            RailCutKind kind,
            string sourceId)
        {
            var cut = new RailCut("v2-cut:" + cuts.Count, rail, start, end, kind, sourceId);
            if (cut.Length >= MinimumPieceLength)
            {
                cuts.Add(cut);
            }
        }

        private static void AddSuppression(
            ICollection<RailSuppressionInterval> suppressions,
            RailCenterline rail,
            float start,
            float end,
            string reason)
        {
            var suppression = new RailSuppressionInterval(
                "v2-suppression:" + suppressions.Count,
                rail,
                start,
                end,
                reason);
            if (suppression.Length >= MinimumPieceLength)
            {
                suppressions.Add(suppression);
            }
        }

        private static LineCurve Slice(LineCurve curve, float start, float end)
        {
            float clampedStart = Mathf.Clamp(start, 0f, curve.Length);
            float clampedEnd = Mathf.Clamp(end, clampedStart, curve.Length);
            return curve.Skip(clampedStart, true).Take(clampedEnd - clampedStart);
        }

        private static bool IsValidStockCorridorPiece(
            SpecialWorkDefinition definition,
            RailPiece piece,
            SwitchBladePlan blade)
        {
            if (string.Equals(
                piece.SourceRailId,
                blade.StockRail.Id,
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (IsDualBothDivergePreset(definition))
            {
                return false;
            }

            float stockTip = blade.StockRail.Curve.DistanceTo(blade.BladeCurve.Head.point);
            float stockRoot = blade.StockRail.Curve.DistanceTo(blade.BladeCurve.Tail.point);
            LineCurve stockBladeCorridor = Slice(
                blade.StockRail.Curve,
                Mathf.Min(stockTip, stockRoot),
                Mathf.Max(stockTip, stockRoot));

            // Shared dual-gauge rail ownership can assign the visible stock rail
            // to another route-derived centerline. Physical corridor ownership,
            // not the source rail id, determines whether it is valid here.
            return CurveOverlapLength(piece.Curve, stockBladeCorridor) > 0.2f;
        }

        private static float CurveOverlapLength(LineCurve a, LineCurve b)
        {
            float overlap = 0f;
            int count = Mathf.Max(2, Mathf.CeilToInt(b.Length / BladeSampleSpacing) + 1);
            for (int i = 0; i + 1 < count; i++)
            {
                float d0 = Mathf.Min(b.Length, i * BladeSampleSpacing);
                float d1 = i == count - 2 ? b.Length : Mathf.Min(b.Length, (i + 1) * BladeSampleSpacing);
                if (DistancePointToCurve(b.LinePointAtDistance(d0).point, a) <= CorridorTolerance
                    && DistancePointToCurve(b.LinePointAtDistance(d1).point, a) <= CorridorTolerance)
                {
                    overlap += d1 - d0;
                }
            }

            return overlap;
        }

        private static float DistancePointToCurve(Vector3 point, LineCurve curve)
        {
            return curve.Segments.Min(segment =>
                DistancePointToSegment(point, segment.Item2.a.point, segment.Item2.b.point));
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

        private static bool IntervalsOverlap(float aStart, float aEnd, float bStart, float bEnd)
        {
            return aEnd > bStart + 0.01f && bEnd > aStart + 0.01f;
        }

        private static bool TryParseRailSide(string value, out RailSide side)
        {
            return Enum.TryParse(value ?? string.Empty, ignoreCase: true, out side);
        }

        private static Color RoleColor(RailRole role)
        {
            switch (role)
            {
                case RailRole.StockRail:
                    return Color.green;
                case RailRole.MovablePointBlade:
                case RailRole.PointBlade:
                    return Color.yellow;
                case RailRole.ClosureRail:
                case RailRole.FrogApproachRail:
                    return new Color(1f, 0.55f, 0f, 1f);
                case RailRole.FrogRail:
                    return Color.red;
                case RailRole.SharedRail:
                    return new Color(0.1f, 1f, 0.1f, 1f);
                case RailRole.SuppressedRail:
                    return Color.gray;
                default:
                    return Color.white;
            }
        }

        private static IEnumerable<string> ValidateTopology(Graph graph, SpecialWorkDefinition definition)
        {
            foreach (string nodeId in definition.NativeSwitchNodeIds)
            {
                TrackNode? node = graph.GetNode(nodeId);
                int degree = node == null ? 0 : graph.SegmentsConnectedTo(node).Count;
                if (degree > 3)
                {
                    yield return $"Generated node '{nodeId}' has {degree} legs; expected native three-leg switch topology.";
                }
            }
        }

        private readonly struct BladeSpec
        {
            public BladeSpec(
                string label,
                string movableRouteId,
                RailSide movableSide,
                string stockRouteId,
                RailSide stockSide)
            {
                Label = string.IsNullOrWhiteSpace(label) ? movableRouteId + ":" + movableSide : label;
                MovableRouteId = movableRouteId;
                MovableSide = movableSide;
                StockRouteId = stockRouteId;
                StockSide = stockSide;
            }

            public string Label { get; }
            public string MovableRouteId { get; }
            public RailSide MovableSide { get; }
            public string StockRouteId { get; }
            public RailSide StockSide { get; }
        }
    }
}
