using System;
using System.Collections.Generic;
using System.Linq;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    public sealed class TypeScriptCharArithmeticTests {
        [Fact]
        public void CharacterSubtraction_UsesUtf16CodeUnits() {
            AssertEmitted("class C { int Parse(char digit) => digit - '0'; }", "(digit).charCodeAt(0) - (\"0\").charCodeAt(0)");
        }

        [Fact]
        public void CharacterComparison_PreservesStringComparison() {
            AssertEmitted("class C { bool Before(char left, char right) => left < right; }", "left < right");
        }

        [Fact]
        public void CharacterConcatenation_PreservesStringOperand() {
            AssertEmitted("class C { string Format(char value) => \"item:\" + value; }", "\"item:\" + value");
        }

        static void AssertEmitted(string source, string expected) {
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var expression = compilation.Root.DescendantNodes().OfType<BinaryExpressionSyntax>().Single();
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: VariableUtil.GetVarType("int"));
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context, expression, output);

            Assert.Equal(expected, TsProcessorTestHarness.JoinLines(output).Trim());
        }
    }
}
