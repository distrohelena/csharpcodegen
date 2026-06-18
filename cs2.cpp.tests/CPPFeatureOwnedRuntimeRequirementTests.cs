using System.Text.Json;
using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies that runtime helper ownership participates in feature pruning for generated C++ output.
/// </summary>
public class CPPFeatureOwnedRuntimeRequirementTests {
    /// <summary>
    /// Ensures shader-only text helpers are removed from generated output when shaders are force-disabled.
    /// </summary>
    [Fact]
    public void WriteOutput_WhenShadersAreForceDisabled_PrunesShaderOwnedStringReaderRuntimeFile() {
        string source = """
using System.IO;

namespace ExampleEngine.Core.Shaders.Compilation {
    public class ShaderConditionalPreprocessor {
        public string Filter(string source) {
            using StringReader reader = new StringReader(source);
            return source;
        }
    }
}
""";

        string outputPath = RunConversion(
            source,
            CPPBuildFeatureProfile.CreateDefault().WithMode("shaders", CPPFeatureMode.Disabled));

        string stringReaderPath = Path.Combine(outputPath, "system", "io", "string-reader.hpp");
        string reportPath = Path.Combine(outputPath, "cpp-conversion-report.json");
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(reportPath));

        Assert.False(File.Exists(stringReaderPath));
        Assert.DoesNotContain(
            report.RootElement.GetProperty("registeredRuntimeRequirements").EnumerateArray(),
            requirement => requirement.GetString() == "StringReader");
    }

    /// <summary>
    /// Ensures the runtime requirement catalog classifies restricted helpers under the expanded feature buckets.
    /// </summary>
    [Fact]
    public void Catalog_MapsRestrictedHelpersToExpandedFeatureBuckets() {
        CPPRuntimeRequirementCatalog catalog = new CPPRuntimeRequirementCatalog(CPPTestFeatureCatalogFactory.CreateSampleFeatureCatalog());

        Assert.True(catalog.TryGet("NativeType", out CPPRuntimeRequirementDefinition nativeType));
        Assert.Empty(nativeType.OwningFeatureIds);

        Assert.True(catalog.TryGet("Regex", out CPPRuntimeRequirementDefinition regex));
        Assert.Contains("shaders", regex.OwningFeatureIds);
        Assert.Contains("text_processing", regex.OwningFeatureIds);

        Assert.True(catalog.TryGet("File", out CPPRuntimeRequirementDefinition file));
        Assert.Empty(file.OwningFeatureIds);
    }

    /// <summary>
    /// Runs the converter against a temporary single-file project and returns the output folder.
    /// </summary>
    /// <param name="source">C# source file content to convert.</param>
    /// <param name="featureProfile">Feature profile used for the conversion.</param>
    /// <returns>The generated output directory.</returns>
    static string RunConversion(string source, CPPBuildFeatureProfile featureProfile) {
        string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-feature-owned-runtime-tests", Guid.NewGuid().ToString("N"));
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
        options.FeatureCatalog = CPPTestFeatureCatalogFactory.CreateSampleFeatureCatalog();

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
