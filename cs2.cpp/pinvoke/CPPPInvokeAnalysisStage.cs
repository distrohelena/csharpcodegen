using cs2.core;
using cs2.core.Pipeline;
using Microsoft.CodeAnalysis;

namespace cs2.cpp;

/// <summary>
/// Runs P/Invoke analysis after document preprocessing, records every CPPPINV diagnostic, and hands the validated
/// plan of native imports, callback trampolines, and mirror structs to the converter before ownership analysis runs.
/// </summary>
internal sealed class CPPPInvokeAnalysisStage : IConversionStage {
    /// <summary>
    /// Holds the converter that receives diagnostics and the validated P/Invoke plan.
    /// </summary>
    readonly CPPCodeConverter Owner;

    /// <summary>
    /// Initializes the P/Invoke gate for one converter.
    /// </summary>
    /// <param name="owner">Converter that receives the diagnostics and the validated plan.</param>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is null.</exception>
    public CPPPInvokeAnalysisStage(CPPCodeConverter owner) {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <summary>
    /// Analyzes every DllImport and UnmanagedCallersOnly declaration in the active project closure, records the
    /// diagnostics, rejects hard errors, and stores the validated plan on the converter.
    /// </summary>
    /// <param name="session">The active conversion session after document preprocessing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    /// <exception cref="InvalidOperationException">At least one P/Invoke declaration was rejected.</exception>
    public void Execute(ConversionSession session) {
        if (session == null) {
            throw new ArgumentNullException(nameof(session));
        }

        IReadOnlyList<Compilation> compilations = CPPConversionStageUtils.CollectCompilations(session.Project, "P/Invoke analysis");
        CPPPInvokeAnalysisResult result = new CPPPInvokeAnalyzer().Analyze(compilations);
        CPPConversionStageUtils.AppendDiagnostics(Owner.Report, result.Diagnostics);

        if (result.HasErrors) {
            CPPConversionDiagnostic firstError = result.Diagnostics
                .First(diagnostic => diagnostic.Severity == CPPDiagnosticSeverity.Error);
            throw new InvalidOperationException(CPPConversionStageUtils.FormatFailure(firstError));
        }

        Owner.SetPInvokePlan(result.Plan);
    }
}
