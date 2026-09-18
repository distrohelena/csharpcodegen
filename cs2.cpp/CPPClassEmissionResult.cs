using cs2.core;

namespace cs2.cpp {
    /// <summary>
    /// Captures the lowered header and source text for one generated class so file writing can happen after all lowering completes.
    /// </summary>
    public sealed class CPPClassEmissionResult {
        /// <summary>
        /// Initializes a result for one lowered class.
        /// </summary>
        /// <param name="conversionClass">Class that was lowered.</param>
        /// <param name="fileStem">Generated file stem without extension.</param>
        /// <param name="headerText">Complete header file contents.</param>
        /// <param name="sourceText">Complete source file contents.</param>
        /// <param name="runtimeRequirements">Runtime requirement names registered while lowering this class.</param>
        /// <param name="diagnostics">Diagnostics recorded while lowering this class.</param>
        /// <param name="profilingScopes">Generated function profiling scopes emitted for this class.</param>
        public CPPClassEmissionResult(
            ConversionClass conversionClass,
            string fileStem,
            string headerText,
            string sourceText,
            IReadOnlyList<string> runtimeRequirements,
            IReadOnlyList<CPPConversionDiagnostic> diagnostics,
            IReadOnlyList<CPPGeneratedFunctionProfilingScope> profilingScopes) {
            Class = conversionClass ?? throw new ArgumentNullException(nameof(conversionClass));
            if (string.IsNullOrWhiteSpace(fileStem)) {
                throw new ArgumentException("A lowered class requires a file stem.", nameof(fileStem));
            }

            FileStem = fileStem;
            HeaderText = headerText ?? throw new ArgumentNullException(nameof(headerText));
            SourceText = sourceText ?? throw new ArgumentNullException(nameof(sourceText));
            RuntimeRequirements = runtimeRequirements ?? throw new ArgumentNullException(nameof(runtimeRequirements));
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            ProfilingScopes = profilingScopes ?? throw new ArgumentNullException(nameof(profilingScopes));
        }

        /// <summary>
        /// Gets the runtime requirement names registered while lowering this class.
        /// </summary>
        public IReadOnlyList<string> RuntimeRequirements { get; }

        /// <summary>
        /// Gets the diagnostics recorded while lowering this class.
        /// </summary>
        public IReadOnlyList<CPPConversionDiagnostic> Diagnostics { get; }

        /// <summary>
        /// Gets the generated function profiling scopes emitted for this class.
        /// </summary>
        public IReadOnlyList<CPPGeneratedFunctionProfilingScope> ProfilingScopes { get; }

        /// <summary>
        /// Gets the class that was lowered.
        /// </summary>
        public ConversionClass Class { get; }

        /// <summary>
        /// Gets the generated file stem shared by the header and source files.
        /// </summary>
        public string FileStem { get; }

        /// <summary>
        /// Gets the complete header text.
        /// </summary>
        public string HeaderText { get; }

        /// <summary>
        /// Gets the complete source text.
        /// </summary>
        public string SourceText { get; }
    }
}
