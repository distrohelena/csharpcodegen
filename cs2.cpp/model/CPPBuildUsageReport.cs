namespace cs2.cpp {
    /// <summary>
    /// Summarizes the resolved feature decisions and detected roots for a build.
    /// </summary>
    public class CPPBuildUsageReport {
        /// <summary>
        /// Gets the final feature decisions captured during build analysis.
        /// </summary>
        public List<CPPFeatureDecision> FeatureDecisions { get; } = new List<CPPFeatureDecision>();

        /// <summary>
        /// Gets the detected feature roots captured during build analysis.
        /// </summary>
        public List<CPPFeatureUsageRoot> DetectedRoots { get; } = new List<CPPFeatureUsageRoot>();

        /// <summary>
        /// Gets the conflicts found while applying explicit feature overrides.
        /// </summary>
        public List<CPPFeatureConflict> Conflicts { get; } = new List<CPPFeatureConflict>();

        /// <summary>
        /// Returns whether the specified feature is enabled in the resolved report.
        /// </summary>
        /// <param name="featureId">The caller-owned feature id to inspect.</param>
        /// <returns><c>true</c> when the feature is enabled; otherwise <c>false</c>.</returns>
        public bool IsEnabled(string featureId) {
            return GetDecision(featureId).Enabled;
        }

        /// <summary>
        /// Returns the resolved decision for the specified feature.
        /// </summary>
        /// <param name="featureId">The caller-owned feature id to inspect.</param>
        /// <returns>The resolved decision for the feature.</returns>
        public CPPFeatureDecision GetDecision(string featureId) {
            foreach (CPPFeatureDecision decision in FeatureDecisions) {
                if (string.Equals(decision.FeatureId, featureId, StringComparison.Ordinal)) {
                    return decision;
                }
            }

            return new CPPFeatureDecision {
                FeatureId = featureId ?? string.Empty,
                Enabled = false,
                Origin = CPPFeatureDecisionOrigin.NotIncluded,
            };
        }
    }
}
