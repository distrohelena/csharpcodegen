using System;
using System.Collections.Generic;
using System.Linq;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    public sealed class TypeScriptAsyncInvocationTests {
        [Fact]
        public void TypeScriptAsyncInvocationIsParenthesizedBeforeMemberAccess() {
            const string source = "using System; class TypeScriptAsyncAttribute : Attribute { } class Store { [TypeScriptAsync] public string Read() => \"x\"; } class C { int Count(Store store) => store.Read().Length; }";
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var expression = compilation.Root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "store.Read().Length");
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: VariableUtil.GetVarType("int"));
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context, expression, output);

            Assert.Equal("(await store.Read()).Length", TsProcessorTestHarness.JoinLines(output).Trim());
        }

        [Fact]
        public void TypeScriptAsyncInvocationIsParenthesizedBeforeLinqChain() {
            const string source = "using System; using System.Linq; using System.Collections.Generic; class TypeScriptAsyncAttribute : Attribute { } [TypeScriptAsync] interface IStore { IEnumerable<string> Read(); } class C { bool HasEntries(IStore store) => store.Read().Any(); }";
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var expression = compilation.Root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "store.Read().Any()");
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: VariableUtil.GetVarType("bool"));
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context, expression, output);

            Assert.Equal("(await store.Read()).Any()", TsProcessorTestHarness.JoinLines(output).Trim());
        }

        [Fact]
        public void ManualResetEventSlimWaitPropagatesToItsConvertedCaller() {
            const string source = "using System.Threading; class C { bool Wait(ManualResetEventSlim signal) => signal.Wait(12); bool Caller(ManualResetEventSlim signal) => Wait(signal); }";
            var compilation = RoslynTestHelper.CreateCompilation(source);
            Assert.DoesNotContain(compilation.Compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
            var waitMethod = RoslynTestHelper.GetMethodByName(compilation.Root, "Wait") ?? throw new InvalidOperationException("Wait method fixture missing.");
            var callerMethod = RoslynTestHelper.GetMethodByName(compilation.Root, "Caller") ?? throw new InvalidOperationException("Caller method fixture missing.");
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: VariableUtil.GetVarType("bool"));
            var currentClass = harness.Context.GetCurrentClass() ?? throw new InvalidOperationException("Conversion class fixture missing.");
            currentClass.Semantic = compilation.Model;
            currentClass.Functions.Add(new ConversionFunction {
                Name = "Wait", ReturnType = VariableUtil.GetVarType("bool"), ArrowExpression = waitMethod.ExpressionBody
            });
            currentClass.Functions.Add(new ConversionFunction {
                Name = "Caller", ReturnType = VariableUtil.GetVarType("bool"), ArrowExpression = callerMethod.ExpressionBody
            });
            var callerExpression = callerMethod.ExpressionBody?.Expression ?? throw new InvalidOperationException("Caller expression fixture missing.");
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context, callerExpression, output);

            Assert.Equal("(await this.Wait(signal))", TsProcessorTestHarness.JoinLines(output).Trim());
            Assert.True((harness.Context.GetCurrentFunction() ?? throw new InvalidOperationException("Conversion function fixture missing.")).Function.IsAsync);
            Assert.True(currentClass.Functions.Single(function => function.Name == "Wait").IsAsync);
        }
        [Fact]
        public void ManualResetEventSlimWaitIsAwaitedAndMarksItsCallerAsync() {
            const string source = "using System.Threading; class C { bool Wait(ManualResetEventSlim signal) => signal.Wait(12); }";
            var compilation = RoslynTestHelper.CreateCompilation(source);
            Assert.DoesNotContain(compilation.Compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
            var expression = compilation.Root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "signal.Wait(12)");
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: VariableUtil.GetVarType("bool"));
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context, expression, output);

            Assert.Equal("(await signal.Wait(12))", TsProcessorTestHarness.JoinLines(output).Trim());
            Assert.True((harness.Context.GetCurrentFunction() ?? throw new InvalidOperationException("Conversion function fixture missing.")).Function.IsAsync);
            Assert.Contains(TypeScriptRuntimeRequirementCatalog.BaseRequirements,
                definition => definition.Name == "ManualResetEventSlim" && definition.Path == "./system/threading/manual-reset-event-slim");
        }
    }
}
