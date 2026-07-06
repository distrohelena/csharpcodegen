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
            if (!selectedOptions.TryGetValue(CPPCodegenOptionNames.ForcedDisabledFeatures, out string serializedFeatureIds)
                || string.IsNullOrWhiteSpace(serializedFeatureIds)) {
                return profile;
            }

            HashSet<string> knownFeatureIds = new(
                featureCatalog.Features.Select(feature => feature.Id),
                StringComparer.Ordinal);
            HashSet<string> addedFeatureIds = new(StringComparer.Ordinal);
            string[] featureIds = serializedFeatureIds.Split(
                [',', ';', ' '],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int index = 0; index < featureIds.Length; index++) {
                string featureId = featureIds[index];
                if (!addedFeatureIds.Add(featureId)) {
                    continue;
                }
                if (!knownFeatureIds.Contains(featureId)) {
                    throw new InvalidOperationException($"Unknown forced-disabled codegen feature '{featureId}'.");
                }

                profile.WithMode(featureId, CPPFeatureMode.Disabled);
            }

            return profile;
        }
    }
}
