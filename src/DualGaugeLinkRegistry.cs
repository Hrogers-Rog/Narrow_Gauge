using System;
using System.Collections.Generic;
using System.Linq;
using Track;

namespace NarrowGaugeMod
{
    public enum GaugeGraphFamily
    {
        Standard,
        Narrow
    }

    public sealed class DualGaugeSegmentLink
    {
        internal DualGaugeSegmentLink(string standardSegmentId, string narrowSegmentId)
        {
            StandardSegmentId = standardSegmentId;
            NarrowSegmentId = narrowSegmentId;
        }

        public string StandardSegmentId { get; }
        public string NarrowSegmentId { get; }
    }

    /// <summary>
    /// Stable public boundary for systems that need to relate the two native
    /// routes of one dual-gauge segment. The future cross-family coupler should
    /// only bridge segments accepted by CanBridgeCoupling.
    /// </summary>
    public static class DualGaugeLinkRegistry
    {
        private static readonly Dictionary<string, DualGaugeSegmentLink> LinksBySegmentId =
            new Dictionary<string, DualGaugeSegmentLink>(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyCollection<DualGaugeSegmentLink> Links =>
            LinksBySegmentId.Values
                .Distinct()
                .ToArray();

        public static bool TryGetLink(string segmentId, out DualGaugeSegmentLink link)
        {
            if (string.IsNullOrWhiteSpace(segmentId))
            {
                link = null!;
                return false;
            }

            return LinksBySegmentId.TryGetValue(segmentId, out link);
        }

        public static bool TryGetCounterpart(string segmentId, out string counterpartSegmentId)
        {
            counterpartSegmentId = string.Empty;
            if (!TryGetLink(segmentId, out DualGaugeSegmentLink link))
            {
                return false;
            }

            counterpartSegmentId = string.Equals(
                segmentId,
                link.StandardSegmentId,
                StringComparison.OrdinalIgnoreCase)
                ? link.NarrowSegmentId
                : link.StandardSegmentId;
            return true;
        }

        public static GaugeGraphFamily GetFamily(string segmentId)
        {
            if (TryGetLink(segmentId, out DualGaugeSegmentLink link)
                && string.Equals(segmentId, link.NarrowSegmentId, StringComparison.OrdinalIgnoreCase))
            {
                return GaugeGraphFamily.Narrow;
            }

            return GaugeGraphFamily.Standard;
        }

        public static bool CanBridgeCoupling(TrackSegment first, TrackSegment second)
        {
            if (first == null || second == null)
            {
                return false;
            }

            if (!TryGetLink(first.id, out DualGaugeSegmentLink link)
                || !string.Equals(
                    string.Equals(first.id, link.StandardSegmentId, StringComparison.OrdinalIgnoreCase)
                        ? link.NarrowSegmentId
                        : link.StandardSegmentId,
                    second.id,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            TrackSegment standard = string.Equals(first.id, link.StandardSegmentId, StringComparison.OrdinalIgnoreCase)
                ? first
                : second;
            TrackSegment narrow = standard == first ? second : first;

            return NarrowGaugeManager.IsDualGauge(standard)
                && NarrowGaugeManager.IsGeneratedGhost(narrow)
                && string.Equals(
                    narrow.a?.id,
                    GhostGraphSynchronizer.GetGhostNodeId(standard.a?.id ?? string.Empty),
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    narrow.b?.id,
                    GhostGraphSynchronizer.GetGhostNodeId(standard.b?.id ?? string.Empty),
                    StringComparison.OrdinalIgnoreCase);
        }

        internal static void Replace(IEnumerable<DualGaugeSegmentLink> links)
        {
            LinksBySegmentId.Clear();

            foreach (DualGaugeSegmentLink link in links ?? Enumerable.Empty<DualGaugeSegmentLink>())
            {
                LinksBySegmentId[link.StandardSegmentId] = link;
                LinksBySegmentId[link.NarrowSegmentId] = link;
            }
        }

        internal static void Clear()
        {
            LinksBySegmentId.Clear();
        }
    }
}
