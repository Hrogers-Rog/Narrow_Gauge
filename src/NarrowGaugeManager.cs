using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Core;
using Track;
using UnityEngine;

namespace NarrowGaugeMod
{
    /// <summary>
    /// Keeps track of which segments should be treated as narrow gauge.
    /// Uses explicit JSON gauge metadata loaded from installed game-graph files.
    /// </summary>
    public class NarrowGaugeManager : MonoBehaviour
    {
        private static readonly HashSet<string> ExplicitNarrowSegmentIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> ExplicitDualGaugeSegmentIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static ShadowNarrowGaugeGraph? CurrentShadowGraph;
        private static bool GaugeMetadataLoadedFromInstalledMods;

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            Main.Log("NarrowGaugeManager awake.");
        }

        void Start()
        {
            StartCoroutine(WaitForGraphAndScan());
        }

        IEnumerator WaitForGraphAndScan()
        {
            var wait = new WaitForSeconds(0.5f);

            Main.Log("Waiting for Graph.Shared to be ready...");

            while (true)
            {
                Graph graph = Graph.Shared;

                if (graph != null && graph.HasPopulatedCollections)
                {
                    ReloadGaugeMetadataFromInstalledMods();
                    Main.Log("Graph ready; scanning for narrow gauge segments.");
                    ScanGraph(graph);
                    yield break;
                }

                yield return wait;
            }
        }

        public static void ScanGraph(Graph graph)
        {
            if (graph == null)
            {
                Main.Warn("ScanGraph called with null graph.");
                return;
            }

            var narrowSegments = graph.Segments.Where(IsNarrowGauge).ToList();
            var dualSegments   = graph.Segments.Where(IsDualGauge).ToList();

            if (narrowSegments.Count == 0 && dualSegments.Count == 0)
            {
                Main.Log(
                    "No narrow or dual gauge segments found. " +
                    "Use Gauge='Narrow' or Gauge='DualGauge' in StrangeCustoms graph JSON.");
                return;
            }

            if (narrowSegments.Count > 0)
            {
                Main.Log($"Found {narrowSegments.Count} narrow gauge segment(s):");
                foreach (TrackSegment seg in narrowSegments)
                {
                    Main.Log(
                        $"  [{seg.id}]  gauge=Narrow" +
                        $"  class={seg.trackClass}" +
                        $"  style={seg.style}" +
                        $"  speed={seg.speedLimit}" +
                        $"  a={seg.a?.id ?? "null"}  b={seg.b?.id ?? "null"}");
                }
            }

            if (dualSegments.Count > 0)
            {
                Main.Log($"Found {dualSegments.Count} dual gauge segment(s):");
                foreach (TrackSegment seg in dualSegments)
                {
                    Main.Log(
                        $"  [{seg.id}]  gauge=DualGauge" +
                        $"  class={seg.trackClass}" +
                        $"  style={seg.style}" +
                        $"  speed={seg.speedLimit}" +
                        $"  a={seg.a?.id ?? "null"}  b={seg.b?.id ?? "null"}");
                }
            }

            RebuildShadowGraph(graph);
        }

        public static void ClearExplicitGaugeTags(string? reason = null)
        {
            if (ExplicitNarrowSegmentIds.Count == 0 && ExplicitDualGaugeSegmentIds.Count == 0)
            {
                CurrentShadowGraph = null;
                GaugeMetadataLoadedFromInstalledMods = false;

                if (!string.IsNullOrWhiteSpace(reason))
                    Main.Log($"Gauge metadata already empty: {reason}");

                return;
            }

            ExplicitNarrowSegmentIds.Clear();
            ExplicitDualGaugeSegmentIds.Clear();
            CurrentShadowGraph = null;
            GaugeMetadataLoadedFromInstalledMods = false;

            if (!string.IsNullOrWhiteSpace(reason))
                Main.Log($"Cleared explicit gauge metadata: {reason}");
        }

        public static void EnsureGaugeMetadataLoaded()
        {
            if (GaugeMetadataLoadedFromInstalledMods)
                return;

            LoadGaugeMetadataFromInstalledMods();
        }

        public static void ReloadGaugeMetadataFromInstalledMods()
        {
            ClearExplicitGaugeTags("reloading installed mod gauge metadata");
            LoadGaugeMetadataFromInstalledMods();
        }

