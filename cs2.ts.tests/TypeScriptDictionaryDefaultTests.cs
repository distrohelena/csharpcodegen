using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Checks framework dictionary lookup translation against the real runtime and erased C# defaults.</summary>
    public sealed class TypeScriptDictionaryDefaultTests {
        /// <summary>Missing keys receive the constructed value type's default while present null and falsy values survive lookup.</summary>
        /// <param name="type">C# dictionary value type.</param>
        /// <param name="expression">Framework extension invocation to translate.</param>
        /// <param name="expected">JavaScript literal expected for a missing key.</param>
        [Theory]
        [InlineData("object", "dictionary.GetValueOrDefault(\"key\")", "null")]
        [InlineData("string", "dictionary.GetValueOrDefault(\"key\")", "null")]
        [InlineData("int", "dictionary.GetValueOrDefault(\"key\")", "0")]
        [InlineData("uint", "dictionary.GetValueOrDefault(\"key\")", "0")]
        [InlineData("bool", "dictionary.GetValueOrDefault(\"key\")", "false")]
        [InlineData("char", "dictionary.GetValueOrDefault(\"key\")", "'\\0'")]
        [InlineData("int?", "dictionary.GetValueOrDefault(\"key\")", "null")]
        [InlineData("object", "dictionary.GetValueOrDefault(\"key\", fallback)", "'fallback'")]
        [InlineData("int", "CollectionExtensions.GetValueOrDefault(dictionary, \"key\")", "0")]
        public void LookupPreservesMissingAndPresentValues(string type, string expression, string expected) {
            string emitted = Emit("using System.Collections.Generic; class C { " + type + " M(Dictionary<string, " + type + "> dictionary, " + type + " fallback) { return " + expression + "; } }");
            ExecuteRuntime(emitted, expected);
        }

        /// <summary>User methods with the same name are not framework extensions and must retain their signature.</summary>
        [Fact]
        public void UserMethodIsNotRewritten() {
            string emitted = Emit("class D { public int GetValueOrDefault(string key) => 1; } class C { int M(D dictionary) { return dictionary.GetValueOrDefault(\"key\"); } }");
            Assert.DoesNotContain(", 0", emitted);
            Assert.DoesNotContain(", null", emitted);
        }

        /// <summary>Emits the return invocation from a valid C# semantic model without synthetic source rewrites.</summary>
        /// <param name="source">C# fixture containing the lookup method.</param>
        /// <returns>The converter's actual lookup expression.</returns>
        static string Emit(string source) {
            var compilation = RoslynTestHelper.CreateCompilation(source);
            Assert.DoesNotContain(compilation.Compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var invocation = compilation.Root.DescendantNodes().OfType<InvocationExpressionSyntax>().First();
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context);
            var lines = new List<string>();
            harness.Processor.ProcessExpression(compilation.Model, harness.Context, invocation, lines);
            return string.Concat(lines);
        }

        /// <summary>Loads the actual TypeScript dictionary source offline and tests emitted calls against its key comparer and presence semantics.</summary>
        /// <param name="expression">Generated dictionary lookup.</param>
        /// <param name="expected">Expected missing-key result expressed as a JavaScript literal.</param>
        static void ExecuteRuntime(string expression, string expected) {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "cs2.ts", ".net.ts"));
            var start = new ProcessStartInfo("node") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-e");
            start.ArgumentList.Add("const path = require('node:path'); const ts = require(path.join(process.argv[1], 'node_modules/typescript')); require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(require('node:fs').readFileSync(file, 'utf8'), { compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS } }).outputText, file); const { Dictionary } = require(path.join(process.argv[1], 'system/collections/generic/dictionary.ts')); const run = new Function('dictionary', 'fallback', 'return ' + process.argv[2]); const expected = new Function('return ' + process.argv[3])(); const dictionary = new Dictionary({ GetHashCode: () => 1, Equals: (a,b) => a.toLowerCase() === b.toLowerCase() }); dictionary.add('other', 9); if (!Object.is(run(dictionary, 'fallback'), expected)) throw new Error('Missing-key default changed'); for (const value of [null, undefined, false, 0, '', 'stored']) { dictionary.set('KEY', value); if (!Object.is(run(dictionary, 'fallback'), value)) throw new Error('Stored value was replaced by default'); } if (dictionary.Count !== 2) throw new Error('Lookup mutated dictionary');");
            start.ArgumentList.Add(root);
            start.ArgumentList.Add(expression);
            start.ArgumentList.Add(expected);
            var process = Process.Start(start);
            if (process == null) { throw new InvalidOperationException("Could not start dictionary runtime verification."); }
            using var ownedProcess = process;
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, error + output);
        }
    }
}
