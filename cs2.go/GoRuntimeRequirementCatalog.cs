using System.Collections.Generic;

namespace cs2.go {
    /// <summary>
    /// Catalog of runtime requirement definitions for the Go backend.
    /// </summary>
    public static class GoRuntimeRequirementCatalog {
        /// <summary>
        /// Gets the shared runtime requirement definitions.
        /// </summary>
        public static IReadOnlyList<GoRuntimeRequirementDefinition> BaseRequirements { get; } = new List<GoRuntimeRequirementDefinition> {
            new GoRuntimeRequirementDefinition("DateTime", "time.Time", "time"),
            new GoRuntimeRequirementDefinition("TimeSpan", "time.Duration", "time"),
            new GoRuntimeRequirementDefinition("Random", "rand.Rand", "math/rand", "rand"),
            new GoRuntimeRequirementDefinition("Regex", "regexp.Regexp", "regexp"),
            new GoRuntimeRequirementDefinition("StringBuilder", "strings.Builder", "strings")
        };
    }
}
