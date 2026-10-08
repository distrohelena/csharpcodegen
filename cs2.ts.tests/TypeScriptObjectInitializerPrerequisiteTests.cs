using System.Collections.Generic;
using System.Linq;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    public sealed class TypeScriptObjectInitializerPrerequisiteTests {
        [Fact]
        public void ShortCircuitOutExpressionKeepsPrerequisitesInsidePropertyValue() {
            const string source = "class R { public bool Required { get; set; } } class C { bool Try(out int value) { value = 1; return true; } R M() => new R { Required = Try(out int value) && value > 0 }; }";
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var creation = compilation.Root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single();
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context);
            var lines = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context, creation, lines);

            string output = TsProcessorTestHarness.JoinLines(lines);
            Assert.Contains("Required : (() => {", output);
            Assert.Contains("let out_", output);
            Assert.Contains("return __initializerValue", output);
        }
    }
}
