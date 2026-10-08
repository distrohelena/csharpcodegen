using System.Collections.Generic;
using System.Linq;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    public sealed class TypeScriptNamedArgumentLoweringTests {
        [Fact]
        public void NamedArgumentAfterOmittedOptionalsUsesItsDeclaredParameterPosition() {
            const string source = "class C { static void Target(int a, bool b = false, bool c = false, byte[] d = null) { } static void M(byte[] key) { Target(1, d: key); } }";
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var invocation = compilation.Root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Single(candidate => candidate.Expression.ToString() == "Target");
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context);
            var lines = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context, invocation, lines);

            Assert.Equal("C.Target(1, false, false, key)", TsProcessorTestHarness.JoinLines(lines));
        }
    }
}
