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
        /// Gets the generic option name that supplies the header implementing restricted runtime storage and failure hooks.
        /// </summary>
        public const string RuntimeProviderHeader = "codegen-runtime-provider-header";

        /// <summary>
        /// Gets the generic option name that selects standard-library string storage for generated C++.
        /// </summary>
        public const string UseStdString = "codegen-use-std-string";

        /// <summary>
        /// Gets the generic option name that selects standard-library vector storage for the shared runtime.
        /// </summary>
        public const string UseStdVector = "codegen-use-std-vector";

        /// <summary>
        /// Gets the generic option name that selects standard-library unordered-map storage for the shared runtime.
        /// </summary>
        public const string UseStdUnorderedMap = "codegen-use-std-unordered-map";

        /// <summary>
        /// Gets the generic option name that selects C++ exception unwinding for generated code and runtime helpers.
        /// </summary>
        public const string UseExceptions = "codegen-use-exceptions";

        /// <summary>
        /// Gets the generic option name that selects compiler RTTI for generated casts and runtime type queries.
        /// </summary>
        public const string UseRtti = "codegen-use-rtti";

        /// <summary>
        /// Gets the generic option name containing semicolon-delimited maintained-symbol prefixes selected for generated function profiling.
        /// </summary>
        public const string GeneratedFunctionProfilingMaintainedSymbolPrefixes = "codegen-generated-function-profiling-maintained-symbol-prefixes";
    }
}