        public static void LoadGaugeMetadataFromInstalledMods()
        {
            if (GaugeMetadataLoadedFromInstalledMods)
                return;

            string? modsRoot = GetModsRoot();
            if (string.IsNullOrEmpty(modsRoot) || !Directory.Exists(modsRoot))
            {
                Main.Warn("Could not locate Railroader Mods folder for gauge metadata scan.");
                return;
            }

            int scannedFiles = 0;

            foreach (string definitionPath in Directory.GetFiles(
                modsRoot,
                "Definition.json",
                SearchOption.TopDirectoryOnly))
            {
                scannedFiles += LoadGaugeMetadataFromDefinition(definitionPath);
            }

            foreach (string modDirectory in Directory.GetDirectories(modsRoot))
            {
                string definitionPath = Path.Combine(modDirectory, "Definition.json");
                if (!File.Exists(definitionPath))
                    continue;

                scannedFiles += LoadGaugeMetadataFromDefinition(definitionPath);
            }

            foreach (string modDirectory in Directory.GetDirectories(modsRoot))
            {
                string infoPath = Path.Combine(modDirectory, "Info.json");
                if (!File.Exists(infoPath))
                    continue;

                scannedFiles += LoadGaugeMetadataFromFuseInfo(infoPath);
            }

            if (scannedFiles == 0)
            {
                Main.Log("Gauge metadata scan found no game-graph or FUSE files.");
            }
            else
            {
                Main.Log($"Gauge metadata scan checked {scannedFiles} game-graph/FUSE file(s).");
            }

            GaugeMetadataLoadedFromInstalledMods = true;
        }

        public static void RecordGaugeMetadata(JObject? patchRoot, string source)
        {
            JObject? segments = patchRoot?["tracks"]?["segments"] as JObject;
            if (segments == null)
                return;

            int added = 0;
            int removed = 0;

            foreach (JProperty property in segments.Properties())
            {
                string segmentId = property.Name;
                JToken value = property.Value;

                if (value.Type == JTokenType.Null)
                {
                    if (ExplicitNarrowSegmentIds.Remove(segmentId))
                        removed++;

                    continue;
                }

                if (value is not JObject segmentObject)
                    continue;

                string? gauge = GetGaugeValue(segmentObject);
                if (gauge == null)
                    continue;

                if (IsNarrowGaugeValue(gauge))
                {
                    ExplicitDualGaugeSegmentIds.Remove(segmentId);
                    if (ExplicitNarrowSegmentIds.Add(segmentId))
                        added++;
                }
                else if (IsDualGaugeValue(gauge))
                {
                    ExplicitNarrowSegmentIds.Remove(segmentId);
                    if (ExplicitDualGaugeSegmentIds.Add(segmentId))
                        added++;
                }
                else if (ExplicitNarrowSegmentIds.Remove(segmentId)
                       | ExplicitDualGaugeSegmentIds.Remove(segmentId))
                {
                    removed++;
                }
            }

            if (added > 0 || removed > 0)
            {
                Main.Log(
                    $"Loaded gauge metadata from '{source}': " +
                    $"{added} narrow, {removed} cleared.");
            }
        }

