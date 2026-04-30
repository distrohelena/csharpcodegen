namespace cs2.cpp {
    /// <summary>
    /// Describes a mismatch between detected usage and an explicit feature override.
    /// </summary>
    public class CPPFeatureConflict {
        /// <summary>
        /// Gets or sets the feature that produced the conflict.
        /// </summary>
        public CPPFeatureKind Feature { get; set; }

        /// <summary>
        /// Gets or sets the policy that controls how the conflict should be treated.
        /// </summary>
        public CPPFeatureConflictPolicy Policy { get; set; }

        /// <summary>
        /// Gets or sets a human-readable explanation for the conflict.
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }
}
