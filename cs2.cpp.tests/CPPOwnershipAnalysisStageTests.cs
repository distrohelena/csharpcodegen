using System.Reflection;
using cs2.cpp;
using cs2.cpp.tests.TestHelpers;

#nullable disable

namespace cs2.cpp.tests;

/// <summary>
/// Verifies that semantic native ownership analysis gates C++ lowering inside the converter pipeline.
/// </summary>
public sealed class CPPOwnershipAnalysisStageTests {
    /// <summary>
    /// Ensures a valid project completes ownership analysis and stores the immutable result for later lowering stages.
    /// </summary>
    [Fact]
    public void AddCsproj_WithValidOwnership_StoresAnalysisResult() {
        string projectPath = CreateProject("""
            using System.Collections.Generic;

            public static class Factory {
                public static List<int> Build() {
                    return new List<int>();
                }
            }

            public sealed class Fixture {
                public void Run() {
                    List<int> values = Factory.Build();
                    values.Add(42);
                }
            }
            """);
        CPPClassProcessingTrackingConverter converter = new CPPClassProcessingTrackingConverter();

        converter.AddCsproj(projectPath);

        CPPOwnershipAnalysisResult result = ReadOwnershipAnalysisResult(converter);
        Assert.NotNull(result);
        Assert.Contains(
            result.EmissionPlan.Transitions,
            transition => transition.LocalName == "values" && transition.Kind == CPPOwnershipTransitionKind.ScopeCleanup);
        Assert.DoesNotContain(converter.Report.Diagnostics, diagnostic => diagnostic.Code.StartsWith("CPPOWN", StringComparison.Ordinal));
        Assert.True(converter.ClassProcessingStarted);
    }

    /// <summary>
    /// Ensures resetting a converter removes the prior run's semantic ownership plan before another project is analyzed.
    /// </summary>
    [Fact]
    public void ResetRunState_AfterSuccessfulAnalysis_ClearsAnalysisResult() {
        string projectPath = CreateProject("""
            public sealed class Fixture {
            }
            """);
        CPPCodeConverter converter = CreateConverter();
        converter.AddCsproj(projectPath);
        Assert.NotNull(ReadOwnershipAnalysisResult(converter));

        converter.ResetRunState();

        Assert.Null(ReadOwnershipAnalysisResult(converter));
    }

    /// <summary>
    /// Ensures an unresolved native return contract is reported and stops the pipeline before class processing can begin.
    /// </summary>
    [Fact]
    public void AddCsproj_WithOwnershipError_ReportsDiagnosticAndStopsBeforeLowering() {
        string projectPath = CreateProject("""
            using System.Collections.Generic;

            public abstract class ExternalFactory {
                public abstract List<int> Create();
            }

            public static class Consumer {
                public static List<int> Create(ExternalFactory factory) {
                    return factory.Create();
                }
            }
            """);
        CPPClassProcessingTrackingConverter converter = new CPPClassProcessingTrackingConverter();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => converter.AddCsproj(projectPath));

