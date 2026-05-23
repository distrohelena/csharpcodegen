namespace cs2.cpp {
    /// <summary>
    /// Emits a tiny runtime manifest that exposes the final feature decisions inside generated native builds.
    /// </summary>
    public static class CPPFeatureManifestWriter {
        /// <summary>
        /// Writes the feature manifest header and source files into the generated runtime folder.
        /// </summary>
        /// <param name="outputFolder">Root output folder for the generated C++ project.</param>
        /// <param name="buildUsageReport">Resolved feature decisions for the active build.</param>
        /// <returns>The emitted file paths.</returns>
        public static IReadOnlyList<string> Write(string outputFolder, CPPBuildUsageReport buildUsageReport) {
            if (string.IsNullOrWhiteSpace(outputFolder)) {
                throw new ArgumentException("Output folder must not be empty.", nameof(outputFolder));
            }

            if (buildUsageReport == null) {
                throw new ArgumentNullException(nameof(buildUsageReport));
            }

            string runtimeFolder = Path.Combine(outputFolder, "runtime");
            Directory.CreateDirectory(runtimeFolder);

            string headerPath = Path.Combine(runtimeFolder, "feature_manifest.hpp");
            string sourcePath = Path.Combine(runtimeFolder, "feature_manifest.cpp");

            File.WriteAllText(headerPath, BuildHeaderText(buildUsageReport));
            File.WriteAllText(sourcePath, BuildSourceText(buildUsageReport));

            return new[] { headerPath, sourcePath };
        }

        static string BuildHeaderText(CPPBuildUsageReport buildUsageReport) {
            List<string> lines = new List<string> {
                """
#pragma once

#include <cstddef>

enum class HEFeature {
"""
            };

            CPPFeatureDecision[] features = buildUsageReport.FeatureDecisions
                .OrderBy(item => item.FeatureId, StringComparer.Ordinal)
                .ToArray();
            for (int index = 0; index < features.Length; index++) {
                string separator = index < features.Length - 1 ? "," : string.Empty;
                lines.Add($"    {CPPFeatureIdentifierFormatter.ToEnumMemberName(features[index].FeatureId)}{separator}");
            }

            lines.Add("""
};

enum class HEFeatureDecisionOrigin {
    ForcedEnabled,
    ForcedDisabled,
    AutoDetected,
    NotIncluded
};

struct HEFeatureEntry {
    HEFeature Feature;
    bool Enabled;
    HEFeatureDecisionOrigin Origin;
    const char* Name;
};

bool he_feature_enabled(HEFeature feature);
const HEFeatureEntry* he_get_feature_entries(std::size_t* count);
""");
            return string.Join(Environment.NewLine, lines);
        }

        static string BuildSourceText(CPPBuildUsageReport buildUsageReport) {
            List<string> lines = new List<string> {
                "#include \"feature_manifest.hpp\"",
                string.Empty,
                "static const HEFeatureEntry kFeatureEntries[] = {"
            };

            foreach (CPPFeatureDecision decision in buildUsageReport.FeatureDecisions.OrderBy(item => item.FeatureId, StringComparer.Ordinal)) {
                string enabledLiteral = decision.Enabled ? "true" : "false";
                lines.Add($"    {{ HEFeature::{CPPFeatureIdentifierFormatter.ToEnumMemberName(decision.FeatureId)}, {enabledLiteral}, HEFeatureDecisionOrigin::{decision.Origin}, \"{decision.FeatureId}\" }},");
            }

            lines.Add("};");
            lines.Add(string.Empty);
            lines.Add("bool he_feature_enabled(HEFeature feature) {");
            lines.Add("    for (const HEFeatureEntry& entry : kFeatureEntries) {");
            lines.Add("        if (entry.Feature == feature) {");
            lines.Add("            return entry.Enabled;");
            lines.Add("        }");
            lines.Add("    }");
            lines.Add(string.Empty);
            lines.Add("    return false;");
            lines.Add("}");
            lines.Add(string.Empty);
            lines.Add("const HEFeatureEntry* he_get_feature_entries(std::size_t* count) {");
            lines.Add("    if (count != nullptr) {");
            lines.Add("        *count = sizeof(kFeatureEntries) / sizeof(kFeatureEntries[0]);");
            lines.Add("    }");
            lines.Add(string.Empty);
            lines.Add("    return kFeatureEntries;");
            lines.Add("}");
            lines.Add(string.Empty);

            return string.Join(Environment.NewLine, lines);
        }
    }
}
