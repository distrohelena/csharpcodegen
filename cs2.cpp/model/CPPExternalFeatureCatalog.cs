namespace cs2.cpp {
    /// <summary>
    /// Holds the externally supplied feature definitions, root rules, and runtime requirement ownership used by the generic converter.
    /// </summary>
    public sealed class CPPExternalFeatureCatalog {
        /// <summary>
        /// Initializes a new external feature catalog.
        /// </summary>
        /// <param name="features">Feature definitions declared by the caller.</param>
        /// <param name="rootRules">Type-root rules declared by the caller.</param>
        /// <param name="runtimeRequirements">Runtime requirement ownership declared by the caller.</param>
        public CPPExternalFeatureCatalog(
            IReadOnlyList<CPPExternalFeatureDefinition> features,
            IReadOnlyList<CPPExternalFeatureRootRule> rootRules,
            IReadOnlyList<CPPExternalRuntimeRequirementOwnership> runtimeRequirements) {
            Features = features;
            RootRules = rootRules;
            RuntimeRequirements = runtimeRequirements;
        }

        /// <summary>
        /// Gets the caller-owned feature definitions.
        /// </summary>
        public IReadOnlyList<CPPExternalFeatureDefinition> Features { get; }

        /// <summary>
        /// Gets the type-root rules used for feature detection.
        /// </summary>
        public IReadOnlyList<CPPExternalFeatureRootRule> RootRules { get; }

        /// <summary>
        /// Gets the runtime requirement ownership rules.
        /// </summary>
        public IReadOnlyList<CPPExternalRuntimeRequirementOwnership> RuntimeRequirements { get; }
    }
}
