using System.Text.Json;
using cs2.cpp;

namespace cs2.cpp.tests {
    /// <summary>
    /// Covers compile-validation regressions that surfaced from native compilation of generated helengine.core output.
    /// </summary>
    public class CPPCompileValidationRegressionTests {
        /// <summary>
        /// Ensures character literals preserve C# escape text so generated C++ uses valid char literals.
        /// </summary>
        [Fact]
        public void WriteOutput_WithCharacterLiterals_EmitsEscapedCppCharLiterals() {
            string source = """
                public class CharacterGate {
                    public char Map(bool shift) {
                        return shift ? '"' : '\'';
                    }

                    public bool IsEmpty(char value) {
                        return value == '\0';
                    }

                    public char Space() {
                        return ' ';
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);

            AssertNoDiagnostic(output.Report, "CharacterLiteralExpression");
            Assert.Contains("return shift ? '\"' : '\\'';", output.GeneratedText);
            Assert.Contains("value == '\\0'", output.GeneratedText);
            Assert.Contains("return ' ';", output.GeneratedText);
            Assert.DoesNotContain("return shift ? \"\"\" : \"'\";", output.GeneratedText);
        }

        /// <summary>
        /// Ensures the copied console runtime template uses the correct include path for the file helper.
        /// </summary>
        [Fact]
        public void WriteOutput_CopiesConsoleRuntimeTemplateWithIoInclude() {
            string source = """
                public class EmptyGate {
                }
                """;

            ConversionOutput output = RunConversion(source);
            string consolePath = Path.Combine(output.OutputPath, "system", "console.cpp");

            Assert.True(File.Exists(consolePath));
            Assert.Contains("#include \"io/file.hpp\"", File.ReadAllText(consolePath));
        }

        /// <summary>
        /// Ensures generated member access and primitive headers use native C++ syntax required by compile validation.
        /// </summary>
        [Fact]
        public void WriteOutput_NormalizesThisBaseNullAndFixedWidthIntegerHeaders() {
            string source = """
                public class Node {
                }

                public class BaseGate {
                    protected Node Current;

                    public virtual void Tick(int amount) {
                    }
                }

                public class DerivedGate : BaseGate {
                    Node Value;

                    public override void Tick(int amount) {
                        this.Value = this.Current;

                        if (this.Value == null) {
                            this.Value = null;
                        }

                        base.Tick(amount);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);

            Assert.Contains("#include <cstdint>", output.GeneratedText);
            Assert.Contains("this->Value = this->Current;", output.GeneratedText);
            Assert.Contains("if (this->Value == nullptr)", output.GeneratedText);
            Assert.Contains("this->Value = nullptr;", output.GeneratedText);
            Assert.Contains("BaseGate::Tick(amount);", output.GeneratedText);
            Assert.DoesNotContain("this.Value", output.GeneratedText);
            Assert.DoesNotContain("super", output.GeneratedText);
        }

        /// <summary>
        /// Ensures inherited classes and method-body type references emit the includes and base declaration required for native compile visibility.
        /// </summary>
        [Fact]
        public void WriteOutput_EmitsBaseInheritanceAndBodyTypeIncludes() {
            string source = """
                public class HelperNode {
                }

                public class CoreGate {
                    public static HelperNode Shared;
                }

                public class BaseGate {
                    public virtual void Tick() {
                    }
                }

                public class DerivedGate : BaseGate {
                    public override void Tick() {
                        base.Tick();
                        var local = new HelperNode();
                        var shared = CoreGate.Shared;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string derivedHeader = File.ReadAllText(Path.Combine(output.OutputPath, "DerivedGate.hpp"));
            string derivedSource = File.ReadAllText(Path.Combine(output.OutputPath, "DerivedGate.cpp"));

            Assert.Contains("#include \"BaseGate.hpp\"", derivedHeader);
            Assert.Contains("#include \"HelperNode.hpp\"", derivedHeader);
            Assert.Contains("#include \"CoreGate.hpp\"", derivedHeader);
            Assert.Contains("class DerivedGate : public BaseGate", derivedHeader);
            Assert.Contains("BaseGate::Tick()", derivedSource);
            Assert.Contains("new HelperNode()", derivedSource);
            Assert.Contains("CoreGate::Shared", derivedSource);
        }

        /// <summary>
        /// Ensures cyclic generated pointer references emit forward declarations so headers stay compilable without depending on include order.
        /// </summary>
        [Fact]
        public void WriteOutput_WithCyclicGeneratedPointerReferences_EmitsForwardDeclarations() {
            string source = """
                public class Component {
                    public Entity Parent;
                }

                public class Entity {
                    public Component Component;
                }
                """;

            ConversionOutput output = RunConversion(source);
            string entityHeader = File.ReadAllText(Path.Combine(output.OutputPath, "Entity.hpp"));
            string componentHeader = File.ReadAllText(Path.Combine(output.OutputPath, "Component.hpp"));

            Assert.Contains("class Component;", entityHeader);
            Assert.Contains("class Entity;", componentHeader);
            Assert.Contains("Component* Component", entityHeader);
            Assert.Contains("Entity* Parent", componentHeader);
        }

        /// <summary>
        /// Ensures generated references to generic converted types emit template-aware forward declarations instead of non-template class declarations.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericGeneratedReference_EmitsTemplateForwardDeclaration() {
            string source = """
                public class Stream {
                }

                public interface IContentProcessor<T> {
                    T Read(Stream stream);
                }

                public class ContentManager {
                    public IContentProcessor<Stream> Processor;
                }
                """;

            ConversionOutput output = RunConversion(source);
            string contentManagerHeader = File.ReadAllText(Path.Combine(output.OutputPath, "ContentManager.hpp"));

            Assert.Contains("template <typename T>", contentManagerHeader);
            Assert.Contains("class IContentProcessor_1;", contentManagerHeader);
            Assert.Contains("#include \"IContentProcessor_1.hpp\"", contentManagerHeader);
        }

        /// <summary>
        /// Ensures generated enum references do not emit invalid class forward declarations that conflict with enum class output.
        /// </summary>
        [Fact]
        public void WriteOutput_WithEnumReference_DoesNotEmitClassForwardDeclaration() {
            string source = """
                public enum ButtonState {
                    Released,
                    Pressed
                }

                public class InputState {
                    public ButtonState Button;
                }
                """;

            ConversionOutput output = RunConversion(source);
            string inputStateHeader = File.ReadAllText(Path.Combine(output.OutputPath, "InputState.hpp"));

            Assert.DoesNotContain("class ButtonState;", inputStateHeader, StringComparison.Ordinal);
            Assert.Contains("#include \"ButtonState.hpp\"", inputStateHeader);
        }

        /// <summary>
        /// Runs the C++ converter against a temporary single-file project and returns the generated output bundle.
        /// </summary>
        /// <param name="source">C# source file content to convert.</param>
        /// <returns>Output folder, parsed report, and generated textual output.</returns>
        static ConversionOutput RunConversion(string source) {
            string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-compile-validation-tests", Guid.NewGuid().ToString("N"));
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
        /// Represents the generated output artifacts captured for a compile-validation regression fixture.
        /// </summary>
        /// <param name="OutputPath">Generated output directory.</param>
        /// <param name="GeneratedText">Concatenated generated textual output.</param>
        /// <param name="Report">Parsed conversion report.</param>
        record ConversionOutput(string OutputPath, string GeneratedText, JsonDocument Report);
    }
}
