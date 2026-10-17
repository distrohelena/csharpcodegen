using cs2.core;
using cs2.cpp.tests.TestHelpers;
using Microsoft.CodeAnalysis;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies that editor authoring markers remain metadata while unrelated runtime identities still emit source.
/// </summary>
public sealed class CPPGeneratedAuthoringMetadataEmissionPolicyTests {
    /// <summary>
    /// Evaluates full managed identities so an authoring exclusion cannot remove unrelated types with the same leaf name.
    /// </summary>
    /// <param name="typeName">Fully qualified managed identity evaluated by the policy.</param>
    /// <param name="expectedEmission">Whether the identity owns runtime-native source.</param>
    [Theory]
    [InlineData("helengine.RunInEditorAttribute", false)]
    [InlineData("helengine.CpuReadableModelReferenceAttribute", false)]
    [InlineData("helengine.ScenePersistenceIgnoreAttribute", false)]
    [InlineData("helengine.NativeOwnedReturnAttribute", false)]
    [InlineData("UserMetadata.RunInEditorAttribute", true)]
    [InlineData("UserMetadata.CpuReadableModelReferenceAttribute", true)]
    [InlineData("helengine.RunInEditorAttributeRuntime", true)]
    [InlineData("helengine.PortableComponent", true)]
    public void ShouldEmit_UsesExactAuthoringMetadataIdentity(string typeName, bool expectedEmission) {
        string source = """
            namespace helengine {
                public sealed class RunInEditorAttribute : System.Attribute { }
                public sealed class CpuReadableModelReferenceAttribute : System.Attribute { }
                public sealed class ScenePersistenceIgnoreAttribute : System.Attribute { }
                public sealed class NativeOwnedReturnAttribute : System.Attribute { }
                public sealed class RunInEditorAttributeRuntime { }
                public sealed class PortableComponent { }
            }
            namespace UserMetadata {
                public sealed class RunInEditorAttribute : System.Attribute { }
                public sealed class CpuReadableModelReferenceAttribute : System.Attribute { }
            }
            """;
        INamedTypeSymbol typeSymbol = RoslynTestHelper.CreateCompilation(source).GetTypeByMetadataName(typeName)
            ?? throw new InvalidOperationException($"Fixture type '{typeName}' was not resolved.");
        ConversionClass conversionClass = new ConversionClass { TypeSymbol = typeSymbol };

        Assert.Equal(expectedEmission, CPPGeneratedTypeEmissionPolicy.ShouldEmit(conversionClass));
    }

    /// <summary>
    /// Converts unguarded authoring annotations while retaining the component's portable runtime implementation.
    /// </summary>
    [Fact]
    public void WriteOutput_WithUnguardedAuthoringAttributes_EmitsOnlyPortableRuntimeTypes() {
        string fixtureRoot = Path.Combine(Path.GetTempPath(), "authoring-metadata-emission", Guid.NewGuid().ToString("N"));
        string projectPath = Path.Combine(fixtureRoot, "Fixture.csproj");
        string outputPath = Path.Combine(fixtureRoot, "out");
        Directory.CreateDirectory(fixtureRoot);
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>disable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(fixtureRoot, "Fixture.cs"), """
            namespace helengine {
                public sealed class RunInEditorAttribute : System.Attribute { }
                public sealed class CpuReadableModelReferenceAttribute : System.Attribute { }
                [RunInEditor]
                public sealed class PortableComponent {
                    [CpuReadableModelReference]
                    public string ModelId;
                    public int ReadValue() { return 7; }
                }
            }
            """);
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(projectPath);

        converter.WriteOutput(outputPath);

        Assert.False(File.Exists(Path.Combine(outputPath, "RunInEditorAttribute.hpp")));
        Assert.False(File.Exists(Path.Combine(outputPath, "RunInEditorAttribute.cpp")));
        Assert.False(File.Exists(Path.Combine(outputPath, "CpuReadableModelReferenceAttribute.hpp")));
        Assert.False(File.Exists(Path.Combine(outputPath, "CpuReadableModelReferenceAttribute.cpp")));
        Assert.True(File.Exists(Path.Combine(outputPath, "PortableComponent.hpp")));
        Assert.Contains("return 7;", File.ReadAllText(Path.Combine(outputPath, "PortableComponent.cpp")));
    }
}
