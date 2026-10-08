using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Exercises pattern variables that become available when a negative type guard does not match.</summary>
    public sealed class TypeScriptNegatedPatternTests {
        /// <summary>Checks failure, successful narrowing, reassignment and conditional branches against emitted code.</summary>
        /// <param name="body">A valid C# method body using a negative declaration pattern.</param>
        [Theory]
        [InlineData("if (value is not string text) { return 0; } text = \"changed\"; return text == \"changed\" ? 1 : 0;")]
        [InlineData("if (value is not (string text)) { return 0; } return text == value ? 1 : 0;")]
        [InlineData("if (value is not string text) { return 0; } else { return text == value ? 1 : 0; }")]
        [InlineData("if (value == null) { return 0; } else if (value is not string text) { return 0; } else { return text == value ? 1 : 0; }")]
        [InlineData("if (read() is not string text) { return 0; } return text == value ? 1 : 0;")]
        [InlineData("if (value is string text) { return text == value ? 1 : 0; } return 0;")]
        public void NegativeGuard_PreservesVariableScope(string body) {
            var compilation = RoslynTestHelper.CreateCompilation("class C { int M(object value, System.Func<object> read) { " + body + " } }");
            Assert.DoesNotContain(compilation.Compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
            var method = RoslynTestHelper.GetFirstMethod(compilation.Root);
            if (method.Body == null) { throw new InvalidOperationException("The pattern fixture requires a method body."); }
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: new VariableType(VariableDataType.Int32));
            var lines = new List<string>();
            harness.Processor.ProcessBlock(compilation.Model, harness.Context, method.Body, lines, depth: 0);
            ExecuteTypeScript(string.Concat(lines));
        }

        /// <summary>Executes both matching and nonmatching inputs without installing dependencies or accessing the network.</summary>
        /// <param name="body">Generated method body whose references and control flow must remain valid.</param>
        static void ExecuteTypeScript(string body) {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            string compiler = Path.Combine(root, "cs2.ts", ".net.ts", "node_modules", "typescript", "lib", "typescript.js");
            Assert.True(File.Exists(compiler), "The existing TypeScript runtime dependency is required.");
            var start = new ProcessStartInfo("node") {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("-e");
            start.ArgumentList.Add("const ts = require(process.argv[1]); const source = '(function(value: unknown, read: () => unknown){' + process.argv[2] + '})'; const options = { strict: true, target: ts.ScriptTarget.ES2020 }; const host = ts.createCompilerHost(options); const original = host.getSourceFile.bind(host); host.getSourceFile = (name, version, onError) => name === 'pattern-test.ts' ? ts.createSourceFile(name, source, version, true) : original(name, version, onError); const diagnostics = ts.getPreEmitDiagnostics(ts.createProgram(['pattern-test.ts'], options, host)); if (diagnostics.length) throw new Error(ts.formatDiagnosticsWithColorAndContext(diagnostics, { getCurrentDirectory: () => '', getCanonicalFileName: x => x, getNewLine: () => String.fromCharCode(10) })); const js = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2020 } }).outputText; const run = require('node:vm').runInNewContext(js); for (const input of ['valid', 42, null, {}]) { const expected = typeof input === 'string' ? 1 : 0; let reads = 0; if (run(input, () => { reads++; return input; }) !== expected) throw new Error('Incorrect negative-pattern result'); if (reads > 1) throw new Error('Pattern input evaluated repeatedly'); }");
            start.ArgumentList.Add(compiler);
            start.ArgumentList.Add(body);
            var process = Process.Start(start);
            if (process == null) { throw new InvalidOperationException("Could not start the generated pattern regression."); }
            using var ownedProcess = process;
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, error + output);
        }
    }
}
