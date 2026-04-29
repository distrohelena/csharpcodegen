namespace cs2.cpp {
    /// <summary>
    /// Captures a single issue discovered during C++ conversion analysis or emission.
    /// </summary>
    public class CPPConversionDiagnostic {
        /// <summary>
        /// Gets or sets the machine-readable diagnostic code.
        /// </summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the severity level of the diagnostic.
        /// </summary>
        public CPPDiagnosticSeverity Severity { get; set; }

        /// <summary>
        /// Gets or sets the human-readable diagnostic message.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the source type that triggered the diagnostic.
        /// </summary>
        public string SourceTypeName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the source member that triggered the diagnostic.
        /// </summary>
        public string SourceMemberName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the Roslyn syntax kind or other classifier associated with the issue.
        /// </summary>
        public string SyntaxKind { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the source file path when available.
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the recommended action or mapping that should resolve the reported issue.
        /// </summary>
        public string Recommendation { get; set; } = string.Empty;
    }
}
