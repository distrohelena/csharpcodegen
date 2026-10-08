using System.Collections.Generic;
using System.Linq;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    public sealed class TypeScriptFrameworkInvocationLoweringTests {
        [Fact]
        public void BinaryWriterNullableLengthUsesSelectedInt32Overload() {
            const string source = "using System.IO; class C { byte[] Data; void M(BinaryWriter writer) { writer.Write(Data?.Length ?? 0); } }";
            string output = EmitOnlyInvocation(source, "Write");
            Assert.StartsWith("writer.writeInt32(", output);
            Assert.Contains("this.Data", output);
            Assert.EndsWith(")", output);
        }

        [Fact]
        public void ContinueWithUsingDefaultSchedulerUsesPromiseContinuation() {
            const string source = "using System; using System.Threading.Tasks; class C { void M(int delay, Action action) { _ = Task.Delay(delay).ContinueWith(_ => action(), TaskScheduler.Default); } }";
            string output = EmitOnlyInvocation(source, "ContinueWith");
            Assert.Contains("Task.Delay(delay).then(_ => action())", output);
            Assert.DoesNotContain("TaskScheduler", output);
        }

        [Fact]
        public void StaticArrayIndexOfUsesTheArrayInstance() {
            const string source = "using System; class C { int M(string[] values, string value) => Array.IndexOf(values, value); }";
            string output = EmitOnlyInvocation(source, "IndexOf");
            Assert.Equal("values.indexOf(value)", output);
        }

        static string EmitOnlyInvocation(string source, string methodName) {
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var invocation = compilation.Root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Single(candidate => candidate.Expression.ToString().Contains(methodName));
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context);
            var lines = new List<string>();
            harness.Processor.ProcessExpression(compilation.Model, harness.Context, invocation, lines);
            return TsProcessorTestHarness.JoinLines(lines).Trim();
        }
    }
}
