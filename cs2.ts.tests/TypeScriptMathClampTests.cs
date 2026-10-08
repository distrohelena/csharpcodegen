using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Verifies that framework Math.Clamp calls retain their runtime dependency after conversion.</summary>
    public sealed class TypeScriptMathClampTests {
        /// <summary>Maps the selected framework overload to the numeric helper instead of the nonexistent browser Math member.</summary>
        [Fact]
        public void FrameworkClampUsesNativeNumberRuntime() {
            Assert.Equal("NativeNumberUtil.clamp(value, minimum, maximum)", Emit("using System; class C { int M(int value, int minimum, int maximum) { return Math.Clamp(value, minimum, maximum); } }"));
        }

        /// <summary>Does not rewrite an unrelated user-defined Clamp method with the same name.</summary>
        [Fact]
        public void UserDefinedClampRemainsAnInstanceMethod() {
            Assert.DoesNotContain("NativeNumberUtil", Emit("class Value { public int Clamp(int minimum, int maximum) => minimum; } class C { int M(Value value) { return value.Clamp(1, 2); } }"));
        }

        /// <summary>Named arguments require reordering without changing evaluation order, so unsupported emission must fail visibly.</summary>
        [Fact]
        public void NamedArgumentsAreRejectedRatherThanReordered() {
            Assert.Throws<NotSupportedException>(() => Emit("using System; class C { int M(int value, int minimum, int maximum) { return Math.Clamp(max: maximum, value: value, min: minimum); } }"));
        }

        /// <summary>Builds the native runtime catalog and emits the selected invocation from Roslyn semantic information.</summary>
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
