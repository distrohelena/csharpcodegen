using System.Text.Json;

namespace cs2.cpp {
    /// <summary>
    /// Loads and validates caller-owned feature metadata for the generic C#-to-C++ converter.
    /// </summary>
    public static class CPPExternalFeatureCatalogLoader {
        /// <summary>
        /// Loads an external feature catalog from a metadata file.
        /// </summary>
        /// <param name="filePath">Full path to the metadata file.</param>
        /// <returns>The validated external feature catalog.</returns>
        public static CPPExternalFeatureCatalog LoadFromFile(string filePath) {
            if (string.IsNullOrWhiteSpace(filePath)) {
                throw new ArgumentException("Feature metadata file path must not be empty.", nameof(filePath));
            }

            if (!File.Exists(filePath)) {
                throw new FileNotFoundException("Feature metadata file was not found.", filePath);
            }

            return LoadFromJson(File.ReadAllText(filePath));
        }

        /// <summary>
        /// Loads an external feature catalog from a JSON payload.
        /// </summary>
        /// <param name="json">JSON payload containing caller-owned feature metadata.</param>
        /// <returns>The validated external feature catalog.</returns>
        public static CPPExternalFeatureCatalog LoadFromJson(string json) {
            if (string.IsNullOrWhiteSpace(json)) {
                throw new ArgumentException("Feature metadata JSON must not be empty.", nameof(json));
            }

            using JsonDocument jsonDocument = JsonDocument.Parse(json);
            JsonElement rootElement = jsonDocument.RootElement;
            if (rootElement.ValueKind != JsonValueKind.Object) {
                throw new InvalidOperationException("Feature metadata root must be a JSON object.");
            }

            IReadOnlyList<CPPExternalFeatureDefinition> features = ReadFeatures(rootElement);
            Dictionary<string, CPPExternalFeatureDefinition> featureMap = features.ToDictionary(feature => feature.Id, StringComparer.Ordinal);
            IReadOnlyList<CPPExternalFeatureRootRule> rootRules = ReadRootRules(rootElement, featureMap);
            IReadOnlyList<CPPExternalRuntimeRequirementOwnership> runtimeRequirements = ReadRuntimeRequirements(rootElement, featureMap);

            return new CPPExternalFeatureCatalog(features, rootRules, runtimeRequirements);
        }

        static IReadOnlyList<CPPExternalFeatureDefinition> ReadFeatures(JsonElement rootElement) {
            if (!rootElement.TryGetProperty("features", out JsonElement featuresElement)) {
                return Array.Empty<CPPExternalFeatureDefinition>();
            }

            if (featuresElement.ValueKind != JsonValueKind.Array) {
                throw new InvalidOperationException("Feature metadata 'features' must be an array.");
            }

            List<CPPExternalFeatureDefinition> features = new List<CPPExternalFeatureDefinition>();
            HashSet<string> seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement featureElement in featuresElement.EnumerateArray()) {
                if (featureElement.ValueKind != JsonValueKind.Object) {
                    throw new InvalidOperationException("Each feature metadata entry must be an object.");
                }

                string id = ReadRequiredStringProperty(featureElement, "id", "Feature metadata entry");
                string normalizedId = NormalizeId(id, "Feature metadata id");
                if (!seenIds.Add(normalizedId)) {
                    throw new InvalidOperationException($"Feature metadata contains a duplicate feature id '{normalizedId}'.");
                }

                CPPFeatureMode defaultMode = ParseFeatureMode(
                    ReadRequiredStringProperty(featureElement, "defaultMode", $"Feature '{normalizedId}'"),
                    normalizedId);
                CPPFeatureConflictPolicy conflictPolicy = ParseConflictPolicy(
                    ReadRequiredStringProperty(featureElement, "conflictPolicy", $"Feature '{normalizedId}'"),
                    normalizedId);

                features.Add(new CPPExternalFeatureDefinition(normalizedId, defaultMode, conflictPolicy));
            }

            return features;
        }

        static IReadOnlyList<CPPExternalFeatureRootRule> ReadRootRules(
            JsonElement rootElement,
            IReadOnlyDictionary<string, CPPExternalFeatureDefinition> featureMap) {
            if (!rootElement.TryGetProperty("rootRules", out JsonElement rootRulesElement)) {
                return Array.Empty<CPPExternalFeatureRootRule>();
            }

            if (rootRulesElement.ValueKind != JsonValueKind.Array) {
                throw new InvalidOperationException("Feature metadata 'rootRules' must be an array.");
            }

            List<CPPExternalFeatureRootRule> rootRules = new List<CPPExternalFeatureRootRule>();
            foreach (JsonElement rootRuleElement in rootRulesElement.EnumerateArray()) {
                if (rootRuleElement.ValueKind != JsonValueKind.Object) {
                    throw new InvalidOperationException("Each root rule entry must be an object.");
                }

                string typeName = ReadRequiredStringProperty(rootRuleElement, "typeName", "Root rule entry");
                if (string.IsNullOrWhiteSpace(typeName)) {
                    throw new InvalidOperationException("Root rule typeName must not be empty.");
                }

                IReadOnlyList<string> featureIds = ReadFeatureIdArray(rootRuleElement, "featureIds", "Root rule entry", featureMap);
                rootRules.Add(new CPPExternalFeatureRootRule(typeName.Trim(), featureIds));
            }

            return rootRules;
        }

