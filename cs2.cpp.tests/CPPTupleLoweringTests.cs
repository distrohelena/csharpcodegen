using System.IO;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies that tuple generic arguments lower into concrete C++ type tokens instead of null placeholders.
/// </summary>
public class CPPTupleLoweringTests {
    /// <summary>
    /// Ensures tuple generic arguments in object creation can be emitted without crashing the writer.
    /// </summary>
    [Fact]
    public void WriteOutput_WithTupleGenericArguments_EmitsConcreteTypeTokens() {
        string fixtureRoot = Path.Combine(Path.GetTempPath(), $"cs2cpp-tuple-{Guid.NewGuid():N}");
        string outputRoot = Path.Combine(fixtureRoot, "out");

        Directory.CreateDirectory(fixtureRoot);
        string projectPath = CreateFixtureProject(fixtureRoot);

        CPPConversionOptions options = CreateTestOptions();
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(projectPath);

        converter.WriteOutput(outputRoot);

        string sourcePath = Path.Combine(outputRoot, "TupleStore.cpp");
        string source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("<NULL>", source, StringComparison.Ordinal);
        Assert.Contains("ValueTuple", source, StringComparison.Ordinal);
        Assert.Contains("new List<ValueTuple", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Creates focused converter options that avoid external runtime metadata tooling during unit tests.
    /// </summary>
    /// <returns>The option set used by the tuple lowering tests.</returns>
    static CPPConversionOptions CreateTestOptions() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.WriteConversionReport = true;
        return options;
    }

    /// <summary>
    /// Writes a minimal SDK-style project that reproduces tuple generic lowering during C++ emission.
    /// </summary>
    /// <param name="fixtureRoot">Temporary folder that receives the fixture project.</param>
    /// <returns>The path to the generated project file.</returns>
    static string CreateFixtureProject(string fixtureRoot) {
        string projectPath = Path.Combine(fixtureRoot, "FixtureTuple.csproj");
        string sourcePath = Path.Combine(fixtureRoot, "TupleStore.cs");

        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>disable</Nullable>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(sourcePath, """
            using System.Collections.Generic;

            public class TupleStore {
                public List<(string Category, string Key)> Snapshot() {
                    var result = new List<(string Category, string Key)>();
                    return result;
                }
            }
            """);

        return projectPath;
    }
}
