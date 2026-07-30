using System.Text.Json;
using cs2.cpp;

namespace cs2.cpp.tests {
    /// <summary>
    /// Verifies that the C++ converter preserves checked-context block bodies without leaving unsupported diagnostics behind.
    /// </summary>
    public class CPPUncheckedStatementAuditTests {
        /// <summary>
        /// Ensures an unchecked hash-code block lowers by emitting only the enclosed statements.
        /// </summary>
        [Fact]
        public void WriteOutput_WithUncheckedBlock_DoesNotReportUncheckedStatement() {
            string source = """
                public class HashCarrier {
                    public int GetHashCode(int value, int other) {
                        unchecked {
                            var hashCode = value;
                            hashCode = (hashCode * 397) ^ other;
                            return hashCode;
                        }
                    }
                }
                """;

            string output = RunConversion(source, out JsonDocument report);

            AssertNoDiagnostic(report, "UncheckedStatement");
            Assert.Contains("hashCode = (hashCode * 397) ^ other;", output);
            Assert.Contains("return hashCode;", output);
        }

        /// <summary>
        /// Ensures a checked counter block routes its mutation through an overflow-checking native helper.
        /// </summary>
        [Fact]
        public void WriteOutput_WithCheckedBlock_DoesNotReportCheckedStatement() {
            string source = """
                public class Counter {
                    int value;

                    public void Increment() {
                        checked {
                            value++;
                        }
                    }
                }
                """;

            string output = RunConversion(source, out JsonDocument report);

            AssertNoDiagnostic(report, "CheckedStatement");
            Assert.False(report.RootElement.GetProperty("hasErrors").GetBoolean());
            Assert.Contains("Number::CheckedPostIncrement(this->value);", output);
            Assert.Contains("static T CheckedPostIncrement(T& value)", output);
            Assert.Contains("if (value == std::numeric_limits<T>::max())", output);
            Assert.Contains("throw OverflowException();", output);
            Assert.DoesNotContain("this->value++;", output, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures unsupported checked arithmetic remains an explicit conversion error instead of silently using ordinary C++ overflow behavior.
        /// </summary>
        [Fact]
        public void WriteOutput_WithUnsupportedCheckedAddition_ReportsCheckedArithmeticDiagnostic() {
            string source = """
                public class Counter {
                    public int Add(int value, int increment) {
                        checked {
                            return value + increment;
                        }
                    }
                }
                """;

            RunConversion(source, out JsonDocument report);

            Assert.True(report.RootElement.GetProperty("hasErrors").GetBoolean());
            AssertDiagnostic(report, "AddExpression");
        }

        /// <summary>
        /// Runs the C++ converter against a temporary single-file project and returns all generated textual output.
        /// </summary>
        /// <param name="source">C# source file content to convert.</param>
        /// <param name="report">Parsed conversion report produced by the converter.</param>
        /// <returns>Concatenated generated file contents.</returns>
        static string RunConversion(string source, out JsonDocument report) {
            string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-unchecked-tests", Guid.NewGuid().ToString("N"));
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
            return ReadGeneratedOutput(outputPath);
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
        /// Asserts that the conversion report contains at least one error for the supplied checked arithmetic syntax.
        /// </summary>
        /// <param name="report">Parsed conversion report to inspect.</param>
        /// <param name="syntaxKind">Roslyn syntax kind that must be reported.</param>
        static void AssertDiagnostic(JsonDocument report, string syntaxKind) {
            foreach (JsonElement diagnostic in report.RootElement.GetProperty("diagnostics").EnumerateArray()) {
                string actualSyntaxKind = diagnostic.GetProperty("syntaxKind").GetString() ?? string.Empty;
                if (string.Equals(actualSyntaxKind, syntaxKind, StringComparison.Ordinal)) {
                    Assert.Equal("Error", diagnostic.GetProperty("severity").GetString());
                    return;
                }
            }

            Assert.Fail($"Expected a conversion diagnostic for checked arithmetic syntax '{syntaxKind}'.");
        }
    }
}
