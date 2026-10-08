using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    public sealed class TypeScriptStringSplitTests {
        [Fact]
        public void StringSplitWithOptionsUsesNativeStringRuntime() {
            Assert.Contains(TypeScriptRuntimeRequirementCatalog.BaseRequirements, entry => entry.Name == "StringSplitOptions" && entry.Path == "./system/string-split-options");
            const string source = "using System; class C { string[] Parts(string value) => value.Split('/', StringSplitOptions.RemoveEmptyEntries); }";
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var invocation = compilation.Root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            var harness = TsProcessorTestHarness.Create();
            var nativeRemap = typeof(TypeScriptProgram).GetMethod("buildNativeRemap", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(nativeRemap);
            nativeRemap.Invoke(harness.Program, null);
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: VariableUtil.GetVarType("string[]"));
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context, invocation, output);

            Assert.Equal("NativeStringUtil.split(value, \"/\", StringSplitOptions.RemoveEmptyEntries)", TsProcessorTestHarness.JoinLines(output).Trim());
        }
        [Fact]
        public void StringSplitOptionsRuntimeIsPackagedWithConverterAssembly() {
            string assemblyDirectory = Path.GetDirectoryName(typeof(TypeScriptProgram).Assembly.Location)!;
            string runtimeFile = Path.Combine(assemblyDirectory, ".net.ts", "system", "string-split-options.ts");

            Assert.True(File.Exists(runtimeFile), $"Expected packaged runtime file at '{runtimeFile}'.");
        }
        [Fact]
        public void EveryCatalogRuntimeRequirementIsPackagedWithConverterAssembly() {
            string assemblyDirectory = Path.GetDirectoryName(typeof(TypeScriptProgram).Assembly.Location)!;
            string runtimeDirectory = Path.Combine(assemblyDirectory, ".net.ts");
            var missing = TypeScriptRuntimeRequirementCatalog.BaseRequirements
                .Select(requirement => Path.Combine(runtimeDirectory, requirement.Path.Replace("./", "").Replace('/', Path.DirectorySeparatorChar) + ".ts"))
                .Where(path => !File.Exists(path))
                .ToArray();

            Assert.True(missing.Length == 0, $"Missing packaged runtime requirements: {string.Join(", ", missing)}");
        }
    }
}