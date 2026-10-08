using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Checks that anonymous signing records preserve names, values and evaluation order through emission.</summary>
    public sealed class TypeScriptAnonymousObjectTests {
        /// <summary>Executes generated record expressions and compares their complete ordered JSON representation.</summary>
        /// <param name="expression">C# anonymous record under test.</param>
        /// <param name="expected">Expected complete serialized record.</param>
        [Theory]
        [InlineData("new { tokenId, sourceId, startUnixTime, durationSeconds = duration }", "{\"tokenId\":\"window\",\"sourceId\":\"authority\",\"startUnixTime\":10,\"durationSeconds\":300}")]
        [InlineData("new { Nested = new { sourceId }, Empty = new { }, @event = tokenId }", "{\"Nested\":{\"sourceId\":\"authority\"},\"Empty\":{},\"event\":\"window\"}")]
        [InlineData("new { __proto__ = sourceId, Value = tokenId }", "{\"__proto__\":\"authority\",\"Value\":\"window\"}")]
        [InlineData("new { First = next(), Second = next() }", "{\"First\":1,\"Second\":2}")]
        [InlineData("new { Success = parse(out int parsed), Parsed = parsed }", "{\"Success\":true,\"Parsed\":7}")]
        [InlineData("new { sourceId.Length }", "{\"Length\":9}")]
        public void AnonymousRecordPreservesSerializedPayload(string expression, string expected) {
            var compilation = RoslynTestHelper.CreateCompilation("delegate bool Try(out int value); class C { object M(string tokenId, string sourceId, long startUnixTime, int duration, System.Func<int> next, Try parse) { return " + expression + "; } }");
            Assert.DoesNotContain(compilation.Compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var creation = compilation.Root.DescendantNodes().OfType<AnonymousObjectCreationExpressionSyntax>().First();
            var harness = TsProcessorTestHarness.Create();
            var nativeRemap = typeof(TypeScriptProgram).GetMethod("buildNativeRemap", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(nativeRemap);
            nativeRemap.Invoke(harness.Program, null);
            TsProcessorTestHarness.PushClassAndFunction(harness.Context);
            var lines = new List<string>();
            var result = harness.Processor.ProcessExpression(compilation.Model, harness.Context, creation, lines);
            Assert.True(result.Processed);
            Execute(string.Concat(lines), expected, result.BeforeLines == null ? string.Empty : string.Concat(result.BeforeLines));
        }

        /// <summary>An enclosing camel-cased field must never rename the created object's Pascal-cased property.</summary>
        /// <param name="remap">Optional runtime member alias on the created type.</param>
        /// <param name="expected">Only the created member's declared name or explicit alias is permitted.</param>
        [Theory]
        [InlineData("", "SourceId")]
        [InlineData("runtimeSource", "runtimeSource")]
        public void InitializerUsesCreatedTypeMember(string remap, string expected) {
            var compilation = RoslynTestHelper.CreateCompilation("class Token { public string SourceId { get; set; } } class C { string sourceId; object M() { return new Token { SourceId = sourceId }; } }");
            var assignment = compilation.Root.DescendantNodes().OfType<AssignmentExpressionSyntax>().Single();
            var harness = TsProcessorTestHarness.Create();
            var nativeRemap = typeof(TypeScriptProgram).GetMethod("buildNativeRemap", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(nativeRemap);
            nativeRemap.Invoke(harness.Program, null);
            TsProcessorTestHarness.PushClassAndFunction(harness.Context);
            var currentClass = harness.Context.GetCurrentClass();
            Assert.NotNull(currentClass);
            currentClass.Variables.Add(new ConversionVariable { Name = "sourceId", VarType = VariableUtil.GetVarType("string") });
            var tokenClass = new ConversionClass { Name = "Token" };
            tokenClass.Variables.Add(new ConversionVariable { Name = "SourceId", Remap = remap, VarType = VariableUtil.GetVarType("string") });
            harness.Program.Classes.Add(tokenClass);
            var lines = new List<string>();
            harness.Processor.ProcessExpression(compilation.Model, harness.Context, assignment.Left, lines);
            Assert.Equal(expected, string.Concat(lines));
        }

        /// <summary>Executes the actual emission in an isolated Node context with deterministic values and a side-effect counter.</summary>
        /// <param name="expression">Converted object expression.</param>
        /// <param name="expected">Serialized record expected from the C# declaration.</param>
        /// <param name="before">Prerequisite declarations emitted for out-variable scope.</param>
        static void Execute(string expression, string expected, string before) {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "cs2.ts", ".net.ts"));
            var start = new ProcessStartInfo("node") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-e");
            start.ArgumentList.Add("const ts = require(require('node:path').join(process.argv[1], 'node_modules/typescript')); const source = '(function(tokenId, sourceId, startUnixTime, duration, next, parse) { ' + process.argv[4] + 'return ' + process.argv[2] + '; })'; const js = ts.transpileModule(source, {compilerOptions:{target:ts.ScriptTarget.ES2020}}).outputText; const run = require('node:vm').runInNewContext(js); let calls = 0; const value = run('window','authority',10,300,()=>++calls,out=>{out.value=7;return true;}); const actual = JSON.stringify(value); if (actual !== process.argv[3]) throw new Error(actual + ' !== ' + process.argv[3]);");
            start.ArgumentList.Add(root);
            start.ArgumentList.Add(expression);
            start.ArgumentList.Add(expected);
            start.ArgumentList.Add(before);
            var process = Process.Start(start);
            if (process == null) { throw new InvalidOperationException("Could not execute anonymous object regression."); }
            using var ownedProcess = process;
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, error + output);
        }
    }
}
