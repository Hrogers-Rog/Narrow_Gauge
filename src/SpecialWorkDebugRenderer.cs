using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Helpers;
using UnityEngine;
using UnityEngine.Rendering;

namespace NarrowGaugeMod
{
    internal sealed class SpecialWorkDebugRenderer : MonoBehaviour
    {
        private static readonly Color StandardRouteColor = new Color(0.1f, 0.35f, 1f, 1f);
        private static readonly Color NarrowRouteColor = Color.cyan;
        private static readonly Color SharedRailColor = Color.green;
        private static readonly Color IntersectionColor = new Color(1f, 0.45f, 0f, 1f);
        private static readonly Color FrogColor = Color.red;
        private static readonly Color BladeColor = new Color(0.7f, 0.15f, 1f, 1f);

        private const float VerticalOffset = 0.22f;
        private Material? _lineMaterial;
        private GUIStyle? _labelStyle;
        private bool _warnedMissingShader;

        private void OnRenderObject()
        {
            NarrowGaugeSettings? settings = Main.Settings;
            if (!Main.Enabled
                || settings == null
                || !settings.ShowSpecialWorkDebug
                || SpecialWorkRuntimeRegistry.Analyses.Count == 0
                || !EnsureMaterial())
            {
                return;
            }

            _lineMaterial!.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.LINES);

