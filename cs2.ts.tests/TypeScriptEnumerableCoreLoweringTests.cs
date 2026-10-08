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
    public sealed class TypeScriptEnumerableCoreLoweringTests {
        [Theory]
        [InlineData("using System.Collections.Generic; using System.Linq; class C { string[] M(IReadOnlyList<string> values) => values.Skip(1).ToArray(); }", "NativeArrayUtil.toArray(NativeArrayUtil.skip(values, 1))")]
        [InlineData("using System.Collections.Generic; using System.Linq; class C { bool M(IReadOnlyList<string> values) => values.All(value => value.Length > 0); }", "NativeArrayUtil.all(values,")]
        [InlineData("using System.Collections.Generic; using System.Linq; class C { long M(IReadOnlyList<long> values) => values.Sum(value => value); }", "NativeArrayUtil.sum(values,")]
        [InlineData("using System; using System.Collections.Generic; using System.Linq; class C { string[] M(IReadOnlyList<string> values) => values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(); }", "NativeArrayUtil.toArray(NativeArrayUtil.orderBy(values,")]
        [InlineData("using System.Collections.Generic; using System.Linq; class C { string[] M(IReadOnlyList<string> values) => values.OrderBy(value => value).ToArray(); }", "NativeArrayUtil.toArray(NativeArrayUtil.orderBy(values,")]
        [InlineData("using System.Collections.Generic; using System.Linq; class C { IEnumerable<string> M(IReadOnlyList<string> values) => values.Distinct(); }", "NativeArrayUtil.distinct(values)")]
        [InlineData("using System; using System.Collections.Generic; using System.Linq; class C { IEnumerable<string> M(IReadOnlyList<string> values) => values.Distinct(StringComparer.OrdinalIgnoreCase); }", "NativeArrayUtil.distinct(values, StringComparer.OrdinalIgnoreCase)")]
        [InlineData("using System.Collections.Generic; using System.Linq; class C { List<string> M(IReadOnlyList<string> values) => values.ToList(); }", "NativeArrayUtil.toList(values)")]
        [InlineData("using System.Collections.Generic; using System.Linq; class C { bool M(IReadOnlyList<string> values) => values.Contains(\"x\"); }", "NativeArrayUtil.contains(values, \"x\")")]
        [InlineData("using System.Collections.Generic; using System.Linq; class C { IEnumerable<string> M(IReadOnlyList<string> values) => values.Take(2); }", "NativeArrayUtil.take(values, 2)")]
        [InlineData("using System.Collections.Generic; using System.Linq; class C { IEnumerable<int> M(IReadOnlyList<string> values) => values.Select(value => value.Length); }", "NativeArrayUtil.select(values,")]
        [InlineData("using System.Collections.Generic; using System.Linq; class C { int M(IReadOnlyList<string> values) => values.Count(value => value.Length > 0); }", "NativeArrayUtil.count(values,")]
        public void ReducedEnumerableOperatorsUseIterableRuntime(string source, string expected) {
            string output = Emit(source);
            Assert.Contains(expected, output);
        }

        [Fact]
        public void UserDefinedAllIsNotRewritten() {
            string output = Emit("class Values { public bool All(System.Func<string, bool> predicate) => true; } class C { bool M(Values values) => values.All(value => true); }");
            Assert.DoesNotContain("NativeArrayUtil.all", output);
        }

        [Fact]
        public void ConditionalToListUsesIterableRuntimeWithoutCallingAnInterfaceMember() {
            const string source = "using System.Collections.Generic; using System.Linq; class C { List<string> M(IReadOnlyList<string> values) => values?.ToList(); }";
            string output = Emit(source);
            Assert.Contains("NativeArrayUtil.toList", output);
            Assert.DoesNotContain("?.ToList", output);
        }

        [Fact]
        public void ConditionalEnumerableChainKeepsReceiverInsideRuntimeHelpers() {
            const string source = "using System; using System.Collections.Generic; using System.Linq; class C { List<string> M(IReadOnlyList<string> values) => values?.Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); }";
            string output = Emit(source);
            Assert.True(output.Contains("NativeArrayUtil.toList(NativeArrayUtil.distinct(NativeArrayUtil.where(__conditionalReceiver"), output);
            Assert.DoesNotContain(".NativeArrayUtil", output);
            Assert.DoesNotContain("distinct(Where", output);
        }

        static string Emit(string source) {
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
            string trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("Trusted platform assembly paths are unavailable.");
            string runtimeAssembly = trusted.Split(System.IO.Path.PathSeparator).Single(path => string.Equals(System.IO.Path.GetFileName(path), "System.Runtime.dll", StringComparison.OrdinalIgnoreCase));
            string collectionsAssembly = trusted.Split(System.IO.Path.PathSeparator).Single(path => string.Equals(System.IO.Path.GetFileName(path), "System.Collections.dll", StringComparison.OrdinalIgnoreCase));
            var compilation = CSharpCompilation.Create("EnumerableCoreFixture", new[] { tree }, new[] {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(runtimeAssembly),
                MetadataReference.CreateFromFile(collectionsAssembly)
            }, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var expression = tree.GetCompilationUnitRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.ValueText == "M").ExpressionBody!.Expression;
            var harness = TsProcessorTestHarness.Create();
            typeof(TypeScriptProgram).GetMethod("buildNativeRemap", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(harness.Program, null);
            TsProcessorTestHarness.PushClassAndFunction(harness.Context);
            List<string> lines = new List<string>();
            harness.Processor.ProcessExpression(compilation.GetSemanticModel(tree), harness.Context, expression, lines);
            return TsProcessorTestHarness.JoinLines(lines).Trim();
        }
    }
}
