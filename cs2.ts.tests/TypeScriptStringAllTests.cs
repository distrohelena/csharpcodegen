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
    public sealed class TypeScriptStringAllTests {
        [Fact]
        public void EnumerableAllOnStringWithPredicateUsesNativeStringRuntime() {
            const string source = "using System.Linq; class C { bool Validate(string value) => value.All(character => character != '-'); }";
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
            string runtimeAssembly = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
                .Split(System.IO.Path.PathSeparator).Single(path => System.IO.Path.GetFileName(path) == "System.Runtime.dll");
            var compilation = CSharpCompilation.Create("StringAllFixture", new[] { tree }, new[] {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(runtimeAssembly)
            }, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var invocation = tree.GetCompilationUnitRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            var harness = TsProcessorTestHarness.Create();
            var nativeRemap = typeof(TypeScriptProgram).GetMethod("buildNativeRemap", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(nativeRemap);
            nativeRemap.Invoke(harness.Program, null);
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: cs2.core.VariableUtil.GetVarType("bool"));
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.GetSemanticModel(tree), harness.Context, invocation, output);

            string emitted = TsProcessorTestHarness.JoinLines(output).Trim();
            Assert.Contains("NativeStringUtil.all(value,", emitted);
            Assert.DoesNotContain("value.All", emitted);
        }
    }
}