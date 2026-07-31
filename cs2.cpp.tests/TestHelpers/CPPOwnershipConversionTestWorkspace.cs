using cs2.cpp;

namespace cs2.cpp.tests.TestHelpers;

/// <summary>
/// Creates isolated ownership conversion fixtures beneath the repository-owned scratch directory.
/// </summary>
public sealed class CPPOwnershipConversionTestWorkspace {
    /// <summary>
    /// Repository root resolved from the compiled test assembly location.
    /// </summary>
    readonly string RepositoryRoot;

    /// <summary>
    /// Initializes a workspace factory rooted in the active source repository.
    /// </summary>
    public CPPOwnershipConversionTestWorkspace() {
        RepositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    /// <summary>
    /// Converts one complete C# source fixture into a unique workspace-owned generated C++ directory.
    /// </summary>
    /// <param name="testId">Human-readable identifier included in the scratch directory name.</param>
    /// <param name="source">Complete C# source compiled and converted by the production pipeline.</param>
    /// <returns>The completed conversion output and its generated artifacts.</returns>
    public CPPOwnershipConversionOutput Convert(string testId, string source) {
        if (string.IsNullOrWhiteSpace(testId)) {
            throw new ArgumentException("An ownership conversion test identifier is required.", nameof(testId));
        }
        if (string.IsNullOrWhiteSpace(source)) {
            throw new ArgumentException("An ownership conversion source fixture is required.", nameof(source));
        }

        string workspaceName = SanitizeTestId(testId) + "-" + Guid.NewGuid().ToString("N");
        string workspacePath = Path.Combine(RepositoryRoot, "scratch", "ownership-tests", workspaceName);
        string projectPath = Path.Combine(workspacePath, "Fixture.csproj");
        string outputPath = Path.Combine(workspacePath, "generated");
        Directory.CreateDirectory(workspacePath);
        try {
            File.WriteAllText(projectPath, CreateProjectFile());
            File.WriteAllText(Path.Combine(workspacePath, "Fixture.cs"), source);

            CPPConversionOptions options = CPPConversionOptions.CreateDefault();
            options.LoadNativeRuntimeMetadata = false;
            options.WriteConversionReport = true;
            CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
            converter.AddCsproj(projectPath);
            converter.WriteOutput(outputPath);

            string generatedText = string.Join(
                Environment.NewLine,
                Directory.GetFiles(outputPath, "*.*", SearchOption.AllDirectories)
                    .Where(path => path.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .Select(File.ReadAllText));
            return new CPPOwnershipConversionOutput(
                workspacePath,
                projectPath,
                outputPath,
                generatedText,
                converter.Report,
                converter);
        } catch {
            Directory.Delete(workspacePath, true);
            throw;
        }
    }

    /// <summary>
    /// Creates the minimal SDK project used by ownership conversion fixtures and references the production ownership attributes.
    /// </summary>
    /// <returns>Complete SDK project XML.</returns>
    string CreateProjectFile() {
        string attributesProjectPath = Path.Combine(RepositoryRoot, "cs2.attributes", "cs2.attributes.csproj");
        string escapedAttributesProjectPath = System.Security.SecurityElement.Escape(attributesProjectPath)
            ?? throw new InvalidOperationException("The ownership attributes project path could not be escaped for XML.");
        return $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <LangVersion>preview</LangVersion>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>disable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{escapedAttributesProjectPath}}" />
              </ItemGroup>
            </Project>
            """;
    }

    /// <summary>
    /// Converts a test identifier into a filesystem-safe directory component.
    /// </summary>
    /// <param name="testId">Caller-provided test identifier.</param>
    /// <returns>A non-empty identifier containing only letters, digits, dashes, and underscores.</returns>
    static string SanitizeTestId(string testId) {
        char[] characters = testId
            .Select(character => char.IsLetterOrDigit(character) || character == '-' || character == '_'
                ? character
                : '-')
            .ToArray();
        string sanitized = new string(characters).Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "ownership" : sanitized;
    }
}
