using System.Linq;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>
    /// Verifies that generated overload names preserve interface implementation contracts.
    /// </summary>
    public sealed class TypeScriptInterfaceImplementationOverloadTests {
        /// <summary>
        /// Keeps the overload selected by Roslyn for an interface member on the interface's emitted name.
        /// </summary>
        [Fact]
        public void ImplicitInterfaceImplementationKeepsInterfaceMemberName() {
            const string source = "interface IService { void Send(int value, int delay); } class Client : IService { public void Send(int value) { } public void Send(int value, int delay) { } }";
            var compilation = RoslynTestHelper.CreateCompilation(source, "interface-overload.cs");
            Assert.DoesNotContain(compilation.Compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var program = new TypeScriptProgram(new ConversionRules());
            var context = new ConversionContext(program);

            foreach (var member in compilation.Root.Members) {
                ConversionPreProcessor.PreProcessExpression(compilation.Model, context, member);
            }

            ConversionClass client = Assert.Single(program.Classes.Where(candidate => candidate.Name == "Client"));
            TypeScriptInterfaceContractAligner.Align(client, program);
            ConversionFunction shortOverload = Assert.Single(client.Functions.Where(function => function.InParameters.Count == 1));
            ConversionFunction interfaceOverload = Assert.Single(client.Functions.Where(function => function.InParameters.Count == 2));
            Assert.Equal("Send2", shortOverload.Remap);
            Assert.Equal("Send", interfaceOverload.Remap);
        }
    }
}
