using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace cs2.core.Pipeline {
    /// <summary>
    /// Applies preprocessor symbol changes across a project and every transitive project reference in its solution closure.
    /// </summary>
    public static class ProjectPreprocessorSymbolApplicator {
        /// <summary>
        /// Replaces the active preprocessor symbol set on the supplied project closure.
        /// </summary>
        /// <param name="rootProject">Root project whose closure should receive the replacement symbol set.</param>
        /// <param name="symbols">Preprocessor symbols that should replace each project's existing parse symbols.</param>
        /// <returns>The updated root project from the rewritten solution.</returns>
        public static Project ReplaceSymbols(Project rootProject, IEnumerable<string> symbols) {
            return ApplySymbols(rootProject, NormalizeSymbols(symbols), true);
        }

        /// <summary>
        /// Adds preprocessor symbols to the supplied project closure while preserving each project's existing parse symbols.
        /// </summary>
        /// <param name="rootProject">Root project whose closure should receive the additional symbol set.</param>
        /// <param name="symbols">Preprocessor symbols that should be added to each project's existing parse symbols.</param>
        /// <returns>The updated root project from the rewritten solution.</returns>
        public static Project AddSymbols(Project rootProject, IEnumerable<string> symbols) {
            return ApplySymbols(rootProject, NormalizeSymbols(symbols), false);
        }

        /// <summary>
        /// Normalizes one symbol sequence by removing blanks and duplicates.
        /// </summary>
        /// <param name="symbols">Raw symbol sequence supplied by the caller.</param>
        /// <returns>Stable normalized symbol list.</returns>
        static IReadOnlyList<string> NormalizeSymbols(IEnumerable<string> symbols) {
            if (symbols == null) {
                throw new ArgumentNullException(nameof(symbols));
            }

            List<string> normalized = new List<string>();
            HashSet<string> seenSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string symbol in symbols) {
                if (string.IsNullOrWhiteSpace(symbol)) {
                    continue;
                }

                string trimmedSymbol = symbol.Trim();
                if (seenSymbols.Add(trimmedSymbol)) {
                    normalized.Add(trimmedSymbol);
                }
            }

            return normalized;
        }

        /// <summary>
        /// Applies either additive or replacement parse-symbol updates across one project closure.
        /// </summary>
        /// <param name="rootProject">Root project whose closure should be rewritten.</param>
        /// <param name="symbols">Normalized symbol set to apply.</param>
        /// <param name="replaceExistingSymbols">Whether existing project parse symbols should be replaced instead of extended.</param>
        /// <returns>The updated root project from the rewritten solution.</returns>
        static Project ApplySymbols(Project rootProject, IReadOnlyList<string> symbols, bool replaceExistingSymbols) {
            if (rootProject == null) {
                throw new ArgumentNullException(nameof(rootProject));
            }
            if (symbols == null) {
                throw new ArgumentNullException(nameof(symbols));
            }

            Solution updatedSolution = rootProject.Solution;
            IReadOnlyList<ProjectId> projectIds = EnumerateProjectClosure(rootProject);
            for (int index = 0; index < projectIds.Count; index++) {
                Project currentProject = updatedSolution.GetProject(projectIds[index]);
                if (currentProject == null || currentProject.ParseOptions is not CSharpParseOptions parseOptions) {
                    continue;
                }

                CSharpParseOptions updatedParseOptions = replaceExistingSymbols
                    ? parseOptions.WithPreprocessorSymbols(symbols)
                    : parseOptions.WithPreprocessorSymbols(CombineSymbols(parseOptions.PreprocessorSymbolNames, symbols));
                updatedSolution = currentProject.WithParseOptions(updatedParseOptions).Solution;
            }

            return updatedSolution.GetProject(rootProject.Id)
                ?? throw new InvalidOperationException($"Updated project '{rootProject.Name}' could not be resolved after applying preprocessor symbols.");
        }

        /// <summary>
        /// Combines existing parse symbols with additional backend-owned symbols while preserving first-seen order.
        /// </summary>
        /// <param name="existingSymbols">Project-defined parse symbols currently active for one project.</param>
        /// <param name="additionalSymbols">Backend-owned symbols that should be appended when missing.</param>
        /// <returns>Combined symbol list without duplicates.</returns>
        static IReadOnlyList<string> CombineSymbols(IEnumerable<string> existingSymbols, IReadOnlyList<string> additionalSymbols) {
            if (existingSymbols == null) {
                throw new ArgumentNullException(nameof(existingSymbols));
            }
            if (additionalSymbols == null) {
                throw new ArgumentNullException(nameof(additionalSymbols));
            }

            List<string> combined = new List<string>();
            HashSet<string> seenSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string symbol in existingSymbols) {
                if (string.IsNullOrWhiteSpace(symbol)) {
                    continue;
                }

                string trimmedSymbol = symbol.Trim();
                if (seenSymbols.Add(trimmedSymbol)) {
                    combined.Add(trimmedSymbol);
                }
            }

            for (int index = 0; index < additionalSymbols.Count; index++) {
                string symbol = additionalSymbols[index];
                if (seenSymbols.Add(symbol)) {
                    combined.Add(symbol);
                }
            }

            return combined;
        }

        /// <summary>
        /// Enumerates one project and every transitive project reference once in deterministic traversal order.
        /// </summary>
        /// <param name="rootProject">Root project whose closure should be enumerated.</param>
        /// <returns>Ordered project identifiers that belong to the closure.</returns>
        static IReadOnlyList<ProjectId> EnumerateProjectClosure(Project rootProject) {
            if (rootProject == null) {
                throw new ArgumentNullException(nameof(rootProject));
            }

            List<ProjectId> orderedProjectIds = new List<ProjectId>();
            HashSet<ProjectId> visitedProjectIds = new HashSet<ProjectId>();
            AddProjectClosure(rootProject, orderedProjectIds, visitedProjectIds);
            return orderedProjectIds;
        }

        /// <summary>
        /// Adds one project and each transitive reference to the ordered closure list.
        /// </summary>
        /// <param name="project">Project to append.</param>
        /// <param name="orderedProjectIds">Ordered closure list receiving discovered projects.</param>
        /// <param name="visitedProjectIds">Set of project identifiers already visited.</param>
        static void AddProjectClosure(Project project, List<ProjectId> orderedProjectIds, HashSet<ProjectId> visitedProjectIds) {
            if (project == null) {
                throw new ArgumentNullException(nameof(project));
            }
            if (orderedProjectIds == null) {
                throw new ArgumentNullException(nameof(orderedProjectIds));
            }
            if (visitedProjectIds == null) {
                throw new ArgumentNullException(nameof(visitedProjectIds));
            }

            if (!visitedProjectIds.Add(project.Id)) {
                return;
            }

            orderedProjectIds.Add(project.Id);

            foreach (ProjectReference projectReference in project.ProjectReferences) {
                Project referencedProject = project.Solution.GetProject(projectReference.ProjectId);
                if (referencedProject == null) {
                    continue;
                }

                AddProjectClosure(referencedProject, orderedProjectIds, visitedProjectIds);
            }
        }
    }
}
