using System.Text.Json;
using cs2.cpp;

namespace cs2.cpp.tests {
    /// <summary>
    /// Verifies that the C++ converter lowers namespace-qualified type references without unsupported identifier diagnostics.
    /// </summary>
    public class CPPQualifiedNameAuditTests {
        /// <summary>
        /// Ensures namespace-qualified framework references emit C++ scope resolution and do not report unsupported identifier names.
        /// </summary>
        [Fact]
        public void Convert_WithNamespaceQualifiedFrameworkTypes_DoesNotReportIdentifierName() {
            string source = """
                public class QualifiedNamesFixture {
                    public string Compute(string text) {
                        if (text == null) {
                            throw new System.ArgumentNullException(nameof(text));
                        }

                        System.Text.StringBuilder builder = new System.Text.StringBuilder();
                        builder.Append(text);
                        return builder.ToString();
                    }
                }
                """;

            string output = RunConversion(source, out JsonDocument report);

            AssertNoDiagnostic(report, "IdentifierName");
            Assert.Contains("throw new ArgumentNullException(\"text\")", output);
            Assert.Contains("StringBuilder *builder = new StringBuilder()", output);
        }

        /// <summary>
        /// Ensures namespace-qualified engine types resolve to the generated local header name instead of a dotted include path.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNamespaceQualifiedEngineTypes_UsesLocalGeneratedHeaderIncludes() {
            string source = """
                namespace helengine {
                    public class float3 {
                    }

                    public class float4 {
                        public helengine.float3 Axis;

                        public static helengine.float3 Rotate(helengine.float3 value) {
                            return value;
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversionWithOutput(source, out JsonDocument report);
            string float4Header = File.ReadAllText(Path.Combine(output.OutputPath, "helengine_float4.hpp"));
            string float4CompatibilityHeader = File.ReadAllText(Path.Combine(output.OutputPath, "float4.hpp"));

            AssertNoDiagnostic(report, "IdentifierName");
            Assert.Contains("#include \"helengine_float3.hpp\"", float4Header);
            Assert.DoesNotContain("#include \"helengine.float3.hpp\"", float4Header, StringComparison.Ordinal);
            Assert.Contains("#include \"helengine_float4.hpp\"", float4CompatibilityHeader);
        }

        /// <summary>
        /// Ensures generated headers remain distinct on case-insensitive filesystems when two namespaces expose leaf type names that differ only by case.
        /// </summary>
        [Fact]
        public void WriteOutput_WithCaseOnlyQualifiedTypeCollision_EmitsDistinctHeaderPaths() {
            string source = """
                namespace helengine {
                    public struct int2 {
                        public int X;
                        public int Y;
                    }
                }

                namespace BepuUtilities {
                    public struct Int2 {
                        public int X;
                        public int Y;
                    }
                }

