using UnityModManagerNet;

namespace NarrowGaugeMod
{
    public sealed class NarrowGaugeSettings : UnityModManager.ModSettings
    {
        public bool EnableSpecialWorkTopology = true;
        public bool EnableSpecialWorkHardware = true;
        public bool DebugShowOnlyVanillaTurnoutObjects;
        public bool DebugShowOnlySpecialWorkObjects;
        public bool DebugHideCustomRails;
        public bool DebugHideCustomTies;
        public bool DebugHideVanillaInSpecialWork = true;
        public bool DebugLabelRailRoleSections = true;
        public bool DebugLabelSuppressionIntervals = true;
        public bool DebugLabelReplacementPieces = true;
        public bool DebugLabelBladeStockRelationships = true;
        public bool DebugLabelFrogOwnership = true;
        public bool ShowSpecialWorkDebug;
        public bool DebugRoutes = true;
        public bool DebugPhysicalRails = true;
        public bool DebugSharedRails = true;
        public bool DebugIntersections = true;
        public bool DebugIntersectionLabels = true;
        public bool DebugFrogs = true;
        public bool DebugSwitchBlades = true;
        public bool DebugMeshPlan = true;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}
