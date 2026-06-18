using System;
using System.Collections.Generic;
using System.Linq;
using Model;
using Track;

namespace NarrowGaugeMod
{
    /// <summary>
    /// Keeps the two native switch nodes representing one dual-gauge turnout
    /// mechanically linked. Route mapping is segment-based because the offset
    /// ghost switch may decode its normal and reversed branches in the opposite
    /// order from the authored switch.
    /// </summary>
    internal static class DualGaugeSwitchSynchronizer
    {
        private static bool _synchronizing;
        private static bool _validating;

        public static void SynchronizeFrom(TrackNode changedNode)
        {
            if (_synchronizing
                || changedNode == null
                || !TryResolveCounterpartState(
                    changedNode,
                    changedNode.isThrown,
                    out TrackNode counterpart,
                    out bool counterpartThrown)
                || counterpart.isThrown == counterpartThrown)
            {
                return;
            }

            _synchronizing = true;
            try
            {
                counterpart.isThrown = counterpartThrown;
            }
            finally
            {
                _synchronizing = false;
            }
        }

        public static void SynchronizeAll(Graph graph)
        {
            if (_synchronizing || graph == null)
            {
                return;
            }

            foreach (TrackNode sourceNode in graph.Nodes
                .Where(node => node != null && !NarrowGaugeManager.IsGeneratedGhostNode(node))
                .ToArray())
            {
                SynchronizeFrom(sourceNode);
            }
        }

        public static bool CanSetLinkedSwitch(
            TrainController controller,
            TrackNode changedNode,
            bool changedThrown,
            out Car foundCar)
        {
            foundCar = null!;
            if (_validating
                || controller == null
                || changedNode == null
                || !TryResolveCounterpartState(
                    changedNode,
                    changedThrown,
                    out TrackNode counterpart,
                    out bool counterpartThrown))
            {
                return true;
            }

            _validating = true;
            try
            {
                return controller.CanSetSwitch(counterpart, counterpartThrown, out foundCar);
            }
            finally
            {
                _validating = false;
            }
        }

        internal static bool TryResolveCounterpartState(
            TrackNode changedNode,
            bool changedThrown,
            out TrackNode counterpartNode,
            out bool counterpartThrown)
        {
            counterpartNode = null!;
            counterpartThrown = false;

            Graph graph = Graph.Shared;
            if (graph == null
                || changedNode == null
                || !TryGetCounterpartNode(graph, changedNode, out counterpartNode)
                || !graph.DecodeSwitchAt(
                    changedNode,
                    out TrackSegment changedEnter,
                    out TrackSegment changedNormal,
                    out TrackSegment changedReversed)
                || !graph.DecodeSwitchAt(
                    counterpartNode,
                    out TrackSegment counterpartEnter,
                    out TrackSegment counterpartNormal,
                    out TrackSegment counterpartReversed)
                || !SegmentsAreCounterparts(changedEnter, counterpartEnter))
            {
                counterpartNode = null!;
                return false;
            }

            TrackSegment changedSelected = changedThrown ? changedReversed : changedNormal;
            if (SegmentsAreCounterparts(changedSelected, counterpartNormal))
            {
                counterpartThrown = false;
                return true;
            }

            if (SegmentsAreCounterparts(changedSelected, counterpartReversed))
            {
                counterpartThrown = true;
                return true;
            }

            counterpartNode = null!;
            return false;
        }

        private static bool TryGetCounterpartNode(
            Graph graph,
            TrackNode node,
            out TrackNode counterpart)
        {
            counterpart = null!;
            if (string.IsNullOrWhiteSpace(node.id))
            {
                return false;
            }

            string counterpartId;
            if (GhostGraphSynchronizer.IsGeneratedGhostNodeId(node.id))
            {
                counterpartId = node.id.Substring(GhostGraphSynchronizer.GeneratedNodePrefix.Length);
            }
            else
            {
                counterpartId = GhostGraphSynchronizer.GetGhostNodeId(node.id);
            }

            counterpart = graph.GetNode(counterpartId);
            return counterpart != null;
        }

        private static bool SegmentsAreCounterparts(TrackSegment first, TrackSegment second)
        {
            return first != null
                && second != null
                && DualGaugeLinkRegistry.TryGetCounterpart(first.id, out string counterpartId)
                && string.Equals(counterpartId, second.id, StringComparison.OrdinalIgnoreCase);
        }
    }
}
