namespace cs2.go.util {
    /// <summary>
    /// Tracks which helper functions are required by generated Go output.
    /// </summary>
    public class GoHelperUsage {
        /// <summary>
        /// Gets or sets whether the generic ternary helper is required.
        /// </summary>
        public bool NeedsTernary { get; set; }

        /// <summary>
        /// Gets or sets whether the generic type-check helper is required.
        /// </summary>
        public bool NeedsTypeCheck { get; set; }

        /// <summary>
        /// Clears all helper usage flags.
        /// </summary>
        public void Reset() {
            NeedsTernary = false;
            NeedsTypeCheck = false;
        }
    }
}
