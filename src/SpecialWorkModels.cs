using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Newtonsoft.Json.Linq;
using Track;
using UnityEngine;

namespace NarrowGaugeMod
{
    [Flags]
    internal enum GaugeAvailability
    {
        None = 0,
        Standard = 1,
        Narrow = 2,
        Dual = Standard | Narrow
    }

    internal enum SpecialWorkCategory
    {
        Turnout,
        DualGauge,
        Transition,
        Crossing,
        Slip,
        StubSwitch,
        ThreeWay
    }

    internal enum NativeTopologyRecipe
    {
        FixedRoutes,
        BinarySwitch,
        TwoFamilyBinarySwitches,
        StagedThreeWay,
        TwoFamilyStagedThreeWay,
        FixedTransition,
        PlainCrossing,
        SingleSlip,
        DoubleSlip
    }

    internal enum SpecialWorkAnchorKind
    {
        Node,
        Segment
    }

    internal enum MovableAssemblyKind
    {
        PointBlade,
        StubEnd
    }

    internal enum SharedRailRequirement
    {
        None,
        RouteShared,
        GaugeShared,
        RouteAndGaugeShared
    }

    internal enum RailSide
    {
        Left,
        Right
    }

    internal enum RailPieceKind
    {
        FixedRunning,
        SharedRunning,
        FrogNose,
        WingRail,
        GuardRail,
        ClosureRail,
        MovableBlade
    }

    internal enum RailRole
    {
        StockRail,
        PointBlade,
        MovablePointBlade,
        FixedRunningRail,
        ClosureRail,
        FrogApproachRail,
        FrogRail,
        WingRail,
        GuardRail,
        SharedRail,
        SuppressedRail,
        CrossingRail,
        ExitRail,
        Unknown
    }

    internal enum RailCutKind
    {
        SharedDuplicate,
        FrogGap,
        SwitchBlade,
        OwnershipConflict
    }

    internal enum FrogHandedness
    {
        Left,
        Right
    }

    internal enum RailIntersectionKind
    {
        Unclassified,
        SharedOverlap,
        RouteJoin,
        BladeConvergence,
        VeeFrogCandidate,
        CrossingFrogCandidate,
        VeeFrog = VeeFrogCandidate,
        CrossingFrog = CrossingFrogCandidate,
        InvalidShallowCrossing
    }

    internal readonly struct CountRange
    {
        public CountRange(int minimum, int? maximum = null)
        {
            Minimum = minimum;
            Maximum = maximum;
        }

        public int Minimum { get; }
        public int? Maximum { get; }

        public bool Contains(int value)
        {
            return value >= Minimum && (!Maximum.HasValue || value <= Maximum.Value);
        }

        public override string ToString()
        {
            if (!Maximum.HasValue)
            {
                return Minimum + "+";
            }

            return Minimum == Maximum.Value
                ? Minimum.ToString()
                : $"{Minimum}-{Maximum.Value}";
        }
    }

    internal sealed class GeometryExpectation
    {
        public GeometryExpectation(
            CountRange frogs,
            CountRange guards,
            SharedRailRequirement sharedRails,
            bool deriveFrogsFromIntersections,
            int movableAssemblies)
        {
            ExpectedFrogs = frogs;
            ExpectedGuardRails = guards;
            SharedRails = sharedRails;
            DeriveFrogsFromIntersections = deriveFrogsFromIntersections;
            ExpectedMovableAssemblies = movableAssemblies;
        }

        public CountRange ExpectedFrogs { get; }
        public CountRange ExpectedGuardRails { get; }
        public SharedRailRequirement SharedRails { get; }
        public bool DeriveFrogsFromIntersections { get; }
        public int ExpectedMovableAssemblies { get; }
    }

    internal sealed class SpecialWorkPresetDefinition
    {
        public SpecialWorkPresetDefinition(
            string id,
            string displayName,
            SpecialWorkCategory category,
            NativeTopologyRecipe topology,
            int nativeSwitchNodes,
            int logicalRoutes,
            GaugeAvailability requiredFamilies,
            bool supportsGhostGraph,
            GeometryExpectation geometry,
            params string[] aliases)
            : this(
                id,
                displayName,
                category,
                topology,
                nativeSwitchNodes,
                logicalRoutes,
                requiredFamilies,
                supportsGhostGraph,
                SpecialWorkAnchorKind.Node,
                geometry,
                aliases)
        {
        }

        public SpecialWorkPresetDefinition(
            string id,
            string displayName,
            SpecialWorkCategory category,
            NativeTopologyRecipe topology,
            int nativeSwitchNodes,
            int logicalRoutes,
            GaugeAvailability requiredFamilies,
            bool supportsGhostGraph,
            SpecialWorkAnchorKind anchorKind,
            GeometryExpectation geometry,
            params string[] aliases)
        {
            Id = id;
            DisplayName = displayName;
            Category = category;
            Topology = topology;
            NativeSwitchNodes = nativeSwitchNodes;
            LogicalRoutes = logicalRoutes;
            RequiredFamilies = requiredFamilies;
            SupportsGhostGraph = supportsGhostGraph;
            AnchorKind = anchorKind;
            Geometry = geometry;
            Aliases = aliases ?? Array.Empty<string>();
        }

