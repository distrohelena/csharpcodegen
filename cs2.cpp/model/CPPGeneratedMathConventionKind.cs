namespace cs2.cpp {
    /// <summary>
    /// Describes the matrix and vector convention that generated runtime math should follow for one platform output.
    /// </summary>
    public enum CPPGeneratedMathConventionKind {
        /// <summary>
        /// Emits the shared engine row-vector matrix convention.
        /// </summary>
        EngineRowVector = 0,

        /// <summary>
        /// Emits a native column-vector matrix convention for direct graphics API consumption.
        /// </summary>
        NativeColumnVector = 1
    }
}
