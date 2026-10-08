using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using cs2.ts.tests.TestHelpers;
using cs2.core.symbols;
using cs2.ts.util;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Checks native task/cancellation registration, lifecycle behavior and strict runtime typing.</summary>
    public sealed class TypeScriptTaskLifecycleTests {
        /// <summary>Native framework constructors must emit real runtime construction rather than nonexistent overload factories.</summary>
        /// <param name="expression">C# object creation using a registered framework type.</param>
        /// <param name="prefix">Expected native constructor at the start of the emitted expression.</param>
        [Theory]
        [InlineData("new System.Threading.Tasks.TaskCompletionSource<int>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously)", "new TaskCompletionSource<number>(")]
        [InlineData("new System.Threading.CancellationTokenSource()", "new CancellationTokenSource(")]
        [InlineData("new System.Threading.CancellationToken(true)", "new CancellationToken(")]
        [InlineData("new System.Threading.ManualResetEventSlim(false)", "new ManualResetEventSlim(")]
        public void FrameworkConstructorsUseNativeRuntime(string expression, string prefix) {
            var compilation = RoslynTestHelper.CreateCompilation("class C { object M() { return " + expression + "; } }");
            Assert.DoesNotContain(compilation.Compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var creation = compilation.Root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().First();
            var harness = TsProcessorTestHarness.Create();
            var nativeRemap = typeof(TypeScriptProgram).GetMethod("buildNativeRemap", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(nativeRemap);
            nativeRemap.Invoke(harness.Program, null);
            TypeScriptTypeMap.PopulateTypeMap(harness.Program);
            var type = compilation.Model.GetTypeInfo(creation).Type;
            Assert.NotNull(type);
            var definition = Assert.Single(TypeScriptRuntimeRequirementCatalog.BaseRequirements.Where(entry => entry.Name == type.Name));
            // Constructor emission needs the native classification; extracted members are validated by official regeneration.
            var requirement = new TypeScriptKnownClass(definition.Name, definition.Path, Array.Empty<Symbol>());
            new TypeScriptNativeClassBuilder().BuildNativeClasses(harness.Program, new[] { requirement });
            TsProcessorTestHarness.PushClassAndFunction(harness.Context);
            var lines = new List<string>();
            var result = harness.Processor.ProcessExpression(compilation.Model, harness.Context, creation, lines);
            Assert.True(result.Processed);
            string emitted = string.Concat(lines);
            Assert.StartsWith(prefix, emitted);
            Assert.DoesNotContain(".New", emitted);
        }

        /// <summary>Executes deterministic timer and promise lifecycle checks against maintained runtime source files.</summary>
        [Fact]
        public void SourceRuntimePassesLifecycleContracts() {
            RunNode("--test", "cs2.ts.tests/tools/task-lifecycle.test.cjs");
        }

        /// <summary>New runtime modules must type-check under strict ES2020 without disabling diagnostics.</summary>
        [Fact]
        public void SourceRuntimePassesStrictTypeChecking() {
            RunNode("cs2.ts/.net.ts/node_modules/typescript/bin/tsc", "--noEmit", "--strict", "--target", "ES2020",
                "--module", "commonjs", "--moduleResolution", "node", "--lib", "ES2020,DOM",
                "cs2.ts/.net.ts/system/func.ts", "cs2.ts/.net.ts/system/threading/tasks/task.ts",
                "cs2.ts/.net.ts/system/threading/cancellation-token-source.ts", "cs2.ts.tests/tools/task-base-type.fixture.ts",
                "cs2.ts.tests/tools/func-contract.fixture.ts");
        }

        /// <summary>Runs offline Node validation from the compiler checkout and reports the complete failure evidence.</summary>
        /// <param name="arguments">Argument list passed directly to Node without shell interpolation.</param>
        static void RunNode(params string[] arguments) {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            var start = new ProcessStartInfo("node") {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            var process = Process.Start(start);
            if (process == null) throw new InvalidOperationException("Could not start task lifecycle verification.");
            using var ownedProcess = process;
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, error + output);
        }
    }
}
