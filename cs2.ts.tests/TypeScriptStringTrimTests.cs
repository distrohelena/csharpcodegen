using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    public sealed class TypeScriptStringTrimTests {
        [Theory]
        [InlineData("class C { string Normalize(string value) => value.Trim('/'); }", "NativeStringUtil.trimCharacters(value, \"both\", \"/\")")]
        [InlineData("class C { string Normalize(string value) => value.TrimStart('/'); }", "NativeStringUtil.trimCharacters(value, \"start\", \"/\")")]
        [InlineData("class C { string Normalize(string value) => value.Trim('/', 'x'); }", "NativeStringUtil.trimCharacters(value, \"both\", \"/\", \"x\")")]
        [InlineData("class C { string Normalize(string value) => value.TrimEnd('/'); }", "NativeStringUtil.trimCharacters(value, \"end\", \"/\")")]
        public void CharacterTrimOverloads_UseNativeStringRuntime(string source, string expected) {
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var invocation = compilation.Root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            var harness = TsProcessorTestHarness.Create();
            typeof(TypeScriptProgram).GetMethod("buildNativeRemap", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(harness.Program, null);
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: VariableUtil.GetVarType("string"));
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context, invocation, output);

            Assert.Equal(expected, TsProcessorTestHarness.JoinLines(output).Trim());
        }

        [Fact]
        public void WhitespaceTrimWithoutCharacters_UsesNativeJavaScriptTrim() {
            var compilation = RoslynTestHelper.CreateCompilation("class C { string Normalize(string value) => value.Trim(); }");
            var invocation = compilation.Root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            var harness = TsProcessorTestHarness.Create();
            typeof(TypeScriptProgram).GetMethod("buildNativeRemap", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(harness.Program, null);
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: VariableUtil.GetVarType("string"));
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context, invocation, output);

            Assert.Equal("value.trim()", TsProcessorTestHarness.JoinLines(output).Trim());
        }
    }
}
