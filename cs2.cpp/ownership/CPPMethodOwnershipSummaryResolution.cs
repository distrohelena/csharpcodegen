using Microsoft.CodeAnalysis;

namespace cs2.cpp;

/// <summary>
/// Contains fixed-point method ownership summaries and all hard errors discovered while resolving them.
/// </summary>
public sealed class CPPMethodOwnershipSummaryResolution {
    /// <summary>
    /// Stores immutable method summaries keyed by stable source identity.
    /// </summary>
    readonly IReadOnlyDictionary<string, CPPMethodOwnershipSummary> SummaryValues;

    /// <summary>
    /// Stores immutable diagnostics produced during summary resolution.
    /// </summary>
    readonly IReadOnlyList<CPPConversionDiagnostic> DiagnosticValues;

    /// <summary>
    /// Initializes one complete ownership summary result.
    /// </summary>
    /// <param name="summaries">Resolved method summaries keyed by stable method identity.</param>
    /// <param name="diagnostics">Hard errors discovered during summary resolution.</param>
    public CPPMethodOwnershipSummaryResolution(
        IReadOnlyDictionary<string, CPPMethodOwnershipSummary> summaries,
        IReadOnlyList<CPPConversionDiagnostic> diagnostics) {
        SummaryValues = summaries != null
            ? new Dictionary<string, CPPMethodOwnershipSummary>(summaries, StringComparer.Ordinal)
            : throw new ArgumentNullException(nameof(summaries));
        DiagnosticValues = diagnostics != null
            ? diagnostics.ToArray()
            : throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <summary>
    /// Gets immutable method summaries keyed by stable source identity.
    /// </summary>
    public IReadOnlyDictionary<string, CPPMethodOwnershipSummary> Summaries => SummaryValues;

    /// <summary>
    /// Gets immutable diagnostics produced while resolving method contracts.
    /// </summary>
    public IReadOnlyList<CPPConversionDiagnostic> Diagnostics => DiagnosticValues;

    /// <summary>
    /// Gets whether summary resolution produced at least one hard error.
    /// </summary>
    public bool HasErrors => DiagnosticValues.Any(diagnostic => diagnostic.Severity == CPPDiagnosticSeverity.Error);

    /// <summary>
    /// Resolves the ownership summary for one Roslyn method symbol.
    /// </summary>
    /// <param name="method">Method whose summary is required.</param>
    /// <returns>The resolved ownership summary.</returns>
    public CPPMethodOwnershipSummary GetSummary(IMethodSymbol method) {
        string methodKey = CPPMethodOwnershipKey.Create(method);
        if (!SummaryValues.TryGetValue(methodKey, out CPPMethodOwnershipSummary summary)) {
            throw new KeyNotFoundException($"Ownership summary '{methodKey}' was not resolved.");
        }

        return summary;
    }
}
