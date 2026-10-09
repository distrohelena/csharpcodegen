using System.Text.Json;

namespace cs2.cpp.tests;

/// <summary>Verifies native range exceptions retain verified actual values without silently introducing managed-object boxing.</summary>
public sealed class CPPActualValueExceptionRegressionTests {
    /// <summary>Converts a once-evaluated 64-bit enum actual value with both full and compact native message policies.</summary>
    /// <param name="compactMessages">Selects canonical exception messages while retaining the actual-value diagnostic field.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteOutput_WithEnumActualValue_PreservesDiagnosticAndEvaluation(bool compactMessages) {
        string outputRoot = ConvertFixture(compactMessages ? "exception-compact" : "exception-full", """
            using System;
            public enum ProjectionMode : long { Unsupported = 4294967294L }
            public class ActualValueFixture {
                public static int Calls;
                static ProjectionMode ReadMode() { Calls++; return ProjectionMode.Unsupported; }
                public static int Fail() {
                    throw new ArgumentOutOfRangeException("mode", ReadMode(), "Unsupported projection.");
                }
            }
            """, compactMessages);
        string source = File.ReadAllText(Path.Combine(outputRoot, "ActualValueFixture.cpp"));
        Assert.Contains("String::ToJoinString(static_cast<int64_t>(", source, StringComparison.Ordinal);
        Assert.Equal(1, source.Split("= ActualValueFixture::ReadMode();", StringSplitOptions.None).Length - 1);
        string runtimeHeader = File.ReadAllText(Path.Combine(outputRoot, "runtime", "native_exceptions.hpp"));
        Assert.Contains("HeCppString ActualValueDiagnostic;", runtimeHeader, StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(outputRoot, "native-harness.cpp"), """
            #include "ActualValueFixture.hpp"
            #include "runtime/native_exceptions.hpp"
            #include <cassert>
            #include <string>
            int main() {
                try {
                    (void)ActualValueFixture::Fail();
                    assert(false);
                } catch (ArgumentOutOfRangeException* error) {
                    assert(ActualValueFixture::Calls == 1);
                    assert(error->ActualValueDiagnostic == "4294967294");
            #if !HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
                    assert(std::string(error->what()).find("Unsupported projection.") != std::string::npos);
                    assert(std::string(error->what()).find("Actual value was 4294967294.") != std::string::npos);
                    assert(std::string(error->what()).find("mode") != std::string::npos);
            #endif
                    delete error;
                }
                return 0;
            }
            """);
    }

    /// <summary>Rejects arbitrary managed actual-value objects with an explicit diagnostic instead of dropping their value.</summary>
    [Fact]
    public void WriteOutput_WithObjectActualValue_ReportsUnsupportedBoxing() {
        string outputRoot = ConvertFixture("exception-unsupported-object", """
            using System;
            public class UnsupportedActualValueFixture {
                public static void Fail(object value) {
                    throw new ArgumentOutOfRangeException("value", value, "Unsupported object.");
                }
            }
            """, false);
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputRoot, "cpp-conversion-report.json")));
        Assert.True(report.RootElement.GetProperty("hasErrors").GetBoolean());
        Assert.Contains("arbitrary managed object boxing is unavailable", report.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>Runs the complete project converter and keeps fixtures in the visible task-owned workspace.</summary>
    /// <param name="name">Distinct fixture name used for full, compact and unsupported cases.</param>
    /// <param name="source">Managed source consumed by the actual project loader.</param>
    /// <param name="compactMessages">Whether to generate the compact-message policy.</param>
    /// <returns>The generated native source directory.</returns>
    static string ConvertFixture(string name, string source, bool compactMessages) {
        string artifactRoot = Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_ARTIFACT_ROOT")
            ?? "C:/dev/helworks/builds/helengine-n64/menu-20261007/indexed-textures/generator-fixtures";
        string root = Path.Combine(artifactRoot, name);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Fixture.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>disable</Nullable></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(root, "Fixture.cs"), source);
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.WriteConversionReport = true;
        options.RuntimeProfile.UseExceptions = true;
        options.PlatformOptionValues = new Dictionary<string, string> {
            ["codegen-compact-native-exception-messages"] = compactMessages ? "true" : "false"
        };
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(Path.Combine(root, "Fixture.csproj"));
        string output = Path.Combine(root, "generated");
        converter.WriteOutput(output);
        return output;
    }
}