                public class CollisionFixture {
                    public helengine.int2 EngineSize;
                    public BepuUtilities.Int2 UtilitySize;
                }
                """;

            ConversionOutput output = RunConversionWithOutput(source, out JsonDocument report);
            string fixtureHeader = File.ReadAllText(Path.Combine(output.OutputPath, "CollisionFixture.hpp"));
            string engineHeaderPath = Path.Combine(output.OutputPath, "helengine_int2.hpp");
            string utilityHeaderPath = Path.Combine(output.OutputPath, "BepuUtilities_Int2.hpp");

            AssertNoDiagnostic(report, "IdentifierName");
            Assert.True(File.Exists(engineHeaderPath));
            Assert.True(File.Exists(utilityHeaderPath));
            Assert.Contains("class int2", File.ReadAllText(engineHeaderPath));
            Assert.Contains("class Int2", File.ReadAllText(utilityHeaderPath));
            Assert.Contains("#include \"helengine_int2.hpp\"", fixtureHeader);
            Assert.Contains("#include \"BepuUtilities_Int2.hpp\"", fixtureHeader);
            Assert.DoesNotContain("#include \"int2.hpp\"", fixtureHeader, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures the generated header path for one shared type stays stable across separate project conversion passes when only one pass sees the colliding type.
        /// </summary>
        [Fact]
        public void WriteOutput_WithCrossProjectCaseOnlyCollision_UsesStableSharedHeaderPath() {
            string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-qualified-name-tests", Guid.NewGuid().ToString("N"));
            string coreProjectPath = Path.Combine(rootPath, "core", "Core.csproj");
            string physicsProjectPath = Path.Combine(rootPath, "physics", "Physics.csproj");
            string coreSourcePath = Path.Combine(rootPath, "core", "int2.cs");
            string physicsSourcePath = Path.Combine(rootPath, "physics", "CollisionFixture.cs");
            string coreOutputPath = Path.Combine(rootPath, "core-out");
            string physicsOutputPath = Path.Combine(rootPath, "physics-out");

            Directory.CreateDirectory(Path.GetDirectoryName(coreProjectPath) ?? rootPath);
            Directory.CreateDirectory(Path.GetDirectoryName(physicsProjectPath) ?? rootPath);
            File.WriteAllText(coreProjectPath, CreateProjectFile());
            File.WriteAllText(
                physicsProjectPath,
                CreateProjectFileWithReference(Path.GetRelativePath(Path.GetDirectoryName(physicsProjectPath) ?? rootPath, coreProjectPath)));
            File.WriteAllText(
                coreSourcePath,
                """
                namespace helengine {
                    public struct int2 {
                        public int X;
                        public int Y;
                    }
                }
                """);
            File.WriteAllText(
                physicsSourcePath,
                """
                namespace BepuUtilities {
                    public struct Int2 {
                        public int X;
                        public int Y;
                    }
                }

                public class CollisionFixture {
                    public helengine.int2 EngineSize;
                    public BepuUtilities.Int2 UtilitySize;
                }
                """);

            RunConversionForProject(coreProjectPath, coreOutputPath, out JsonDocument _);
            RunConversionForProject(physicsProjectPath, physicsOutputPath, out JsonDocument report);

            AssertNoDiagnostic(report, "IdentifierName");
            Assert.True(File.Exists(Path.Combine(coreOutputPath, "helengine_int2.hpp")));
            Assert.True(File.Exists(Path.Combine(physicsOutputPath, "helengine_int2.hpp")));
            Assert.Contains("#include \"helengine_int2.hpp\"", File.ReadAllText(Path.Combine(coreOutputPath, "int2.hpp")));
            Assert.DoesNotContain("class int2", File.ReadAllText(Path.Combine(coreOutputPath, "int2.hpp")), StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures shared symbols reachable through a project-reference closure emit a single generated source artifact even when one referenced project only forwards the type.
        /// </summary>
        [Fact]
        public void WriteOutput_WithTypeForwarderProjectReference_DoesNotDuplicateGeneratedSourceFiles() {
            string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-qualified-name-tests", Guid.NewGuid().ToString("N"));
            string coreProjectPath = Path.Combine(rootPath, "core", "Core.csproj");
            string forwarderProjectPath = Path.Combine(rootPath, "forwarder", "Forwarder.csproj");
            string appProjectPath = Path.Combine(rootPath, "app", "App.csproj");
            string coreSourcePath = Path.Combine(rootPath, "core", "int2.cs");
            string forwarderSourcePath = Path.Combine(rootPath, "forwarder", "TypeForwarders.cs");
            string appSourcePath = Path.Combine(rootPath, "app", "App.cs");
            string outputPath = Path.Combine(rootPath, "out");

            Directory.CreateDirectory(Path.GetDirectoryName(coreProjectPath) ?? rootPath);
            Directory.CreateDirectory(Path.GetDirectoryName(forwarderProjectPath) ?? rootPath);
            Directory.CreateDirectory(Path.GetDirectoryName(appProjectPath) ?? rootPath);

            File.WriteAllText(coreProjectPath, CreateProjectFile());
            File.WriteAllText(
                forwarderProjectPath,
                CreateProjectFileWithReference(Path.GetRelativePath(Path.GetDirectoryName(forwarderProjectPath) ?? rootPath, coreProjectPath)));
            File.WriteAllText(
                appProjectPath,
                CreateProjectFileWithReferences(
                    Path.GetRelativePath(Path.GetDirectoryName(appProjectPath) ?? rootPath, coreProjectPath),
                    Path.GetRelativePath(Path.GetDirectoryName(appProjectPath) ?? rootPath, forwarderProjectPath)));

            File.WriteAllText(
                coreSourcePath,
                """
                namespace helengine {
                    public struct int2 {
                        public int X;
                        public int Y;
                    }
                }
                """);

            File.WriteAllText(
                forwarderSourcePath,
                """
                using System.Runtime.CompilerServices;

