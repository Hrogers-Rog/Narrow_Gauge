using System;
using System.Collections.Generic;
using System.Linq;
using FUSE.Loading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NarrowGaugeMod
{
    internal sealed class SpecialWorkAuthoringSnapshot
    {
        public SpecialWorkAuthoringSnapshot(
            IEnumerable<SpecialWorkAuthoringBinding> bindings,
            IEnumerable<string> issues)
        {
            Bindings = (bindings ?? Enumerable.Empty<SpecialWorkAuthoringBinding>()).ToArray();
            Issues = (issues ?? Enumerable.Empty<string>()).ToArray();
        }

        public IReadOnlyList<SpecialWorkAuthoringBinding> Bindings { get; }
        public IReadOnlyList<string> Issues { get; }
    }

    /// <summary>
    /// Reads companion-module authoring data from FUSE's generic extension bag.
    /// FUSE remains unaware of NarrowGauge-specific schemas and behavior.
    /// </summary>
    internal static class SpecialWorkAuthoring
    {
        public const string ExtensionKey = "narrowGauge.specialWork";
        private const int SupportedVersion = 1;

        public static SpecialWorkAuthoringSnapshot Load()
        {
            var bindings = new List<SpecialWorkAuthoringBinding>();
            var issues = new List<string>();

            foreach (FuseLoadedMod loaded in FuseModLoader.GetLoadedModsInOrder())
            {
                string packageId = loaded?.Definition?.Id ?? string.Empty;
                Dictionary<string, object>? extensions = loaded?.Definition?.Extensions;
                if (extensions == null
                    || !extensions.TryGetValue(ExtensionKey, out object raw)
                    || raw == null)
                {
                    continue;
                }

                try
                {
                    JObject root = ToObject(raw);
                    int version = root.Value<int?>("version") ?? SupportedVersion;
                    if (version != SupportedVersion)
                    {
                        issues.Add(
                            $"Package '{packageId}' uses unsupported {ExtensionKey} version {version}; " +
                            $"supported version is {SupportedVersion}.");
                        continue;
                    }

                    ParseObjects(packageId, root["objects"], bindings, issues);
                }
                catch (Exception ex)
                {
                    issues.Add(
                        $"Package '{packageId}' has invalid {ExtensionKey} data: {ex.Message}");
                }
            }

            foreach (IGrouping<string, SpecialWorkAuthoringBinding> duplicate in bindings
                .GroupBy(binding => binding.Id, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1))
            {
                issues.Add(
                    $"Authored special-work id '{duplicate.Key}' is declared more than once: " +
                    string.Join(", ", duplicate.Select(item => item.PackageId)));
            }

            foreach (IGrouping<string, SpecialWorkAuthoringBinding> duplicate in bindings
                .Where(binding => binding.Enabled)
                .GroupBy(binding => binding.AnchorNodeId, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1))
            {
                issues.Add(
                    $"Anchor node '{duplicate.Key}' is assigned to more than one enabled special-work object.");
            }

            return new SpecialWorkAuthoringSnapshot(bindings, issues);
        }

        public static IReadOnlyList<SpecialWorkDefinition> Apply(
            IEnumerable<SpecialWorkDefinition> discovered,
            SpecialWorkAuthoringSnapshot snapshot,
            out IReadOnlyList<string> issues)
        {
            var result = (discovered ?? Enumerable.Empty<SpecialWorkDefinition>()).ToList();
            var applyIssues = new List<string>(snapshot?.Issues ?? Array.Empty<string>());
            if (snapshot == null)
            {
                issues = applyIssues;
                return result;
            }

            foreach (SpecialWorkAuthoringBinding binding in snapshot.Bindings.Where(item => item.Enabled))
            {
                if (!SpecialWorkPresetCatalog.TryGet(binding.PresetId, out SpecialWorkPresetDefinition preset))
                {
                    applyIssues.Add(
                        $"Authored special work '{binding.Id}' in '{binding.PackageId}' selects unknown preset " +
                        $"'{binding.PresetId}'.");
                    continue;
                }

                int matchIndex = result.FindIndex(definition =>
                    string.Equals(
                        definition.Id,
                        "special-work:" + binding.AnchorNodeId,
                        StringComparison.OrdinalIgnoreCase)
                    || definition.NativeSwitchNodeIds.Contains(
                        binding.AnchorNodeId,
                        StringComparer.OrdinalIgnoreCase));
                if (matchIndex < 0)
                {
                    applyIssues.Add(
                        $"Authored special work '{binding.Id}' in '{binding.PackageId}' could not derive " +
                        $"preset '{preset.Id}' at anchor node '{binding.AnchorNodeId}'.");
                    continue;
                }

                SpecialWorkDefinition derived = result[matchIndex];
                if (!IsCompatible(derived, preset, out string reason))
                {
                    applyIssues.Add(
                        $"Authored special work '{binding.Id}' in '{binding.PackageId}' cannot use preset " +
                        $"'{preset.Id}' at '{binding.AnchorNodeId}': {reason}");
                    continue;
                }

                result[matchIndex] = new SpecialWorkDefinition(
                    binding.Id,
                    preset,
                    derived.Ports,
                    derived.Routes,
                    derived.SwitchGroups,
                    derived.NativeSwitchNodeIds,
                    binding);
            }

            issues = applyIssues;
            return result;
        }

        private static void ParseObjects(
            string packageId,
            JToken? token,
            ICollection<SpecialWorkAuthoringBinding> bindings,
            ICollection<string> issues)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                issues.Add($"Package '{packageId}' {ExtensionKey} extension has no 'objects' collection.");
                return;
            }

            if (token is JArray array)
            {
                foreach (JToken item in array)
                {
                    ParseObject(packageId, item as JObject, null, bindings, issues);
                }

                return;
            }

            if (token is JObject keyedObjects)
            {
                foreach (JProperty property in keyedObjects.Properties())
                {
                    ParseObject(packageId, property.Value as JObject, property.Name, bindings, issues);
                }

                return;
            }

            issues.Add(
                $"Package '{packageId}' {ExtensionKey}.objects must be an array or object keyed by id.");
        }

        private static void ParseObject(
            string packageId,
            JObject? data,
            string? keyedId,
            ICollection<SpecialWorkAuthoringBinding> bindings,
            ICollection<string> issues)
        {
            if (data == null)
            {
                issues.Add($"Package '{packageId}' contains a non-object special-work entry.");
                return;
            }

            string id = ReadString(data, "id") ?? keyedId ?? string.Empty;
            string preset = ReadString(data, "preset") ?? string.Empty;
            string anchorNode = ReadString(data, "anchorNode") ?? string.Empty;
            bool enabled = data.Value<bool?>("enabled") ?? true;

            if (string.IsNullOrWhiteSpace(id)
                || string.IsNullOrWhiteSpace(preset)
                || string.IsNullOrWhiteSpace(anchorNode))
            {
                issues.Add(
                    $"Package '{packageId}' special-work entry requires non-empty id, preset, and anchorNode.");
                return;
            }

            var parameters = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
            if (data["parameters"] is JObject parameterObject)
            {
                foreach (JProperty parameter in parameterObject.Properties())
                {
                    parameters[parameter.Name] = parameter.Value.DeepClone();
                }
            }
            else if (data["parameters"] != null && data["parameters"]!.Type != JTokenType.Null)
            {
                issues.Add(
                    $"Package '{packageId}' special-work '{id}' parameters must be an object.");
            }

            bindings.Add(new SpecialWorkAuthoringBinding(
                packageId,
                id,
                preset,
                anchorNode,
                enabled,
                parameters));
        }

        private static bool IsCompatible(
            SpecialWorkDefinition derived,
            SpecialWorkPresetDefinition selected,
            out string reason)
        {
            if (derived.Routes.Count != selected.LogicalRoutes)
            {
                reason =
                    $"derived {derived.Routes.Count} logical routes, selected preset requires {selected.LogicalRoutes}";
                return false;
            }

            if (derived.NativeSwitchNodeIds.Count != selected.NativeSwitchNodes)
            {
                reason =
                    $"derived {derived.NativeSwitchNodeIds.Count} native switch nodes, selected preset requires " +
                    selected.NativeSwitchNodes;
                return false;
            }

            if (derived.Preset.Topology != selected.Topology)
            {
                reason =
                    $"derived topology is {derived.Preset.Topology}, selected preset requires {selected.Topology}";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static JObject ToObject(object raw)
        {
            if (raw is JObject value)
            {
                return value;
            }

            if (raw is JToken token)
            {
                if (token.Type != JTokenType.Object)
                {
                    throw new JsonException("extension root must be an object");
                }

                return (JObject)token;
            }

            return JObject.FromObject(raw);
        }

        private static string? ReadString(JObject value, string property)
        {
            return value[property]?.Type == JTokenType.String
                ? value.Value<string>(property)?.Trim()
                : null;
        }
    }
}
