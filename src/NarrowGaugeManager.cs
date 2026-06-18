using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using Core;
using FUSE.Authoring.Data;
using FUSE.Runtime.API;
using FUSE.Runtime.Events;
using Track;
using UnityEngine;

namespace NarrowGaugeMod
{
    /// <summary>
    /// Fuse companion lifecycle, gauge metadata cache, and generated ghost graph
    /// synchronization coordinator.
    /// </summary>
    public class NarrowGaugeManager : MonoBehaviour
    {
        private static readonly HashSet<string> ExplicitNarrowSegmentIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> ExplicitDualGaugeSegmentIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> GeneratedGhostSegmentIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static ShadowNarrowGaugeGraph? CurrentShadowGraph;
        private static string LastValidationSignature = string.Empty;
        private static string LastAuthoringIssueSignature = string.Empty;
        private static string LastSpecialWorkAnalysisSignature = string.Empty;
        private static bool HasSpecialWorkAnalysisSnapshot;

        private bool _synchronizing;
        private bool _syncRequested;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            FuseEvents.TrackGraphApplying += HandleTrackGraphApplying;
            FuseEvents.GraphRebuilt += HandleFuseGraphRebuilt;
            FuseEvents.FuseUnloaded += HandleFuseUnloaded;
            Main.Log("FUSE Narrow Gauge manager awake.");
        }

        private void Start()
        {
            StartCoroutine(WaitForGraphAndSynchronize());
        }

        private void LateUpdate()
        {
            if (!_syncRequested || _synchronizing)
            {
                return;
            }

            Graph graph = Graph.Shared;
            if (graph == null || !graph.HasPopulatedCollections)
            {
                return;
            }

            _syncRequested = false;
            SynchronizeOutsideFuseRebuild(graph, "deferred graph synchronization");
        }

        private void OnDestroy()
        {
            FuseEvents.TrackGraphApplying -= HandleTrackGraphApplying;
            FuseEvents.GraphRebuilt -= HandleFuseGraphRebuilt;
            FuseEvents.FuseUnloaded -= HandleFuseUnloaded;
            DualGaugeLinkRegistry.Clear();
            SpecialWorkRuntimeRegistry.Clear();
        }

        private IEnumerator WaitForGraphAndSynchronize()
        {
            var wait = new WaitForSeconds(0.5f);
            while (true)
            {
                Graph graph = Graph.Shared;
                if (graph != null && graph.HasPopulatedCollections)
                {
                    SynchronizeOutsideFuseRebuild(graph, "initial module synchronization");
                    yield break;
                }

                yield return wait;
            }
        }

        private void HandleTrackGraphApplying(FuseTrackGraphApplyingContext context)
        {
            if (_synchronizing || context?.Graph == null)
            {
                return;
            }

            _synchronizing = true;
            try
            {
                GhostGraphSynchronizationResult result = GhostGraphSynchronizer.Synchronize(context.Graph);
                SpecialWorkTopologySynchronizationResult specialWork =
                    Main.Settings?.EnableSpecialWorkTopology == false
                        ? new SpecialWorkTopologySynchronizationResult()
                        : SpecialWorkTopologySynchronizer.Synchronize(context.Graph);
                RefreshGaugeMetadata(context.Graph);
                _syncRequested = false;
                LogSynchronization(result, "Fuse pre-rebuild");
                LogSpecialWorkSynchronization(specialWork, "Fuse pre-rebuild");
            }
            finally
            {
                _synchronizing = false;
            }
        }

        private void HandleFuseGraphRebuilt()
        {
            // Graph.RebuildCollections is patched so the measured plans exist
            // before TrackObjectManager renders the rebuilt graph. Running the
            // same full scan again here, after rendering, only duplicates the
            // most expensive startup work.
            _syncRequested = true;
        }

        private void HandleFuseUnloaded()
        {
            ExplicitNarrowSegmentIds.Clear();
            ExplicitDualGaugeSegmentIds.Clear();
            GeneratedGhostSegmentIds.Clear();
            CurrentShadowGraph = null;
            LastAuthoringIssueSignature = string.Empty;
            LastSpecialWorkAnalysisSignature = string.Empty;
            HasSpecialWorkAnalysisSnapshot = false;
            DualGaugeSharedRailRegistry.Clear();
            DualGaugeLinkRegistry.Clear();
            SpecialWorkRuntimeRegistry.Clear();
        }

