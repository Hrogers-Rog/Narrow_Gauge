using HarmonyLib;
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

        /// <summary>True while the mod is enabled.</summary>
        public static bool Enabled { get; private set; }

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;
            NarrowGaugeManager.ReloadGaugeMetadataFromInstalledMods();

            modEntry.OnToggle = OnToggle;

            Harmony = new Harmony(modEntry.Info.Id);
            Harmony.PatchAll();

            new GameObject("NarrowGaugeManager")
                .AddComponent<NarrowGaugeManager>();

            Log("Loaded successfully. Patches applied. Manager spawned.");
            Log(
                "Mark narrow gauge with Gauge='Narrow' in StrangeCustoms graph JSON.");
            return true;
        }

        static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            Log(value ? "Enabled." : "Disabled.");
            return true;
        }

        public static void Log(string message) => ModEntry?.Logger.Log(message);
        public static void Warn(string message) => ModEntry?.Logger.Warning(message);
        public static void Error(string message) => ModEntry?.Logger.Error(message);
    }
}