        public string Id { get; }
        public string DisplayName { get; }
        public SpecialWorkCategory Category { get; }
        public NativeTopologyRecipe Topology { get; }
        public int NativeSwitchNodes { get; }
        public int LogicalRoutes { get; }
        public GaugeAvailability RequiredFamilies { get; }
        public bool SupportsGhostGraph { get; }
        public SpecialWorkAnchorKind AnchorKind { get; }
        public GeometryExpectation Geometry { get; }
        public IReadOnlyList<string> Aliases { get; }
    }

    internal sealed class SpecialWorkPort
    {
        public SpecialWorkPort(
            string id,
            GaugeAvailability availableFamilies,
            TrackNode node)
        {
            Id = id;
            AvailableFamilies = availableFamilies;
            Node = node;
        }

        public string Id { get; }
        public GaugeAvailability AvailableFamilies { get; }
        public TrackNode Node { get; }
        public Vector3 Position => Node != null ? Node.transform.localPosition : Vector3.zero;
    }

    internal sealed class LogicalRoute
    {
        public LogicalRoute(
            string id,
            GaugeGraphFamily family,
            LineCurve centerline,
            IEnumerable<string> sourceSegmentIds,
            string? switchGroupId = null,
            string? requiredStateId = null)
        {
            Id = id;
            Family = family;
            Centerline = centerline;
            SourceSegmentIds = (sourceSegmentIds ?? Enumerable.Empty<string>()).ToArray();
            SwitchGroupId = switchGroupId;
            RequiredStateId = requiredStateId;
        }

        public string Id { get; }
        public GaugeGraphFamily Family { get; }
        public LineCurve Centerline { get; }
        public IReadOnlyList<string> SourceSegmentIds { get; }
        public string? SwitchGroupId { get; }
        public string? RequiredStateId { get; }
    }

    internal sealed class SpecialWorkSwitchGroup
    {
        public SpecialWorkSwitchGroup(
            string id,
            IEnumerable<string> nativeNodeIds,
            IEnumerable<string> legalStateIds)
        {
            Id = id;
            NativeNodeIds = (nativeNodeIds ?? Enumerable.Empty<string>()).ToArray();
            LegalStateIds = (legalStateIds ?? Enumerable.Empty<string>()).ToArray();
        }

        public string Id { get; }
        public IReadOnlyList<string> NativeNodeIds { get; }
        public IReadOnlyList<string> LegalStateIds { get; }
    }

    internal sealed class SpecialWorkDefinition
    {
        public SpecialWorkDefinition(
            string id,
            SpecialWorkPresetDefinition preset,
            IEnumerable<SpecialWorkPort> ports,
            IEnumerable<LogicalRoute> routes,
            IEnumerable<SpecialWorkSwitchGroup> switchGroups,
            IEnumerable<string> nativeSwitchNodeIds,
            SpecialWorkAuthoringBinding? authoring = null)
        {
            Id = id;
            Preset = preset;
            Ports = (ports ?? Enumerable.Empty<SpecialWorkPort>()).ToArray();
            Routes = (routes ?? Enumerable.Empty<LogicalRoute>()).ToArray();
            SwitchGroups = (switchGroups ?? Enumerable.Empty<SpecialWorkSwitchGroup>()).ToArray();
            NativeSwitchNodeIds = (nativeSwitchNodeIds ?? Enumerable.Empty<string>()).ToArray();
            Authoring = authoring;
        }

        public string Id { get; }
        public SpecialWorkPresetDefinition Preset { get; }
        public IReadOnlyList<SpecialWorkPort> Ports { get; }
        public IReadOnlyList<LogicalRoute> Routes { get; }
        public IReadOnlyList<SpecialWorkSwitchGroup> SwitchGroups { get; }
        public IReadOnlyList<string> NativeSwitchNodeIds { get; }
        public SpecialWorkAuthoringBinding? Authoring { get; }
    }

    internal sealed class SpecialWorkAuthoringBinding
    {
        public SpecialWorkAuthoringBinding(
            string packageId,
            string id,
            string presetId,
            string anchorNodeId,
            bool enabled,
            IReadOnlyDictionary<string, JToken>? parameters)
        {
            PackageId = packageId;
            Id = id;
            PresetId = presetId;
            AnchorNodeId = anchorNodeId;
            Enabled = enabled;
            Parameters = parameters ?? new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
        }

        public string PackageId { get; }
        public string Id { get; }
        public string PresetId { get; }
        public string AnchorNodeId { get; }
        public bool Enabled { get; }
        public IReadOnlyDictionary<string, JToken> Parameters { get; }
    }

