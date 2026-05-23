using System.Text.Json;
using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies build-feature reporting and runtime manifest emission for feature-pruned C++ output.
/// </summary>
public class CPPFeatureManifestWriterTests {
    /// <summary>
    /// Verifies that the conversion report records the resolved feature decisions for the active build profile.
    /// </summary>
    [Fact]
    public void WriteOutput_WhenShadersAreForceDisabled_WritesFeatureDecisionsToConversionReport() {
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
            CPPBuildFeatureProfile.CreateDefault().WithMode("shaders", CPPFeatureMode.Disabled));

        string reportPath = Path.Combine(outputPath, "cpp-conversion-report.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(reportPath));

        JsonElement decisions = document.RootElement.GetProperty("buildFeatures").GetProperty("decisions");
        JsonElement shadersDecision = decisions.EnumerateArray().Single(decision => decision.GetProperty("feature").GetString() == "shaders");

        Assert.False(shadersDecision.GetProperty("enabled").GetBoolean());
        Assert.Equal("ForcedDisabled", shadersDecision.GetProperty("origin").GetString());
    }

    /// <summary>
    /// Verifies that the converter emits a small runtime manifest that exposes the final feature decisions in-engine.
    /// </summary>
    [Fact]
    public void WriteOutput_WhenShadersAreForceDisabled_EmitsRuntimeFeatureManifest() {
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
            CPPBuildFeatureProfile.CreateDefault().WithMode("shaders", CPPFeatureMode.Disabled));

        string headerPath = Path.Combine(outputPath, "runtime", "feature_manifest.hpp");
        string sourcePath = Path.Combine(outputPath, "runtime", "feature_manifest.cpp");

        Assert.True(File.Exists(headerPath));
        Assert.True(File.Exists(sourcePath));
        Assert.Contains("Shaders", File.ReadAllText(sourcePath), StringComparison.Ordinal);
        Assert.Contains("ForcedDisabled", File.ReadAllText(sourcePath), StringComparison.Ordinal);
    }

    /// <summary>
    /// Runs the converter against a temporary single-file project and returns the output folder.
    /// </summary>
    /// <param name="source">C# source file content to convert.</param>
    /// <param name="featureProfile">Feature profile used for the conversion.</param>
    /// <returns>The generated output directory.</returns>
    static string RunConversion(string source, CPPBuildFeatureProfile featureProfile) {
        string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-feature-manifest-tests", Guid.NewGuid().ToString("N"));
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
        options.FeatureCatalog = CPPTestFeatureCatalogFactory.CreateHelengineCatalog();

        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(projectPath);
        converter.WriteOutput(outputPath);
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
