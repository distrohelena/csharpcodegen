using cs2.core;
using Microsoft.CodeAnalysis;

namespace cs2.cpp;

/// <summary>
/// Shared helpers for semantic analysis stages that inspect the whole project closure before C++ lowering: collecting
/// the Roslyn compilations to analyze, recording diagnostics without duplicates, and formatting the hard error that
/// stops the pipeline.
/// </summary>
internal static class CPPConversionStageUtils {
    /// <summary>
    /// Collects one Roslyn compilation for the root project and each transitive project reference exactly once.
    /// </summary>
    /// <param name="rootProject">The active project supplied to the conversion pipeline.</param>
    /// <param name="failureContext">Name of the analysis that needs the compilations, used as the start of the
    /// failure message when a project cannot be compiled (for example "Ownership analysis").</param>
    /// <returns>Compilations ordered from the root project through its reference closure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rootProject"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A project in the closure could not be compiled.</exception>
    public static IReadOnlyList<Compilation> CollectCompilations(Project rootProject, string failureContext) {
        if (rootProject == null) {
            throw new ArgumentNullException(nameof(rootProject));
        }

        List<Project> projects = new List<Project>();
        AddProject(rootProject, projects, new HashSet<ProjectId>());
        List<Compilation> compilations = new List<Compilation>(projects.Count);
        foreach (Project project in projects) {
            Compilation compilation = AsyncUtil.RunSync(() => project.GetCompilationAsync());
            if (compilation == null) {
                throw new InvalidOperationException($"{failureContext} could not compile project '{project.Name}'.");
            }

            compilations.Add(compilation);
        }

        return compilations;
    }

    /// <summary>
    /// Appends analysis diagnostics to a conversion report, skipping any diagnostic the report already holds so
    /// source and recommendation metadata are kept without duplicate entries.
    /// </summary>
    /// <param name="report">Conversion report that receives the diagnostics.</param>
    /// <param name="diagnostics">Diagnostics produced by one analysis phase.</param>
    public static void AppendDiagnostics(CPPConversionReport report, IReadOnlyList<CPPConversionDiagnostic> diagnostics) {
        foreach (CPPConversionDiagnostic diagnostic in diagnostics) {
            if (!ContainsDiagnostic(report, diagnostic)) {
                report.Diagnostics.Add(diagnostic);
            }
        }
    }

    /// <summary>
    /// Formats a hard analysis error as a compact source-located pipeline failure.
    /// </summary>
    /// <param name="diagnostic">The first error that prevented lowering.</param>
    /// <returns>An exception message beginning with the stable diagnostic code and location.</returns>
    public static string FormatFailure(CPPConversionDiagnostic diagnostic) {
        string location = string.IsNullOrWhiteSpace(diagnostic.FilePath)
            ? "unknown source"
            : diagnostic.LineNumber > 0
                ? $"{diagnostic.FilePath}({diagnostic.LineNumber},{diagnostic.ColumnNumber})"
                : diagnostic.FilePath;
        return $"{diagnostic.Code} {location}: {diagnostic.Message}";
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
    /// Determines whether a report already contains an equivalent diagnostic, matching code, message, file, line,
    /// and column.
    /// </summary>
    /// <param name="report">Conversion report searched for an equivalent entry.</param>
    /// <param name="candidate">Diagnostic considered for insertion.</param>
    /// <returns>True when an equivalent diagnostic is already present; otherwise false.</returns>
    static bool ContainsDiagnostic(CPPConversionReport report, CPPConversionDiagnostic candidate) {
        return report.Diagnostics.Any(existing =>
            existing.Code == candidate.Code &&
            existing.Message == candidate.Message &&
            existing.FilePath == candidate.FilePath &&
            existing.LineNumber == candidate.LineNumber &&
            existing.ColumnNumber == candidate.ColumnNumber);
    }
}
