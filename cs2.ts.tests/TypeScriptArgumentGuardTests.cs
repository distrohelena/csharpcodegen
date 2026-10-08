using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Verifies that framework argument guards import a cycle-free runtime implementation.</summary>
    public sealed class TypeScriptArgumentGuardTests {
        /// <summary>Maps the framework whitespace guard to a runtime that imports both exception classes itself.</summary>
        [Fact]
        public void FrameworkWhitespaceGuardUsesDedicatedRuntime() {
            Assert.Equal("ArgumentGuard.throwIfNullOrWhiteSpace(value, null)", Emit("using System; class C { void M(string value) { ArgumentException.ThrowIfNullOrWhiteSpace(value, null); } }"));
        }

        /// <summary>Does not confuse a user method that happens to have the framework guard's name.</summary>
        [Fact]
        public void UserDefinedWhitespaceGuardRemainsUnchanged() {
            Assert.DoesNotContain("ArgumentGuard", Emit("class Guard { public void ThrowIfNullOrWhiteSpace(string value) { } } class C { void M(Guard guard, string value) { guard.ThrowIfNullOrWhiteSpace(value); } }"));
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
