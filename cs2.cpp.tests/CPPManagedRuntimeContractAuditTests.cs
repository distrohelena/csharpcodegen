using System.Text.Json;
using cs2.cpp;

namespace cs2.cpp.tests {
    /// <summary>
    /// Covers runtime-backed managed contract mappings required for native compile validation.
    /// </summary>
    public class CPPManagedRuntimeContractAuditTests {
        /// <summary>
        /// Ensures IDisposable resolves to a runtime header instead of a missing generated header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithDisposableBase_UsesRuntimeContractHeader() {
            string source = """
                using System;

                public class Entity : IDisposable {
                    public void Dispose() {
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string entityHeader = File.ReadAllText(Path.Combine(output.OutputPath, "Entity.hpp"));

            Assert.Contains("#include \"runtime/native_disposable.hpp\"", entityHeader);
            Assert.DoesNotContain("#include \"IDisposable.hpp\"", entityHeader, StringComparison.Ordinal);
            Assert.Contains("class Entity : public IDisposable", entityHeader);
            AssertRuntimeRequirement(output.Report, "NativeDisposable");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "runtime", "native_disposable.hpp")));
        }

        /// <summary>
        /// Ensures IEquatable resolves to a runtime header instead of a missing generated header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithEquatableBase_UsesRuntimeContractHeader() {
            string source = """
                using System;

                public class Vector3 : IEquatable<Vector3> {
                    public bool Equals(Vector3 other) {
                        return true;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string vectorHeader = File.ReadAllText(Path.Combine(output.OutputPath, "Vector3.hpp"));

            Assert.Contains("#include \"runtime/native_equatable.hpp\"", vectorHeader);
            Assert.DoesNotContain("#include \"IEquatable.hpp\"", vectorHeader, StringComparison.Ordinal);
            Assert.Contains("class Vector3 : public IEquatable", vectorHeader);
            AssertRuntimeRequirement(output.Report, "NativeEquatable");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "runtime", "native_equatable.hpp")));
        }

        /// <summary>
        /// Ensures System.IO.Stream resolves to the built-in runtime header instead of a missing generated header.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSystemIoStreamParameter_UsesRuntimeStreamHeader() {
            string source = """
                using System.IO;

                public interface IContentProcessor<T> {
                    T Read(Stream stream);
                }
                """;

            ConversionOutput output = RunConversion(source);
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "IContentProcessor.hpp"));

            Assert.Contains("#include \"system/io/stream.hpp\"", header);
            Assert.DoesNotContain("#include \"Stream.hpp\"", header, StringComparison.Ordinal);
            AssertRuntimeRequirement(output.Report, "Stream");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "system", "io", "stream.hpp")));
        }

        /// <summary>
        /// Runs the C++ converter against a temporary single-file project and returns the generated output bundle.
        /// </summary>
        /// <param name="source">C# source file content to convert.</param>
        /// <returns>Output folder, parsed report, and generated textual output.</returns>
        static ConversionOutput RunConversion(string source) {
            string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-runtime-contract-tests", Guid.NewGuid().ToString("N"));
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
            JsonDocument report = JsonDocument.Parse(File.ReadAllText(reportPath));
            string generatedText = ReadGeneratedOutput(outputPath);
            return new ConversionOutput(outputPath, generatedText, report);
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
        /// Ensures the conversion report registered the expected runtime requirement.
        /// </summary>
        /// <param name="report">Parsed conversion report.</param>
        /// <param name="requirementName">Stable runtime requirement name that must be present.</param>
        static void AssertRuntimeRequirement(JsonDocument report, string requirementName) {
            foreach (JsonElement requirement in report.RootElement.GetProperty("registeredRuntimeRequirements").EnumerateArray()) {
                if (string.Equals(requirement.GetString(), requirementName, StringComparison.Ordinal)) {
                    return;
                }
            }

            Assert.Fail($"Expected runtime requirement '{requirementName}' to be registered.");
        }

        /// <summary>
        /// Represents the generated output artifacts captured for a managed runtime contract fixture.
        /// </summary>
        /// <param name="OutputPath">Generated output directory.</param>
        /// <param name="GeneratedText">Concatenated generated textual output.</param>
        /// <param name="Report">Parsed conversion report.</param>
        record ConversionOutput(string OutputPath, string GeneratedText, JsonDocument Report);
    }
}