                [assembly: TypeForwardedTo(typeof(helengine.int2))]
                """);

            File.WriteAllText(
                appSourcePath,
                """
                public class App {
                    public helengine.int2 Size;
                }
                """);

            RunConversionForProject(appProjectPath, outputPath, out JsonDocument report);

            AssertNoDiagnostic(report, "IdentifierName");
            Assert.True(File.Exists(Path.Combine(outputPath, "helengine_int2.cpp")));
            Assert.False(File.Exists(Path.Combine(outputPath, "int2.cpp")));
            Assert.True(File.Exists(Path.Combine(outputPath, "int2.hpp")));
            Assert.Contains("#include \"helengine_int2.hpp\"", File.ReadAllText(Path.Combine(outputPath, "int2.hpp")));
        }

        /// <summary>
        /// Ensures a shared physical source file linked into multiple projects in the reference closure only emits one generated source artifact.
        /// </summary>
        [Fact]
        public void WriteOutput_WithLinkedSharedSourceInProjectClosure_DoesNotDuplicateGeneratedSourceFiles() {
            string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-qualified-name-tests", Guid.NewGuid().ToString("N"));
            string sharedSourcePath = Path.Combine(rootPath, "shared", "int2.cs");
            string coreProjectPath = Path.Combine(rootPath, "core", "Core.csproj");
            string bridgeProjectPath = Path.Combine(rootPath, "bridge", "Bridge.csproj");
            string appProjectPath = Path.Combine(rootPath, "app", "App.csproj");
            string bridgeSourcePath = Path.Combine(rootPath, "bridge", "BridgeType.cs");
            string appSourcePath = Path.Combine(rootPath, "app", "App.cs");
            string outputPath = Path.Combine(rootPath, "out");

            Directory.CreateDirectory(Path.GetDirectoryName(sharedSourcePath) ?? rootPath);
            Directory.CreateDirectory(Path.GetDirectoryName(coreProjectPath) ?? rootPath);
            Directory.CreateDirectory(Path.GetDirectoryName(bridgeProjectPath) ?? rootPath);
            Directory.CreateDirectory(Path.GetDirectoryName(appProjectPath) ?? rootPath);

            File.WriteAllText(
                sharedSourcePath,
                """
                namespace helengine {
                    public struct int2 {
                        public int X;
                        public int Y;
                    }
                }
                """);

            File.WriteAllText(
                coreProjectPath,
                CreateProjectFileWithCompileInclude(Path.GetRelativePath(Path.GetDirectoryName(coreProjectPath) ?? rootPath, sharedSourcePath)));
            File.WriteAllText(
                bridgeProjectPath,
                CreateProjectFileWithCompileIncludeAndReference(
                    Path.GetRelativePath(Path.GetDirectoryName(bridgeProjectPath) ?? rootPath, sharedSourcePath),
                    Path.GetRelativePath(Path.GetDirectoryName(bridgeProjectPath) ?? rootPath, coreProjectPath)));
            File.WriteAllText(
                appProjectPath,
                CreateProjectFileWithReferences(
                    Path.GetRelativePath(Path.GetDirectoryName(appProjectPath) ?? rootPath, coreProjectPath),
                    Path.GetRelativePath(Path.GetDirectoryName(appProjectPath) ?? rootPath, bridgeProjectPath)));

            File.WriteAllText(
                bridgeSourcePath,
                """
                public class BridgeType {
                    public helengine.int2 Size;
                }
                """);

            File.WriteAllText(
                appSourcePath,
                """
                public class App {
                    public helengine.int2 Size;
                    public BridgeType Bridge;
                }
                """);

            RunConversionForProject(appProjectPath, outputPath, out JsonDocument report);

            AssertNoDiagnostic(report, "IdentifierName");
            Assert.True(File.Exists(Path.Combine(outputPath, "helengine_int2.cpp")));
            Assert.False(File.Exists(Path.Combine(outputPath, "int2.cpp")));
        }

        /// <summary>
        /// Ensures unqualified engine type references in downstream projects stay bound to the stable emitted engine type name even when another referenced namespace exposes a case-colliding leaf type.
        /// </summary>
        [Fact]
        public void WriteOutput_WithUnqualifiedEngineTypeAcrossProjectClosure_UsesStableEngineEmittedTypeName() {
            string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-qualified-name-tests", Guid.NewGuid().ToString("N"));
            string coreProjectPath = Path.Combine(rootPath, "core", "Core.csproj");
            string utilityProjectPath = Path.Combine(rootPath, "utility", "Utility.csproj");
            string appProjectPath = Path.Combine(rootPath, "app", "App.csproj");
            string coreSourcePath = Path.Combine(rootPath, "core", "int2.cs");
            string utilitySourcePath = Path.Combine(rootPath, "utility", "Int2.cs");
            string appSourcePath = Path.Combine(rootPath, "app", "Fixture.cs");
            string outputPath = Path.Combine(rootPath, "out");

            Directory.CreateDirectory(Path.GetDirectoryName(coreProjectPath) ?? rootPath);
            Directory.CreateDirectory(Path.GetDirectoryName(utilityProjectPath) ?? rootPath);
            Directory.CreateDirectory(Path.GetDirectoryName(appProjectPath) ?? rootPath);

            File.WriteAllText(coreProjectPath, CreateProjectFile());
            File.WriteAllText(utilityProjectPath, CreateProjectFile());
            File.WriteAllText(
                appProjectPath,
                CreateProjectFileWithReferences(
                    Path.GetRelativePath(Path.GetDirectoryName(appProjectPath) ?? rootPath, coreProjectPath),
                    Path.GetRelativePath(Path.GetDirectoryName(appProjectPath) ?? rootPath, utilityProjectPath)));

            File.WriteAllText(
                coreSourcePath,
                """
                namespace helengine {
                    public struct int2 {
                        public int X;
                        public int Y;
                    }
                }
                """);

            File.WriteAllText(
                utilitySourcePath,
                """
                namespace BepuUtilities {
                    public struct Int2 {
                        public int X;
                        public int Y;
                    }
                }
                """);

            File.WriteAllText(
                appSourcePath,
                """
                global using helengine;

