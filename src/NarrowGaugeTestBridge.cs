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
                nodeId = request["nodeId"]?.ToString() ?? string.Empty;
                TrackNode? node = string.IsNullOrEmpty(nodeId) ? null : Graph.Shared?.GetNode(nodeId);
                if (node == null)
                {
                    WriteResult(false, $"Node '{nodeId}' not found in the active graph.");
                }
                else
                {
                    Vector3 gamePoint = WorldTransformer.WorldToGame(node.transform.position);
                    CameraSelector.shared.JumpToPoint(gamePoint, node.transform.rotation, null);
                    WriteResult(true, $"Jumped to '{nodeId}' at {node.transform.position}.");
                }
            }
            catch (Exception exception)
            {
                WriteResult(false, $"Error resolving '{nodeId}': {exception}");
            }
            finally
            {
                File.Delete(requestPath);
            }
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
