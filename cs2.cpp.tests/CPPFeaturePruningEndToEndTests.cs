using System.Text.Json;

namespace cs2.cpp.tests;

/// <summary>
/// Covers end-to-end feature-pruned output generation for the C++ backend.
/// </summary>
public class CPPFeaturePruningEndToEndTests {
    /// <summary>
    /// Verifies that force-disabling shaders removes shader-tagged generated files from output.
    /// </summary>
    [Fact]
    public void WriteOutput_WhenShadersAreDisabled_DoesNotEmitShaderTypes() {
        string source = """
namespace helengine.core.shaders {
    public class ShaderAsset {
    }
}

namespace helengine.core.scene {
    public class SceneNode {
    }
}
""";

        string outputPath = RunConversion(
            source,
            CPPBuildFeatureProfile.CreateDefault().WithMode(CPPFeatureKind.Shaders, CPPFeatureMode.Disabled));

        Assert.False(File.Exists(Path.Combine(outputPath, "ShaderAsset.hpp")));
        Assert.False(File.Exists(Path.Combine(outputPath, "ShaderAsset.cpp")));
        Assert.True(File.Exists(Path.Combine(outputPath, "SceneNode.hpp")));
        Assert.True(File.Exists(Path.Combine(outputPath, "SceneNode.cpp")));
    }

    /// <summary>
    /// Verifies that force-disabling the debug overlay removes both overlay output and its feature-owned runtime helper.
    /// </summary>
    [Fact]
    public void WriteOutput_WhenDebugOverlayIsDisabled_DoesNotEmitOverlayRuntimeHelpers() {
        string source = """
using System.Text;

namespace helengine {
    public class DebugOverlayComponent {
        public string BuildText() {
            StringBuilder builder = new StringBuilder();
            builder.Append("debug");
            return builder.ToString();
        }
    }
}

namespace helengine.core.scene {
    public class SceneNode {
    }
}
""";

        string outputPath = RunConversion(
            source,
            CPPBuildFeatureProfile.CreateDefault().WithMode(CPPFeatureKind.DebugOverlay, CPPFeatureMode.Disabled));

        Assert.False(File.Exists(Path.Combine(outputPath, "DebugOverlayComponent.hpp")));
        Assert.False(File.Exists(Path.Combine(outputPath, "DebugOverlayComponent.cpp")));
        Assert.False(File.Exists(Path.Combine(outputPath, "system", "text", "string-builder.hpp")));
        Assert.True(File.Exists(Path.Combine(outputPath, "SceneNode.hpp")));
        Assert.True(File.Exists(Path.Combine(outputPath, "SceneNode.cpp")));
    }

    /// <summary>
    /// Runs the C++ converter against a temporary single-file project and returns the generated output path.
    /// </summary>
    /// <param name="source">C# source file content to convert.</param>
    /// <param name="featureProfile">Resolved feature configuration for the conversion.</param>
    /// <returns>The output directory that contains the generated C++ files.</returns>
    static string RunConversion(string source, CPPBuildFeatureProfile featureProfile) {
        string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-feature-pruning-tests", Guid.NewGuid().ToString("N"));
        string projectPath = Path.Combine(rootPath, "Fixture.csproj");
        string sourcePath = Path.Combine(rootPath, "Fixture.cs");
        string outputPath = Path.Combine(rootPath, "out");

        Directory.CreateDirectory(rootPath);
        File.WriteAllText(projectPath, CreateProjectFile());
        File.WriteAllText(sourcePath, source);

        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.WriteConversionReport = true;
        options.BuildFeatureProfile = featureProfile;

        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(projectPath);
        converter.WriteOutput(outputPath);

        string reportPath = Path.Combine(outputPath, "cpp-conversion-report.json");
        using JsonDocument _ = JsonDocument.Parse(File.ReadAllText(reportPath));
        return outputPath;
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
