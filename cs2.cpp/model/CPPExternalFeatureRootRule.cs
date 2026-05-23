namespace cs2.cpp {
    /// <summary>
    /// Describes a fully qualified type name that should enable one or more caller-owned features when referenced.
    /// </summary>
    public sealed class CPPExternalFeatureRootRule {
        /// <summary>
        /// Initializes a new external root rule.
        /// </summary>
        /// <param name="typeName">Fully qualified type name to detect.</param>
        /// <param name="featureIds">Caller-owned feature ids enabled by the type.</param>
        public CPPExternalFeatureRootRule(string typeName, IReadOnlyList<string> featureIds) {
            TypeName = typeName;
            FeatureIds = featureIds;
        }

        /// <summary>
        /// Gets the fully qualified type name to detect.
        /// </summary>
        public string TypeName { get; }

        /// <summary>
        /// Gets the caller-owned feature ids enabled by the detected type.
        /// </summary>
        public IReadOnlyList<string> FeatureIds { get; }
    }
}
