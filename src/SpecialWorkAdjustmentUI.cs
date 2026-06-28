using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core;
using Newtonsoft.Json;
using Track;
using UnityEngine;

namespace NarrowGaugeMod
{
    internal sealed class SpecialWorkAdjustmentUI : MonoBehaviour
    {
        private const float WindowWidth = 560f;
        private const float WindowHeight = 900f;
        private const float MinimumAdjustedRailLength = 0.1f;
        private const float RenderedMinimumRailPieceLength = 0.35f;

        internal bool Visible;
        private Rect _windowRect = new Rect(10f, 60f, WindowWidth, WindowHeight);
        internal string? DebugLabelNodeFilter;

        private string? _selectedNodeId;
        private SpecialWorkAnalysis? _selectedAnalysis;
        private int _selectedPieceIndex = -1;
        private Vector2 _nodeScrollPosition;
        private Vector2 _pieceScrollPosition;
        private Transform? _cachedRoot;
        private List<PieceState> _pieces = new List<PieceState>();

        private static string SavePath =>
            Path.Combine(
                Path.GetDirectoryName(typeof(Main).Assembly.Location) ?? ".",
                "SpecialWorkOverrides.json");

        private Dictionary<string, SavedNodeData> _savedData =
            new Dictionary<string, SavedNodeData>(StringComparer.OrdinalIgnoreCase);

        private bool _autoApplyPending;
        private float _autoApplyTimer;
        private readonly Dictionary<string, Dictionary<string, Vector3>> _trueOriginalPositions =
            new Dictionary<string, Dictionary<string, Vector3>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, Quaternion>> _trueOriginalRotations =
            new Dictionary<string, Dictionary<string, Quaternion>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, Mesh>> _trueOriginalMeshes =
            new Dictionary<string, Dictionary<string, Mesh>>(StringComparer.OrdinalIgnoreCase);

        private void Start()
        {
            LoadFromDisk();
            _autoApplyPending = _savedData.Count > 0;
        }

        private int _autoApplyAttempts;
        private float _reapplyTimer;
        private readonly Dictionary<string, int> _appliedInstanceIds =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private void LateUpdate()
        {
            if (!Main.Enabled || _savedData.Count == 0)
            {
                return;
            }

            if (_autoApplyPending)
            {
                _autoApplyTimer += Time.deltaTime;
                if (_autoApplyTimer >= 5f)
                {
                    _autoApplyTimer = 0f;
                    _autoApplyAttempts++;
                    int applied = AutoApplyAllSaved();
                    if (applied >= _savedData.Count || _autoApplyAttempts >= 10)
                    {
                        _autoApplyPending = false;
                    }
                }

                return;
            }

            _reapplyTimer += Time.deltaTime;
            if (_reapplyTimer < 2f)
            {
                return;
            }

            _reapplyTimer = 0f;
            foreach (var pair in _savedData)
            {
                string nodeId = pair.Key;
                SavedNodeData data = pair.Value;
                if (data.Pieces == null || data.Pieces.Length == 0)
                {
                    continue;
                }

                Transform? root = FindSpecialWorkRoot(nodeId);
                if (root == null)
                {
                    continue;
                }

                int instanceId = root.gameObject.GetInstanceID();
                if (_appliedInstanceIds.TryGetValue(nodeId, out int lastId) && lastId == instanceId)
                {
                    continue;
                }

                _trueOriginalPositions.Remove(nodeId);
                _trueOriginalRotations.Remove(nodeId);
                _trueOriginalMeshes.Remove(nodeId);
                CaptureOriginals(nodeId, root);
                ApplySavedToRoot(nodeId, root, data);
                _appliedInstanceIds[nodeId] = instanceId;
            }
        }

        private int AutoApplyAllSaved()
        {
            int applied = 0;
            foreach (var pair in _savedData)
            {
                string nodeId = pair.Key;
                SavedNodeData data = pair.Value;
                if (data.Pieces == null || data.Pieces.Length == 0)
                {
                    applied++;
                    continue;
                }

                Transform? root = FindSpecialWorkRoot(nodeId);
                if (root == null)
                {
                    Main.Log($"[AdjustUI] Auto-apply: root not found for '{nodeId}' (attempt {_autoApplyAttempts})");
                    continue;
                }

                CaptureOriginals(nodeId, root);
                ApplySavedToRoot(nodeId, root, data);
                _appliedInstanceIds[nodeId] = root.gameObject.GetInstanceID();
                applied++;
                Main.Log($"[AdjustUI] Auto-apply: applied {data.Pieces.Length} overrides to '{nodeId}'");
            }

            return applied;
        }

        private void CaptureOriginals(string nodeId, Transform root)
        {
            bool captureTransforms = !_trueOriginalPositions.ContainsKey(nodeId)
                || !_trueOriginalRotations.ContainsKey(nodeId);
            bool captureMeshes = !_trueOriginalMeshes.ContainsKey(nodeId);
            if (!captureTransforms && !captureMeshes)
            {
                return;
            }

            var positions = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
            var rotations = new Dictionary<string, Quaternion>(StringComparer.OrdinalIgnoreCase);
            var meshes = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase);
            foreach (Transform child in root)
            {
                if (child != null && !string.IsNullOrEmpty(child.name))
                {
                    positions[child.name] = child.localPosition;
                    rotations[child.name] = child.localRotation;
                    MeshFilter? mf = child.GetComponentInChildren<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        meshes[child.name] = mf.sharedMesh;
                    }
                }
            }

            if (captureTransforms)
            {
                _trueOriginalPositions[nodeId] = positions;
                _trueOriginalRotations[nodeId] = rotations;
            }

