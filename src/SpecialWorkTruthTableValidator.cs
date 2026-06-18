using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Core;
using Newtonsoft.Json;
using Track;
using UnityEngine;

namespace NarrowGaugeMod
{
    internal static class SpecialWorkTruthTableValidator
    {
        private const float ContinuityTolerance = 0.08f;
        private const float BladeOverlapTolerance = 0.08f;
        private const float BladeOverlapSampleSpacing = 0.1f;
        private const float SharedOverlapTolerance = 0.08f;

        public static IEnumerable<string> Validate(
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<SharedRailInterval> shared,
            IReadOnlyList<RailPiece> fixedRails,
            IReadOnlyList<RailWorkInterval> workIntervals,
            IReadOnlyList<RailCut> cuts,
            IReadOnlyList<FrogCandidate> frogs,
            IReadOnlyList<WingRailPlan> wings,
            IReadOnlyList<GuardRailPlan> guards,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            if (!SpecialWorkTruthTableCatalog.TryGet(
                definition.Preset.Id,
                rails,
                frogs,
                out TurnoutTruthTable truth))
            {
                yield break;
            }

            foreach (string issue in ValidateRoutes(definition, truth))
            {
                yield return Format(truth, issue);
            }

            foreach (TruthRail railRule in truth.Rails)
            {
                RailCenterline? rail = FindRail(rails, railRule);
                if (rail == null)
                {
                    yield return Format(
                        truth,
                        $"required rail '{railRule.Label}' was not generated " +
                        $"({railRule.RouteId}:{railRule.Side}).");
                    continue;
                }

                foreach (string issue in ValidateRailRule(
                    definition,
                    railRule,
                    rail,
                    fixedRails,
                    cuts,
                    frogs,
                    blades))
                {
                    yield return Format(truth, issue);
                }
            }

            foreach (TruthSuppressedInterval interval in truth.SuppressedIntervals)
            {
                RailCenterline? rail = FindRail(rails, interval.RouteId, interval.Side);
                if (rail == null)
                {
                    yield return Format(
                        truth,
                        $"suppressed interval '{interval.Label}' references missing rail " +
                        $"'{interval.RouteId}:{interval.Side}'.");
                    continue;
                }

                if (!TryParseEnum(interval.Kind, out RailCutKind kind))
                {
                    yield return Format(
                        truth,
                        $"suppressed interval '{interval.Label}' uses unknown cut kind '{interval.Kind}'.");
                    continue;
                }

                if (!cuts.Any(cut => cut.Rail == rail && cut.Kind == kind))
                {
                    yield return Format(
                        truth,
                        $"rail '{rail.Id}' is missing required suppressed interval kind '{kind}'.");
                }
            }

            TruthFrogRules frogRules = truth.Frogs ?? new TruthFrogRules();
            if (frogs.Count < frogRules.MinimumCount)
            {
                yield return Format(
                    truth,
                    $"expected at least {frogRules.MinimumCount} frog candidate(s), got {frogs.Count}.");
            }

            if (frogRules.RequireCuts)
            {
                foreach (FrogCandidate frog in frogs)
                {
                    if (!HasFrogCut(cuts, frog.Intersection.RailA, frog.Intersection.DistanceA)
                        || !HasFrogCut(cuts, frog.Intersection.RailB, frog.Intersection.DistanceB))
                    {
                        yield return Format(
                            truth,
                            $"frog '{frog.Id}' does not cut both involved rails.");
                    }
                }
            }

            if (frogRules.RequireWingRails)
            {
                foreach (FrogCandidate frog in frogs)
                {
                    int expectedWingCount =
                        frog.Intersection.Kind == RailIntersectionKind.VeeFrogCandidate ? 2 : 4;
                    if (wings.Count(wing => wing.FrogId == frog.Id) < expectedWingCount)
                    {
                        yield return Format(
                            truth,
                            $"frog '{frog.Id}' has fewer than {expectedWingCount} type-specific wing rails.");
                    }
                }
            }

            if (frogRules.RequireGuardRails)
            {
                foreach (FrogCandidate frog in frogs)
                {
                    if (guards.Count(guard => guard.FrogId == frog.Id) < 2)
                    {
                        yield return Format(
                            truth,
                            $"frog '{frog.Id}' has fewer than two guard rails.");
                    }
                }
            }

            if (truth.Guards != null && guards.Count < truth.Guards.MinimumCount)
            {
                yield return Format(
                    truth,
                    $"expected at least {truth.Guards.MinimumCount} guard rail(s), got {guards.Count}.");
            }

            if (truth.WingRails != null && wings.Count < truth.WingRails.MinimumCount)
            {
                yield return Format(
                    truth,
                    $"expected at least {truth.WingRails.MinimumCount} wing rail(s), got {wings.Count}.");
            }

            foreach (TruthBlade bladeRule in truth.Blades)
            {
                if (!TryResolveBladeRule(rails, bladeRule, out RailCenterline? movable, out RailCenterline? stock))
                {
                    yield return Format(
                        truth,
                        $"blade '{bladeRule.Label}' references missing stock or movable rail.");
                    continue;
                }

                if (!blades.Any(blade => blade.MovableRail == movable && blade.StockRail == stock))
                {
                    yield return Format(
                        truth,
                        $"blade '{bladeRule.Label}' was not generated as stock '{stock!.Id}' " +
                        $"and movable '{movable!.Id}'.");
                }
            }

            if (truth.MeshRules?.RejectUnexpectedBlades == true)
            {
                foreach (string issue in ValidateUnexpectedBlades(rails, truth.Blades, blades))
                {
                    yield return Format(truth, issue);
                }
            }

            if (truth.MeshRules?.RejectFixedRailsUnderBlades == true)
            {
                foreach (string issue in ValidateNoFixedRailUnderBlades(fixedRails, blades))
                {
                    yield return Format(truth, issue);
                }
            }

            if (truth.MeshRules?.RejectDuplicateSharedRailRendering == true)
            {
                foreach (string issue in ValidateSharedRailsRenderOnce(
                    definition,
                    rails,
                    shared,
                    fixedRails,
                    cuts,
                    frogs,
                    blades))
                {
                    yield return Format(truth, issue);
                }
            }
        }

