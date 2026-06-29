using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace NarrowGaugeMod
{
    internal static class SpecialWorkHardwareProfileCatalog
    {
        private static readonly string[] SolidNarrowReversedFrogProfiles =
        {
            "solid-narrow-reversed-frog",
            "solid-reversed-frog",
            "solidNarrowReversedFrog",
            "solidReversedFrog"
        };

        private static readonly string[] PushedFrogRailProfiles =
        {
            "pushed-frog-flangeways",
            "push-frog-flangeways",
            "pushedFrogFlangeways"
        };

        private static readonly string[] NoveSplitFrogProfiles =
        {
            "nove-split-frog-catalog",
            "noveSplitFrogCatalog"
        };

        private static readonly string[] U6N0Ids =
        {
            "u6n0",
            "Custom_u6n0",
            "NCustom_u6n0"
        };

        private static readonly string[] NoveIds =
        {
            "Nove",
            "special-work:Nove",
            "fuse-ng:n:Nove"
        };

        private static readonly string[] SuppressSpecialWorkTieProfiles =
        {
            "suppress-special-work-ties",
            "suppressSpecialWorkTies",
            "native-switch-ties"
        };

        public static bool ShouldRenderSolidFrogRail(
            SpecialWorkAnalysis analysis,
            string objectName)
        {
            if (analysis == null
                || !NameContains(objectName, "NarrowReversedFrog"))
            {
                return false;
            }

            return HasHardwareProfile(analysis, SolidNarrowReversedFrogProfiles)
                || HasObjectOverride(analysis, objectName, "solidFrogRails")
                || HasObjectOverride(analysis, objectName, "solidRails");
        }

        public static bool TryGetPushedFrogRail(
            SpecialWorkAnalysis analysis,
            string objectName,
            out PushedFrogRailProfile profile)
        {
            profile = default;
            if (analysis == null || string.IsNullOrWhiteSpace(objectName))
            {
                return false;
            }

            bool authoredProfile = HasHardwareProfile(analysis, PushedFrogRailProfiles)
                || HasObjectOverride(analysis, objectName, "pushedFrogRails")
                || HasObjectOverride(analysis, objectName, "pushFrogRails");
            bool builtInU6N0 =
                MatchesAnyDefinitionId(analysis, U6N0Ids)
                && (NameContains(objectName, "Fixed-9-StandardThroughFrog")
                    || NameContains(objectName, "Fixed-16-NarrowReversedFrog"));
            if (!authoredProfile && !builtInU6N0)
            {
                return false;
            }

            profile = new PushedFrogRailProfile(
                spanMultiplier: builtInU6N0 ? 1.35f : 1.2f,
                cutWindowMultiplier: builtInU6N0 ? 1.15f : 1.0f,
                minimumCutWindow: builtInU6N0 ? 0.75f : 0.6f);
            return true;
        }

        public static bool ShouldUseNoveSplitFrogCatalog(SpecialWorkDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            return MatchesAnyDefinitionId(definition, NoveIds)
                || HasHardwareProfile(definition, NoveSplitFrogProfiles);
        }

        public static bool ShouldSuppressSpecialWorkTies(SpecialWorkAnalysis analysis)
        {
            if (analysis == null)
            {
                return false;
            }

            return MatchesAnyDefinitionId(analysis, NoveIds)
                || HasHardwareProfile(analysis, SuppressSpecialWorkTieProfiles);
        }

        private static bool HasHardwareProfile(
            SpecialWorkAnalysis analysis,
            IReadOnlyCollection<string> profileNames)
        {
            return HasHardwareProfile(analysis.Definition, profileNames);
        }

        private static bool HasHardwareProfile(
            SpecialWorkDefinition definition,
            IReadOnlyCollection<string> profileNames)
        {
            return TryGetParameter(definition, "hardwareProfile", out JToken? value)
                    && TokenContainsAny(value!, profileNames)
                || TryGetParameter(definition, "hardwareProfiles", out value)
                    && TokenContainsAny(value!, profileNames);
        }

        private static bool HasObjectOverride(
            SpecialWorkAnalysis analysis,
            string objectName,
            string parameterName)
        {
            return TryGetParameter(analysis, parameterName, out JToken? value)
                && TokenContainsObjectName(value!, objectName);
        }

        private static bool TryGetParameter(
            SpecialWorkAnalysis analysis,
            string name,
            out JToken? value)
        {
            return TryGetParameter(analysis.Definition, name, out value);
        }

        private static bool TryGetParameter(
            SpecialWorkDefinition definition,
            string name,
            out JToken? value)
        {
            value = null;
            return definition.Authoring?.Parameters.TryGetValue(name, out value) == true;
        }

        private static bool TokenContainsAny(
            JToken token,
            IReadOnlyCollection<string> candidates)
        {
            foreach (string value in FlattenStringTokens(token))
            {
                if (candidates.Any(candidate =>
                        string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TokenContainsObjectName(JToken token, string objectName)
        {
            foreach (string value in FlattenStringTokens(token))
            {
                if (string.Equals(value, "*", StringComparison.OrdinalIgnoreCase)
                    || NameContains(objectName, value))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> FlattenStringTokens(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                yield break;
            }

            if (token.Type == JTokenType.String)
            {
                string? value = token.Value<string>();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value!.Trim();
                }

                yield break;
            }

            if (token is JArray array)
            {
                foreach (JToken item in array)
                {
                    foreach (string value in FlattenStringTokens(item))
                    {
                        yield return value;
                    }
                }
            }
        }

        private static bool MatchesAnyDefinitionId(
            SpecialWorkAnalysis analysis,
            IReadOnlyCollection<string> candidates)
        {
            return MatchesAnyDefinitionId(analysis.Definition, candidates);
        }

        private static bool MatchesAnyDefinitionId(
            SpecialWorkDefinition definition,
            IReadOnlyCollection<string> candidates)
        {
            return DefinitionIds(definition).Any(id =>
                candidates.Any(candidate =>
                    string.Equals(id, candidate, StringComparison.OrdinalIgnoreCase)
                    || id.EndsWith(":" + candidate, StringComparison.OrdinalIgnoreCase)
                    || id.EndsWith("_" + candidate, StringComparison.OrdinalIgnoreCase)
                    || id.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static IEnumerable<string> DefinitionIds(SpecialWorkAnalysis analysis)
        {
            return DefinitionIds(analysis.Definition);
        }

        private static IEnumerable<string> DefinitionIds(SpecialWorkDefinition definition)
        {
            yield return definition.Id;

            foreach (string nodeId in definition.NativeSwitchNodeIds)
            {
                yield return nodeId;
            }

            SpecialWorkAuthoringBinding? authoring = definition.Authoring;
            if (authoring != null)
            {
                yield return authoring.Id;
                yield return authoring.AnchorNodeId;
            }
        }

        private static bool NameContains(string value, string expected)
        {
            return !string.IsNullOrWhiteSpace(value)
                && !string.IsNullOrWhiteSpace(expected)
                && value.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    internal readonly struct PushedFrogRailProfile
    {
        public PushedFrogRailProfile(
            float spanMultiplier,
            float cutWindowMultiplier,
            float minimumCutWindow)
        {
            SpanMultiplier = spanMultiplier;
            CutWindowMultiplier = cutWindowMultiplier;
            MinimumCutWindow = minimumCutWindow;
        }

        public float SpanMultiplier { get; }
        public float CutWindowMultiplier { get; }
        public float MinimumCutWindow { get; }

        public float CutWindowLength(FrogCandidate frog)
        {
            return Mathf.Max(MinimumCutWindow, frog.CutHalfLength * CutWindowMultiplier);
        }
    }
}
