namespace cs2.cpp {
    /// <summary>
    /// Records a detected root that pulled a feature into the build graph.
    /// </summary>
    public class CPPFeatureUsageRoot {
        /// <summary>
        /// Gets or sets the feature reached by this root.
        /// </summary>
        public CPPFeatureKind Feature { get; set; }

        /// <summary>
        /// Gets or sets the stable identifier for the detected root.
        /// </summary>
        public string RootId { get; set; }

        /// <summary>
        /// Gets or sets the source category for the detected root.
        /// </summary>
        public string SourceKind { get; set; }
    }
}
