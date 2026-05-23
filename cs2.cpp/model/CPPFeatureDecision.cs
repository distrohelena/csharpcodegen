namespace cs2.cpp {
    /// <summary>
    /// Records the final state chosen for a build feature.
    /// </summary>
    public class CPPFeatureDecision {
        /// <summary>
        /// Gets or sets the caller-owned feature id being described.
        /// </summary>
        public string FeatureId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets whether the feature is included in the final build.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Gets or sets the reason the feature ended up in its final state.
        /// </summary>
        public CPPFeatureDecisionOrigin Origin { get; set; }
    }
}
