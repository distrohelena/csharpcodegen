using System.Collections.Generic;
using System.Linq;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Verifies single-threaded browser lowering for atomic framework primitives.</summary>
    public sealed class TypeScriptThreadingPrimitiveLoweringTests {
        /// <summary>Preserves Exchange's old-value result while mutating the referenced local, and treats volatile reads as direct values.</summary>
        [Fact]
        public void ExchangeAndReadPreserveObservableSemanticsWithoutRuntimeTypes() {
            const string source = "using System.Threading; class C { int M() { int value = 0; int old = Interlocked.Exchange(ref value, 1); int observed = Volatile.Read(ref value); return old + observed; } }";
            var compilation = RoslynTestHelper.CreateCompilation(source);
            Assert.DoesNotContain(compilation.Compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var method = RoslynTestHelper.GetFirstMethod(compilation.Root);
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: new VariableType(VariableDataType.Int32));
            var lines = new List<string>();

            harness.Processor.ProcessBlock(compilation.Model, harness.Context, method.Body!, lines, depth: 0);

            string output = TsProcessorTestHarness.JoinLines(lines);
            Assert.Contains("const __interlockedOld", output);
            Assert.Contains("value = 1", output);
            Assert.Contains("return __interlockedOld", output);
            Assert.Contains("let observed = value", output);
            Assert.DoesNotContain("Interlocked", output);
            Assert.DoesNotContain("Volatile", output);
        }

        /// <summary>Turns a TimeSpan sleep into an asynchronous browser delay and marks the owner async.</summary>
        [Fact]
        public void TimeSpanSleepUsesTaskDelayWithoutBlockingTheBrowserThread() {
            const string source = "using System; using System.Threading; class C { void M(TimeSpan delay) { Thread.Sleep(delay); } }";
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var invocation = compilation.Root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>().Single();
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context);
            var lines = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context, invocation, lines);

            Assert.Equal("await Task.Delay(delay)", TsProcessorTestHarness.JoinLines(lines));
            Assert.True(harness.Context.GetCurrentFunction()!.Function.IsAsync);
        }
    }
}