    internal sealed class RailCenterline
    {
        public RailCenterline(
            string id,
            GaugeGraphFamily family,
            RailSide side,
            LineCurve curve,
            IEnumerable<string> sourceRouteIds,
            RailRole role = RailRole.Unknown,
            string? wheelPathId = null,
            string? startPortId = null,
            string? endPortId = null)
        {
            Id = id;
            Family = family;
            Side = side;
            Curve = curve;
            Role = role;
            WheelPathId = wheelPathId ?? string.Empty;
            StartPortId = startPortId ?? string.Empty;
            EndPortId = endPortId ?? string.Empty;
            SourceRouteIds = new HashSet<string>(
                sourceRouteIds ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            Samples = Array.Empty<RailSample>();
            Segments2D = Array.Empty<RailSegment2D>();
            Sections = Array.Empty<RailRoleSection>();
            Suppressions = Array.Empty<RailSuppressionInterval>();
        }

        public string Id { get; }
        public GaugeGraphFamily Family { get; }
        public RailSide Side { get; }
        public LineCurve Curve { get; }
        public RailRole Role { get; private set; }
        public string WheelPathId { get; }
        public string StartPortId { get; }
        public string EndPortId { get; }
        public IReadOnlyCollection<string> SourceRouteIds { get; }
        public IReadOnlyList<RailSample> Samples { get; private set; }
        public IReadOnlyList<RailSegment2D> Segments2D { get; private set; }
        public IReadOnlyList<RailRoleSection> Sections { get; private set; }
        public IReadOnlyList<RailSuppressionInterval> Suppressions { get; private set; }

        public void SetRole(RailRole role)
        {
            Role = role;
        }

        public void SetProjection(
            IEnumerable<RailSample> samples,
            IEnumerable<RailSegment2D> segments)
        {
            Samples = (samples ?? Enumerable.Empty<RailSample>()).ToArray();
            Segments2D = (segments ?? Enumerable.Empty<RailSegment2D>()).ToArray();
        }

        public void SetSections(IEnumerable<RailRoleSection> sections)
        {
            Sections = (sections ?? Enumerable.Empty<RailRoleSection>())
                .OrderBy(section => section.StartDistance)
                .ThenBy(section => section.EndDistance)
                .ToArray();
        }

        public void SetSuppressions(IEnumerable<RailSuppressionInterval> suppressions)
        {
            Suppressions = (suppressions ?? Enumerable.Empty<RailSuppressionInterval>())
                .OrderBy(suppression => suppression.StartDistance)
                .ThenBy(suppression => suppression.EndDistance)
                .ToArray();
        }
    }

    internal sealed class PhysicalRail
    {
        public PhysicalRail(RailCenterline rail)
        {
            Rail = rail;
        }

        public RailCenterline Rail { get; }
    }

    internal sealed class RailRoleSection
    {
        public RailRoleSection(
            string id,
            RailCenterline rail,
            RailRole role,
            float startDistance,
            float endDistance,
            string ownerRouteId,
            GaugeGraphFamily ownerFamily,
            string sourceCurveKind,
            string reason)
        {
            Id = id;
            Rail = rail;
            Role = role;
            StartDistance = Mathf.Max(0f, Mathf.Min(startDistance, endDistance));
            EndDistance = Mathf.Min(rail.Curve.Length, Mathf.Max(startDistance, endDistance));
            OwnerRouteId = ownerRouteId;
            OwnerFamily = ownerFamily;
            SourceCurveKind = sourceCurveKind;
            Reason = reason;
        }

        public string Id { get; }
        public RailCenterline Rail { get; }
        public RailRole Role { get; }
        public float StartDistance { get; }
        public float EndDistance { get; }
        public string OwnerRouteId { get; }
        public GaugeGraphFamily OwnerFamily { get; }
        public string SourceCurveKind { get; }
        public string Reason { get; }
        public float Length => EndDistance - StartDistance;
    }

    internal sealed class RailSuppressionInterval
    {
        public RailSuppressionInterval(
            string id,
            RailCenterline rail,
            float startDistance,
            float endDistance,
            string reason)
        {
            Id = id;
            Rail = rail;
            StartDistance = Mathf.Max(0f, Mathf.Min(startDistance, endDistance));
            EndDistance = Mathf.Min(rail.Curve.Length, Mathf.Max(startDistance, endDistance));
            Reason = reason;
        }

        public string Id { get; }
        public RailCenterline Rail { get; }
        public float StartDistance { get; }
        public float EndDistance { get; }
        public string Reason { get; }
        public float Length => EndDistance - StartDistance;
    }

    internal sealed class RailOwnershipPlan
    {
        public RailOwnershipPlan(
            IEnumerable<RailCenterline> rails,
            IEnumerable<RailRoleSection> sections,
            IEnumerable<RailSuppressionInterval> suppressions)
        {
            Rails = (rails ?? Enumerable.Empty<RailCenterline>()).ToArray();
            Sections = (sections ?? Enumerable.Empty<RailRoleSection>()).ToArray();
            Suppressions = (suppressions ?? Enumerable.Empty<RailSuppressionInterval>()).ToArray();
        }

