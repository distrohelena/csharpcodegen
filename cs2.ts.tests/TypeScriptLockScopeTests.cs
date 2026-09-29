using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Executes emitted lock bodies to prove lexical scopes and enclosing control flow survive conversion.</summary>
    public sealed class TypeScriptLockScopeTests {
        /// <summary>Checks sibling names, nested locks, returns, and loop control without introducing function boundaries.</summary>
        /// <param name="body">C# method body containing the lock-scoping scenario.</param>
        /// <param name="expected">Integer returned by the corresponding emitted JavaScript.</param>
        [Theory]
        [InlineData("int total = 0; lock (gate) { int existing = 1; total += existing; } lock (gate) { int existing = 2; total += existing; } return total;", 3)]
        [InlineData("int total = 0; lock (gate) { int outer = 2; lock (gate) { int value = 3; total += outer + value; } } lock (gate) { int value = 4; total += value; } return total;", 9)]
        [InlineData("lock (gate) { return 7; }", 7)]
        [InlineData("lock (gate) return 5;", 5)]
        [InlineData("int total = 0; for (int i = 0; i < 4; i++) { lock (gate) { if (i == 1) continue; if (i == 3) break; total += i; } } return total;", 2)]
        public void LockBodies_PreserveScopeAndControlFlow(string body, int expected) {
            var compilation = RoslynTestHelper.CreateCompilation("class C { object gate = new object(); int M() { " + body + " } }");
            var method = RoslynTestHelper.GetFirstMethod(compilation.Root);
            if (method.Body == null) { throw new InvalidOperationException("The test method must have a body."); }
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: new VariableType(VariableDataType.Int32));
            var emitted = TsProcessorTestHarness.RunProcessBlock(harness.Processor, harness.Context, compilation.Model, method.Body);
            string source = string.Concat(emitted.Lines);
            Assert.Contains("// Lock omitted in TypeScript\n{\n", source);
            Assert.Equal(expected, ExecuteTypeScript(source));
        }

        /// <summary>Transpiles with the installed runtime TypeScript package and executes the actual output in Node.</summary>
        /// <param name="source">Emitted statements to execute inside the enclosing function.</param>
        /// <returns>The integer produced by the emitted return statement.</returns>
        static int ExecuteTypeScript(string source) {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            string compiler = Path.Combine(root, "cs2.ts", ".net.ts", "node_modules", "typescript", "lib", "typescript.js");
            Assert.True(File.Exists(compiler), "Install the TypeScript runtime dependencies before running emission tests.");
            ProcessStartInfo start = new ProcessStartInfo("node") {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("-e");
            start.ArgumentList.Add("const ts = require(process.argv[1]); const source = '(function(){' + process.argv[2] + '})()'; const js = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText; const result = require('node:vm').runInNewContext(js); process.stdout.write(JSON.stringify(result));");
            start.ArgumentList.Add(compiler);
            start.ArgumentList.Add(source);
            var process = Process.Start(start);
            if (process == null) { throw new InvalidOperationException("Could not start the emitted-code runtime check."); }
            using var ownedProcess = process;
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, error);
            return int.Parse(output, CultureInfo.InvariantCulture);
        }
    }
}
