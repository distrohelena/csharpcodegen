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
    public sealed class TypeScriptEnumerableGroupingTests {
        [Fact]
        public void GroupByThenToDictionary_UsesComparerAwareRuntimeHelpers() {
            const string source = "using System; using System.Collections.Generic; using System.Linq; class C { Dictionary<int, IGrouping<int, int>> Group(int[] items) => items.GroupBy(item => item % 2).ToDictionary(group => group.Key); }";
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
            string trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("Trusted platform assembly paths are unavailable.");
            string runtimeAssembly = trusted.Split(System.IO.Path.PathSeparator).Single(path => string.Equals(System.IO.Path.GetFileName(path), "System.Runtime.dll", StringComparison.OrdinalIgnoreCase));
            string collectionsAssembly = trusted.Split(System.IO.Path.PathSeparator).Single(path => string.Equals(System.IO.Path.GetFileName(path), "System.Collections.dll", StringComparison.OrdinalIgnoreCase));
            var compilation = CSharpCompilation.Create("EnumerableGroupingFixture", new[] { tree }, new[] {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(runtimeAssembly),
                MetadataReference.CreateFromFile(collectionsAssembly)
            }, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var invocation = tree.GetCompilationUnitRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single(candidate => candidate.Parent is ArrowExpressionClauseSyntax);
            var harness = TsProcessorTestHarness.Create();
            typeof(TypeScriptProgram).GetMethod("buildNativeRemap", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(harness.Program, null);
            TsProcessorTestHarness.PushClassAndFunction(harness.Context);
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.GetSemanticModel(tree), harness.Context, invocation, output);

            string emitted = TsProcessorTestHarness.JoinLines(output);
            Assert.Contains("NativeArrayUtil.toDictionary(", emitted);
            Assert.Contains("NativeArrayUtil.groupBy(", emitted);
            Assert.DoesNotContain(".GroupBy(", emitted);
            Assert.DoesNotContain(".ToDictionary(", emitted);
        }
        [Fact]
        public void ListFluentGroupByThenToDictionary_UsesRuntimeHelpersForEveryReducedCall() {
            const string source = "using System; using System.Collections.Generic; using System.Linq; class C { Dictionary<int, List<int>> Group(IEnumerable<int> items) { List<int> compatible = items.Where(item => item > 0).OrderBy(item => item).ToList(); return compatible.GroupBy(item => item % 2, EqualityComparer<int>.Default).ToDictionary(group => group.Key, group => group.ToList(), EqualityComparer<int>.Default); } }";
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
            string trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("Trusted platform assembly paths are unavailable.");
            string runtimeAssembly = trusted.Split(System.IO.Path.PathSeparator).Single(path => string.Equals(System.IO.Path.GetFileName(path), "System.Runtime.dll", StringComparison.OrdinalIgnoreCase));
            string collectionsAssembly = trusted.Split(System.IO.Path.PathSeparator).Single(path => string.Equals(System.IO.Path.GetFileName(path), "System.Collections.dll", StringComparison.OrdinalIgnoreCase));
            var compilation = CSharpCompilation.Create("EnumerableGroupingFluentFixture", new[] { tree }, new[] {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(runtimeAssembly),
                MetadataReference.CreateFromFile(collectionsAssembly)
            }, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var invocation = tree.GetCompilationUnitRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single(candidate => candidate.Expression is MemberAccessExpressionSyntax access && access.Name.Identifier.Text == "GroupBy");
            var harness = TsProcessorTestHarness.Create();
            typeof(TypeScriptProgram).GetMethod("buildNativeRemap", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(harness.Program, null);
            TsProcessorTestHarness.PushClassAndFunction(harness.Context);
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.GetSemanticModel(tree), harness.Context, invocation, output);

            string emitted = TsProcessorTestHarness.JoinLines(output);
            Assert.Contains("NativeArrayUtil.groupBy(", emitted);
            Assert.DoesNotContain(".GroupBy(", emitted);
        }
    }
}
