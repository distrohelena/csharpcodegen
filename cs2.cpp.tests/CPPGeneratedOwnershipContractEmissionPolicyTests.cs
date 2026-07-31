using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies that source-only ownership metadata never becomes part of the generated native runtime.
/// </summary>
public sealed class CPPGeneratedOwnershipContractEmissionPolicyTests {
    /// <summary>
    /// Ensures all ownership contract attributes remain conversion metadata when their project is referenced by converted source.
    /// </summary>
    [Fact]
    public void WriteOutput_WithOwnershipContracts_DoesNotEmitAttributeRuntimeTypes() {
        string fixtureRoot = Path.Combine(Path.GetTempPath(), "ownership-contract-emission", Guid.NewGuid().ToString("N"));
        string projectPath = Path.Combine(fixtureRoot, "Fixture.csproj");
        string outputPath = Path.Combine(fixtureRoot, "out");
        Directory.CreateDirectory(fixtureRoot);
        File.WriteAllText(projectPath, CreateProjectFile());
        File.WriteAllText(Path.Combine(fixtureRoot, "Fixture.cs"), CreateFixtureSource());

        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(projectPath);
        converter.WriteOutput(outputPath);

        Assert.False(File.Exists(Path.Combine(outputPath, "NativeOwnedReturnAttribute.hpp")));
        Assert.False(File.Exists(Path.Combine(outputPath, "NativeBorrowedReturnAttribute.hpp")));
        Assert.False(File.Exists(Path.Combine(outputPath, "NativeTakesOwnershipAttribute.hpp")));
        Assert.False(File.Exists(Path.Combine(outputPath, "NativeOwnedMemberAttribute.hpp")));
    }

    /// <summary>
    /// Creates the SDK project that references the checked-in ownership attribute assembly.
    /// </summary>
    /// <returns>Complete project-file text for the conversion fixture.</returns>
    static string CreateProjectFile() {
        string attributesProjectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "cs2.attributes", "cs2.attributes.csproj"));
        return
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            "    <TargetFramework>net9.0</TargetFramework>\n" +
            "    <ImplicitUsings>enable</ImplicitUsings>\n" +
            "    <Nullable>disable</Nullable>\n" +
            "  </PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            $"    <ProjectReference Include=\"{attributesProjectPath}\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n";
    }

    /// <summary>
    /// Creates source that exercises every ownership contract declaration target.
    /// </summary>
    /// <returns>Complete C# source for the conversion fixture.</returns>
    static string CreateFixtureSource() {
        return """
            using cs2.attributes;

            public sealed class OwnershipContractFixture {
                [NativeOwnedMember]
                public object OwnedValue;

                [NativeOwnedReturn]
                public object Create() {
                    return new object();
                }

                [NativeBorrowedReturn]
                public object Borrow(object value) {
                    return value;
                }

                public void Take([NativeTakesOwnership] object value) {
                    OwnedValue = value;
                }
            }
            """;
    }
}
