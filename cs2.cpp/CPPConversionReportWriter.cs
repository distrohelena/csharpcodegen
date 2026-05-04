using System.Text.Json;

namespace cs2.cpp {
    /// <summary>
    /// Writes the per-run C++ conversion report to a deterministic JSON file.
    /// </summary>
    public static class CPPConversionReportWriter {
        /// <summary>
        /// Gets the default file name used for serialized conversion reports.
        /// </summary>
        public const string DefaultFileName = "cpp-conversion-report.json";

        /// <summary>
        /// Writes the supplied conversion report into the output folder.
        /// </summary>
        /// <param name="outputFolder">Folder that receives the serialized report.</param>
        /// <param name="report">Report to serialize.</param>
        /// <param name="options">Active conversion options that define the emitted target profiles.</param>
        /// <returns>The full path to the written report file.</returns>
        public static string Write(string outputFolder, CPPConversionReport report, CPPConversionOptions options = null) {
            if (string.IsNullOrWhiteSpace(outputFolder)) {
                throw new ArgumentException("Output folder must not be empty.", nameof(outputFolder));
            }

            if (report == null) {
                throw new ArgumentNullException(nameof(report));
            }

            Directory.CreateDirectory(outputFolder);

            string filePath = Path.Combine(outputFolder, DefaultFileName);
            JsonSerializerOptions serializerOptions = new JsonSerializerOptions {
                WriteIndented = true
            };

            CPPConversionDiagnostic[] orderedDiagnostics = report.Diagnostics
                .OrderBy(diagnostic => diagnostic.SourceTypeName, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.SourceMemberName, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.SyntaxKind, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.FilePath, StringComparer.Ordinal)
                .ToArray();

            CPPFeatureDecision[] orderedFeatureDecisions = report.BuildUsageReport.FeatureDecisions
                .OrderBy(decision => decision.Feature.ToString(), StringComparer.Ordinal)
                .ToArray();

            CPPFeatureUsageRoot[] orderedDetectedRoots = report.BuildUsageReport.DetectedRoots
                .OrderBy(root => root.Feature.ToString(), StringComparer.Ordinal)
                .ThenBy(root => root.RootId, StringComparer.Ordinal)
                .ThenBy(root => root.SourceKind, StringComparer.Ordinal)
                .ToArray();

            CPPFeatureConflict[] orderedConflicts = report.BuildUsageReport.Conflicts
                .OrderBy(conflict => conflict.Feature.ToString(), StringComparer.Ordinal)
                .ThenBy(conflict => conflict.Policy.ToString(), StringComparer.Ordinal)
                .ThenBy(conflict => conflict.Message, StringComparer.Ordinal)
                .ToArray();

            var model = new {
                assemblyName = report.AssemblyName,
                assemblyVersion = report.AssemblyVersion,
                targetFramework = report.TargetFramework,
                presetId = options?.PresetId ?? string.Empty,
                processedTypeCount = report.ProcessedTypeCount,
                emittedTypeCount = report.EmittedTypeCount,
                emittedFileCount = report.EmittedFiles.Count,
                hasErrors = report.HasErrors,
                errorCount = orderedDiagnostics.Count(diagnostic => diagnostic.Severity == CPPDiagnosticSeverity.Error),
                warningCount = orderedDiagnostics.Count(diagnostic => diagnostic.Severity == CPPDiagnosticSeverity.Warning),
                infoCount = orderedDiagnostics.Count(diagnostic => diagnostic.Severity == CPPDiagnosticSeverity.Info),
                activeProfiles = new {
                    compiler = options?.CompilerProfile?.Name ?? string.Empty,
                    platform = options?.PlatformProfile?.Name ?? string.Empty,
                    runtime = options?.RuntimeProfile?.Name ?? string.Empty,
                    restrictions = options?.RestrictionProfile?.Name ?? string.Empty
                },
                buildFeatures = new {
                    decisions = orderedFeatureDecisions.Select(decision => new {
                        feature = decision.Feature.ToString(),
                        enabled = decision.Enabled,
                        origin = decision.Origin.ToString()
                    }).ToArray(),
                    detectedRoots = orderedDetectedRoots.Select(root => new {
                        feature = root.Feature.ToString(),
                        rootId = root.RootId,
                        sourceKind = root.SourceKind
                    }).ToArray(),
                    conflicts = orderedConflicts.Select(conflict => new {
                        feature = conflict.Feature.ToString(),
                        policy = conflict.Policy.ToString(),
                        message = conflict.Message
                    }).ToArray()
                },
                registeredRuntimeRequirements = report.RegisteredRuntimeRequirements.ToArray(),
                emittedFiles = report.EmittedFiles.ToArray(),
                diagnostics = orderedDiagnostics.Select(diagnostic => new {
                    severity = diagnostic.Severity.ToString(),
                    code = diagnostic.Code,
                    message = diagnostic.Message,
                    sourceTypeName = diagnostic.SourceTypeName,
                    sourceMemberName = diagnostic.SourceMemberName,
                    syntaxKind = diagnostic.SyntaxKind,
                    filePath = diagnostic.FilePath,
                    recommendation = diagnostic.Recommendation
                }).ToArray(),
                diagnosticsByTypeMember = orderedDiagnostics
                    .GroupBy(diagnostic => new {
                        SourceTypeName = diagnostic.SourceTypeName,
                        SourceMemberName = diagnostic.SourceMemberName
                    })
                    .OrderBy(group => group.Key.SourceTypeName, StringComparer.Ordinal)
                    .ThenBy(group => group.Key.SourceMemberName, StringComparer.Ordinal)
                    .Select(group => new {
                        sourceTypeName = group.Key.SourceTypeName,
                        sourceMemberName = group.Key.SourceMemberName,
                        errorCount = group.Count(diagnostic => diagnostic.Severity == CPPDiagnosticSeverity.Error),
                        warningCount = group.Count(diagnostic => diagnostic.Severity == CPPDiagnosticSeverity.Warning),
                        diagnostics = group.Select(diagnostic => new {
                            severity = diagnostic.Severity.ToString(),
                            code = diagnostic.Code,
                            message = diagnostic.Message,
                            syntaxKind = diagnostic.SyntaxKind,
                            filePath = diagnostic.FilePath,
                            recommendation = diagnostic.Recommendation
                        }).ToArray()
                    }).ToArray(),
                unsupportedSyntaxSummary = orderedDiagnostics
                    .Where(diagnostic => !string.IsNullOrWhiteSpace(diagnostic.SyntaxKind))
                    .GroupBy(diagnostic => diagnostic.SyntaxKind, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new {
                        syntaxKind = group.Key,
                        count = group.Count(),
                        sourceTypeCount = group
                            .Select(diagnostic => diagnostic.SourceTypeName)
                            .Where(sourceTypeName => !string.IsNullOrWhiteSpace(sourceTypeName))
                            .Distinct(StringComparer.Ordinal)
                            .Count()
                    }).ToArray(),
                unsupportedConstructCount = orderedDiagnostics.Count(diagnostic => !string.IsNullOrWhiteSpace(diagnostic.SyntaxKind)),
                unsupportedMemberCount = orderedDiagnostics
                    .Where(diagnostic => !string.IsNullOrWhiteSpace(diagnostic.SyntaxKind))
                    .Select(diagnostic => string.Concat(diagnostic.SourceTypeName, "::", diagnostic.SourceMemberName))
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                generatedAtUtc = "0001-01-01T00:00:00.0000000Z",
                diagnosticsVersion = 1,
                diagnosticsSchema = "cpp-conversion-report.v1"
            };

            string json = JsonSerializer.Serialize(model, serializerOptions);
            File.WriteAllText(filePath, json + Environment.NewLine);
            return filePath;
        }
    }
}
