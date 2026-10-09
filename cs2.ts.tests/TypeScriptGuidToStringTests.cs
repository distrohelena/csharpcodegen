using System.Collections.Generic;
using System.Linq;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>
    /// Pins that every <c>Guid.ToString</c> call binds to the runtime shim's <c>toString</c>, whatever the receiver
    /// is. A pattern variable (<c>value is Guid identifier</c>) is the case the converter's own variable tracking
    /// never sees, and the one that used to fall through to the raw <c>ToString</c> identifier.
    /// </summary>
    public sealed class TypeScriptGuidToStringTests {
        [Theory]
        [InlineData("using System; class C { string M(object value) => value is Guid identifier ? identifier.ToString(\"D\") : null; }", "identifier.toString(\"D\")")]
        [InlineData("using System; class C { string M() => Guid.NewGuid().ToString(\"N\"); }", ".toString(\"N\")")]
        [InlineData("using System; class C { string M(Guid value) => value.ToString(); }", "value.toString()")]
        [InlineData("using System; class C { string M(Guid value) => value.ToString(\"B\"); }", "value.toString(\"B\")")]
        public void GuidToStringAlwaysBindsToTheShimToString(string source, string expected) {
            string output = Emit(source);

            Assert.Contains(expected, output);
            Assert.DoesNotContain(".ToString(", output);
        }

        static string Emit(string source) {
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var expression = compilation.Root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single().ExpressionBody!.Expression;
            var harness = TsProcessorTestHarness.Create();
            var owner = new ConversionClass { Name = "C" };
            harness.Program.Classes.Add(owner);
            harness.Context.AddClass(owner);
            harness.Context.AddFunction(new FunctionStack(new ConversionFunction { Name = "M", ReturnType = VariableUtil.GetVarType("string") }));
            var lines = new List<string>();
            harness.Processor.ProcessExpression(compilation.Model, harness.Context, expression, lines);
            return TsProcessorTestHarness.JoinLines(lines);
        }
    }
}