        public IReadOnlyList<RailCenterline> Rails { get; }
        public IReadOnlyList<RailRoleSection> Sections { get; }
        public IReadOnlyList<RailSuppressionInterval> Suppressions { get; }
    }

    internal sealed class WheelPath
    {
        public WheelPath(
            string id,
            string routeId,
            GaugeGraphFamily family,
            string startPortId,
            string endPortId,
            LineCurve centerline,
            LineCurve leftFlangeGuide,
            LineCurve rightFlangeGuide,
            string leftRailId,
            string rightRailId,
            string? switchGroupId,
            string? requiredStateId)
        {
            Id = id;
            RouteId = routeId;
            Family = family;
            StartPortId = startPortId;
            EndPortId = endPortId;
            Centerline = centerline;
            LeftFlangeGuide = leftFlangeGuide;
            RightFlangeGuide = rightFlangeGuide;
            LeftRailId = leftRailId;
            RightRailId = rightRailId;
            SwitchGroupId = switchGroupId;
            RequiredStateId = requiredStateId;
        }

        public string Id { get; }
        public string RouteId { get; }
        public GaugeGraphFamily Family { get; }
        public string StartPortId { get; }
        public string EndPortId { get; }
        public LineCurve Centerline { get; }
        public LineCurve LeftFlangeGuide { get; }
        public LineCurve RightFlangeGuide { get; }
        public string LeftRailId { get; }
        public string RightRailId { get; }
        public string? SwitchGroupId { get; }
        public string? RequiredStateId { get; }

        public string RailId(RailSide side)
        {
            return side == RailSide.Left ? LeftRailId : RightRailId;
        }

        public LineCurve FlangeGuide(RailSide side)
        {
            return side == RailSide.Left ? LeftFlangeGuide : RightFlangeGuide;
        }
    }

    internal readonly struct RailSample
    {
        public RailSample(
            int index,
            float distance,
            Vector2 localPoint,
            Vector3 worldPoint)
        {
            Index = index;
            Distance = distance;
            LocalPoint = localPoint;
            WorldPoint = worldPoint;
        }

        public int Index { get; }
        public float Distance { get; }
        public Vector2 LocalPoint { get; }
        public Vector3 WorldPoint { get; }
    }

    internal sealed class RailSegment2D
    {
        public RailSegment2D(
            RailCenterline rail,
            int index,
            RailSample start,
            RailSample end)
        {
            Rail = rail;
            Index = index;
            Start = start;
            End = end;
        }

        public RailCenterline Rail { get; }
        public int Index { get; }
        public RailSample Start { get; }
        public RailSample End { get; }
        public Vector2 Delta => End.LocalPoint - Start.LocalPoint;
        public float Length => Delta.magnitude;
        public Vector2 Direction => Length > 0.00001f ? Delta / Length : Vector2.zero;
    }

    internal sealed class RailProjectionFrame
    {
        public RailProjectionFrame(Vector3 origin, Vector3 right, Vector3 forward)
        {
            Origin = origin;
            Right = Flatten(right).normalized;
            Forward = Flatten(forward).normalized;
        }

        public static RailProjectionFrame Identity { get; } =
            new RailProjectionFrame(Vector3.zero, Vector3.right, Vector3.forward);

        public Vector3 Origin { get; }
        public Vector3 Right { get; }
        public Vector3 Forward { get; }

        public Vector2 Project(Vector3 point)
        {
            Vector3 delta = point - Origin;
            return new Vector2(
                Vector3.Dot(delta, Right),
                Vector3.Dot(delta, Forward));
        }

        public Vector2 ProjectDirection(Vector3 direction)
        {
            return new Vector2(
                Vector3.Dot(direction, Right),
                Vector3.Dot(direction, Forward)).normalized;
        }

        public Vector3 RestoreDirection(Vector2 direction)
        {
            return (Right * direction.x + Forward * direction.y).normalized;
        }

        private static Vector3 Flatten(Vector3 value)
        {
            return new Vector3(value.x, 0f, value.z);
        }
    }

    internal readonly struct SharedRailInterval
    {
        public SharedRailInterval(Vector3 start, Vector3 end)
            : this(string.Empty, string.Empty, Vector2.zero, Vector2.zero, start, end)
        {
        }

        public SharedRailInterval(
            string railAId,
            string railBId,
            Vector2 localStart,
            Vector2 localEnd,
            Vector3 start,
            Vector3 end)
        {
            RailAId = railAId;
            RailBId = railBId;
            LocalStart = localStart;
            LocalEnd = localEnd;
            Start = start;
            End = end;
        }

        public string RailAId { get; }
        public string RailBId { get; }
        public Vector2 LocalStart { get; }
        public Vector2 LocalEnd { get; }
        public Vector3 Start { get; }
        public Vector3 End { get; }
    }

