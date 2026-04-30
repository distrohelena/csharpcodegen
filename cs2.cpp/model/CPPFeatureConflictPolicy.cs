namespace cs2.cpp {
    /// <summary>
    /// Describes how a forced-disable conflict should be surfaced in reports.
    /// </summary>
    public enum CPPFeatureConflictPolicy {
        /// <summary>
        /// Records the conflict as a warning so the build can continue.
        /// </summary>
        Warn,

        /// <summary>
        /// Records the conflict as an error because the build cannot degrade safely.
        /// </summary>
        Error,
    }
}
