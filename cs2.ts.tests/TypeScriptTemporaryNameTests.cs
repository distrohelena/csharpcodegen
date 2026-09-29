using System;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Checks stable temporary names, source-name collisions and execution of the emitted statements.</summary>
    public sealed class TypeScriptTemporaryNameTests {
        /// <summary>Reserves names across source files, including escaped C# identifiers in partial declarations.</summary>
        [Fact]
        public void Allocator_SkipsIdentifiersFromEverySourceFile() {
            var source = RoslynTestHelper.CreateCompilation("partial class C { int __cond_1; }");
            var other = CSharpSyntaxTree.ParseText("partial class C { int @__cond_2; int __cond_3; }");
            var compilation = source.Compilation.AddSyntaxTrees(other);
            var model = compilation.GetSemanticModel(source.Root.SyntaxTree);
            var allocator = new TypeScriptTemporaryNameAllocator();
            Assert.Equal("__cond_4", allocator.Allocate(model, "__cond_"));
            Assert.Equal("out_5", allocator.Allocate(model, "out_"));
            Assert.Equal("__cond_6", allocator.Allocate(model, "__cond_"));
            Assert.Equal("__cond_4", new TypeScriptTemporaryNameAllocator().Allocate(model, "__cond_"));
        }

        /// <summary>Fresh processors emit identical code while the JavaScript retains source-variable values.</summary>
        /// <param name="body">C# statements containing temporaries and deliberately similar source names.</param>
        /// <param name="expected">Return value required from the emitted JavaScript.</param>
        [Theory]
        [InlineData("int out_1 = 20; int x; Read(out x); return x + out_1;", 25)]
        [InlineData("int __binary_1 = 20; int x; bool ok = Read(out x) && Read(out x); return ok ? x + __binary_1 : 0;", 25)]
        [InlineData("int __cond_1 = 20; int x; if (Read(out x)) { return x + __cond_1; } return 0;", 25)]
        [InlineData("object input = \"abc\"; int __patternTarget1 = 20; if (input is string text) return text.Length + __patternTarget1; return 0;", 23)]
        [InlineData("int __using_1 = 20; using (Open()) { return __using_1; }", 20)]
        [InlineData("int x = 7; bool ok = false && Read(out x); return x;", 7)]
        [InlineData("int x = 7; bool ok = true || Read(out x); return x;", 7)]
        [InlineData("int x = 0; bool enabled = true; int value = enabled ? (Read(out x) ? x : 0) : 0; return value;", 5)]
        public void GeneratedStatements_AreStableAndPreserveValues(string body, int expected) {
            string first = Emit(body);
            string second = Emit(body);
            Assert.Equal(first, second);
            Assert.Equal(expected, Execute(first));
        }

        /// <summary>Collection initialization and conditional expression emission are stable across fresh processors.</summary>
        /// <param name="body">C# collection or conditional statements whose temporary names previously changed each run.</param>
        [Theory]
        [InlineData("var list = new System.Collections.Generic.List<int> { 1, 2 }; return list.Count;")]
        [InlineData("System.Collections.Generic.List<int> list = new() { 1, 2 }; return list.Count;")]
        [InlineData("int x; return Read(out x) ? x : 0;")]
        public void GeneratedExpressions_AreStable(string body) {
            Assert.Equal(Emit(body), Emit(body));
        }

        /// <summary>Converts the actual method body with a fresh processor and semantic model for each invocation.</summary>
        /// <param name="body">Statements to place inside the source method.</param>
        /// <returns>TypeScript statements emitted for the source method.</returns>
        static string Emit(string body) {
            var source = RoslynTestHelper.CreateCompilation("class C { static bool Read(out int value) { value = 5; return true; } static System.IDisposable Open() { return null; } int M() { " + body + " } }");
            var method = RoslynTestHelper.GetMethodByName(source.Root, "M");
            if (method.Body == null) { throw new InvalidOperationException("A test method body is required."); }
            var harness = TsProcessorTestHarness.Create();
            cs2.ts.util.TypeScriptTypeMap.PopulateTypeMap(harness.Program);
            var stringType = new ConversionClass { Name = "string", IsNative = true };
            stringType.Functions.Add(new ConversionFunction { Name = "Length", Remap = "length", ReturnType = new VariableType(VariableDataType.Int32, "int") });
            harness.Program.RegisterClass(stringType);
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: new VariableType(VariableDataType.Int32));
            var result = TsProcessorTestHarness.RunProcessBlock(harness.Processor, harness.Context, source.Model, method.Body);
            return string.Concat(result.Lines);
        }

        /// <summary>Executes generated code with deterministic external read/resource functions, preserving actual local scoping.</summary>
        /// <param name="source">Generated TypeScript body to execute.</param>
        /// <returns>The integer returned from the emitted body.</returns>
        static int Execute(string source) {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            string compiler = Path.Combine(root, "cs2.ts", ".net.ts", "node_modules", "typescript", "lib", "typescript.js");
            Assert.True(File.Exists(compiler), "Install runtime TypeScript dependencies before executing emission tests.");
            var start = new ProcessStartInfo("node") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-e");
            start.ArgumentList.Add("const ts = require(process.argv[1]); const source = '(function(){' + process.argv[2] + '})()'; const js = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText; const Read = value => { value.value = 5; return true; }; const Open = () => ({ dispose() {} }); const result = require('node:vm').runInNewContext(js, { Read, Open, C: { Read, Open } }); process.stdout.write(JSON.stringify(result));");
            start.ArgumentList.Add(compiler);
            start.ArgumentList.Add(source);
            using var process = Process.Start(start);
            if (process == null) { throw new InvalidOperationException("Could not start the generated-code runtime test."); }
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, error + "\n" + source);
            Assert.True(int.TryParse(output, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value), output + "\n" + source);
            return value;
        }
    }
}
