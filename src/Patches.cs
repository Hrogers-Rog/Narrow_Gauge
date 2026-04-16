using HarmonyLib;
using System.Linq;
using Track;
using UnityEngine;

namespace NarrowGaugeMod
{
    [HarmonyPatch(typeof(Graph), nameof(Graph.RebuildCollections))]
    static class Patch_Graph_RebuildCollections
    {
        static void Postfix(Graph __instance)
        {
            int segCount = __instance.Segments.Count();
            int nodeCount = __instance.Nodes.Count();
            Main.Log(
                $"[Patch] Graph.RebuildCollections fired. " +
                $"Segments={segCount}  Nodes={nodeCount}");

            NarrowGaugeManager.ScanGraph(__instance);
        }
    }

    [HarmonyPatch(typeof(SwitchGeometry), nameof(SwitchGeometry.Calculate))]
    static class Patch_SwitchGeometry_Calculate
    {
        static bool Prefix(
            TrackNode node,
            SegmentProxy a,
            SegmentProxy b,
            ref SegmentProxy sliceA,
            ref SegmentProxy sliceB,
            ref System.Collections.Generic.List<SegmentProxy> remainder,
            ref SwitchGeometry __result)
        {
            if (!NarrowGaugeManager.IsNarrowGauge(a.Segment)
                || !NarrowGaugeManager.IsNarrowGauge(b.Segment))
                return true;

            __result = NarrowGaugeSwitchGeometry.Calculate(
                node,
                a,
                b,
                NarrowGaugeTrackBuilder.ThreeFootGauge,
                out sliceA,
                out sliceB,
                out remainder);

            return false;
        }
    }

    [HarmonyPatch(typeof(TrackObjectManager), "BuildGameObject")]
    static class Patch_TrackObjectManager_BuildGameObject
    {
        static bool Prefix(
            TrackObjectManager __instance,
            TrackObjectManager.ITrackDescriptor descriptor,
            ref GameObject __result)
        {
            if (!NarrowGaugeTrackBuilder.TryBuild(__instance, descriptor, out GameObject replacement))
                return true;

            __result = replacement;
            return false;
        }
    }
}
