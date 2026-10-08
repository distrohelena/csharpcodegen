using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    public sealed class TypeScriptStringJoinTests {
        [Fact]
        public void StringJoinUsesNativeStringRuntime() {
            const string source = "using System; using System.Collections.Generic; class C { string Join(IEnumerable<string> values) => string.Join(\",\", values); }";
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var invocation = compilation.Root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "string.Join(\",\", values)");
            var harness = TsProcessorTestHarness.Create();
            var nativeRemap = typeof(TypeScriptProgram).GetMethod("buildNativeRemap", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(nativeRemap);
            nativeRemap.Invoke(harness.Program, null);
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: cs2.core.VariableUtil.GetVarType("string"));
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context, invocation, output);

            Assert.Equal("NativeStringUtil.join(\",\", values)", TsProcessorTestHarness.JoinLines(output).Trim());
        }
    }
}