        private void SynchronizeOutsideFuseRebuild(Graph graph, string reason)
        {
            if (_synchronizing)
            {
                return;
            }

            _synchronizing = true;
            GhostGraphSynchronizationResult result;
            SpecialWorkTopologySynchronizationResult specialWork;
            TrackAPI.BeginBatch();
            try
            {
                result = GhostGraphSynchronizer.Synchronize(graph);
                specialWork = Main.Settings?.EnableSpecialWorkTopology == false
                    ? new SpecialWorkTopologySynchronizationResult()
                    : SpecialWorkTopologySynchronizer.Synchronize(graph);
                RefreshGaugeMetadata(graph);
            }
            finally
            {
                TrackAPI.EndBatch(true);
                _synchronizing = false;
            }

            LogSynchronization(result, reason);
            LogSpecialWorkSynchronization(specialWork, reason);
        }

        private static void LogSynchronization(GhostGraphSynchronizationResult result, string reason)
        {
            if (result.HasChanges || result.DualGaugeSources > 0)
            {
                Main.Log($"Ghost graph synchronization ({reason}): {result}");
            }
        }

        private static void LogSpecialWorkSynchronization(
            SpecialWorkTopologySynchronizationResult result,
            string reason)
        {
            if (result.HasChanges
                || result.NarrowBranchTransitions > 0
                || result.DualToNarrowContinuations > 0
                || result.Issues.Count > 0)
            {
                Main.Log($"Special-work topology synchronization ({reason}): {result}");
            }

            foreach (string issue in result.Issues.Take(8))
            {
                Main.Warn("  " + issue);
            }
        }

        public static void RequestSynchronization()
        {
            HasSpecialWorkAnalysisSnapshot = false;
            NarrowGaugeManager manager = FindObjectOfType<NarrowGaugeManager>();
            if (manager != null)
            {
                manager._syncRequested = true;
            }
        }

