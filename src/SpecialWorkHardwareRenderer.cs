using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Track;
using UnityEngine;

namespace NarrowGaugeMod
{
    /// <summary>
    /// Renders only validated measured mesh plans. Graph routing and native
    /// switch-point animation remain owned by the base game.
    /// </summary>
    internal static class SpecialWorkHardwareRenderer
    {
        private const float PhysicalOverlapTolerance = 0.06f;
        private const float OverlapSampleSpacing = 0.08f;
        private const float MinimumOverlapLength = 0.18f;
        private const float MinimumRailPieceLength = 0.35f;
        private const float OwnershipSeamOverlap = 0.12f;
        private const float TieOverlapTolerance = 1.0f;
        private const float TieOwnershipMargin = 0.35f;
        private const float FrogPointNoseTaperLength = 0.38f;
        private const float BladeTipStubCullLength = 1.05f;
        private const float BladeStockLeadExtensionLength = 1.05f;
        private const float BladeFullWidthTailLength = 0.65f;
        private const float BladeTaperExponent = 0.85f;
        private const int RailProfileVertexCount = 21;

        public static bool HasValidPlan(TrackNode? node)
        {
            return node != null
                && SpecialWorkRuntimeRegistry.FindByNativeNodeId(node.id)?.MeshPlan?.IsValid == true;
        }

        public static bool HasValidPlanForSegment(TrackSegment? segment)
        {
            return segment != null
                && SpecialWorkRuntimeRegistry.Analyses.Any(analysis =>
                    analysis.MeshPlan?.IsValid == true
                    && analysis.Definition.Routes.Any(route =>
                        route.SourceSegmentIds.Contains(
                            segment.id,
                            StringComparer.OrdinalIgnoreCase)));
        }

        public static bool HasTruthTable(TrackNode? node)
        {
            SpecialWorkAnalysis? analysis =
                node == null ? null : SpecialWorkRuntimeRegistry.FindByNativeNodeId(node.id);
            return analysis != null
                && SpecialWorkTruthTableCatalog.TryGet(analysis.Definition.Preset.Id, out _);
        }

