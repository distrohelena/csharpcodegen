using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Verifies that discards evaluate their operands without inventing bindings or awaiting source Tasks.</summary>
    public sealed class TypeScriptDiscardTests {
        /// <summary>Discarded scalar expressions execute once while a legitimate underscore local remains assignable.</summary>
        /// <param name="body">Source method body containing a discard or a real underscore binding.</param>
        /// <param name="expected">Expected returned scalar.</param>
        [Theory]
        [InlineData("_ = Next(); return Next();", 2)]
        [InlineData("int _ = 0; _ = 7; return _;", 7)]
        [InlineData("return (_ = Next());", 1)]
        [InlineData("System.Func<int, int> map = _ => _ + 1; return map(6);", 7)]
        public void ScalarDiscardPreservesEvaluation(string body, int expected) {
            string emitted = Emit(body, "int", "int", false);
            Execute(emitted, "scalar", expected);
        }

        /// <summary>Both discarded source Tasks must start before either completion is released.</summary>
        /// <param name="taskType">Framework task return type whose completion must not serialize the calls.</param>
        [Theory]
        [InlineData("System.Threading.Tasks.Task<int>")]
        [InlineData("System.Threading.Tasks.Task")]
        [InlineData("System.Threading.Tasks.ValueTask<int>")]
        [InlineData("System.Threading.Tasks.ValueTask")]
        public void DiscardedTasksRemainConcurrent(string taskType) {
            string emitted = Emit("_ = Next(); _ = Next(); return 2;", "int", taskType, true);
            Assert.DoesNotContain("await", emitted);
            Assert.DoesNotContain("_ =", emitted);
            Execute(emitted, "concurrent", 2);
        }

        /// <summary>A real underscore field is assigned on the receiver rather than treated as a discard.</summary>
        [Fact]
        public void UnderscoreFieldRemainsAssignable() {
            string emitted = Emit("_ = 7; return _;", "int", "int", false, true);
            Assert.Contains("this._", emitted);
            Execute(emitted, "scalar", 7);
        }

        /// <summary>Task-valued conditional branches retain source concurrency instead of awaiting their selected branch.</summary>
        [Fact]
        public void ConditionalTaskDiscardRemainsConcurrent() {
            string emitted = Emit("_ = true ? Next() : Next(); _ = Next(); return 2;", "int", "System.Threading.Tasks.Task<int>", true);
            Assert.DoesNotContain("await", emitted);
            Execute(emitted, "concurrent", 2);
        }

        /// <summary>An explicit await remains a sequencing boundary even when its value is discarded.</summary>
        [Fact]
        public void ExplicitAwaitIsPreserved() {
            string emitted = Emit("_ = await Next(); return 1;", "async System.Threading.Tasks.Task<int>", "System.Threading.Tasks.Task<int>", true);
            Assert.Contains("await", emitted);
            Assert.DoesNotContain("await await", emitted);
            Execute(emitted, "awaited", 1);
        }

        /// <summary>Returning a C# Task forwards its promise without inserting an await at the call site.</summary>
        [Fact]
        public void TaskReturnPreservesPromise() {
            string emitted = Emit("return Next();", "System.Threading.Tasks.Task<int>", "System.Threading.Tasks.Task<int>", true);
            Assert.DoesNotContain("await", emitted);
            Execute(emitted, "awaited", 1);
        }

        /// <summary>A synchronous C# operation translated to an asynchronous browser API must still finish before control advances.</summary>
        [Fact]
        public void ConvertedSynchronousOperationStillWaits() {
            string emitted = Emit("_ = Next(); return 1;", "int", "int", true);
            Assert.Contains("await", emitted);
            Execute(emitted, "awaited", 1);
        }

        /// <summary>An implicitly awaited nullable result retains its value-type metadata during member emission.</summary>
        [Fact]
        public void ConvertedNullableResultRetainsValueAccess() {
            string emitted = Emit("return Next().Value;", "int", "int?", true);
            Assert.DoesNotContain(".Value", emitted);
            Assert.Contains("await", emitted);
            Execute(emitted, "awaited", 1);
        }

        /// <summary>Configuring framework Tasks evaluates without consuming or serializing their completion.</summary>
        /// <param name="taskType">Source Task or ValueTask shape.</param>
        [Theory]
        [InlineData("System.Threading.Tasks.Task<int>")]
        [InlineData("System.Threading.Tasks.Task")]
        [InlineData("System.Threading.Tasks.ValueTask<int>")]
        [InlineData("System.Threading.Tasks.ValueTask")]
        public void ConfigureAwaitPreservesTaskConcurrency(string taskType) {
            string emitted = Emit("_ = Next().ConfigureAwait(false); _ = Next().ConfigureAwait(true); return 2;", "int", taskType, true);
            Assert.DoesNotContain(".ConfigureAwait", emitted);
            Assert.DoesNotContain("await ", emitted);
            Execute(emitted, "concurrent", 2);
        }

        /// <summary>Explicit configured awaits retain their completion boundary for either context flag.</summary>
        /// <param name="capture">Boolean context flag supplied by the source.</param>
        [Theory]
        [InlineData("false")]
        [InlineData("true")]
        public void ConfiguredAwaitStillWaits(string capture) {
            string emitted = Emit("return await Next().ConfigureAwait(" + capture + ");", "async System.Threading.Tasks.Task<int>", "System.Threading.Tasks.Task<int>", true);
            Assert.DoesNotContain(".ConfigureAwait", emitted);
            Execute(emitted, "awaited", 1);
        }

        /// <summary>The ignored synchronization flag is still evaluated once after the task receiver.</summary>
        [Fact]
        public void ConfigureAwaitEvaluatesContextArgument() {
            string emitted = Emit("_ = Next().ConfigureAwait(Capture()); return 1;", "int", "System.Threading.Tasks.Task<int>", true);
            Execute(emitted, "configured", 1);
        }

        /// <summary>Framework synchronous result consumption becomes an await while preserving its result and error boundary.</summary>
        /// <param name="taskType">Task or ValueTask declaration.</param>
        /// <param name="body">Source result-consumption statements.</param>
        [Theory]
        [InlineData("System.Threading.Tasks.Task<int>", "return Next().GetAwaiter().GetResult();")]
        [InlineData("System.Threading.Tasks.ValueTask<int>", "return Next().GetAwaiter().GetResult();")]
        [InlineData("System.Threading.Tasks.Task", "Next().GetAwaiter().GetResult(); return 1;")]
        [InlineData("System.Threading.Tasks.ValueTask", "Next().GetAwaiter().GetResult(); return 1;")]
        [InlineData("System.Threading.Tasks.Task<int>", "return Next().ConfigureAwait(false).GetAwaiter().GetResult();")]
        [InlineData("System.Threading.Tasks.ValueTask<int>", "return Next().ConfigureAwait(true).GetAwaiter().GetResult();")]
        public void FrameworkAwaiterPreservesCompletion(string taskType, string body) {
            string emitted = Emit(body, "int", taskType, true);
            Assert.DoesNotContain(".GetAwaiter", emitted);
            Assert.DoesNotContain(".GetResult", emitted);
            Execute(emitted, "awaited", 1);
            Execute(emitted, "rejected", 1);
        }

        /// <summary>User methods sharing framework operation names keep their ordinary calls.</summary>
        [Fact]
        public void UserConfigureAwaitRemainsOrdinaryCall() {
            string emitted = Emit("return this.ConfigureAwait(false);", "int", "int", false);
            Assert.Contains(".ConfigureAwait", emitted);
            Execute(emitted, "scalar", 7);
        }

        /// <summary>Out-variable work finishes within its operand and stays visible after configured calls.</summary>
        /// <param name="body">Configured invocation with an out variable in its receiver or context argument.</param>
        /// <param name="methodType">Source return type, including an async modifier when needed.</param>
        /// <param name="mode">Expected task-consumption behavior.</param>
        [Theory]
        [InlineData("_ = Next().ConfigureAwait(TryCapture(out int captured)); return captured;", "int", "configured")]
        [InlineData("_ = NextWithOut(out int captured).ConfigureAwait(Capture()); return captured;", "int", "configured")]
        [InlineData("await Next().ConfigureAwait(TryCapture(out int captured)); return captured;", "async System.Threading.Tasks.Task<int>", "awaited")]
        public void ConfiguredOperandsPreserveOutVariables(string body, string methodType, string mode) {
            string emitted = Emit(body, methodType, "System.Threading.Tasks.Task<int>", true);
            Execute(emitted, mode, 1);
        }

        /// <summary>A null Task remains invalid when configuring or acquiring its framework awaiter.</summary>
        /// <param name="body">Operation that must reject a null task.</param>
        [Theory]
        [InlineData("_ = Next().ConfigureAwait(Capture()); return 1;")]
        [InlineData("return Next().GetAwaiter().GetResult();")]
        public void NullTaskRemainsInvalid(string body) {
            string emitted = Emit(body, "int", "System.Threading.Tasks.Task<int>", true);
            Execute(emitted, "nulltask", 1);
        }

        /// <summary>Complete method emission preserves source Task metadata and produces one valid Promise signature.</summary>
        /// <param name="generic">Whether the source task carries a generic result.</param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ForwardedTaskSignatureRemainsValid(bool generic) {
            string task = generic ? "System.Threading.Tasks.Task<int>" : "System.Threading.Tasks.Task";
            var compilation = RoslynTestHelper.CreateCompilation("class C { " + task + " Next() => default; " + task + " M() { return Next(); } }");
            var method = RoslynTestHelper.GetMethodByName(compilation.Root, "M");
            var harness = TsProcessorTestHarness.Create();
            harness.Program.TypeMap["int"] = "number";
            var result = new VariableType(VariableDataType.Object, "Task");
            if (generic) { result.GenericArgs.Add(VariableUtil.GetVarType("int")); }
            var function = new ConversionFunction { Name = "M", Remap = "M", ReturnType = result, RawBlock = method.Body, AsyncAnalyzed = true };
            var cl = new ConversionClass { Name = "C", Semantic = compilation.Model };
            cl.Functions.Add(function);
            cl.Functions.Add(new ConversionFunction { Name = "Next", Remap = "Next", ReturnType = result, IsAsync = true, AsyncAnalyzed = true });
            harness.Program.Classes.Add(cl);
            var emitter = new TypeScriptClassEmitter(harness.Processor, harness.Program, harness.Program,
                new TypeScriptConversionOptions(), new cs2.ts.util.TypeScriptReflectionImportTracker(), null);
            using var output = new StringWriter();
            var emitFunctions = typeof(TypeScriptClassEmitter).GetMethod("EmitFunctions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(emitFunctions);
            emitFunctions.Invoke(emitter, new object[] { cl, new cs2.ts.util.TypeScriptOutputWriter(output) });
            Assert.Contains(generic ? "M(): Promise<number>" : "M(): Promise<void>", output.ToString());
            Assert.DoesNotContain("Promise<Task>", output.ToString());
            Assert.Equal("Task", function.ReturnType.TypeName);
            Assert.Equal(generic ? 1 : 0, function.ReturnType.GenericArgs.Count);
        }

        /// <summary>Formatting resolves task layers while keeping generic payload arguments and source descriptors intact.</summary>
        [Fact]
        public void AsyncResultPreservesGenericPayload() {
            var harness = TsProcessorTestHarness.Create();
            var payload = new VariableType(VariableDataType.Object, "Array");
            var item = new VariableType(VariableDataType.Object, "T") { IsGenericParameter = true };
            payload.GenericArgs.Add(item);
            var task = new VariableType(VariableDataType.Object, "Task");
            task.GenericArgs.Add(payload);
            Assert.Equal("Promise<Array<T>>", task.ToTypeScriptAsyncReturnString(harness.Program));
            Assert.Equal("Task", task.TypeName);
            Assert.Same(payload, task.GenericArgs[0]);
            Assert.Equal("Promise<Awaited<T>>", item.ToTypeScriptAsyncReturnString(harness.Program));
            var nested = new VariableType(VariableDataType.Object, "Task");
            nested.GenericArgs.Add(task);
            Assert.Equal("Promise<Array<T>>", nested.ToTypeScriptAsyncReturnString(harness.Program));
        }

        /// <summary>Emits a complete method body with the same async method metadata used by converted application code.</summary>
        /// <param name="body">Source statements to translate.</param>
        /// <param name="methodType">Source return type and optional async modifier.</param>
        /// <param name="calleeType">Actual C# return type of the operation.</param>
        /// <param name="convertedAsync">Whether the converted operation returns a Promise.</param>
        /// <param name="underscoreField">Whether the fixture declares a genuine field named underscore.</param>
        /// <returns>Generated statements with real before/after-expression handling.</returns>
        static string Emit(string body, string methodType, string calleeType, bool convertedAsync, bool underscoreField = false) {
            var compilation = RoslynTestHelper.CreateCompilation("class C { " + (underscoreField ? "int _; " : string.Empty) + "bool Capture() => true; bool TryCapture(out int captured) { captured = 1; return true; } int ConfigureAwait(bool capture) => 7; " + calleeType + " NextWithOut(out int captured) { captured = 1; return default; } " + calleeType + " Next() => default; " + methodType + " M() { " + body + " } }");
            Assert.DoesNotContain(compilation.Compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var method = RoslynTestHelper.GetMethodByName(compilation.Root, "M");
            Assert.NotNull(method.Body);
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, returnType: VariableUtil.GetVarType("int"));
            var currentClass = harness.Context.GetCurrentClass();
            Assert.NotNull(currentClass);
            if (underscoreField) { currentClass.Variables.Add(new ConversionVariable { Name = "_", VarType = VariableUtil.GetVarType("int") }); }
            var calleeSymbol = compilation.Model.GetDeclaredSymbol(RoslynTestHelper.GetMethodByName(compilation.Root, "Next")) as IMethodSymbol;
            Assert.NotNull(calleeSymbol);
            var calleeResult = VariableUtil.GetVarType("int");
            calleeResult.IsNullable = calleeSymbol.ReturnType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
            currentClass.Functions.Add(new ConversionFunction { Name = "Next", ReturnType = calleeResult, IsAsync = convertedAsync, AsyncAnalyzed = true });
            currentClass.Functions.Add(new ConversionFunction { Name = "NextWithOut", ReturnType = calleeResult, IsAsync = convertedAsync, AsyncAnalyzed = true });
            currentClass.Functions.Add(new ConversionFunction { Name = "TryCapture", ReturnType = VariableUtil.GetVarType("bool"), AsyncAnalyzed = true });
            currentClass.Functions.Add(new ConversionFunction { Name = "Capture", ReturnType = VariableUtil.GetVarType("bool"), AsyncAnalyzed = true });
            currentClass.Functions.Add(new ConversionFunction { Name = "ConfigureAwait", ReturnType = VariableUtil.GetVarType("int"), AsyncAnalyzed = true });
            var lines = new List<string>();
            harness.Processor.ProcessBlock(compilation.Model, harness.Context, method.Body, lines, depth: 0);
            return string.Concat(lines);
        }

        /// <summary>Executes strict generated JavaScript and checks ordering before resolving controlled pending Promises.</summary>
        /// <param name="body">Emitted method statements.</param>
        /// <param name="mode">Scalar, concurrent Task, or required-await scenario.</param>
        /// <param name="expected">Expected completed method result.</param>
        static void Execute(string body, string mode, int expected) {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "cs2.ts", ".net.ts"));
            var start = new ProcessStartInfo("node") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-e");
            start.ArgumentList.Add("const ts = require(require('node:path').join(process.argv[1], 'node_modules/typescript')); const body = process.argv[2], mode = process.argv[3], expected = Number(process.argv[4]); const source = '(' + (body.includes('await ') ? 'async ' : '') + 'function(){' + String.fromCharCode(34) + 'use strict' + String.fromCharCode(34) + ';' + body + '})'; const run = require('node:vm').runInNewContext(ts.transpileModule(source, {compilerOptions:{target:ts.ScriptTarget.ES2020}}).outputText); const failure = new Error('task failed'); let calls = 0, captures = 0; const releases = []; const receiver = { NextWithOut(box) { box.value = 1; return this.Next(); }, TryCapture(box) { box.value = 1; return this.Capture(); }, ConfigureAwait() { return 7; }, Capture() { if (calls !== 1) throw new Error('Context evaluated before task'); captures++; return true; }, Next() { calls++; return mode === 'nulltask' ? null : mode === 'scalar' ? calls : new Promise((resolve, reject) => releases.push(mode === 'rejected' ? () => reject(failure) : resolve)); } }; (async () => { const result = mode === 'nulltask' ? Promise.resolve().then(() => run.call(receiver)) : run.call(receiver); if (mode === 'configured' && (calls !== 1 || captures !== 1 || result !== 1)) throw new Error('Context argument was dropped or awaited'); if (mode === 'concurrent' && (calls !== 2 || result !== 2)) throw new Error('Discarded tasks became sequential'); if (mode === 'awaited' || mode === 'rejected') { if (calls !== 1 || !result || typeof result.then !== 'function') throw new Error('Required await was removed'); let complete = false; result.then(() => complete = true, () => complete = true); await Promise.resolve(); if (complete) throw new Error('Advanced before operation completed'); } for (const release of releases) release(1); if (mode === 'nulltask') { await result.then(() => { throw new Error('Null task was accepted'); }, error => { if (error.name !== 'TypeError') throw error; }); if (calls !== 1) throw new Error('Null task evaluated incorrectly'); } else if (mode === 'rejected') { await result.then(() => { throw new Error('Task failure swallowed'); }, error => { if (error !== failure) throw new Error('Task failure replaced'); }); } else if (await result !== expected) throw new Error('Wrong expression result'); })().catch(error => { process.stderr.write(String(error)); process.exitCode = 1; });");
            start.ArgumentList.Add(root);
            start.ArgumentList.Add(body);
            start.ArgumentList.Add(mode);
            start.ArgumentList.Add(expected.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var process = Process.Start(start);
            if (process == null) { throw new InvalidOperationException("Could not start discard runtime verification."); }
            using var ownedProcess = process;
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, error + output);
        }
    }
}
