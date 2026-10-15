using System.Text.Json;

namespace cs2.cpp {
    /// <summary>
    /// Emits a tiny runtime manifest that exposes the final feature decisions inside generated native builds.
    /// </summary>
    public static class CPPFeatureManifestWriter {
        /// <summary>Re-emits the single native feature manifest from an officially merged conversion report using the same producer as normal conversion.</summary>
        /// <param name="reportPath">Existing conversion report whose feature decisions already reflect all merged projects.</param>
        /// <param name="outputFolder">Existing combined source tree with the overlay feature-config include; the two manifest files and feature config are written.</param>
        /// <param name="featureCatalogPath">The validated feature catalog used by all contributing conversions.</param>
        /// <returns>The generated native manifest paths.</returns>
        public static IReadOnlyList<string> WriteFromConversionReport(string reportPath, string outputFolder, string featureCatalogPath) {
            if (string.IsNullOrWhiteSpace(reportPath)) throw new ArgumentException("Conversion report path must not be empty.", nameof(reportPath));
            if (string.IsNullOrWhiteSpace(outputFolder)) throw new ArgumentException("Output folder must not be empty.", nameof(outputFolder));
            string[] configLines = File.ReadAllLines(Path.Combine(outputFolder, CPPGeneratedConfigWriter.DefaultFileName));
            if (!configLines.Contains("#include \"runtime/feature_config.hpp\"", StringComparer.Ordinal)
                || configLines.Any(line => line.StartsWith("#define HE_CPP_FEATURE_", StringComparison.Ordinal))) {
                throw new InvalidOperationException("Combined feature emission requires the scene-code-modules config with separate feature definitions.");
            }
            CPPExternalFeatureCatalog catalog = CPPExternalFeatureCatalogLoader.LoadFromFile(featureCatalogPath);
            HashSet<string> knownFeatures = catalog.Features.Select(feature => feature.Id).ToHashSet(StringComparer.Ordinal);
            HashSet<string> seenFeatures = new(StringComparer.Ordinal);
            HashSet<string> emittedNames = new(StringComparer.Ordinal);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(reportPath));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("buildFeatures", out JsonElement features)
                || features.ValueKind != JsonValueKind.Object || !features.TryGetProperty("decisions", out JsonElement decisions)
                || decisions.ValueKind != JsonValueKind.Array) {
                throw new InvalidOperationException("Conversion report must contain a buildFeatures.decisions array.");
            }
            CPPBuildUsageReport report = new();
            foreach (JsonElement decision in decisions.EnumerateArray()) {
                if (decision.ValueKind != JsonValueKind.Object || !decision.TryGetProperty("feature", out JsonElement feature)
                    || feature.ValueKind != JsonValueKind.String || !decision.TryGetProperty("enabled", out JsonElement enabled)
                    || (enabled.ValueKind != JsonValueKind.True && enabled.ValueKind != JsonValueKind.False)
                    || !decision.TryGetProperty("origin", out JsonElement origin) || origin.ValueKind != JsonValueKind.String) {
                    throw new InvalidOperationException("Every feature decision requires a feature id, boolean enabled value and named origin.");
                }
                string id = feature.GetString();
                if (!knownFeatures.Contains(id) || !seenFeatures.Add(id)) throw new InvalidOperationException($"Unknown or duplicate feature decision '{id}'.");
                string originName = origin.GetString();
                if (!Enum.TryParse(originName, false, out CPPFeatureDecisionOrigin parsedOrigin)
                    || !Enum.IsDefined(parsedOrigin) || parsedOrigin.ToString() != originName) {
                    throw new InvalidOperationException($"Feature '{id}' has an invalid decision origin '{originName}'.");
                }
                if (!emittedNames.Add(CPPFeatureIdentifierFormatter.ToEnumMemberName(id))) throw new InvalidOperationException($"Feature '{id}' collides with another generated enum member.");
                report.FeatureDecisions.Add(new CPPFeatureDecision { FeatureId = id, Enabled = enabled.GetBoolean(), Origin = parsedOrigin });
            }
            if (!seenFeatures.SetEquals(knownFeatures)) throw new InvalidOperationException("Conversion report must contain exactly one decision for every catalog feature.");
            IReadOnlyList<string> paths = Write(outputFolder, report);
            string featureConfigPath = CPPGeneratedConfigWriter.WriteFeatureConfig(outputFolder, report);
            return [paths[0], paths[1], featureConfigPath];
        }

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

        /// <summary>
        /// Builds the generated feature-manifest declarations and stable feature enumeration.
        /// </summary>
        /// <param name="buildUsageReport">Resolved feature decisions for the active build.</param>
        /// <returns>Complete generated header text.</returns>
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

        /// <summary>
        /// Builds the feature-manifest implementation without relying on non-standard zero-length arrays.
        /// </summary>
        /// <param name="buildUsageReport">Resolved feature decisions for the active build.</param>
        /// <returns>Complete generated implementation text.</returns>
        static string BuildSourceText(CPPBuildUsageReport buildUsageReport) {
            CPPFeatureDecision[] decisions = buildUsageReport.FeatureDecisions
                .OrderBy(item => item.FeatureId, StringComparer.Ordinal)
                .ToArray();
            if (decisions.Length == 0) {
                return string.Join(Environment.NewLine, new[] {
                    "#include \"feature_manifest.hpp\"",
                    string.Empty,
                    "bool he_feature_enabled(HEFeature feature) {",
                    "    (void)feature;",
                    "    return false;",
                    "}",
                    string.Empty,
                    "const HEFeatureEntry* he_get_feature_entries(std::size_t* count) {",
                    "    if (count != nullptr) {",
                    "        *count = 0;",
                    "    }",
                    string.Empty,
                    "    return nullptr;",
                    "}",
                    string.Empty
                });
            }

            List<string> lines = new List<string> {
                "#include \"feature_manifest.hpp\"",
                string.Empty,
                "static const HEFeatureEntry kFeatureEntries[] = {"
            };

            foreach (CPPFeatureDecision decision in decisions) {
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
