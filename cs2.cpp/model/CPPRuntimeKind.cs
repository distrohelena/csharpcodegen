namespace cs2.cpp {
    /// <summary>
    /// Identifies the runtime support level available to generated C++ code.
    /// </summary>
    public enum CPPRuntimeKind {
        /// <summary>
        /// Runtime surface kept minimal for portability.
        /// </summary>
        Minimal,

        /// <summary>
        /// Runtime may use a constrained subset of the C++ standard library.
        /// </summary>
        StlLite,

        /// <summary>
        /// Runtime is backed by a custom portability layer for retro targets.
        /// </summary>
        CustomRetro
    }
}