    internal sealed class RailIntersection
    {
        public RailIntersection(
            string id,
            RailCenterline railA,
            RailCenterline railB,
            float distanceA,
            float distanceB,
            Vector3 position,
            Vector3 tangentA,
            Vector3 tangentB,
            float acuteAngleDegrees,
            RailIntersectionKind kind)
            : this(
                id,
                railA,
                railB,
                distanceA,
                distanceB,
                new Vector2(position.x, position.z),
                position,
                new Vector2(tangentA.x, tangentA.z).normalized,
                new Vector2(tangentB.x, tangentB.z).normalized,
                tangentA,
                tangentB,
                acuteAngleDegrees,
                kind)
        {
        }

        public RailIntersection(
            string id,
            RailCenterline railA,
            RailCenterline railB,
            float distanceA,
            float distanceB,
            Vector2 localPoint,
            Vector3 position,
            Vector2 tangentA2D,
            Vector2 tangentB2D,
            Vector3 tangentA,
            Vector3 tangentB,
            float acuteAngleDegrees,
            RailIntersectionKind kind)
        {
            Id = id;
            RailA = railA;
            RailB = railB;
            DistanceA = distanceA;
            DistanceB = distanceB;
            LocalPoint = localPoint;
            Position = position;
            TangentA2D = tangentA2D;
            TangentB2D = tangentB2D;
            TangentA = tangentA;
            TangentB = tangentB;
            AcuteAngleDegrees = acuteAngleDegrees;
            Kind = kind;
        }

        public string Id { get; }
        public RailCenterline RailA { get; }
        public RailCenterline RailB { get; }
        public float DistanceA { get; }
        public float DistanceB { get; }
        public Vector2 LocalPoint { get; }
        public Vector3 Position { get; }
        public Vector2 TangentA2D { get; }
        public Vector2 TangentB2D { get; }
        public Vector3 TangentA { get; }
        public Vector3 TangentB { get; }
        public float AcuteAngleDegrees { get; }
        public RailIntersectionKind Kind { get; set; }
    }

    internal sealed class FrogCandidate
    {
        public FrogCandidate(string id, RailIntersection intersection, Vector3 noseDirection)
            : this(
                id,
                intersection,
                noseDirection,
                -noseDirection,
                FrogHandedness.Right,
                0f,
                0f,
                0f,
                string.Empty,
                string.Empty,
                string.Empty)
        {
        }

        public FrogCandidate(
            string id,
            RailIntersection intersection,
            Vector3 noseDirection,
            Vector3 heelDirection,
            FrogHandedness handedness,
            float railHeadSetback,
            float flangewaySetback,
            float cutHalfLength,
            string ownerRouteId = "",
            string crossingRouteId = "",
            string protectedRouteId = "")
        {
            Id = id;
            Intersection = intersection;
            NoseDirection = noseDirection;
            HeelDirection = heelDirection;
            Handedness = handedness;
            RailHeadSetback = railHeadSetback;
            FlangewaySetback = flangewaySetback;
            CutHalfLength = cutHalfLength;
            OwnerRouteId = ownerRouteId ?? string.Empty;
            CrossingRouteId = crossingRouteId ?? string.Empty;
            ProtectedRouteId = protectedRouteId ?? string.Empty;
        }

        public string Id { get; }
        public RailIntersection Intersection { get; }
        public Vector3 NoseDirection { get; }
        public Vector3 HeelDirection { get; }
        public FrogHandedness Handedness { get; }
        public float RailHeadSetback { get; }
        public float FlangewaySetback { get; }
        public float CutHalfLength { get; }
        public string OwnerRouteId { get; }
        public string CrossingRouteId { get; }
        public string ProtectedRouteId { get; }
    }

    internal sealed class RailCut
    {
        public RailCut(
            string id,
            RailCenterline rail,
            float startDistance,
            float endDistance,
            RailCutKind kind,
            string sourceId)
        {
            Id = id;
            Rail = rail;
            StartDistance = Mathf.Max(0f, Mathf.Min(startDistance, endDistance));
            EndDistance = Mathf.Min(rail.Curve.Length, Mathf.Max(startDistance, endDistance));
            Kind = kind;
            SourceId = sourceId;
        }

        public string Id { get; }
        public RailCenterline Rail { get; }
        public float StartDistance { get; }
        public float EndDistance { get; }
        public RailCutKind Kind { get; }
        public string SourceId { get; }
        public float Length => EndDistance - StartDistance;
    }

    internal sealed class RailPiece
    {
        public RailPiece(
            string id,
            string sourceRailId,
            RailPieceKind kind,
            LineCurve curve,
            float startDistance,
            float endDistance,
            string? sourcePlanId = null)
        {
            Id = id;
            SourceRailId = sourceRailId;
            Kind = kind;
            Curve = curve;
            StartDistance = startDistance;
            EndDistance = endDistance;
            SourcePlanId = sourcePlanId;
        }

