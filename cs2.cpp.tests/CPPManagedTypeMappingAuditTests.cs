using System.Text.Json;
using cs2.cpp;

namespace cs2.cpp.tests {
    /// <summary>
    /// Covers managed type mapping regressions that surfaced during native compile validation of generated C++ output.
    /// </summary>
    public class CPPManagedTypeMappingAuditTests {
        /// <summary>
        /// Ensures the C++ backend maps core managed aliases and containers to native/runtime-backed C++ types instead of leaking raw C# names.
        /// </summary>
        [Fact]
        public void WriteOutput_WithManagedAliasesAndContainers_UsesNativeCppTypeMappings() {
            string source = """
                using System.Collections.Generic;

                public class ManagedMapGate {
                    public bool Enabled;
                    public string Name;
                    public List<string> Labels;
                    public IReadOnlyList<string> LabelView;
                    public Dictionary<string, byte[]> Buffers;
                    public IReadOnlyDictionary<string, byte[]> BufferView;

                    public string GetName() {
                        return this.Name;
                    }

                    public void SetData(List<string> labels, IReadOnlyList<string> labelView, Dictionary<string, byte[]> buffers, IReadOnlyDictionary<string, byte[]> bufferView, UInt16 count, UInt32 mask, Single scale, byte kind) {
                        this.Labels = labels;
                        this.LabelView = labelView;
                        this.Buffers = buffers;
                        this.BufferView = bufferView;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);

            Assert.Contains("std::string", output.GeneratedText);
            Assert.Contains("List<std::string>*", output.GeneratedText);
            Assert.Contains("Dictionary<std::string, Array<uint8_t>*>*", output.GeneratedText);
            Assert.Contains("bool Enabled;", output.GeneratedText);
            Assert.Contains("uint16_t", output.GeneratedText);
            Assert.Contains("uint32_t", output.GeneratedText);
            Assert.Contains("float", output.GeneratedText);
            Assert.Contains("uint8_t", output.GeneratedText);
            Assert.DoesNotContain("\n    string Name;", output.GeneratedText, StringComparison.Ordinal);
            Assert.DoesNotContain("List<string>", output.GeneratedText, StringComparison.Ordinal);
            Assert.DoesNotContain("IReadOnlyList<string>", output.GeneratedText, StringComparison.Ordinal);
            Assert.DoesNotContain("Dictionary<string, byte[]>", output.GeneratedText, StringComparison.Ordinal);
            Assert.DoesNotContain("IReadOnlyDictionary<string, byte[]>", output.GeneratedText, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Boolean.hpp\"", output.GeneratedText, StringComparison.Ordinal);
            Assert.DoesNotContain("\"UInt16.hpp\"", output.GeneratedText, StringComparison.Ordinal);
            Assert.DoesNotContain("\"string.hpp\"", output.GeneratedText, StringComparison.Ordinal);
            Assert.DoesNotContain("\"IReadOnlyList<string>.hpp\"", output.GeneratedText, StringComparison.Ordinal);
            Assert.DoesNotContain("\"IReadOnlyDictionary<string, byte[]>.hpp\"", output.GeneratedText, StringComparison.Ordinal);

            AssertRegisteredRuntimeRequirement(output.Report, "NativeString");
            AssertRegisteredRuntimeRequirement(output.Report, "NativeList");
            AssertRegisteredRuntimeRequirement(output.Report, "NativeDictionary");
            AssertRegisteredRuntimeRequirement(output.Report, "NativeArray");
        }

        /// <summary>
        /// Ensures pointer-sized managed handles lower to native pointer-sized integers instead of synthetic generated headers.
        /// </summary>
        [Fact]
        public void WriteOutput_WithIntPtrSignature_UsesNativePointerSizedIntegerMapping() {
            string source = """
                using System;

                public class NativeHandleGate {
                    public IntPtr MainHandle;

                    public void SetHandle(IntPtr handle) {
                        this.MainHandle = handle;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);

            Assert.Contains("intptr_t MainHandle;", output.GeneratedText);
            Assert.Contains("void SetHandle(intptr_t handle)", output.GeneratedText);
            Assert.DoesNotContain("IntPtr*", output.GeneratedText, StringComparison.Ordinal);
            Assert.DoesNotContain("\"IntPtr.hpp\"", output.GeneratedText, StringComparison.Ordinal);
        }

        /// <summary>
        /// Runs the C++ converter against a temporary single-file project and returns the generated output bundle.
        /// </summary>
        /// <param name="source">C# source file content to convert.</param>
        /// <returns>Output folder, parsed report, and generated textual output.</returns>
        static ConversionOutput RunConversion(string source) {
            string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-managed-type-mapping-tests", Guid.NewGuid().ToString("N"));
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
        /// Asserts that the conversion report registered the supplied runtime requirement.
        /// </summary>
        /// <param name="report">Parsed conversion report to inspect.</param>
        /// <param name="requirementName">Stable runtime requirement name that should be present.</param>
        static void AssertRegisteredRuntimeRequirement(JsonDocument report, string requirementName) {
            foreach (JsonElement requirement in report.RootElement.GetProperty("registeredRuntimeRequirements").EnumerateArray()) {
                string actualRequirementName = requirement.GetString() ?? string.Empty;
                if (actualRequirementName == requirementName) {
                    return;
                }
            }

            Assert.Fail($"Expected runtime requirement '{requirementName}' to be registered.");
        }

        /// <summary>
        /// Represents the generated output artifacts captured for a managed type mapping fixture.
        /// </summary>
        /// <param name="OutputPath">Generated output directory.</param>
        /// <param name="GeneratedText">Concatenated generated textual output.</param>
        /// <param name="Report">Parsed conversion report.</param>
        record ConversionOutput(string OutputPath, string GeneratedText, JsonDocument Report);
    }
}
