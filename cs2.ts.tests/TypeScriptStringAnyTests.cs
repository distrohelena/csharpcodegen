using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    public sealed class TypeScriptStringAnyTests {
        [Fact]
        public void EnumerableAnyOnStringWithPredicateUsesNativeStringRuntime() {
            const string source = "using System.Linq; class C { bool Validate(string recordId) => recordId.Any(character => character == '-'); }";
            string output = Emit(source);
            Assert.Contains("NativeStringUtil.any(recordId,", output);
            Assert.DoesNotContain("recordId.Any", output);
        }

        static string Emit(string source) {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
            string trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
                ?? throw new InvalidOperationException("Trusted platform assembly paths are unavailable.");
            string runtimeAssembly = trustedPlatformAssemblies
                .Split(System.IO.Path.PathSeparator)
                .Single(candidate => string.Equals(System.IO.Path.GetFileName(candidate), "System.Runtime.dll", StringComparison.OrdinalIgnoreCase));
            var compilation = CSharpCompilation.Create(
                assemblyName: "StringAnyFixture",
                syntaxTrees: new[] { syntaxTree },
                references: new[] {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                    MetadataReference.CreateFromFile(runtimeAssembly)
                },
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var model = compilation.GetSemanticModel(syntaxTree);
            var invocation = syntaxTree.GetCompilationUnitRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            var harness = TsProcessorTestHarness.Create();
            var nativeRemap = typeof(TypeScriptProgram).GetMethod("buildNativeRemap", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(nativeRemap);
            nativeRemap.Invoke(harness.Program, null);
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: cs2.core.VariableUtil.GetVarType("bool"));
            List<string> lines = new List<string>();
            harness.Processor.ProcessExpression(model, harness.Context, invocation, lines);
            return TsProcessorTestHarness.JoinLines(lines).Trim();
        }
    }
}
