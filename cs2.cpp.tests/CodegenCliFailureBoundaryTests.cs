using codegen;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies conversion failures are contained by the codegen command-line boundary.
/// </summary>
public sealed class CodegenCliFailureBoundaryTests {
    /// <summary>
    /// Ensures an ownership conversion diagnostic becomes deterministic stderr and a nonzero result without escaping execution.
    /// </summary>
    [Fact]
    public void Execute_with_ownership_failure_returns_nonzero_and_writes_unwrapped_diagnostic() {
        string fixtureRoot = Path.Combine(Path.GetTempPath(), "codegen-cli-failure-boundary", Guid.NewGuid().ToString("N"));
        string projectPath = Path.Combine(fixtureRoot, "Fixture.csproj");
        string outputPath = Path.Combine(fixtureRoot, "generated");
        Directory.CreateDirectory(fixtureRoot);
        File.WriteAllText(projectPath, CreateFixtureProject());
        File.WriteAllText(Path.Combine(fixtureRoot, "Fixture.cs"), CreateOwnershipFailureSource());

        TextWriter originalError = Console.Error;
        StringWriter capturedError = new();
        try {
            Console.SetError(capturedError);

            int exitCode = -1;
            Exception escapedException = Record.Exception(() => exitCode = Program.Execute([
                    "--cpp",
                    "--project",
                    projectPath,
                    "--output",
                    outputPath,
                    "--set",
                    "load-native-runtime-metadata=false"
                ]));

            Assert.Null(escapedException);
            string errorOutput = capturedError.ToString();
            Assert.Equal(2, exitCode);
            Assert.StartsWith("Codegen failed: ", errorOutput, StringComparison.Ordinal);
            Assert.Contains("CPPOWN001", errorOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("TargetInvocationException", errorOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Unhandled exception", errorOutput, StringComparison.OrdinalIgnoreCase);
        } finally {
            Console.SetError(originalError);
            Directory.Delete(fixtureRoot, true);
        }
    }

    /// <summary>
    /// Creates the minimal SDK project used by the deterministic CLI failure fixture.
    /// </summary>
    /// <returns>Complete SDK project XML.</returns>
    static string CreateFixtureProject() {
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
    /// Creates source whose unresolved ownership boundary deterministically stops conversion during analysis.
    /// </summary>
    /// <returns>C# source containing one unresolved ownership return contract.</returns>
    static string CreateOwnershipFailureSource() {
        return """
            using System.Collections.Generic;

            public abstract class ExternalFactory {
                public abstract List<int> Create();
            }

            public static class Consumer {
                public static List<int> Create(ExternalFactory factory) {
                    return factory.Create();
                }
            }
            """;
    }
}
