using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies C++ conversion tolerates owned-list local initializers that come from referenced project methods.
/// </summary>
public sealed class CPPProjectReferenceOwnedListAuditTests {
    /// <summary>
    /// Ensures project-reference method calls that return owned list-family values do not crash the converter.
    /// </summary>
    [Fact]
    public void WriteOutput_WithReferencedProjectOwnedListInitializer_DoesNotThrow() {
        string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-project-reference-owned-list-tests", Guid.NewGuid().ToString("N"));
        string referencedProjectPath = Path.Combine(rootPath, "Referenced", "Referenced.csproj");
        string rootProjectPath = Path.Combine(rootPath, "Root", "Root.csproj");
        string outputPath = Path.Combine(rootPath, "out");

        Directory.CreateDirectory(Path.GetDirectoryName(referencedProjectPath) ?? throw new InvalidOperationException("Referenced project directory path must resolve."));
        Directory.CreateDirectory(Path.GetDirectoryName(rootProjectPath) ?? throw new InvalidOperationException("Root project directory path must resolve."));
        File.WriteAllText(referencedProjectPath, CreateProjectFile());
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(referencedProjectPath) ?? throw new InvalidOperationException("Referenced project directory path must resolve."), "ReferencedFactory.cs"),
            """
            using System.Collections.Generic;

            public class ReferencedFactory {
                public List<int> CreateValues() {
                    return [1, 2, 3];
                }
            }
            """);
        File.WriteAllText(
            rootProjectPath,
            CreateProjectFileWithReference(Path.GetRelativePath(
                Path.GetDirectoryName(rootProjectPath) ?? throw new InvalidOperationException("Root project directory path must resolve."),
                referencedProjectPath)));
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(rootProjectPath) ?? throw new InvalidOperationException("Root project directory path must resolve."), "RootScene.cs"),
            """
            using System.Collections.Generic;

            public class RootScene {
                readonly ReferencedFactory Factory = new ReferencedFactory();

                public int ReadFirstValue() {
                    List<int> values = Factory.CreateValues();
                    return values[0];
                }
            }
            """);

        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;

        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(rootProjectPath);

        Exception exception = Record.Exception(() => converter.WriteOutput(outputPath));

        Assert.Null(exception);
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

    /// <summary>
    /// Creates a minimal SDK-style project file that references another temporary fixture project.
    /// </summary>
    /// <param name="projectReferencePath">Relative path from the root fixture project to the referenced project.</param>
    /// <returns>Project file content suitable for Roslyn-based analysis.</returns>
    static string CreateProjectFileWithReference(string projectReferencePath) {
        if (string.IsNullOrWhiteSpace(projectReferencePath)) {
            throw new ArgumentException("Project reference path must be provided.", nameof(projectReferencePath));
        }

        return $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <LangVersion>preview</LangVersion>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>disable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{projectReferencePath}}" />
              </ItemGroup>
            </Project>
            """;
    }
}