        private static IEnumerable<string> ValidateRoutes(
            SpecialWorkDefinition definition,
            TurnoutTruthTable truth)
        {
            foreach (TruthRoute routeRule in truth.Routes)
            {
                LogicalRoute? route = definition.Routes.FirstOrDefault(item =>
                    string.Equals(item.Id, routeRule.Id, StringComparison.OrdinalIgnoreCase));
                if (route == null)
                {
                    yield return $"required route '{routeRule.Id}' was not generated.";
                    continue;
                }

                if (TryParseEnum(routeRule.Family, out GaugeGraphFamily family)
                    && route.Family != family)
                {
                    yield return
                        $"route '{route.Id}' has family '{route.Family}', expected '{family}'.";
                }

                if (!string.IsNullOrWhiteSpace(routeRule.State)
                    && !string.Equals(routeRule.State, "fixed", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(route.RequiredStateId, routeRule.State, StringComparison.OrdinalIgnoreCase))
                {
                    yield return
                        $"route '{route.Id}' has state '{route.RequiredStateId ?? "<fixed>"}', " +
                        $"expected '{routeRule.State}'.";
                }
            }
        }

        private static IEnumerable<string> ValidateRailRule(
            SpecialWorkDefinition definition,
            TruthRail rule,
            RailCenterline rail,
            IReadOnlyList<RailPiece> fixedRails,
            IReadOnlyList<RailCut> cuts,
            IReadOnlyList<FrogCandidate> frogs,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            if (TryParseEnum(rule.Family, out GaugeGraphFamily family)
                && rail.Family != family)
            {
                yield return
                    $"rail '{rule.Label}' maps to '{rail.Id}' with family '{rail.Family}', " +
                    $"expected '{family}'.";
            }

            if (TryParseEnum(rule.Side, out RailSide side)
                && rail.Side != side)
            {
                yield return
                    $"rail '{rule.Label}' maps to '{rail.Id}' with side '{rail.Side}', expected '{side}'.";
            }

            if (!rail.SourceRouteIds.Contains(rule.RouteId, StringComparer.OrdinalIgnoreCase))
            {
                yield return
                    $"rail '{rule.Label}' maps to '{rail.Id}' but is not sourced from route '{rule.RouteId}'.";
            }

            LogicalRoute? route = definition.Routes.FirstOrDefault(item =>
                string.Equals(item.Id, rule.RouteId, StringComparison.OrdinalIgnoreCase));
            if (route != null && !SourceCurveMatches(route, rule.SourceCurve))
            {
                yield return
                    $"rail '{rule.Label}' is sourced from '{SourceCurveName(route)}', " +
                    $"expected '{rule.SourceCurve}'.";
            }

            RailRole[] resolvedRoles = rail.Sections
                .Where(section =>
                    section.Role != RailRole.SuppressedRail
                    && section.Role != RailRole.Unknown)
                .Select(section => section.Role)
                .Concat(blades.Any(blade => blade.MovableRail == rail)
                    ? new[] { RailRole.MovablePointBlade }
                    : Array.Empty<RailRole>())
                .Concat(blades.Any(blade => blade.StockRail == rail)
                    ? new[] { RailRole.StockRail }
                    : Array.Empty<RailRole>())
                .Distinct()
                .DefaultIfEmpty(rail.Role)
                .ToArray();
            if (rule.AllowedRoles.Length > 0
                && resolvedRoles.Any(role =>
                    !rule.AllowedRoles.Any(allowed => RoleMatches(role, allowed))))
            {
                yield return
                    $"rail '{rule.Label}' maps to '{rail.Id}' with resolved roles " +
                    $"'{string.Join(",", resolvedRoles)}', " +
                    $"expected one of '{string.Join(",", rule.AllowedRoles)}'.";
            }

            foreach (string pieceKindText in rule.RequiredPieceKinds)
            {
                if (!TryParseEnum(pieceKindText, out RailPieceKind pieceKind))
                {
                    yield return
                        $"rail '{rule.Label}' asks for unknown piece kind '{pieceKindText}'.";
                    continue;
                }

                if (!fixedRails.Any(piece =>
                    piece.SourceRailId == rail.Id
                    && piece.Kind == pieceKind))
                {
                    yield return
                        $"rail '{rule.Label}' has no rendered '{pieceKind}' piece.";
                }
            }

            if (rule.MustHaveBlade
                && !blades.Any(blade => blade.MovableRail == rail))
            {
                yield return $"rail '{rule.Label}' is not bound to a movable point blade.";
            }

            if (rule.MustEnterFrog
                && !frogs.Any(frog =>
                    frog.Intersection.RailA == rail
                    || frog.Intersection.RailB == rail))
            {
                yield return $"rail '{rule.Label}' does not enter any frog candidate.";
            }

            if (rule.ContinuousFromBladeRootToFrog)
            {
                foreach (string issue in ValidateClosureContinuity(rule, rail, fixedRails, cuts, blades))
                {
                    yield return issue;
                }
            }
        }

        private static IEnumerable<string> ValidateClosureContinuity(
            TruthRail rule,
            RailCenterline rail,
            IReadOnlyList<RailPiece> fixedRails,
            IReadOnlyList<RailCut> cuts,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            SwitchBladePlan? blade = blades.FirstOrDefault(item => item.MovableRail == rail);
            if (blade == null)
            {
                yield return
                    $"closure rail '{rule.Label}' has no movable blade root to start from.";
                yield break;
            }

            RailCut? frogCut = cuts
                .Where(cut =>
                    cut.Rail == rail
                    && cut.Kind == RailCutKind.FrogGap
                    && cut.StartDistance > blade.RootDistance + ContinuityTolerance)
                .OrderBy(cut => cut.StartDistance)
                .FirstOrDefault();
            if (frogCut == null)
            {
                yield return
                    $"closure rail '{rule.Label}' has no frog cut after blade root.";
                yield break;
            }

            RailPiece[] closurePieces = fixedRails
                .Where(piece =>
                    piece.SourceRailId == rail.Id
                    && (piece.Kind == RailPieceKind.ClosureRail
                        || piece.Kind == RailPieceKind.FixedRunning
                        || piece.Kind == RailPieceKind.SharedRunning))
                .Where(piece =>
                    piece.EndDistance >= blade.RootDistance - ContinuityTolerance
                    && piece.StartDistance <= frogCut.StartDistance + ContinuityTolerance)
                .OrderBy(piece => piece.StartDistance)
                .ToArray();
            float coveredTo = blade.RootDistance;
            foreach (RailPiece piece in closurePieces)
            {
                if (piece.StartDistance > coveredTo + ContinuityTolerance)
                {
                    break;
                }

                coveredTo = Mathf.Max(coveredTo, piece.EndDistance);
                if (coveredTo >= frogCut.StartDistance - ContinuityTolerance)
                {
                    break;
                }
            }

            if (coveredTo < frogCut.StartDistance - ContinuityTolerance)
            {
                yield return
                    $"closure rail '{rule.Label}' is not continuous from blade root " +
                    $"{blade.RootDistance:0.000} to frog cut {frogCut.StartDistance:0.000}; " +
                    $"coverage ends at {coveredTo:0.000}.";
            }
        }

        private static IEnumerable<string> ValidateNoFixedRailUnderBlades(
            IReadOnlyList<RailPiece> fixedRails,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            foreach (SwitchBladePlan blade in blades)
            {
                foreach (RailPiece piece in fixedRails)
                {
                    if (piece.SourceRailId == blade.MovableRail.Id)
                    {
                        continue;
                    }

                    float stockOverlap = EstimateCurveOverlapLength(
                        piece.Curve,
                        blade.StockRail.Curve,
                        BladeOverlapTolerance);
                    if (stockOverlap >= 0.2f)
                    {
                        continue;
                    }

                    float overlap = EstimateCurveOverlapLength(piece.Curve, blade.BladeCurve, BladeOverlapTolerance);
                    if (overlap >= 0.2f)
                    {
                        yield return
                            $"fixed rail piece '{piece.Id}' ({piece.Kind} {piece.SourceRailId}) " +
                            $"runs under blade '{blade.Id}' for {overlap:0.000}m.";
                    }
                }
            }
        }

        private static IEnumerable<string> ValidateUnexpectedBlades(
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<TruthBlade> allowedRules,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TruthBlade rule in allowedRules)
            {
                if (TryResolveBladeRule(rails, rule, out RailCenterline? movable, out RailCenterline? stock))
                {
                    allowed.Add(BladeKey(stock!, movable!));
                }
            }

            foreach (SwitchBladePlan blade in blades)
            {
                if (!allowed.Contains(BladeKey(blade.StockRail, blade.MovableRail)))
                {
                    yield return
                        $"unexpected blade '{blade.Id}' was generated as stock '{blade.StockRail.Id}' " +
                        $"and movable '{blade.MovableRail.Id}'.";
                }
            }
        }

