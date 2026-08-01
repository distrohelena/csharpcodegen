namespace cs2.cpp {
    /// <summary>
    /// Resolves generic codegen option values into build-feature profile overrides.
    /// </summary>
    public static class CPPFeatureProfileOptionResolver {
        /// <summary>
        /// Builds one feature profile from generic selected options and the active external feature catalog.
        /// </summary>
        /// <param name="selectedOptions">Caller-selected generic option values.</param>
        /// <param name="featureCatalog">Loaded external feature catalog used to validate feature ids.</param>
        /// <returns>Resolved feature profile.</returns>
        public static CPPBuildFeatureProfile BuildProfile(
            IReadOnlyDictionary<string, string> selectedOptions,
            CPPExternalFeatureCatalog featureCatalog) {
            if (selectedOptions == null) {
                throw new ArgumentNullException(nameof(selectedOptions));
            }
            if (featureCatalog == null) {
                throw new ArgumentNullException(nameof(featureCatalog));
            }

            CPPBuildFeatureProfile profile = CPPBuildFeatureProfile.CreateDefault();
            HashSet<string> knownFeatureIds = new(
                featureCatalog.Features.Select(feature => feature.Id),
                StringComparer.Ordinal);
            HashSet<string> enabledFeatureIds = ParseFeatureIds(
                selectedOptions,
                CPPCodegenOptionNames.EnabledFeatures,
                "enabled",
                knownFeatureIds);
            HashSet<string> disabledFeatureIds = ParseFeatureIds(
                selectedOptions,
                CPPCodegenOptionNames.ForcedDisabledFeatures,
                "forced-disabled",
                knownFeatureIds);

            foreach (string featureId in enabledFeatureIds) {
                if (disabledFeatureIds.Contains(featureId)) {
                    throw new InvalidOperationException($"Codegen feature '{featureId}' cannot be both enabled and forced-disabled.");
                }
                profile.WithMode(featureId, CPPFeatureMode.Enabled);
            }
            foreach (string featureId in disabledFeatureIds) {
                profile.WithMode(featureId, CPPFeatureMode.Disabled);
            }

            return profile;
        }

        /// <summary>
        /// Parses one feature option into a validated set of catalog feature identifiers.
        /// </summary>
        /// <param name="selectedOptions">Caller-selected generic option values.</param>
        /// <param name="optionName">Stable option name whose serialized value should be parsed.</param>
        /// <param name="modeDescription">Human-readable feature mode used in validation failures.</param>
        /// <param name="knownFeatureIds">Catalog feature identifiers accepted by the active build.</param>
        /// <returns>Unique feature identifiers selected by the option.</returns>
        static HashSet<string> ParseFeatureIds(
            IReadOnlyDictionary<string, string> selectedOptions,
            string optionName,
            string modeDescription,
            IReadOnlySet<string> knownFeatureIds) {
            HashSet<string> featureIds = new(StringComparer.Ordinal);
            if (!selectedOptions.TryGetValue(optionName, out string serializedFeatureIds)
                || string.IsNullOrWhiteSpace(serializedFeatureIds)) {
                return featureIds;
            }

            string[] serializedIds = serializedFeatureIds.Split(
                [',', ';', ' '],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int index = 0; index < serializedIds.Length; index++) {
                string featureId = serializedIds[index];
                if (!knownFeatureIds.Contains(featureId)) {
                    throw new InvalidOperationException($"Unknown {modeDescription} codegen feature '{featureId}'.");
                }

                featureIds.Add(featureId);
            }

            return featureIds;
        }
    }
}
