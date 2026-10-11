using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using cs2.core.Pipeline;

namespace cs2.core {
    public abstract class CodeConverter {
        protected Project project;
        protected ConversionContext context;
        protected ConversionProgram program;
        protected ConversionRules rules;

        protected MSBuildWorkspace workspace;

        /// <summary>
        /// Load failures the workspace reported while the current project opened; checked after the load returns.
        /// </summary>
        readonly List<string> workspaceFailures = new List<string>();

        /// <summary>
        /// When true, a loaded project that does not compile fails the conversion instead of being converted with
        /// partially bound symbols. Off by default: test fixtures and partial projects are converted as before; the
        /// webhub runtime converter turns it on because an unbound identifier there breaks every browser session.
        /// </summary>
        public bool RequireCleanCompilation { get; set; }

        protected abstract string[] PreProcessorSymbols { get; }

        public CodeConverter(ConversionRules rules) {
            this.rules = rules;
        }

        public void AddCsproj(string csprojPath) {
            if (!File.Exists(csprojPath)) {
                Console.WriteLine($"The .csproj file '{csprojPath}' does not exist.");
                throw new Exception($"The .csproj file '{csprojPath}' does not exist.");
            }

            if (program == null) {
                throw new InvalidOperationException("Conversion program must be initialized before loading a project.");
            }

            if (workspace == null) {
                workspace = MSBuildWorkspace.Create();
                workspace.WorkspaceFailed += OnWorkspaceFailed;
            }
            context ??= new ConversionContext(program);

            workspaceFailures.Clear();
            project = AsyncUtil.RunSync(() => workspace.OpenProjectAsync(csprojPath));
            ThrowOnWorkspaceFailures(csprojPath);
            if (RequireCleanCompilation) {
                ThrowOnCompilationErrors(project);
            }
            OnProjectOpened();

            ConversionSession session = new ConversionSession(project, workspace, program, context, rules, this);
            ConversionPipeline pipeline = BuildPipeline();
            pipeline.Execute(session);

            project = session.Project;
        }

        /// <summary>
        /// Records workspace load failures raised while a project opens, so the conversion can fail loudly once the
        /// load returns instead of converting a project whose references never resolved.
        /// </summary>
        /// <param name="sender">Workspace raising the diagnostic.</param>
        /// <param name="e">Diagnostic describing the failure or warning.</param>
        void OnWorkspaceFailed(object sender, WorkspaceDiagnosticEventArgs e) {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure) {
                workspaceFailures.Add(e.Diagnostic.Message);
                return;
            }

            Console.WriteLine($"-- workspace warning: {e.Diagnostic.Message}");
        }

        /// <summary>
        /// Fails the conversion when the workspace reported a load failure for the project just opened. A partial
        /// load leaves symbols unbound, and every backend would then emit raw member names in silence (a Guid
        /// `ToString` that never reaches the `toString` remap, for example).
        /// </summary>
        /// <param name="csprojPath">Project whose load was attempted.</param>
        void ThrowOnWorkspaceFailures(string csprojPath) {
            if (workspaceFailures.Count == 0) {
                return;
            }

            foreach (string failure in workspaceFailures) {
                Console.WriteLine($"-- workspace failure: {failure}");
            }

            throw new InvalidOperationException(
                $"Workspace failed to load '{csprojPath}' ({workspaceFailures.Count} failure(s)); conversion would emit unbound identifiers.");
        }

        /// <summary>
        /// Fails the conversion when the loaded project does not compile, because a semantic model with errors binds
        /// symbols only partially and the backends cannot tell a deliberate raw identifier from an unresolved one.
        /// </summary>
        /// <param name="loadedProject">Project returned by the workspace.</param>
        static void ThrowOnCompilationErrors(Project loadedProject) {
            Compilation compilation = AsyncUtil.RunSync(() => loadedProject.GetCompilationAsync());
            if (compilation == null) {
                throw new InvalidOperationException($"Project '{loadedProject.Name}' produced no compilation.");
            }

            List<Diagnostic> errors = compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToList();
            if (errors.Count == 0) {
                return;
            }

            foreach (Diagnostic error in errors.Take(25)) {
                Console.WriteLine($"-- compilation error: {error}");
            }

            throw new InvalidOperationException(
                $"Project '{loadedProject.Name}' has {errors.Count} compilation error(s); conversion would emit unbound identifiers.");
        }

        protected virtual ConversionPipeline BuildPipeline() {
            ConversionPipelineBuilder builder = new ConversionPipelineBuilder();
            ConfigurePipeline(builder);
            return builder.Build();
        }