        public string Id { get; }
        public string SourceRailId { get; }
        public RailPieceKind Kind { get; }
        public LineCurve Curve { get; }
        public float StartDistance { get; }
        public float EndDistance { get; }
        public string? SourcePlanId { get; }
    }

    internal sealed class WingRailPlan
    {
        public WingRailPlan(
            string id,
            string frogId,
            RailCenterline sourceRail,
            LineCurve curve,
            bool approachSide)
        {
            Id = id;
            FrogId = frogId;
            SourceRail = sourceRail;
            Curve = curve;
            ApproachSide = approachSide;
        }

        public string Id { get; }
        public string FrogId { get; }
        public RailCenterline SourceRail { get; }
        public LineCurve Curve { get; }
        public bool ApproachSide { get; }
    }

    internal sealed class GuardRailPlan
    {
        public GuardRailPlan(
            string id,
            string frogId,
            string protectedRouteId,
            RailCenterline oppositeRunningRail,
            LineCurve curve)
        {
            Id = id;
            FrogId = frogId;
            ProtectedRouteId = protectedRouteId;
            OppositeRunningRail = oppositeRunningRail;
            Curve = curve;
        }

        public string Id { get; }
        public string FrogId { get; }
        public string ProtectedRouteId { get; }
        public RailCenterline OppositeRunningRail { get; }
        public LineCurve Curve { get; }
    }

    internal sealed class SwitchBladePlan
    {
        public SwitchBladePlan(
            string id,
            string? nativeNodeId,
            string? switchGroupId,
            RailCenterline stockRail,
            RailCenterline movableRail,
            float tipDistance,
            float rootDistance,
            LineCurve bladeCurve,
            LineCurve closureCurve)
        {
            Id = id;
            NativeNodeId = nativeNodeId;
            SwitchGroupId = switchGroupId;
            StockRail = stockRail;
            MovableRail = movableRail;
            TipDistance = tipDistance;
            RootDistance = rootDistance;
            BladeCurve = bladeCurve;
            ClosureCurve = closureCurve;
        }

        public string Id { get; }
        public string? NativeNodeId { get; }
        public string? SwitchGroupId { get; }
        public RailCenterline StockRail { get; }
        public RailCenterline MovableRail { get; }
        public float TipDistance { get; }
        public float RootDistance { get; }
        public LineCurve BladeCurve { get; }
        public LineCurve ClosureCurve { get; }
    }

    internal sealed class GeometryDebugLabel
    {
        public GeometryDebugLabel(
            string text,
            Vector3 position,
            Color color,
            LineCurve? referenceCurve = null)
        {
            Text = text;
            Position = position;
            Color = color;
            ReferenceCurve = referenceCurve;
        }

        public string Text { get; }
        public Vector3 Position { get; }
        public Color Color { get; }
        public LineCurve? ReferenceCurve { get; }
    }

    internal sealed class SpecialWorkMeshPlan
    {
        public SpecialWorkMeshPlan(
            SpecialWorkGeometryParameters parameters,
            IEnumerable<RailWorkInterval> workIntervals,
            IEnumerable<RailOwnershipInterval> ownershipIntervals,
            RailOwnershipPlan? railOwnershipPlan,
            IEnumerable<RailPiece> fixedRunningRails,
            IEnumerable<RailCut> cuts,
            IEnumerable<FrogCandidate> frogs,
            IEnumerable<RailPiece> frogPieces,
            IEnumerable<WingRailPlan> wingRails,
            IEnumerable<GuardRailPlan> guardRails,
            IEnumerable<SwitchBladePlan> switchBlades,
            IEnumerable<GeometryDebugLabel> debugLabels,
            IEnumerable<string> validationIssues)
        {
            Parameters = parameters;
            WorkIntervals = (workIntervals ?? Enumerable.Empty<RailWorkInterval>()).ToArray();
            OwnershipIntervals = (ownershipIntervals ?? Enumerable.Empty<RailOwnershipInterval>()).ToArray();
            RailOwnershipPlan = railOwnershipPlan;
            FixedRunningRails = (fixedRunningRails ?? Enumerable.Empty<RailPiece>()).ToArray();
            Cuts = (cuts ?? Enumerable.Empty<RailCut>()).ToArray();
            Frogs = (frogs ?? Enumerable.Empty<FrogCandidate>()).ToArray();
            FrogPieces = (frogPieces ?? Enumerable.Empty<RailPiece>()).ToArray();
            WingRails = (wingRails ?? Enumerable.Empty<WingRailPlan>()).ToArray();
            GuardRails = (guardRails ?? Enumerable.Empty<GuardRailPlan>()).ToArray();
            SwitchBlades = (switchBlades ?? Enumerable.Empty<SwitchBladePlan>()).ToArray();
            DebugLabels = (debugLabels ?? Enumerable.Empty<GeometryDebugLabel>()).ToArray();
            ValidationIssues = (validationIssues ?? Enumerable.Empty<string>()).ToArray();
        }