        public static bool HasExplicitNarrowGauge(TrackSegment? segment)
        {
            EnsureGaugeMetadataLoaded();

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
            EnsureGaugeMetadataLoaded();

            return segment != null
                && !string.IsNullOrEmpty(segment.id)
                && ExplicitDualGaugeSegmentIds.Contains(segment.id);
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
                return false;

            foreach (TrackSegment connected in Graph.Shared.SegmentsConnectedTo(node))
            {
                if (connected == null || connected == bridgeSegment)
                    continue;

                if (HasExplicitNarrowGauge(connected))
                    return true;
            }

            return false;
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

                Main.Log(
                    $"Shadow NG graph built: " +
                    $"{CurrentShadowGraph.Nodes.Count} node(s), " +
                    $"{CurrentShadowGraph.Segments.Count} segment(s), " +
                    $"{CurrentShadowGraph.DualSegmentCount} dual, " +
                    $"{CurrentShadowGraph.NarrowOnlySegmentCount} narrow-only, " +
                    $"{CurrentShadowGraph.TransitionNodeCount} transition node(s).");

                foreach (ShadowNarrowGaugeNode transitionNode in CurrentShadowGraph.Nodes.Values
                    .Where(n => n.RequiresTransition)
                    .Take(8))
                {
                    Main.Log(
                        $"  [ShadowNG] transition node {transitionNode.Id} " +
                        $"pos=({transitionNode.Position.x:F3}, {transitionNode.Position.y:F3}, {transitionNode.Position.z:F3}) " +
                        $"dual={transitionNode.HasDualGaugeConnection} narrow={transitionNode.HasNarrowOnlyConnection}");
                }

                foreach (ShadowNarrowGaugeTransition transition in CurrentShadowGraph.Transitions.Values.Take(8))
                {
                    var samples = transition.SampledCurve.Points.ToList();
                    if (samples.Count == 0)
                        continue;

                    LinePoint start = samples[0];
                    LinePoint mid = samples[samples.Count / 2];
                    LinePoint end = samples[samples.Count - 1];

                    Main.Log(
                        $"  [ShadowNG] transition curve {transition.Id} " +
                        $"start=({start.point.x:F3}, {start.point.y:F3}, {start.point.z:F3}) " +
                        $"mid=({mid.point.x:F3}, {mid.point.y:F3}, {mid.point.z:F3}) " +
                        $"end=({end.point.x:F3}, {end.point.y:F3}, {end.point.z:F3})");
                }
            }
            catch (Exception ex)
            {
                CurrentShadowGraph = null;
                Main.Warn($"Failed to build shadow NG graph: {ex.Message}");
            }
        }

        private static string? GetGaugeValue(JObject segmentObject)
        {
            JProperty? gaugeProperty = segmentObject.Properties()
                .FirstOrDefault(p => string.Equals(
                    p.Name,
                    "Gauge",
                    StringComparison.OrdinalIgnoreCase));

            if (gaugeProperty == null)
                return null;

            if (gaugeProperty.Value.Type == JTokenType.Null)
                return string.Empty;

            return gaugeProperty.Value.Type == JTokenType.String
                ? gaugeProperty.Value.Value<string>()
                : gaugeProperty.Value.ToString();
        }

        private static bool IsNarrowGaugeValue(string gauge)
        {
            return gauge.Equals("Narrow", StringComparison.OrdinalIgnoreCase)
                || gauge.Equals("3ft", StringComparison.OrdinalIgnoreCase)
                || gauge.Equals("3 ft", StringComparison.OrdinalIgnoreCase)
                || gauge.Equals("ThreeFoot", StringComparison.OrdinalIgnoreCase)
                || gauge.Equals("Three Foot", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDualGaugeValue(string gauge)
        {
            return gauge.Equals("DualGauge", StringComparison.OrdinalIgnoreCase)
                || gauge.Equals("Dual", StringComparison.OrdinalIgnoreCase)
                || gauge.Equals("Mixed", StringComparison.OrdinalIgnoreCase)
                || gauge.Equals("MixedGauge", StringComparison.OrdinalIgnoreCase);
        }

        private static int LoadGaugeMetadataFromDefinition(string definitionPath)
        {
            try
            {
                JObject definition = JObject.Parse(File.ReadAllText(definitionPath));
                JArray? mixintos = definition["mixintos"]?["game-graph"] as JArray;
                if (mixintos == null || mixintos.Count == 0)
                    return 0;

                string modDirectory = Path.GetDirectoryName(definitionPath) ?? string.Empty;
                int scannedFiles = 0;

                foreach (JToken mixinto in mixintos)
                {
                    string? fileReference = mixinto.Value<string>();
                    string? fileName = ParseFileMixinto(fileReference);
                    if (string.IsNullOrEmpty(fileName))
                        continue;

                    string patchPath = Path.Combine(modDirectory, fileName);
                    if (!File.Exists(patchPath))
                    {
                        Main.Warn($"Gauge metadata scan skipped missing file '{patchPath}'.");
                        continue;
                    }

                    JObject patchRoot = JObject.Parse(File.ReadAllText(patchPath));
                    RecordGaugeMetadata(patchRoot, patchPath);
                    scannedFiles++;
                }

                return scannedFiles;
            }
            catch (Exception ex)
            {
                Main.Warn(
                    $"Gauge metadata scan failed for '{definitionPath}': {ex.Message}");
                return 0;
            }
        }

        private static int LoadGaugeMetadataFromFuseInfo(string infoPath)
        {
            try
            {
                JObject info = JObject.Parse(File.ReadAllText(infoPath));
                string modDirectory = Path.GetDirectoryName(infoPath) ?? string.Empty;
                int scannedFiles = 0;

                foreach (string fileName in GetFuseDataFiles(info))
                {
                    string dataPath = Path.Combine(modDirectory, fileName);
                    if (!File.Exists(dataPath))
                    {
                        Main.Warn($"Gauge metadata scan skipped missing FUSE file '{dataPath}'.");
                        continue;
                    }

                    JObject fuseRoot = JObject.Parse(File.ReadAllText(dataPath));
                    RecordGaugeMetadata(fuseRoot, dataPath);
                    scannedFiles++;
                }

                return scannedFiles;
            }
            catch (Exception ex)
            {
                Main.Warn($"Gauge metadata scan failed for FUSE info '{infoPath}': {ex.Message}");
                return 0;
            }
        }

        private static IEnumerable<string> GetFuseDataFiles(JObject info)
        {
            string? single = info["FuseDataFile"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(single))
                yield return single!;

            if (info["FuseDataFiles"] is not JArray files)
                yield break;

            foreach (JToken token in files)
            {
                string? value = token.Value<string>();
                if (!string.IsNullOrWhiteSpace(value))
                    yield return value!;
            }
        }

        private static string? ParseFileMixinto(string? mixintoReference)
        {
            if (mixintoReference == null || string.IsNullOrWhiteSpace(mixintoReference))
                return null;

            const string prefix = "file(";
            if (!mixintoReference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || !mixintoReference.EndsWith(")", StringComparison.Ordinal))
            {
                return null;
            }

            return mixintoReference.Substring(
                prefix.Length,
                mixintoReference.Length - prefix.Length - 1);
        }

        private static string? GetModsRoot()
        {
            string assemblyLocation = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(assemblyLocation))
                return null;

            string? modDirectory = Path.GetDirectoryName(assemblyLocation);
            if (string.IsNullOrEmpty(modDirectory))
                return null;

            DirectoryInfo? parent = Directory.GetParent(modDirectory);
            return parent?.FullName;
        }
    }
}
