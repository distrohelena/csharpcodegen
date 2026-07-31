namespace cs2.cpp;

/// <summary>
/// Contains method summaries, local emission plans, and hard errors for one semantic ownership analysis run.
/// </summary>
public sealed class CPPOwnershipAnalysisResult {
    /// <summary>
    /// Stores immutable diagnostics produced by local ownership analysis.
    /// </summary>
    readonly IReadOnlyList<CPPConversionDiagnostic> DiagnosticValues;

    /// <summary>
    /// Initializes one complete semantic ownership analysis result.
    /// </summary>
    /// <param name="methodSummaries">Resolved method return and parameter contracts.</param>
    /// <param name="emissionPlan">Plan consumed by generated C++ lowering.</param>
    /// <param name="diagnostics">Hard errors discovered while analyzing local lifetimes.</param>
    public CPPOwnershipAnalysisResult(
        CPPMethodOwnershipSummaryResolution methodSummaries,
        CPPOwnershipEmissionPlan emissionPlan,
        IReadOnlyList<CPPConversionDiagnostic> diagnostics) {
        MethodSummaries = methodSummaries ?? throw new ArgumentNullException(nameof(methodSummaries));
        EmissionPlan = emissionPlan ?? throw new ArgumentNullException(nameof(emissionPlan));
        DiagnosticValues = diagnostics != null
            ? diagnostics.ToArray()
            : throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <summary>
    /// Gets resolved method return and parameter ownership contracts.
    /// </summary>
    public CPPMethodOwnershipSummaryResolution MethodSummaries { get; }

    /// <summary>
    /// Gets the immutable semantic plan consumed by C++ emission.
    /// </summary>
    public CPPOwnershipEmissionPlan EmissionPlan { get; }

    /// <summary>
    /// Gets hard errors discovered while analyzing local lifetimes.
    /// </summary>
    public IReadOnlyList<CPPConversionDiagnostic> Diagnostics => DiagnosticValues;

    /// <summary>
    /// Gets whether local or method summary analysis produced at least one hard error.
    /// </summary>
    public bool HasErrors => MethodSummaries.HasErrors || DiagnosticValues.Any(diagnostic => diagnostic.Severity == CPPDiagnosticSeverity.Error);
}