        public SpecialWorkGeometryParameters Parameters { get; }
        public IReadOnlyList<RailWorkInterval> WorkIntervals { get; }
        public IReadOnlyList<RailOwnershipInterval> OwnershipIntervals { get; }
        public RailOwnershipPlan? RailOwnershipPlan { get; }
        public IReadOnlyList<RailPiece> FixedRunningRails { get; }
        public IReadOnlyList<RailCut> Cuts { get; }
        public IReadOnlyList<FrogCandidate> Frogs { get; }
        public IReadOnlyList<RailPiece> FrogPieces { get; }
        public IReadOnlyList<WingRailPlan> WingRails { get; }
        public IReadOnlyList<GuardRailPlan> GuardRails { get; }
        public IReadOnlyList<SwitchBladePlan> SwitchBlades { get; }
        public IReadOnlyList<GeometryDebugLabel> DebugLabels { get; }
        public IReadOnlyList<string> ValidationIssues { get; }
        public bool IsValid => ValidationIssues.Count == 0;
    }

    internal sealed class RailWorkInterval
    {
        public RailWorkInterval(
            RailCenterline rail,
            float startDistance,
            float endDistance)
        {
            Rail = rail;
            StartDistance = Mathf.Max(0f, Mathf.Min(startDistance, endDistance));
            EndDistance = Mathf.Min(rail.Curve.Length, Mathf.Max(startDistance, endDistance));
        }

        public RailCenterline Rail { get; }
        public float StartDistance { get; }
        public float EndDistance { get; }
        public float Length => EndDistance - StartDistance;
    }

    internal sealed class SpecialWorkValidationResult
    {
        public SpecialWorkValidationResult(IEnumerable<string> issues)
        {
            Issues = (issues ?? Enumerable.Empty<string>()).ToArray();
        }

        public IReadOnlyList<string> Issues { get; }
        public bool IsValid => Issues.Count == 0;
    }

    internal sealed class NativeTopologyPlan
    {
        public NativeTopologyPlan(
            IEnumerable<string> nativeSwitchNodeIds,
            IEnumerable<string> issues)
        {
            NativeSwitchNodeIds = (nativeSwitchNodeIds ?? Enumerable.Empty<string>()).ToArray();
            Issues = (issues ?? Enumerable.Empty<string>()).ToArray();
        }

        public IReadOnlyList<string> NativeSwitchNodeIds { get; }
        public IReadOnlyList<string> Issues { get; }
    }

    internal sealed class SpecialWorkBuildResult
    {
        public SpecialWorkBuildResult(
            IReadOnlyList<WheelPath> wheelPaths,
            IReadOnlyList<RailCenterline> rails,
            IReadOnlyList<SharedRailInterval> shared,
            IReadOnlyList<RailIntersection> intersections,
            SpecialWorkMeshPlan meshPlan,
            RailProjectionFrame projectionFrame,
            SpecialWorkValidationResult validation,
            NativeTopologyPlan topology)
        {
            WheelPaths = wheelPaths;
            Rails = rails;
            Shared = shared;
            Intersections = intersections;
            MeshPlan = meshPlan;
            ProjectionFrame = projectionFrame;
            Validation = validation;
            Topology = topology;
        }

        public IReadOnlyList<WheelPath> WheelPaths { get; }
        public IReadOnlyList<RailCenterline> Rails { get; }
        public IReadOnlyList<SharedRailInterval> Shared { get; }
        public IReadOnlyList<RailIntersection> Intersections { get; }
        public SpecialWorkMeshPlan MeshPlan { get; }
        public RailProjectionFrame ProjectionFrame { get; }
        public SpecialWorkValidationResult Validation { get; }
        public NativeTopologyPlan Topology { get; }
    }

    internal sealed class SpecialWorkDebugExport
    {
        public SpecialWorkDebugExport(string path)
        {
            Path = path;
        }

        public string Path { get; }
    }

    internal sealed class RailOwnershipInterval
    {
        public RailOwnershipInterval(
            string id,
            RailCenterline rail,
            string ownerRouteId,
            GaugeGraphFamily ownerFamily,
            RailRole role,
            RailPieceKind pieceKind,
            float startDistance,
            float endDistance,
            string sourceId)
        {
            Id = id;
            Rail = rail;
            OwnerRouteId = ownerRouteId;
            OwnerFamily = ownerFamily;
            Role = role;
            PieceKind = pieceKind;
            StartDistance = Mathf.Max(0f, Mathf.Min(startDistance, endDistance));
            EndDistance = Mathf.Min(rail.Curve.Length, Mathf.Max(startDistance, endDistance));
            SourceId = sourceId;
        }

        public string Id { get; }
        public RailCenterline Rail { get; }
        public string OwnerRouteId { get; }
        public GaugeGraphFamily OwnerFamily { get; }
        public RailRole Role { get; }
        public RailPieceKind PieceKind { get; }
        public float StartDistance { get; }
        public float EndDistance { get; }
        public string SourceId { get; }
        public float Length => EndDistance - StartDistance;
    }

