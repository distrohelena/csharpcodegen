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

        CPPPInvokePlan emptyPlan = new CPPPInvokePlan(new List<CPPPInvokeImport>(), new List<CPPPInvokeCallback>(), new List<CPPPInvokeMirrorStruct>());
        string cmake = CPPWindowsHandoffWriter.Write(layout, emptyPlan);

        Assert.Contains("set(CPP_GENERATED_CORE_ROOT \"${CMAKE_CURRENT_LIST_DIR}\")", cmake, StringComparison.Ordinal);
        Assert.Contains("set(CPP_NATIVE_INTERMEDIATE_ROOT \"C:/Builds/MyGame/Intermediate/windows-directx\")", cmake, StringComparison.Ordinal);
        Assert.Contains("${CPP_GENERATED_CORE_ROOT}/generated_unity.cpp", cmake, StringComparison.Ordinal);
        Assert.Contains("set(CPP_GENERATED_NATIVE_IMPORTS_SOURCE \"\")", cmake, StringComparison.Ordinal);
        Assert.Contains("set(CPP_GENERATED_NATIVE_LINK_LIBRARIES \"\")", cmake, StringComparison.Ordinal);
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
        try {
            File.WriteAllText(projectPath, CreateProjectFile());
            File.WriteAllText(sourcePath, """
                using System.Runtime.InteropServices;
                namespace ExampleEngine.Core {
                    public static class CoreBootstrap {
                        [DllImport("kernel32.dll")]
                        public static extern ulong GetTickCount64();
                    }
                }
                """);

            CPPConversionOptions options = CPPConversionOptions.CreateDefault();
            options.LoadNativeRuntimeMetadata = false;
            options.WriteConversionReport = true;
            options.DeploymentRoot = deploymentRoot;
            options.TargetId = "windows-directx";

            CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
            converter.AddCsproj(projectPath);
            converter.WriteOutput(outputPath);

            string handoffContractPath = Path.Combine(generatedSourceRoot, "generated_windows_handoff.cmake");

            Assert.True(File.Exists(Path.Combine(generatedSourceRoot, "helcpp_config.hpp")));
            Assert.True(File.Exists(Path.Combine(generatedSourceRoot, "runtime", "feature_manifest.hpp")));
            Assert.True(File.Exists(Path.Combine(generatedSourceRoot, "generated_unity.cpp")));
            Assert.True(File.Exists(Path.Combine(generatedSourceRoot, "native_imports", "native_imports.cpp")));
            Assert.True(File.Exists(Path.Combine(generatedSourceRoot, CPPConversionReportWriter.DefaultFileName)));
            string handoff = File.ReadAllText(handoffContractPath);
            Assert.Contains("CPP_NATIVE_INTERMEDIATE_ROOT", handoff, StringComparison.Ordinal);
            Assert.Contains("set(CPP_GENERATED_NATIVE_IMPORTS_SOURCE \"${CPP_GENERATED_CORE_ROOT}/native_imports/native_imports.cpp\")", handoff, StringComparison.Ordinal);
            Assert.Contains("set(CPP_GENERATED_NATIVE_LINK_LIBRARIES \"kernel32\")", handoff, StringComparison.Ordinal);
            Assert.DoesNotContain("native_imports/native_imports.cpp", File.ReadAllText(Path.Combine(generatedSourceRoot, "generated_unity.cpp")), StringComparison.Ordinal);
            Assert.False(Directory.Exists(outputPath));
            Assert.NotEmpty(converter.Report.EmittedFiles);
            Assert.All(converter.Report.EmittedFiles, path => Assert.StartsWith(generatedSourceRoot + Path.DirectorySeparatorChar, path, StringComparison.OrdinalIgnoreCase));
        } finally {
            Directory.Delete(rootPath, true);
        }
    }

    /// <summary>
    /// Ensures callers without deployment options retain the explicit mirror and existing handoff file contract.
    /// </summary>
    [Fact]
    public void WriteOutput_WithWindowsHandoffOutputFolder_CopiesGeneratedCoreAndHandoffContract() {
        string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-windows-handoff-tests", Guid.NewGuid().ToString("N"));
        string projectPath = Path.Combine(rootPath, "Fixture.csproj");
        string outputPath = Path.Combine(rootPath, "out");
        string handoffPath = Path.Combine(rootPath, "sample-windows", "generated", "sample.core");
        Directory.CreateDirectory(rootPath);
        try {
            File.WriteAllText(projectPath, CreateProjectFile());
            File.WriteAllText(Path.Combine(rootPath, "Fixture.cs"), "namespace ExampleEngine.Core { public class CoreBootstrap { } }");

            CPPConversionOptions options = CPPConversionOptions.CreateDefault();
            options.LoadNativeRuntimeMetadata = false;
            options.WindowsHandoffOutputFolder = handoffPath;

            CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
            converter.AddCsproj(projectPath);
            converter.WriteOutput(outputPath);

            Assert.True(File.Exists(Path.Combine(outputPath, "helcpp_config.hpp")));
            Assert.True(File.Exists(Path.Combine(handoffPath, "helcpp_config.hpp")));
            Assert.True(File.Exists(Path.Combine(handoffPath, "runtime", "feature_manifest.hpp")));
            Assert.True(File.Exists(Path.Combine(handoffPath, "generated_windows_handoff.cmake")));
            Assert.Contains("CPP_GENERATED_CORE_ROOT", File.ReadAllText(Path.Combine(handoffPath, "generated_windows_handoff.cmake")), StringComparison.Ordinal);
        } finally {
            Directory.Delete(rootPath, true);
        }
    }

    /// <summary>
    /// Ensures a plan without native imports publishes empty native-import source and link-library variables.
    /// </summary>
    [Fact]
    public void Write_WithEmptyPlan_PublishesEmptyNativeImportVariables() {
        string folder = Path.Combine(Path.GetTempPath(), "cs2cpp-windows-handoff-tests", Guid.NewGuid().ToString("N"));
        CPPPInvokePlan emptyPlan = new CPPPInvokePlan(new List<CPPPInvokeImport>(), new List<CPPPInvokeCallback>(), new List<CPPPInvokeMirrorStruct>());

        try {
            string handoffPath = CPPWindowsHandoffWriter.Write(folder, emptyPlan);

            string handoff = File.ReadAllText(handoffPath);
            Assert.Contains("set(CPP_GENERATED_CORE_ROOT \"${CMAKE_CURRENT_LIST_DIR}\")", handoff, StringComparison.Ordinal);
            Assert.Contains("set(CPP_GENERATED_NATIVE_IMPORTS_SOURCE \"\")", handoff, StringComparison.Ordinal);
            Assert.Contains("set(CPP_GENERATED_NATIVE_LINK_LIBRARIES \"\")", handoff, StringComparison.Ordinal);
        } finally {
            Directory.Delete(folder, true);
        }
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
