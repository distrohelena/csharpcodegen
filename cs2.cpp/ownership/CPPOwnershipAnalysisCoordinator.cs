using Microsoft.CodeAnalysis;

namespace cs2.cpp;

/// <summary>
/// Coordinates method-summary resolution and control-flow ownership analysis for one complete C++ conversion closure.
/// </summary>
public sealed class CPPOwnershipAnalysisCoordinator {
    /// <summary>
    /// Resolves native ownership contracts and the lowering plan for every supplied source compilation.
    /// </summary>
    /// <param name="compilations">Root and transitively referenced compilations participating in one conversion.</param>
    /// <returns>The immutable semantic ownership result consumed by later C++ lowering stages.</returns>
    public CPPOwnershipAnalysisResult Analyze(IReadOnlyList<Compilation> compilations) {
        if (compilations == null) {
            throw new ArgumentNullException(nameof(compilations));
        }

        CPPMethodOwnershipSummaryResolution summaries = new CPPMethodOwnershipSummaryResolver().Resolve(compilations);
        return new CPPLocalOwnershipAnalyzer().Analyze(compilations, summaries);
    }
}