        protected virtual void ConfigurePipeline(ConversionPipelineBuilder builder) {
            builder.AddStage(new ResetConversionStateStage())
                   .AddStage(new ApplyPreprocessorSymbolsStage(PreProcessorSymbols))
                   .AddStage(new DocumentPreprocessingStage())
                   .AddStage(new ClassProcessingStage())
                   .AddStage(new ProgramSortingStage());
        }

        /// <summary>
        /// Runs after the Roslyn project is open and before the pipeline executes, so backends can join startup work that overlapped the workspace load.
        /// </summary>
        protected virtual void OnProjectOpened() {
        }

        protected virtual void SortProgram() {
            program.Classes.Sort((a, b) => {
                int delegateComparison = (b.DeclarationType == MemberDeclarationType.Delegate).CompareTo(a.DeclarationType == MemberDeclarationType.Delegate);
                if (delegateComparison != 0) {
                    return delegateComparison;
                }

                // Prioritize enums after abstract classes
                int enumComparison = (b.DeclarationType == MemberDeclarationType.Enum).CompareTo(a.DeclarationType == MemberDeclarationType.Enum);
                if (enumComparison != 0) {
                    return enumComparison;
                }

                // Prioritize interfaces over other types
                int interfaceComparison = (b.DeclarationType == MemberDeclarationType.Interface).CompareTo(a.DeclarationType == MemberDeclarationType.Interface);
                if (interfaceComparison != 0) {
                    return interfaceComparison;
                }

                // If both are interfaces, prioritize those with fewer extensions
                if (a.DeclarationType == MemberDeclarationType.Interface && b.DeclarationType == MemberDeclarationType.Interface) {
                    int extensionComparison = a.Extensions.Count - b.Extensions.Count;
                    if (extensionComparison != 0) {
                        return extensionComparison;
                    }
                    // Alphabetical order as a final tie-breaker
                    return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
                }

                // Prioritize abstract classes after interfaces
                int abstractComparison = (b.DeclarationType == MemberDeclarationType.Abstract).CompareTo(a.DeclarationType == MemberDeclarationType.Abstract);
                if (abstractComparison != 0) {
                    return abstractComparison;
                }

                // If both are abstract classes, prioritize those with fewer extensions
                if (a.DeclarationType == MemberDeclarationType.Abstract && b.DeclarationType == MemberDeclarationType.Abstract) {
                    int extensionComparison = a.Extensions.Count - b.Extensions.Count;
                    if (extensionComparison != 0) {
                        return extensionComparison;
                    }
                    // Alphabetical order as a final tie-breaker
                    return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
                }

                // For other types, sort by Extensions.Count
                int extensionsComparison = a.Extensions.Count - b.Extensions.Count;
                if (extensionsComparison != 0) {
                    return extensionsComparison;
                }

                // Alphabetical order as a final tie-breaker for any remaining equality
                return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });
        }

        protected virtual void SortVariables(ConversionClass cl) {
            if (cl.IsValueType) {
                return;
            }

            cl.Variables.Sort((a, b) => {
                int staticComparison = b.IsStatic.CompareTo(a.IsStatic);
                if (staticComparison != 0) {
                    return staticComparison;
                }

                int accessComparison = a.AccessType.CompareTo(b.AccessType);
                if (accessComparison != 0) {
                    return accessComparison;
                }

                return a.SourceOrder.CompareTo(b.SourceOrder);
            });
        }

        protected virtual void SortFunctions(ConversionClass cl) {
            cl.Functions.Sort((a, b) => {
                int accessComparison = a.AccessType.CompareTo(b.AccessType);
                if (accessComparison != 0) {
                    return accessComparison;
                }

                return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });
        }

        protected abstract void PreProcessExpression(SemanticModel model, MemberDeclarationSyntax member, ConversionContext context);

        protected abstract void ProcessClass(ConversionClass cl, ConversionProgram program);

        internal void RunPreProcess(SemanticModel model, MemberDeclarationSyntax member, ConversionContext ctx) {
            PreProcessExpression(model, member, ctx);
        }

        internal void RunProcessClass(ConversionClass cl, ConversionProgram conversionProgram) {
            ProcessClass(cl, conversionProgram);
        }

        internal void SortMembers(ConversionClass cl) {
            SortVariables(cl);
            SortFunctions(cl);
        }

        internal void SortProgramInternal() {
            SortProgram();
        }
    }
}
