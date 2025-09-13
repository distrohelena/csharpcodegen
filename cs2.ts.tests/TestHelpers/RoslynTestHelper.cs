using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.ts.tests.TestHelpers {
    internal static class RoslynTestHelper {
        public static (Compilation Compilation, SemanticModel Model, CompilationUnitSyntax Root) CreateCompilation(string code) {
            var syntaxTree = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Latest));

            var references = new[] {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            };

            var compilation = CSharpCompilation.Create(
                assemblyName: "TestAssembly",
                syntaxTrees: new[] { syntaxTree },
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );

            var model = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetCompilationUnitRoot();
            return (compilation, model, root);
        }

        public static MethodDeclarationSyntax GetFirstMethod(CompilationUnitSyntax root) {
            return root.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        }

        public static MethodDeclarationSyntax GetMethodByName(CompilationUnitSyntax root, string name) {
            return root.DescendantNodes().OfType<MethodDeclarationSyntax>().First(m => m.Identifier.ValueText == name);
        }

        public static StatementSyntax GetSingleStatement(MethodDeclarationSyntax method) {
            if (method.Body != null && method.Body.Statements.Count == 1) {
                return method.Body.Statements[0];
            }
            throw new InvalidOperationException("Method must contain exactly one statement for this helper.");
        }

        public static ExpressionSyntax GetFirstExpression(MethodDeclarationSyntax method) {
            var expr = method.DescendantNodes().OfType<ExpressionSyntax>().FirstOrDefault();
            if (expr == null) throw new InvalidOperationException("No expression found in method body.");
            return expr;
        }
    }
}