            SpecialWorkAdjustmentUI? adjustUILines = Main.ManagerObject?.GetComponent<SpecialWorkAdjustmentUI>();
            string? lineNodeFilter = adjustUILines?.DebugLabelNodeFilter;
            foreach (SpecialWorkAnalysis analysis in SpecialWorkRuntimeRegistry.Analyses)
            {
                if (!string.IsNullOrEmpty(lineNodeFilter)
                    && !analysis.Definition.NativeSwitchNodeIds.Contains(
                        lineNodeFilter, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (settings.DebugRoutes)
                {
                    foreach (LogicalRoute route in analysis.Definition.Routes)
                    {
                        DrawCurve(
                            route.Centerline,
                            route.Family == GaugeGraphFamily.Standard
                                ? StandardRouteColor
                                : NarrowRouteColor);
                    }
                }

                if (settings.DebugPhysicalRails)
                {
                    foreach (RailCenterline rail in analysis.Rails)
                    {
                        DrawRailSamples(
                            rail,
                            RailRoleColor(rail.Role));
                    }
                }

                if (settings.DebugSharedRails)
                {
                    foreach (SharedRailInterval interval in analysis.SharedRailIntervals)
                    {
                        DrawLine(interval.Start, interval.End, SharedRailColor);
                    }
                }

                if (settings.DebugIntersections)
                {
                    foreach (RailIntersection intersection in analysis.Intersections)
                    {
                        DrawCross(
                            intersection.Position,
                            0.22f,
                            IntersectionKindColor(intersection.Kind));
                    }
                }

                if (settings.DebugFrogs)
                {
                    foreach (FrogCandidate frog in analysis.Frogs)
                    {
                        DrawCross(frog.Intersection.Position, 0.32f, FrogColor);
                        DrawLine(
                            frog.Intersection.Position,
                            frog.Intersection.Position + frog.NoseDirection * 0.9f,
                            FrogColor);
                    }
                }

                if (settings.DebugSwitchBlades)
                {
                    foreach (SwitchBladeCandidate blade in analysis.Blades)
                    {
                        foreach (LineCurve curve in blade.MovableCurves)
                        {
                            DrawCurve(curve, BladeColor);
                        }
                    }
                }

                if (settings.DebugMeshPlan && analysis.MeshPlan != null)
                {
                    DrawMeshPlan(analysis.MeshPlan);
                    DrawDebugLabelTargets(settings, analysis.MeshPlan);
                }
            }

            GL.End();
            GL.PopMatrix();
        }

        private void OnGUI()
        {
            NarrowGaugeSettings? settings = Main.Settings;
            Camera camera = Camera.main;
            if (!Main.Enabled
                || settings == null
                || !settings.ShowSpecialWorkDebug
                || !settings.DebugIntersections
                || !settings.DebugIntersectionLabels
                || camera == null)
            {
                return;
            }

            EnsureLabelStyle();
            SpecialWorkAdjustmentUI? adjustUI = Main.ManagerObject?.GetComponent<SpecialWorkAdjustmentUI>();
            string? nodeFilter = adjustUI?.DebugLabelNodeFilter;
            var occupied = new List<Rect>();
            foreach (SpecialWorkAnalysis analysis in SpecialWorkRuntimeRegistry.Analyses)
            {
                if (!string.IsNullOrEmpty(nodeFilter)
                    && !analysis.Definition.NativeSwitchNodeIds.Contains(
                        nodeFilter, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
                foreach (RailIntersection intersection in analysis.Intersections)
                {
                    Vector3 world = WorldTransformer.GameToWorld(
                        intersection.Position + Vector3.up * (VerticalOffset + 0.12f));
                    Vector3 screen = camera.WorldToScreenPoint(world);
                    if (screen.z <= 0f
                        || screen.x < 0f
                        || screen.x > Screen.width
                        || screen.y < 0f
                        || screen.y > Screen.height)
                    {
                        continue;
                    }

                    string label =
                        $"{intersection.RailA.Id} x {intersection.RailB.Id}\n" +
                        $"{intersection.AcuteAngleDegrees:0.0} deg  {intersection.Kind}\n" +
                        $"2D ({intersection.LocalPoint.x:0.00}, {intersection.LocalPoint.y:0.00})  " +
                        $"d {intersection.DistanceA:0.00}/{intersection.DistanceB:0.00}";
                    var rect = new Rect(
                        screen.x + 8f,
                        Screen.height - screen.y - 50f,
                        330f,
                        48f);
                    for (int attempt = 0; attempt < 12 && occupied.Exists(other => other.Overlaps(rect)); attempt++)
                    {
                        rect.y += 14f;
                    }

                    occupied.Add(rect);
                    Vector2 anchor = new Vector2(screen.x, Screen.height - screen.y);
                    DrawScreenLeaderLine(anchor, ClosestPointOnRect(rect, anchor), IntersectionKindColor(intersection.Kind));
                    GUI.Label(rect, label, _labelStyle);
                }

                if (settings.DebugMeshPlan && analysis.MeshPlan != null)
                {
                    foreach (GeometryDebugLabel debugLabel in analysis.MeshPlan.DebugLabels)
                    {
                        if (!ShouldDrawDebugLabel(settings, debugLabel.Text))
                        {
                            continue;
                        }

                        Vector3 world = WorldTransformer.GameToWorld(
                            debugLabel.Position + Vector3.up * (VerticalOffset + 0.18f));
                        Vector3 screen = camera.WorldToScreenPoint(world);
                        if (screen.z <= 0f)
                        {
                            continue;
                        }

                        var rect = new Rect(
                            screen.x + 8f,
                            Screen.height - screen.y - 18f,
                            360f,
                            18f);
                        for (int attempt = 0; attempt < 12 && occupied.Exists(other => other.Overlaps(rect)); attempt++)
                        {
                            rect.y += 14f;
                        }

                        occupied.Add(rect);
                        Vector2 anchor = new Vector2(screen.x, Screen.height - screen.y);
                        DrawScreenLeaderLine(
                            anchor,
                            ClosestPointOnRect(rect, anchor),
                            debugLabel.Color);
                        Color previous = GUI.color;
                        GUI.color = debugLabel.Color;
                        GUI.Label(rect, debugLabel.Text, _labelStyle);
                        GUI.color = previous;
                    }
                }
            }
        }

        private static void DrawDebugLabelTargets(
            NarrowGaugeSettings settings,
            SpecialWorkMeshPlan plan)
        {
            foreach (GeometryDebugLabel label in plan.DebugLabels)
            {
                if (!ShouldDrawDebugLabel(settings, label.Text))
                {
                    continue;
                }

                Color targetColor = new Color(
                    label.Color.r,
                    label.Color.g,
                    label.Color.b,
                    0.95f);
                if (label.ReferenceCurve != null)
                {
                    DrawCurve(label.ReferenceCurve, targetColor);
                }

                DrawCross(label.Position, 0.13f, targetColor);
            }
        }

        private void OnDestroy()
        {
            if (_lineMaterial != null)
            {
                Destroy(_lineMaterial);
                _lineMaterial = null;
            }

            _labelStyle = null;
        }

        private bool EnsureMaterial()
        {
            if (_lineMaterial != null)
            {
                return true;
            }

            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                if (!_warnedMissingShader)
                {
                    _warnedMissingShader = true;
                    Main.Warn("Special-work debug view could not find the Hidden/Internal-Colored shader.");
                }

                return false;
            }

            _lineMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _lineMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _lineMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _lineMaterial.SetInt("_Cull", (int)CullMode.Off);
            _lineMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
            _lineMaterial.SetInt("_ZWrite", 0);
            return true;
        }

        private static bool ShouldDrawDebugLabel(
            NarrowGaugeSettings settings,
            string text)
        {
            if (text.StartsWith("Suppressed ")
                && !settings.DebugLabelSuppressionIntervals)
            {
                return false;
            }

            if (text.StartsWith("ReplacementPiece ")
                && !settings.DebugLabelReplacementPieces)
            {
                return false;
            }

            if (text.StartsWith("BladeStock ")
                && !settings.DebugLabelBladeStockRelationships)
            {
                return false;
            }

            if (text.StartsWith("FrogOwnership ")
                && !settings.DebugLabelFrogOwnership)
            {
                return false;
            }

            if ((text.StartsWith("StockRail ")
                    || text.StartsWith("MovablePointBlade ")
                    || text.StartsWith("FixedRunningRail ")
                    || text.StartsWith("ClosureRail ")
                    || text.StartsWith("FrogApproachRail ")
                    || text.StartsWith("FrogRail ")
                    || text.StartsWith("SharedRail ")
                    || text.StartsWith("SuppressedRail "))
                && !settings.DebugLabelRailRoleSections)
            {
                return false;
            }

            return true;
        }

        private static void DrawCurve(LineCurve curve, Color color)
        {
            if (curve == null)
            {
                return;
            }

            foreach ((int _, LineSegment segment) in curve.Segments)
            {
                DrawLine(segment.a.point, segment.b.point, color);
            }
        }

        private static void DrawRailSamples(RailCenterline rail, Color color)
        {
            if (rail.Samples.Count < 2)
            {
                DrawCurve(rail.Curve, color);
                return;
            }

            for (int index = 0; index + 1 < rail.Samples.Count; index++)
            {
                DrawLine(
                    rail.Samples[index].WorldPoint,
                    rail.Samples[index + 1].WorldPoint,
                    color);
            }
        }

        private static Color IntersectionKindColor(RailIntersectionKind kind)
        {
            switch (kind)
            {
                case RailIntersectionKind.SharedOverlap:
                    return SharedRailColor;
                case RailIntersectionKind.BladeConvergence:
                    return BladeColor;
                case RailIntersectionKind.VeeFrogCandidate:
                case RailIntersectionKind.CrossingFrogCandidate:
                    return FrogColor;
                case RailIntersectionKind.InvalidShallowCrossing:
                    return Color.yellow;
                case RailIntersectionKind.RouteJoin:
                    return Color.gray;
                default:
                    return IntersectionColor;
            }
        }

        private static void DrawMeshPlan(SpecialWorkMeshPlan plan)
        {
            foreach (RailCut cut in plan.Cuts)
            {
                DrawCurve(
                    cut.Rail.Curve
                        .Skip(cut.StartDistance, true)
                        .Take(cut.EndDistance - cut.StartDistance),
                    Color.yellow);
            }

            foreach (RailPiece piece in plan.FrogPieces)
            {
                DrawCurve(piece.Curve, FrogColor);
            }

            foreach (RailPiece piece in plan.FixedRunningRails)
            {
                DrawCurve(piece.Curve, PieceKindColor(piece.Kind));
            }

            foreach (WingRailPlan wing in plan.WingRails)
            {
                DrawCurve(wing.Curve, IntersectionColor);
            }

            foreach (GuardRailPlan guard in plan.GuardRails)
            {
                DrawCurve(guard.Curve, SharedRailColor);
            }

            foreach (SwitchBladePlan blade in plan.SwitchBlades)
            {
                DrawCurve(blade.BladeCurve, BladeColor);
            }
        }

        private void EnsureLabelStyle()
        {
            if (_labelStyle != null)
            {
                return;
            }

            _labelStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 11,
                wordWrap = false
            };
            _labelStyle.normal.textColor = Color.white;
        }

        private static void DrawCross(Vector3 point, float size, Color color)
        {
            DrawLine(point + Vector3.right * size, point - Vector3.right * size, color);
            DrawLine(point + Vector3.forward * size, point - Vector3.forward * size, color);
        }

        private static void DrawLine(Vector3 start, Vector3 end, Color color)
        {
            GL.Color(color);
            GL.Vertex(WorldTransformer.GameToWorld(start + Vector3.up * VerticalOffset));
            GL.Vertex(WorldTransformer.GameToWorld(end + Vector3.up * VerticalOffset));
        }

        private static Vector2 ClosestPointOnRect(Rect rect, Vector2 point)
        {
            Vector2 clamped = new Vector2(
                Mathf.Clamp(point.x, rect.xMin, rect.xMax),
                Mathf.Clamp(point.y, rect.yMin, rect.yMax));
            if (!rect.Contains(point))
            {
                return clamped;
            }

            float left = point.x - rect.xMin;
            float right = rect.xMax - point.x;
            float top = point.y - rect.yMin;
            float bottom = rect.yMax - point.y;
            float nearest = Mathf.Min(left, right, top, bottom);
            if (Mathf.Approximately(nearest, left))
            {
                return new Vector2(rect.xMin, point.y);
            }

            if (Mathf.Approximately(nearest, right))
            {
                return new Vector2(rect.xMax, point.y);
            }

            return Mathf.Approximately(nearest, top)
                ? new Vector2(point.x, rect.yMin)
                : new Vector2(point.x, rect.yMax);
        }

        private static void DrawScreenLeaderLine(Vector2 start, Vector2 end, Color color)
        {
            Vector2 delta = end - start;
            float length = delta.magnitude;
            if (length <= 1f)
            {
                return;
            }

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, 0.9f);
            GUIUtility.RotateAroundPivot(
                Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg,
                start);
            GUI.DrawTexture(
                new Rect(start.x, start.y - 1f, length, 2f),
                Texture2D.whiteTexture);
            GUI.matrix = previousMatrix;
            GUI.color = color;
            GUI.DrawTexture(
                new Rect(start.x - 3f, start.y - 3f, 6f, 6f),
                Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private static Color PieceKindColor(RailPieceKind kind)
        {
            switch (kind)
            {
                case RailPieceKind.SharedRunning:
                    return SharedRailColor;
                case RailPieceKind.ClosureRail:
                    return new Color(1f, 0.55f, 0f, 1f);
                case RailPieceKind.FrogNose:
                    return FrogColor;
                case RailPieceKind.WingRail:
                    return new Color(1f, 0.35f, 0.75f, 1f);
                case RailPieceKind.GuardRail:
                    return Color.magenta;
                case RailPieceKind.MovableBlade:
                    return Color.yellow;
                default:
                    return Color.green;
            }
        }

        private static Color RailRoleColor(RailRole role)
        {
            switch (role)
            {
                case RailRole.StockRail:
                    return Color.green;
                case RailRole.PointBlade:
                    return Color.yellow;
                case RailRole.ClosureRail:
                    return new Color(1f, 0.55f, 0f, 1f);
                case RailRole.FrogRail:
                case RailRole.CrossingRail:
                    return FrogColor;
                case RailRole.WingRail:
                    return new Color(1f, 0.35f, 0.75f, 1f);
                case RailRole.GuardRail:
                    return Color.magenta;
                case RailRole.SharedRail:
                    return new Color(0.35f, 1f, 0.1f, 1f);
                case RailRole.SuppressedRail:
                    return Color.gray;
                default:
                    return new Color(1f, 0f, 0f, 1f);
            }
        }
    }
}