            if (captureMeshes)
            {
                _trueOriginalMeshes[nodeId] = meshes;
            }
        }

        private void ApplySavedToRoot(string nodeId, Transform root, SavedNodeData data)
        {
            if (data.Pieces == null)
            {
                return;
            }

            SpecialWorkAnalysis? analysis = SpecialWorkRuntimeRegistry.FindByNativeNodeId(nodeId);
            _trueOriginalPositions.TryGetValue(nodeId, out var origPositions);
            _trueOriginalRotations.TryGetValue(nodeId, out var origRotations);

            foreach (Transform child in root)
            {
                if (child == null || string.IsNullOrEmpty(child.name))
                {
                    continue;
                }

                SavedPieceData? saved = data.Pieces.FirstOrDefault(p =>
                    string.Equals(p.Name, child.name, StringComparison.OrdinalIgnoreCase));
                if (saved == null)
                {
                    continue;
                }

                PieceState piece = CreatePieceState(
                    child,
                    root,
                    nodeId,
                    analysis,
                    origPositions,
                    origRotations);
                ApplySavedData(piece, saved);
                ApplyPiece(piece);
            }
        }

        private void OnGUI()
        {
            if (!Visible || !Main.Enabled)
            {
                return;
            }

            _windowRect = GUI.Window(
                9847201,
                _windowRect,
                DrawWindow,
                "Special Work Editor");

            if (_windowRect.Contains(Event.current.mousePosition))
            {
                UnityEngine.GUI.FocusWindow(9847201);
            }
        }

        private void DrawWindow(int id)
        {
            // --- Node selection ---
            GUILayout.Label("<b>Switch Nodes:</b>");
            _nodeScrollPosition = GUILayout.BeginScrollView(
                _nodeScrollPosition, GUILayout.Height(140f));
            foreach (SpecialWorkAnalysis analysis in SpecialWorkRuntimeRegistry.Analyses
                .Where(a => a.MeshPlan?.IsValid == true)
                .OrderBy(a => a.Definition.Id))
            {
                string nodeId = analysis.Definition.NativeSwitchNodeIds.FirstOrDefault() ?? "";
                if (string.IsNullOrEmpty(nodeId))
                {
                    continue;
                }

                string presetName = analysis.Definition.Preset.Id;
                int lastDot = presetName.LastIndexOf('.');
                if (lastDot >= 0)
                {
                    presetName = presetName.Substring(lastDot + 1);
                }

                bool selected = string.Equals(
                    _selectedNodeId, nodeId, StringComparison.OrdinalIgnoreCase);
                if (GUILayout.Toggle(selected, $"{nodeId}  [{presetName}]") && !selected)
                {
                    SelectNode(nodeId);
                }
            }

            GUILayout.EndScrollView();

            if (_pieces.Count == 0 || _cachedRoot == null)
            {
                GUI.DragWindow();
                return;
            }

            // --- Toolbar ---
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save")) { SaveCurrentNode(); SaveToDisk(); }
            if (GUILayout.Button("Load")) { LoadCurrentNode(); }
            if (GUILayout.Button("Reset All")) { ResetAll(); }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            bool debugThis = string.Equals(
                DebugLabelNodeFilter, _selectedNodeId, StringComparison.OrdinalIgnoreCase);
            bool newDebugThis = GUILayout.Toggle(debugThis, "Show debug labels for this switch only");
            if (newDebugThis != debugThis)
            {
                DebugLabelNodeFilter = newDebugThis ? _selectedNodeId : null;
            }
            GUILayout.EndHorizontal();

            // --- Piece list ---
            GUILayout.Space(4f);
            GUILayout.Label($"<b>Pieces ({_pieces.Count}):</b>");
            _pieceScrollPosition = GUILayout.BeginScrollView(
                _pieceScrollPosition, GUILayout.Height(200f));
            for (int i = 0; i < _pieces.Count; i++)
            {
                PieceState piece = _pieces[i];
                bool isSel = i == _selectedPieceIndex;

                GUILayout.BeginHorizontal();

                bool wasVisible = piece.Visible;
                piece.Visible = GUILayout.Toggle(piece.Visible, "", GUILayout.Width(18f));
                if (wasVisible != piece.Visible && piece.Transform != null)
                {
                    piece.Transform.gameObject.SetActive(piece.Visible);
                }

                string btnLabel = isSel ? (">> " + piece.Name) : piece.Name;
                if (GUILayout.Button(btnLabel))
                {
                    _selectedPieceIndex = i;
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();

            // --- Selected piece editor ---
            if (_selectedPieceIndex >= 0 && _selectedPieceIndex < _pieces.Count)
            {
                PieceState sel = _pieces[_selectedPieceIndex];
                GUILayout.Space(6f);
                GUILayout.Label($"<b><color=yellow>Editing: {sel.Name}</color></b>");
                DrawPieceEditor(sel);
            }

            GUI.DragWindow();
        }

        private void DrawPieceEditor(PieceState piece)
        {
            bool changed = false;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Lateral (mm):", GUILayout.Width(90f));
            changed |= DrawTextField(ref piece.LateralText, 70f);
            GUILayout.Label("Longitud (mm):", GUILayout.Width(95f));
            changed |= DrawTextField(ref piece.LongitudinalText, 70f);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Vertical (mm):", GUILayout.Width(90f));
            changed |= DrawTextField(ref piece.VerticalText, 70f);
            GUILayout.Label("Rotation (deg):", GUILayout.Width(95f));
            changed |= DrawTextField(ref piece.RotationText, 70f);
            GUILayout.EndHorizontal();

            if (piece.HasRailMatch)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Head (mm):", GUILayout.Width(90f));
                changed |= DrawTextField(ref piece.HeadDeltaText, 70f);
                GUILayout.Label("Tail (mm):", GUILayout.Width(95f));
                changed |= DrawTextField(ref piece.TailDeltaText, 70f);
                GUILayout.EndHorizontal();
            }

            if (piece.HasFrogPullback)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Frog pullback (mm):", GUILayout.Width(120f));
                changed |= DrawTextField(ref piece.FrogPullbackText, 70f);
                GUILayout.EndHorizontal();
            }

            if (piece.HasFlangewayCut)
            {
                GUILayout.BeginHorizontal();
                bool flipped = GUILayout.Toggle(
                    piece.FlipFlangewaySide,
                    "Flip flangeway cut side");
                if (flipped != piece.FlipFlangewaySide)
                {
                    piece.FlipFlangewaySide = flipped;
                    changed = true;
                }

                GUILayout.EndHorizontal();
            }

            if (piece.HasRailMatch && !string.IsNullOrEmpty(piece.SourceNodeId))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Cut by rail:", GUILayout.Width(70f));
                piece.ManualCutRailId = GUILayout.TextField(
                    piece.ManualCutRailId ?? "", GUILayout.Width(160f));
                GUILayout.Label("W:", GUILayout.Width(18f));
                piece.ManualCutWidthText = GUILayout.TextField(
                    piece.ManualCutWidthText ?? "63", GUILayout.Width(40f));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Cut"))
                {
                    ApplyManualFlangewayCut(piece);
                }
                if (GUILayout.Button("Clear"))
                {
                    piece.ManualCutRailId = "";
                    RestoreMesh(piece);
                }
                GUILayout.EndHorizontal();

                if (_selectedAnalysis != null)
                {
                    GUILayout.Label("Available rails (click to select):");
                    foreach (RailCenterline rail in _selectedAnalysis.Rails)
                    {
                        if (GUILayout.Button(rail.Id))
                        {
                            piece.ManualCutRailId = rail.Id;
                        }
                    }
                }
            }

            if (changed)
            {
                ParseAndApply(piece);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply"))
            {
                ParseAndApply(piece);
            }

            if (GUILayout.Button("Zero"))
            {
                piece.LateralOffset = 0f;
                piece.LongitudinalOffset = 0f;
                piece.VerticalOffset = 0f;
                piece.RotationDegrees = 0f;
                piece.HeadDelta = 0f;
                piece.TailDelta = 0f;
                piece.FrogPullback = 0f;
                piece.FlipFlangewaySide = false;
                piece.LateralText = "0";
                piece.LongitudinalText = "0";
                piece.VerticalText = "0";
                piece.RotationText = "0";
                piece.HeadDeltaText = "0";
                piece.TailDeltaText = "0";
                piece.FrogPullbackText = "0";
                ApplyPiece(piece);
            }

            if (GUILayout.Button("Duplicate"))
            {
                DuplicatePiece(piece);
            }

            if (GUILayout.Button("Delete"))
            {
                DeletePiece(piece);
            }

            GUILayout.EndHorizontal();
        }

        private static bool DrawTextField(ref string text, float width)
        {
            string updated = GUILayout.TextField(text, GUILayout.Width(width));
            if (string.Equals(updated, text, StringComparison.Ordinal))
            {
                return false;
            }

            text = updated;
            return true;
        }

        private void ParseAndApply(PieceState piece)
        {
            if (float.TryParse(piece.LateralText, out float lat))
                piece.LateralOffset = lat / 1000f;
            if (float.TryParse(piece.LongitudinalText, out float lon))
                piece.LongitudinalOffset = lon / 1000f;
            if (float.TryParse(piece.VerticalText, out float vert))
                piece.VerticalOffset = vert / 1000f;
            if (float.TryParse(piece.RotationText, out float rot))
                piece.RotationDegrees = rot;
            if (float.TryParse(piece.HeadDeltaText, out float head))
                piece.HeadDelta = head / 1000f;
            if (float.TryParse(piece.TailDeltaText, out float tail))
                piece.TailDelta = tail / 1000f;
            if (float.TryParse(piece.FrogPullbackText, out float frogPullback))
                piece.FrogPullback = frogPullback / 1000f;
            ApplyPiece(piece);
            if (!string.IsNullOrEmpty(piece.ManualCutRailId))
            {
                ApplyManualFlangewayCut(piece);
            }
        }

        private void SelectNode(string nodeId)
        {
            _selectedNodeId = nodeId;
            _selectedPieceIndex = -1;
            _cachedRoot = null;
            _pieces.Clear();

            Transform? root = FindSpecialWorkRoot(nodeId);
            if (root == null)
            {
                return;
            }

            _cachedRoot = root;
            _selectedAnalysis = SpecialWorkRuntimeRegistry.Analyses
                .FirstOrDefault(a => a.Definition.NativeSwitchNodeIds
                    .Contains(nodeId, StringComparer.OrdinalIgnoreCase));
            SpecialWorkAnalysis? analysis = _selectedAnalysis;
            CaptureOriginals(nodeId, root);
            _trueOriginalPositions.TryGetValue(nodeId, out var origPositions);
            _trueOriginalRotations.TryGetValue(nodeId, out var origRotations);

            foreach (Transform child in root)
            {
                if (child == null || string.IsNullOrEmpty(child.name))
                {
                    continue;
                }

                _pieces.Add(CreatePieceState(
                    child,
                    root,
                    nodeId,
                    analysis,
                    origPositions,
                    origRotations));
            }

            LoadCurrentNode();
        }

        private PieceState CreatePieceState(
            Transform child,
            Transform root,
            string nodeId,
            SpecialWorkAnalysis? analysis,
            IReadOnlyDictionary<string, Vector3>? origPositions,
            IReadOnlyDictionary<string, Quaternion>? origRotations)
        {
            Vector3 origPos = origPositions != null
                && origPositions.TryGetValue(child.name, out Vector3 cached)
                    ? cached
                    : child.localPosition;
            Quaternion origRot = origRotations != null
                && origRotations.TryGetValue(child.name, out Quaternion cachedRot)
                    ? cachedRot
                    : child.localRotation;

            Vector3 forward = origRot * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.0001f)
            {
                forward = root.forward;
                forward.y = 0f;
            }

            if (forward.sqrMagnitude <= 0.0001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            if (right.sqrMagnitude <= 0.0001f)
            {
                right = Vector3.right;
            }

            RailPieceMatch? match = MatchToPlanPiece(child, analysis);
            return new PieceState
            {
                Name = child.name,
                Transform = child,
                OriginalPosition = origPos,
                OriginalRotation = origRot,
                Right = right,
                Forward = forward,
                SourceNodeId = nodeId,
                MatchedRail = match?.Rail,
                MatchedStartDistance = match?.StartDistance ?? 0f,
                MatchedEndDistance = match?.EndDistance ?? 0f,
                MatchedFlangewayCenters = match?.FlangewayCenters ?? Array.Empty<LineCurve>(),
                MatchedFlangewayKeepPoint = match?.FlangewayKeepPoint ?? Vector3.zero,
                MatchedFlangewayWidth = match?.FlangewayWidth ?? 0f,
                MatchedFlangewayCutFocusPoint = match?.FlangewayCutFocusPoint,
                MatchedFlangewayCutWindowLength = match?.FlangewayCutWindowLength ?? 0f,
                AutoFlipFlangewaySide = match?.AutoFlipFlangewaySide ?? false,
                AutoFlipFlangewayIndex = match?.AutoFlipFlangewayIndex ?? -1,
                FrogSide = match?.FrogSide ?? FrogEndpointSide.None,
                FrogDistance = match?.FrogDistance ?? 0f,
                HasRailMatch = match != null
            };
        }

        private static void ApplySavedData(PieceState piece, SavedPieceData saved)
        {
            float headDeltaMm = saved.HeadDeltaMm ?? 0f;
            float tailDeltaMm = saved.TailDeltaMm ?? saved.LengthDeltaMm;
            float frogPullbackMm = saved.FrogPullbackMm ?? 0f;

            piece.LateralOffset = saved.LateralMm / 1000f;
            piece.LongitudinalOffset = saved.LongitudinalMm / 1000f;
            piece.VerticalOffset = saved.VerticalMm / 1000f;
            piece.RotationDegrees = saved.RotationDeg;
            piece.HeadDelta = headDeltaMm / 1000f;
            piece.TailDelta = tailDeltaMm / 1000f;
            piece.FrogPullback = frogPullbackMm / 1000f;
            piece.FlipFlangewaySide = saved.FlipFlangewaySide;
            piece.ManualCutRailId = saved.ManualCutRailId;
            piece.ManualCutWidth = saved.ManualCutWidthMm / 1000f;
            piece.ManualCutWidthText = saved.ManualCutWidthMm.ToString("0");
            piece.LateralText = saved.LateralMm.ToString("0.0");
            piece.LongitudinalText = saved.LongitudinalMm.ToString("0.0");
            piece.VerticalText = saved.VerticalMm.ToString("0.0");
            piece.RotationText = saved.RotationDeg.ToString("0.00");
            piece.HeadDeltaText = headDeltaMm.ToString("0.0");
            piece.TailDeltaText = tailDeltaMm.ToString("0.0");
            piece.FrogPullbackText = frogPullbackMm.ToString("0.0");
            piece.Visible = saved.Visible;
            if (piece.Transform != null)
            {
                piece.Transform.gameObject.SetActive(piece.Visible);
            }
        }

        private void ApplyPiece(PieceState piece)
        {
            if (piece.Transform == null)
            {
                return;
            }

            piece.Transform.localPosition = piece.OriginalPosition
                + piece.Right * piece.LateralOffset
                + piece.Forward * piece.LongitudinalOffset
                + Vector3.up * piece.VerticalOffset;

            piece.Transform.localRotation = piece.OriginalRotation
                * Quaternion.Euler(0f, piece.RotationDegrees, 0f);

            if (piece.HasRailMatch
                && (Mathf.Abs(piece.HeadDelta) > 0.0001f
                    || Mathf.Abs(piece.TailDelta) > 0.0001f
                    || Mathf.Abs(piece.FrogPullback) > 0.0001f
                    || piece.FlipFlangewaySide))
            {
                RebuildMeshWithLength(piece);
            }
            else if (piece.OriginalMesh != null)
            {
                RestoreMesh(piece);
            }
        }

        private static void RebuildMeshWithLength(PieceState piece)
        {
            if (piece.Transform == null || piece.MatchedRail == null)
            {
                return;
            }

            MeshFilter? mf = piece.Transform.GetComponentInChildren<MeshFilter>();
            if (mf == null)
            {
                return;
            }

            if (piece.OriginalMesh == null)
            {
                piece.OriginalMesh = mf.sharedMesh;
            }

            if (!TryGetAdjustedDistances(piece, out float adjustedStart, out float adjustedEnd)
                || !TryBuildAdjustedCurve(piece.MatchedRail.Curve, adjustedStart, adjustedEnd, out LineCurve sliced))
            {
                return;
            }

            TrackNode? node = !string.IsNullOrEmpty(piece.SourceNodeId)
                ? Graph.Shared?.GetNode(piece.SourceNodeId)
                : null;
            Vector3 switchHome = node != null
                ? node.transform.localPosition
                : Vector3.zero;

            Mesh? newMesh = null;
            if (piece.MatchedFlangewayCenters.Count > 0
                && piece.MatchedFlangewayWidth > 0f)
            {
                newMesh = SpecialWorkHardwareRenderer.BuildFlangewayCutFrogRailMesh(
                    sliced,
                    piece.MatchedFlangewayCenters,
                    piece.MatchedFlangewayKeepPoint,
                    piece.MatchedFlangewayWidth,
                    switchHome,
                    piece.AutoFlipFlangewaySide ^ piece.FlipFlangewaySide,
                    piece.AutoFlipFlangewayIndex,
                    piece.MatchedFlangewayCutFocusPoint,
                    piece.MatchedFlangewayCutWindowLength);
            }

            newMesh = newMesh ?? NarrowGaugeTrackBuilder.BuildStockRailMesh(
                sliced.Offset(-switchHome),
                switchHome,
                Gauge.Standard,
                _ => 1f);
            mf.mesh = newMesh;
        }

        private static bool TryGetAdjustedDistances(
            PieceState piece,
            out float adjustedStart,
            out float adjustedEnd)
        {
            adjustedStart = 0f;
            adjustedEnd = 0f;
            if (piece.MatchedRail == null)
            {
                return false;
            }

            float railLength = piece.MatchedRail.Curve.Length;
            adjustedStart = piece.MatchedStartDistance - piece.HeadDelta;
            adjustedEnd = piece.MatchedEndDistance + piece.TailDelta;
            switch (piece.FrogSide)
            {
                case FrogEndpointSide.Start:
                    if (Mathf.Abs(piece.FrogPullback) > 0.0001f)
                    {
                        float targetStart = piece.FrogDistance + piece.FrogPullback;
                        adjustedStart = piece.FrogPullback >= 0f
                            ? Mathf.Max(adjustedStart, targetStart)
                            : Mathf.Min(adjustedStart, targetStart);
                    }
                    break;
                case FrogEndpointSide.End:
                    if (Mathf.Abs(piece.FrogPullback) > 0.0001f)
                    {
                        float targetEnd = piece.FrogDistance - piece.FrogPullback;
                        adjustedEnd = piece.FrogPullback >= 0f
                            ? Mathf.Min(adjustedEnd, targetEnd)
                            : Mathf.Max(adjustedEnd, targetEnd);
                    }
                    break;
            }

            if (adjustedEnd - adjustedStart < MinimumAdjustedRailLength)
            {
                adjustedEnd = adjustedStart + MinimumAdjustedRailLength;
                if (adjustedEnd - adjustedStart < MinimumAdjustedRailLength)
                {
                    adjustedStart = adjustedEnd - MinimumAdjustedRailLength;
                }
            }

            return railLength > 0.01f && adjustedEnd - adjustedStart >= 0.01f;
        }

        private static bool TryBuildAdjustedCurve(
            LineCurve source,
            float adjustedStart,
            float adjustedEnd,
            out LineCurve curve)
        {
            curve = null!;
            if (source == null
                || source.Points.Count() < 2
                || adjustedEnd - adjustedStart < 0.01f)
            {
                return false;
            }

            float sourceLength = source.Length;
            if (adjustedEnd <= 0f)
            {
                Vector3 tangent = CurveTangent(source, atHead: true);
                LinePoint head = source.Head;
                curve = new LineCurve(
                    new[]
                    {
                        new LinePoint(head.point + tangent * adjustedStart, head.Rotation),
                        new LinePoint(head.point + tangent * adjustedEnd, head.Rotation)
                    },
                    source.hand);
                return true;
            }

            if (adjustedStart >= sourceLength)
            {
                Vector3 tangent = CurveTangent(source, atHead: false);
                LinePoint tail = source.Tail;
                curve = new LineCurve(
                    new[]
                    {
                        new LinePoint(tail.point + tangent * (adjustedStart - sourceLength), tail.Rotation),
                        new LinePoint(tail.point + tangent * (adjustedEnd - sourceLength), tail.Rotation)
                    },
                    source.hand);
                return true;
            }

            float sliceStart = Mathf.Clamp(adjustedStart, 0f, sourceLength);
            float sliceEnd = Mathf.Clamp(adjustedEnd, 0f, sourceLength);
            var points = source
                .Skip(sliceStart, true)
                .Take(sliceEnd - sliceStart)
                .Points
                .ToList();

            if (adjustedStart < 0f)
            {
                LinePoint head = source.Head;
                points.Insert(
                    0,
                    new LinePoint(
                        head.point + CurveTangent(source, atHead: true) * adjustedStart,
                        head.Rotation));
            }

            if (adjustedEnd > sourceLength)
            {
                LinePoint tail = source.Tail;
                points.Add(
                    new LinePoint(
                        tail.point + CurveTangent(source, atHead: false) * (adjustedEnd - sourceLength),
                        tail.Rotation));
            }

            if (points.Count < 2)
            {
                return false;
            }

            curve = new LineCurve(points, source.hand);
            return true;
        }

        private static Vector3 CurveTangent(LineCurve curve, bool atHead)
        {
            LinePoint[] points = curve.Points.ToArray();
            if (points.Length >= 2)
            {
                Vector3 delta = atHead
                    ? points[1].point - points[0].point
                    : points[points.Length - 1].point - points[points.Length - 2].point;
                if (delta.sqrMagnitude > 0.0001f)
                {
                    return delta.normalized;
                }
            }

            Vector3 tangent = (atHead ? curve.Head : curve.Tail).Rotation * Vector3.forward;
            return tangent.sqrMagnitude > 0.0001f
                ? tangent.normalized
                : Vector3.forward;
        }

        private static void RestoreMesh(PieceState piece)
        {
            if (piece.Transform == null || piece.OriginalMesh == null)
            {
                return;
            }

            MeshFilter? mf = piece.Transform.GetComponentInChildren<MeshFilter>();
            if (mf != null)
            {
                mf.mesh = piece.OriginalMesh;
            }

            piece.OriginalMesh = null;
        }

        private void ApplyManualFlangewayCut(PieceState piece)
        {
            if (piece.Transform == null
                || piece.MatchedRail == null
                || string.IsNullOrEmpty(piece.ManualCutRailId))
            {
                Main.Log("[AdjustUI] Manual cut: missing data");
                return;
            }

            SpecialWorkAnalysis? analysis = _selectedAnalysis;
            if (analysis == null)
            {
                Main.Log("[AdjustUI] Manual cut: no analysis cached");
                return;
            }

            RailCenterline? cutterRail = analysis.Rails.FirstOrDefault(r =>
                string.Equals(r.Id, piece.ManualCutRailId, StringComparison.OrdinalIgnoreCase));
            if (cutterRail == null)
            {
                Main.Log($"[AdjustUI] Manual cut: rail '{piece.ManualCutRailId}' not found");
                return;
            }

            MeshFilter? mf = piece.Transform.GetComponentInChildren<MeshFilter>();
            if (mf == null)
            {
                return;
            }

            if (piece.OriginalMesh == null)
            {
                piece.OriginalMesh = mf.sharedMesh;
            }

            TrackNode? node = Graph.Shared?.GetNode(piece.SourceNodeId);
            Vector3 switchHome = node != null
                ? node.transform.localPosition
                : Vector3.zero;

            LineCurve pieceCurve = piece.MatchedRail.Curve
                .Skip(piece.MatchedStartDistance, true)
                .Take(piece.MatchedEndDistance - piece.MatchedStartDistance);
            LineCurve cutterSlice = cutterRail.Curve.Skip(
                cutterRail.Curve.DistanceTo(pieceCurve.Head.point), true)
                .Take(Mathf.Abs(
                    cutterRail.Curve.DistanceTo(pieceCurve.Tail.point)
                    - cutterRail.Curve.DistanceTo(pieceCurve.Head.point)));
            if (cutterSlice.Length < 0.1f)
            {
                Main.Log("[AdjustUI] Manual cut: cutter slice too short");
                return;
            }

            Vector3 keepPoint = pieceCurve.LinePointAtDistance(pieceCurve.Length).point;
            if (float.TryParse(piece.ManualCutWidthText, out float parsedWidth))
            {
                piece.ManualCutWidth = parsedWidth / 1000f;
            }

            float flangewayWidth = Mathf.Max(piece.ManualCutWidth, 0.01f);

            LineCurve correctedPiece = SpecialWorkHardwareRenderer.CorrectMeasuredRailRenderFramePublic(
                analysis, piece.MatchedRail.Id, pieceCurve);
            LineCurve correctedCutter = SpecialWorkHardwareRenderer.CorrectMeasuredRailRenderFramePublic(
                analysis, cutterRail.Id, cutterSlice);

            var cuts = new List<(LineCurve Center, Vector3 KeepPoint)>
            {
                (correctedCutter, keepPoint)
            };
            SpecialWorkHardwareRenderer.CreateFlangewayCutRailDirect(
                correctedPiece,
                cuts,
                flangewayWidth,
                switchHome,
                out Mesh? resultMesh);
            if (resultMesh != null)
            {
                mf.mesh = resultMesh;
                Main.Log($"[AdjustUI] Manual cut: applied cut from '{cutterRail.Id}' to '{piece.Name}'");
            }
        }

        private void ApplyAll()
        {
            foreach (PieceState piece in _pieces)
            {
                ApplyPiece(piece);
            }
        }

        private void ResetAll()
        {
            foreach (PieceState piece in _pieces)
            {
                piece.LateralOffset = 0f;
                piece.LongitudinalOffset = 0f;
                piece.VerticalOffset = 0f;
                piece.RotationDegrees = 0f;
                piece.HeadDelta = 0f;
                piece.TailDelta = 0f;
                piece.FrogPullback = 0f;
                piece.FlipFlangewaySide = false;
                piece.LateralText = "0";
                piece.LongitudinalText = "0";
                piece.VerticalText = "0";
                piece.RotationText = "0";
                piece.HeadDeltaText = "0";
                piece.TailDeltaText = "0";
                piece.FrogPullbackText = "0";
                piece.Visible = true;
                if (piece.Transform != null)
                {
                    piece.Transform.gameObject.SetActive(true);
                }
            }

            ApplyAll();
        }

        private void DuplicatePiece(PieceState source)
        {
            if (source.Transform == null || _cachedRoot == null)
            {
                return;
            }

            GameObject clone = Instantiate(
                source.Transform.gameObject,
                _cachedRoot);
            clone.name = source.Name + "_copy" + _pieces.Count;

            Vector3 forward = source.Forward;
            Vector3 right = source.Right;

            var newPiece = new PieceState
            {
                Name = clone.name,
                Transform = clone.transform,
                OriginalPosition = source.OriginalPosition,
                OriginalRotation = source.OriginalRotation,
                Right = right,
                Forward = forward,
                LateralOffset = source.LateralOffset,
                LongitudinalOffset = source.LongitudinalOffset,
                VerticalOffset = source.VerticalOffset,
                RotationDegrees = source.RotationDegrees,
                HeadDelta = source.HeadDelta,
                TailDelta = source.TailDelta,
                FrogPullback = source.FrogPullback,
                FlipFlangewaySide = source.FlipFlangewaySide,
                LateralText = source.LateralText,
                LongitudinalText = source.LongitudinalText,
                VerticalText = source.VerticalText,
                RotationText = source.RotationText,
                HeadDeltaText = source.HeadDeltaText,
                TailDeltaText = source.TailDeltaText,
                FrogPullbackText = source.FrogPullbackText,
                SourceNodeId = source.SourceNodeId,
                MatchedRail = source.MatchedRail,
                MatchedStartDistance = source.MatchedStartDistance,
                MatchedEndDistance = source.MatchedEndDistance,
                MatchedFlangewayCenters = source.MatchedFlangewayCenters,
                MatchedFlangewayKeepPoint = source.MatchedFlangewayKeepPoint,
                MatchedFlangewayWidth = source.MatchedFlangewayWidth,
                MatchedFlangewayCutFocusPoint = source.MatchedFlangewayCutFocusPoint,
                MatchedFlangewayCutWindowLength = source.MatchedFlangewayCutWindowLength,
                AutoFlipFlangewaySide = source.AutoFlipFlangewaySide,
                AutoFlipFlangewayIndex = source.AutoFlipFlangewayIndex,
                FrogSide = source.FrogSide,
                FrogDistance = source.FrogDistance,
                HasRailMatch = source.HasRailMatch,
                OriginalMesh = source.OriginalMesh
            };

            _pieces.Add(newPiece);
            _selectedPieceIndex = _pieces.Count - 1;
        }

        private void DeletePiece(PieceState piece)
        {
            if (piece.Transform != null)
            {
                Destroy(piece.Transform.gameObject);
            }

            int idx = _pieces.IndexOf(piece);
            _pieces.Remove(piece);
            if (_selectedPieceIndex >= _pieces.Count)
            {
                _selectedPieceIndex = _pieces.Count - 1;
            }
        }

        // --- Save / Load ---

        private void SaveCurrentNode()
        {
            if (string.IsNullOrEmpty(_selectedNodeId))
            {
                return;
            }

            _savedData[_selectedNodeId!] = new SavedNodeData
            {
                Pieces = _pieces.Select(p => new SavedPieceData
                {
                    Name = p.Name,
                    LateralMm = p.LateralOffset * 1000f,
                    LongitudinalMm = p.LongitudinalOffset * 1000f,
                    VerticalMm = p.VerticalOffset * 1000f,
                    RotationDeg = p.RotationDegrees,
                    HeadDeltaMm = p.HeadDelta * 1000f,
                    TailDeltaMm = p.TailDelta * 1000f,
                    FrogPullbackMm = p.FrogPullback * 1000f,
                    FlipFlangewaySide = p.FlipFlangewaySide,
                    ManualCutRailId = p.ManualCutRailId ?? "",
                    ManualCutWidthMm = p.ManualCutWidth * 1000f,
                    LengthDeltaMm = p.TailDelta * 1000f,
                    Visible = p.Visible
                }).ToArray()
            };
        }

        private void LoadCurrentNode()
        {
            if (string.IsNullOrEmpty(_selectedNodeId)
                || !_savedData.TryGetValue(_selectedNodeId!, out SavedNodeData data)
                || data.Pieces == null)
            {
                return;
            }

            foreach (PieceState piece in _pieces)
            {
                SavedPieceData? saved = data.Pieces.FirstOrDefault(p =>
                    string.Equals(p.Name, piece.Name, StringComparison.OrdinalIgnoreCase));
                if (saved == null)
                {
                    continue;
                }

                ApplySavedData(piece, saved);
            }

            ApplyAll();
        }

        private void SaveToDisk()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_savedData, Formatting.Indented);
                File.WriteAllText(SavePath, json);
                Main.Log($"[AdjustUI] Saved to {SavePath}");
            }
            catch (Exception ex)
            {
                Main.Warn($"[AdjustUI] Save failed: {ex.Message}");
            }
        }

        private void LoadFromDisk()
        {
            try
            {
                if (!File.Exists(SavePath))
                {
                    return;
                }

                string json = File.ReadAllText(SavePath);
                _savedData = JsonConvert.DeserializeObject<Dictionary<string, SavedNodeData>>(json)
                    ?? new Dictionary<string, SavedNodeData>(StringComparer.OrdinalIgnoreCase);
                Main.Log($"[AdjustUI] Loaded {_savedData.Count} node overrides from disk.");
            }
            catch (Exception ex)
            {
                Main.Warn($"[AdjustUI] Load failed: {ex.Message}");
            }
        }

        // --- Find objects ---

        private static RailPieceMatch? MatchToPlanPiece(Transform child, SpecialWorkAnalysis? analysis)
        {
            if (analysis?.MeshPlan == null || child == null)
            {
                return null;
            }

            if (TryParseFixedPieceIndex(child.name, out int fixedIndex)
                && fixedIndex >= 0
                && fixedIndex < analysis.MeshPlan.FixedRunningRails.Count)
            {
                return CreateRailPieceMatch(
                    child.name,
                    analysis,
                    analysis.MeshPlan.FixedRunningRails[fixedIndex]);
            }

            Renderer? rend = child.GetComponentInChildren<Renderer>();
            if (rend == null)
            {
                return null;
            }

            Vector3 center = rend.bounds.center;
            float bestDist = float.MaxValue;
            RailPiece? bestPiece = null;

            foreach (RailPiece piece in analysis.MeshPlan.FixedRunningRails)
            {
                Vector3 pieceMid = piece.Curve.LinePointAtDistance(piece.Curve.Length * 0.5f).point;
                float dist = Vector3.Distance(center, pieceMid);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestPiece = piece;
                }
            }

            if (bestPiece == null || bestDist > 2f)
            {
                return null;
            }

            return CreateRailPieceMatch(child.name, analysis, bestPiece);
        }

        private static bool TryParseFixedPieceIndex(string name, out int index)
        {
            index = -1;
            const string prefix = "Fixed-";
            if (string.IsNullOrEmpty(name)
                || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int start = prefix.Length;
            int end = start;
            while (end < name.Length && char.IsDigit(name[end]))
            {
                end++;
            }

            return end > start
                && int.TryParse(name.Substring(start, end - start), out index);
        }

        private static RailPieceMatch? CreateRailPieceMatch(
            string objectName,
            SpecialWorkAnalysis analysis,
            RailPiece piece)
        {
            RailCenterline? rail = analysis.Rails.FirstOrDefault(item =>
                string.Equals(item.Id, piece.SourceRailId, StringComparison.OrdinalIgnoreCase));
            if (rail == null)
            {
                return null;
            }

            float start = piece.StartDistance;
            float end = piece.EndDistance;
            ResolveSpecialRenderedInterval(
                objectName,
                analysis,
                piece,
                ref start,
                ref end,
                out IReadOnlyList<LineCurve> flangewayCenters,
                out Vector3 flangewayKeepPoint,
                out float flangewayWidth,
                out Vector3? flangewayCutFocusPoint,
                out float flangewayCutWindowLength,
                out FrogEndpointSide frogSide,
                out float frogDistance);
            start = Mathf.Clamp(start, 0f, rail.Curve.Length);
            end = Mathf.Clamp(end, start, rail.Curve.Length);
            return new RailPieceMatch(
                rail,
                start,
                end,
                flangewayCenters,
                flangewayKeepPoint,
                flangewayWidth,
                flangewayCutFocusPoint,
                flangewayCutWindowLength,
                SpecialWorkHardwareRenderer.ShouldAutoFlipFlangewayKeepSide(analysis, objectName),
                SpecialWorkHardwareRenderer.AutoFlipFlangewayKeepSideIndex(analysis, objectName),
                frogSide,
                frogDistance);
        }

        private static void ResolveSpecialRenderedInterval(
            string objectName,
            SpecialWorkAnalysis analysis,
            RailPiece piece,
            ref float start,
            ref float end,
            out IReadOnlyList<LineCurve> flangewayCenters,
            out Vector3 flangewayKeepPoint,
            out float flangewayWidth,
            out Vector3? flangewayCutFocusPoint,
            out float flangewayCutWindowLength,
            out FrogEndpointSide frogSide,
            out float frogDistance)
        {
            flangewayCenters = Array.Empty<LineCurve>();
            flangewayKeepPoint = Vector3.zero;
            flangewayWidth = 0f;
            flangewayCutFocusPoint = null;
            flangewayCutWindowLength = 0f;
            frogSide = FrogEndpointSide.None;
            frogDistance = 0f;
            SpecialWorkMeshPlan? plan = analysis.MeshPlan;
            if (plan == null || string.IsNullOrEmpty(objectName))
            {
                return;
            }

            foreach (FrogCandidate frog in plan.Frogs.Where(item =>
                item.Intersection.Kind == RailIntersectionKind.CrossingFrogCandidate))
            {
                if (!TryResolveNarrowBranchCrossingRails(
                    frog,
                    out RailCenterline standardRail,
                    out float standardDistance,
                    out RailCenterline narrowRail,
                    out float narrowDistance))
                {
                    continue;
                }

                LineCurve standardFlangeway = null!;
                LineCurve narrowFlangeway = null!;
                bool hasFlangewayCenters = TryResolveRailFlangeway(
                    standardRail,
                    analysis.WheelPaths,
                    out standardFlangeway);
                hasFlangewayCenters = hasFlangewayCenters
                    && TryResolveRailFlangeway(
                        narrowRail,
                        analysis.WheelPaths,
                        out narrowFlangeway);

                bool isGaugeSeparation = string.Equals(
                    analysis.Definition.Preset.Id,
                    SpecialWorkPresetIds.DualSplit,
                    StringComparison.OrdinalIgnoreCase);
                if (NameContains(objectName, "GaugeSeparationDoubleFrog")
                    && isGaugeSeparation
                    && RailIdEquals(piece.SourceRailId, standardRail.Id)
                    && piece.EndDistance < standardDistance
                    && piece.EndDistance >= standardDistance - frog.CutHalfLength - 0.05f)
                {
                    float doubleFrogSetback = Mathf.Max(
                        0.08f,
                        CrossingPointSetback(frog) - 0.08f);
                    end = Mathf.Clamp(
                        standardDistance - doubleFrogSetback,
                        0f,
                        standardRail.Curve.Length);
                    frogSide = FrogEndpointSide.End;
                    frogDistance = standardDistance;
                    if (hasFlangewayCenters)
                    {
                        SetFlangewayCut(
                            new[] { standardFlangeway, narrowFlangeway },
                            standardRail.Curve.LinePointAtDistance(Mathf.Clamp(
                                piece.EndDistance - RenderedMinimumRailPieceLength,
                                0f,
                                standardRail.Curve.Length)).point,
                            plan.Parameters.FlangewayWidth,
                            out flangewayCenters,
                            out flangewayKeepPoint,
                            out flangewayWidth);
                        if (SpecialWorkHardwareRenderer.ShouldLocalizeFrogFlangewayCut(
                            analysis,
                            objectName))
                        {
                            flangewayCutFocusPoint = frog.Intersection.Position;
                            flangewayCutWindowLength =
                                SpecialWorkHardwareRenderer.FrogFlangewayCutWindowLength(frog);
                        }
                    }

                    return;
                }

                if (NameContains(objectName, "StandardThroughFrog")
                    && RailIdEquals(piece.SourceRailId, standardRail.Id)
                    && piece.StartDistance > standardDistance
                    && piece.StartDistance <= standardDistance + frog.CutHalfLength + 0.05f)
                {
                    start = Mathf.Clamp(
                        standardDistance - frog.CutHalfLength,
                        0f,
                        standardRail.Curve.Length);
                    frogSide = FrogEndpointSide.Start;
                    frogDistance = standardDistance;
                    if (hasFlangewayCenters)
                    {
                        SetFlangewayCut(
                            new[] { standardFlangeway, narrowFlangeway },
                            standardRail.Curve.LinePointAtDistance(Mathf.Clamp(
                                piece.StartDistance + RenderedMinimumRailPieceLength,
                                0f,
                                standardRail.Curve.Length)).point,
                            plan.Parameters.FlangewayWidth,
                            out flangewayCenters,
                            out flangewayKeepPoint,
                            out flangewayWidth);
                    }

                    return;
                }

                float narrowBladeSide = SideTowardDirection(
                    narrowRail,
                    narrowDistance,
                    frog.Intersection.Position,
                    DirectionTowardBlades(frog, plan.SwitchBlades));
                bool renderNarrowAfterFrog = narrowBladeSide > 0f;
                if (NameContains(objectName, "NarrowThroughFrog")
                    && renderNarrowAfterFrog
                    && RailIdEquals(piece.SourceRailId, narrowRail.Id)
                    && piece.StartDistance > narrowDistance
                    && piece.StartDistance <= narrowDistance + frog.CutHalfLength + 0.05f)
                {
                    start = Mathf.Clamp(
                        narrowDistance - frog.CutHalfLength,
                        0f,
                        narrowRail.Curve.Length);
                    frogSide = FrogEndpointSide.Start;
                    frogDistance = narrowDistance;
                    if (hasFlangewayCenters)
                    {
                        SetFlangewayCut(
                            new[] { standardFlangeway, narrowFlangeway },
                            narrowRail.Curve.LinePointAtDistance(Mathf.Clamp(
                                piece.StartDistance + RenderedMinimumRailPieceLength,
                                0f,
                                narrowRail.Curve.Length)).point,
                            plan.Parameters.FlangewayWidth,
                            out flangewayCenters,
                            out flangewayKeepPoint,
                            out flangewayWidth);
                    }

                    return;
                }

                if (NameContains(objectName, "NarrowReversedFrog")
                    && !renderNarrowAfterFrog
                    && RailIdEquals(piece.SourceRailId, narrowRail.Id)
                    && piece.EndDistance < narrowDistance
                    && piece.EndDistance >= narrowDistance - frog.CutHalfLength - 0.05f)
                {
                    end = Mathf.Clamp(
                        narrowDistance + frog.CutHalfLength,
                        0f,
                        narrowRail.Curve.Length);
                    frogSide = FrogEndpointSide.End;
                    frogDistance = narrowDistance;
                    if (hasFlangewayCenters)
                    {
                        SetFlangewayCut(
                            new[] { standardFlangeway, narrowFlangeway },
                            narrowRail.Curve.LinePointAtDistance(Mathf.Clamp(
                                piece.EndDistance - RenderedMinimumRailPieceLength,
                                0f,
                                narrowRail.Curve.Length)).point,
                            plan.Parameters.FlangewayWidth,
                            out flangewayCenters,
                            out flangewayKeepPoint,
                            out flangewayWidth);
                    }

                    return;
                }
            }
        }

        private static void SetFlangewayCut(
            IReadOnlyList<LineCurve> centers,
            Vector3 keepPoint,
            float width,
            out IReadOnlyList<LineCurve> flangewayCenters,
            out Vector3 flangewayKeepPoint,
            out float flangewayWidth)
        {
            flangewayCenters = centers;
            flangewayKeepPoint = keepPoint;
            flangewayWidth = width;
        }

        private static bool TryResolveRailFlangeway(
            RailCenterline rail,
            IReadOnlyList<WheelPath> wheelPaths,
            out LineCurve flangeway)
        {
            return TryResolveRailFlangeway(
                rail,
                wheelPaths,
                rail.Side,
                out flangeway);
        }

        private static bool TryResolveRailFlangeway(
            RailCenterline rail,
            IReadOnlyList<WheelPath> wheelPaths,
            RailSide guideSide,
            out LineCurve flangeway)
        {
            flangeway = null!;
            WheelPath? path = wheelPaths.FirstOrDefault(item =>
                    string.Equals(item.Id, rail.WheelPathId, StringComparison.OrdinalIgnoreCase))
                ?? wheelPaths.FirstOrDefault(item =>
                    rail.SourceRouteIds.Any(routeId =>
                        string.Equals(routeId, item.RouteId, StringComparison.OrdinalIgnoreCase)));
            if (path == null)
            {
                return false;
            }

            flangeway = path.FlangeGuide(guideSide);
            return flangeway != null && flangeway.Points.Count() >= 2;
        }

        private static RailSide OppositeSide(RailSide side)
        {
            return side == RailSide.Left ? RailSide.Right : RailSide.Left;
        }

        private static bool NameContains(string name, string value)
        {
            return name.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool RailIdEquals(string first, string second)
        {
            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryResolveNarrowBranchCrossingRails(
            FrogCandidate frog,
            out RailCenterline standardRail,
            out float standardDistance,
            out RailCenterline narrowRail,
            out float narrowDistance)
        {
            standardRail = null!;
            narrowRail = null!;
            standardDistance = 0f;
            narrowDistance = 0f;
            foreach ((RailCenterline rail, float distance) in new[]
            {
                (frog.Intersection.RailA, frog.Intersection.DistanceA),
                (frog.Intersection.RailB, frog.Intersection.DistanceB)
            })
            {
                if (rail.Family == GaugeGraphFamily.Standard)
                {
                    standardRail = rail;
                    standardDistance = distance;
                }
                else if (rail.Family == GaugeGraphFamily.Narrow)
                {
                    narrowRail = rail;
                    narrowDistance = distance;
                }
            }

            return standardRail != null && narrowRail != null;
        }

        private static float CrossingPointSetback(FrogCandidate frog)
        {
            return Mathf.Clamp(
                frog.FlangewaySetback * 0.28f,
                0.16f,
                Mathf.Min(0.24f, frog.CutHalfLength * 0.35f));
        }

        private static float SideTowardDirection(
            RailCenterline rail,
            float centerDistance,
            Vector3 center,
            Vector3 direction)
        {
            const float sampleDistance = 0.35f;
            LinePoint before = rail.Curve.LinePointAtDistance(
                Mathf.Max(0f, centerDistance - sampleDistance));
            LinePoint after = rail.Curve.LinePointAtDistance(
                Mathf.Min(rail.Curve.Length, centerDistance + sampleDistance));
            return Vector3.Dot(after.point - center, direction)
                >= Vector3.Dot(before.point - center, direction)
                ? 1f
                : -1f;
        }

        private static Vector3 DirectionTowardBlades(
            FrogCandidate frog,
            IReadOnlyList<SwitchBladePlan> blades)
        {
            Vector3 bladeCenter = blades.Count == 0
                ? frog.Intersection.Position + frog.NoseDirection
                : blades
                    .Select(blade => blade.BladeCurve.Head.point)
                    .Aggregate(Vector3.zero, (sum, point) => sum + point) / blades.Count;
            Vector3 direction = bladeCenter - frog.Intersection.Position;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : frog.NoseDirection.normalized;
        }

        private sealed class RailPieceMatch
        {
            public readonly RailCenterline Rail;
            public readonly float StartDistance;
            public readonly float EndDistance;
            public readonly IReadOnlyList<LineCurve> FlangewayCenters;
            public readonly Vector3 FlangewayKeepPoint;
            public readonly float FlangewayWidth;
            public readonly Vector3? FlangewayCutFocusPoint;
            public readonly float FlangewayCutWindowLength;
            public readonly bool AutoFlipFlangewaySide;
            public readonly int AutoFlipFlangewayIndex;
            public readonly FrogEndpointSide FrogSide;
            public readonly float FrogDistance;

            public RailPieceMatch(
                RailCenterline rail,
                float start,
                float end,
                IReadOnlyList<LineCurve> flangewayCenters,
                Vector3 flangewayKeepPoint,
                float flangewayWidth,
                Vector3? flangewayCutFocusPoint,
                float flangewayCutWindowLength,
                bool autoFlipFlangewaySide,
                int autoFlipFlangewayIndex,
                FrogEndpointSide frogSide,
                float frogDistance)
            {
                Rail = rail;
                StartDistance = start;
                EndDistance = end;
                FlangewayCenters = flangewayCenters ?? Array.Empty<LineCurve>();
                FlangewayKeepPoint = flangewayKeepPoint;
                FlangewayWidth = flangewayWidth;
                FlangewayCutFocusPoint = flangewayCutFocusPoint;
                FlangewayCutWindowLength = flangewayCutWindowLength;
                AutoFlipFlangewaySide = autoFlipFlangewaySide;
                AutoFlipFlangewayIndex = autoFlipFlangewayIndex;
                FrogSide = frogSide;
                FrogDistance = frogDistance;
            }
        }

        internal enum FrogEndpointSide
        {
            None,
            Start,
            End
        }

        private static Transform? FindSpecialWorkRoot(string nodeId)
        {
            string rootName = "measured-special-work-" + nodeId;
            GameObject? found = GameObject.Find(rootName);
            if (found != null)
            {
                return found.transform;
            }

            TrackNode? node = Graph.Shared?.GetNode(nodeId);
            if (node == null)
            {
                return null;
            }

            Transform? parent = node.transform.parent;
            if (parent == null)
            {
                return null;
            }

            foreach (Transform sibling in parent)
            {
                Transform? child = FindNamedChild(sibling, rootName, 5);
                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        private static Transform? FindNamedChild(Transform root, string name, int depth)
        {
            if (depth <= 0)
            {
                return null;
            }

            foreach (Transform child in root)
            {
                if (string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }

                Transform? found = FindNamedChild(child, name, depth - 1);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        // --- Data classes ---

        internal sealed class PieceState
        {
            public string Name = string.Empty;
            public Transform? Transform;
            public Vector3 OriginalPosition;
            public Quaternion OriginalRotation = Quaternion.identity;
            public Vector3 Right;
            public Vector3 Forward;
            public float LateralOffset;
            public float LongitudinalOffset;
            public float VerticalOffset;
            public float RotationDegrees;
            public string LateralText = "0";
            public string LongitudinalText = "0";
            public string VerticalText = "0";
            public string RotationText = "0";
            public float HeadDelta;
            public float TailDelta;
            public float FrogPullback;
            public bool FlipFlangewaySide;
            public string HeadDeltaText = "0";
            public string TailDeltaText = "0";
            public string FrogPullbackText = "0";
            public string? SourceNodeId;
            public RailCenterline? MatchedRail;
            public float MatchedStartDistance;
            public float MatchedEndDistance;
            public IReadOnlyList<LineCurve> MatchedFlangewayCenters = Array.Empty<LineCurve>();
            public Vector3 MatchedFlangewayKeepPoint;
            public float MatchedFlangewayWidth;
            public Vector3? MatchedFlangewayCutFocusPoint;
            public float MatchedFlangewayCutWindowLength;
            public bool AutoFlipFlangewaySide;
            public int AutoFlipFlangewayIndex = -1;
            public FrogEndpointSide FrogSide;
            public float FrogDistance;
            public bool HasFrogPullback => FrogSide != FrogEndpointSide.None;
            public string? ManualCutRailId;
            public string ManualCutWidthText = "63";
            public float ManualCutWidth = 0.063f;
            public bool HasFlangewayCut =>
                MatchedFlangewayCenters.Count > 0 && MatchedFlangewayWidth > 0f;
            public bool HasRailMatch;
            public Mesh? OriginalMesh;
            public bool Visible = true;
        }

        internal sealed class SavedNodeData
        {
            public SavedPieceData[]? Pieces;
        }

        internal sealed class SavedPieceData
        {
            public string Name = string.Empty;
            public float LateralMm;
            public float LongitudinalMm;
            public float VerticalMm;
            public float RotationDeg;
            public float? HeadDeltaMm;
            public float? TailDeltaMm;
            public float? FrogPullbackMm;
            public bool FlipFlangewaySide;
            public string ManualCutRailId = "";
            public float ManualCutWidthMm = 63f;
            public float LengthDeltaMm;
            public bool Visible = true;
        }
    }
}