                public class Fixture {
                    public int2 EngineSize;
                    public BepuUtilities.Int2 UtilitySize;
                }
                """);

            RunConversionForProject(appProjectPath, outputPath, out JsonDocument report);
            string fixtureHeader = File.ReadAllText(Path.Combine(outputPath, "Fixture.hpp"));

            AssertNoDiagnostic(report, "IdentifierName");
            Assert.Contains("::helengine_int2 EngineSize;", fixtureHeader);
            Assert.Contains("::Int2* UtilitySize;", fixtureHeader);
            Assert.DoesNotContain("::int2 EngineSize;", fixtureHeader, StringComparison.Ordinal);
        }

        /// <summary>
        /// Runs the C++ converter against a temporary single-file project and returns all generated textual output.
        /// </summary>
        /// <param name="source">C# source file content to convert.</param>
        /// <param name="report">Parsed conversion report produced by the converter.</param>
        /// <returns>Concatenated generated file contents.</returns>
        static string RunConversion(string source, out JsonDocument report) {
            ConversionOutput output = RunConversionWithOutput(source, out report);
            return ReadGeneratedOutput(output.OutputPath);
        }

        /// <summary>
        /// Runs the C++ converter against a temporary single-file project and returns the generated output path.
        /// </summary>
        /// <param name="source">C# source file content to convert.</param>
        /// <param name="report">Parsed conversion report produced by the converter.</param>
        /// <returns>Output metadata for the generated fixture.</returns>
        static ConversionOutput RunConversionWithOutput(string source, out JsonDocument report) {
            string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-qualified-name-tests", Guid.NewGuid().ToString("N"));
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
            return new ConversionOutput(outputPath);
        }

        /// <summary>
        /// Runs the C++ converter against one existing project path and writes its output to the supplied directory.
        /// </summary>
        /// <param name="projectPath">Project file that should be converted.</param>
        /// <param name="outputPath">Directory that should receive generated output.</param>
        /// <param name="report">Parsed conversion report produced by the converter.</param>
        static void RunConversionForProject(string projectPath, string outputPath, out JsonDocument report) {
            CPPConversionOptions options = CPPConversionOptions.CreateDefault();
            options.LoadNativeRuntimeMetadata = false;
            options.WriteConversionReport = true;

            CPPConversionRules rules = new CPPConversionRules();
            CPPCodeConverter converter = new CPPCodeConverter(rules, options);
            converter.AddCsproj(projectPath);
            converter.WriteOutput(outputPath);

            string reportPath = Path.Combine(outputPath, "cpp-conversion-report.json");
            report = JsonDocument.Parse(File.ReadAllText(reportPath));
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
        /// Creates a minimal SDK-style project file that references another temporary project.
        /// </summary>
        /// <param name="relativeProjectReferencePath">Relative path from the temporary project to the referenced project file.</param>
        /// <returns>Project file content suitable for Roslyn-based multi-project analysis.</returns>
        static string CreateProjectFileWithReference(string relativeProjectReferencePath) {
            return $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net9.0</TargetFramework>
                    <LangVersion>preview</LangVersion>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>disable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{{relativeProjectReferencePath}}" />
                  </ItemGroup>
                </Project>
                """;
        }