        private static SpecialWorkAnalysis? FindRelatedAnalysisForVanillaSuppression(TrackNode? node)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.id))
            {
                return null;
            }

            SpecialWorkAnalysis? exact = SpecialWorkRuntimeRegistry.FindByNativeNodeId(node.id);
            if (exact != null)
            {
                return exact;
            }

            // A narrow-branch transition is one visible special-work object
            // backed by a standard source node and a generated narrow switch
            // node. Suppress vanilla hardware on either native half, but keep
            // custom rendering anchored only to the explicitly registered node.
            string linkedNodeId = GhostGraphSynchronizer.IsGeneratedGhostNodeId(node.id)
                ? node.id.Substring(GhostGraphSynchronizer.GeneratedNodePrefix.Length)
                : GhostGraphSynchronizer.GetGhostNodeId(node.id);
            return SpecialWorkRuntimeRegistry.FindByNativeNodeId(linkedNodeId);
        }

        public static bool ShouldSuppressLegacySpecialWorkRails(TrackNode? node)
        {
            NarrowGaugeSettings? settings = Main.Settings;
            if (settings?.DebugShowOnlyVanillaTurnoutObjects == true)
            {
                return false;
            }

            SpecialWorkAnalysis? analysis = FindRelatedAnalysisForVanillaSuppression(node);
            if (analysis == null)
            {
                return false;
            }

            if (settings?.DebugShowOnlySpecialWorkObjects == true)
            {
                return true;
            }

            // A validated custom object owns the complete turnout visual.
            // Invalid measured plans must leave the original switch visible;
            // otherwise bad authored topology gets hidden instead of being
            // obvious in-game.
            return settings?.EnableSpecialWorkHardware != false
                && settings?.DebugHideCustomRails != true
                && analysis.MeshPlan?.IsValid == true;
        }

        public static bool CanRenderLegacyPointHardware(TrackNode? node)
        {
            return !ShouldSuppressLegacySpecialWorkRails(node);
        }

        public static bool CanRenderCustomSpecialWork(TrackNode? node)
        {
            NarrowGaugeSettings? settings = Main.Settings;
            if (settings?.EnableSpecialWorkHardware == false
                || settings?.DebugShowOnlyVanillaTurnoutObjects == true
                || settings?.DebugHideCustomRails == true)
            {
                return false;
            }

            SpecialWorkAnalysis? analysis =
                node == null ? null : SpecialWorkRuntimeRegistry.FindByNativeNodeId(node.id);
            return analysis?.MeshPlan?.IsValid == true
                || settings?.DebugShowOnlySpecialWorkObjects == true;
        }

        public static void LogVanillaSuppression(
            TrackNode? node,
            string descriptorId,
            string sourceBuilder,
            int vanillaSwitchObjects,
            int vanillaRailObjects,
            int vanillaTieObjects)
        {
            if (node == null)
            {
                return;
            }

            SpecialWorkAnalysis? analysis =
                SpecialWorkRuntimeRegistry.FindByNativeNodeId(node.id);
            SpecialWorkMeshPlan? plan = analysis?.MeshPlan;
            int customRailObjects = plan == null
                ? 0
                : plan.FixedRunningRails.Count
                    + plan.FrogPieces.Count
                    + plan.WingRails.Count
                    + plan.GuardRails.Count
                    + plan.SwitchBlades.Count;
            int customTieObjects = Main.Settings?.DebugHideCustomTies == true ? 0 : 1;
            Main.Log(
                $"[SpecialWorkSuppress] node={node.id} descriptor={descriptorId} " +
                $"source={sourceBuilder} vanillaSwitchObjects={vanillaSwitchObjects} " +
                $"vanillaRailObjects={vanillaRailObjects} vanillaTieObjects={vanillaTieObjects} " +
                $"specialWorkRailObjects={customRailObjects} specialWorkTieObjects={customTieObjects} " +
                $"planValid={plan?.IsValid.ToString() ?? "<none>"}");
        }

        public static void LogBuiltObjectCounts(
            TrackNode? node,
            string descriptorId,
            string sourceBuilder,
            Transform container)
        {
            if (node == null || container == null)
            {
                return;
            }

            Transform[] objects = container.GetComponentsInChildren<Transform>(includeInactive: true);
            string vanillaRootName = "sw-" + node.id;
            string customRootName = "measured-special-work-" + node.id;
            int vanillaSwitchObjects = objects.Count(item =>
                string.Equals(item.name, vanillaRootName, System.StringComparison.OrdinalIgnoreCase));
            int vanillaTieObjects = objects.Count(item =>
                IsUnderNamedRoot(item, vanillaRootName)
                && item.name.IndexOf("tie", System.StringComparison.OrdinalIgnoreCase) >= 0);
            int vanillaRailObjects = objects.Count(item =>
                IsUnderNamedRoot(item, vanillaRootName)
                && IsRailObjectName(item.name));
            int specialWorkRailObjects = objects.Count(item =>
                IsUnderNamedRoot(item, customRootName)
                && item != container
                && !string.Equals(item.name, customRootName, System.StringComparison.OrdinalIgnoreCase));
            int specialWorkTieObjects = objects.Count(item =>
                IsUnderNamedRoot(item, customRootName)
                && item.name.IndexOf("tie", System.StringComparison.OrdinalIgnoreCase) >= 0);
            Main.Log(
                $"[SpecialWorkObjects] node={node.id} descriptor={descriptorId} source={sourceBuilder} " +
                $"vanillaSwitchObjects={vanillaSwitchObjects} vanillaRailObjects={vanillaRailObjects} " +
                $"vanillaTieObjects={vanillaTieObjects} specialWorkRailObjects={specialWorkRailObjects} " +
                $"specialWorkTieObjects={specialWorkTieObjects}");
        }

        private static bool IsUnderNamedRoot(Transform item, string rootName)
        {
            for (Transform? current = item; current != null; current = current.parent)
            {
                if (string.Equals(current.name, rootName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsRailObjectName(string name)
        {
            string safe = name ?? string.Empty;
            return safe.IndexOf("stock", System.StringComparison.OrdinalIgnoreCase) >= 0
                || safe.IndexOf("closure", System.StringComparison.OrdinalIgnoreCase) >= 0
                || safe.IndexOf("point", System.StringComparison.OrdinalIgnoreCase) >= 0
                || safe.IndexOf("frog", System.StringComparison.OrdinalIgnoreCase) >= 0
                || safe.IndexOf("guard", System.StringComparison.OrdinalIgnoreCase) >= 0
                || safe.IndexOf("third", System.StringComparison.OrdinalIgnoreCase) >= 0
                || safe.IndexOf("middle", System.StringComparison.OrdinalIgnoreCase) >= 0
                || safe.IndexOf("outer", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static IEnumerable<(float Start, float End)> OwnershipCuts(
            LineCurve worldVisibleRail,
            TrackSegment sourceSegment)
        {
            foreach (SpecialWorkAnalysis analysis in SpecialWorkRuntimeRegistry.Analyses.Where(item =>
                item.MeshPlan?.IsValid == true
                && item.Definition.Routes.Any(route =>
                    route.SourceSegmentIds.Contains(
                        sourceSegment.id,
                        StringComparer.OrdinalIgnoreCase))))
            {
                bool isGaugeSeparation = string.Equals(
                    analysis.Definition.Preset.Id,
                    SpecialWorkPresetIds.DualSplit,
                    StringComparison.OrdinalIgnoreCase);
                HashSet<string> sourceRouteIds = analysis.Definition.Routes
                    .Where(route => route.SourceSegmentIds.Contains(
                        sourceSegment.id,
                        StringComparer.OrdinalIgnoreCase))
                    .Select(route => route.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                IEnumerable<RailWorkInterval> ownedIntervals =
                    analysis.MeshPlan!.WorkIntervals;
                if (isGaugeSeparation)
                {
                    ownedIntervals = ownedIntervals.Where(work =>
                        work.Rail.SourceRouteIds.Any(sourceRouteIds.Contains));
                }

                foreach (RailWorkInterval work in ownedIntervals)
                {
                    LineCurve ownedCurve = Slice(
                        work.Rail.Curve,
                        work.StartDistance,
                        work.EndDistance);
                    foreach ((float start, float end) in FindOverlapIntervals(
                        worldVisibleRail,
                        ownedCurve,
                        PhysicalOverlapTolerance))
                    {
                        if (TryCreateOwnershipCut(start, end, out var cut))
                        {
                            yield return cut;
                        }
                    }
                }
            }
        }

        public static IEnumerable<(float Start, float End)> TieOwnershipCuts(
            LineCurve worldCenterline,
            TrackSegment sourceSegment)
        {
            foreach (SpecialWorkAnalysis analysis in SpecialWorkRuntimeRegistry.Analyses.Where(item =>
                item.MeshPlan?.IsValid == true
                && item.Definition.Routes.Any(route =>
                    route.SourceSegmentIds.Contains(
                        sourceSegment.id,
                        StringComparer.OrdinalIgnoreCase))))
            {
                SpecialWorkMeshPlan plan = analysis.MeshPlan!;
                foreach (LogicalRoute route in analysis.Definition.Routes.Where(route =>
                    route.SourceSegmentIds.Contains(
                        sourceSegment.id,
                        StringComparer.OrdinalIgnoreCase)))
                {
                    foreach (RailWorkInterval work in plan.WorkIntervals.Where(work =>
                        work.Rail.SourceRouteIds.Contains(route.Id, StringComparer.OrdinalIgnoreCase)))
                    {
                        LineCurve ownedCurve = Slice(
                            work.Rail.Curve,
                            work.StartDistance,
                            work.EndDistance);
                        foreach ((float start, float end) in FindOverlapIntervals(
                            worldCenterline,
                            ownedCurve,
                            TieOverlapTolerance))
                        {
                            yield return (
                                Mathf.Max(0f, start - TieOwnershipMargin),
                                Mathf.Min(worldCenterline.Length, end + TieOwnershipMargin));
                        }
                    }
                }
            }
        }

        public static IEnumerable<(float Start, float End)> OwnershipCutsForNode(
            LineCurve worldVisibleRail,
            TrackNode node)
        {
            SpecialWorkMeshPlan? plan =
                SpecialWorkRuntimeRegistry.FindByNativeNodeId(node?.id)?.MeshPlan;
            if (node == null || plan?.IsValid != true)
            {
                yield break;
            }

            foreach (RailWorkInterval work in plan.WorkIntervals)
            {
                LineCurve ownedCurve = Slice(
                    work.Rail.Curve,
                    work.StartDistance,
                    work.EndDistance);
                foreach ((float start, float end) in FindOverlapIntervals(
                    worldVisibleRail,
                    ownedCurve))
                {
                    if (TryCreateOwnershipCut(start, end, out var cut))
                    {
                        yield return cut;
                    }
                }
            }
        }

        private static bool TryCreateOwnershipCut(
            float start,
            float end,
            out (float Start, float End) cut)
        {
            float cutStart = Mathf.Min(end, start + OwnershipSeamOverlap);
            float cutEnd = Mathf.Max(cutStart, end - OwnershipSeamOverlap);
            cut = (cutStart, cutEnd);
            return cutEnd - cutStart >= MinimumRailPieceLength;
        }

        public static void AddAdditionalHardware(
            TrackObjectBuilder builder,
            TrackNode node,
            SwitchGeometry nativeGeometry,
            Transform parent)
        {
            SpecialWorkAnalysis? analysis =
                SpecialWorkRuntimeRegistry.FindByNativeNodeId(node?.id);
            SpecialWorkMeshPlan? plan = analysis?.MeshPlan;
            if (node == null
                || analysis == null
                || plan == null
                || !CanRenderCustomSpecialWork(node)
                || analysis.Definition.Preset.Category != SpecialWorkCategory.DualGauge)
            {
                if (node != null && analysis != null && plan != null)
                {
                    Main.Log(
                        $"[Build] Skipping measured special-work '{analysis.Definition.Id}' " +
                        $"node={node.id} valid={plan.IsValid} customAllowed={CanRenderCustomSpecialWork(node)} " +
                        $"issues={string.Join(" | ", plan.ValidationIssues.Take(3))}");
                }
                return;
            }

            GameObject root = NarrowGaugeTrackBuilder.CreateTrackRoot(
                builder,
                "measured-special-work-" + node.id,
                parent);
            Main.Log(
                $"[Build] Rendering measured special-work '{analysis.Definition.Id}': " +
                $"fixed={plan.FixedRunningRails.Count}, frogs={plan.Frogs.Count}, " +
                $"wings={plan.WingRails.Count}, guards={plan.GuardRails.Count}, " +
                $"blades={plan.SwitchBlades.Count}.");

            bool suppressSpecialWorkTies =
                SpecialWorkHardwareProfileCatalog.ShouldSuppressSpecialWorkTies(analysis);
            if (Main.Settings?.DebugHideCustomTies != true && !suppressSpecialWorkTies)
            {
                GameObject tiesRoot = NarrowGaugeTrackBuilder.CreateTrackRoot(
                    builder,
                    "SpecialWorkTies",
                    root.transform);
                NarrowGaugeTrackBuilder.CreateSpecialWorkTies(
                    builder,
                    analysis,
                    tiesRoot.transform,
                    nativeGeometry.switchHome);
            }
            else if (suppressSpecialWorkTies)
            {
                Main.Log(
                    $"[SpecialWorkTies] Suppressed by hardware catalog for '{analysis.Definition.Id}'.");
            }

            int fixedIndex = 0;
            foreach (RailPiece piece in plan.FixedRunningRails)
            {
                string fixedName = "Fixed-" + fixedIndex++;
                if (IsBladeTipFixedStub(piece, plan.SwitchBlades))
                {
                    continue;
                }

                if (ShouldSuppressCompoundVeePostFrogPiece(
                    analysis,
                    piece,
                    plan.Frogs))
                {
                    continue;
                }

                if (TryCreateSharedRailAroundOverlappingBlade(
                    builder,
                    root,
                    analysis,
                    piece,
                    plan.SwitchBlades,
                    nativeGeometry.switchHome,
                    fixedName))
                {
                    continue;
                }

                if (TryCreateBladeStockLead(
                    builder,
                    root,
                    analysis,
                    piece,
                    plan.FixedRunningRails,
                    plan.SwitchBlades,
                    nativeGeometry.switchHome,
                    fixedName))
                {
                    continue;
                }

                if (TryCreateNarrowBranchExtendedFixedPoint(
                    builder,
                    root,
                    analysis,
                    piece,
                    plan.Frogs,
                    analysis.WheelPaths,
                    plan.Parameters,
                    plan.SwitchBlades,
                    nativeGeometry.switchHome,
                    fixedName))
                {
                    continue;
                }

                bool rebuildFrame = ShouldRebuildFixedRailFrameFromPath(analysis, piece);
                LineCurve fixedCurve = CorrectMeasuredRailRenderFrame(
                    analysis,
                    piece.SourceRailId,
                    piece.Curve,
                    preserveProfileCenter: !rebuildFrame);
                LogDualBothDivergeNarrowClosureFrame(
                    analysis,
                    piece,
                    fixedName,
                    piece.Curve,
                    fixedCurve,
                    rebuildFrame);
                CreateRail(
                    builder,
                    root,
                    fixedCurve,
                    nativeGeometry.switchHome,
                    fixedName,
                    _ => 1f);
            }

            var renderedBlades =
                new Dictionary<SwitchBladePlan, (GameObject Object, float OpenRotation)>();
            int bladeIndex = 0;
            foreach (SwitchBladePlan blade in plan.SwitchBlades)
            {
                GameObject? bladeObject = CreatePointBlade(
                    builder,
                    root,
                    CorrectMeasuredRailRenderFrame(
                        analysis,
                        blade.MovableRail.Id,
                        blade.BladeCurve,
                        preserveProfileCenter: false),
                    nativeGeometry.switchHome,
                    "Blade-" + bladeIndex++);
                if (bladeObject == null)
                {
                    continue;
                }

                float openRotation = CalculateBladeOpenRotation(blade, plan.Parameters);
                renderedBlades[blade] = (bladeObject, openRotation);
            }

            ConfigureBladeAnimationGroups(root, analysis.Definition, node, renderedBlades);

            int frogIndex = 0;
            var compoundVeeFrogs = new HashSet<FrogCandidate>();
            if (IsDualStandardBranch(analysis))
            {
                foreach ((FrogCandidate first, FrogCandidate second) in FindCloseVeeFrogPairs(plan.Frogs))
                {
                    CreateCompoundVeeFrogAssembly(
                        builder,
                        root,
                        analysis,
                        first,
                        second,
                        plan.SwitchBlades,
                        nativeGeometry.switchHome,
                        "DoubleVeeFrog-" + frogIndex++);
                    compoundVeeFrogs.Add(first);
                    compoundVeeFrogs.Add(second);
                }
            }

            foreach (FrogCandidate frog in plan.Frogs.Where(item =>
                item.Intersection.Kind == RailIntersectionKind.VeeFrogCandidate
                && !compoundVeeFrogs.Contains(item)))
            {
                CreateVeeFrogAssembly(
                    builder,
                    root,
                    analysis,
                    frog,
                    plan.SwitchBlades,
                    nativeGeometry.switchHome,
                    "VeeFrog-" + frogIndex++);
            }

            foreach (FrogCandidate frog in plan.Frogs.Where(item =>
                item.Intersection.Kind == RailIntersectionKind.CrossingFrogCandidate))
            {
                CreateCrossingFrogAssembly(
                    builder,
                    root,
                    analysis,
                    frog,
                    plan.SwitchBlades,
                    nativeGeometry.switchHome,
                    "CrossingFrog-" + frogIndex++);
            }

            int guardIndex = 0;
            foreach (GuardRailPlan guard in plan.GuardRails)
            {
                CreateRail(
                    builder,
                    root,
                    CorrectMeasuredRailRenderFrame(
                        analysis,
                        guard.OppositeRunningRail.Id,
                        guard.Curve),
                    nativeGeometry.switchHome,
                    "Guard-" + guardIndex++,
                    _ => 1f);
            }
        }

        private static bool ShouldSuppressCompoundVeePostFrogPiece(
            SpecialWorkAnalysis analysis,
            RailPiece piece,
            IReadOnlyList<FrogCandidate> frogs)
        {
            if (!IsDualStandardBranch(analysis)
                || piece.SourcePlanId?.EndsWith(
                    ":post-frog",
                    StringComparison.OrdinalIgnoreCase) != true)
            {
                return false;
            }

            FrogCandidate[] participating = frogs
                .Where(frog =>
                    frog.Intersection.Kind == RailIntersectionKind.VeeFrogCandidate
                    && FrogRails(frog).Any(item => string.Equals(
                        item.Rail.Id,
                        piece.SourceRailId,
                        StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            return participating.Length >= 2
                && participating.Any(first => participating.Any(second =>
                    first != second
                    && Vector3.Distance(
                        first.Intersection.Position,
                        second.Intersection.Position) <= 0.18f));
        }

        private static bool TryCreateBladeStockLead(
            TrackObjectBuilder builder,
            GameObject root,
            SpecialWorkAnalysis analysis,
            RailPiece piece,
            IReadOnlyList<RailPiece> fixedRails,
            IReadOnlyList<SwitchBladePlan> blades,
            Vector3 switchHome,
            string name)
        {
            bool isLeftNarrowBranch = IsLeftNarrowBranchTruth(analysis);
            bool isDualStandardBranch = IsDualStandardBranch(analysis);
            if ((!isLeftNarrowBranch && !isDualStandardBranch)
                || piece.Kind != RailPieceKind.FixedRunning
                && piece.Kind != RailPieceKind.SharedRunning)
            {
                return false;
            }

            SwitchBladePlan? straightBlade = blades.FirstOrDefault(blade =>
                string.Equals(
                    blade.StockRail.Id,
                    piece.SourceRailId,
                    StringComparison.OrdinalIgnoreCase)
                && (isDualStandardBranch || BladeHasSharedOwnerOverlap(blade, fixedRails)));
            if (straightBlade == null)
            {
                return false;
            }

            if (isDualStandardBranch
                && Mathf.Abs(piece.StartDistance - straightBlade.RootDistance) <= 0.15f)
            {
                float stockLeadStart = Mathf.Max(0f, straightBlade.TipDistance - OverlapSampleSpacing);
                if (piece.EndDistance - stockLeadStart < MinimumRailPieceLength)
                {
                    return false;
                }

                CreateRail(
                    builder,
                    root,
                    CorrectMeasuredRailRenderFrame(
                        analysis,
                        piece.SourceRailId,
                        Slice(straightBlade.StockRail.Curve, stockLeadStart, piece.EndDistance)),
                    switchHome,
                    name + "-StockLead",
                    _ => 1f);
                return true;
            }

            if (!isLeftNarrowBranch
                || Mathf.Abs(piece.EndDistance - straightBlade.RootDistance) > 0.15f
                || piece.StartDistance - straightBlade.TipDistance > BladeStockLeadExtensionLength + 0.15f)
            {
                return false;
            }

            float start = Mathf.Max(
                0f,
                Mathf.Max(
                    straightBlade.TipDistance,
                    piece.StartDistance - BladeStockLeadExtensionLength));
            if (piece.StartDistance - start < MinimumRailPieceLength)
            {
                return false;
            }

            CreateRail(
                builder,
                root,
                CorrectMeasuredRailRenderFrame(
                    analysis,
                    piece.SourceRailId,
                    Slice(straightBlade.StockRail.Curve, start, piece.EndDistance)),
                switchHome,
                name + "-StockLead",
                _ => 1f);
            return true;
        }

        private static bool TryCreateSharedRailAroundOverlappingBlade(
            TrackObjectBuilder builder,
            GameObject root,
            SpecialWorkAnalysis analysis,
            RailPiece piece,
            IReadOnlyList<SwitchBladePlan> blades,
            Vector3 switchHome,
            string name)
        {
            if (!IsLeftNarrowBranchTruth(analysis)
                || piece.Kind != RailPieceKind.SharedRunning)
            {
                return false;
            }

            if (!TryFindBladeOverlap(
                    piece,
                    blades,
                    out _,
                    out float cutStart,
                    out float cutEnd))
            {
                return false;
            }

            if (cutEnd - cutStart < MinimumOverlapLength)
            {
                return false;
            }

            int spanIndex = 0;
            CreateSpan(0f, cutStart);
            CreateSpan(cutEnd, piece.Curve.Length);
            return true;

            void CreateSpan(float start, float end)
            {
                if (end - start < MinimumRailPieceLength)
                {
                    return;
                }

                CreateRail(
                    builder,
                    root,
                    CorrectMeasuredRailRenderFrame(
                        analysis,
                        piece.SourceRailId,
                        Slice(piece.Curve, start, end)),
                    switchHome,
                    name + "-BladeClearance-" + spanIndex++,
                    _ => 1f);
            }
        }

        private static bool IsLeftNarrowBranchTruth(SpecialWorkAnalysis analysis)
        {
            return SpecialWorkTruthTableCatalog.TryGet(
                    analysis.Definition.Preset.Id,
                    analysis.Rails,
                    analysis.Intersections,
                    out TurnoutTruthTable truth)
                && string.Equals(
                    truth.Id,
                    "DualGauge_NarrowBranch_Left",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDualStandardBranch(SpecialWorkAnalysis analysis)
        {
            return string.Equals(
                analysis.Definition.Preset.Id,
                SpecialWorkPresetIds.DualStandardBranch,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDualBothDiverge(SpecialWorkAnalysis analysis)
        {
            return string.Equals(
                analysis.Definition.Preset.Id,
                SpecialWorkPresetIds.DualBothDiverge,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldRebuildFixedRailFrameFromPath(
            SpecialWorkAnalysis analysis,
            RailPiece piece)
        {
            return IsDualBothDiverge(analysis)
                && piece.Kind == RailPieceKind.ClosureRail;
        }

        private static void LogDualBothDivergeNarrowClosureFrame(
            SpecialWorkAnalysis analysis,
            RailPiece piece,
            string objectName,
            LineCurve sourceCurve,
            LineCurve renderCurve,
            bool rebuildFrame)
        {
            if (!IsDualBothDiverge(analysis)
                || piece.Kind != RailPieceKind.ClosureRail
                || !string.Equals(
                    piece.SourceRailId,
                    "narrow-normal:right",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Main.Log(
                $"[NarrowClosureDebug] object={objectName} source={piece.SourcePlanId} " +
                $"rail={piece.SourceRailId} dist={piece.StartDistance:0.000}-{piece.EndDistance:0.000} " +
                $"rebuildFrame={rebuildFrame} " +
                $"srcHead={FormatPoint(sourceCurve.Head.point)} srcTail={FormatPoint(sourceCurve.Tail.point)} " +
                $"dstHead={FormatPoint(renderCurve.Head.point)} dstTail={FormatPoint(renderCurve.Tail.point)} " +
                $"srcProfileHead={FormatPoint(ProfileCenter(sourceCurve.Head, sourceCurve.hand))} " +
                $"srcProfileTail={FormatPoint(ProfileCenter(sourceCurve.Tail, sourceCurve.hand))} " +
                $"dstProfileHead={FormatPoint(ProfileCenter(renderCurve.Head, renderCurve.hand))} " +
                $"dstProfileTail={FormatPoint(ProfileCenter(renderCurve.Tail, renderCurve.hand))}");
        }

        private static Vector3 ProfileCenter(LinePoint point, Hand hand)
        {
            float offset = hand == Hand.Left
                ? -Gauge.Standard.HeadWidth * 0.5f
                : Gauge.Standard.HeadWidth * 0.5f;
            return point.point + point.Rotation * Vector3.right * offset;
        }

        private static string FormatPoint(Vector3 point)
        {
            return $"({point.x:0.000},{point.y:0.000},{point.z:0.000})";
        }

        private static LineCurve CorrectMeasuredRailRenderFrame(
            SpecialWorkAnalysis analysis,
            string sourceRailId,
            LineCurve curve,
            bool preserveProfileCenter = true)
        {
            if (!NeedsMeasuredRailFrameCorrection(analysis))
            {
                return curve;
            }

            bool affectedSource =
                analysis.Rails.Any(rail =>
                    string.Equals(
                        rail.Id,
                        sourceRailId,
                        StringComparison.OrdinalIgnoreCase));
            return affectedSource
                ? NormalizeRenderFrames(curve, preserveProfileCenter)
                : curve;
        }

        private static bool NeedsMeasuredRailFrameCorrection(SpecialWorkAnalysis analysis)
        {
            return IsLeftNarrowBranchTruth(analysis)
                || string.Equals(
                    analysis.Definition.Preset.Id,
                    SpecialWorkPresetIds.DualSplit,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    analysis.Definition.Preset.Id,
                    SpecialWorkPresetIds.DualBothDiverge,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    analysis.Definition.Preset.Id,
                    SpecialWorkPresetIds.DualStandardBranch,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static LineCurve NormalizeRenderFrames(
            LineCurve curve,
            bool preserveProfileCenter)
        {
            LinePoint[] source = curve.Points.ToArray();
            var corrected = new LinePoint[source.Length];
            float profileCenterOffset = curve.hand == Hand.Left
                ? -Gauge.Standard.HeadWidth * 0.5f
                : Gauge.Standard.HeadWidth * 0.5f;
            for (int index = 0; index < source.Length; index++)
            {
                Vector3 direction = index == 0
                    ? source[1].point - source[0].point
                    : index == source.Length - 1
                        ? source[index].point - source[index - 1].point
                        : source[index + 1].point - source[index - 1].point;
                if (direction.sqrMagnitude <= 0.000001f)
                {
                    corrected[index] = source[index];
                    continue;
                }

                Quaternion correctedRotation =
                    Quaternion.LookRotation(direction.normalized, Vector3.up);
                Vector3 correctedPoint = source[index].point;
                if (preserveProfileCenter)
                {
                    Vector3 originalRight = source[index].Rotation * Vector3.right;
                    Vector3 correctedRight = correctedRotation * Vector3.right;
                    correctedPoint +=
                        (originalRight - correctedRight) * profileCenterOffset;
                }

                corrected[index] = new LinePoint(correctedPoint, correctedRotation);
            }

            return new LineCurve(corrected, curve.hand);
        }

        private static IEnumerable<(float Start, float End)> FindOverlapIntervals(
            LineCurve target,
            LineCurve candidate,
            float tolerance = PhysicalOverlapTolerance)
        {
            var matches = new List<float>();
            int count = Mathf.Max(
                2,
                Mathf.CeilToInt(candidate.Length / OverlapSampleSpacing) + 1);
            for (int index = 0; index < count; index++)
            {
                float candidateDistance = index == count - 1
                    ? candidate.Length
                    : Mathf.Min(candidate.Length, index * OverlapSampleSpacing);
                Vector3 point = candidate.LinePointAtDistance(candidateDistance).point;
                if (!TryDistanceAlongCurve(
                    target,
                    point,
                    out float targetDistance,
                    out float separation)
                    || separation > tolerance)
                {
                    continue;
                }

                matches.Add(targetDistance);
            }

            if (matches.Count < 3)
            {
                yield break;
            }

            matches.Sort();
            float start = matches[0];
            float previous = matches[0];
            for (int index = 1; index < matches.Count; index++)
            {
                if (matches[index] - previous <= OverlapSampleSpacing * 2.5f)
                {
                    previous = matches[index];
                    continue;
                }

                if (previous - start >= MinimumOverlapLength)
                {
                    yield return (
                        Mathf.Max(0f, start - OverlapSampleSpacing),
                        Mathf.Min(target.Length, previous + OverlapSampleSpacing));
                }

                start = matches[index];
                previous = matches[index];
            }

            if (previous - start >= MinimumOverlapLength)
            {
                yield return (
                    Mathf.Max(0f, start - OverlapSampleSpacing),
                    Mathf.Min(target.Length, previous + OverlapSampleSpacing));
            }
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

        private static LineCurve Slice(LineCurve curve, float start, float end)
        {
            float clampedStart = Mathf.Clamp(start, 0f, curve.Length);
            float clampedEnd = Mathf.Clamp(end, clampedStart, curve.Length);
            return curve.Skip(clampedStart, true).Take(clampedEnd - clampedStart);
        }

        private static void CreateRail(
            TrackObjectBuilder builder,
            GameObject root,
            LineCurve worldCurve,
            Vector3 switchHome,
            string name,
            Func<int, float> profile)
        {
            if (worldCurve == null
                || worldCurve.Points.Count() < 2
                || worldCurve.Length < MinimumRailPieceLength)
            {
                return;
            }

            Mesh mesh = NarrowGaugeTrackBuilder.BuildStockRailMesh(
                worldCurve.Offset(-switchHome),
                switchHome,
                Gauge.Standard,
                profile);
            NarrowGaugeTrackBuilder.CreateMeshObject(builder, mesh, name, root);
        }

        private static bool IsBladeTipFixedStub(
            RailPiece piece,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            if (piece.Kind != RailPieceKind.FixedRunning
                || piece.EndDistance - piece.StartDistance > BladeTipStubCullLength)
            {
                return false;
            }

            return blades.Any(blade =>
                Mathf.Abs(piece.EndDistance - blade.TipDistance) <= 0.01f
                && string.Equals(
                    piece.SourceRailId,
                    blade.MovableRail.Id,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static GameObject? CreatePointBlade(
            TrackObjectBuilder builder,
            GameObject root,
            LineCurve worldCurve,
            Vector3 switchHome,
            string name)
        {
            if (worldCurve == null
                || worldCurve.Points.Count() < 2
                || worldCurve.Length < MinimumRailPieceLength)
            {
                return null;
            }

            LineCurve bladeCurve = worldCurve
                .Offset(-switchHome)
                .Subdivide(0.08f);
            LinePoint[] points = bladeCurve.Points.ToArray();
            if (points.Length < 2)
            {
                return null;
            }

            var distances = new float[points.Length];
            for (int i = 1; i < points.Length; i++)
            {
                distances[i] = distances[i - 1]
                    + Vector3.Distance(points[i - 1].point, points[i].point);
            }

            float totalLength = Mathf.Max(distances[distances.Length - 1], MinimumRailPieceLength);
            float taperLength = Mathf.Clamp(
                totalLength - BladeFullWidthTailLength,
                MinimumRailPieceLength,
                Mathf.Max(MinimumRailPieceLength, totalLength - 0.05f));
            Vector3 pivot = points.Last().point;
            LineCurve pivotedCurve = new LineCurve(
                bladeCurve.Offset(-pivot).Points.ToArray(),
                bladeCurve.hand);
            Mesh mesh = NarrowGaugeTrackBuilder.BuildStockRailMesh(
                pivotedCurve,
                switchHome,
                Gauge.Standard,
                i =>
                {
                    int pointIndex = Mathf.Clamp(i, 0, distances.Length - 1);
                    float t = Mathf.Clamp01(distances[pointIndex] / taperLength);
                    return Mathf.SmoothStep(0f, 1f, Mathf.Pow(t, BladeTaperExponent));
                });
            RemoveRailEndCap(mesh, points.Length, removeStartCap: true);

            GameObject rail = NarrowGaugeTrackBuilder.CreateMeshObject(builder, mesh, name, root);
            rail.transform.localPosition = pivot;
            return rail;
        }

        private static float CalculateBladeOpenRotation(
            SwitchBladePlan blade,
            SpecialWorkGeometryParameters parameters)
        {
            Vector3 tip = blade.BladeCurve.Head.point;
            Vector3 root = blade.BladeCurve.Tail.point;
            Vector3 closedTipVector = tip - root;
            closedTipVector.y = 0f;
            if (closedTipVector.sqrMagnitude <= 0.0001f)
            {
                return 0f;
            }

            LinePoint stockAtTip = blade.StockRail.Curve.LinePointAtDistance(
                Mathf.Clamp(blade.StockRail.Curve.DistanceTo(tip), 0f, blade.StockRail.Curve.Length));
            LinePoint stockAtRoot = blade.StockRail.Curve.LinePointAtDistance(
                Mathf.Clamp(blade.StockRail.Curve.DistanceTo(root), 0f, blade.StockRail.Curve.Length));
            Vector3 awayFromStock = root - stockAtRoot.point;
            awayFromStock.y = 0f;
            if (awayFromStock.sqrMagnitude <= 0.0001f)
            {
                awayFromStock = tip - stockAtTip.point;
                awayFromStock.y = 0f;
            }

            if (awayFromStock.sqrMagnitude <= 0.0001f)
            {
                awayFromStock = stockAtTip.Rotation * Vector3.right;
                awayFromStock.y = 0f;
            }

            float openCenterSeparation = parameters.RailHeadWidth + parameters.FlangewayWidth;
            Vector3 openTip = stockAtTip.point + awayFromStock.normalized * openCenterSeparation;
            Vector3 openTipVector = openTip - root;
            openTipVector.y = 0f;
            return openTipVector.sqrMagnitude <= 0.0001f
                ? 0f
                : Vector3.SignedAngle(closedTipVector, openTipVector, Vector3.up);
        }

        internal static float CalculateProceduralFrogCutHalfLength(
            LineCurve railA,
            LineCurve railB,
            LinePoint intersection)
        {
            return CreateProceduralFrogCandidate(
                railA,
                GaugeGraphFamily.Standard,
                railB,
                GaugeGraphFamily.Narrow,
                intersection,
                intersection.point + intersection.direction,
                RailIntersectionKind.CrossingFrogCandidate,
                "procedural-cut").CutHalfLength;
        }

        private static FrogCandidate CreateProceduralFrogCandidate(
            LineCurve railA,
            GaugeGraphFamily familyA,
            LineCurve railB,
            GaugeGraphFamily familyB,
            LinePoint intersection,
            Vector3 bladeTip,
            RailIntersectionKind kind,
            string id,
            RailSide sideA = RailSide.Left,
            RailSide sideB = RailSide.Right)
        {
            float distanceA = Mathf.Clamp(railA.DistanceTo(intersection.point), 0f, railA.Length);
            float distanceB = Mathf.Clamp(railB.DistanceTo(intersection.point), 0f, railB.Length);
            Vector3 tangentA = railA.LinePointAtDistance(distanceA).direction;
            Vector3 tangentB = railB.LinePointAtDistance(distanceB).direction;
            tangentA.y = 0f;
            tangentB.y = 0f;
            tangentA = tangentA.sqrMagnitude > 0.0001f ? tangentA.normalized : Vector3.forward;
            tangentB = tangentB.sqrMagnitude > 0.0001f ? tangentB.normalized : Vector3.forward;

            float angle = Vector3.Angle(tangentA, tangentB);
            float acuteAngle = Mathf.Min(angle, 180f - angle);
            Vector3 alignedB = Vector3.Dot(tangentA, tangentB) < 0f ? -tangentB : tangentB;
            Vector3 bisector = (tangentA + alignedB).normalized;
            if (bisector.sqrMagnitude <= 0.0001f)
            {
                bisector = tangentA;
            }

            Vector3 nose = bladeTip - intersection.point;
            nose.y = 0f;
            if (nose.sqrMagnitude <= 0.0001f)
            {
                nose = bisector;
            }
            nose.Normalize();

            float halfAngle = Mathf.Max(acuteAngle * 0.5f * Mathf.Deg2Rad, 0.01f);
            float railHeadSetback = Gauge.Standard.HeadWidth / Mathf.Tan(halfAngle);
            float flangewaySetback = 0.05f / Mathf.Sin(halfAngle);
            float cutHalfLength = Mathf.Clamp(
                Mathf.Max(
                    railHeadSetback + Gauge.Standard.HeadWidth * 0.5f,
                    flangewaySetback + 0.12f),
                0.16f,
                1.5f);
            var railCenterA = new RailCenterline(
                id + ":rail-a",
                familyA,
                sideA,
                railA,
                new[] { id + ":route-a" });
            var railCenterB = new RailCenterline(
                id + ":rail-b",
                familyB,
                sideB,
                railB,
                new[] { id + ":route-b" });
            var railIntersection = new RailIntersection(
                id + ":intersection",
                railCenterA,
                railCenterB,
                distanceA,
                distanceB,
                intersection.point,
                tangentA,
                tangentB,
                acuteAngle,
                kind);
            return new FrogCandidate(
                id,
                railIntersection,
                nose,
                -nose,
                Vector3.Cross(tangentA, tangentB).y >= 0f
                    ? FrogHandedness.Left
                    : FrogHandedness.Right,
                railHeadSetback,
                flangewaySetback,
                cutHalfLength);
        }

        private static void ConfigureBladeAnimationGroups(
            GameObject root,
            SpecialWorkDefinition definition,
            TrackNode fallbackNode,
            IReadOnlyDictionary<SwitchBladePlan, (GameObject Object, float OpenRotation)> renderedBlades)
        {
            foreach (SpecialWorkSwitchGroup group in definition.SwitchGroups)
            {
                SwitchBladePlan[] groupBlades = renderedBlades.Keys
                    .Where(blade => string.Equals(
                        blade.SwitchGroupId,
                        group.Id,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (groupBlades.Length == 0)
                {
                    continue;
                }

                TrackNode? groupNode = group.NativeNodeIds
                    .Select(nodeId => Graph.Shared?.GetNode(nodeId))
                    .FirstOrDefault(node => node != null)
                    ?? fallbackNode;
                SwitchBladePlan? normalBlade = groupBlades.FirstOrDefault(blade =>
                    BladeRequiresState(definition, blade, "normal"));
                SwitchBladePlan? reversedBlade = groupBlades.FirstOrDefault(blade =>
                    BladeRequiresState(definition, blade, "reversed"));
                (GameObject Object, float OpenRotation)? normal =
                    normalBlade != null ? renderedBlades[normalBlade] : null;
                (GameObject Object, float OpenRotation)? reversed =
                    reversedBlade != null ? renderedBlades[reversedBlade] : null;
                if (normal == null && reversed == null)
                {
                    continue;
                }

                GameObject normalObject = normal?.Object
                    ?? CreateBladeAnimationDummy(root, reversed!.Value.Object, group.Id + "-NormalDummy");
                GameObject reversedObject = reversed?.Object
                    ?? CreateBladeAnimationDummy(root, normal!.Value.Object, group.Id + "-ReversedDummy");
                root.AddComponent<SwitchPointRails>().Configure(
                    groupNode,
                    normalObject,
                    reversedObject,
                    normal?.OpenRotation ?? 0f,
                    reversed?.OpenRotation ?? 0f);
            }
        }

        private static bool BladeRequiresState(
            SpecialWorkDefinition definition,
            SwitchBladePlan blade,
            string stateId)
        {
            return definition.Routes.Any(route =>
                string.Equals(route.RequiredStateId, stateId, StringComparison.OrdinalIgnoreCase)
                && (blade.MovableRail.SourceRouteIds.Contains(
                        route.Id,
                        StringComparer.OrdinalIgnoreCase)
                    || string.Equals(
                            blade.SwitchGroupId,
                            "narrow-separation",
                            StringComparison.OrdinalIgnoreCase)
                        && blade.StockRail.SourceRouteIds.Contains(
                            route.Id,
                            StringComparer.OrdinalIgnoreCase)));
        }

        private static GameObject CreateBladeAnimationDummy(
            GameObject root,
            GameObject bladeObject,
            string name)
        {
            var dummy = new GameObject(name);
            dummy.transform.SetParent(root.transform, false);
            dummy.transform.localPosition = bladeObject.transform.localPosition;
            return dummy;
        }

        private static void RemoveRailEndCap(
            Mesh mesh,
            int pathPointCount,
            bool removeStartCap)
        {
            if (mesh == null || pathPointCount < 2)
            {
                return;
            }

            int[] triangles = mesh.triangles;
            int sideIndexCount = (pathPointCount - 1) * (RailProfileVertexCount - 1) * 6;
            int capIndexCount = (triangles.Length - sideIndexCount) / 2;
            if (sideIndexCount <= 0
                || capIndexCount <= 0
                || sideIndexCount + capIndexCount * 2 != triangles.Length)
            {
                return;
            }

            int[] result = removeStartCap
                ? triangles
                    .Take(sideIndexCount)
                    .Concat(triangles.Skip(sideIndexCount + capIndexCount))
                    .ToArray()
                : triangles.Take(sideIndexCount + capIndexCount).ToArray();
            mesh.triangles = result;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
        }

        private static LineCurve ReprofilePointBlade(LineCurve curve)
        {
            if (curve.Length <= 0.3f)
            {
                return curve;
            }

            float tipTrim = Mathf.Min(0.12f, Mathf.Max(curve.Length - 0.05f, 0.01f));
            LineCurve trimmed = curve.Skip(tipTrim, false);
            if (!trimmed.Points.Any())
            {
                return curve;
            }

            LinePoint tip = trimmed.Points.First();
            float taperLength = Mathf.Min(
                Mathf.Max(trimmed.Length * 0.6f, 0.35f),
                Mathf.Max(trimmed.Length - 0.05f, 0.05f));
            LineCurve reprofiled = trimmed.Skip(taperLength, false);
            if (!reprofiled.Points.Any())
            {
                return trimmed;
            }

            reprofiled.Insert(0, tip);
            return reprofiled;
        }

        private static void CreateVeeFrogAssembly(
            TrackObjectBuilder builder,
            GameObject root,
            SpecialWorkAnalysis analysis,
            FrogCandidate frog,
            IReadOnlyList<SwitchBladePlan> blades,
            Vector3 switchHome,
            string name)
        {
            Vector3 noseSide = DirectionTowardBlades(frog, blades);
            LinePoint heelA = HeelPoint(
                frog.Intersection.RailA,
                frog.Intersection.DistanceA,
                frog,
                noseSide);
            LinePoint heelB = HeelPoint(
                frog.Intersection.RailB,
                frog.Intersection.DistanceB,
                frog,
                noseSide);
            LinePoint[] points =
            {
                new LinePoint(heelA.point - switchHome, heelA.Rotation),
                new LinePoint(
                    frog.Intersection.Position - switchHome,
                    Quaternion.LookRotation(noseSide, Vector3.up)),
                new LinePoint(heelB.point - switchHome, heelB.Rotation)
            };
            if (NeedsMeasuredRailFrameCorrection(analysis))
            {
                points = NormalizeRenderFrames(
                    new LineCurve(points, Hand.Left),
                    preserveProfileCenter: false)
                    .Points
                    .ToArray();
            }

            Mesh mesh = NarrowGaugeTrackBuilder.BuildFrogMesh(points, Gauge.Standard);
            NarrowGaugeTrackBuilder.CreateMeshObject(builder, mesh, name, root);

            CreateVeeWingRail(
                builder,
                root,
                analysis,
                frog.Intersection.RailA,
                frog.Intersection.DistanceA,
                heelB,
                heelA,
                frog,
                blades,
                switchHome,
                name + "-WingA");
            CreateVeeWingRail(
                builder,
                root,
                analysis,
                frog.Intersection.RailB,
                frog.Intersection.DistanceB,
                heelA,
                heelB,
                frog,
                blades,
                switchHome,
                name + "-WingB");
        }

        private static void CreateVeeWingRail(
            TrackObjectBuilder builder,
            GameObject root,
            SpecialWorkAnalysis analysis,
            RailCenterline sourceRail,
            float intersectionDistance,
            LinePoint oppositeHeel,
            LinePoint otherHeel,
            FrogCandidate frog,
            IReadOnlyList<SwitchBladePlan> blades,
            Vector3 switchHome,
            string name)
        {
            Vector3 bladeDirection = DirectionTowardBlades(frog, blades);
            float beforeDistance = Mathf.Max(0f, intersectionDistance - frog.CutHalfLength);
            float afterDistance = Mathf.Min(sourceRail.Curve.Length, intersectionDistance + frog.CutHalfLength);
            LinePoint before = sourceRail.Curve.LinePointAtDistance(beforeDistance);
            LinePoint after = sourceRail.Curve.LinePointAtDistance(afterDistance);
            bool bladeSideIsAfter =
                Vector3.Dot(after.point - frog.Intersection.Position, bladeDirection)
                > Vector3.Dot(before.point - frog.Intersection.Position, bladeDirection);

            float nearSetback = Mathf.Min(0.45f, frog.CutHalfLength * 0.7f);
            float nearDistance = Mathf.Clamp(
                intersectionDistance + (bladeSideIsAfter ? nearSetback : -nearSetback),
                0f,
                sourceRail.Curve.Length);
            float boundaryDistance = bladeSideIsAfter ? afterDistance : beforeDistance;
            LineCurve wing = bladeSideIsAfter
                ? Slice(sourceRail.Curve, nearDistance, boundaryDistance).Reverse()
                : Slice(sourceRail.Curve, boundaryDistance, nearDistance);
            if (wing.Points.Count() < 2)
            {
                return;
            }

            Vector3 outward = oppositeHeel.point - otherHeel.point;
            outward.y = 0f;
            if (outward.sqrMagnitude <= 0.0001f)
            {
                outward = oppositeHeel.Rotation
                    * (sourceRail.Side == RailSide.Left ? Vector3.left : Vector3.right);
            }

            wing.Add(new LinePoint(
                oppositeHeel.point + outward.normalized * 0.1f,
                oppositeHeel.Rotation));
            CreateRail(
                builder,
                root,
                CorrectMeasuredRailRenderFrame(
                    analysis,
                    sourceRail.Id,
                    wing,
                    preserveProfileCenter: !IsDualBothDiverge(analysis)),
                switchHome,
                name,
                _ => 1f);
        }

        private static IEnumerable<(FrogCandidate First, FrogCandidate Second)> FindCloseVeeFrogPairs(
            IReadOnlyList<FrogCandidate> frogs)
        {
            var used = new HashSet<FrogCandidate>();
            FrogCandidate[] candidates = frogs
                .Where(frog => frog.Intersection.Kind == RailIntersectionKind.VeeFrogCandidate)
                .ToArray();
            for (int i = 0; i < candidates.Length; i++)
            {
                if (used.Contains(candidates[i]))
                {
                    continue;
                }

                for (int j = i + 1; j < candidates.Length; j++)
                {
                    if (used.Contains(candidates[j])
                        || Vector3.Distance(
                            candidates[i].Intersection.Position,
                            candidates[j].Intersection.Position) > 0.18f
                        || !TryResolveCompoundVeeRails(
                            candidates[i],
                            candidates[j],
                            out _,
                            out _,
                            out _,
                            out _,
                            out _,
                            out _))
                    {
                        continue;
                    }

                    used.Add(candidates[i]);
                    used.Add(candidates[j]);
                    yield return (candidates[i], candidates[j]);
                    break;
                }
            }
        }

        private static void CreateCompoundVeeFrogAssembly(
            TrackObjectBuilder builder,
            GameObject root,
            SpecialWorkAnalysis analysis,
            FrogCandidate first,
            FrogCandidate second,
            IReadOnlyList<SwitchBladePlan> blades,
            Vector3 switchHome,
            string name)
        {
            if (!TryResolveCompoundVeeRails(
                first,
                second,
                out RailCenterline sharedRail,
                out float sharedDistance,
                out RailCenterline firstOuterRail,
                out float firstOuterDistance,
                out RailCenterline secondOuterRail,
                out float secondOuterDistance))
            {
                CreateVeeFrogAssembly(builder, root, analysis, first, blades, switchHome, name + "-First");
                CreateVeeFrogAssembly(builder, root, analysis, second, blades, switchHome, name + "-Second");
                return;
            }

            FrogCandidate? standardFrog = new[] { first, second }.FirstOrDefault(frog =>
                FrogRails(frog).All(item => item.Rail.Family == GaugeGraphFamily.Standard));
            FrogCandidate? mixedFrog = new[] { first, second }.FirstOrDefault(frog =>
                FrogRails(frog).Any(item => item.Rail.Family == GaugeGraphFamily.Narrow));
            if (standardFrog == null || mixedFrog == null)
            {
                CreateVeeFrogAssembly(builder, root, analysis, first, blades, switchHome, name + "-First");
                CreateVeeFrogAssembly(builder, root, analysis, second, blades, switchHome, name + "-Second");
                return;
            }

            CreateVeeFrogAssembly(
                builder,
                root,
                analysis,
                standardFrog,
                blades,
                switchHome,
                name + "-Standard");

            // When the standard and mixed vee candidates resolve to the same
            // physical nose, the complete standard vee owns the point and both
            // wings. Rendering the mixed overlay would duplicate that hardware.
            if (Vector3.Distance(
                    standardFrog.Intersection.Position,
                    mixedFrog.Intersection.Position)
                <= PhysicalOverlapTolerance)
            {
                return;
            }

            (RailCenterline Rail, float Distance) narrow =
                FrogRails(mixedFrog).First(item => item.Rail.Family == GaugeGraphFamily.Narrow);
            Vector3 bladeDirection = DirectionTowardBlades(mixedFrog, blades);
            float narrowBladeSide = SideTowardDirection(
                narrow.Rail,
                narrow.Distance,
                mixedFrog.Intersection.Position,
                bladeDirection);
            LineCurve narrowPoint = SliceSignedSpan(
                narrow.Rail,
                narrow.Distance,
                -narrowBladeSide * mixedFrog.CutHalfLength,
                0f);
            CreateExtendedFrogPointRail(
                builder,
                root,
                CorrectMeasuredRailRenderFrame(
                    analysis,
                    narrow.Rail.Id,
                    narrowPoint),
                mixedFrog.RailHeadSetback,
                switchHome,
                name + "-NarrowFrogPoint");

            LinePoint sharedHeel = HeelPoint(
                sharedRail,
                sharedDistance,
                mixedFrog,
                bladeDirection);
            LinePoint narrowHeel = HeelPoint(
                narrow.Rail,
                narrow.Distance,
                mixedFrog,
                bladeDirection);
            CreateVeeWingRail(
                builder,
                root,
                analysis,
                sharedRail,
                sharedDistance,
                narrowHeel,
                sharedHeel,
                mixedFrog,
                blades,
                switchHome,
                name + "-ClosureWing");
            CreateVeeWingRail(
                builder,
                root,
                analysis,
                narrow.Rail,
                narrow.Distance,
                sharedHeel,
                narrowHeel,
                mixedFrog,
                blades,
                switchHome,
                name + "-NarrowWing");
        }

        private static bool TryResolveCompoundVeeRails(
            FrogCandidate first,
            FrogCandidate second,
            out RailCenterline sharedRail,
            out float sharedDistance,
            out RailCenterline firstOuterRail,
            out float firstOuterDistance,
            out RailCenterline secondOuterRail,
            out float secondOuterDistance)
        {
            sharedRail = null!;
            firstOuterRail = null!;
            secondOuterRail = null!;
            sharedDistance = 0f;
            firstOuterDistance = 0f;
            secondOuterDistance = 0f;

            (RailCenterline Rail, float Distance)[] firstRails = FrogRails(first).ToArray();
            (RailCenterline Rail, float Distance)[] secondRails = FrogRails(second).ToArray();
            foreach ((RailCenterline rail, float firstDistance) in firstRails)
            {
                (RailCenterline Rail, float Distance) match =
                    secondRails.FirstOrDefault(item => item.Rail == rail);
                if (match.Rail == null)
                {
                    continue;
                }

                sharedRail = rail;
                sharedDistance = (firstDistance + match.Distance) * 0.5f;
                (firstOuterRail, firstOuterDistance) = firstRails.First(item => item.Rail != rail);
                (secondOuterRail, secondOuterDistance) = secondRails.First(item => item.Rail != rail);
                return firstOuterRail != null && secondOuterRail != null;
            }

            return false;
        }

        private static IEnumerable<(RailCenterline Rail, float Distance)> FrogRails(FrogCandidate frog)
        {
            yield return (frog.Intersection.RailA, frog.Intersection.DistanceA);
            yield return (frog.Intersection.RailB, frog.Intersection.DistanceB);
        }

        private static void CreateCrossingFrogAssembly(
            TrackObjectBuilder builder,
            GameObject root,
            SpecialWorkAnalysis analysis,
            FrogCandidate frog,
            IReadOnlyList<SwitchBladePlan> blades,
            Vector3 switchHome,
            string name)
        {
            if (!TryResolveNarrowBranchCrossingRails(
                    frog,
                    out RailCenterline standardRail,
                    out float standardDistance,
                    out RailCenterline narrowRail,
                    out float narrowDistance))
            {
                CreateGenericCrossingPoints(
                    builder,
                    root,
                    analysis,
                    frog,
                    blades,
                    switchHome,
                    name);
                return;
            }

            LineCurve stockHandoff = BuildNarrowBranchStockHandoff(
                frog,
                blades,
                standardRail,
                standardDistance,
                narrowRail,
                narrowDistance);
            CreateRail(
                builder,
                root,
                CorrectMeasuredRailRenderFrame(
                    analysis,
                    standardRail.Id,
                    stockHandoff,
                    preserveProfileCenter: false),
                switchHome,
                name + "-ContinuousStockHandoff",
                _ => 1f);
        }

        private static void CreateGenericCrossingPoints(
            TrackObjectBuilder builder,
            GameObject root,
            SpecialWorkAnalysis analysis,
            FrogCandidate frog,
            IReadOnlyList<SwitchBladePlan> blades,
            Vector3 switchHome,
            string name)
        {
            Vector3 bladeDirection = DirectionTowardBlades(frog, blades);
            float pointSetback = Mathf.Clamp(
                frog.FlangewaySetback,
                Gauge.Standard.HeadWidth + FrogPointNoseTaperLength * 0.15f,
                frog.CutHalfLength * 0.45f);
            int index = 0;
            foreach ((RailCenterline rail, float distance) in new[]
            {
                (frog.Intersection.RailA, frog.Intersection.DistanceA),
                (frog.Intersection.RailB, frog.Intersection.DistanceB)
            })
            {
                RailCenterline crossingRail = rail == frog.Intersection.RailA
                    ? frog.Intersection.RailB
                    : frog.Intersection.RailA;
                float bladeSide = SideTowardDirection(
                    rail,
                    distance,
                    frog.Intersection.Position,
                    bladeDirection);
                foreach (float side in new[] { bladeSide, -bladeSide })
                {
                    LineCurve point = SliceSignedSpan(
                        rail,
                        distance,
                        side * frog.CutHalfLength,
                        side * pointSetback);
                    if (point.Length < MinimumRailPieceLength)
                    {
                        continue;
                    }

                    var flangewayCuts = new List<(LineCurve Center, Vector3 KeepPoint)>();
                    Vector3 keepPoint = point.LinePointAtDistance(
                        side > 0 ? point.Length : 0f).point;
                    LineCurve crossingSlice = Slice(
                        crossingRail.Curve,
                        crossingRail.Curve.DistanceTo(point.Head.point),
                        crossingRail.Curve.DistanceTo(point.Tail.point));
                    if (crossingSlice.Length >= MinimumRailPieceLength)
                    {
                        flangewayCuts.Add((crossingSlice, keepPoint));
                    }

                    if (flangewayCuts.Count > 0)
                    {
                        CreateFlangewayCutRail(
                            builder,
                            root,
                            CorrectMeasuredRailRenderFrame(analysis, rail.Id, point),
                            flangewayCuts.Select(cut => (
                                CorrectMeasuredRailRenderFrame(analysis, crossingRail.Id, cut.Center),
                                cut.KeepPoint)).ToList(),
                            Gauge.Standard.HeadWidth * 0.5f + 0.025f,
                            switchHome,
                            name + "-Point-" + index++);
                    }
                    else
                    {
                        CreateTaperedPointRail(
                            builder,
                            root,
                            CorrectMeasuredRailRenderFrame(analysis, rail.Id, point),
                            switchHome,
                            name + "-Point-" + index++);
                    }
                }
            }
        }

        private static bool BladeHasSharedOwnerOverlap(
            SwitchBladePlan blade,
            IReadOnlyList<RailPiece> fixedRails)
        {
            return fixedRails.Any(piece =>
                piece.Kind == RailPieceKind.SharedRunning
                && TryFindBladeOverlap(
                    piece,
                    new[] { blade },
                    out _,
                    out _,
                    out _));
        }

        private static bool TryFindBladeOverlap(
            RailPiece piece,
            IEnumerable<SwitchBladePlan> blades,
            out SwitchBladePlan? matchingBlade,
            out float cutStart,
            out float cutEnd)
        {
            matchingBlade = null;
            cutStart = 0f;
            cutEnd = 0f;
            float bestLength = 0f;

            foreach (SwitchBladePlan blade in blades)
            {
                if (string.Equals(
                        piece.SourceRailId,
                        blade.MovableRail.Id,
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        piece.SourceRailId,
                        blade.StockRail.Id,
                        StringComparison.OrdinalIgnoreCase)
                    || !TryDistanceAlongCurve(
                        piece.Curve,
                        blade.BladeCurve.Head.point,
                        out float tip,
                        out float tipSeparation)
                    || !TryDistanceAlongCurve(
                        piece.Curve,
                        blade.BladeCurve.Tail.point,
                        out float rootDistance,
                        out float rootSeparation)
                    || tipSeparation > PhysicalOverlapTolerance
                    || rootSeparation > PhysicalOverlapTolerance)
                {
                    continue;
                }

                float start = Mathf.Min(tip, rootDistance);
                float end = Mathf.Max(tip, rootDistance);
                float length = end - start;
                if (length <= bestLength || length < MinimumOverlapLength)
                {
                    continue;
                }

                matchingBlade = blade;
                cutStart = start;
                cutEnd = end;
                bestLength = length;
            }

            return matchingBlade != null;
        }

        private static bool TryResolveNarrowBranchCrossingRails(
            FrogCandidate frog,
            out RailCenterline standardRail,
            out float standardDistance,
            out RailCenterline narrowRail,
            out float narrowDistance)
        {
            standardRail = null!;
            narrowRail = null!;
            standardDistance = 0f;
            narrowDistance = 0f;
            foreach ((RailCenterline rail, float distance) in new[]
            {
                (frog.Intersection.RailA, frog.Intersection.DistanceA),
                (frog.Intersection.RailB, frog.Intersection.DistanceB)
            })
            {
                if (rail.Family == GaugeGraphFamily.Standard)
                {
                    standardRail = rail;
                    standardDistance = distance;
                }
                else if (rail.Family == GaugeGraphFamily.Narrow)
                {
                    narrowRail = rail;
                    narrowDistance = distance;
                }
            }

            return standardRail != null && narrowRail != null;
        }

        private static bool TryCreateNarrowBranchExtendedFixedPoint(
            TrackObjectBuilder builder,
            GameObject root,
            SpecialWorkAnalysis analysis,
            RailPiece piece,
            IReadOnlyList<FrogCandidate> frogs,
            IReadOnlyList<WheelPath> wheelPaths,
            SpecialWorkGeometryParameters parameters,
            IReadOnlyList<SwitchBladePlan> blades,
            Vector3 switchHome,
            string name)
        {
            foreach (FrogCandidate frog in frogs.Where(item =>
                item.Intersection.Kind == RailIntersectionKind.CrossingFrogCandidate))
            {
                if (!TryResolveNarrowBranchCrossingRails(
                    frog,
                    out RailCenterline standardRail,
                    out float standardDistance,
                    out RailCenterline narrowRail,
                    out float narrowDistance))
                {
                    continue;
                }

                if (!TryResolveRailFlangeway(
                    standardRail,
                    wheelPaths,
                    out LineCurve standardFlangeway)
                    || !TryResolveRailFlangeway(
                        narrowRail,
                        wheelPaths,
                        out LineCurve narrowFlangeway))
                {
                    continue;
                }

                float narrowBladeSide = SideTowardDirection(
                    narrowRail,
                    narrowDistance,
                    frog.Intersection.Position,
                    DirectionTowardBlades(frog, blades));
                bool renderNarrowAfterFrog = narrowBladeSide > 0f;
                bool isGaugeSeparation = string.Equals(
                    analysis.Definition.Preset.Id,
                    SpecialWorkPresetIds.DualSplit,
                    StringComparison.OrdinalIgnoreCase);
                if (isGaugeSeparation
                    && piece.SourceRailId == standardRail.Id
                    && piece.EndDistance < standardDistance
                    && piece.EndDistance >= standardDistance - frog.CutHalfLength - 0.05f)
                {
                    float doubleFrogSetback = Mathf.Max(
                        0.08f,
                        CrossingPointSetback(frog) - 0.08f);
                    float pocketEnd = Mathf.Clamp(
                        standardDistance - doubleFrogSetback,
                        0f,
                        standardRail.Curve.Length);
                    if (pocketEnd - piece.StartDistance < MinimumRailPieceLength)
                    {
                        return false;
                    }

                    Vector3 keepPoint = standardRail.Curve.LinePointAtDistance(
                        Mathf.Clamp(
                            piece.EndDistance - MinimumRailPieceLength,
                            0f,
                            standardRail.Curve.Length)).point;
                    CreateFlangewayCutFrogRail(
                        builder,
                        root,
                        CorrectMeasuredRailRenderFrame(
                            analysis,
                            standardRail.Id,
                            Slice(standardRail.Curve, piece.StartDistance, pocketEnd)),
                        new[] { standardFlangeway, narrowFlangeway },
                        keepPoint,
                        parameters.FlangewayWidth,
                        switchHome,
                        name + "-GaugeSeparationDoubleFrog");
                    return true;
                }

                if (piece.SourceRailId == standardRail.Id
                    && piece.StartDistance > standardDistance
                    && piece.StartDistance <= standardDistance + frog.CutHalfLength + 0.05f)
                {
                    float pocketStart = Mathf.Clamp(
                        standardDistance - frog.CutHalfLength,
                        0f,
                        standardRail.Curve.Length);
                    if (piece.EndDistance - pocketStart < MinimumRailPieceLength)
                    {
                        return false;
                    }

                    Vector3 keepPoint = standardRail.Curve.LinePointAtDistance(
                        Mathf.Clamp(
                            piece.StartDistance + MinimumRailPieceLength,
                            0f,
                            standardRail.Curve.Length)).point;
                    string frogName = name + "-StandardThroughFrog";
                    bool localFlip = ShouldAutoFlipFlangewayKeepSide(analysis, frogName);
                    bool localizeCut = ShouldLocalizeFrogFlangewayCut(analysis, frogName);
                    CreateFlangewayCutFrogRail(
                        builder,
                        root,
                        CorrectMeasuredRailRenderFrame(
                            analysis,
                            standardRail.Id,
                            Slice(standardRail.Curve, pocketStart, piece.EndDistance)),
                        new[] { standardFlangeway, narrowFlangeway },
                        keepPoint,
                        parameters.FlangewayWidth,
                        switchHome,
                        frogName,
                        localFlip,
                        AutoFlipFlangewayKeepSideIndex(analysis, frogName),
                        localizeCut ? frog.Intersection.Position : (Vector3?)null,
                        localizeCut ? FrogFlangewayCutWindowLength(frog) : 0f);
                    return true;
                }

                if (renderNarrowAfterFrog
                    && piece.SourceRailId == narrowRail.Id
                    && piece.StartDistance > narrowDistance
                    && piece.StartDistance <= narrowDistance + frog.CutHalfLength + 0.05f)
                {
                    float pocketStart = Mathf.Clamp(
                        narrowDistance - frog.CutHalfLength,
                        0f,
                        narrowRail.Curve.Length);
                    if (piece.EndDistance - pocketStart < MinimumRailPieceLength)
                    {
                        return false;
                    }

                    Vector3 keepPoint = narrowRail.Curve.LinePointAtDistance(
                        Mathf.Clamp(
                            piece.StartDistance + MinimumRailPieceLength,
                            0f,
                            narrowRail.Curve.Length)).point;
                    CreateFlangewayCutFrogRail(
                        builder,
                        root,
                        CorrectMeasuredRailRenderFrame(
                            analysis,
                            narrowRail.Id,
                            Slice(narrowRail.Curve, pocketStart, piece.EndDistance)),
                        new[] { standardFlangeway, narrowFlangeway },
                        keepPoint,
                        parameters.FlangewayWidth,
                        switchHome,
                        name + "-NarrowThroughFrog");
                    return true;
                }

                if (piece.SourceRailId == narrowRail.Id
                    && !renderNarrowAfterFrog
                    && piece.EndDistance < narrowDistance
                    && piece.EndDistance >= narrowDistance - frog.CutHalfLength - 0.05f)
                {
                    float pocketEnd = Mathf.Clamp(
                        narrowDistance + frog.CutHalfLength,
                        0f,
                        narrowRail.Curve.Length);
                    if (pocketEnd - piece.StartDistance < MinimumRailPieceLength)
                    {
                        return false;
                    }

                    Vector3 keepPoint = narrowRail.Curve.LinePointAtDistance(
                        Mathf.Clamp(
                            piece.EndDistance - MinimumRailPieceLength,
                            0f,
                            narrowRail.Curve.Length)).point;
                    CreateFlangewayCutFrogRail(
                        builder,
                        root,
                        CorrectMeasuredRailRenderFrame(
                            analysis,
                            narrowRail.Id,
                            Slice(narrowRail.Curve, piece.StartDistance, pocketEnd)),
                        new[] { standardFlangeway, narrowFlangeway },
                        keepPoint,
                        parameters.FlangewayWidth,
                        switchHome,
                        name + "-NarrowReversedFrog");
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveRailFlangeway(
            RailCenterline rail,
            IReadOnlyList<WheelPath> wheelPaths,
            out LineCurve flangeway)
        {
            return TryResolveRailFlangeway(
                rail,
                wheelPaths,
                rail.Side,
                out flangeway);
        }

        private static bool TryResolveRailFlangeway(
            RailCenterline rail,
            IReadOnlyList<WheelPath> wheelPaths,
            RailSide guideSide,
            out LineCurve flangeway)
        {
            flangeway = null!;
            WheelPath? path = wheelPaths.FirstOrDefault(item =>
                    string.Equals(item.Id, rail.WheelPathId, StringComparison.OrdinalIgnoreCase))
                ?? wheelPaths.FirstOrDefault(item =>
                    rail.SourceRouteIds.Any(routeId =>
                        string.Equals(routeId, item.RouteId, StringComparison.OrdinalIgnoreCase)));
            if (path == null)
            {
                return false;
            }

            flangeway = path.FlangeGuide(guideSide);
            return flangeway != null && flangeway.Points.Count() >= 2;
        }

        private static RailSide OppositeSide(RailSide side)
        {
            return side == RailSide.Left ? RailSide.Right : RailSide.Left;
        }

        private static void CreateFlangewayCutFrogRail(
            TrackObjectBuilder builder,
            GameObject root,
            LineCurve worldCurve,
            IReadOnlyList<LineCurve> flangewayCenters,
            Vector3 keepPoint,
            float flangewayWidth,
            Vector3 switchHome,
            string name,
            bool invertKeepSide = false,
            int invertKeepSideIndex = -1,
            Vector3? cutFocusPoint = null,
            float cutWindowLength = 0f)
        {
            Mesh? mesh = BuildFlangewayCutFrogRailMesh(
                worldCurve,
                flangewayCenters,
                keepPoint,
                flangewayWidth,
                switchHome,
                invertKeepSide,
                invertKeepSideIndex,
                cutFocusPoint,
                cutWindowLength);
            if (mesh == null)
            {
                return;
            }

            NarrowGaugeTrackBuilder.CreateMeshObject(builder, mesh, name, root);
        }

        internal static Mesh? BuildFlangewayCutFrogRailMesh(
            LineCurve worldCurve,
            IReadOnlyList<LineCurve> flangewayCenters,
            Vector3 keepPoint,
            float flangewayWidth,
            Vector3 switchHome,
            bool invertKeepSide = false,
            int invertKeepSideIndex = -1,
            Vector3? cutFocusPoint = null,
            float cutWindowLength = 0f)
        {
            if (worldCurve == null
                || flangewayCenters == null
                || worldCurve.Points.Count() < 2
                || worldCurve.Length < MinimumRailPieceLength)
            {
                return null;
            }

            LineCurve sampled = worldCurve.Subdivide(0.08f);
            var cuts = new List<FlangewayCut>();
            float halfWidth = Mathf.Max(flangewayWidth * 0.5f, 0.001f);
            LineCurve[] centers = flangewayCenters
                .Where(item => item != null && item.Points.Count() >= 2)
                .ToArray();
            int invertIndex = invertKeepSide
                ? ResolveFlangewayCutIndexToInvert(centers, worldCurve, invertKeepSideIndex)
                : -1;
            for (int index = 0; index < centers.Length; index++)
            {
                LineCurve center = centers[index];
                float windowStart = 0f;
                float windowEnd = center.Length;
                bool hasDistanceWindow = cutFocusPoint.HasValue
                    && cutWindowLength > 0.001f;
                if (hasDistanceWindow)
                {
                    Vector3 focusPoint = cutFocusPoint.GetValueOrDefault();
                    float centerDistance = Mathf.Clamp(
                        center.DistanceTo(focusPoint),
                        0f,
                        center.Length);
                    float halfWindow = Mathf.Max(cutWindowLength * 0.5f, halfWidth * 2f);
                    windowStart = Mathf.Max(0f, centerDistance - halfWindow);
                    windowEnd = Mathf.Min(center.Length, centerDistance + halfWindow);
                }

                float keepSignedDistance = SignedDistanceToCurve(
                    center,
                    keepPoint,
                    out _);
                float keepSign = keepSignedDistance >= 0f ? 1f : -1f;
                if (index == invertIndex)
                {
                    keepSign = -keepSign;
                }

                cuts.Add(new FlangewayCut(
                    center,
                    halfWidth,
                    keepSign,
                    windowStart,
                    windowEnd,
                    hasDistanceWindow));
            }

            Mesh mesh = NarrowGaugeTrackBuilder.BuildStockRailMesh(
                sampled.Offset(-switchHome),
                switchHome,
                Gauge.Standard,
                _ => 1f);
            ApplyFlangewayCuts(mesh, cuts, switchHome);
            if (mesh.vertexCount == 0 || mesh.triangles.Length == 0)
            {
                Main.Warn("[FlangewayCut] Clipping removed all rail geometry; returning uncut rail.");
                return NarrowGaugeTrackBuilder.BuildStockRailMesh(
                    sampled.Offset(-switchHome),
                    switchHome,
                    Gauge.Standard,
                    _ => 1f);
            }

            return mesh;
        }

        private static int ResolveFlangewayCutIndexToInvert(
            IReadOnlyList<LineCurve> centers,
            LineCurve worldCurve,
            int requestedIndex)
        {
            if (centers.Count == 0)
            {
                return -1;
            }

            if (requestedIndex >= 0 && requestedIndex < centers.Count)
            {
                return requestedIndex;
            }

            if (centers.Count == 1)
            {
                return 0;
            }

            int selected = 0;
            float bestDistance = float.PositiveInfinity;
            for (int index = 0; index < centers.Count; index++)
            {
                float distance = ClosestDistanceBetweenCurves(centers[index], worldCurve);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    selected = index;
                }
            }

            return selected;
        }

        private static float ClosestDistanceBetweenCurves(LineCurve first, LineCurve second)
        {
            if (first == null || second == null || second.Points.Count() == 0)
            {
                return float.PositiveInfinity;
            }

            int sampleCount = Mathf.Clamp(
                Mathf.CeilToInt(second.Length / 0.25f) + 1,
                3,
                25);
            float best = float.PositiveInfinity;
            for (int index = 0; index < sampleCount; index++)
            {
                float distance = sampleCount <= 1
                    ? 0f
                    : second.Length * index / (sampleCount - 1);
                Vector3 point = second.LinePointAtDistance(distance).point;
                float firstDistance = Mathf.Clamp(first.DistanceTo(point), 0f, first.Length);
                Vector3 closest = first.LinePointAtDistance(firstDistance).point;
                best = Mathf.Min(best, Vector3.Distance(point, closest));
            }

            return best;
        }

        internal static bool ShouldAutoFlipFlangewayKeepSide(
            SpecialWorkAnalysis analysis,
            string objectName)
        {
            return false;
        }

        internal static int AutoFlipFlangewayKeepSideIndex(
            SpecialWorkAnalysis analysis,
            string objectName)
        {
            return ShouldAutoFlipFlangewayKeepSide(analysis, objectName)
                ? 1
                : -1;
        }

        internal static bool ShouldLocalizeFrogFlangewayCut(
            SpecialWorkAnalysis analysis,
            string objectName)
        {
            return IsDualBothDiverge(analysis)
                && analysis.Definition.Id.IndexOf("fc97", StringComparison.OrdinalIgnoreCase) >= 0
                && objectName.IndexOf("StandardThroughFrog", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static float FrogFlangewayCutWindowLength(FrogCandidate frog)
        {
            return Mathf.Max(0.45f, frog.CutHalfLength * 2.25f);
        }

        internal static void CreateFlangewayCutRail(
            TrackObjectBuilder builder,
            GameObject root,
            LineCurve worldCurve,
            IReadOnlyList<(LineCurve Center, Vector3 KeepPoint)> flangewayCenters,
            float flangewayWidth,
            Vector3 switchHome,
            string name)
        {
            if (worldCurve == null
                || flangewayCenters == null
                || worldCurve.Points.Count() < 2
                || worldCurve.Length < MinimumRailPieceLength)
            {
                return;
            }

            LineCurve sampled = worldCurve.Subdivide(0.08f);
            var cuts = new List<FlangewayCut>();
            float halfWidth = Mathf.Max(flangewayWidth * 0.5f, 0.001f);
            foreach ((LineCurve center, Vector3 keepPoint) in flangewayCenters.Where(item =>
                item.Center != null && item.Center.Points.Count() >= 2))
            {
                float keepSignedDistance = SignedDistanceToCurve(
                    center,
                    keepPoint,
                    out _);
                cuts.Add(new FlangewayCut(
                    center,
                    halfWidth,
                    keepSignedDistance >= 0f ? 1f : -1f));
            }

            Mesh mesh = NarrowGaugeTrackBuilder.BuildStockRailMesh(
                sampled.Offset(-switchHome),
                switchHome,
                Gauge.Standard,
                _ => 1f);
            int trianglesBefore = mesh.triangles.Length / 3;
            Vector3[] sourceVertices = mesh.vertices;
            for (int cutIndex = 0; cutIndex < cuts.Count; cutIndex++)
            {
                FlangewayCut cut = cuts[cutIndex];
                float minimumClearance = float.MaxValue;
                float maximumClearance = float.MinValue;
                foreach (Vector3 vertex in sourceVertices)
                {
                    float clearance = ClearancePastCut(vertex + switchHome, cut);
                    minimumClearance = Mathf.Min(minimumClearance, clearance);
                    maximumClearance = Mathf.Max(maximumClearance, clearance);
                }

                Main.Log(
                    $"[SharedRailTransitionClip] {name} cut={cutIndex} " +
                    $"keepSign={cut.KeepSign:0} clearance=" +
                    $"{minimumClearance:0.000}/{maximumClearance:0.000} " +
                    $"crosses={(minimumClearance < 0f && maximumClearance > 0f)}.");
            }

            ApplyFlangewayCuts(mesh, cuts, switchHome);
            Main.Log(
                $"[SharedRailTransitionClip] {name} triangles=" +
                $"{trianglesBefore}->{mesh.triangles.Length / 3}.");
            NarrowGaugeTrackBuilder.CreateMeshObject(builder, mesh, name, root);
        }

        internal static void CreateFlangewayCutRailDirect(
            LineCurve worldCurve,
            IReadOnlyList<(LineCurve Center, Vector3 KeepPoint)> flangewayCenters,
            float flangewayWidth,
            Vector3 switchHome,
            out Mesh? result)
        {
            result = null;
            if (worldCurve == null
                || flangewayCenters == null
                || worldCurve.Points.Count() < 2
                || worldCurve.Length < MinimumRailPieceLength)
            {
                return;
            }

            LineCurve sampled = worldCurve.Subdivide(0.08f);
            var cuts = new List<FlangewayCut>();
            float halfWidth = Mathf.Max(flangewayWidth * 0.5f, 0.001f);
            foreach ((LineCurve center, Vector3 keepPoint) in flangewayCenters.Where(item =>
                item.Center != null && item.Center.Points.Count() >= 2))
            {
                float keepSignedDistance = SignedDistanceToCurve(
                    center,
                    keepPoint,
                    out _);
                cuts.Add(new FlangewayCut(
                    center,
                    halfWidth,
                    keepSignedDistance >= 0f ? 1f : -1f));
            }

            if (cuts.Count == 0)
            {
                return;
            }

            Mesh mesh = NarrowGaugeTrackBuilder.BuildStockRailMesh(
                sampled.Offset(-switchHome),
                switchHome,
                Gauge.Standard,
                _ => 1f);
            ApplyFlangewayCuts(mesh, cuts, switchHome);
            result = mesh;
        }

        internal static LineCurve CorrectMeasuredRailRenderFramePublic(
            SpecialWorkAnalysis analysis,
            string sourceRailId,
            LineCurve curve)
        {
            return CorrectMeasuredRailRenderFrame(analysis, sourceRailId, curve);
        }

        private static void ApplyFlangewayCuts(
            Mesh mesh,
            IReadOnlyList<FlangewayCut> cuts,
            Vector3 switchHome)
        {
            if (mesh == null || cuts.Count == 0)
            {
                return;
            }

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            Vector2[] uvs = mesh.uv;
            Vector3[] normals = mesh.normals;
            bool hasUvs = uvs != null && uvs.Length == vertices.Length;
            bool hasNormals = normals != null && normals.Length == vertices.Length;

            var clippedVertices = new List<Vector3>(vertices.Length);
            var clippedUvs = hasUvs ? new List<Vector2>(vertices.Length) : null;
            var clippedNormals = hasNormals ? new List<Vector3>(vertices.Length) : null;
            var clippedTriangles = new List<int>(triangles.Length);

            for (int triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex += 3)
            {
                var polygon = new List<MeshClipVertex>(3)
                {
                    CreateClipVertex(triangles[triangleIndex], vertices, uvs, normals, hasUvs, hasNormals, switchHome),
                    CreateClipVertex(triangles[triangleIndex + 1], vertices, uvs, normals, hasUvs, hasNormals, switchHome),
                    CreateClipVertex(triangles[triangleIndex + 2], vertices, uvs, normals, hasUvs, hasNormals, switchHome)
                };

                foreach (FlangewayCut cut in cuts)
                {
                    polygon = ClipPolygonAgainstFlangeway(polygon, cut);
                    if (polygon.Count < 3)
                    {
                        break;
                    }
                }

                if (polygon.Count < 3)
                {
                    continue;
                }

                for (int index = 1; index < polygon.Count - 1; index++)
                {
                    AddClippedTriangle(
                        polygon[0],
                        polygon[index],
                        polygon[index + 1],
                        switchHome,
                        clippedVertices,
                        clippedUvs,
                        clippedNormals,
                        clippedTriangles);
                }
            }

            mesh.Clear();
            mesh.vertices = clippedVertices.ToArray();
            mesh.triangles = clippedTriangles.ToArray();
            if (clippedUvs != null)
            {
                mesh.uv = clippedUvs.ToArray();
            }

            if (clippedNormals != null)
            {
                mesh.normals = clippedNormals.ToArray();
            }
            else
            {
                mesh.RecalculateNormals();
            }

            mesh.RecalculateBounds();
        }

        private static MeshClipVertex CreateClipVertex(
            int index,
            Vector3[] vertices,
            Vector2[]? uvs,
            Vector3[]? normals,
            bool hasUvs,
            bool hasNormals,
            Vector3 switchHome)
        {
            return new MeshClipVertex(
                vertices[index] + switchHome,
                hasUvs && uvs != null ? uvs[index] : Vector2.zero,
                hasNormals && normals != null ? normals[index] : Vector3.up);
        }

        private static List<MeshClipVertex> ClipPolygonAgainstFlangeway(
            List<MeshClipVertex> polygon,
            FlangewayCut cut)
        {
            var output = new List<MeshClipVertex>(polygon.Count + 2);
            MeshClipVertex previous = polygon[polygon.Count - 1];
            float previousClearance = ClearancePastCut(previous.World, cut);
            bool previousInside = previousClearance >= 0f;

            for (int index = 0; index < polygon.Count; index++)
            {
                MeshClipVertex current = polygon[index];
                float currentClearance = ClearancePastCut(current.World, cut);
                bool currentInside = currentClearance >= 0f;

                if (currentInside)
                {
                    if (!previousInside)
                    {
                        output.Add(FindCutBoundary(previous, current, previousClearance, cut));
                    }

                    output.Add(current);
                }
                else if (previousInside)
                {
                    output.Add(FindCutBoundary(previous, current, previousClearance, cut));
                }

                previous = current;
                previousClearance = currentClearance;
                previousInside = currentInside;
            }

            return output;
        }

        private static MeshClipVertex FindCutBoundary(
            MeshClipVertex start,
            MeshClipVertex end,
            float startClearance,
            FlangewayCut cut)
        {
            float low = 0f;
            float high = 1f;
            bool lowInside = startClearance >= 0f;
            for (int iteration = 0; iteration < 10; iteration++)
            {
                float middle = (low + high) * 0.5f;
                MeshClipVertex sample = MeshClipVertex.Lerp(start, end, middle);
                bool middleInside = ClearancePastCut(sample.World, cut) >= 0f;
                if (middleInside == lowInside)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            return MeshClipVertex.Lerp(start, end, (low + high) * 0.5f);
        }

        private static float ClearancePastCut(Vector3 worldPoint, FlangewayCut cut)
        {
            float centerDistance = Mathf.Clamp(cut.Center.DistanceTo(worldPoint), 0f, cut.Center.Length);
            if (cut.HasDistanceWindow
                && (centerDistance < cut.WindowStart || centerDistance > cut.WindowEnd))
            {
                return 1f;
            }

            return SignedDistanceToCurveAtDistance(cut.Center, centerDistance, worldPoint, out _)
                * cut.KeepSign
                - cut.HalfWidth;
        }

        private static void AddClippedTriangle(
            MeshClipVertex a,
            MeshClipVertex b,
            MeshClipVertex c,
            Vector3 switchHome,
            List<Vector3> vertices,
            List<Vector2>? uvs,
            List<Vector3>? normals,
            List<int> triangles)
        {
            Vector3 cross = Vector3.Cross(b.World - a.World, c.World - a.World);
            if (cross.sqrMagnitude <= 0.00000001f)
            {
                return;
            }

            int start = vertices.Count;
            vertices.Add(a.World - switchHome);
            vertices.Add(b.World - switchHome);
            vertices.Add(c.World - switchHome);
            uvs?.Add(a.Uv);
            uvs?.Add(b.Uv);
            uvs?.Add(c.Uv);
            normals?.Add(a.Normal);
            normals?.Add(b.Normal);
            normals?.Add(c.Normal);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
        }

        private static float SignedDistanceToCurve(
            LineCurve center,
            Vector3 point,
            out Vector3 normal)
        {
            float distance = Mathf.Clamp(center.DistanceTo(point), 0f, center.Length);
            return SignedDistanceToCurveAtDistance(center, distance, point, out normal);
        }

        private static float SignedDistanceToCurveAtDistance(
            LineCurve center,
            float distance,
            Vector3 point,
            out Vector3 normal)
        {
            LinePoint centerPoint = center.LinePointAtDistance(
                Mathf.Clamp(distance, 0f, center.Length));
            normal = centerPoint.Rotation * Vector3.right;
            normal.y = 0f;
            if (normal.sqrMagnitude <= 0.0001f)
            {
                Vector3 direction = centerPoint.direction;
                direction.y = 0f;
                normal = Vector3.Cross(Vector3.up, direction);
            }

            normal.Normalize();
            Vector3 delta = point - centerPoint.point;
            delta.y = 0f;
            return Vector3.Dot(delta, normal);
        }

        private readonly struct FlangewayCut
        {
            public FlangewayCut(
                LineCurve center,
                float halfWidth,
                float keepSign,
                float windowStart = 0f,
                float windowEnd = 0f,
                bool hasDistanceWindow = false)
            {
                Center = center;
                HalfWidth = halfWidth;
                KeepSign = keepSign;
                WindowStart = windowStart;
                WindowEnd = windowEnd;
                HasDistanceWindow = hasDistanceWindow;
            }

            public LineCurve Center { get; }
            public float HalfWidth { get; }
            public float KeepSign { get; }
            public float WindowStart { get; }
            public float WindowEnd { get; }
            public bool HasDistanceWindow { get; }
        }

        private readonly struct MeshClipVertex
        {
            public MeshClipVertex(Vector3 world, Vector2 uv, Vector3 normal)
            {
                World = world;
                Uv = uv;
                Normal = normal;
            }

            public Vector3 World { get; }
            public Vector2 Uv { get; }
            public Vector3 Normal { get; }

            public static MeshClipVertex Lerp(MeshClipVertex a, MeshClipVertex b, float t)
            {
                Vector3 normal = Vector3.Lerp(a.Normal, b.Normal, t);
                if (normal.sqrMagnitude > 0.0001f)
                {
                    normal.Normalize();
                }

                return new MeshClipVertex(
                    Vector3.Lerp(a.World, b.World, t),
                    Vector2.Lerp(a.Uv, b.Uv, t),
                    normal);
            }
        }

        private static LineCurve BuildNarrowBranchStockHandoff(
            FrogCandidate frog,
            IReadOnlyList<SwitchBladePlan> blades,
            RailCenterline standardRail,
            float standardDistance,
            RailCenterline narrowRail,
            float narrowDistance)
        {
            Vector3 bladeDirection = DirectionTowardBlades(frog, blades);
            float standardBladeSide = SideTowardDirection(
                standardRail,
                standardDistance,
                frog.Intersection.Position,
                bladeDirection);
            float narrowBladeSide = SideTowardDirection(
                narrowRail,
                narrowDistance,
                frog.Intersection.Position,
                bladeDirection);
            LinePoint standardStockBoundary = PointAtSignedOffset(
                standardRail,
                standardDistance,
                standardBladeSide * frog.CutHalfLength);
            LinePoint narrowStockBoundary = PointAtSignedOffset(
                narrowRail,
                narrowDistance,
                -narrowBladeSide * frog.CutHalfLength);
            LineCurve handoff = BuildKinkedHandoff(
                standardStockBoundary,
                narrowStockBoundary,
                frog.Intersection.Position);
            LineCurve positive = handoff.Parallel(Gauge.Standard.HeadWidth);
            LineCurve negative = handoff.Parallel(-Gauge.Standard.HeadWidth);
            Vector3 stdBefore = standardRail.Curve.LinePointAtDistance(
                Mathf.Max(0f, standardDistance - frog.CutHalfLength * 1.5f)).point;
            Vector3 stdAfter = standardRail.Curve.LinePointAtDistance(
                Mathf.Min(standardRail.Curve.Length, standardDistance + frog.CutHalfLength * 1.5f)).point;
            float positiveToStd = Mathf.Min(
                Vector3.Distance(positive.Head.point, stdBefore),
                Vector3.Distance(positive.Tail.point, stdAfter));
            float negativeToStd = Mathf.Min(
                Vector3.Distance(negative.Head.point, stdBefore),
                Vector3.Distance(negative.Tail.point, stdAfter));
            return positiveToStd <= negativeToStd ? positive : negative;
        }

        private static float CrossingPointSetback(FrogCandidate frog)
        {
            return Mathf.Clamp(
                frog.FlangewaySetback * 0.28f,
                0.16f,
                Mathf.Min(0.24f, frog.CutHalfLength * 0.35f));
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

        private static LineCurve SliceSignedSpan(
            RailCenterline rail,
            float centerDistance,
            float fromOffset,
            float toOffset)
        {
            float from = Mathf.Clamp(centerDistance + fromOffset, 0f, rail.Curve.Length);
            float to = Mathf.Clamp(centerDistance + toOffset, 0f, rail.Curve.Length);
            LineCurve curve = Slice(rail.Curve, Mathf.Min(from, to), Mathf.Max(from, to));
            return from <= to ? curve : curve.Reverse();
        }

        private static LineCurve BuildKinkedHandoff(
            LinePoint start,
            LinePoint end,
            Vector3 crossing)
        {
            Vector3 spanDirection = end.point - start.point;
            Vector3 startTangent = start.direction;
            Vector3 endTangent = end.direction;
            startTangent.y = 0f;
            endTangent.y = 0f;
            spanDirection.y = 0f;
            if (Vector3.Dot(startTangent, spanDirection) < 0f)
            {
                startTangent = -startTangent;
            }
            if (Vector3.Dot(endTangent, spanDirection) < 0f)
            {
                endTangent = -endTangent;
            }
            startTangent = startTangent.sqrMagnitude > 0.0001f
                ? startTangent.normalized
                : spanDirection.normalized;
            endTangent = endTangent.sqrMagnitude > 0.0001f
                ? endTangent.normalized
                : spanDirection.normalized;
            Vector3 kinkDirection = (startTangent + endTangent).normalized;
            if (kinkDirection.sqrMagnitude <= 0.0001f)
            {
                kinkDirection = spanDirection.normalized;
            }

            return new LineCurve(
                new[]
                {
                    new LinePoint(start.point, Quaternion.LookRotation(startTangent, Vector3.up)),
                    new LinePoint(crossing, Quaternion.LookRotation(kinkDirection, Vector3.up)),
                    new LinePoint(end.point, Quaternion.LookRotation(endTangent, Vector3.up))
                },
                Hand.Left);
        }

        private static void CreateTaperedPointRail(
            TrackObjectBuilder builder,
            GameObject root,
            LineCurve worldCurve,
            Vector3 switchHome,
            string name,
            bool taperAtStart = false)
        {
            if (worldCurve == null
                || worldCurve.Points.Count() < 2
                || worldCurve.Length < MinimumRailPieceLength)
            {
                return;
            }

            int pointCount = worldCurve.Points.Count();
            Mesh mesh = NarrowGaugeTrackBuilder.BuildStockRailMesh(
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
            NarrowGaugeTrackBuilder.CreateMeshObject(builder, mesh, name, root);
        }

        private static void CreateExtendedFrogPointRail(
            TrackObjectBuilder builder,
            GameObject root,
            LineCurve worldCurve,
            float taperLength,
            Vector3 switchHome,
            string name,
            bool taperAtStart = false)
        {
            if (worldCurve == null
                || worldCurve.Points.Count() < 2
                || worldCurve.Length < MinimumRailPieceLength)
            {
                return;
            }

            LineCurve sampled = worldCurve.Subdivide(0.18f);
            LinePoint[] source = sampled.Points.ToArray();
            if (source.Length < 2)
            {
                return;
            }

            var distances = new float[source.Length];
            for (int i = 1; i < source.Length; i++)
            {
                distances[i] = distances[i - 1]
                    + Vector3.Distance(source[i - 1].point, source[i].point);
            }

            float totalLength = Mathf.Max(distances[distances.Length - 1], MinimumRailPieceLength);
            float effectiveTaperLength = Mathf.Clamp(
                Mathf.Min(taperLength, FrogPointNoseTaperLength),
                MinimumRailPieceLength,
                totalLength);
            Mesh mesh = NarrowGaugeTrackBuilder.BuildStockRailMesh(
                sampled.Offset(-switchHome),
                switchHome,
                Gauge.Standard,
                index =>
                {
                    int pointIndex = Mathf.Clamp(index, 0, distances.Length - 1);
                    float distanceFromTip = taperAtStart
                        ? distances[pointIndex]
                        : totalLength - distances[pointIndex];
                    float fromTip = Mathf.Clamp01(distanceFromTip / effectiveTaperLength);
                    return Mathf.SmoothStep(0f, 1f, fromTip);
                });
            NarrowGaugeTrackBuilder.CreateMeshObject(builder, mesh, name, root);
        }

        private static void CreatePlanedFrogPointRail(
            TrackObjectBuilder builder,
            GameObject root,
            LineCurve worldCurve,
            float taperLength,
            LineCurve stockReference,
            Vector3 switchHome,
            string name,
            bool taperAtStart = false)
        {
            if (worldCurve == null
                || stockReference == null
                || worldCurve.Points.Count() < 2
                || worldCurve.Length < MinimumRailPieceLength)
            {
                return;
            }

            LineCurve sampled = worldCurve.Subdivide(0.18f);
            LinePoint[] source = sampled.Points.ToArray();
            if (source.Length < 2)
            {
                return;
            }

            var distances = new float[source.Length];
            for (int i = 1; i < source.Length; i++)
            {
                distances[i] = distances[i - 1]
                    + Vector3.Distance(source[i - 1].point, source[i].point);
            }

            float totalLength = Mathf.Max(distances[distances.Length - 1], MinimumRailPieceLength);
            float effectiveTaperLength = Mathf.Clamp(
                Mathf.Min(taperLength, FrogPointNoseTaperLength),
                MinimumRailPieceLength,
                totalLength);

            int tipIndex = taperAtStart ? 0 : source.Length - 1;
            LinePoint tip = source[tipIndex];
            LinePoint stockAtTip = stockReference.LinePointAtDistance(
                stockReference.DistanceTo(tip.point));
            Vector3 away = tip.point - stockAtTip.point;
            away.y = 0f;
            if (away.sqrMagnitude <= 0.0001f)
            {
                int heelIndex = taperAtStart ? source.Length - 1 : 0;
                LinePoint heel = source[heelIndex];
                LinePoint stockAtHeel = stockReference.LinePointAtDistance(
                    stockReference.DistanceTo(heel.point));
                away = heel.point - stockAtHeel.point;
                away.y = 0f;
            }

            if (away.sqrMagnitude <= 0.0001f)
            {
                away = tip.Rotation * Vector3.right;
                away.y = 0f;
            }

            away.Normalize();
            float desiredSeparation = Gauge.Standard.HeadWidth + 0.05f;
            Vector3 desiredTip = stockAtTip.point + away * desiredSeparation;
            Vector3 tipDelta = desiredTip - tip.point;
            tipDelta.y = 0f;

            var adjusted = new LinePoint[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                float distanceFromTip = taperAtStart
                    ? distances[i]
                    : totalLength - distances[i];
                float noseWeight = 1f - Mathf.Clamp01(distanceFromTip / effectiveTaperLength);
                noseWeight = Mathf.SmoothStep(0f, 1f, noseWeight);
                adjusted[i] = new LinePoint(
                    source[i].point + tipDelta * noseWeight,
                    source[i].Rotation);
            }

            for (int i = 0; i < adjusted.Length; i++)
            {
                Vector3 direction;
                if (i == 0)
                {
                    direction = adjusted[1].point - adjusted[0].point;
                }
                else if (i == adjusted.Length - 1)
                {
                    direction = adjusted[i].point - adjusted[i - 1].point;
                }
                else
                {
                    direction = adjusted[i + 1].point - adjusted[i - 1].point;
                }

                direction.y = 0f;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    adjusted[i] = new LinePoint(
                        adjusted[i].point,
                        Quaternion.LookRotation(direction.normalized, Vector3.up));
                }
            }

            LineCurve planed = new LineCurve(adjusted, sampled.hand);
            Mesh mesh = NarrowGaugeTrackBuilder.BuildStockRailMesh(
                planed.Offset(-switchHome),
                switchHome,
                Gauge.Standard,
                index =>
                {
                    int pointIndex = Mathf.Clamp(index, 0, distances.Length - 1);
                    float distanceFromTip = taperAtStart
                        ? distances[pointIndex]
                        : totalLength - distances[pointIndex];
                    float fromTip = Mathf.Clamp01(distanceFromTip / effectiveTaperLength);
                    return Mathf.SmoothStep(0f, 1f, fromTip);
                });
            NarrowGaugeTrackBuilder.CreateMeshObject(builder, mesh, name, root);
        }

        private static LinePoint PointAtSignedOffset(
            RailCenterline rail,
            float centerDistance,
            float signedOffset)
        {
            return rail.Curve.LinePointAtDistance(
                Mathf.Clamp(centerDistance + signedOffset, 0f, rail.Curve.Length));
        }

        private static LinePoint HeelPoint(
            RailCenterline rail,
            float intersectionDistance,
            FrogCandidate frog,
            Vector3 noseSide)
        {
            float beforeDistance = Mathf.Max(0f, intersectionDistance - frog.CutHalfLength);
            float afterDistance = Mathf.Min(rail.Curve.Length, intersectionDistance + frog.CutHalfLength);
            LinePoint before = rail.Curve.LinePointAtDistance(beforeDistance);
            LinePoint after = rail.Curve.LinePointAtDistance(afterDistance);
            Vector3 beforeDirection = before.point - frog.Intersection.Position;
            Vector3 afterDirection = after.point - frog.Intersection.Position;
            return Vector3.Dot(beforeDirection, noseSide)
                <= Vector3.Dot(afterDirection, noseSide)
                ? before
                : after;
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

    }
}
