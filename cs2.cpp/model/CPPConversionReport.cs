namespace cs2.cpp {
    /// <summary>
    /// Collects diagnostics and summary state for a single C++ conversion run.
    /// </summary>
    public class CPPConversionReport {
        /// <summary>
        /// Gets the diagnostics collected for the current conversion run.
        /// </summary>
        public List<CPPConversionDiagnostic> Diagnostics { get; } = new List<CPPConversionDiagnostic>();

        /// <summary>
        /// Gets or sets the number of processed source types.
        /// </summary>
        public int ProcessedTypeCount { get; set; }

        /// <summary>
        /// Gets or sets the number of emitted output types.
        /// </summary>
        public int EmittedTypeCount { get; set; }

        /// <summary>
        /// Gets the generated file paths emitted during the active conversion run.
        /// </summary>
        public List<string> EmittedFiles { get; } = new List<string>();

        /// <summary>
        /// Gets the runtime requirement names registered for the active conversion run.
        /// </summary>
        public List<string> RegisteredRuntimeRequirements { get; } = new List<string>();

        /// <summary>
        /// Gets or sets the resolved assembly name for the active source project.
        /// </summary>
        public string AssemblyName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the resolved assembly version for the active source project.
        /// </summary>
        public string AssemblyVersion { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the resolved target framework for the active source project.
        /// </summary>
        public string TargetFramework { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the resolved build feature usage report for the active conversion run.
        /// </summary>
        public CPPBuildUsageReport BuildUsageReport { get; set; } = new CPPBuildUsageReport();

        /// <summary>
        /// Gets whether the current report contains at least one error diagnostic.
        /// </summary>
        public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == CPPDiagnosticSeverity.Error);

        /// <summary>
        /// Adds a diagnostic entry to the report.
        /// </summary>
        /// <param name="severity">The severity of the diagnostic.</param>
        /// <param name="code">The diagnostic code.</param>
        /// <param name="message">The diagnostic message.</param>
        /// <param name="sourceTypeName">The source type name when available.</param>
        /// <param name="sourceMemberName">The source member name when available.</param>
        /// <param name="syntaxKind">The associated syntax kind when available.</param>
        /// <param name="filePath">The source file path when available.</param>
        public void AddDiagnostic(CPPDiagnosticSeverity severity, string code, string message, string sourceTypeName = "", string sourceMemberName = "", string syntaxKind = "", string filePath = "") {
            Diagnostics.Add(new CPPConversionDiagnostic {
                Severity = severity,
                Code = code,
                Message = message,
                SourceTypeName = sourceTypeName,
                SourceMemberName = sourceMemberName,
                SyntaxKind = syntaxKind,
                FilePath = filePath
            });
        }

        /// <summary>
        /// Resets the report to a clean per-run state while keeping the same report instance alive.
        /// </summary>
        public void Reset() {
            Diagnostics.Clear();
            EmittedFiles.Clear();
            RegisteredRuntimeRequirements.Clear();
            ProcessedTypeCount = 0;
            EmittedTypeCount = 0;
            AssemblyName = string.Empty;
            AssemblyVersion = string.Empty;
            TargetFramework = string.Empty;
            BuildUsageReport = new CPPBuildUsageReport();
        }
    }
}
