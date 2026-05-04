using System.Text.Json;
using cs2.cpp;

namespace cs2.cpp.tests {
    /// <summary>
    /// Verifies that the C++ converter lowers declaration-pattern is checks without leaving unsupported diagnostics behind.
    /// </summary>
    public class CPPIsPatternAuditTests {
        /// <summary>
        /// Ensures a declaration-pattern guard introduces a typed temporary through the runtime cast helper.
        /// </summary>
        [Fact]
        public void WriteOutput_WithDeclarationPatternGuard_DoesNotReportIsPatternExpression() {
            string source = """
                public class Asset {
                }

                public class TextureAsset : Asset {
                }

                public class AssetWriter {
                    public void Write(Asset asset) {
                        if (asset is TextureAsset textureAsset) {
                            Use(textureAsset);
                        }
                    }

                    void Use(TextureAsset asset) {
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);

            AssertNoDiagnostic(output.Report, "IsPatternExpression");
            Assert.Contains("TextureAsset* textureAsset = he_cpp_try_cast<TextureAsset>(asset);", output.GeneratedText);
            Assert.Contains("if (textureAsset != nullptr)", output.GeneratedText);
            AssertRuntimeRequirement(output.Report, "NativeCast");
            Assert.True(File.Exists(Path.Combine(output.OutputPath, "runtime", "native_cast.hpp")));
        }

        /// <summary>
        /// Ensures interface declaration-pattern guards still lower through the native cast helper and copy the updated runtime template.
        /// </summary>
        [Fact]
        public void WriteOutput_WithInterfaceDeclarationPattern_UsesDynamicCastRuntimeHelper() {
            string source = """
                public interface IAnchorBoundsProvider {
                }

                public class Entity {
                }

                public class AnchorComponent {
                    public void Refresh(Entity current) {
                        if (current is IAnchorBoundsProvider provider) {
                            Use(provider);
                        }
                    }

                    void Use(IAnchorBoundsProvider provider) {
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);

            AssertNoDiagnostic(output.Report, "IsPatternExpression");
            Assert.Contains("IAnchorBoundsProvider* provider = he_cpp_try_cast<IAnchorBoundsProvider>(current);", output.GeneratedText);
            Assert.Contains("if (provider != nullptr)", output.GeneratedText);
            Assert.Contains("dynamic_cast", File.ReadAllText(Path.Combine(output.OutputPath, "runtime", "native_cast.hpp")));
            AssertRuntimeRequirement(output.Report, "NativeCast");
        }

        /// <summary>
        /// Runs the C++ converter against a temporary single-file project and returns all generated textual output.
        /// </summary>
        /// <param name="source">C# source file content to convert.</param>
        /// <param name="report">Parsed conversion report produced by the converter.</param>
        /// <returns>Concatenated generated file contents.</returns>
        static ConversionOutput RunConversion(string source) {
            string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-ispattern-tests", Guid.NewGuid().ToString("N"));
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
            return new ConversionOutput(outputPath, ReadGeneratedOutput(outputPath), report);
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
        /// Captures generated output artifacts for declaration-pattern lowering tests.
        /// </summary>
        /// <param name="OutputPath">Generated converter output directory.</param>
        /// <param name="GeneratedText">Concatenated generated text.</param>
        /// <param name="Report">Parsed conversion report.</param>
        record ConversionOutput(string OutputPath, string GeneratedText, JsonDocument Report);
    }
}
