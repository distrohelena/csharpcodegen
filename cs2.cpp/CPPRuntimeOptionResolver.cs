using cs2.core;

namespace cs2.cpp {
    /// <summary>
    /// Resolves caller-owned generic runtime capability options after named preset defaults have been applied.
    /// </summary>
    public static class CPPRuntimeOptionResolver {
        /// <summary>
        /// Applies supported caller overrides to the active runtime profile and validates restricted storage requirements.
        /// </summary>
        /// <param name="options">Conversion options whose runtime profile should be resolved.</param>
        public static void Resolve(CPPConversionOptions options) {
            if (options == null) {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.RuntimeProfile == null) {
                throw new InvalidOperationException("A runtime profile is required before resolving runtime capability options.");
            }

            IReadOnlyDictionary<string, string> values = options.PlatformOptionValues ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            options.RuntimeProfile.UseStdString = ResolveBooleanOverride(values, CPPCodegenOptionNames.UseStdString, options.RuntimeProfile.UseStdString);
            options.RuntimeProfile.UseStdVector = ResolveBooleanOverride(values, CPPCodegenOptionNames.UseStdVector, options.RuntimeProfile.UseStdVector);
            options.RuntimeProfile.UseStdUnorderedMap = ResolveBooleanOverride(values, CPPCodegenOptionNames.UseStdUnorderedMap, options.RuntimeProfile.UseStdUnorderedMap);
            options.RuntimeProfile.UseExceptions = ResolveBooleanOverride(values, CPPCodegenOptionNames.UseExceptions, options.RuntimeProfile.UseExceptions);
            options.RuntimeProfile.UseRtti = ResolveBooleanOverride(values, CPPCodegenOptionNames.UseRtti, options.RuntimeProfile.UseRtti);

            if (UsesRestrictedStorage(options.RuntimeProfile) && string.IsNullOrWhiteSpace(GetProviderHeader(options))) {
                throw new ArgumentException(
                    $"Option '{CPPCodegenOptionNames.RuntimeProviderHeader}' is required when standard runtime storage is disabled.",
                    nameof(options));
            }
        }

        /// <summary>
        /// Resolves the C++ spelling used for managed string values in generated declarations and expressions.
        /// </summary>
        /// <param name="options">Resolved conversion options.</param>
        /// <returns><c>std::string</c> for standard storage, or <c>HeCppString</c> for provider-backed storage.</returns>
        public static string GetStringTypeName(CPPConversionOptions options) {
            if (options?.RuntimeProfile == null) {
                return "std::string";
            }

            return options.RuntimeProfile.UseStdString ? "std::string" : "HeCppString";
        }

        /// <summary>
        /// Resolves the C++ spelling used for managed string values from the active conversion program.
        /// </summary>
        /// <param name="program">Program whose options or conversion rules describe the active runtime.</param>
        /// <returns>The resolved native string type spelling.</returns>
        public static string GetStringTypeName(ConversionProgram program) {
            if (program is CPPProgram cppProgram && cppProgram.Options != null) {
                return GetStringTypeName(cppProgram.Options);
            }

            if (program?.Rules is CPPConversionRules rules) {
                return rules.UseStdString ? "std::string" : "HeCppString";
            }

            return "std::string";
        }

        /// <summary>
        /// Resolves the caller-owned provider header path for the active conversion.
        /// </summary>
        /// <param name="options">Conversion options to inspect.</param>
        /// <returns>The provider header token, or an empty string when standard storage is sufficient.</returns>
        public static string GetProviderHeader(CPPConversionOptions options) {
            if (options?.PlatformOptionValues == null ||
                !options.PlatformOptionValues.TryGetValue(CPPCodegenOptionNames.RuntimeProviderHeader, out string providerHeader)) {
                return string.Empty;
            }

            return providerHeader?.Trim() ?? string.Empty;
        }

        /// <summary>
        /// Returns whether any managed storage type requires the caller-owned runtime provider.
        /// </summary>
        /// <param name="profile">Runtime profile to inspect.</param>
        /// <returns><c>true</c> when at least one standard storage capability is disabled.</returns>
        public static bool UsesRestrictedStorage(CPPRuntimeProfile profile) {
            return profile != null && (!profile.UseStdString || !profile.UseStdVector || !profile.UseStdUnorderedMap);
        }

        /// <summary>
        /// Parses one optional strict Boolean override or returns the profile default.
        /// </summary>
        /// <param name="values">Caller-selected generic options.</param>
        /// <param name="key">Capability option key.</param>
        /// <param name="defaultValue">Profile value used when the caller did not provide an override.</param>
        /// <returns>The caller override or profile default.</returns>
        static bool ResolveBooleanOverride(IReadOnlyDictionary<string, string> values, string key, bool defaultValue) {
            if (values == null || !values.TryGetValue(key, out string rawValue)) {
                return defaultValue;
            }

            if (!bool.TryParse(rawValue, out bool parsedValue)) {
                throw new ArgumentException($"Option '{key}' must be a Boolean value.", nameof(values));
            }

            return parsedValue;
        }
    }
}
