using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    public sealed class TypeScriptStringMemberTests {
        [Theory]
        [InlineData("class C { string Normalize(string value) => value.ToUpperInvariant(); }", "value.ToUpperInvariant()", "value.toUpperCase()")]
        [InlineData("class C { string Normalize(string value) => value?.Trim(); }", "value?.Trim()", "(() => { const __conditionalReceiver1 = value; return __conditionalReceiver1 == null ? undefined : __conditionalReceiver1.trim(); })()")]
        [InlineData("class C { string Normalize(string value) => value.TrimStart('/'); }", "value.TrimStart('/')", "NativeStringUtil.trimCharacters(value, \"start\", \"/\")")]
        [InlineData("class C { bool Matches(string value) => value.Equals(\"Descending\", System.StringComparison.OrdinalIgnoreCase); }", "value.Equals(\"Descending\", System.StringComparison.OrdinalIgnoreCase)", "String.Equals(value, \"Descending\", StringComparison.OrdinalIgnoreCase)")]
        [InlineData("class C { bool Matches(string left, string right) => string.Equals(left, right, System.StringComparison.Ordinal); }", "string.Equals(left, right, System.StringComparison.Ordinal)", "String.Equals(left, right, StringComparison.Ordinal)")]
        [InlineData("class Token { } class C { bool Matches(Token left, Token right) => object.ReferenceEquals(left, right); }", "object.ReferenceEquals(left, right)", "left === right")]
        [InlineData("class Token { } class C { bool Matches(Token left, Token right) => ReferenceEquals(left, right); }", "ReferenceEquals(left, right)", "left === right")]
        [InlineData("class Token { public static bool ReferenceEquals(Token left, Token right) => false; } class C { bool Matches(Token left, Token right) => Token.ReferenceEquals(left, right); }", "Token.ReferenceEquals(left, right)", "Token.ReferenceEquals(left, right)")]
        public void StringMembersUseJavaScriptEquivalent(string source, string expressionText, string expected) {
            var compilation = RoslynTestHelper.CreateCompilation(source);
            ExpressionSyntax expression = expressionText.Contains("?.")
                ? compilation.Root.DescendantNodes().OfType<ConditionalAccessExpressionSyntax>().Single()
                : compilation.Root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single(node => node.ToString() == expressionText);
            var harness = TsProcessorTestHarness.Create();
            var nativeRemap = typeof(TypeScriptProgram).GetMethod("buildNativeRemap", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(nativeRemap);
            nativeRemap.Invoke(harness.Program, null);
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: VariableUtil.GetVarType("string"));
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context, expression, output);

            Assert.Equal(expected, TsProcessorTestHarness.JoinLines(output).Trim());
        }
        [Fact]
        public void NegatedReferenceEqualsParenthesizesItsLoweredIdentityExpression() {
            var compilation = RoslynTestHelper.CreateCompilation("class Token { } class C { bool Matches(Token left, Token right) => !ReferenceEquals(left, right); }");
            var expression = compilation.Root.DescendantNodes().OfType<PrefixUnaryExpressionSyntax>().Single();
            var harness = TsProcessorTestHarness.Create();
            typeof(TypeScriptProgram).GetMethod("buildNativeRemap", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(harness.Program, null);
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: VariableUtil.GetVarType("bool"));
            var output = new List<string>();
            harness.Processor.ProcessExpression(compilation.Model, harness.Context, expression, output);
            Assert.Equal("!(left === right)", TsProcessorTestHarness.JoinLines(output).Trim());
        }
    }
}
