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
    public sealed class TypeScriptStringSelectTests {
        [Fact]
        public void EnumerableSelectOnStringWithSelectorUsesNativeStringRuntime() {
            const string source = "using System.Linq; class C { char[] Sanitize(string value, char[] invalid) => value.Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray(); }";
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
            string runtimeAssembly = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
                .Split(System.IO.Path.PathSeparator).Single(path => System.IO.Path.GetFileName(path) == "System.Runtime.dll");
            var compilation = CSharpCompilation.Create("StringSelectFixture", new[] { tree }, new[] {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(runtimeAssembly)
            }, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var invocation = tree.GetCompilationUnitRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Single(node => node.Expression is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == "Select");
            var method = compilation.GetSemanticModel(tree).GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            Assert.NotNull(method);
            Assert.Equal("Select", method.Name);
            Assert.Equal("System.Linq.Enumerable", method.ContainingType.ToDisplayString());
            Assert.NotNull(method.ReducedFrom);
            var harness = TsProcessorTestHarness.Create();
            var nativeRemap = typeof(TypeScriptProgram).GetMethod("buildNativeRemap", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(nativeRemap);
            nativeRemap.Invoke(harness.Program, null);
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: cs2.core.VariableUtil.GetVarType("char[]"));
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.GetSemanticModel(tree), harness.Context, invocation, output);

            string emitted = TsProcessorTestHarness.JoinLines(output).Trim();
            Assert.Contains("NativeStringUtil.select(value.trim(),", emitted);
            Assert.DoesNotContain(".Select(", emitted);
        }
    }
}