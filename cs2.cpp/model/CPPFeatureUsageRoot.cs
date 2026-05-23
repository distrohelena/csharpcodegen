namespace cs2.cpp {
    /// <summary>
    /// Records a detected root that pulled a feature into the build graph.
    /// </summary>
    public class CPPFeatureUsageRoot {
        /// <summary>
        /// Gets or sets the caller-owned feature id reached by this root.
        /// </summary>
        public string FeatureId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the stable identifier for the detected root.
        /// </summary>
        public string RootId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the source category for the detected root.
        /// </summary>
        public string SourceKind { get; set; } = string.Empty;
    }
}
