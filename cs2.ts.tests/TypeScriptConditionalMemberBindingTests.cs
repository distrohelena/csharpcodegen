using System.Collections.Generic;
using System.Linq;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    public sealed class TypeScriptConditionalMemberBindingTests {
        [Theory]
        [InlineData("using System; class C { string M(Guid? value) => value?.ToString(); }", ".toString()")]
        [InlineData("class C { int? M(byte[] value) => value?.Length; }", ".length")]
        [InlineData("class C { long M(byte[] value) => value.LongLength; }", ".length")]
        public void FrameworkMembersRetainRuntimeRemaps(string source, string expected) {
            string output = Emit(source);
            Assert.Contains(expected, output);
        }

        [Fact]
        public void InterfacePropertyDoesNotBindToSameNamedOwnerField() {
            const string source = "interface ISettings { object RegistrarSigningKey { get; } } class C { object registrarSigningKey; object M(ISettings settings) => settings?.RegistrarSigningKey; }";
            string output = Emit(source);
            Assert.Contains(".RegistrarSigningKey", output);
            Assert.DoesNotContain(".registrarSigningKey", output);
        }

        [Fact]
        public void ConditionalCallToConvertedAsyncMethodAwaitsTheConditionalResult() {
            const string source = "class R { } class Store { public R Get() => new R(); } class C { Store store; R M() => store?.Get(); }";
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var expression = compilation.Root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.ValueText == "M").ExpressionBody!.Expression;
            var harness = TsProcessorTestHarness.Create();
            var store = new ConversionClass { Name = "Store" };
            store.Functions.Add(new ConversionFunction { Name = "Get", ReturnType = VariableUtil.GetVarType("R"), IsAsync = true });
            harness.Program.Classes.Add(store);
            var owner = new ConversionClass { Name = "C" };
            owner.Variables.Add(new ConversionVariable { Name = "store", VarType = VariableUtil.GetVarType("Store") });
            harness.Program.Classes.Add(owner);
            harness.Context.AddClass(owner);
            harness.Context.AddFunction(new FunctionStack(new ConversionFunction { Name = "M", ReturnType = VariableUtil.GetVarType("R") }));
            var lines = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context, expression, lines);

            Assert.StartsWith("await (() =>", TsProcessorTestHarness.JoinLines(lines));
            Assert.True(harness.Context.GetCurrentFunction()!.Function.IsAsync);
        }

        static string Emit(string source) {
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var expression = compilation.Root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single().ExpressionBody!.Expression;
            var harness = TsProcessorTestHarness.Create();
            var owner = new ConversionClass { Name = "C" };
            owner.Variables.Add(new ConversionVariable { Name = "registrarSigningKey", VarType = VariableUtil.GetVarType("object") });
            harness.Program.Classes.Add(owner);
            harness.Context.AddClass(owner);
            harness.Context.AddFunction(new FunctionStack(new ConversionFunction { Name = "M", ReturnType = VariableUtil.GetVarType("object") }));
            var lines = new List<string>();
            harness.Processor.ProcessExpression(compilation.Model, harness.Context, expression, lines);
            return TsProcessorTestHarness.JoinLines(lines);
        }
    }
}
