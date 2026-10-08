using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    public sealed class TypeScriptParamsTests {
        [Fact]
        public void ParamsMethodEmitsTypeScriptRestParameter() {
            var harness = TsProcessorTestHarness.Create();
            var function = new ConversionFunction {
                Name = "Read",
                Remap = "Read",
                ReturnType = VariableUtil.GetVarType("string")
            };
            function.InParameters.Add(new ConversionVariable {
                Name = "keys",
                VarType = VariableUtil.GetVarType("string[]"),
                Modifier = cs2.core.ParameterModifier.Params
            });
            var type = new ConversionClass { Name = "C", DeclarationType = MemberDeclarationType.Interface };
            type.Functions.Add(function);
            harness.Program.Classes.Add(type);

            string output = EmitFunctions(harness, type);

            Assert.Contains("Read(...keys: string[]): string;", output);
            Assert.DoesNotContain("Read(keys: string[]): string;", output);
        }

        [Fact]
        public void ParamsArrayForwardingEmitsSpreadArgument() {
            const string source = "class C { static int Count(params string[] keys) => keys.Length; static int Forward(string[] keys) => Count(keys); static int Expanded() => Count(\"a\", \"b\"); }";
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var invocation = compilation.Root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Single(node => node.Expression.ToString() == "Count" && node.ArgumentList.Arguments.Count == 1 && node.ArgumentList.Arguments[0].Expression.ToString() == "keys");
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: VariableUtil.GetVarType("int"));
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context, invocation, output);

            Assert.Equal("C.Count(...keys)", TsProcessorTestHarness.JoinLines(output).Trim());
        }

        [Fact]
        public void OmittedParamsDoesNotMaterializeAnArrayArgument() {
            const string source = "class C { static int Count(params string[] keys) => keys.Length; static int Empty() => Count(); }";
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var invocation = compilation.Root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Single(node => node.Expression.ToString() == "Count" && node.ArgumentList.Arguments.Count == 0);
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: VariableUtil.GetVarType("int"));
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context, invocation, output);

            Assert.Equal("C.Count()", TsProcessorTestHarness.JoinLines(output).Trim());
        }

        [Fact]
        public void TaskWhenAllArrayOverloadKeepsIterableArgument() {
            const string source = "using System.Threading.Tasks; class C { static Task<int[]> Forward(Task<int>[] tasks) => Task.WhenAll(tasks); }";
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var invocation = compilation.Root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Single(node => node.Expression.ToString() == "Task.WhenAll");
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: VariableUtil.GetVarType("Task<int[]>"));
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context, invocation, output);

            Assert.DoesNotContain("...tasks", TsProcessorTestHarness.JoinLines(output));
            Assert.Contains("Task.WhenAll(tasks)", TsProcessorTestHarness.JoinLines(output));
        }
        static string EmitFunctions((TypeScriptConversiorProcessor Processor, TypeScriptLayerContext Context, TypeScriptProgram Program) harness, ConversionClass type) {
            var emitter = new TypeScriptClassEmitter(harness.Processor, harness.Program, harness.Program,
                new TypeScriptConversionOptions(), new cs2.ts.util.TypeScriptReflectionImportTracker(), null);
            using var output = new StringWriter();
            var emitFunctions = typeof(TypeScriptClassEmitter).GetMethod("EmitFunctions", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(emitFunctions);
            emitFunctions.Invoke(emitter, new object[] { type, new cs2.ts.util.TypeScriptOutputWriter(output) });
            return output.ToString();
        }
    }
}
