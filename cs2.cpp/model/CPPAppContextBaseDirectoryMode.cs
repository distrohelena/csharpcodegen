namespace cs2.cpp {
    /// <summary>
    /// Selects how the generated AppContext runtime exposes the executable base directory for converted code.
    /// </summary>
    public enum CPPAppContextBaseDirectoryMode {
        /// <summary>
        /// Preserves the existing runtime path-resolution behavior and dynamic string initialization.
        /// </summary>
        DynamicRuntime,

        /// <summary>
        /// Emits one compile-time dot literal so non-host targets can avoid dynamic AppContext initialization.
        /// </summary>
        StaticDot
    }
}
