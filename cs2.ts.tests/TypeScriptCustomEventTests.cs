using System.IO;
using System.Linq;
using System.Reflection;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>
    /// Verifies custom C# event accessors and delegate combination in generated TypeScript.
    /// </summary>
    public sealed class TypeScriptCustomEventTests {
        /// <summary>
        /// Emits add and remove accessors as Event hooks while preserving multicast delegate operations inside them.
        /// </summary>
        [Fact]
        public void CustomEventAccessorsBecomeEventHooks() {
            const string source = "using System; interface IEvents { event Action Changed; } class Source { public event Action Changed; } class Adapter : IEvents { Source source; Action subscribers; public event Action Changed { add { source.Changed += value; subscribers += value; } remove { source.Changed -= value; subscribers -= value; } } }";
            var compilation = RoslynTestHelper.CreateCompilation(source, "custom-event.cs");
            Assert.DoesNotContain(compilation.Compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var program = new TypeScriptProgram(new ConversionRules());
            var context = new ConversionContext(program);
            foreach (var member in compilation.Root.Members) {
                ConversionPreProcessor.PreProcessExpression(compilation.Model, context, member);
            }

            ConversionClass adapter = Assert.Single(program.Classes.Where(candidate => candidate.Name == "Adapter"));
            string output = EmitVariables(program, adapter);

            Assert.Contains("Changed: Event = new Event(", output);
            Assert.Contains("this.source.Changed.Add(value)", output);
            Assert.Contains("this.source.Changed.Remove(value)", output);
            Assert.True(output.Contains("this.subscribers = NativeDelegateUtil.combine(this.subscribers, value)"), output);
            Assert.Contains("this.subscribers = NativeDelegateUtil.remove(this.subscribers, value)", output);
        }

        /// <summary>
        /// Emits converted variables through the production class emitter.
        /// </summary>
        /// <param name="program">Program containing the converted class.</param>
        /// <param name="conversionClass">Class whose variables should be emitted.</param>
        /// <returns>Generated TypeScript variables.</returns>
        static string EmitVariables(TypeScriptProgram program, ConversionClass conversionClass) {
            var processor = new TypeScriptConversiorProcessor();
            var emitter = new TypeScriptClassEmitter(processor, program, program,
                new TypeScriptConversionOptions(), new cs2.ts.util.TypeScriptReflectionImportTracker(), null);
            using var output = new StringWriter();
            MethodInfo emitVariables = typeof(TypeScriptClassEmitter).GetMethod("EmitVariables", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(emitVariables);
            emitVariables.Invoke(emitter, new object[] { conversionClass, new cs2.ts.util.TypeScriptOutputWriter(output) });
            return output.ToString();
        }
    }
}