        private static string BladeKey(RailCenterline stock, RailCenterline movable)
        {
            return stock.Id + "=>" + movable.Id;
        }

        private static IEnumerable<string> ValidateSharedRailsRenderOnce(
            SpecialWorkDefinition definition,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<SharedRailInterval> shared,
            IReadOnlyList<RailPiece> fixedRails,
            IReadOnlyList<RailCut> cuts,
            IReadOnlyList<FrogCandidate> frogs,
            IReadOnlyList<SwitchBladePlan> blades)
        {
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

                bool railACut = HasSharedDuplicateCut(cuts, railA, interval);
                bool railBCut = HasSharedDuplicateCut(cuts, railB, interval);
                bool railARenders = RendersSharedInterval(fixedRails, railA, interval);
                bool railBRenders = RendersSharedInterval(fixedRails, railB, interval);

                if (railARenders && railBRenders)
                {
                    if (!railACut
                        && !railBCut
                        && corridorOwners != null
                        && corridorOwners.TryGetValue(railA.Id, out RailCenterline? owner)
                        && (owner == railA || owner == railB))
                    {
                        RailCenterline loser = owner == railA ? railB : railA;
                        if (RailParticipatesInAcceptedFrog(loser, frogs))
                        {
                            continue;
                        }
                    }

                    yield return
                        $"shared rail interval '{railA.Id}'/'{railB.Id}' is rendered by both logical rails.";
                    continue;
                }

                if (railACut && railARenders)
                {
                    yield return
                        $"shared duplicate '{railA.Id}' still renders in interval shared with '{railB.Id}'.";
                }

                if (railBCut && railBRenders)
                {
                    yield return
                        $"shared duplicate '{railB.Id}' still renders in interval shared with '{railA.Id}'.";
                }
            }
        }

