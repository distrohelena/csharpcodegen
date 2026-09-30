using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Verifies semantic selection of primitive numeric comparisons without rewriting custom or boxed overloads.</summary>
    public sealed class TypeScriptNumericCompareToTests {
        /// <summary>All supported typed numeric overloads import and invoke the number runtime.</summary>
        [Theory]
        [InlineData("sbyte")]
        [InlineData("byte")]
        [InlineData("short")]
        [InlineData("ushort")]
        [InlineData("int")]
        [InlineData("uint")]
        [InlineData("long")]
        [InlineData("ulong")]
        [InlineData("float")]
        [InlineData("double")]
        [InlineData("decimal")]
        public void TypedNumericOverloadUsesRuntime(string type) {
            Assert.Equal("NativeNumberUtil.compareTo(left, right)", Emit($"class C {{ int M({type} left, {type} right) {{ return left.CompareTo(right); }} }}"));
        }

        /// <summary>Receiver and argument calls appear exactly once and remain in their source evaluation order.</summary>
        [Fact]
        public void SideEffectsRemainOrdered() {
            string output = Emit("class C { double Left() => 0; double Right() => 0; int M() { return Left().CompareTo(Right()); } }");
            Assert.Equal("NativeNumberUtil.compareTo(Left(), Right())", output);
        }

        /// <summary>A user-defined comparison method is not mistaken for a numeric primitive.</summary>
        [Fact]
        public void CustomComparisonRetainsMethod() {
            Assert.DoesNotContain("NativeNumberUtil", Emit("class Value { public int CompareTo(Value value) => 0; } class C { int M(Value left, Value right) { return left.CompareTo(right); } }"));
        }

        /// <summary>The boxed overload must retain its own type checking instead of being coerced to a number comparison.</summary>
        [Fact]
        public void ObjectOverloadIsNotRewritten() {
            Assert.DoesNotContain("NativeNumberUtil", Emit("class C { int M(int left, object right) { return left.CompareTo(right); } }"));
        }

        /// <summary>Builds the real native runtime catalog and emits the comparison invocation from a Roslyn semantic model.</summary>
        static string Emit(string source) {
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var invocation = compilation.Root.DescendantNodes().OfType<InvocationExpressionSyntax>().First();
            var harness = TsProcessorTestHarness.Create();
            var nativeRemap = typeof(TypeScriptProgram).GetMethod("buildNativeRemap", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(nativeRemap);
            nativeRemap.Invoke(harness.Program, null);
            TsProcessorTestHarness.PushClassAndFunction(harness.Context);
            List<string> lines = new List<string>();
            harness.Processor.ProcessExpression(compilation.Model, harness.Context, invocation, lines);
            return TsProcessorTestHarness.JoinLines(lines).Trim();
        }
    }
}
