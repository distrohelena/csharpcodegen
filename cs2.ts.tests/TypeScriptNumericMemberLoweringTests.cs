using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    public sealed class TypeScriptNumericMemberLoweringTests {
        [Theory]
        [InlineData("double.Epsilon", "Number.MIN_VALUE")]
        [InlineData("double.MinValue", "-Number.MAX_VALUE")]
        [InlineData("long.MinValue", "-9223372036854775808")]
        [InlineData("int.MinValue", "-2147483648")]
        public void PrimitiveConstantsUseJavaScriptValuesWithClrMeaning(string expression, string expected) {
            string output = EmitExpression($"class C {{ double M() => {expression}; }}");
            Assert.Equal(expected, output);
        }

        [Fact]
        public void MathAbsUsesJavaScriptCasing() {
            const string source = "class C { double M(double value) => Math.Abs(value); }";
            string output = EmitExpression(source);
            Assert.Equal("Math.abs(value)", output);
        }

        [Fact]
        public void IntParseUsesCheckedRuntimeParser() {
            string output = EmitExpression("class C { int M(string value) => int.Parse(value); }");
            Assert.Contains("NativeNumberUtil.parseInteger(value, -2147483648, 2147483647)", output);
        }

        static string EmitExpression(string source) {
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var expression = compilation.Root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single().ExpressionBody!.Expression;
            var harness = TsProcessorTestHarness.Create();
            typeof(TypeScriptProgram).GetMethod("buildNativeRemap", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(harness.Program, null);
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: VariableUtil.GetVarType("double"));
            var lines = new List<string>();
            harness.Processor.ProcessExpression(compilation.Model, harness.Context, expression, lines);
            return TsProcessorTestHarness.JoinLines(lines).Trim();
        }
    }
}
