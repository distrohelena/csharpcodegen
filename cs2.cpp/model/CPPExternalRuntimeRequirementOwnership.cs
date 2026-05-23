namespace cs2.cpp {
    /// <summary>
    /// Describes which caller-owned features should own a named runtime requirement when pruning is enabled.
    /// </summary>
    public sealed class CPPExternalRuntimeRequirementOwnership {
        /// <summary>
        /// Initializes a new runtime requirement ownership declaration.
        /// </summary>
        /// <param name="requirementId">Stable runtime requirement id.</param>
        /// <param name="featureIds">Caller-owned feature ids that own the requirement.</param>
        public CPPExternalRuntimeRequirementOwnership(string requirementId, IReadOnlyList<string> featureIds) {
            RequirementId = requirementId;
            FeatureIds = featureIds;
        }

        /// <summary>
        /// Gets the stable runtime requirement id.
        /// </summary>
        public string RequirementId { get; }

        /// <summary>
        /// Gets the caller-owned feature ids that own the requirement.
        /// </summary>
        public IReadOnlyList<string> FeatureIds { get; }
    }
}
