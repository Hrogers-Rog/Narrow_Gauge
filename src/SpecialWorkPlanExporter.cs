using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace NarrowGaugeMod
{
    internal static class SpecialWorkPlanExporter
    {
        private const float SvgScale = 80f;
        private const float SvgMargin = 60f;

        public static string ExportAll()
        {
            string directory = Path.Combine(
                Application.persistentDataPath,
                "NarrowGauge",
                "SpecialWorkPlans");
            Directory.CreateDirectory(directory);

            foreach (SpecialWorkAnalysis analysis in SpecialWorkRuntimeRegistry.Analyses)
            {
                ExportSvg(directory, analysis);
                ExportText(directory, analysis);
            }

            return directory;
        }

        private static void ExportSvg(string directory, SpecialWorkAnalysis analysis)
        {
            SpecialWorkMeshPlan? plan = analysis.MeshPlan;
            if (plan == null)
            {
                return;
            }

            Vector2[] allPoints = plan.WorkIntervals
                .SelectMany(work => SampleCurve(
                    work.Rail.Curve
                        .Skip(work.StartDistance, true)
                        .Take(work.EndDistance - work.StartDistance)))
                .Select(analysis.ProjectionFrame.Project)
                .ToArray();
            if (allPoints.Length == 0)
            {
                return;
            }

            float minX = allPoints.Min(point => point.x);
            float maxX = allPoints.Max(point => point.x);
            float minY = allPoints.Min(point => point.y);
            float maxY = allPoints.Max(point => point.y);
            float width = Mathf.Max((maxX - minX) * SvgScale + SvgMargin * 2f, 400f);
            float height = Mathf.Max((maxY - minY) * SvgScale + SvgMargin * 2f, 400f);

            var svg = new StringBuilder();
            svg.AppendLine(
                $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(width)}\" height=\"{F(height)}\" viewBox=\"0 0 {F(width)} {F(height)}\">");
            svg.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#111318\"/>");
            svg.AppendLine(
                $"<text x=\"12\" y=\"22\" fill=\"white\" font-family=\"monospace\" font-size=\"14\">{Escape(analysis.Definition.Id)} | {Escape(analysis.Definition.Preset.DisplayName)} | valid={plan.IsValid}</text>");

            foreach (LogicalRoute route in analysis.Definition.Routes)
            {
                DrawCurve(
                    svg,
                    analysis,
                    route.Centerline,
                    route.Family == GaugeGraphFamily.Standard ? "#2874ff" : "#18e6ef",
                    0.8f,
                    "2,4");
            }

            foreach (WheelPath path in analysis.WheelPaths)
            {
                string color = path.Family == GaugeGraphFamily.Standard ? "#78a6ff" : "#8fffff";
                DrawCurve(svg, analysis, path.LeftFlangeGuide, color, 0.7f, "1,5");
                DrawCurve(svg, analysis, path.RightFlangeGuide, color, 0.7f, "1,5");
            }

            RailOwnershipPlan? ownershipPlan = plan.RailOwnershipPlan;
            if (ownershipPlan != null)
            {
                foreach (RailRoleSection section in ownershipPlan.Sections)
                {
                    DrawCurve(
                        svg,
                        analysis,
                        section.Rail.Curve
                            .Skip(section.StartDistance, true)
                            .Take(section.EndDistance - section.StartDistance),
                        RailRoleColor(section.Role),
                        section.Role == RailRole.SuppressedRail ? 2f : 1.8f,
                        section.Role == RailRole.SuppressedRail ? "3,3" : "");
                    DrawText(
                        svg,
                        analysis.ProjectionFrame.Project(
                            section.Rail.Curve
                                .LinePointAtDistance((section.StartDistance + section.EndDistance) * 0.5f)
                                .point),
                        $"{section.Role} {section.Rail.Id}",
                        RailRoleColor(section.Role),
                        8f);
                }
            }
            else
            {
                foreach (RailWorkInterval work in plan.WorkIntervals)
                {
                    RailCenterline rail = work.Rail;
                    string color = RailRoleColor(rail.Role);
                    DrawCurve(
                        svg,
                        analysis,
                        rail.Curve
                            .Skip(work.StartDistance, true)
                            .Take(work.EndDistance - work.StartDistance),
                        color,
                        1.2f,
                        "");
                }
            }

            foreach (SharedRailInterval shared in analysis.SharedRailIntervals.Where(shared =>
                plan.WorkIntervals.Any(work =>
                    (work.Rail.Id == shared.RailAId || work.Rail.Id == shared.RailBId)
                    && DistanceWithin(
                        work,
                        work.Rail.Curve.DistanceTo(shared.Start),
                        work.Rail.Curve.DistanceTo(shared.End)))))
            {
                DrawLine(svg, shared.LocalStart, shared.LocalEnd, "#31df52", 3f, "");
            }

            foreach (RailCut cut in plan.Cuts)
            {
                DrawCurve(
                    svg,
                    analysis,
                    cut.Rail.Curve
                        .Skip(cut.StartDistance, true)
                        .Take(cut.EndDistance - cut.StartDistance),
                    "#ffe03a",
                    4f,
                    "4,3");
            }

            foreach (RailIntersection intersection in analysis.Intersections)
            {
                string color = IntersectionColor(intersection.Kind);
                DrawCircle(svg, intersection.LocalPoint, 4f, color);
                DrawText(
                    svg,
                    intersection.LocalPoint + new Vector2(0.08f, 0.08f),
                    $"{intersection.RailA.Id} x {intersection.RailB.Id} {intersection.AcuteAngleDegrees:0.0} {intersection.Kind}",
                    color,
                    9f);
            }

            foreach (RailPiece frogPiece in plan.FrogPieces)
            {
                DrawCurve(svg, analysis, frogPiece.Curve, "#ff3030", 3f, "");
            }

            foreach (RailPiece piece in plan.FixedRunningRails)
            {
                string color = PieceKindColor(piece.Kind);
                DrawCurve(svg, analysis, piece.Curve, color, 2.5f, "");
                DrawText(
                    svg,
                    analysis.ProjectionFrame.Project(
                        piece.Curve.LinePointAtDistance(piece.Curve.Length * 0.5f).point),
                    $"{piece.Kind} {piece.SourceRailId}",
                    color,
                    8f);
            }

            foreach (WingRailPlan wing in plan.WingRails)
            {
                DrawCurve(svg, analysis, wing.Curve, "#ff8a24", 2.5f, "");
            }

            foreach (GuardRailPlan guard in plan.GuardRails)
            {
                DrawCurve(svg, analysis, guard.Curve, "#38dc65", 2.5f, "");
            }

            foreach (SwitchBladePlan blade in plan.SwitchBlades)
            {
                DrawCurve(svg, analysis, blade.BladeCurve, "#b532ff", 3f, "");
            }

            DrawLegend(svg);
            svg.AppendLine("</svg>");

            File.WriteAllText(
                Path.Combine(directory, SafeFileName(analysis.Definition.Id) + ".svg"),
                svg.ToString());

            Vector2 Transform(Vector2 point)
            {
                return new Vector2(
                    SvgMargin + (point.x - minX) * SvgScale,
                    height - SvgMargin - (point.y - minY) * SvgScale);
            }

            void DrawPolyline(
                StringBuilder output,
                IEnumerable<Vector2> points,
                string color,
                float strokeWidth,
                string dash)
            {
                string value = string.Join(
                    " ",
                    points.Select(point =>
                    {
                        Vector2 transformed = Transform(point);
                        return $"{F(transformed.x)},{F(transformed.y)}";
                    }));
                output.AppendLine(
                    $"<polyline points=\"{value}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"{F(strokeWidth)}\"{Dash(dash)}/>");
            }

            void DrawLine(
                StringBuilder output,
                Vector2 start,
                Vector2 end,
                string color,
                float strokeWidth,
                string dash)
            {
                Vector2 a = Transform(start);
                Vector2 b = Transform(end);
                output.AppendLine(
                    $"<line x1=\"{F(a.x)}\" y1=\"{F(a.y)}\" x2=\"{F(b.x)}\" y2=\"{F(b.y)}\" stroke=\"{color}\" stroke-width=\"{F(strokeWidth)}\"{Dash(dash)}/>");
            }

            void DrawCircle(StringBuilder output, Vector2 point, float radius, string color)
            {
                Vector2 transformed = Transform(point);
                output.AppendLine(
                    $"<circle cx=\"{F(transformed.x)}\" cy=\"{F(transformed.y)}\" r=\"{F(radius)}\" fill=\"{color}\"/>");
            }

            void DrawText(
                StringBuilder output,
                Vector2 point,
                string text,
                string color,
                float fontSize)
            {
                Vector2 transformed = Transform(point);
                output.AppendLine(
                    $"<text x=\"{F(transformed.x)}\" y=\"{F(transformed.y)}\" fill=\"{color}\" font-family=\"monospace\" font-size=\"{F(fontSize)}\">{Escape(text)}</text>");
            }

            void DrawCurve(
                StringBuilder output,
                SpecialWorkAnalysis source,
                Core.LineCurve curve,
                string color,
                float strokeWidth,
                string dash)
            {
                DrawPolyline(
                    output,
                    SampleCurve(curve).Select(source.ProjectionFrame.Project),
                    color,
                    strokeWidth,
                    dash);
            }
        }

        private static void ExportText(string directory, SpecialWorkAnalysis analysis)
        {
            SpecialWorkMeshPlan? plan = analysis.MeshPlan;
            if (plan == null)
            {
                return;
            }

            var text = new StringBuilder();
            text.AppendLine($"Special work: {analysis.Definition.Id}");
            text.AppendLine($"Preset: {analysis.Definition.Preset.Id}");
            text.AppendLine($"Plan valid: {plan.IsValid}");
            text.AppendLine(
                $"First failure: {(plan.ValidationIssues.FirstOrDefault() ?? "<none>")}");
            text.AppendLine(
                $"Parameters: railHead={plan.Parameters.RailHeadWidth:0.000} " +
                $"flangeway={plan.Parameters.FlangewayWidth:0.000} " +
                $"frogSetback={plan.Parameters.MinimumFrogSetback:0.000}-{plan.Parameters.MaximumFrogSetback:0.000} " +
                $"guardOffset={plan.Parameters.GuardCenterOffset:0.000} " +
                $"guardLeadTrail={plan.Parameters.GuardLeadLength:0.000}/{plan.Parameters.GuardTrailLength:0.000} " +
                $"bladeDivergence={plan.Parameters.BladeDivergenceThreshold:0.000} " +
                $"bladeRootSeparation={plan.Parameters.BladeRootSeparation:0.000} " +
                $"maxBlade={plan.Parameters.MaximumBladeLength:0.000}");
            text.AppendLine(
                $"WheelPaths={analysis.WheelPaths.Count} rails={analysis.Rails.Count} shared={analysis.SharedRailIntervals.Count} intersections={analysis.Intersections.Count} cuts={plan.Cuts.Count} frogs={plan.Frogs.Count} wings={plan.WingRails.Count} guards={plan.GuardRails.Count} blades={plan.SwitchBlades.Count}");
            text.AppendLine();

            AppendSection(text, "Validation", plan.ValidationIssues);
            AppendSection(
                text,
                "TruthTable",
                TruthTableSummary(analysis, plan));
            AppendSection(
                text,
                "WheelPaths",
                analysis.WheelPaths.Select(item =>
                    $"{item.Id} route={item.RouteId} family={item.Family} state={item.RequiredStateId ?? "<fixed>"} " +
                    $"ports={item.StartPortId}->{item.EndPortId} rails={item.LeftRailId}/{item.RightRailId} " +
                    $"leftFlangeLen={item.LeftFlangeGuide.Length:0.000} rightFlangeLen={item.RightFlangeGuide.Length:0.000}"));
            AppendSection(
                text,
                "Rails",
                analysis.Rails.Select(item =>
                    $"{item.Id} family={item.Family} side={item.Side} role={item.Role} wheelPath={item.WheelPathId} " +
                    $"ports={item.StartPortId}->{item.EndPortId} routes={string.Join(",", item.SourceRouteIds)}"));
            AppendSection(
                text,
                "WorkIntervals",
                plan.WorkIntervals.Select(item =>
                    $"{item.Rail.Id} {item.StartDistance:0.000}-{item.EndDistance:0.000} length={item.Length:0.000}"));
            AppendSection(
                text,
                "RailOwnership",
                plan.OwnershipIntervals.Select(item =>
                    $"{item.Id} rail={item.Rail.Id} owner={item.OwnerRouteId} family={item.OwnerFamily} " +
                    $"role={item.Role} kind={item.PieceKind} {item.StartDistance:0.000}-{item.EndDistance:0.000} " +
                    $"source={item.SourceId}"));
            AppendSection(
                text,
                "RailRoleSections",
                (plan.RailOwnershipPlan?.Sections ?? Enumerable.Empty<RailRoleSection>()).Select(item =>
                    $"{item.Id} rail={item.Rail.Id} role={item.Role} owner={item.OwnerRouteId} " +
                    $"family={item.OwnerFamily} sourceCurve={item.SourceCurveKind} " +
                    $"{item.StartDistance:0.000}-{item.EndDistance:0.000} reason={item.Reason}"));
            AppendSection(
                text,
                "RailSuppressions",
                (plan.RailOwnershipPlan?.Suppressions ?? Enumerable.Empty<RailSuppressionInterval>()).Select(item =>
                    $"{item.Id} rail={item.Rail.Id} {item.StartDistance:0.000}-{item.EndDistance:0.000} " +
                    $"reason={item.Reason}"));
            AppendSection(
                text,
                "SuppressionCoverage",
                (plan.RailOwnershipPlan?.Suppressions ?? Enumerable.Empty<RailSuppressionInterval>()).Select(item =>
                    $"RailId={item.Rail.Id} StartDistance={item.StartDistance:0.000} " +
                    $"EndDistance={item.EndDistance:0.000} SuppressionReason={item.Reason} " +
                    $"ExpectedReplacementType={ExpectedReplacementType(item)} " +
                    $"ActualReplacementPieceIds={ReplacementIds(plan, item)}"));
            AppendSection(
                text,
                "FixedRailPieces",
                plan.FixedRunningRails.Select(item =>
                    $"{item.Id} rail={item.SourceRailId} owner={item.SourcePlanId ?? "<none>"} " +
                    $"kind={item.Kind} {item.StartDistance:0.000}-{item.EndDistance:0.000} " +
                    $"length={item.Curve.Length:0.000}"));
            AppendSection(
                text,
                "Intersections",
                analysis.Intersections.Select(item =>
                    $"{item.Id} {item.RailA.Id} x {item.RailB.Id} " +
                    $"local=({item.LocalPoint.x:0.000},{item.LocalPoint.y:0.000}) " +
                    $"d=({item.DistanceA:0.000},{item.DistanceB:0.000}) " +
                    $"angle={item.AcuteAngleDegrees:0.000} kind={item.Kind}"));
            AppendSection(
                text,
                "Cuts",
                plan.Cuts.Select(item =>
                    $"{item.Id} rail={item.Rail.Id} {item.StartDistance:0.000}-{item.EndDistance:0.000} kind={item.Kind} source={item.SourceId}"));
            AppendSection(
                text,
                "Frogs",
                plan.Frogs.Select(item =>
                    $"{item.Id} rails={item.Intersection.RailA.Id}/{item.Intersection.RailB.Id} " +
                    $"angle={item.Intersection.AcuteAngleDegrees:0.000} hand={item.Handedness} " +
                    $"railHeadSetback={item.RailHeadSetback:0.000} flangewaySetback={item.FlangewaySetback:0.000} cutHalf={item.CutHalfLength:0.000}"));
            AppendSection(
                text,
                "Wings",
                plan.WingRails.Select(item =>
                    $"{item.Id} frog={item.FrogId} rail={item.SourceRail.Id} approach={item.ApproachSide} length={item.Curve.Length:0.000}"));
            AppendSection(
                text,
                "Guards",
                plan.GuardRails.Select(item =>
                    $"{item.Id} frog={item.FrogId} route={item.ProtectedRouteId} opposite={item.OppositeRunningRail.Id} length={item.Curve.Length:0.000}"));
            AppendSection(
                text,
                "Blades",
                plan.SwitchBlades.Select(item =>
                    $"{item.Id} group={item.SwitchGroupId} node={item.NativeNodeId} stock={item.StockRail.Id} movable={item.MovableRail.Id} tip={item.TipDistance:0.000} root={item.RootDistance:0.000}"));

            File.WriteAllText(
                Path.Combine(directory, SafeFileName(analysis.Definition.Id) + ".txt"),
                text.ToString());
        }

        private static IEnumerable<Vector3> SampleCurve(Core.LineCurve curve)
        {
            const float spacing = 0.1f;
            int count = Mathf.Max(2, Mathf.CeilToInt(curve.Length / spacing) + 1);
            for (int index = 0; index < count; index++)
            {
                float distance = index == count - 1
                    ? curve.Length
                    : Mathf.Min(curve.Length, index * spacing);
                yield return curve.LinePointAtDistance(distance).point;
            }
        }

        private static IEnumerable<string> TruthTableSummary(
            SpecialWorkAnalysis analysis,
            SpecialWorkMeshPlan plan)
        {
            string[] issues = plan.ValidationIssues
                .Where(issue => issue.StartsWith("TruthTable[", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (issues.Length > 0)
            {
                return issues;
            }

            return SpecialWorkTruthTableCatalog.TryGet(
                analysis.Definition.Preset.Id,
                analysis.Rails,
                plan.Frogs,
                out TurnoutTruthTable truth)
                    ? new[] { "passed: " + truth.Id }
                    : new[] { "no truth table matched; measured geometry fallback used" };
        }

        private static void AppendSection(
            StringBuilder text,
            string title,
            IEnumerable<string> lines)
        {
            text.AppendLine("[" + title + "]");
            foreach (string line in lines)
            {
                text.AppendLine(line);
            }

            text.AppendLine();
        }

        private static void DrawLegend(StringBuilder svg)
        {
            svg.AppendLine("<g font-family=\"monospace\" font-size=\"11\">");
            svg.AppendLine("<text x=\"12\" y=\"42\" fill=\"#2874ff\">Blue: standard route baseline</text>");
            svg.AppendLine("<text x=\"12\" y=\"57\" fill=\"#18e6ef\">Cyan: narrow route baseline</text>");
            svg.AppendLine("<text x=\"12\" y=\"72\" fill=\"#2ff06d\">Green: stock rail</text>");
            svg.AppendLine("<text x=\"12\" y=\"87\" fill=\"#ffe03a\">Yellow: point blade / cut interval</text>");
            svg.AppendLine("<text x=\"12\" y=\"102\" fill=\"#ff8a24\">Orange: closure / wing rail</text>");
            svg.AppendLine("<text x=\"12\" y=\"117\" fill=\"#ff3030\">Red: frog / crossing rail</text>");
            svg.AppendLine("<text x=\"12\" y=\"132\" fill=\"#ff00ff\">Magenta: guard rail</text>");
            svg.AppendLine("<text x=\"12\" y=\"147\" fill=\"#777777\">Gray: suppressed rail</text>");
            svg.AppendLine("</g>");
        }

        private static string IntersectionColor(RailIntersectionKind kind)
        {
            switch (kind)
            {
                case RailIntersectionKind.SharedOverlap:
                    return "#31df52";
                case RailIntersectionKind.BladeConvergence:
                    return "#b532ff";
                case RailIntersectionKind.VeeFrogCandidate:
                case RailIntersectionKind.CrossingFrogCandidate:
                    return "#ff3030";
                case RailIntersectionKind.InvalidShallowCrossing:
                    return "#ffe03a";
                case RailIntersectionKind.RouteJoin:
                    return "#aaaaaa";
                default:
                    return "#ff8a24";
            }
        }

        private static string PieceKindColor(RailPieceKind kind)
        {
            switch (kind)
            {
                case RailPieceKind.SharedRunning:
                    return "#31df52";
                case RailPieceKind.ClosureRail:
                    return "#ff8a24";
                case RailPieceKind.FrogNose:
                    return "#ff3030";
                case RailPieceKind.WingRail:
                    return "#ff59b3";
                case RailPieceKind.GuardRail:
                    return "#ff00ff";
                case RailPieceKind.MovableBlade:
                    return "#ffe03a";
                default:
                    return "#2ff06d";
            }
        }

        private static string RailRoleColor(RailRole role)
        {
            switch (role)
            {
                case RailRole.StockRail:
                    return "#2ff06d";
                case RailRole.PointBlade:
                    return "#ffe03a";
                case RailRole.ClosureRail:
                    return "#ff8a24";
                case RailRole.FrogRail:
                case RailRole.CrossingRail:
                    return "#ff3030";
                case RailRole.WingRail:
                    return "#ff59b3";
                case RailRole.GuardRail:
                    return "#ff00ff";
                case RailRole.SharedRail:
                    return "#31df52";
                case RailRole.SuppressedRail:
                    return "#777777";
                default:
                    return "#ff0000";
            }
        }

        private static string SafeFileName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }

            return value;
        }

        private static string Escape(string value)
        {
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }

        private static string Dash(string dash)
        {
            return string.IsNullOrEmpty(dash)
                ? string.Empty
                : $" stroke-dasharray=\"{dash}\"";
        }

        private static string F(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static bool DistanceWithin(
            RailWorkInterval work,
            float first,
            float second)
        {
            float start = Mathf.Min(first, second);
            float end = Mathf.Max(first, second);
            return Mathf.Min(end, work.EndDistance) - Mathf.Max(start, work.StartDistance) > 0.01f;
        }

        private static string ExpectedReplacementType(RailSuppressionInterval suppression)
        {
            string reason = suppression.Reason ?? string.Empty;
            if (reason.Contains("movable blade"))
            {
                return "MovablePointBlade";
            }

            if (reason.Contains("frog gap"))
            {
                return "FrogRail/WingRail/GuardRail";
            }

            if (reason.Contains("shared duplicate"))
            {
                return "EmptySharedDuplicate";
            }

            if (reason.Contains("fixed rail under blade"))
            {
                return "EmptyBladeClearance";
            }

            return "ReplacementPiece";
        }

        private static string ReplacementIds(
            SpecialWorkMeshPlan plan,
            RailSuppressionInterval suppression)
        {
            var ids = new List<string>();
            ids.AddRange(plan.SwitchBlades.Where(blade =>
                    blade.MovableRail == suppression.Rail
                    && IntervalsOverlap(
                        suppression.StartDistance,
                        suppression.EndDistance,
                        blade.TipDistance,
                        blade.RootDistance))
                .Select(blade => blade.Id));
            ids.AddRange(plan.FrogPieces.Where(piece =>
                    piece.SourceRailId == suppression.Rail.Id
                    && IntervalsOverlap(
                        suppression.StartDistance,
                        suppression.EndDistance,
                        piece.StartDistance,
                        piece.EndDistance))
                .Select(piece => piece.Id));
            ids.AddRange(plan.WingRails.Where(wing =>
                    wing.SourceRail == suppression.Rail
                    && wing.Curve.Length > 0.01f)
                .Select(wing => wing.Id));

            foreach (FrogCandidate frog in plan.Frogs.Where(frog =>
                string.Equals(
                    suppression.Reason,
                    "frog gap " + frog.Id,
                    System.StringComparison.OrdinalIgnoreCase)))
            {
                ids.AddRange(plan.GuardRails
                    .Where(guard => guard.FrogId == frog.Id)
                    .Select(guard => guard.Id));
            }

            return ids.Count == 0 ? "<none>" : string.Join(",", ids.Distinct());
        }

        private static bool IntervalsOverlap(float aStart, float aEnd, float bStart, float bEnd)
        {
            return aEnd > bStart + 0.01f && bEnd > aStart + 0.01f;
        }
    }
}
