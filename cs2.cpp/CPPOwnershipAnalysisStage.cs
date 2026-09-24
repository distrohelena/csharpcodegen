using cs2.core.Pipeline;
using cs2.core;
using Microsoft.CodeAnalysis;

namespace cs2.cpp;

/// <summary>
/// Runs semantic native ownership analysis after preprocessing and prevents invalid programs from reaching C++ lowering.
/// </summary>
internal sealed class CPPOwnershipAnalysisStage : IConversionStage {
    /// <summary>
    /// Holds the converter that receives diagnostics and the validated ownership plan.
    /// </summary>
    readonly CPPCodeConverter Owner;

    /// <summary>
    /// Coordinates method-summary and control-flow ownership analysis.
    /// </summary>
    readonly CPPOwnershipAnalysisCoordinator Coordinator;

    /// <summary>
    /// Initializes the ownership gate for one converter.
    /// </summary>
    /// <param name="owner">Converter that receives the validated analysis result.</param>
    public CPPOwnershipAnalysisStage(CPPCodeConverter owner)
        : this(owner, new CPPOwnershipAnalysisCoordinator()) {
    }

    /// <summary>
    /// Initializes the ownership gate with an explicit coordinator for focused validation.
    /// </summary>
    /// <param name="owner">Converter that receives the validated analysis result.</param>
    /// <param name="coordinator">Coordinator that performs semantic ownership analysis.</param>
    internal CPPOwnershipAnalysisStage(CPPCodeConverter owner, CPPOwnershipAnalysisCoordinator coordinator) {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    /// <summary>
    /// Analyzes the active project closure, records every diagnostic, and rejects hard errors before class processing starts.
    /// </summary>
    /// <param name="session">The active conversion session after document preprocessing.</param>
    public void Execute(ConversionSession session) {
        if (session == null) {
            throw new ArgumentNullException(nameof(session));
        }

        IReadOnlyList<Compilation> compilations = CPPConversionStageUtils.CollectCompilations(session.Project, "Ownership analysis");
        int workerCount = CPPWorkerThreadOptionResolver.Resolve(Owner.Options);
        CPPOwnershipAnalysisResult result = Coordinator.Analyze(compilations, workerCount);
        CPPConversionStageUtils.AppendDiagnostics(Owner.Report, result.MethodSummaries.Diagnostics);
        CPPConversionStageUtils.AppendDiagnostics(Owner.Report, result.Diagnostics);

        if (result.HasErrors) {
            CPPConversionDiagnostic firstError = result.MethodSummaries.Diagnostics
                .Concat(result.Diagnostics)
                .First(diagnostic => diagnostic.Severity == CPPDiagnosticSeverity.Error);
            throw new InvalidOperationException(CPPConversionStageUtils.FormatFailure(firstError));
        }

        Owner.SetOwnershipAnalysisResult(result);
    }
}