        private static bool IsDualBothDivergePreset(SpecialWorkDefinition definition)
        {
            return string.Equals(
                definition.Preset.Id,
                SpecialWorkPresetIds.DualBothDiverge,
                StringComparison.OrdinalIgnoreCase);
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

        private static bool RailParticipatesInAcceptedFrog(
            RailCenterline rail,
            IEnumerable<FrogCandidate> frogs)
        {
            return frogs.Any(frog =>
                frog.Intersection.RailA == rail
                || frog.Intersection.RailB == rail);
        }

        private static bool HasSharedDuplicateCut(
            IReadOnlyList<RailCut> cuts,
            RailCenterline rail,
            SharedRailInterval interval)
        {
            float start = rail.Curve.DistanceTo(interval.Start);
            float end = rail.Curve.DistanceTo(interval.End);
            float minimum = Mathf.Min(start, end);
            float maximum = Mathf.Max(start, end);
            return cuts.Any(cut =>
                cut.Rail == rail
                && cut.Kind == RailCutKind.SharedDuplicate
                && cut.StartDistance <= minimum + ContinuityTolerance
                && cut.EndDistance >= maximum - ContinuityTolerance);
        }

        private static bool RendersSharedInterval(
            IReadOnlyList<RailPiece> fixedRails,
            RailCenterline rail,
            SharedRailInterval interval)
        {
            return fixedRails.Any(piece =>
                piece.SourceRailId == rail.Id
                && DistancePointToSegment(
                    piece.Curve.LinePointAtDistance(piece.Curve.Length * 0.5f).point,
                    interval.Start,
                    interval.End) <= SharedOverlapTolerance);
        }

        private static bool TryResolveBladeRule(
            IReadOnlyList<RailCenterline> rails,
            TruthBlade rule,
            out RailCenterline? movable,
            out RailCenterline? stock)
        {
            movable = FindRail(rails, rule.MovableRouteId, rule.MovableSide);
            stock = FindRail(rails, rule.StockRouteId, rule.StockSide);
            return movable != null && stock != null;
        }

        private static RailCenterline? FindRail(
            IReadOnlyList<RailCenterline> rails,
            TruthRail rule)
        {
            return FindRail(rails, rule.RouteId, rule.Side);
        }

        private static RailCenterline? FindRail(
            IReadOnlyList<RailCenterline> rails,
            string routeId,
            string sideText)
        {
            if (!TryParseEnum(sideText, out RailSide side))
            {
                return null;
            }

            return rails.FirstOrDefault(rail =>
                rail.Side == side
                && rail.SourceRouteIds.Contains(routeId, StringComparer.OrdinalIgnoreCase));
        }

        private static bool HasFrogCut(
            IReadOnlyList<RailCut> cuts,
            RailCenterline rail,
            float center)
        {
            return cuts.Any(cut =>
                cut.Rail == rail
                && cut.Kind == RailCutKind.FrogGap
                && cut.StartDistance <= center + ContinuityTolerance
                && cut.EndDistance >= center - ContinuityTolerance);
        }

        private static bool RoleMatches(RailRole actual, string expected)
        {
            return TryParseEnum(expected, out RailRole role) && actual == role;
        }

        private static bool SourceCurveMatches(LogicalRoute route, string expected)
        {
            return string.IsNullOrWhiteSpace(expected)
                || string.Equals(SourceCurveName(route), expected, StringComparison.OrdinalIgnoreCase);
        }

        private static string SourceCurveName(LogicalRoute route)
        {
            if (string.Equals(route.RequiredStateId, "reversed", StringComparison.OrdinalIgnoreCase))
            {
                return "DivergingRoute";
            }

            if (string.Equals(route.RequiredStateId, "normal", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(route.RequiredStateId)
                || route.Id.IndexOf("through", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "ThroughRoute";
            }

            return "RouteCenterline";
        }

        private static float EstimateCurveOverlapLength(
            LineCurve target,
            LineCurve candidate,
            float tolerance)
        {
            float overlap = 0f;
            int count = Mathf.Max(
                2,
                Mathf.CeilToInt(candidate.Length / BladeOverlapSampleSpacing) + 1);
            for (int index = 0; index < count - 1; index++)
            {
                float distance = Mathf.Min(candidate.Length, index * BladeOverlapSampleSpacing);
                float nextDistance = index == count - 2
                    ? candidate.Length
                    : Mathf.Min(candidate.Length, (index + 1) * BladeOverlapSampleSpacing);
                Vector3 point = candidate.LinePointAtDistance(distance).point;
                Vector3 nextPoint = candidate.LinePointAtDistance(nextDistance).point;
                if (DistancePointToCurve(point, target) <= tolerance
                    && DistancePointToCurve(nextPoint, target) <= tolerance)
                {
                    overlap += nextDistance - distance;
                }
            }

            return overlap;
        }

        private static float DistancePointToCurve(Vector3 point, LineCurve curve)
        {
            float best = float.MaxValue;
            foreach ((int _, LineSegment segment) in curve.Segments)
            {
                best = Mathf.Min(
                    best,
                    DistancePointToSegment(point, segment.a.point, segment.b.point));
            }

            return best;
        }

        private static float DistancePointToSegment(
            Vector3 point,
            Vector3 start,
            Vector3 end)
        {
            Vector3 delta = end - start;
            if (delta.sqrMagnitude <= 0.000001f)
            {
                return Vector3.Distance(point, start);
            }

            float t = Mathf.Clamp01(Vector3.Dot(point - start, delta) / delta.sqrMagnitude);
            return Vector3.Distance(point, start + delta * t);
        }

        private static bool TryParseEnum<T>(string text, out T value)
            where T : struct
        {
            return Enum.TryParse(text ?? string.Empty, ignoreCase: true, out value);
        }

        private static string Format(TurnoutTruthTable truth, string issue)
        {
            return $"TruthTable[{truth.Id}] {issue}";
        }
    }

    internal static class SpecialWorkTruthTableCatalog
    {
        private static readonly Lazy<IReadOnlyList<TurnoutTruthTable>> Tables =
            new Lazy<IReadOnlyList<TurnoutTruthTable>>(Load);

        public static bool TryGet(string presetId, out TurnoutTruthTable table)
        {
            table = Tables.Value.FirstOrDefault(item =>
                string.Equals(item.PresetId, presetId, StringComparison.OrdinalIgnoreCase))!;
            return table != null;
        }

        public static bool TryGet(
            string presetId,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<FrogCandidate> frogs,
            out TurnoutTruthTable table)
        {
            TurnoutTruthTable[] candidates = Tables.Value
                .Where(item =>
                    string.Equals(item.PresetId, presetId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            table = candidates.FirstOrDefault(item => MatchesSelector(item, rails, frogs))
                ?? candidates.FirstOrDefault(item => item.SelectorFrogPair == null)!;
            return table != null;
        }

        public static bool TryGet(
            string presetId,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<RailIntersection> intersections,
            out TurnoutTruthTable table)
        {
            TurnoutTruthTable[] candidates = Tables.Value
                .Where(item =>
                    string.Equals(item.PresetId, presetId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            table = candidates.FirstOrDefault(item => MatchesSelector(item, rails, intersections))
                ?? candidates.FirstOrDefault(item => item.SelectorFrogPair == null)!;
            return table != null;
        }

        private static bool MatchesSelector(
            TurnoutTruthTable table,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<FrogCandidate> frogs)
        {
            TruthFrogPairSelector? selector = table.SelectorFrogPair;
            if (selector == null)
            {
                return true;
            }

            RailCenterline? railA = FindRail(rails, selector.RailA);
            RailCenterline? railB = FindRail(rails, selector.RailB);
            return railA != null
                && railB != null
                && frogs.Any(frog =>
                    (frog.Intersection.RailA == railA && frog.Intersection.RailB == railB)
                    || (frog.Intersection.RailA == railB && frog.Intersection.RailB == railA));
        }

        private static bool MatchesSelector(
            TurnoutTruthTable table,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<RailIntersection> intersections)
        {
            TruthFrogPairSelector? selector = table.SelectorFrogPair;
            if (selector == null)
            {
                return true;
            }

            RailCenterline? railA = FindRail(rails, selector.RailA);
            RailCenterline? railB = FindRail(rails, selector.RailB);
            return railA != null
                && railB != null
                && intersections.Any(intersection =>
                    (intersection.RailA == railA && intersection.RailB == railB)
                    || (intersection.RailA == railB && intersection.RailB == railA));
        }

        private static RailCenterline? FindRail(
            IReadOnlyList<RailCenterline> rails,
            TruthRailReference reference)
        {
            if (!Enum.TryParse(reference.Side, ignoreCase: true, out RailSide side))
            {
                return null;
            }

            return rails.FirstOrDefault(rail =>
                rail.Side == side
                && rail.SourceRouteIds.Contains(
                    reference.RouteId,
                    StringComparer.OrdinalIgnoreCase));
        }

        private static IReadOnlyList<TurnoutTruthTable> Load()
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                string? resourceName = assembly
                    .GetManifestResourceNames()
                    .FirstOrDefault(name =>
                        name.EndsWith(
                            "SpecialWorkTruthTables.json",
                            StringComparison.OrdinalIgnoreCase));
                if (resourceName == null)
                {
                    return Array.Empty<TurnoutTruthTable>();
                }

                using Stream stream = assembly.GetManifestResourceStream(resourceName);
                using var reader = new StreamReader(stream);
                string json = reader.ReadToEnd();
                TruthCatalog catalog =
                    JsonConvert.DeserializeObject<TruthCatalog>(json) ?? new TruthCatalog();
                return catalog.Turnouts ?? Array.Empty<TurnoutTruthTable>();
            }
            catch (Exception ex)
            {
                Main.Warn($"[SpecialWorkTruth] Could not load truth tables: {ex.Message}");
                return Array.Empty<TurnoutTruthTable>();
            }
        }
    }

    internal sealed class TruthCatalog
    {
        [JsonProperty("version")]
        public int Version { get; set; }

        [JsonProperty("turnouts")]
        public TurnoutTruthTable[] Turnouts { get; set; } = Array.Empty<TurnoutTruthTable>();
    }

    internal sealed class TurnoutTruthTable
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("presetId")]
        public string PresetId { get; set; } = string.Empty;

        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;

        [JsonProperty("criticalRules")]
        public string[] CriticalRules { get; set; } = Array.Empty<string>();

        [JsonProperty("selectorFrogPair")]
        public TruthFrogPairSelector? SelectorFrogPair { get; set; }

        [JsonProperty("routes")]
        public TruthRoute[] Routes { get; set; } = Array.Empty<TruthRoute>();

        [JsonProperty("rails")]
        public TruthRail[] Rails { get; set; } = Array.Empty<TruthRail>();

        [JsonProperty("suppressedIntervals")]
        public TruthSuppressedInterval[] SuppressedIntervals { get; set; } =
            Array.Empty<TruthSuppressedInterval>();

        [JsonProperty("frogs")]
        public TruthFrogRules? Frogs { get; set; }

        [JsonProperty("guards")]
        public TruthCountRule? Guards { get; set; }

        [JsonProperty("wingRails")]
        public TruthCountRule? WingRails { get; set; }

        [JsonProperty("blades")]
        public TruthBlade[] Blades { get; set; } = Array.Empty<TruthBlade>();

        [JsonProperty("meshRules")]
        public TruthMeshRules? MeshRules { get; set; }
    }

    internal sealed class TruthRoute
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("family")]
        public string Family { get; set; } = string.Empty;

        [JsonProperty("kind")]
        public string Kind { get; set; } = string.Empty;

        [JsonProperty("state")]
        public string State { get; set; } = string.Empty;
    }

