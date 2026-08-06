using cs2.cpp;

#nullable disable

namespace cs2.cpp.tests;

/// <summary>
/// Reproduces owned-insertion lowering shapes observed in engine sources.
/// </summary>
public sealed class CPPOwnedInsertionReproTests {
    /// <summary>
    /// Ensures owned insertions lower correctly when native runtime metadata is loaded like production builds.
    /// </summary>
    [Fact]
    public void WriteOutput_WithRuntimeMetadataLoaded_EmitsAddOwned() {
        string fixtureRoot = Path.Combine(Path.GetTempPath(), "owned-insertion-repro", Guid.NewGuid().ToString("N"));
        string projectPath = Path.Combine(fixtureRoot, "Fixture.csproj");
        string outputPath = Path.Combine(fixtureRoot, "generated");
        Directory.CreateDirectory(fixtureRoot);
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <LangVersion>preview</LangVersion>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>disable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(fixtureRoot, "Fixture.cs"), """
            using System.Collections.Generic;

            public sealed class Pass {
                public Pass(int offset, int color) {
                }
            }

            public interface ISource {
                int Offset { get; }
                int Color { get; }
            }

            public static class Builder {
                public static List<Pass> Build(ISource source) {
                    List<Pass> passes = new List<Pass>(6);
                    passes.Add(new Pass(source.Offset, source.Color));
                    return passes;
                }
            }
            """);

        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(projectPath);
        converter.WriteOutput(outputPath);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Builder.cpp"));

        Assert.Contains("->AddOwned(", sourceOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures a local list receiving fresh allocations with member-access constructor arguments lowers to owning insertions.
    /// </summary>
    [Fact]
    public void WriteOutput_WithLocalListAndConstructedElements_EmitsAddOwned() {
        string outputPath = CPPOwnershipEmissionTests.ConvertForTest("""
            using System.Collections.Generic;

            public sealed class Pass {
                public Pass(int offset, int color) {
                }
            }

            public interface ISource {
                int Offset { get; }
                int Color { get; }
            }

            public static class Builder {
                public static List<Pass> Build(ISource source) {
                    List<Pass> passes = new List<Pass>(6);
                    passes.Add(new Pass(source.Offset, source.Color));
                    return passes;
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Builder.cpp"));

        Assert.Contains("->AddOwned(", sourceOutput, StringComparison.Ordinal);
    }
}
