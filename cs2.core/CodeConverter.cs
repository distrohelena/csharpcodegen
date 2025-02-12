using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Nucleus;

namespace cs2.core {
    public abstract class CodeConverter {
        protected Project project;
        protected ConversionContext context;
        protected ConvertedProgram program;
        protected ConversionRules rules;

        protected MSBuildWorkspace workspace;

        protected abstract string[] PreProcessorSymbols { get; }

        public CodeConverter(ConversionRules rules) {
            this.rules = rules;
        }

        public void AddCsproj(string csprojPath) {
            if (!File.Exists(csprojPath)) {
                Console.WriteLine($"The .csproj file '{csprojPath}' does not exist.");
                return;
            }

            project = AsyncUtil.RunSync(() => workspace.OpenProjectAsync(csprojPath));

            // Create custom parse options with preprocessor symbols
            var customParseOptions = ((CSharpParseOptions)project.ParseOptions)
                .WithPreprocessorSymbols(PreProcessorSymbols);

            // Update the project with new parse options
            project = project.WithParseOptions(customParseOptions);

            foreach (var document in project.Documents) {
                Console.WriteLine($"-- Processing: {document.Name}");

                // Parse the syntax tree
                SyntaxTree syntaxTree = AsyncUtil.RunSync(() => document.GetSyntaxTreeAsync());
                if (syntaxTree == null) continue;

                // Access the semantic model
                var semanticModel = AsyncUtil.RunSync(() => document.GetSemanticModelAsync());
                if (semanticModel == null) continue;

                // Example: Find all classes in the syntax tree
                CompilationUnitSyntax root = (CompilationUnitSyntax)AsyncUtil.RunSync(() => syntaxTree.GetRootAsync());

                foreach (MemberDeclarationSyntax member in root.Members) {
                    PreProcessExpression(semanticModel, member, context);
                }
            }

            for (int i = 0; i < program.Classes.Count; i++) {
                ConvertedClass cl = program.Classes[i];
                if (cl.IsNative) {
                    continue;
                }

                ProcessClass(cl, program);
            }
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

        protected virtual void SortVariables(ConvertedClass cl) {
            cl.Variables.Sort((a, b) => {
                int staticComparison = b.IsStatic.CompareTo(a.IsStatic);
                if (staticComparison != 0) {
                    return staticComparison;
                }

                int accessComparison = a.AccessType.CompareTo(b.AccessType);
                if (accessComparison != 0) {
                    return accessComparison;
                }

                return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });
        }

        protected virtual void SortFunctions(ConvertedClass cl) {
            cl.Functions.Sort((a, b) => {
                int accessComparison = a.AccessType.CompareTo(b.AccessType);
                if (accessComparison != 0) {
                    return accessComparison;
                }

                return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });
        }


        protected abstract void PreProcessExpression(SemanticModel model, MemberDeclarationSyntax member, ConversionContext context);

        protected abstract void ProcessClass(ConvertedClass cl, ConvertedProgram program);
    }
}
