namespace cs2.cpp {
    /// <summary>
    /// Captures the outcome of validating a resolved build against a restriction profile.
    /// </summary>
    public class CPPRestrictionValidationResult {
        /// <summary>
        /// Gets the diagnostics collected during validation.
        /// </summary>
        public List<string> Diagnostics { get; } = new List<string>();

        /// <summary>
        /// Gets whether the build satisfied every active restriction.
        /// </summary>
        public bool IsValid => Diagnostics.Count == 0;
    }
}