        public static void ScanGraph(Graph graph)
        {
            if (graph == null)
            {
                return;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            RefreshGaugeMetadata(graph);
            long metadataElapsed = stopwatch.ElapsedMilliseconds;
            RebuildShadowGraph(graph);
            long shadowElapsed = stopwatch.ElapsedMilliseconds;
            DualGaugeSwitchSynchronizer.SynchronizeAll(graph);
            long switchesElapsed = stopwatch.ElapsedMilliseconds;
            RebuildSpecialWorkAnalyses(graph);
            long specialWorkElapsed = stopwatch.ElapsedMilliseconds;

            int visibleNarrow = graph.Segments.Count(segment =>
                IsNarrowGauge(segment) && !IsGeneratedGhost(segment));
            int dual = graph.Segments.Count(IsDualGauge);
            int ghosts = graph.Segments.Count(IsGeneratedGhost);

            if (visibleNarrow > 0 || dual > 0 || ghosts > 0)
            {
                Main.Log(
                    $"Gauge graph: narrow={visibleNarrow}, dual={dual}, " +
                    $"generatedGhosts={ghosts}, links={DualGaugeLinkRegistry.Links.Count}.");
            }

            LogValidationChanges(GaugeGraphValidator.Validate(graph));
            stopwatch.Stop();
            Main.Log(
                $"Gauge graph scan timing: metadataMs={metadataElapsed}, " +
                $"shadowMs={shadowElapsed - metadataElapsed}, " +
                $"switchesMs={switchesElapsed - shadowElapsed}, " +
                $"specialWorkMs={specialWorkElapsed - switchesElapsed}, " +
                $"validationMs={stopwatch.ElapsedMilliseconds - specialWorkElapsed}, " +
                $"totalMs={stopwatch.ElapsedMilliseconds}.");
        }

        public static void RefreshGaugeMetadata(Graph graph)
        {
            ExplicitNarrowSegmentIds.Clear();
            ExplicitDualGaugeSegmentIds.Clear();
            GeneratedGhostSegmentIds.Clear();

            if (graph == null)
            {
                return;
            }

            foreach (TrackSegment segment in graph.Segments)
            {
                if (segment == null || string.IsNullOrWhiteSpace(segment.id))
                {
                    continue;
                }

                if (GhostGraphSynchronizer.IsGeneratedGhostSegmentId(segment.id))
                {
                    GeneratedGhostSegmentIds.Add(segment.id);
                    ExplicitNarrowSegmentIds.Add(segment.id);
                    continue;
                }

                FuseSegment definition = TrackAPI.GetSegmentDefinition(segment.id);
                if (GhostGraphSynchronizer.IsDualGaugeDefinition(definition))
                {
                    ExplicitDualGaugeSegmentIds.Add(segment.id);
                }
                else if (GhostGraphSynchronizer.IsNarrowGaugeDefinition(definition))
                {
                    ExplicitNarrowSegmentIds.Add(segment.id);
                }
            }
        }

        public static bool HasExplicitNarrowGauge(TrackSegment? segment)
        {
            return segment != null
                && !string.IsNullOrEmpty(segment.id)
                && ExplicitNarrowSegmentIds.Contains(segment.id);
        }

        public static bool IsNarrowGauge(TrackSegment? segment)
        {
            return HasExplicitNarrowGauge(segment)
                || IsNarrowTurntableBridge(segment);
        }

        public static bool IsDualGauge(TrackSegment? segment)
        {
            return segment != null
                && !string.IsNullOrEmpty(segment.id)
                && ExplicitDualGaugeSegmentIds.Contains(segment.id);
        }

        public static bool IsGeneratedGhost(TrackSegment? segment)
        {
            return segment != null
                && !string.IsNullOrEmpty(segment.id)
                && GeneratedGhostSegmentIds.Contains(segment.id);
        }

        public static bool IsGeneratedGhostNode(TrackNode? node)
        {
            return node != null && GhostGraphSynchronizer.IsGeneratedGhostNodeId(node.id);
        }

        private static bool IsNarrowTurntableBridge(TrackSegment? segment)
        {
            if (segment == null
                || segment.turntable == null
                || segment.style != TrackSegment.Style.Bridge)
            {
                return false;
            }

            return TurntableBridgeEndTouchesExplicitNarrowGauge(segment, segment.a)
                || TurntableBridgeEndTouchesExplicitNarrowGauge(segment, segment.b);
        }

        private static bool TurntableBridgeEndTouchesExplicitNarrowGauge(
            TrackSegment bridgeSegment,
            TrackNode? node)
        {
            if (node == null || Graph.Shared == null)
            {
                return false;
            }

            return Graph.Shared.SegmentsConnectedTo(node)
                .Any(connected => connected != null
                    && connected != bridgeSegment
                    && HasExplicitNarrowGauge(connected));
        }

        internal static ShadowNarrowGaugeGraph? GetShadowGraph()
        {
            return CurrentShadowGraph;
        }

        internal static ShadowNarrowGaugeTransition? GetShadowTransition(TrackNode? node)
        {
            if (node == null
                || CurrentShadowGraph == null
                || string.IsNullOrEmpty(node.id))
            {
                return null;
            }

            return CurrentShadowGraph.Transitions.TryGetValue(node.id, out ShadowNarrowGaugeTransition transition)
                ? transition
                : null;
        }

        public static void RebuildShadowGraph(Graph graph)
        {
            try
            {
                CurrentShadowGraph = ShadowNarrowGaugeGraphBuilder.Build(graph);
            }
            catch (Exception ex)
            {
                CurrentShadowGraph = null;
                Main.Warn($"Failed to build narrow-gauge geometry shadow graph: {ex.Message}");
            }
        }

        private static void RebuildSpecialWorkAnalyses(Graph graph)
        {
            try
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                IReadOnlyList<SpecialWorkDefinition> definitions =
                    SpecialWorkRuntimeDiscovery.Discover(graph);
                SpecialWorkAuthoringSnapshot authoring = SpecialWorkAuthoring.Load();
                definitions = SpecialWorkAuthoring.Apply(
                    definitions,
                    authoring,
                    out IReadOnlyList<string> authoringIssues);
                LogAuthoringIssues(authoring, authoringIssues);

                string analysisSignature = BuildSpecialWorkAnalysisSignature(
                    definitions,
                    authoringIssues);
                if (HasSpecialWorkAnalysisSnapshot
                    && string.Equals(
                        analysisSignature,
                        LastSpecialWorkAnalysisSignature,
                        StringComparison.Ordinal))
                {
                    return;
                }

                var completedAnalyses = new List<SpecialWorkAnalysis>();
                foreach (SpecialWorkDefinition definition in definitions)
                {
                    try
                    {
                        completedAnalyses.Add(
                            SpecialWorkGeometryAnalyzer.Analyze(graph, definition));
                    }
                    catch (Exception ex)
                    {
                        Main.Warn(
                            $"Failed to analyze special-work '{definition.Id}' " +
                            $"({definition.Preset.Id}): {ex.Message}");
                    }
                }

                SpecialWorkAnalysis[] analyses = completedAnalyses.ToArray();
                SpecialWorkRuntimeRegistry.Replace(analyses);
                LastSpecialWorkAnalysisSignature = analysisSignature;
                HasSpecialWorkAnalysisSnapshot = true;
                stopwatch.Stop();

                int invalid = analyses.Count(analysis => analysis.ValidationIssues.Count > 0);
                if (analyses.Length > 0)
                {
                    Main.Log(
                        $"Special-work analysis: objects={analyses.Length}, invalid={invalid}, " +
                        $"elapsedMs={stopwatch.ElapsedMilliseconds}.");
                }

                foreach (SpecialWorkAnalysis analysis in analyses
                    .Where(item => item.MeshPlan != null))
                {
                    SpecialWorkMeshPlan plan = analysis.MeshPlan!;
                    Main.Log(
                        $"Special-work plan '{analysis.Definition.Id}' ({analysis.Definition.Preset.Id}): " +
                        $"valid={plan.IsValid}, rails={analysis.Rails.Count}, " +
                        $"shared={analysis.SharedRailIntervals.Count}, " +
                        $"intersections={analysis.Intersections.Count}, cuts={plan.Cuts.Count}, " +
                        $"fixed={plan.FixedRunningRails.Count}, frogs={plan.Frogs.Count}, " +
                        $"wings={plan.WingRails.Count}, guards={plan.GuardRails.Count}, " +
                        $"blades={plan.SwitchBlades.Count}.");
                }

                foreach (SpecialWorkAnalysis analysis in analyses
                    .Where(item => item.ValidationIssues.Count > 0)
                    .Take(6))
                {
                    Main.Warn(
                        $"Special-work FIRST FAILURE '{analysis.Definition.Id}': " +
                        analysis.ValidationIssues.First());
                    Main.Warn(
                        $"Special-work '{analysis.Definition.Id}' ({analysis.Definition.Preset.Id}) " +
                        $"has {analysis.ValidationIssues.Count} validation issue(s).");
                    foreach (string issue in analysis.ValidationIssues.Take(4))
                    {
                        Main.Warn("  " + issue);
                    }
                }
            }
            catch (Exception ex)
            {
                SpecialWorkRuntimeRegistry.Clear();
                HasSpecialWorkAnalysisSnapshot = false;
                Main.Warn($"Failed to rebuild special-work analysis: {ex.Message}");
            }
        }

