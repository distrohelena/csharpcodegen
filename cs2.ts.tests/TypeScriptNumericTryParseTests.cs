using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Verifies that numeric TryParse keeps its C# numeric category after native-number remapping.</summary>
    public sealed class TypeScriptNumericTryParseTests {
        [Fact]
        public void Int32TryParseUsesBoundedIntegerRuntime() {
            Assert.Equal(
                "NativeNumberUtil.tryParseInteger(value, out_1, -2147483648, 2147483647)",
                Emit("class C { bool M(string value, out int outValue) { return int.TryParse(value, out outValue); } }"));
        }

        [Fact]
        public void DoubleTryParseUsesFloatingPointRuntime() {
            Assert.Equal(
                "NativeNumberUtil.tryParseFloatingPoint(value, out_1)",
                Emit("class C { bool M(string value, out double outValue) { return double.TryParse(value, out outValue); } }"));
        }

        [Fact]
        public void IntegerStyleAndProviderArgumentsRemainInTheEmission() {
            Assert.Equal(
                "NativeNumberUtil.tryParseIntegerWithFormat(value, NumberStyles.None, CultureInfo.InvariantCulture, out_1, -2147483648, 2147483647)",
                Emit("using System.Globalization; class C { bool M(string value, out int outValue) { return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out outValue); } }"));
        }

        [Fact]
        public void OutArgumentUsesHolderAndCopiesTheParsedValueBack() {
            var emission = EmitWithFlow("class C { bool M(string value, out int outValue) { return int.TryParse(value, out outValue); } }");
            Assert.Equal("NativeNumberUtil.tryParseInteger(value, out_1, -2147483648, 2147483647)", emission.Output);
            Assert.Equal("let out_1 = { value: undefined };", emission.Before);
            Assert.Equal("outValue = out_1.value;", emission.After);
        }

        [Fact]
        public void CustomTryParseIsNotMistakenForFrameworkNumericParsing() {
            Assert.DoesNotContain("NativeNumberUtil", Emit("class Value { public static bool TryParse(string value, out Value result) { result = null; return false; } } class C { bool M(string value, out Value outValue) { return Value.TryParse(value, out outValue); } }"));
        }

        static string Emit(string source) {
            return EmitWithFlow(source).Output;
        }

        static (string Output, string Before, string After) EmitWithFlow(string source) {
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var invocation = compilation.Root.DescendantNodes().OfType<InvocationExpressionSyntax>().First();
            var harness = TsProcessorTestHarness.Create();
            var nativeRemap = typeof(TypeScriptProgram).GetMethod("buildNativeRemap", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(nativeRemap);
            nativeRemap.Invoke(harness.Program, null);
            TsProcessorTestHarness.PushClassAndFunction(harness.Context);
            List<string> lines = new List<string>();
            var result = harness.Processor.ProcessExpression(compilation.Model, harness.Context, invocation, lines);
            return (
                TsProcessorTestHarness.JoinLines(lines).Trim(),
                TsProcessorTestHarness.JoinLines(result.BeforeLines ?? new List<string>()).Trim(),
                TsProcessorTestHarness.JoinLines(result.AfterLines ?? new List<string>()).Trim());
        }
    }
}
