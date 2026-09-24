using cs2.core;
using Microsoft.CodeAnalysis;

namespace cs2.cpp {
    /// <summary>
    /// Run state and reporting services a lowering processor needs, implemented by the converter on the main thread and by an emission worker on a pool thread.
    /// </summary>
    public interface ICPPConversionHost {
        /// <summary>
        /// Gets the active conversion options.
        /// </summary>
        CPPConversionOptions Options { get; }

        /// <summary>
        /// Gets the C++ conversion rules derived from the runtime profile.
        /// </summary>
        CPPConversionRules CPPRules { get; }

        /// <summary>
        /// Gets the shared program model; read-only while workers are running.
        /// </summary>
        ConversionProgram Program { get; }

        /// <summary>
        /// Gets the validated ownership plan for the active run, or null before analysis.
        /// </summary>
        CPPOwnershipAnalysisResult OwnershipAnalysisResult { get; }

        /// <summary>
        /// Gets the validated P/Invoke plan (native imports, callback trampolines, and mirror structs) for the active run,
        /// or null before P/Invoke analysis.
        /// </summary>
        CPPPInvokePlan PInvokePlan { get; }

        /// <summary>
        /// Gets the registrar that records runtime requirements for lowering performed through this host.
        /// </summary>
        CPPRuntimeRequirementRegistrar RuntimeRequirementRegistrar { get; }

        /// <summary>
        /// Gets the report that receives diagnostics for lowering performed through this host.
        /// </summary>
        CPPConversionReport Report { get; }

        /// <summary>
        /// Registers a named runtime requirement.
        /// </summary>
        /// <param name="name">Stable runtime requirement name.</param>
        void RegisterRuntimeRequirement(string name);

        /// <summary>
        /// Records an unsupported-construct diagnostic.
        /// </summary>
        /// <param name="sourceTypeName">The source type that contains the unsupported construct.</param>
        /// <param name="sourceMemberName">The source member that contains the unsupported construct.</param>
        /// <param name="syntaxKind">The Roslyn syntax kind that could not be lowered.</param>
        /// <param name="message">Human-readable explanation of why the construct is unsupported.</param>
        /// <param name="recommendation">Suggested next action to make the code portable.</param>
        /// <param name="filePath">The source file path when available.</param>
        void ReportUnsupportedConstruct(string sourceTypeName, string sourceMemberName, string syntaxKind, string message, string recommendation, string filePath = "");

        /// <summary>
        /// Records a runtime capability violation and throws so lowering stops.
        /// </summary>
        /// <param name="sourceTypeName">The source type that contains the capability-dependent construct.</param>
        /// <param name="sourceMemberName">The source member that contains the capability-dependent construct.</param>
        /// <param name="syntaxKind">The Roslyn syntax kind that requires the unavailable capability.</param>
        /// <param name="message">Human-readable explanation of the unavailable capability.</param>
        /// <param name="recommendation">Suggested action to make the construct portable.</param>
        /// <param name="filePath">The source file path when available.</param>
        void ReportRuntimeCapabilityViolation(string sourceTypeName, string sourceMemberName, string syntaxKind, string message, string recommendation, string filePath = "");

        /// <summary>
        /// Gets the concrete generated types instantiated in one compilation.
        /// </summary>
        /// <param name="compilation">Compilation to scan.</param>
        /// <returns>Distinct instantiated generated types.</returns>
        IReadOnlyList<INamedTypeSymbol> GetInstantiatedGeneratedTypes(Compilation compilation);
    }
}
