using System.IO;
using System.Text.RegularExpressions;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies that repeated ref or out arguments for the same source variable reuse a single lowered temporary.
/// </summary>
public class CPPRefAliasInvocationTests {
    /// <summary>
    /// Ensures a repeated ref or out argument does not crash invocation lowering and only emits one copy-back assignment.
    /// </summary>
    [Fact]
    public void WriteOutput_WithRepeatedRefOutArgument_ReusesSingleTemporary() {
        string fixtureRoot = Path.Combine(Path.GetTempPath(), $"cs2cpp-refalias-{Guid.NewGuid():N}");
        string outputRoot = Path.Combine(fixtureRoot, "out");

        Directory.CreateDirectory(fixtureRoot);
        string projectPath = CreateFixtureProject(fixtureRoot);

        CPPConversionOptions options = CreateTestOptions();
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(projectPath);

        converter.WriteOutput(outputRoot);

        string sourcePath = Path.Combine(outputRoot, "Float3.cpp");
        string source = File.ReadAllText(sourcePath);

        MatchCollection vector1Assignments = Regex.Matches(source, @"vector1 = out_[a-z0-9]+\.value;");
        MatchCollection vector2Assignments = Regex.Matches(source, @"vector2 = out_[a-z0-9]+\.value;");

        Assert.Single(vector1Assignments.Cast<Match>());
        Assert.Single(vector2Assignments.Cast<Match>());
    }

    /// <summary>
    /// Creates focused converter options that avoid external runtime metadata tooling during unit tests.
    /// </summary>
    /// <returns>The option set used by the repeated ref and out invocation tests.</returns>
    static CPPConversionOptions CreateTestOptions() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.WriteConversionReport = true;
        return options;
    }

    /// <summary>
    /// Writes a minimal fixture project that reproduces repeated ref and out lowering for the same variable.
    /// </summary>
    /// <param name="fixtureRoot">Temporary folder that receives the fixture project.</param>
    /// <returns>The path to the generated project file.</returns>
    static string CreateFixtureProject(string fixtureRoot) {
        string projectPath = Path.Combine(fixtureRoot, "FixtureRefAlias.csproj");
        string sourcePath = Path.Combine(fixtureRoot, "Float3.cs");

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
            public struct Float3 {
                public float X;
                public float Y;
                public float Z;

                public static Float3 Cross(Float3 vector1, Float3 vector2) {
                    Cross(ref vector1, ref vector2, out vector1);
                    return vector1;
                }

                public static void Cross(ref Float3 vector1, ref Float3 vector2, out Float3 result) {
                    result = vector1;
                }
            }
            """);

        return projectPath;
    }
}