    internal sealed class TruthFrogPairSelector
    {
        [JsonProperty("railA")]
        public TruthRailReference RailA { get; set; } = new TruthRailReference();

        [JsonProperty("railB")]
        public TruthRailReference RailB { get; set; } = new TruthRailReference();
    }

    internal sealed class TruthRailReference
    {
        [JsonProperty("routeId")]
        public string RouteId { get; set; } = string.Empty;

        [JsonProperty("side")]
        public string Side { get; set; } = string.Empty;
    }

    internal sealed class TruthRail
    {
        [JsonProperty("label")]
        public string Label { get; set; } = string.Empty;

        [JsonProperty("routeId")]
        public string RouteId { get; set; } = string.Empty;

        [JsonProperty("family")]
        public string Family { get; set; } = string.Empty;

        [JsonProperty("side")]
        public string Side { get; set; } = string.Empty;

        [JsonProperty("sourceCurve")]
        public string SourceCurve { get; set; } = string.Empty;

        [JsonProperty("allowedRoles")]
        public string[] AllowedRoles { get; set; } = Array.Empty<string>();

        [JsonProperty("requiredPieceKinds")]
        public string[] RequiredPieceKinds { get; set; } = Array.Empty<string>();

        [JsonProperty("renderedOnce")]
        public bool RenderedOnce { get; set; }

