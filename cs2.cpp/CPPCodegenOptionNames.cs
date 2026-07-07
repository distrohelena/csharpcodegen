namespace cs2.cpp {
    /// <summary>
    /// Defines stable generic codegen option names consumed by the C++ backend.
    /// </summary>
    public static class CPPCodegenOptionNames {
        /// <summary>
        /// Gets the generic option name that forces selected runtime features off.
        /// </summary>
        public const string ForcedDisabledFeatures = "codegen-forced-disabled-features";

        /// <summary>
        /// Gets the generic option name that strips native exception constructor messages from generated C++ output.
        /// </summary>
        public const string CompactNativeExceptionMessages = "codegen-compact-native-exception-messages";
    }
}
