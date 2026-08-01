namespace cs2.cpp {
    /// <summary>
    /// Defines stable generic codegen option names consumed by the C++ backend.
    /// </summary>
    public static class CPPCodegenOptionNames {
        /// <summary>
        /// Gets the generic option name that forces selected runtime features on.
        /// </summary>
        public const string EnabledFeatures = "codegen-enabled-features";

        /// <summary>
        /// Gets the generic option name that forces selected runtime features off.
        /// </summary>
        public const string ForcedDisabledFeatures = "codegen-forced-disabled-features";

        /// <summary>
        /// Gets the generic option name that strips native exception constructor messages from generated C++ output.
        /// </summary>
        public const string CompactNativeExceptionMessages = "codegen-compact-native-exception-messages";

        /// <summary>
        /// Gets the generic option name that enables direct Tracy scopes for generated C++ function bodies.
        /// </summary>
        public const string GeneratedFunctionProfiling = "codegen-generated-function-profiling";

        /// <summary>
        /// Gets the generic option name containing semicolon-delimited maintained-symbol prefixes selected for generated function profiling.
        /// </summary>
        public const string GeneratedFunctionProfilingMaintainedSymbolPrefixes = "codegen-generated-function-profiling-maintained-symbol-prefixes";
    }
}
