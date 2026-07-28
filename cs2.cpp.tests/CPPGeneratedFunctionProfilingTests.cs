using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies opt-in Tracy instrumentation for functions emitted by the C++ generator.
/// </summary>
public class CPPGeneratedFunctionProfilingTests {
    /// <summary>
    /// Ensures malformed generated-function profiling option values fail rather than silently disabling profiling.
    /// </summary>
    [Fact]
    public void Resolve_WhenRequestedValueIsMalformed_Throws() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            [CPPCodegenOptionNames.GeneratedFunctionProfiling] = "sometimes"
        };

        Assert.Throws<InvalidOperationException>(() => CPPGeneratedFunctionProfilingOptionResolver.Resolve(options));
    }

    /// <summary>
    /// Ensures a profiling-enabled conversion writes the support header, direct Tracy include, scopes, and a matching manifest.
    /// </summary>
    [Fact]
    public void WriteOutput_WhenGeneratedFunctionProfilingIsEnabled_EmitsScopesAndMatchingManifest() {
        string outputPath = CPPGeneratedFunctionProfilingTestFixture.RunConversion(true);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "ProfileSubject.cpp"));
        string supportHeaderPath = Path.Combine(outputPath, "runtime", "generated_profiler.hpp");
        string manifestPath = Path.Combine(outputPath, "runtime", "generated_profiler_manifest.json");

        Assert.True(File.Exists(supportHeaderPath));
        Assert.True(File.Exists(manifestPath));
        Assert.Contains("#define HE_CPP_GENERATED_FUNCTION_PROFILING 1", File.ReadAllText(Path.Combine(outputPath, "helcpp_config.hpp")), StringComparison.Ordinal);
        Assert.Contains("#include <tracy/Tracy.hpp>", File.ReadAllText(supportHeaderPath), StringComparison.Ordinal);
        Assert.Contains("static const tracy::SourceLocationData", sourceOutput, StringComparison.Ordinal);
        Assert.Contains("tracy::ScopedZone", sourceOutput, StringComparison.Ordinal);
        Assert.Equal(5, CPPGeneratedFunctionProfilingTestFixture.CountScopes(sourceOutput));
        Assert.Equal(
            CPPGeneratedFunctionProfilingTestFixture.CountScopes(sourceOutput),
            CPPGeneratedFunctionProfilingTestFixture.ReadManifestScopeCount(manifestPath));
    }

    /// <summary>
    /// Ensures a profiling-disabled conversion leaves Tracy support and profiling manifests out of generated output.
    /// </summary>
    [Fact]
    public void WriteOutput_WhenGeneratedFunctionProfilingIsDisabled_EmitsNoProfilerArtifacts() {
        string outputPath = CPPGeneratedFunctionProfilingTestFixture.RunConversion(false);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "ProfileSubject.cpp"));

        Assert.False(File.Exists(Path.Combine(outputPath, "runtime", "generated_profiler.hpp")));
        Assert.False(File.Exists(Path.Combine(outputPath, "runtime", "generated_profiler_manifest.json")));
        Assert.Contains("#define HE_CPP_GENERATED_FUNCTION_PROFILING 0", File.ReadAllText(Path.Combine(outputPath, "helcpp_config.hpp")), StringComparison.Ordinal);
        Assert.DoesNotContain("tracy::ScopedZone", sourceOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("generated_profiler.hpp", sourceOutput, StringComparison.Ordinal);
    }
}

/// <summary>
/// Provides reusable temporary-project setup and output inspection for generated function profiling tests.
/// </summary>
public static class CPPGeneratedFunctionProfilingTestFixture {
    /// <summary>
    /// Runs a small conversion with generated function profiling explicitly enabled or disabled.
    /// </summary>
    /// <param name="enabled">Whether the profiling option should be enabled.</param>
    /// <returns>Root folder containing the generated C++ output.</returns>
    public static string RunConversion(bool enabled) {
        string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-generated-function-profiling-tests", Guid.NewGuid().ToString("N"));
        string projectPath = Path.Combine(rootPath, "Fixture.csproj");
        string sourcePath = Path.Combine(rootPath, "ProfileSubject.cs");
        string outputPath = Path.Combine(rootPath, "out");
        Directory.CreateDirectory(rootPath);
        File.WriteAllText(projectPath, CreateProjectFile());
        File.WriteAllText(sourcePath, CreateSourceFile());

        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            [CPPCodegenOptionNames.GeneratedFunctionProfiling] = enabled.ToString()
        };
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(projectPath);
        converter.WriteOutput(outputPath);
        return outputPath;
    }

    /// <summary>
    /// Counts direct Tracy scopes written to one generated translation unit.
    /// </summary>
    /// <param name="sourceOutput">Generated C++ source text.</param>
    /// <returns>The number of emitted Tracy scopes.</returns>
    public static int CountScopes(string sourceOutput) {
        return sourceOutput.Split("tracy::ScopedZone", StringSplitOptions.None).Length - 1;
    }

    /// <summary>
    /// Reads the scope count from a generated profiling manifest.
    /// </summary>
    /// <param name="manifestPath">Path to the generated manifest.</param>
    /// <returns>Number of profiling scope entries recorded by the generator.</returns>
    public static int ReadManifestScopeCount(string manifestPath) {
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
        return document.RootElement.GetProperty("scopes").GetArrayLength();
    }

    /// <summary>
    /// Produces the minimal SDK project file needed by the Roslyn conversion pipeline.
    /// </summary>
    /// <returns>SDK-style project XML.</returns>
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
    /// Produces members that exercise regular-method, constructor, operator, and property accessor emission.
    /// </summary>
    /// <returns>C# source fixture text.</returns>
    static string CreateSourceFile() {
        return """
            public class ProfileSubject {
                public int Value { get; set; }
                public ProfileSubject() { Value = 1; }
                public int Add(int value) { return Value + value; }
                public static ProfileSubject operator +(ProfileSubject left, ProfileSubject right) { return left; }
            }
            """;
    }
}
