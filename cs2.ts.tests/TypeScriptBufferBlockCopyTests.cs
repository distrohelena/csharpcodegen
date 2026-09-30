using System.Linq;
using System.Reflection;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Checks that primitive byte-copy calls target the imported browser runtime rather than Node globals.</summary>
    public sealed class TypeScriptBufferBlockCopyTests {
        /// <summary>Uses the real native mapping and preserves all five byte-copy arguments.</summary>
        [Fact]
        public void BlockCopy_UsesNativeArrayRuntime() {
            var compilation = RoslynTestHelper.CreateCompilation("using System; class C { void M(byte[] source, byte[] target) { Buffer.BlockCopy(source, 1, target, 2, 3); } }");
            var invocation = compilation.Root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            var harness = TsProcessorTestHarness.Create();
            var nativeRemap = typeof(TypeScriptProgram).GetMethod("buildNativeRemap", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(nativeRemap);
            nativeRemap.Invoke(harness.Program, null);
            TsProcessorTestHarness.PushClassAndFunction(harness.Context);
            string output = TsProcessorTestHarness.JoinLines(TsProcessorTestHarness.RunProcessExpression(
                harness.Processor, harness.Context, compilation.Model, invocation));
            Assert.Equal("NativeArrayUtil.blockCopy(source, 1, target, 2, 3)", output.Trim());
        }
    }
}
