namespace cs2.cpp {
    /// <summary>
    /// Describes how a feature should be selected for a conversion run.
    /// </summary>
    public enum CPPFeatureMode {
        /// <summary>
        /// Lets the conversion pipeline include the feature only when it is detected as reachable.
        /// </summary>
        Auto,

        /// <summary>
        /// Forces the feature to remain enabled even when no usage is detected.
        /// </summary>
        Enabled,

        /// <summary>
        /// Forces the feature to remain disabled even when usage is detected.
        /// </summary>
        Disabled,
    }
}
