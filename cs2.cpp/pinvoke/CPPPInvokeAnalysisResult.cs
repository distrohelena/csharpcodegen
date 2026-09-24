namespace cs2.cpp;

/// <summary>
/// Captures the outcome of running <see cref="CPPPInvokeAnalyzer.Analyze(IReadOnlyList{Microsoft.CodeAnalysis.Compilation})"/>:
/// the validated plan of native forwarders, callback trampolines, and mirror structs, together with every diagnostic
/// raised while analyzing P/Invoke declarations.
/// </summary>
public sealed class CPPPInvokeAnalysisResult {
    /// <summary>
    /// Creates an analysis result pairing a plan with the diagnostics raised while building it.
    /// </summary>
    /// <param name="plan">Validated plan built from every P/Invoke declaration that passed analysis.</param>
    /// <param name="diagnostics">Every diagnostic raised while analyzing P/Invoke declarations, including hard
    /// errors for declarations that were rejected and excluded from <paramref name="plan"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="plan"/> or <paramref name="diagnostics"/> is null.</exception>
    public CPPPInvokeAnalysisResult(CPPPInvokePlan plan, IReadOnlyList<CPPConversionDiagnostic> diagnostics) {
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <summary>
    /// Gets the validated plan built from every P/Invoke declaration that passed analysis. Declarations that failed
    /// validation are excluded, so every entry in the plan is safe to lower to direct native calls.
    /// </summary>
    public CPPPInvokePlan Plan { get; }

    /// <summary>
    /// Gets every diagnostic raised while analyzing P/Invoke declarations, including hard errors for declarations
    /// that were rejected and excluded from <see cref="Plan"/>.
    /// </summary>
    public IReadOnlyList<CPPConversionDiagnostic> Diagnostics { get; }

    /// <summary>
    /// Gets a value indicating whether any diagnostic in <see cref="Diagnostics"/> is an error, meaning at least one
    /// P/Invoke declaration was rejected and excluded from <see cref="Plan"/>.
    /// </summary>
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == CPPDiagnosticSeverity.Error);
}