        static IReadOnlyList<CPPExternalRuntimeRequirementOwnership> ReadRuntimeRequirements(
            JsonElement rootElement,
            IReadOnlyDictionary<string, CPPExternalFeatureDefinition> featureMap) {
            if (!rootElement.TryGetProperty("runtimeRequirements", out JsonElement runtimeRequirementsElement)) {
                return Array.Empty<CPPExternalRuntimeRequirementOwnership>();
            }

            if (runtimeRequirementsElement.ValueKind != JsonValueKind.Array) {
                throw new InvalidOperationException("Feature metadata 'runtimeRequirements' must be an array.");
            }

            List<CPPExternalRuntimeRequirementOwnership> runtimeRequirements = new List<CPPExternalRuntimeRequirementOwnership>();
            foreach (JsonElement runtimeRequirementElement in runtimeRequirementsElement.EnumerateArray()) {
                if (runtimeRequirementElement.ValueKind != JsonValueKind.Object) {
                    throw new InvalidOperationException("Each runtime requirement entry must be an object.");
                }

                string requirementId = ReadRequiredStringProperty(runtimeRequirementElement, "requirementId", "Runtime requirement entry");
                if (string.IsNullOrWhiteSpace(requirementId)) {
                    throw new InvalidOperationException("Runtime requirement id must not be empty.");
                }

                IReadOnlyList<string> featureIds = ReadFeatureIdArray(runtimeRequirementElement, "featureIds", "Runtime requirement entry", featureMap);
                runtimeRequirements.Add(new CPPExternalRuntimeRequirementOwnership(requirementId.Trim(), featureIds));
            }

            return runtimeRequirements;
        }

        static IReadOnlyList<string> ReadFeatureIdArray(
            JsonElement ownerElement,
            string propertyName,
            string ownerDescription,
            IReadOnlyDictionary<string, CPPExternalFeatureDefinition> featureMap) {
            if (!ownerElement.TryGetProperty(propertyName, out JsonElement featureIdsElement)) {
                throw new InvalidOperationException($"{ownerDescription} must declare '{propertyName}'.");
            }

            if (featureIdsElement.ValueKind != JsonValueKind.Array) {
                throw new InvalidOperationException($"{ownerDescription} property '{propertyName}' must be an array.");
            }

            List<string> featureIds = new List<string>();
            HashSet<string> seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement featureIdElement in featureIdsElement.EnumerateArray()) {
                if (featureIdElement.ValueKind != JsonValueKind.String) {
                    throw new InvalidOperationException($"{ownerDescription} property '{propertyName}' must contain only strings.");
                }

                string featureId = NormalizeId(featureIdElement.GetString(), $"{ownerDescription} feature id");
                if (!featureMap.ContainsKey(featureId)) {
                    throw new InvalidOperationException($"{ownerDescription} references unknown feature id '{featureId}'.");
                }

                if (seenIds.Add(featureId)) {
                    featureIds.Add(featureId);
                }
            }

            if (featureIds.Count == 0) {
                throw new InvalidOperationException($"{ownerDescription} property '{propertyName}' must contain at least one feature id.");
            }

            return featureIds;
        }

        static string ReadRequiredStringProperty(JsonElement ownerElement, string propertyName, string ownerDescription) {
            if (!ownerElement.TryGetProperty(propertyName, out JsonElement propertyElement)) {
                throw new InvalidOperationException($"{ownerDescription} must declare '{propertyName}'.");
            }

            if (propertyElement.ValueKind != JsonValueKind.String) {
                throw new InvalidOperationException($"{ownerDescription} property '{propertyName}' must be a string.");
            }

            return propertyElement.GetString() ?? string.Empty;
        }

        static string NormalizeId(string id, string description) {
            if (string.IsNullOrWhiteSpace(id)) {
                throw new InvalidOperationException($"{description} must not be empty.");
            }

            return id.Trim();
        }

        static CPPFeatureMode ParseFeatureMode(string value, string featureId) {
            string normalizedValue = NormalizeId(value, $"Feature '{featureId}' property 'defaultMode'").ToLowerInvariant();
            if (string.Equals(normalizedValue, "auto", StringComparison.Ordinal)) {
                return CPPFeatureMode.Auto;
            }

            if (string.Equals(normalizedValue, "enabled", StringComparison.Ordinal)) {
                return CPPFeatureMode.Enabled;
            }

            if (string.Equals(normalizedValue, "disabled", StringComparison.Ordinal)) {
                return CPPFeatureMode.Disabled;
            }

            throw new InvalidOperationException(
                $"Feature '{featureId}' property 'defaultMode' must be one of: auto, enabled, disabled.");
        }

        static CPPFeatureConflictPolicy ParseConflictPolicy(string value, string featureId) {
            string normalizedValue = NormalizeId(value, $"Feature '{featureId}' property 'conflictPolicy'").ToLowerInvariant();
            if (string.Equals(normalizedValue, "warn", StringComparison.Ordinal)) {
                return CPPFeatureConflictPolicy.Warn;
            }

            if (string.Equals(normalizedValue, "error", StringComparison.Ordinal)) {
                return CPPFeatureConflictPolicy.Error;
            }

            throw new InvalidOperationException(
                $"Feature '{featureId}' property 'conflictPolicy' must be one of: warn, error.");
        }
    }
}
