using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Exercises declaration patterns embedded in short-circuit conditions.</summary>
    public sealed class TypeScriptCompositePatternTests {
        /// <summary>Hoists both bindings, uses a structural interface guard and assigns each binding only on a successful match.</summary>
        [Fact]
        public void NegativeCompoundGuardPreservesInterfaceAndClassBindings() {
            const string source = "interface IWatchable { int Read(); } class Request { public int Value { get; set; } } class C { int M(object service, object payload) { if (!(service is IWatchable watchable) || !(payload is Request request)) { return 0; } return watchable.Read() + request.Value; } }";
            var compilation = RoslynTestHelper.CreateCompilation(source);
            Assert.DoesNotContain(compilation.Compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var method = RoslynTestHelper.GetMethodByName(compilation.Root, "M");
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: new VariableType(VariableDataType.Int32));
            var lines = new List<string>();

            harness.Processor.ProcessBlock(compilation.Model, harness.Context, method.Body!, lines, depth: 0);

            string output = TsProcessorTestHarness.JoinLines(lines);
            Assert.Contains("let watchable!:", output);
            Assert.Contains("let request!:", output);
            Assert.Contains("typeof (<IWatchable><unknown>__pattern).Read === \"function\"", output);
            Assert.DoesNotContain("instanceof IWatchable", output);
            CompileAndRun(output);
        }

        /// <summary>Uses structural casts for framework interfaces that have no emitted TypeScript declaration.</summary>
        [Fact]
        public void FrameworkInterfacePatternDoesNotReferenceMissingType() {
            const string source = "using System; class C { string M(object value) { if (value is IFormattable formattable) { return formattable.ToString(null, null); } return string.Empty; } }";
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var method = RoslynTestHelper.GetMethodByName(compilation.Root, "M");
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: VariableUtil.GetVarType("string"));
            var lines = new List<string>();

            harness.Processor.ProcessBlock(compilation.Model, harness.Context, method.Body!, lines, depth: 0);

            string output = TsProcessorTestHarness.JoinLines(lines);
            Assert.DoesNotContain("<IFormattable>", output);
            Assert.Contains("<any><unknown>", output);
            Assert.Contains("typeof (<any><unknown>", output);
        }

        /// <summary>Erases a class type parameter from a static member because TypeScript forbids that reference.</summary>
        [Fact]
        public void StaticGenericClassPatternErasesClassTypeParameter() {
            const string source = "class C<T> { static bool M(object value) { return value is T typed; } }";
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var method = RoslynTestHelper.GetMethodByName(compilation.Root, "M");
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: VariableUtil.GetVarType("bool"));
            harness.Context.GetCurrentClass()!.GenericArgs = new List<string> { "T" };
            harness.Context.GetCurrentFunction()!.Function.IsStatic = true;
            var lines = new List<string>();

            harness.Processor.ProcessBlock(compilation.Model, harness.Context, method.Body!, lines, depth: 0);

            string output = TsProcessorTestHarness.JoinLines(lines);
            Assert.DoesNotContain("<T>", output);
            Assert.Contains("<any><unknown>", output);
        }

        /// <summary>Compiles the emitted block under strict TypeScript and verifies both success and rejection paths.</summary>
        /// <param name="body">Generated function body to validate.</param>
        static void CompileAndRun(string body) {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            string compiler = Path.Combine(root, "cs2.ts", ".net.ts", "node_modules", "typescript", "lib", "typescript.js");
            Assert.True(File.Exists(compiler), "The existing TypeScript runtime dependency is required.");
            string source = "interface IWatchable { Read(): number; } class Request { Value = 2; } function run(service: unknown, payload: unknown): number {" + body + "}";
            var start = new ProcessStartInfo("node") {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("-e");
            start.ArgumentList.Add("const ts = require(process.argv[1]); const source = process.argv[2]; const result = ts.transpileModule(source, { compilerOptions: { strict: true, target: ts.ScriptTarget.ES2020 }, reportDiagnostics: true }); const diagnostics = result.diagnostics || []; if (diagnostics.length) throw new Error(ts.formatDiagnostics(diagnostics, { getCurrentDirectory: () => '', getCanonicalFileName: x => x, getNewLine: () => String.fromCharCode(10) })); const exports = require('node:vm').runInNewContext(result.outputText + ';({ run, Request })'); if (exports.run({ Read: () => 3 }, new exports.Request()) !== 5) throw new Error('successful bindings were not preserved'); if (exports.run({}, new exports.Request()) !== 0) throw new Error('interface rejection failed');");
            start.ArgumentList.Add(compiler);
            start.ArgumentList.Add(source);
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the generated pattern regression.");
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, error + output);
        }
    }
}
