using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Verifies that byte-array materialization keeps C# copy semantics in the browser runtime.</summary>
    public sealed class TypeScriptByteArrayToArrayTests {
        /// <summary>Maps Enumerable.ToArray over byte[] to a detached Uint8Array copy instead of a missing prototype member.</summary>
        [Fact]
        public void FrameworkByteArrayToArrayUsesDetachedUint8ArrayCopy() {
            Assert.Equal("Uint8Array.from(values)", Emit("using System.Linq; class C { byte[] M(byte[] values) => values.ToArray(); }"));
        }

        /// <summary>Leaves user-defined methods named ToArray on a different receiver unchanged.</summary>
        [Fact]
        public void UserDefinedToArrayIsNotRewritten() {
            Assert.DoesNotContain("Uint8Array.from", Emit("class Bytes { public byte[] ToArray() => new byte[0]; } class C { byte[] M(Bytes values) => values.ToArray(); }"));
        }

        /// <summary>Builds native remaps and emits the source invocation selected by Roslyn.</summary>
        static string Emit(string source) {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
            string trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
                ?? throw new InvalidOperationException("Trusted platform assembly paths are unavailable.");
            string runtimeAssembly = trustedPlatformAssemblies
                .Split(System.IO.Path.PathSeparator)
                .Single(path => string.Equals(System.IO.Path.GetFileName(path), "System.Runtime.dll", StringComparison.OrdinalIgnoreCase));
            var compilation = CSharpCompilation.Create(
                assemblyName: "ByteArrayToArrayFixture",
                syntaxTrees: new[] { syntaxTree },
                references: new[] {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                    MetadataReference.CreateFromFile(runtimeAssembly)
                },
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var model = compilation.GetSemanticModel(syntaxTree);
            var invocation = syntaxTree.GetCompilationUnitRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().First();
            var harness = TsProcessorTestHarness.Create();
            var nativeRemap = typeof(TypeScriptProgram).GetMethod("buildNativeRemap", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(nativeRemap);
            nativeRemap.Invoke(harness.Program, null);
            TsProcessorTestHarness.PushClassAndFunction(harness.Context);
            List<string> lines = new List<string>();
            harness.Processor.ProcessExpression(model, harness.Context, invocation, lines);
            return TsProcessorTestHarness.JoinLines(lines).Trim();
        }
    }
}
