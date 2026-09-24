namespace cs2.cpp.tests;

/// <summary>
/// Verifies the generated handoff contract that lets the Windows host consume a fresh transpiled core copy.
/// </summary>
public class CPPWindowsHandoffWriterTests {
    /// <summary>
    /// Ensures the converter can mirror generated output into a Windows handoff folder with the expected contract files.
    /// </summary>
    [Fact]
    public void WriteOutput_WithWindowsHandoffOutputFolder_CopiesGeneratedCoreAndHandoffContract() {
        string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-windows-handoff-tests", Guid.NewGuid().ToString("N"));
        string projectPath = Path.Combine(rootPath, "Fixture.csproj");
        string sourcePath = Path.Combine(rootPath, "Fixture.cs");
        string outputPath = Path.Combine(rootPath, "out");
        string handoffPath = Path.Combine(rootPath, "sample-windows", "generated", "sample.core");

        Directory.CreateDirectory(rootPath);
        File.WriteAllText(projectPath, CreateProjectFile());
        File.WriteAllText(sourcePath, "namespace ExampleEngine.Core { public class CoreBootstrap { } }");

        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.WindowsHandoffOutputFolder = handoffPath;

        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(projectPath);
        converter.WriteOutput(outputPath);

        string handoffContractPath = Path.Combine(handoffPath, "generated_windows_handoff.cmake");

        Assert.True(File.Exists(Path.Combine(handoffPath, "helcpp_config.hpp")));
        Assert.True(File.Exists(Path.Combine(handoffPath, "runtime", "feature_manifest.hpp")));
        Assert.True(File.Exists(handoffContractPath));
        Assert.Contains("CPP_GENERATED_CORE_ROOT", File.ReadAllText(handoffContractPath), StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures a plan without native imports publishes empty native-import source and link-library variables.
    /// </summary>
    [Fact]
    public void Write_WithEmptyPlan_PublishesEmptyNativeImportVariables() {
        string folder = Path.Combine(Path.GetTempPath(), "cs2cpp-windows-handoff-tests", Guid.NewGuid().ToString("N"));
        CPPPInvokePlan emptyPlan = new CPPPInvokePlan(new List<CPPPInvokeImport>(), new List<CPPPInvokeCallback>(), new List<CPPPInvokeMirrorStruct>());

        string handoffPath = CPPWindowsHandoffWriter.Write(folder, emptyPlan);

        string handoff = File.ReadAllText(handoffPath);
        Assert.Contains("set(CPP_GENERATED_CORE_ROOT \"${CMAKE_CURRENT_LIST_DIR}\")", handoff, StringComparison.Ordinal);
        Assert.Contains("set(CPP_GENERATED_NATIVE_IMPORTS_SOURCE \"\")", handoff, StringComparison.Ordinal);
        Assert.Contains("set(CPP_GENERATED_NATIVE_LINK_LIBRARIES \"\")", handoff, StringComparison.Ordinal);
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
