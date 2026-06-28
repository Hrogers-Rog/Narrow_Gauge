using FUSE;
using HarmonyLib;
using System.Linq;
using UnityEngine;
using UnityModManagerNet;

namespace NarrowGaugeMod
{
    /// <summary>
    /// UMM entry point. Called once by Unity Mod Manager when the game loads.
    /// </summary>
    public static class Main
    {
        public static UnityModManager.ModEntry? ModEntry { get; private set; }
        public static Harmony? Harmony { get; private set; }
        public static NarrowGaugeSettings? Settings { get; private set; }
        internal static GameObject? ManagerObject;

        /// <summary>True while the mod is enabled.</summary>
        public static bool Enabled { get; private set; }

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;
            Settings = UnityModManager.ModSettings.Load<NarrowGaugeSettings>(modEntry)
                ?? new NarrowGaugeSettings();
            NormalizeDebugRenderModes();

            if (!FusePlugin.IsLoaded)
            {
                modEntry.Logger.Error("FUSE Narrow Gauge requires FUSE to be loaded first.");
                return false;
            }

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnUnload = OnUnload;

            Harmony = new Harmony(modEntry.Info.Id);
            Harmony.PatchAll();

            ManagerObject = new GameObject("FUSE Narrow Gauge Manager");
            ManagerObject.AddComponent<NarrowGaugeManager>();
            ManagerObject.AddComponent<SpecialWorkDebugRenderer>();
            ManagerObject.AddComponent<SpecialWorkAdjustmentUI>();

            foreach (string issue in SpecialWorkPresetCatalog.ValidateCatalog())
            {
                Warn("Preset catalog: " + issue);
            }

            Log("Loaded as a FUSE companion module.");
            Log(
                "Use gauge='Narrow', 'DualGauge', 'DualGauge_L', 'DualGauge_R', or 'DualGauge_T' " +
                "on FUSE track segments.");
            Log($"Special-work preset catalog loaded with {SpecialWorkPresetCatalog.Presets.Count} presets.");
            return true;
        }

