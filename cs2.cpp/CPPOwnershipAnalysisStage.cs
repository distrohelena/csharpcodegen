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

        IReadOnlyList<Compilation> compilations = CollectCompilations(session.Project);
        CPPOwnershipAnalysisResult result = Coordinator.Analyze(compilations);
        AppendDiagnostics(result.MethodSummaries.Diagnostics);
        AppendDiagnostics(result.Diagnostics);

        if (result.HasErrors) {
            CPPConversionDiagnostic firstError = result.MethodSummaries.Diagnostics
                .Concat(result.Diagnostics)
                .First(diagnostic => diagnostic.Severity == CPPDiagnosticSeverity.Error);
            throw new InvalidOperationException(FormatFailure(firstError));
        }

        Owner.SetOwnershipAnalysisResult(result);
    }

    /// <summary>
    /// Collects one Roslyn compilation for the root project and each transitive project reference exactly once.
    /// </summary>
    /// <param name="rootProject">The active project supplied to the conversion pipeline.</param>
    /// <returns>Compilations ordered from the root project through its reference closure.</returns>
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
                throw new InvalidOperationException($"Ownership analysis could not compile project '{project.Name}'.");
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
    /// Appends semantic ownership diagnostics to the conversion report without discarding source or recommendation metadata.
    /// </summary>
    /// <param name="diagnostics">Diagnostics produced by one ownership-analysis phase.</param>
    void AppendDiagnostics(IReadOnlyList<CPPConversionDiagnostic> diagnostics) {
        foreach (CPPConversionDiagnostic diagnostic in diagnostics) {
            if (!ContainsDiagnostic(diagnostic)) {
                Owner.Report.Diagnostics.Add(diagnostic);
            }
        }
    }

    /// <summary>
    /// Determines whether the active report already contains the same semantic ownership diagnostic.
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
    /// Formats the first hard error as a compact source-located pipeline failure.
    /// </summary>
    /// <param name="diagnostic">The first ownership error that prevented lowering.</param>
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
