using System.Text.Json;
using cs2.cpp;

namespace cs2.cpp.tests {
    /// <summary>
    /// Verifies that the C++ converter lowers local functions into local lambdas without leaving unsupported diagnostics behind.
    /// </summary>
    public class CPPLocalFunctionAuditTests {
        /// <summary>
        /// Ensures block-bodied and expression-bodied local functions that capture outer locals lower through capture-by-reference lambdas.
        /// </summary>
        [Fact]
        public void WriteOutput_WithCapturingLocalFunctions_DoesNotReportLocalFunctionStatement() {
            string source = """
                public class Rect {
                    public Rect(int x, int y, int w, int h) {
                    }
                }

                public class Generator {
                    public Rect Build(int pad, int tileW0, int tileW1, int tileH0, int tileH1) {
                        Rect Dst(int row, int col, int w, int h) {
                            int y = pad;
                            for (int r = 0; r < row; r++) {
                                int rh = (r % 3 == 0 || r % 3 == 2) ? tileH0 : tileH1;
                                y += rh + pad;
                            }

                            int x = pad;
                            for (int c = 0; c < col; c++) {
                                int cw = c == 1 ? tileW1 : tileW0;
                                x += cw + pad;
                            }

                            return new Rect(x, y, w, h);
                        }

                        float x0(int col) => col == 0 ? pad : (col == 1 ? pad + tileW0 + pad : pad + tileW0 + pad + tileW1 + pad);
                        return Dst(0, 1, tileW1, tileH0);
                    }
                }
                """;

            string output = RunConversion(source, out JsonDocument report);

            AssertNoDiagnostic(report, "LocalFunctionStatement");
            Assert.Contains("auto Dst = [&](int32_t row, int32_t col, int32_t w, int32_t h) -> Rect*", output);
            Assert.Contains("auto x0 = [&](int32_t col) -> float", output);
            Assert.Contains("return Dst(0, 1, tileW1, tileH0);", output);
        }

        /// <summary>
        /// Runs the C++ converter against a temporary single-file project and returns all generated textual output.
        /// </summary>
        /// <param name="source">C# source file content to convert.</param>
        /// <param name="report">Parsed conversion report produced by the converter.</param>
        /// <returns>Concatenated generated file contents.</returns>
        static string RunConversion(string source, out JsonDocument report) {
            string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-localfunction-tests", Guid.NewGuid().ToString("N"));
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
    }
}