        CPPConversionDiagnostic diagnostic = Assert.Single(converter.Report.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN001");
        Assert.Contains("CPPOWN001", exception.Message, StringComparison.Ordinal);
        Assert.EndsWith("Fixture.cs", diagnostic.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.True(diagnostic.LineNumber > 0);
        Assert.Null(ReadOwnershipAnalysisResult(converter));
        Assert.False(converter.ClassProcessingStarted);
    }

    /// <summary>
    /// Ensures ownership errors in a transitively referenced project participate in the same pre-lowering gate.
    /// </summary>
    [Fact]
    public void AddCsproj_WithReferencedOwnershipError_AnalyzesProjectClosure() {
        string projectPath = CreateProjectWithOwnershipErrorReference();
        CPPClassProcessingTrackingConverter converter = new CPPClassProcessingTrackingConverter();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => converter.AddCsproj(projectPath));

        CPPConversionDiagnostic diagnostic = Assert.Single(converter.Report.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN001");
        Assert.Contains("CPPOWN001", exception.Message, StringComparison.Ordinal);
        Assert.EndsWith("Referenced.cs", diagnostic.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.False(converter.ClassProcessingStarted);
    }

    /// <summary>
    /// Creates a converter configured to avoid external runtime metadata tooling in focused pipeline tests.
    /// </summary>
    /// <returns>A deterministic converter suitable for loading one temporary fixture project.</returns>
    static CPPCodeConverter CreateConverter() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        return new CPPCodeConverter(new CPPConversionRules(), options);
    }

    /// <summary>
    /// Creates a workspace-owned SDK project containing one ownership fixture source file.
    /// </summary>
    /// <param name="source">C# source analyzed by the pipeline.</param>
    /// <returns>The absolute path to the generated fixture project.</returns>
    static string CreateProject(string source) {
        string projectRoot = Path.Combine(Path.GetTempPath(), "ownership-analysis-stage", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectRoot);
        string projectPath = Path.Combine(projectRoot, "Fixture.csproj");
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
        File.WriteAllText(Path.Combine(projectRoot, "Fixture.cs"), source);
        return projectPath;
    }

    /// <summary>
    /// Creates a root project whose referenced project contains an unresolved ownership boundary.
    /// </summary>
    /// <returns>The absolute root project path used to verify transitive compilation analysis.</returns>
    static string CreateProjectWithOwnershipErrorReference() {
        string fixtureRoot = Path.Combine(Path.GetTempPath(), "ownership-analysis-stage-reference", Guid.NewGuid().ToString("N"));
        string referencedRoot = Path.Combine(fixtureRoot, "Referenced");
        string rootProjectRoot = Path.Combine(fixtureRoot, "Root");
        Directory.CreateDirectory(referencedRoot);
        Directory.CreateDirectory(rootProjectRoot);

        string referencedProjectPath = Path.Combine(referencedRoot, "Referenced.csproj");
        File.WriteAllText(referencedProjectPath, CreateProjectFileText(string.Empty));
        File.WriteAllText(Path.Combine(referencedRoot, "Referenced.cs"), """
            using System.Collections.Generic;

            public abstract class ExternalFactory {
                public abstract List<int> Create();
            }

            public static class ReferencedConsumer {
                public static List<int> Create(ExternalFactory factory) {
                    return factory.Create();
                }
            }
            """);

        string rootProjectPath = Path.Combine(rootProjectRoot, "Root.csproj");
        string projectReference = Path.GetRelativePath(rootProjectRoot, referencedProjectPath);
        File.WriteAllText(rootProjectPath, CreateProjectFileText(projectReference));
        File.WriteAllText(Path.Combine(rootProjectRoot, "Root.cs"), "public sealed class RootFixture { }");
        return rootProjectPath;
    }

    /// <summary>
    /// Creates minimal SDK project text with an optional project reference.
    /// </summary>
    /// <param name="projectReference">Relative referenced project path, or an empty value for a standalone project.</param>
    /// <returns>Complete SDK project XML.</returns>
    static string CreateProjectFileText(string projectReference) {
        string referenceItem = string.IsNullOrEmpty(projectReference)
            ? string.Empty
            : $"<ItemGroup><ProjectReference Include=\"{projectReference}\" /></ItemGroup>";
        return $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <LangVersion>preview</LangVersion>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>disable</Nullable>
              </PropertyGroup>
              {referenceItem}
            </Project>
            """;
    }

    /// <summary>
    /// Reads the converter's internal per-run ownership result without expanding the production API surface.
    /// </summary>
    /// <param name="converter">Converter whose run state is inspected.</param>
    /// <returns>The stored result when analysis completed successfully; otherwise null.</returns>
    static CPPOwnershipAnalysisResult ReadOwnershipAnalysisResult(CPPCodeConverter converter) {
        PropertyInfo property = typeof(CPPCodeConverter).GetProperty("OwnershipAnalysisResult", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Unable to locate converter ownership analysis state.");
        return (CPPOwnershipAnalysisResult)property.GetValue(converter);
    }
}
