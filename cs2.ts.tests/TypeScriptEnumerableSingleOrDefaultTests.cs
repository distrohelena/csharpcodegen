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
    public sealed class TypeScriptEnumerableSingleOrDefaultTests {
        [Fact]
        public void EnumerableSingleOrDefaultOnArrayWithPredicateUsesNativeArrayRuntime() {
            const string source = "using System.Linq; class C { string Find(string[] assignments, string key) => assignments.SingleOrDefault(assignment => assignment == key); }";
            string output = Emit(source);
            Assert.Contains("NativeArrayUtil.singleOrDefault<string>(assignments,", output);
            Assert.DoesNotContain("assignments.SingleOrDefault", output);
        }

        [Fact]
        public void EnumerableSingleOnReadOnlyListUsesNativeArrayRuntime() {
            const string source = "using System.Collections.Generic; using System.Linq; class C { string Find(IReadOnlyList<string> values) => values.Single(); }";
            string output = Emit(source);
            Assert.Equal("NativeArrayUtil.single(values)", output);
        }

        [Fact]
        public void EnumerableSequenceEqualOnReadOnlyListsWithComparerUsesNativeArrayRuntime() {
            const string source = "using System; using System.Collections.Generic; using System.Linq; class C { bool Same(IReadOnlyList<string> left, IReadOnlyList<string> right) => left.SequenceEqual(right, StringComparer.Ordinal); }";
            string output = Emit(source);
            Assert.Contains("NativeArrayUtil.sequenceEqual(left, right, StringComparer.Ordinal)", output);
            Assert.DoesNotContain("left.SequenceEqual", output);
        }
        static string Emit(string source) {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
            string trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
                ?? throw new InvalidOperationException("Trusted platform assembly paths are unavailable.");
            string runtimeAssembly = trustedPlatformAssemblies
                .Split(System.IO.Path.PathSeparator)
                .Single(candidate => string.Equals(System.IO.Path.GetFileName(candidate), "System.Runtime.dll", StringComparison.OrdinalIgnoreCase));
            var compilation = CSharpCompilation.Create(
                assemblyName: "SingleOrDefaultFixture",
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
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: cs2.core.VariableUtil.GetVarType("string"));
            List<string> lines = new List<string>();
            harness.Processor.ProcessExpression(model, harness.Context, invocation, lines);
            return TsProcessorTestHarness.JoinLines(lines).Trim();
        }
    }
}