        static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            Log(value ? "Enabled." : "Disabled.");
            return true;
        }

        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            if (Settings == null)
            {
                return;
            }

            GUILayout.BeginVertical("box");
            GUILayout.Label("FUSE Narrow Gauge Special Work");
            Settings.EnableSpecialWorkTopology = GUILayout.Toggle(
                Settings.EnableSpecialWorkTopology,
                "Enable automatic special-work topology compilation");
            Settings.EnableSpecialWorkHardware = GUILayout.Toggle(
                Settings.EnableSpecialWorkHardware,
                "Enable custom special-work hardware rendering");
            Settings.DebugHideVanillaInSpecialWork = GUILayout.Toggle(
                Settings.DebugHideVanillaInSpecialWork,
                "Hide vanilla switch rails/ties in special-work territory");
            Settings.DebugShowOnlyVanillaTurnoutObjects = GUILayout.Toggle(
                Settings.DebugShowOnlyVanillaTurnoutObjects,
                "DEBUG: show only vanilla turnout objects");
            Settings.DebugShowOnlySpecialWorkObjects = GUILayout.Toggle(
                Settings.DebugShowOnlySpecialWorkObjects,
                "DEBUG: show only SpecialWork objects");
            NormalizeDebugRenderModes();
            Settings.DebugHideCustomRails = GUILayout.Toggle(
                Settings.DebugHideCustomRails,
                "DEBUG: hide custom SpecialWork rails");
            Settings.DebugHideCustomTies = GUILayout.Toggle(
                Settings.DebugHideCustomTies,
                "DEBUG: hide custom SpecialWork ties");
            GUILayout.Label("Validated measured plans replace fixed turnout rails; invalid plans use fallback geometry.");
            Settings.ShowSpecialWorkDebug = GUILayout.Toggle(
                Settings.ShowSpecialWorkDebug,
                "Show special-work debug view");

            if (Settings.ShowSpecialWorkDebug)
            {
                Settings.DebugRoutes = GUILayout.Toggle(Settings.DebugRoutes, "Routes: Blue standard / Cyan narrow");
                Settings.DebugPhysicalRails = GUILayout.Toggle(
                    Settings.DebugPhysicalRails,
                    "Physical rail centerlines: Blue standard / Cyan narrow");
                Settings.DebugSharedRails = GUILayout.Toggle(Settings.DebugSharedRails, "Shared rail intervals: Green");
                Settings.DebugIntersections = GUILayout.Toggle(Settings.DebugIntersections, "Intersections: Orange");
                Settings.DebugIntersectionLabels = GUILayout.Toggle(
                    Settings.DebugIntersectionLabels,
                    "Intersection labels: rail pair / angle / classification");
                Settings.DebugFrogs = GUILayout.Toggle(Settings.DebugFrogs, "Frog candidates: Red");
                Settings.DebugSwitchBlades = GUILayout.Toggle(Settings.DebugSwitchBlades, "Switch blades / stubs: Purple");
                Settings.DebugMeshPlan = GUILayout.Toggle(
                    Settings.DebugMeshPlan,
                    "Calculated mesh plan: cuts / wings / guards / blades");
                Settings.DebugLabelRailRoleSections = GUILayout.Toggle(
                    Settings.DebugLabelRailRoleSections,
                    "Label rail role sections");
                Settings.DebugLabelSuppressionIntervals = GUILayout.Toggle(
                    Settings.DebugLabelSuppressionIntervals,
                    "Label suppression intervals");
                Settings.DebugLabelReplacementPieces = GUILayout.Toggle(
                    Settings.DebugLabelReplacementPieces,
                    "Label replacement pieces");
                Settings.DebugLabelBladeStockRelationships = GUILayout.Toggle(
                    Settings.DebugLabelBladeStockRelationships,
                    "Label blade/stock relationships");
                Settings.DebugLabelFrogOwnership = GUILayout.Toggle(
                    Settings.DebugLabelFrogOwnership,
                    "Label frog ownership");
            }

            int invalid = SpecialWorkRuntimeRegistry.Analyses.Count(analysis => analysis.ValidationIssues.Count > 0);
            int authored = SpecialWorkRuntimeRegistry.Analyses.Count(
                analysis => analysis.Definition.Authoring != null);
            int validPlans = SpecialWorkRuntimeRegistry.Analyses.Count(
                analysis => analysis.MeshPlan?.IsValid == true);
            GUILayout.Label(
                $"Catalog presets: {SpecialWorkPresetCatalog.Presets.Count} | " +
                $"Runtime objects: {SpecialWorkRuntimeRegistry.Analyses.Count} | " +
                $"Valid plans: {validPlans} | Authored: {authored} | Invalid: {invalid}");
            SpecialWorkAdjustmentUI? adjustUI = ManagerObject?.GetComponent<SpecialWorkAdjustmentUI>();
            if (adjustUI != null)
            {
                adjustUI.Visible = GUILayout.Toggle(adjustUI.Visible, "Show live adjustment panel");
            }

            if (GUILayout.Button("Rebuild special-work analysis"))
            {
                NarrowGaugeManager.RequestSynchronization();
            }
            if (GUILayout.Button("Export measured 2D special-work plans"))
            {
                try
                {
                    string path = SpecialWorkPlanExporter.ExportAll();
                    Log("Exported measured 2D special-work plans to " + path);
                }
                catch (System.Exception ex)
                {
                    Error("Could not export special-work plans: " + ex);
                }
            }

            GUILayout.EndVertical();
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            NormalizeDebugRenderModes();
            Settings?.Save(modEntry);
            NarrowGaugeManager.RequestSynchronization();
        }

        private static void NormalizeDebugRenderModes()
        {
            if (Settings == null
                || !Settings.DebugShowOnlyVanillaTurnoutObjects
                || !Settings.DebugShowOnlySpecialWorkObjects)
            {
                return;
            }

            // Contradictory "only" modes previously produced a misleading
            // native/custom hybrid. Prefer the custom diagnostic view.
            Settings.DebugShowOnlyVanillaTurnoutObjects = false;
            Settings.DebugHideVanillaInSpecialWork = true;
        }

        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            if (Harmony != null)
            {
                Harmony.UnpatchAll(modEntry.Info.Id);
                Harmony = null;
            }

            if (ManagerObject != null)
            {
                Object.Destroy(ManagerObject);
                ManagerObject = null;
            }

            ModEntry = null;
            Settings = null;
            Enabled = false;
            return true;
        }

        public static void Log(string message) => ModEntry?.Logger.Log(message);
        public static void Warn(string message) => ModEntry?.Logger.Warning(message);
        public static void Error(string message) => ModEntry?.Logger.Error(message);
    }
}