        /// <summary>
        /// Creates a temporary SDK-style project file with two project references.
        /// </summary>
        /// <param name="firstRelativeProjectReferencePath">First project reference path relative to the generated project.</param>
        /// <param name="secondRelativeProjectReferencePath">Second project reference path relative to the generated project.</param>
        /// <returns>Project file XML content.</returns>
        static string CreateProjectFileWithReferences(string firstRelativeProjectReferencePath, string secondRelativeProjectReferencePath) {
            return $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net9.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>disable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{{firstRelativeProjectReferencePath}}" />
                    <ProjectReference Include="{{secondRelativeProjectReferencePath}}" />
                  </ItemGroup>
                </Project>
                """;
        }

        /// <summary>
        /// Creates a temporary SDK-style project file that compiles one shared source file.
        /// </summary>
        /// <param name="relativeCompileIncludePath">Compile include path relative to the generated project file.</param>
        /// <returns>Project file XML content.</returns>
        static string CreateProjectFileWithCompileInclude(string relativeCompileIncludePath) {
            return $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net9.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>disable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="{{relativeCompileIncludePath}}" Link="shared\%(Filename)%(Extension)" />
                  </ItemGroup>
                </Project>
                """;
        }

        /// <summary>
        /// Creates a temporary SDK-style project file that compiles one shared source file and references another project.
        /// </summary>
        /// <param name="relativeCompileIncludePath">Compile include path relative to the generated project file.</param>
        /// <param name="relativeProjectReferencePath">Project reference path relative to the generated project file.</param>
        /// <returns>Project file XML content.</returns>
        static string CreateProjectFileWithCompileIncludeAndReference(string relativeCompileIncludePath, string relativeProjectReferencePath) {
            return $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net9.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>disable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="{{relativeCompileIncludePath}}" Link="shared\%(Filename)%(Extension)" />
                  </ItemGroup>
                  <ItemGroup>
                    <ProjectReference Include="{{relativeProjectReferencePath}}" />
                  </ItemGroup>
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
        /// Represents the generated output directory captured for a qualified-name fixture.
        /// </summary>
        /// <param name="OutputPath">Generated output directory.</param>
        record ConversionOutput(string OutputPath);
    }
}