        private static string BuildSpecialWorkAnalysisSignature(
            IEnumerable<SpecialWorkDefinition> definitions,
            IEnumerable<string> authoringIssues)
        {
            var signature = new StringBuilder();
            foreach (SpecialWorkDefinition definition in definitions
                .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
            {
                AppendSignatureValue(signature, definition.Id);
                AppendSignatureValue(signature, definition.Preset.Id);

                foreach (SpecialWorkPort port in definition.Ports
                    .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
                {
                    AppendSignatureValue(signature, port.Id);
                    AppendSignatureValue(signature, port.AvailableFamilies.ToString());
                    AppendSignatureVector(signature, port.Position);
                }

                foreach (LogicalRoute route in definition.Routes
                    .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
                {
                    AppendSignatureValue(signature, route.Id);
                    AppendSignatureValue(signature, route.Family.ToString());
                    AppendSignatureValue(signature, route.SwitchGroupId);
                    AppendSignatureValue(signature, route.RequiredStateId);
                    foreach (string segmentId in route.SourceSegmentIds)
                    {
                        AppendSignatureValue(signature, segmentId);
                    }

                    foreach (LinePoint point in route.Centerline.Points)
                    {
                        AppendSignatureVector(signature, point.point);
                    }
                }

                foreach (SpecialWorkSwitchGroup group in definition.SwitchGroups
                    .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
                {
                    AppendSignatureValue(signature, group.Id);
                    foreach (string nodeId in group.NativeNodeIds)
                    {
                        AppendSignatureValue(signature, nodeId);
                    }

                    foreach (string stateId in group.LegalStateIds)
                    {
                        AppendSignatureValue(signature, stateId);
                    }
                }

                SpecialWorkAuthoringBinding? authored = definition.Authoring;
                if (authored != null)
                {
                    AppendSignatureValue(signature, authored.PackageId);
                    AppendSignatureValue(signature, authored.Id);
                    AppendSignatureValue(signature, authored.PresetId);
                    AppendSignatureValue(signature, authored.AnchorNodeId);
                    foreach (KeyValuePair<string, Newtonsoft.Json.Linq.JToken> parameter in
                        authored.Parameters.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        AppendSignatureValue(signature, parameter.Key);
                        AppendSignatureValue(signature, parameter.Value?.ToString());
                    }
                }
            }

            foreach (string issue in (authoringIssues ?? Array.Empty<string>())
                .OrderBy(item => item, StringComparer.Ordinal))
            {
                AppendSignatureValue(signature, issue);
            }

            return signature.ToString();
        }

        private static void AppendSignatureVector(StringBuilder signature, Vector3 value)
        {
            AppendSignatureValue(signature, value.x.ToString("R", CultureInfo.InvariantCulture));
            AppendSignatureValue(signature, value.y.ToString("R", CultureInfo.InvariantCulture));
            AppendSignatureValue(signature, value.z.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void AppendSignatureValue(StringBuilder signature, string? value)
        {
            string safeValue = value ?? string.Empty;
            signature.Append(safeValue.Length);
            signature.Append(':');
            signature.Append(safeValue);
            signature.Append('|');
        }

        private static void LogAuthoringIssues(
            SpecialWorkAuthoringSnapshot authoring,
            IReadOnlyList<string> issues)
        {
            IReadOnlyList<string> safeIssues = issues ?? Array.Empty<string>();
            string signature = string.Join(
                "\n",
                safeIssues.OrderBy(issue => issue, StringComparer.Ordinal));
            if (string.Equals(signature, LastAuthoringIssueSignature, StringComparison.Ordinal))
            {
                return;
            }

            LastAuthoringIssueSignature = signature;
            if (authoring.Bindings.Count > 0)
            {
                Main.Log(
                    $"Special-work authoring: bindings={authoring.Bindings.Count}, issues={safeIssues.Count}.");
            }

            foreach (string issue in safeIssues.Take(12))
            {
                Main.Warn("Special-work authoring: " + issue);
            }
        }

        private static void LogValidationChanges(GaugeGraphValidationReport report)
        {
            string signature = string.Join("\n", report.Issues.OrderBy(issue => issue, StringComparer.Ordinal));
            if (string.Equals(signature, LastValidationSignature, StringComparison.Ordinal))
            {
                return;
            }

            LastValidationSignature = signature;
            if (report.IsValid)
            {
                Main.Log("Gauge graph validation passed.");
                return;
            }

            Main.Warn($"Gauge graph validation found {report.Issues.Count} issue(s).");
            foreach (string issue in report.Issues.Take(12))
            {
                Main.Warn("  " + issue);
            }
        }
    }
}
