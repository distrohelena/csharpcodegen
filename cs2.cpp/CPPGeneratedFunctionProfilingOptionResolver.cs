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

        /// <summary>
        /// Resolves the optional maintained-symbol prefix allowlist used to limit generated function scopes.
        /// </summary>
        /// <param name="options">Conversion options that may contain the prefix allowlist.</param>
        /// <returns>Ordered unique symbol prefixes, or an empty collection when all maintained symbols are eligible.</returns>
        public static IReadOnlyList<string> ResolveMaintainedSymbolPrefixes(CPPConversionOptions options) {
            if (options == null) {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.PlatformOptionValues == null ||
                !options.PlatformOptionValues.TryGetValue(CPPCodegenOptionNames.GeneratedFunctionProfilingMaintainedSymbolPrefixes, out string rawValue) ||
                string.IsNullOrWhiteSpace(rawValue)) {
                return Array.Empty<string>();
            }

            string[] rawPrefixes = rawValue.Split(';', StringSplitOptions.None);
            if (rawPrefixes.Any(string.IsNullOrWhiteSpace)) {
                throw new InvalidOperationException($"Option '{CPPCodegenOptionNames.GeneratedFunctionProfilingMaintainedSymbolPrefixes}' cannot contain empty symbol prefixes.");
            }

            return rawPrefixes
                .Select(prefix => prefix.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
    }
}
