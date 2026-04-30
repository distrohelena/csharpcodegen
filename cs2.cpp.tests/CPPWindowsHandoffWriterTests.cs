namespace cs2.cpp.tests;

/// <summary>
/// Verifies the generated handoff contract that lets native hosts consume target-scoped generated core output from a deployment root.
/// </summary>
public class CPPWindowsHandoffWriterTests {
    /// <summary>
    /// Ensures the handoff contract describes the target-scoped generated-source and intermediate roots instead of a repo-local mirror.
    /// </summary>
    [Fact]
    public void Write_WhenDeploymentLayoutProvided_EmitsGeneratedSourceAndIntermediateContract() {
        CPPDeploymentBuildLayout layout = new CPPDeploymentBuildLayout(
            @"C:\Builds\MyGame",
            @"C:\Builds\MyGame\GeneratedSource\windows-directx",
            @"C:\Builds\MyGame\Intermediate\windows-directx",
            @"C:\Builds\MyGame\Build");

        string cmake = CPPWindowsHandoffWriter.Write(layout);

        Assert.Contains("HELENGINE_GENERATED_CORE_ROOT", cmake, StringComparison.Ordinal);
        Assert.Contains("HELENGINE_NATIVE_INTERMEDIATE_ROOT", cmake, StringComparison.Ordinal);
        Assert.DoesNotContain("generated/helengine.core", cmake, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the converter writes the handoff contract into the target-scoped generated-source root derived from the deployment root.
    /// </summary>
    [Fact]
    public void WriteOutput_WithDeploymentRoot_WritesHandoffContractIntoTargetGeneratedSource() {
        string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-windows-handoff-tests", Guid.NewGuid().ToString("N"));
        string projectPath = Path.Combine(rootPath, "Fixture.csproj");
        string sourcePath = Path.Combine(rootPath, "Fixture.cs");
        string outputPath = Path.Combine(rootPath, "out");
        string deploymentRoot = Path.Combine(rootPath, "Deployment");
        string generatedSourceRoot = Path.Combine(deploymentRoot, "GeneratedSource", "windows-directx");

        Directory.CreateDirectory(rootPath);
        File.WriteAllText(projectPath, CreateProjectFile());
        File.WriteAllText(sourcePath, "namespace helengine.core { public class CoreBootstrap { } }");

        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.DeploymentRoot = deploymentRoot;
        options.TargetId = "windows-directx";

        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(projectPath);
        converter.WriteOutput(outputPath);

        string handoffContractPath = Path.Combine(generatedSourceRoot, "helengine_windows_handoff.cmake");

        Assert.True(File.Exists(Path.Combine(generatedSourceRoot, "helcpp_config.hpp")));
        Assert.True(File.Exists(Path.Combine(generatedSourceRoot, "runtime", "feature_manifest.hpp")));
        Assert.True(File.Exists(handoffContractPath));
        Assert.Contains("HELENGINE_GENERATED_CORE_ROOT", File.ReadAllText(handoffContractPath), StringComparison.Ordinal);
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
}
