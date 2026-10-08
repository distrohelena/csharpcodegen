using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    public sealed class TypeScriptStringReplaceTests {
        [Theory]
        [InlineData("class C { string Normalize(string value) => value.Replace('a', 'b'); }", "NativeStringUtil.replace(value, \"a\", \"b\")")]
        [InlineData("class C { string Normalize(string value) => value.Replace(\"aa\", \"b\"); }", "NativeStringUtil.replace(value, \"aa\", \"b\")")]
        public void FrameworkStringReplace_UsesLiteralAllOccurrencesRuntime(string source, string expected) {
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var invocation = compilation.Root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            var harness = TsProcessorTestHarness.Create();
            typeof(TypeScriptProgram).GetMethod("buildNativeRemap", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(harness.Program, null);
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: VariableUtil.GetVarType("string"));
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context, invocation, output);

            Assert.Equal(expected, TsProcessorTestHarness.JoinLines(output).Trim());
        }
    }
}
