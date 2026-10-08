using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    public sealed class TypeScriptStaticMemberReferenceTests {
        [Fact]
        public void UnqualifiedStaticFieldRemainsQualifiedWhenNestedClassContextIsPresent() {
            const string source = "class C { const int Value = 7; int M() => Value; }";
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
            var compilation = CSharpCompilation.Create("StaticMemberFixture", new[] { tree }, new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) }, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var identifier = tree.GetCompilationUnitRoot().DescendantNodes().OfType<IdentifierNameSyntax>().Single(candidate => candidate.Identifier.ValueText == "Value" && candidate.Parent is ArrowExpressionClauseSyntax);
            var harness = TsProcessorTestHarness.Create();
            harness.Context.AddClass(new ConversionClass { Name = "Outer" });
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, className: "C", returnType: VariableUtil.GetVarType("int"));
            harness.Context.GetCurrentClass()!.Variables.Add(new ConversionVariable { Name = "Value", IsStatic = true, VarType = VariableUtil.GetVarType("int") });
            List<string> lines = new List<string>();
            harness.Processor.ProcessExpression(compilation.GetSemanticModel(tree), harness.Context, identifier, lines);
            Assert.Equal("C.Value", TsProcessorTestHarness.JoinLines(lines));
        }
        [Fact]
        public void OwnStaticConstantDefault_IsQualifiedInConstructorAndOverloadEmission() {
            var type = new ConversionClass { Name = "C", DeclarationType = MemberDeclarationType.Class };
            type.Variables.Add(new ConversionVariable { Name = "DefaultLifetimeSeconds", IsStatic = true, VarType = VariableUtil.GetVarType("long") });
            var parameter = new ConversionVariable { Name = "lifetime", VarType = VariableUtil.GetVarType("long"), DefaultValue = " = DefaultLifetimeSeconds" };
            var suffix = typeof(TypeScriptClassEmitter).GetMethod("GetParameterDefaultSuffix", BindingFlags.Static | BindingFlags.NonPublic)!;
            var constructorDefault = typeof(TypeScriptClassEmitter).GetMethod("GetParameterDefaultValueExpression", BindingFlags.Static | BindingFlags.NonPublic)!;

            Assert.Equal(" = C.DefaultLifetimeSeconds", suffix.Invoke(null, new object[] { type, parameter }));
            Assert.Equal("C.DefaultLifetimeSeconds", constructorDefault.Invoke(null, new object[] { type, parameter }));
        }
        [Fact]
        public void ImplicitArrayOfOwnStaticFields_QualifiesEveryElement() {
            const string source = "class C { const string A = \"a\"; const string B = \"b\"; static string[] M() => new string[] { A, B }; }";
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
            var compilation = CSharpCompilation.Create("StaticArrayFixture", new[] { tree }, new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) }, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var array = tree.GetCompilationUnitRoot().DescendantNodes().OfType<InitializerExpressionSyntax>().Single(node => node.IsKind(SyntaxKind.ArrayInitializerExpression));
            var harness = TsProcessorTestHarness.Create();
            var type = new ConversionClass { Name = "C" };
            type.Variables.Add(new ConversionVariable { Name = "A", IsStatic = true, VarType = VariableUtil.GetVarType("string") });
            type.Variables.Add(new ConversionVariable { Name = "B", IsStatic = true, VarType = VariableUtil.GetVarType("string") });
            harness.Program.Classes.Add(type);
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, className: "C", returnType: VariableUtil.GetVarType("string[]"));
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.GetSemanticModel(tree), harness.Context, array, output);

            Assert.Equal("[ C.A, C.B ]", TsProcessorTestHarness.JoinLines(output));
        }

        [Fact]
        public void UnqualifiedInstanceMethodUsesLexicalReceiverWhenHelperContextIsOnStack() {
            const string source = "class C { bool TryValidate() => true; bool M() => TryValidate(); }";
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var invocation = compilation.Root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, className: "C", returnType: VariableUtil.GetVarType("bool"));
            harness.Context.AddClass(new ConversionClass { Name = "NestedHelper" });
            List<string> lines = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context, invocation, lines);

            Assert.Equal("this.TryValidate()", TsProcessorTestHarness.JoinLines(lines));
        }
    }
}
