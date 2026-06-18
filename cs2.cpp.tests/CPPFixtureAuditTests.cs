using System.Text.Json;
using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies that the C++ backend can write a deterministic audit report for a real project conversion run.
/// </summary>
public class CPPFixtureAuditTests {
    /// <summary>
    /// Ensures the serialized audit report contains stable profile and unsupported-syntax summary sections.
    /// </summary>
    [Fact]
    public void FixtureAudit_WritesDeterministicDiagnosticReport() {
        string fixtureRoot = Path.Combine(Path.GetTempPath(), "cs2.cpp.tests", Guid.NewGuid().ToString("N"));
        string projectPath = CreateFixtureProject(fixtureRoot);
        string outputFolder = Path.Combine(fixtureRoot, "generated");

        CPPConversionOptions options = CreateTestOptions();
        options.PresetId = "windows-no-shaders";
        options.WriteConversionReport = true;

        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(projectPath);
        converter.WriteOutput(outputFolder);

        string reportPath = Path.Combine(outputFolder, CPPConversionReportWriter.DefaultFileName);
        string reportJson = File.ReadAllText(reportPath);
        using JsonDocument document = JsonDocument.Parse(reportJson);
        JsonElement root = document.RootElement;

        Assert.True(File.Exists(reportPath));
        Assert.Equal("FixtureAudit", root.GetProperty("assemblyName").GetString());
        Assert.Equal("windows-no-shaders", root.GetProperty("presetId").GetString());
        Assert.Equal(1, root.GetProperty("errorCount").GetInt32());
        Assert.Equal("msvc", root.GetProperty("activeProfiles").GetProperty("compiler").GetString());
        Assert.Equal("windows-headless", root.GetProperty("activeProfiles").GetProperty("platform").GetString());
        Assert.Equal("stl-lite", root.GetProperty("activeProfiles").GetProperty("runtime").GetString());
        Assert.Equal("desktop-no-shaders", root.GetProperty("activeProfiles").GetProperty("restrictions").GetString());
        Assert.Contains(root.GetProperty("diagnosticsByTypeMember").EnumerateArray(), group => group.GetProperty("sourceTypeName").GetString() == "BufferBuilder" && group.GetProperty("sourceMemberName").GetString() == "Fill");
        Assert.DoesNotContain(root.GetProperty("unsupportedSyntaxSummary").EnumerateArray(), summary => summary.GetProperty("syntaxKind").GetString() == "StackAllocArrayCreationExpression");
        Assert.Contains(root.GetProperty("unsupportedSyntaxSummary").EnumerateArray(), summary => summary.GetProperty("syntaxKind").GetString() == "UnsafeStatement" && summary.GetProperty("count").GetInt32() == 1);
    }

    /// <summary>
    /// Creates the focused converter options used by the audit test.
    /// </summary>
    /// <returns>The option set for the fixture-backed audit run.</returns>
    static CPPConversionOptions CreateTestOptions() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        return options;
    }

    /// <summary>
    /// Creates a temporary project that exercises unsupported constructs through the full conversion pipeline.
    /// </summary>
    /// <param name="fixtureRoot">Root folder that receives the generated fixture files.</param>
    /// <returns>The project file path for the temporary audit fixture.</returns>
    static string CreateFixtureProject(string fixtureRoot) {
        Directory.CreateDirectory(fixtureRoot);

        string projectPath = Path.Combine(fixtureRoot, "FixtureAudit.csproj");
        string sourcePath = Path.Combine(fixtureRoot, "BufferBuilder.cs");

        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <Nullable>disable</Nullable>
                <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
                <AssemblyName>FixtureAudit</AssemblyName>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(sourcePath, """
            using System;

            public class BufferBuilder {
                public byte ReadByte() {
                    Span<byte> buffer = stackalloc byte[sizeof(int)];
                    return buffer[0];
                }

                public unsafe void Fill(int* values) {
                    unsafe {
                        values[0] = 1;
                    }
                }
            }
            """);

        return projectPath;
    }
}