        [JsonProperty("mustHaveBlade")]
        public bool MustHaveBlade { get; set; }

        [JsonProperty("continuousFromBladeRootToFrog")]
        public bool ContinuousFromBladeRootToFrog { get; set; }

        [JsonProperty("mustEnterFrog")]
        public bool MustEnterFrog { get; set; }
    }

    internal sealed class TruthSuppressedInterval
    {
        [JsonProperty("label")]
        public string Label { get; set; } = string.Empty;

        [JsonProperty("routeId")]
        public string RouteId { get; set; } = string.Empty;

        [JsonProperty("side")]
        public string Side { get; set; } = string.Empty;

        [JsonProperty("kind")]
        public string Kind { get; set; } = string.Empty;
    }

    internal sealed class TruthFrogRules
    {
        [JsonProperty("minimumCount")]
        public int MinimumCount { get; set; }

        [JsonProperty("requireCuts")]
        public bool RequireCuts { get; set; }

        [JsonProperty("requireWingRails")]
        public bool RequireWingRails { get; set; }

        [JsonProperty("requireGuardRails")]
        public bool RequireGuardRails { get; set; }
    }

    internal sealed class TruthCountRule
    {
        [JsonProperty("minimumCount")]
        public int MinimumCount { get; set; }
    }

    internal sealed class TruthBlade
    {
        [JsonProperty("label")]
        public string Label { get; set; } = string.Empty;

        [JsonProperty("movableRouteId")]
        public string MovableRouteId { get; set; } = string.Empty;

        [JsonProperty("movableSide")]
        public string MovableSide { get; set; } = string.Empty;

        [JsonProperty("stockRouteId")]
        public string StockRouteId { get; set; } = string.Empty;

        [JsonProperty("stockSide")]
        public string StockSide { get; set; } = string.Empty;
    }

    internal sealed class TruthMeshRules
    {
        [JsonProperty("rejectUnexpectedBlades")]
        public bool RejectUnexpectedBlades { get; set; }

        [JsonProperty("rejectFixedRailsUnderBlades")]
        public bool RejectFixedRailsUnderBlades { get; set; }

        [JsonProperty("rejectDuplicateSharedRailRendering")]
        public bool RejectDuplicateSharedRailRendering { get; set; }
    }
}
