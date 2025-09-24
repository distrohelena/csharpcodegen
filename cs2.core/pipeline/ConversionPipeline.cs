using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Nucleus;

namespace cs2.core.Pipeline {
    public interface IConversionStage {
        void Execute(ConversionSession session);
    }

    public sealed class ConversionPipeline {
        private readonly IReadOnlyList<IConversionStage> stages;

        public ConversionPipeline(IEnumerable<IConversionStage> stages) {
            if (stages == null) {
                throw new ArgumentNullException(nameof(stages));
            }

            this.stages = stages.ToList();
            if (this.stages.Count == 0) {
                throw new ArgumentException("The conversion pipeline must contain at least one stage.", nameof(stages));
            }
        }

        public void Execute(ConversionSession session) {
            if (session == null) {
                throw new ArgumentNullException(nameof(session));
            }

            foreach (IConversionStage stage in stages) {
                stage.Execute(session);
            }
        }
    }

    public sealed class ConversionPipelineBuilder {
        private readonly List<IConversionStage> stages = new();

        public IList<IConversionStage> Stages => stages;

        public ConversionPipelineBuilder AddStage(IConversionStage stage) {
            if (stage == null) {
                throw new ArgumentNullException(nameof(stage));
            }

            stages.Add(stage);
            return this;
        }

        public ConversionPipelineBuilder Insert(int index, IConversionStage stage) {
            if (stage == null) {
                throw new ArgumentNullException(nameof(stage));
            }

            stages.Insert(index, stage);
            return this;
        }

        public ConversionPipeline Build() {
            return new ConversionPipeline(stages);
        }
    }

    public sealed class ConversionSession {
        public ConversionSession(Project project, MSBuildWorkspace workspace, ConversionProgram program, ConversionContext context, ConversionRules rules, CodeConverter converter) {
            Project = project ?? throw new ArgumentNullException(nameof(project));
            Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            Program = program ?? throw new ArgumentNullException(nameof(program));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Converter = converter ?? throw new ArgumentNullException(nameof(converter));

            Items = new Dictionary<string, object>();
        }

        public Project Project { get; set; }
        public MSBuildWorkspace Workspace { get; }
        public ConversionProgram Program { get; }
        public ConversionContext Context { get; }
        public ConversionRules Rules { get; }
        public CodeConverter Converter { get; }
        public IDictionary<string, object> Items { get; }
    }

    public sealed class ResetConversionStateStage : IConversionStage {
        public void Execute(ConversionSession session) {
            session.Context.Reset();
        }
    }

    public sealed class ApplyPreprocessorSymbolsStage : IConversionStage {
        private readonly IReadOnlyCollection<string> symbols;

        public ApplyPreprocessorSymbolsStage(IEnumerable<string> symbols) {
            this.symbols = symbols?.Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                                   .Select(symbol => symbol.Trim())
                                   .Distinct(StringComparer.OrdinalIgnoreCase)
                                   .ToArray()
                ?? Array.Empty<string>();
        }

        public void Execute(ConversionSession session) {
            if (session.Project.ParseOptions is not CSharpParseOptions parseOptions) {
                return;
            }

            HashSet<string> combined = new HashSet<string>(parseOptions.PreprocessorSymbolNames, StringComparer.OrdinalIgnoreCase);
            foreach (string symbol in symbols) {
                combined.Add(symbol);
            }

            CSharpParseOptions updated = parseOptions.WithPreprocessorSymbols(combined);
            session.Project = session.Project.WithParseOptions(updated);
        }
    }

    public sealed class DocumentPreprocessingStage : IConversionStage {
        public void Execute(ConversionSession session) {
            foreach (Document document in session.Project.Documents) {
                Console.WriteLine($"-- Processing: {document.Name}");

                SyntaxTree? syntaxTree = AsyncUtil.RunSync(() => document.GetSyntaxTreeAsync());
                if (syntaxTree == null) {
                    continue;
                }

                SemanticModel? semanticModel = AsyncUtil.RunSync(() => document.GetSemanticModelAsync());
                if (semanticModel == null) {
                    continue;
                }

                CompilationUnitSyntax? root = AsyncUtil.RunSync(() => syntaxTree.GetRootAsync()) as CompilationUnitSyntax;
                if (root == null) {
                    continue;
                }

                foreach (MemberDeclarationSyntax member in root.Members) {
                    session.Converter.RunPreProcess(semanticModel, member, session.Context);
                }
            }
        }
    }

    public sealed class ClassProcessingStage : IConversionStage {
        public void Execute(ConversionSession session) {
            for (int i = 0; i < session.Program.Classes.Count; i++) {
                ConversionClass conversionClass = session.Program.Classes[i];
                if (conversionClass.IsNative) {
                    continue;
                }

                session.Converter.RunProcessClass(conversionClass, session.Program);
                session.Converter.SortMembers(conversionClass);
            }
        }
    }

    public sealed class ProgramSortingStage : IConversionStage {
        public void Execute(ConversionSession session) {
            session.Converter.SortProgramInternal();
        }
    }
}