    internal sealed class SpecialWorkGeometryParameters
    {
        public SpecialWorkGeometryParameters(
            float railHeadWidth,
            float flangewayWidth,
            float minimumFrogSetback,
            float maximumFrogSetback,
            float guardCenterOffset,
            float guardLeadLength,
            float guardTrailLength,
            float bladeDivergenceThreshold,
            float bladeRootSeparation,
            float maximumBladeLength)
        {
            RailHeadWidth = railHeadWidth;
            FlangewayWidth = flangewayWidth;
            MinimumFrogSetback = minimumFrogSetback;
            MaximumFrogSetback = maximumFrogSetback;
            GuardCenterOffset = guardCenterOffset;
            GuardLeadLength = guardLeadLength;
            GuardTrailLength = guardTrailLength;
            BladeDivergenceThreshold = bladeDivergenceThreshold;
            BladeRootSeparation = bladeRootSeparation;
            MaximumBladeLength = maximumBladeLength;
        }

        public float RailHeadWidth { get; }
        public float FlangewayWidth { get; }
        public float MinimumFrogSetback { get; }
        public float MaximumFrogSetback { get; }
        public float GuardCenterOffset { get; }
        public float GuardLeadLength { get; }
        public float GuardTrailLength { get; }
        public float BladeDivergenceThreshold { get; }
        public float BladeRootSeparation { get; }
        public float MaximumBladeLength { get; }
    }

    internal sealed class SwitchBladeCandidate
    {
        public SwitchBladeCandidate(
            string id,
            MovableAssemblyKind kind,
            string? switchGroupId,
            IEnumerable<LineCurve> movableCurves)
        {
            Id = id;
            Kind = kind;
            SwitchGroupId = switchGroupId;
            MovableCurves = (movableCurves ?? Enumerable.Empty<LineCurve>()).ToArray();
        }

        public string Id { get; }
        public MovableAssemblyKind Kind { get; }
        public string? SwitchGroupId { get; }
        public IReadOnlyList<LineCurve> MovableCurves { get; }
    }

    internal sealed class GuardRailCandidate
    {
        public GuardRailCandidate(
            string id,
            string frogId,
            string protectedRouteId,
            LineCurve curve)
        {
            Id = id;
            FrogId = frogId;
            ProtectedRouteId = protectedRouteId;
            Curve = curve;
        }

        public string Id { get; }
        public string FrogId { get; }
        public string ProtectedRouteId { get; }
        public LineCurve Curve { get; }
    }

    internal sealed class SpecialWorkAnalysis
    {
        public SpecialWorkAnalysis(
            SpecialWorkDefinition definition,
            IEnumerable<WheelPath> wheelPaths,
            IEnumerable<RailCenterline> rails,
            IEnumerable<SharedRailInterval> sharedRailIntervals,
            IEnumerable<RailIntersection> intersections,
            IEnumerable<FrogCandidate> frogs,
            IEnumerable<GuardRailCandidate> guards,
            IEnumerable<SwitchBladeCandidate> blades,
            IEnumerable<string> validationIssues,
            RailProjectionFrame? projectionFrame = null,
            SpecialWorkMeshPlan? meshPlan = null)
        {
            Definition = definition;
            WheelPaths = (wheelPaths ?? Enumerable.Empty<WheelPath>()).ToArray();
            Rails = (rails ?? Enumerable.Empty<RailCenterline>()).ToArray();
            SharedRailIntervals = (sharedRailIntervals ?? Enumerable.Empty<SharedRailInterval>()).ToArray();
            Intersections = (intersections ?? Enumerable.Empty<RailIntersection>()).ToArray();
            Frogs = (frogs ?? Enumerable.Empty<FrogCandidate>()).ToArray();
            Guards = (guards ?? Enumerable.Empty<GuardRailCandidate>()).ToArray();
            Blades = (blades ?? Enumerable.Empty<SwitchBladeCandidate>()).ToArray();
            ValidationIssues = (validationIssues ?? Enumerable.Empty<string>()).ToArray();
            ProjectionFrame = projectionFrame ?? RailProjectionFrame.Identity;
            MeshPlan = meshPlan;
        }

        public SpecialWorkDefinition Definition { get; }
        public IReadOnlyList<WheelPath> WheelPaths { get; }
        public IReadOnlyList<RailCenterline> Rails { get; }
        public IReadOnlyList<SharedRailInterval> SharedRailIntervals { get; }
        public IReadOnlyList<RailIntersection> Intersections { get; }
        public IReadOnlyList<FrogCandidate> Frogs { get; }
        public IReadOnlyList<GuardRailCandidate> Guards { get; }
        public IReadOnlyList<SwitchBladeCandidate> Blades { get; }
        public IReadOnlyList<string> ValidationIssues { get; }
        public RailProjectionFrame ProjectionFrame { get; }
        public SpecialWorkMeshPlan? MeshPlan { get; }
    }
}
