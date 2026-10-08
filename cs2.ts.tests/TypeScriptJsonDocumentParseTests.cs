using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using cs2.core;
using cs2.ts;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Verifies that JsonDocument string parsing retains its framework method identity.</summary>
    public sealed class TypeScriptJsonDocumentParseTests {
        /// <summary>Maps the one-argument string overload to Parse while preserving the converter's explicit default options argument.</summary>
        [Fact]
        public void FrameworkStringParseUsesParseWithoutOptions() {
            Assert.Equal("JsonDocument.Parse(json, null)", Emit("using System.Text.Json; class C { JsonDocument M(string json) => JsonDocument.Parse(json); }"));
        }

        /// <summary>Preserves an explicit JsonDocumentOptions expression when parsing strings.</summary>
        [Fact]
        public void FrameworkStringParsePreservesOptions() {
            Assert.Equal("JsonDocument.Parse(json, options)", Emit("using System.Text.Json; class C { JsonDocument M(string json, JsonDocumentOptions options) => JsonDocument.Parse(json, options); }"));
        }

        /// <summary>Matches MeshManager's string guard and try/catch shape, where Parse must not resolve to reader-only ParseValue.</summary>
        [Fact]
        public void FrameworkStringParseInMeshManagerShapeUsesParse() {
            const string source = "using System; using System.Text.Json; class C { public static bool IsReportExpired(string reportJson, long nowUnixSeconds) { if (string.IsNullOrWhiteSpace(reportJson)) return true; try { using JsonDocument document = JsonDocument.Parse(reportJson); return document.RootElement.ValueKind != JsonValueKind.Object; } catch (JsonException) { return true; } } }";
            Assert.Equal("JsonDocument.Parse(reportJson, null)", Emit(source, includeJsonDocumentRuntime: true));
        }

        /// <summary>Builds framework references explicitly so the fixture resolves the selected System.Text.Json overload.</summary>
        static string Emit(string source, bool includeJsonDocumentRuntime = false) {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
            string trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
                ?? throw new InvalidOperationException("Trusted platform assembly paths are unavailable.");
            string runtimeAssembly = trustedPlatformAssemblies
                .Split(System.IO.Path.PathSeparator)
                .Single(path => string.Equals(System.IO.Path.GetFileName(path), "System.Runtime.dll", StringComparison.OrdinalIgnoreCase));
            string memoryAssembly = trustedPlatformAssemblies
                .Split(System.IO.Path.PathSeparator)
                .Single(path => string.Equals(System.IO.Path.GetFileName(path), "System.Memory.dll", StringComparison.OrdinalIgnoreCase));
            var compilation = CSharpCompilation.Create(
                assemblyName: "JsonDocumentParseFixture",
                syntaxTrees: new[] { syntaxTree },
                references: new[] {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(JsonDocument).Assembly.Location),
                    MetadataReference.CreateFromFile(runtimeAssembly),
                    MetadataReference.CreateFromFile(memoryAssembly)
                },
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var model = compilation.GetSemanticModel(syntaxTree);
            var invocation = syntaxTree.GetCompilationUnitRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single(candidate => model.GetSymbolInfo(candidate).Symbol is IMethodSymbol method && method.Name == "Parse" && method.ContainingType?.ToDisplayString() == "System.Text.Json.JsonDocument");
            var harness = TsProcessorTestHarness.Create();
            if (includeJsonDocumentRuntime) {
                RegisterJsonDocumentRuntime(harness.Program);
            }
            var nativeRemap = typeof(TypeScriptProgram).GetMethod("buildNativeRemap", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(nativeRemap);
            nativeRemap.Invoke(harness.Program, null);
            TsProcessorTestHarness.PushClassAndFunction(harness.Context);
            List<string> lines = new List<string>();
            harness.Processor.ProcessExpression(model, harness.Context, invocation, lines);
            return TsProcessorTestHarness.JoinLines(lines).Trim();
        }

        static void RegisterJsonDocumentRuntime(TypeScriptProgram program) {
            var runtime = new ConversionClass { Name = "JsonDocument" };
            runtime.Functions.Add(new ConversionFunction {
                Name = "Parse",
                IsStatic = true,
                InParameters = new List<ConversionVariable> {
                    new ConversionVariable { Name = "json", VarType = VariableUtil.GetVarType("string") },
                    new ConversionVariable { Name = "options", VarType = VariableUtil.GetVarType("JsonDocumentOptions") }
                }
            });
            runtime.Functions.Add(new ConversionFunction {
                Name = "ParseValue",
                IsStatic = true,
                InParameters = new List<ConversionVariable> {
                    new ConversionVariable { Name = "reader", VarType = VariableUtil.GetVarType("Utf8JsonReader") }
                }
            });
            program.RegisterClass(runtime);
        }
    }
}
