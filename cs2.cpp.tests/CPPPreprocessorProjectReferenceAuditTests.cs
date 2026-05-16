using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies C++ conversion applies backend-owned preprocessor symbols across referenced projects.
/// </summary>
public class CPPPreprocessorProjectReferenceAuditTests {
    /// <summary>
    /// Ensures referenced projects honor cooked-material preprocessor symbols when project-defined symbols are disabled.
    /// </summary>
    [Fact]
    public void WriteOutput_WithReferencedProjectConditionalSymbol_UsesConfiguredBranchInReferencedProjectOutput() {
        string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-project-reference-preprocessor-tests", Guid.NewGuid().ToString("N"));
        string referencedProjectPath = Path.Combine(rootPath, "Referenced", "Referenced.csproj");
        string rootProjectPath = Path.Combine(rootPath, "Root", "Root.csproj");
        string outputPath = Path.Combine(rootPath, "out");

        Directory.CreateDirectory(Path.GetDirectoryName(referencedProjectPath) ?? throw new InvalidOperationException("Referenced project directory path must resolve."));
        Directory.CreateDirectory(Path.GetDirectoryName(rootProjectPath) ?? throw new InvalidOperationException("Root project directory path must resolve."));
        File.WriteAllText(referencedProjectPath, CreateProjectFile());
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(referencedProjectPath) ?? throw new InvalidOperationException("Referenced project directory path must resolve."), "MaterialContract.cs"),
            """
            public class MaterialContract {
            #if HELENGINE_RUNTIME_MATERIAL_RESOLUTION_COOKED_PLATFORM_OWNED
                public string Resolve() {
                    return "Cooked";
                }
            #else
                public string Resolve() {
                    return "Raw";
                }
            #endif
            }
            """);
        File.WriteAllText(
            rootProjectPath,
            CreateProjectFileWithReference(Path.GetRelativePath(
                Path.GetDirectoryName(rootProjectPath) ?? throw new InvalidOperationException("Root project directory path must resolve."),
                referencedProjectPath)));
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(rootProjectPath) ?? throw new InvalidOperationException("Root project directory path must resolve."), "SceneBootstrap.cs"),
            """
            public class SceneBootstrap {
                readonly MaterialContract Contract = new MaterialContract();

                public string Resolve() {
                    return Contract.Resolve();
                }
            }
            """);

        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.IncludeProjectDefinedPreprocessorSymbols = false;
        options.AdditionalPreprocessorSymbols = ["HELENGINE_RUNTIME_MATERIAL_RESOLUTION_COOKED_PLATFORM_OWNED"];

        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(rootProjectPath);
        converter.WriteOutput(outputPath);

        string referencedOutput = File.ReadAllText(Path.Combine(outputPath, "MaterialContract.cpp"));

        Assert.Contains("\"Cooked\"", referencedOutput);
        Assert.DoesNotContain("\"Raw\"", referencedOutput, StringComparison.Ordinal);
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
    /// Creates a minimal SDK-style project file that references another temporary fixture project.
    /// </summary>
    /// <param name="projectReferencePath">Relative path from the root fixture project to the referenced project.</param>
    /// <returns>Project file content suitable for Roslyn-based analysis.</returns>
    static string CreateProjectFileWithReference(string projectReferencePath) {
        if (string.IsNullOrWhiteSpace(projectReferencePath)) {
            throw new ArgumentException("Project reference path must be provided.", nameof(projectReferencePath));
        }

        return $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <LangVersion>preview</LangVersion>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>disable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{projectReferencePath}}" />
              </ItemGroup>
            </Project>
            """;
    }
}
