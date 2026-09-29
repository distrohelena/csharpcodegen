using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Executes generated handlers to verify that rethrows preserve the original exception object.</summary>
    public sealed class TypeScriptRethrowTests {
        /// <summary>Checks named, unnamed and nested handlers without replacing the original failure.</summary>
        /// <param name="body">C# handler scenario that must rethrow the original failure.</param>
        [Theory]
        [InlineData("try { throw failure; } catch (System.Exception exception) { throw; }")]
        [InlineData("try { throw failure; } catch { throw; }")]
        [InlineData("try { throw failure; } catch (System.Exception) { throw; }")]
        [InlineData("try { throw failure; } catch (System.Exception outer) { try { throw other; } catch (System.Exception inner) { } throw; }")]
        [InlineData("try { try { throw failure; } catch { throw; } } catch (System.Exception outer) { throw; }")]
        public void Rethrow_PreservesOriginalException(string body) {
            var compilation = RoslynTestHelper.CreateCompilation("class C { void M(System.Exception failure, System.Exception other) { " + body + " } }");
            var method = RoslynTestHelper.GetFirstMethod(compilation.Root);
            if (method.Body == null) { throw new InvalidOperationException("The test method must have a body."); }
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: new VariableType(VariableDataType.Int32));
            var emitted = TsProcessorTestHarness.RunProcessBlock(harness.Processor, harness.Context, compilation.Model, method.Body);
            string source = string.Concat(emitted.Lines);
            Assert.Equal(1, ExecuteTypeScript(source));
        }

        /// <summary>Transpiles with the installed runtime TypeScript package and executes the actual output in Node.</summary>
        /// <param name="source">Emitted statements to execute inside the enclosing function.</param>
        /// <returns>One when the emitted handler rethrows the original object.</returns>
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
            start.ArgumentList.Add("const ts = require(process.argv[1]); const source = '(function(){' + process.argv[2] + '})()'; const js = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText; const failure = new Error('original'); const other = new Error('nested'); let result = 0; try { require('node:vm').runInNewContext(js, { failure, other }); } catch (error) { if (error === failure) result = 1; } process.stdout.write(JSON.stringify(result));");
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
