namespace cs2.cpp {
    /// <summary>
    /// Resolves the opt-in generated C++ function profiling option while rejecting malformed caller configuration.
    /// </summary>
    public static class CPPGeneratedFunctionProfilingOptionResolver {
        /// <summary>
        /// Resolves whether the current conversion run should emit generated function profiling support.
        /// </summary>
        /// <param name="options">Conversion options that may contain the generic profiling option.</param>
        /// <returns>True when generated function profiling is explicitly enabled.</returns>
        public static bool Resolve(CPPConversionOptions options) {
            if (options == null) {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.PlatformOptionValues == null ||
                !options.PlatformOptionValues.TryGetValue(CPPCodegenOptionNames.GeneratedFunctionProfiling, out string rawValue)) {
                return false;
            }

            if (!bool.TryParse(rawValue, out bool enabled)) {
                throw new InvalidOperationException($"Option '{CPPCodegenOptionNames.GeneratedFunctionProfiling}' must be a Boolean value.");
            }

            return enabled;
        }
    }
}
