namespace cs2.cpp {
    /// <summary>
    /// Validates resolved feature usage and runtime helper registration against a restriction profile.
    /// </summary>
    public static class CPPRestrictionValidator {
        /// <summary>
        /// Validates a build usage report and runtime requirement set against the supplied restrictions.
        /// </summary>
        /// <param name="buildUsageReport">Resolved feature usage report for the active build.</param>
        /// <param name="registeredRequirements">Runtime requirements currently registered for the conversion.</param>
        /// <param name="restrictionProfile">Restriction profile that defines the forbidden systems.</param>
        /// <returns>The validation result and any diagnostics.</returns>
        public static CPPRestrictionValidationResult Validate(CPPBuildUsageReport buildUsageReport, IEnumerable<CPPRuntimeRequirementDefinition> registeredRequirements, CPPRestrictionProfile restrictionProfile) {
            CPPRestrictionValidationResult result = new CPPRestrictionValidationResult();
            CPPBuildUsageReport report = buildUsageReport ?? new CPPBuildUsageReport();
            CPPRestrictionProfile profile = restrictionProfile ?? CPPRestrictionProfile.CreatePermissive("default");

            ValidateFeature(profile, report, "shaders", profile.ForbidShaders, result);
            ValidateFeature(profile, report, "runtime_json", profile.ForbidRuntimeJson, result);
            ValidateFeature(profile, report, "reflection_like_runtime", profile.ForbidReflectionLikeRuntime, result);
            ValidateFeature(profile, report, "debug_overlay", profile.ForbidDebugOnlySystems, result);

            if (profile.ForbidRegex) {
                foreach (CPPRuntimeRequirementDefinition definition in registeredRequirements ?? Array.Empty<CPPRuntimeRequirementDefinition>()) {
                    if (string.Equals(definition.Name, "Regex", StringComparison.Ordinal)) {
                        result.Diagnostics.Add($"Restriction profile '{profile.Name}' forbids regex support, but runtime requirement '{definition.Name}' was registered.");
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Records a diagnostic when a forbidden feature is enabled in the resolved build report.
        /// </summary>
        /// <param name="restrictionProfile">Restriction profile being enforced.</param>
        /// <param name="buildUsageReport">Resolved build usage report.</param>
        /// <param name="featureId">Caller-owned feature id to validate.</param>
        /// <param name="isForbidden">Whether the feature is forbidden.</param>
        /// <param name="result">Validation result that receives diagnostics.</param>
        static void ValidateFeature(CPPRestrictionProfile restrictionProfile, CPPBuildUsageReport buildUsageReport, string featureId, bool isForbidden, CPPRestrictionValidationResult result) {
            if (!isForbidden || !buildUsageReport.IsEnabled(featureId)) {
                return;
            }

            result.Diagnostics.Add($"Restriction profile '{restrictionProfile.Name}' forbids feature '{featureId}', but the build resolved it as enabled.");
        }
    }
}
