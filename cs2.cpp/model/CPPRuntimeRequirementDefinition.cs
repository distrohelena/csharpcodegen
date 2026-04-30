namespace cs2.cpp {
    /// <summary>
    /// Describes a runtime support requirement that generated C++ code may depend on.
    /// </summary>
    public class CPPRuntimeRequirementDefinition {
        /// <summary>
        /// Gets or sets the stable requirement name used by the converter.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the include path emitted for the requirement when needed.
        /// </summary>
        public string IncludePath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the generated config macro that flags the requirement as available.
        /// </summary>
        public string ConfigDefineName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a short human-readable description of the requirement role.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Gets the features that own this runtime requirement when feature pruning is enabled.
        /// </summary>
        public List<CPPFeatureKind> OwningFeatures { get; } = new List<CPPFeatureKind>();
    }
}
