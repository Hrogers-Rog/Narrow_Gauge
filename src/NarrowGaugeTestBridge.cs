using System;
using System.IO;
using Helpers;
using Newtonsoft.Json.Linq;
using Track;
using UnityEngine;

namespace NarrowGaugeMod
{
    /// <summary>
    /// Dev-only file-based bridge that moves the camera to a specific track node.
    /// FUSE.TestBridge's own protocol can launch/load/screenshot but has no way to
    /// aim the camera at a specific switch, so a screenshot only ever shows
    /// whatever the camera already happened to be looking at. This fills that one
    /// gap without touching the separate FUSE repo: drop
    /// <c>ng_goto_request.json</c> (<c>{"nodeId": "Nove"}</c>) next to this mod's
    /// deployed DLL, and within <see cref="PollIntervalSeconds"/> the camera jumps
    /// there and <c>ng_goto_result.json</c> reports what happened.
    ///
    /// Gated behind the <see cref="EnableEnvVar"/> environment variable so it is
    /// completely inert for normal players, mirroring FUSE.TestBridge's own
    /// dev-only gating.
    /// </summary>
    internal sealed class NarrowGaugeTestBridge : MonoBehaviour
    {
        private const string EnableEnvVar = "NARROWGAUGE_TEST_BRIDGE";
        private const string EnableSentinelFileName = "ng_test_bridge_enabled";
        private const string RequestFileName = "ng_goto_request.json";
        private const string ResultFileName = "ng_goto_result.json";
        private const float PollIntervalSeconds = 0.5f;

        private string? _folder;
        private float _nextPollTime;

        private void Awake()
        {
            _folder = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);

            bool enabledByEnv = Environment.GetEnvironmentVariable(EnableEnvVar) == "1";
            bool enabledBySentinel = _folder != null
                && File.Exists(Path.Combine(_folder, EnableSentinelFileName));
            if (!enabledByEnv && !enabledBySentinel)
            {
                enabled = false;
                return;
            }

            Main.Log(
                $"[TestBridge] NarrowGaugeTestBridge enabled via {(enabledByEnv ? EnableEnvVar : EnableSentinelFileName)}"
                + $" - watching '{_folder}' for '{RequestFileName}'.");
        }

        private void Update()
        {
            if (_folder == null || Time.unscaledTime < _nextPollTime)
            {
                return;
            }

            _nextPollTime = Time.unscaledTime + PollIntervalSeconds;
            string requestPath = Path.Combine(_folder, RequestFileName);
            if (!File.Exists(requestPath))
            {
                return;
            }

            string nodeId = "<unparsed>";
            try
            {
                JObject request = JObject.Parse(File.ReadAllText(requestPath));
                string action = request["action"]?.ToString() ?? string.Empty;
                if (action == "exportPlans")
                {
                    string path = SpecialWorkPlanExporter.ExportAll();
                    WriteResult(true, $"Exported measured 2D special-work plans to {path}.");
                    return;
                }

                if (action == "throwSwitch")
                {
                    string throwNodeId = request["nodeId"]?.ToString() ?? string.Empty;
                    bool thrown = request["thrown"]?.ToObject<bool>() ?? true;
                    if (string.IsNullOrEmpty(throwNodeId))
                    {
                        WriteResult(false, "Missing 'nodeId' for throwSwitch.");
                    }
                    else if (TrainController.Shared == null)
                    {
                        WriteResult(false, "TrainController.Shared is null.");
                    }
                    else if (!TrainController.Shared.TrySetSwitch(
                        throwNodeId, thrown, "system:NarrowGaugeTestBridge", out string errorMessage))
                    {
                        WriteResult(false, $"TrySetSwitch failed for '{throwNodeId}': {errorMessage}");
                    }
                    else
                    {
                        WriteResult(true, $"Switch '{throwNodeId}' set to thrown={thrown}.");
                    }
                    return;
                }

                JArray? eyeArray = request["eye"] as JArray;
                JArray? lookAtArray = request["lookAt"] as JArray;
                if (eyeArray != null && lookAtArray != null)
                {
                    Vector3 eye = ParseVector3(eyeArray);
                    Vector3 lookAt = ParseVector3(lookAtArray);
                    Quaternion rotation = Quaternion.LookRotation((lookAt - eye).normalized);
                    Vector3 gamePoint = WorldTransformer.WorldToGame(eye);
                    CameraSelector.shared.JumpToPoint(gamePoint, rotation, CameraSelector.CameraIdentifier.FirstPerson);
                    WriteResult(true, $"Jumped (explicit/first-person): eye={eye} lookingAt={lookAt}.");
                    return;
                }

                nodeId = request["nodeId"]?.ToString() ?? string.Empty;
                bool closeUp = request["closeUp"]?.ToObject<bool>() ?? false;
                float distance = request["distance"]?.ToObject<float>() ?? 4f;
                float height = request["height"]?.ToObject<float>() ?? 1.7f;
                float headingOffsetDegrees = request["headingOffset"]?.ToObject<float>() ?? 0f;
                TrackNode? node = string.IsNullOrEmpty(nodeId) ? null : Graph.Shared?.GetNode(nodeId);
                if (node == null)
                {
                    WriteResult(false, $"Node '{nodeId}' not found in the active graph.");
                }
                else if (!closeUp)
                {
                    Vector3 gamePoint = WorldTransformer.WorldToGame(node.transform.position);
                    CameraSelector.shared.JumpToPoint(gamePoint, node.transform.rotation, null);
                    WriteResult(true, $"Jumped to '{nodeId}' at {node.transform.position}.");
                }
                else
                {
                    Vector3 nodePosition = node.transform.position;
                    Quaternion heading = node.transform.rotation * Quaternion.Euler(0f, headingOffsetDegrees, 0f);
                    Vector3 eyePosition = nodePosition - (heading * Vector3.forward * distance) + Vector3.up * height;
                    Vector3 lookTarget = nodePosition + Vector3.up * 0.3f;
                    Quaternion lookRotation = Quaternion.LookRotation((lookTarget - eyePosition).normalized);
                    Vector3 gamePoint = WorldTransformer.WorldToGame(eyePosition);
                    CameraSelector.shared.JumpToPoint(gamePoint, lookRotation, CameraSelector.CameraIdentifier.FirstPerson);
                    WriteResult(true, $"Jumped (close-up/first-person) near '{nodeId}': eye={eyePosition} lookingAt={nodePosition}.");
                }
            }
            catch (Exception exception)
            {
                WriteResult(false, $"Error resolving '{nodeId}': {exception}");
            }
            finally
            {
                if (File.Exists(requestPath))
                {
                    File.Delete(requestPath);
                }
            }
        }

        private static Vector3 ParseVector3(JArray array)
        {
            return new Vector3(
                array[0].ToObject<float>(),
                array[1].ToObject<float>(),
                array[2].ToObject<float>());
        }

        private void WriteResult(bool ok, string message)
        {
            var result = new JObject
            {
                ["ok"] = ok,
                ["message"] = message,
            };
            File.WriteAllText(Path.Combine(_folder!, ResultFileName), result.ToString());
        }
    }
}
