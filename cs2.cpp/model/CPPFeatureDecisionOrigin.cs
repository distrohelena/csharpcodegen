namespace cs2.cpp {
    /// <summary>
    /// Describes why a feature ended up enabled or disabled in the final build.
    /// </summary>
    public enum CPPFeatureDecisionOrigin {
        /// <summary>
        /// Indicates the feature was enabled explicitly by the build profile.
        /// </summary>
        ForcedEnabled,

        /// <summary>
        /// Indicates the feature was disabled explicitly by the build profile.
        /// </summary>
        ForcedDisabled,

        /// <summary>
        /// Indicates the feature was included because reachable usage was detected.
        /// </summary>
        AutoDetected,

        /// <summary>
        /// Indicates the feature was left out because no reachable usage required it.
        /// </summary>
        NotIncluded,
    }
}
