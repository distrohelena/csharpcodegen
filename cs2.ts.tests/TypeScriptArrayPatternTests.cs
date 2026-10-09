using System.Collections.Generic;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>
    /// Exercises type patterns against array types, which have no TypeScript class name to test with
    /// <c>instanceof</c>: <c>byte[]</c> is emitted as <c>Uint8Array</c> and every other array as a plain JS array.
    /// </summary>
    public sealed class TypeScriptArrayPatternTests {
        /// <summary>A byte-array declaration pattern tests the emitted typed-array class instead of the C# spelling.</summary>
        [Fact]
        public void ByteArrayPatternTestsUint8Array() {
            string output = Convert("class C { int M(object value) { if (value is byte[] bytes) { return bytes.Length; } return 0; } }");
            Assert.Contains("instanceof Uint8Array", output);
            Assert.DoesNotContain("instanceof byte[]", output);
        }

        /// <summary>A non-byte array pattern tests for a plain JS array, the shape every other C# array is emitted as.</summary>
        [Fact]
        public void StringArrayPatternTestsArrayIsArray() {
            string output = Convert("class C { int M(object value) { if (value is string[] texts) { return texts.Length; } return 0; } }");
            Assert.Contains("Array.isArray(", output);
            Assert.DoesNotContain("instanceof string[]", output);
        }

        /// <summary>Converts the body of method <c>M</c> and returns the joined TypeScript.</summary>
        /// <param name="source">C# compilation unit declaring <c>C.M(object)</c> returning int.</param>
        /// <returns>Emitted TypeScript for the method body.</returns>
        static string Convert(string source) {
            var compilation = RoslynTestHelper.CreateCompilation(source);
            Assert.DoesNotContain(compilation.Compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var method = RoslynTestHelper.GetMethodByName(compilation.Root, "M");
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: new VariableType(VariableDataType.Int32));
            var lines = new List<string>();
            harness.Processor.ProcessBlock(compilation.Model, harness.Context, method.Body!, lines, depth: 0);
            return TsProcessorTestHarness.JoinLines(lines);
        }
    }
}
