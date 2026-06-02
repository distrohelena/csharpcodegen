using System.Text.Json;
using cs2.cpp;

namespace cs2.cpp.tests {
    /// <summary>
    /// Covers regressions where named source symbols degrade into opaque object pointers during C++ lowering.
    /// </summary>
    public class CPPStrongTypePreservationAuditTests {
        /// <summary>
        /// Ensures user-defined classes, interfaces, structs, and generic arguments preserve their source type names instead of collapsing to void pointers.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNamedSymbols_PreservesConcreteTypeNames() {
            string source = """
                using System.Collections.Generic;

                public struct float3 {
                    public float X;
                }

                public interface IHelper {
                }

                public class Entity {
                    public float3 Position;
                }

                public class HelperNode : IHelper {
                    public Entity Parent;
                }

                public class StrongTypeGate {
                    public HelperNode Node;
                    public Entity Owner;
                    public float3 Position;
                    public List<HelperNode> Nodes;

                    public HelperNode GetNode(Entity owner, float3 position, IHelper helper) {
                        this.Owner = owner;
                        this.Position = position;
                        return this.Node;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string gateHeader = File.ReadAllText(Path.Combine(output.OutputPath, "StrongTypeGate.hpp"));

            Assert.Contains("HelperNode* Node;", output.GeneratedText);
            Assert.Contains("Entity* Owner;", output.GeneratedText);
            Assert.Contains("::float3 Position;", output.GeneratedText);
            Assert.Contains("List<::HelperNode*>*", output.GeneratedText);
            Assert.Contains("::HelperNode* GetNode(::Entity* owner, ::float3 position, ::IHelper* helper);", output.GeneratedText);
            Assert.Contains("class Entity;", gateHeader);
            Assert.Contains("#include \"float3.hpp\"", gateHeader);
            Assert.DoesNotContain("void* Node;", output.GeneratedText, StringComparison.Ordinal);
            Assert.DoesNotContain("void* Owner;", output.GeneratedText, StringComparison.Ordinal);
            Assert.DoesNotContain("void* Position;", output.GeneratedText, StringComparison.Ordinal);
        }

        /// <summary>
        /// Runs the C++ converter against a temporary single-file project and returns the generated output bundle.
        /// </summary>
        /// <param name="source">C# source file content to convert.</param>
        /// <returns>Output folder, parsed report, and generated textual output.</returns>
        static ConversionOutput RunConversion(string source) {
            string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-strong-type-tests", Guid.NewGuid().ToString("N"));
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
        /// Represents the generated output artifacts captured for a strong type preservation fixture.
        /// </summary>
        /// <param name="OutputPath">Generated output directory.</param>
        /// <param name="GeneratedText">Concatenated generated textual output.</param>
        /// <param name="Report">Parsed conversion report.</param>
        record ConversionOutput(string OutputPath, string GeneratedText, JsonDocument Report);
    }
}
