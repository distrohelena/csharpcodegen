using System.Text.Json;
using cs2.cpp;

namespace cs2.cpp.tests {
    /// <summary>
    /// Verifies that C# array references lower into the shared C++ array runtime contract.
    /// </summary>
    public class CPPArrayMappingAuditTests {
        /// <summary>
        /// Ensures byte-array members and return values emit the runtime array include instead of a raw source-shaped header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithByteArrayMember_EmitsSharedArrayRuntimeInclude() {
            string source = """
                public class BufferHolder {
                    byte[] Data;

                    public byte[] Read() {
                        return Data;
                    }
                }
                """;

            string outputPath = RunConversion(source, out string output, out JsonDocument report);

            AssertNoDiagnostic(report, "ArrayType");
            Assert.DoesNotContain("\"byte[].hpp\"", output);
            Assert.Contains("\"runtime/array.hpp\"", output);
            Assert.True(File.Exists(Path.Combine(outputPath, "runtime", "array.hpp")));
        }

        /// <summary>
        /// Ensures lowered array-backed properties do not reintroduce a fake generated Array header include.
        /// </summary>
        [Fact]
        public void WriteOutput_WithArrayProperty_DoesNotEmitSyntheticArrayHeader() {
            string source = """
                using System;
                using System.Collections.Generic;

                public interface IContentProcessor {
                    Type OutputType { get; }
                }

                public class ContentProcessorRegistration {
                    readonly string[] ExtensionsValue;
                    readonly IContentProcessor ProcessorValue;

                    public ContentProcessorRegistration(IContentProcessor processor, IReadOnlyList<string> extensions) {
                        ProcessorValue = processor;
                        ExtensionsValue = extensions == null ? Array.Empty<string>() : NormalizeExtensions(extensions);
                    }

                    public IReadOnlyList<string> Extensions => ExtensionsValue;

                    string[] NormalizeExtensions(IReadOnlyList<string> sourceExtensions) {
                        string[] normalized = new string[sourceExtensions.Count];
                        return normalized;
                    }
                }
                """;

            string outputPath = RunConversion(source, out string output, out JsonDocument report);
            string header = File.ReadAllText(Path.Combine(outputPath, "ContentProcessorRegistration.hpp"));

            AssertNoDiagnostic(report, "ArrayType");
            Assert.Contains("#include \"runtime/array.hpp\"", header);
            Assert.DoesNotContain("#include \"Array.hpp\"", header, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(outputPath, "runtime", "array.hpp")));
        }

        /// <summary>
        /// Runs the C++ converter against a temporary single-file project and returns the output folder and concatenated generated text.
        /// </summary>
        /// <param name="source">C# source file content to convert.</param>
        /// <param name="output">Concatenated generated textual output.</param>
        /// <param name="report">Parsed conversion report produced by the converter.</param>
        /// <returns>The generated output folder.</returns>
        static string RunConversion(string source, out string output, out JsonDocument report) {
            string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-array-tests", Guid.NewGuid().ToString("N"));
            string projectPath = Path.Combine(rootPath, "Fixture.csproj");
            string sourcePath = Path.Combine(rootPath, "Fixture.cs");
            string outputPath = Path.Combine(rootPath, "out");

            Directory.CreateDirectory(rootPath);
            File.WriteAllText(projectPath, CreateProjectFile());
            File.WriteAllText(sourcePath, source);

            CPPConversionOptions options = CPPConversionOptions.CreateDefault();
            options.LoadNativeRuntimeMetadata = false;
            options.WriteConversionReport = true;

            CPPConversionRules rules = new CPPConversionRules();
            CPPCodeConverter converter = new CPPCodeConverter(rules, options);
            converter.AddCsproj(projectPath);
            converter.WriteOutput(outputPath);

            string reportPath = Path.Combine(outputPath, "cpp-conversion-report.json");
            report = JsonDocument.Parse(File.ReadAllText(reportPath));
            output = ReadGeneratedOutput(outputPath);
            return outputPath;
        }

        /// <summary>
        /// Creates a minimal SDK-style project file for temporary converter fixtures.
        /// </summary>
        /// <returns>Project file content suitable for Roslyn-based analysis.</returns>
        static string CreateProjectFile() {
            return """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net9.0</TargetFramework>
                    <LangVersion>preview</LangVersion>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>disable</Nullable>
                  </PropertyGroup>
                </Project>
                """;
        }

        /// <summary>
        /// Reads all generated headers and sources from a converter output directory into a single string for assertions.
        /// </summary>
        /// <param name="outputPath">Converter output directory to inspect.</param>
        /// <returns>Concatenated generated text content.</returns>
        static string ReadGeneratedOutput(string outputPath) {
            string[] files = Directory.GetFiles(outputPath, "*.*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            return string.Join("\n", files.Select(File.ReadAllText));
        }

        /// <summary>
        /// Asserts that the conversion report contains no diagnostic entries for the supplied syntax kind.
        /// </summary>
        /// <param name="report">Parsed conversion report to inspect.</param>
        /// <param name="syntaxKind">Roslyn syntax kind that should be absent from the report.</param>
        static void AssertNoDiagnostic(JsonDocument report, string syntaxKind) {
            foreach (JsonElement diagnostic in report.RootElement.GetProperty("diagnostics").EnumerateArray()) {
                string actualSyntaxKind = diagnostic.GetProperty("syntaxKind").GetString() ?? string.Empty;
                Assert.NotEqual(syntaxKind, actualSyntaxKind);
            }
        }
    }
}
