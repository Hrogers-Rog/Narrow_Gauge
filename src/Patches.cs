using HarmonyLib;
using System.Linq;
using Model;
using Track;
using UnityEngine;

namespace NarrowGaugeMod
{
    [HarmonyPatch(typeof(Graph), nameof(Graph.RebuildCollections))]
    static class Patch_Graph_RebuildCollections
    {
        static void Postfix(Graph __instance)
        {
            NarrowGaugeManager.ScanGraph(__instance);
        }
    }

    [HarmonyPatch(typeof(TrackNode), "set_isThrown")]
    static class Patch_TrackNode_SetIsThrown
    {
        static void Postfix(TrackNode __instance)
        {
            DualGaugeSwitchSynchronizer.SynchronizeFrom(__instance);
        }
    }

    [HarmonyPatch(typeof(TrainController), nameof(TrainController.CanSetSwitch))]
    static class Patch_TrainController_CanSetSwitch
    {
        static void Postfix(
            TrainController __instance,
            TrackNode node,
            bool thrown,
            ref Car foundCar,
            ref bool __result)
        {
            if (!__result
                || DualGaugeSwitchSynchronizer.CanSetLinkedSwitch(
                    __instance,
                    node,
                    thrown,
                    out Car linkedFoundCar))
            {
                return;
            }

            foundCar = linkedFoundCar;
            __result = false;
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
            bool aHiddenControl = SpecialWorkTopologySynchronizer.IsHiddenControlSegment(a.Segment);
            bool bHiddenControl = SpecialWorkTopologySynchronizer.IsHiddenControlSegment(b.Segment);
            if (SpecialWorkTopologySynchronizer.IsGaugeSeparationControlNode(node)
                && aHiddenControl != bHiddenControl)
            {
                __result = NarrowGaugeSwitchGeometry.CalculateControlShell(
                    node,
                    a,
                    b,
                    out sliceA,
                    out sliceB,
                    out remainder);
                return false;
            }

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

    [HarmonyPatch(typeof(TrackObjectManager), "BuildMaskObject")]
    static class Patch_TrackObjectManager_BuildMaskObject
    {
        static bool Prefix(
            TrackObjectManager __instance,
            TrackObjectManager.ITrackDescriptor descriptor,
            ref GameObject __result)
        {
            if (!NarrowGaugeTrackBuilder.TryBuildMask(__instance, descriptor, out GameObject replacement))
                return true;

            __result = replacement;
            return false;
        }
    }
}
