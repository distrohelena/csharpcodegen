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

        IReadOnlyList<Compilation> compilations = CollectCompilations(session.Project);
        CPPPInvokeAnalysisResult result = new CPPPInvokeAnalyzer().Analyze(compilations);
        AppendDiagnostics(result.Diagnostics);

        if (result.HasErrors) {
            CPPConversionDiagnostic firstError = result.Diagnostics
                .First(diagnostic => diagnostic.Severity == CPPDiagnosticSeverity.Error);
            throw new InvalidOperationException(FormatFailure(firstError));
        }

        Owner.SetPInvokePlan(result.Plan);
    }

    /// <summary>
    /// Collects one Roslyn compilation for the root project and each transitive project reference exactly once.
    /// </summary>
    /// <param name="rootProject">The active project supplied to the conversion pipeline.</param>
    /// <returns>Compilations ordered from the root project through its reference closure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rootProject"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A project in the closure could not be compiled.</exception>
    static IReadOnlyList<Compilation> CollectCompilations(Project rootProject) {
        if (rootProject == null) {
            throw new ArgumentNullException(nameof(rootProject));
        }

        List<Project> projects = new List<Project>();
        AddProject(rootProject, projects, new HashSet<ProjectId>());
        List<Compilation> compilations = new List<Compilation>(projects.Count);
        foreach (Project project in projects) {
            Compilation compilation = AsyncUtil.RunSync(() => project.GetCompilationAsync());
            if (compilation == null) {
                throw new InvalidOperationException($"P/Invoke analysis could not compile project '{project.Name}'.");
            }

            compilations.Add(compilation);
        }

        return compilations;
    }

    /// <summary>
    /// Adds one project and its transitive references to a deterministic root-first sequence.
    /// </summary>
    /// <param name="project">Project currently being visited.</param>
    /// <param name="projects">Ordered destination for distinct projects.</param>
    /// <param name="visitedProjectIds">Project identities already included in the closure.</param>
    static void AddProject(Project project, List<Project> projects, HashSet<ProjectId> visitedProjectIds) {
        if (!visitedProjectIds.Add(project.Id)) {
            return;
        }

        projects.Add(project);
        foreach (ProjectReference projectReference in project.ProjectReferences) {
            Project referencedProject = project.Solution.GetProject(projectReference.ProjectId);
            if (referencedProject != null) {
                AddProject(referencedProject, projects, visitedProjectIds);
            }
        }
    }

    /// <summary>
    /// Appends P/Invoke diagnostics to the conversion report, skipping any diagnostic the report already holds.
    /// </summary>
    /// <param name="diagnostics">Diagnostics produced by P/Invoke analysis.</param>
    void AppendDiagnostics(IReadOnlyList<CPPConversionDiagnostic> diagnostics) {
        foreach (CPPConversionDiagnostic diagnostic in diagnostics) {
            if (!ContainsDiagnostic(diagnostic)) {
                Owner.Report.Diagnostics.Add(diagnostic);
            }
        }
    }

    /// <summary>
    /// Determines whether the active report already contains an equivalent diagnostic, matching code, message,
    /// file, line, and column.
    /// </summary>
    /// <param name="candidate">Diagnostic considered for insertion.</param>
    /// <returns>True when an equivalent diagnostic is already present; otherwise false.</returns>
    bool ContainsDiagnostic(CPPConversionDiagnostic candidate) {
        return Owner.Report.Diagnostics.Any(existing =>
            existing.Code == candidate.Code &&
            existing.Message == candidate.Message &&
            existing.FilePath == candidate.FilePath &&
            existing.LineNumber == candidate.LineNumber &&
            existing.ColumnNumber == candidate.ColumnNumber);
    }

    /// <summary>
    /// Formats the first P/Invoke error as a compact source-located pipeline failure.
    /// </summary>
    /// <param name="diagnostic">The first P/Invoke error that prevented lowering.</param>
    /// <returns>An exception message beginning with the stable diagnostic code and location.</returns>
    static string FormatFailure(CPPConversionDiagnostic diagnostic) {
        string location = string.IsNullOrWhiteSpace(diagnostic.FilePath)
            ? "unknown source"
            : diagnostic.LineNumber > 0
                ? $"{diagnostic.FilePath}({diagnostic.LineNumber},{diagnostic.ColumnNumber})"
                : diagnostic.FilePath;
        return $"{diagnostic.Code} {location}: {diagnostic.Message}";
    }
}
