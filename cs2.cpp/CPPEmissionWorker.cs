using cs2.core;
using Microsoft.CodeAnalysis;

namespace cs2.cpp {
    /// <summary>
    /// Owns one thread's processor, emitter, registrar, report and profiling manifest so classes lower without touching shared run state.
    /// </summary>
    public sealed class CPPEmissionWorker : ICPPConversionHost {
        /// <summary>
        /// Converter that owns the shared, read-only run state.
        /// </summary>
        readonly CPPCodeConverter Owner;

        /// <summary>
        /// Shared program model captured once so lookups never join the metadata thread per access.
        /// </summary>
        readonly ConversionProgram SharedProgram;

        /// <summary>
        /// Per-worker report receiving diagnostics for the class being lowered.
        /// </summary>
        readonly CPPConversionReport WorkerReport;

        /// <summary>
        /// Per-worker profiling manifest receiving scopes for the class being lowered.
        /// </summary>
        readonly CPPGeneratedFunctionProfilingManifest Manifest;

        /// <summary>
        /// Processor bound to this worker.
        /// </summary>
        readonly CPPConversiorProcessor Processor;

        /// <summary>
        /// Emitter bound to this worker's processor and manifest.
        /// </summary>
        readonly CPPClassEmitter Emitter;

        /// <summary>
        /// Registrar for the class currently being lowered; replaced per class so each result carries only its own requirements.
        /// </summary>
        CPPRuntimeRequirementRegistrar ClassRegistrar;

        /// <summary>
        /// Initializes a worker bound to one converter.
        /// </summary>
        /// <param name="owner">Converter supplying options, rules, program and ownership results.</param>
        /// <param name="program">C++ program model shared by every worker.</param>
        public CPPEmissionWorker(CPPCodeConverter owner, CPPProgram program) {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            SharedProgram = program ?? throw new ArgumentNullException(nameof(program));
            WorkerReport = new CPPConversionReport();
            Manifest = new CPPGeneratedFunctionProfilingManifest();
            ClassRegistrar = CreateClassRegistrar();
            Processor = new CPPConversiorProcessor(this);
            Emitter = new CPPClassEmitter(Processor, program, Manifest);
        }

        /// <inheritdoc />
        public CPPConversionOptions Options => Owner.Options;

        /// <inheritdoc />
        public CPPConversionRules CPPRules => Owner.CPPRules;

        /// <inheritdoc />
        public ConversionProgram Program => SharedProgram;

        /// <inheritdoc />
        public CPPOwnershipAnalysisResult OwnershipAnalysisResult => Owner.OwnershipAnalysisResult;

        /// <inheritdoc />
        public CPPRuntimeRequirementRegistrar RuntimeRequirementRegistrar => ClassRegistrar;

        /// <inheritdoc />
        public CPPConversionReport Report => WorkerReport;

        /// <summary>
        /// Lowers one class into text and collects everything the main thread must merge.
        /// </summary>
        /// <param name="conversionClass">Class to lower.</param>
        /// <param name="fileStem">Generated file stem resolved on the main thread.</param>
        /// <returns>The lowered class and its side effects; the runtime requirement names are sorted ordinally so the merge order never depends on the order in which the emitter happened to register them.</returns>
        public CPPClassEmissionResult Lower(ConversionClass conversionClass, string fileStem) {
            if (conversionClass == null) {
                throw new ArgumentNullException(nameof(conversionClass));
            }

            WorkerReport.Reset();
            Manifest.Clear();
            ClassRegistrar = CreateClassRegistrar();

            using (StringWriter headerWriter = new StringWriter()) {
                using (StringWriter sourceWriter = new StringWriter()) {
                    Emitter.Emit(conversionClass, headerWriter, sourceWriter);
                    List<string> requirementNames = ClassRegistrar.RegisteredRequirements
                        .Select(definition => definition.Name)
                        .OrderBy(name => name, StringComparer.Ordinal)
                        .ToList();
                    return new CPPClassEmissionResult(
                        conversionClass,
                        fileStem,
                        headerWriter.ToString(),
                        sourceWriter.ToString(),
                        requirementNames,
                        WorkerReport.Diagnostics.ToList(),
                        Manifest.Entries.ToList());
                }
            }
        }

        /// <inheritdoc />
        public void RegisterRuntimeRequirement(string name) {
            ClassRegistrar.Register(name);
        }

        /// <inheritdoc />
        public void ReportUnsupportedConstruct(string sourceTypeName, string sourceMemberName, string syntaxKind, string message, string recommendation, string filePath = "") {
            if (string.IsNullOrWhiteSpace(message)) {
                throw new ArgumentException("Unsupported construct message must not be empty.", nameof(message));
            }

            WorkerReport.AddDiagnostic(CPPDiagnosticSeverity.Error, "CPP1000", message);
            CPPConversionDiagnostic diagnostic = WorkerReport.Diagnostics[^1];
            diagnostic.SourceTypeName = sourceTypeName ?? string.Empty;
            diagnostic.SourceMemberName = sourceMemberName ?? string.Empty;
            diagnostic.SyntaxKind = syntaxKind ?? string.Empty;
            diagnostic.Recommendation = recommendation ?? string.Empty;
            diagnostic.FilePath = filePath ?? string.Empty;
        }

        /// <inheritdoc />
        public void ReportRuntimeCapabilityViolation(string sourceTypeName, string sourceMemberName, string syntaxKind, string message, string recommendation, string filePath = "") {
            if (string.IsNullOrWhiteSpace(message)) {
                throw new ArgumentException("Runtime capability violation message must not be empty.", nameof(message));
            }

            WorkerReport.AddDiagnostic(CPPDiagnosticSeverity.Error, "CPP1001", message);
            CPPConversionDiagnostic diagnostic = WorkerReport.Diagnostics[^1];
            diagnostic.SourceTypeName = sourceTypeName ?? string.Empty;
            diagnostic.SourceMemberName = sourceMemberName ?? string.Empty;
            diagnostic.SyntaxKind = syntaxKind ?? string.Empty;
            diagnostic.Recommendation = recommendation ?? string.Empty;
            diagnostic.FilePath = filePath ?? string.Empty;
            throw new NotSupportedException($"CPP1001 {diagnostic.FilePath}: {diagnostic.SourceTypeName}.{diagnostic.SourceMemberName}: {message}");
        }

        /// <inheritdoc />
        public IReadOnlyList<INamedTypeSymbol> GetInstantiatedGeneratedTypes(Compilation compilation) {
            return Owner.GetInstantiatedGeneratedTypes(compilation);
        }

        /// <summary>
        /// Creates a fresh registrar that shares the catalog and build usage report but records only one class's requirements.
        /// </summary>
        /// <returns>The per-class registrar.</returns>
        CPPRuntimeRequirementRegistrar CreateClassRegistrar() {
            CPPRuntimeRequirementRegistrar registrar = new CPPRuntimeRequirementRegistrar(Owner.RuntimeRequirementCatalog, WorkerReport);
            registrar.ApplyBuildUsageReport(Owner.BuildUsageReport);
            return registrar;
        }
    }
}
