using System.Globalization;
using System.Text.Json;

namespace cs2.cpp.tests;

/// <summary>Checks exact invariant primitive overload lowering and the restricted D6/F0/F1 native formatter against managed golden values.</summary>
public sealed class CPPNumericInvariantOverloadTests {
    /// <summary>Converts field, local and side-effect receivers for both native exception policies and writes an executable native golden harness.</summary>
    /// <param name="exceptions">Whether generated helpers permit recoverable native exceptions.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WriteOutput_InvariantNumericOverloads_PreserveExactSupportedFormatting(bool exceptions) {
        string output = ConvertFixture(exceptions ? "exceptions" : "fatal", """
            using System.Globalization;
            public class NumericFixture {
                public int Count;
                public double Time;
                public float Rate;
                public static int Calls;
                static double NextDouble() { Calls++; return 2.25; }
                public string CountField() { return Count.ToString("D6", CultureInfo.InvariantCulture); }
                public string TimeField() { return Time.ToString("F1", CultureInfo.InvariantCulture); }
                public string RateField() { return Rate.ToString("F0", CultureInfo.InvariantCulture); }
                public string CountProvider() { return Count.ToString(CultureInfo.InvariantCulture); }
                public static string Integer(int value) { return value.ToString("D6", CultureInfo.InvariantCulture); }
                public static string IntegerFixed(int value) { return value.ToString("F1", CultureInfo.InvariantCulture); }
                public static string Double(double value) { return value.ToString("F1", CultureInfo.InvariantCulture); }
                public static string Whole(double value) { return value.ToString("F0", CultureInfo.InvariantCulture); }
                public static string Float(float value) { return value.ToString("F1", CultureInfo.InvariantCulture); }
                public static string IntegerProvider(int value) { return value.ToString(CultureInfo.InvariantCulture); }
                public static string Once() { return NextDouble().ToString("F1", CultureInfo.InvariantCulture); }
            }
            """, exceptions);
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "cpp-conversion-report.json")));
        Assert.False(report.RootElement.GetProperty("hasErrors").GetBoolean());
        string source = File.ReadAllText(Path.Combine(output, "NumericFixture.cpp"));
        Assert.DoesNotContain("CultureInfo", source);
        Assert.Contains("String::ToInvariantDecimal6", source);
        Assert.Contains("String::ToInvariantFixed", source);
        string harness = """
            #include "NumericFixture.hpp"
            #include <cassert>
            #include <cstdint>
            #include <limits>
            int main() {
                NumericFixture fixture;
                fixture.Count = 37; fixture.Time = 2.25; fixture.Rate = 2.5f;
                assert(fixture.CountField() == "000037");
                assert(fixture.TimeField() == "2.2");
                assert(fixture.RateField() == "2");
                assert(fixture.CountProvider() == "37");
                assert(NumericFixture::Integer(-37) == "-000037");
                assert(NumericFixture::Integer(0) == "000000");
                assert(NumericFixture::Integer(INT32_MIN) == "-2147483648");
                assert(NumericFixture::IntegerFixed(INT32_MIN) == "-2147483648.0");
                assert(NumericFixture::Double(2.25) == "2.2");
                assert(NumericFixture::Double(1.15) == "1.1");
                assert(NumericFixture::Double(-1.15) == "-1.1");
                assert(NumericFixture::Double(-2.25) == "-2.2");
                assert(NumericFixture::Double(2.75) == "2.8");
                assert(NumericFixture::Double(9.99) == "10.0");
                assert(NumericFixture::Double(-0.0) == "-0.0");
                assert(NumericFixture::Whole(-0.0) == "-0");
                assert(NumericFixture::Whole(2.5) == "2" && NumericFixture::Whole(3.5) == "4");
                assert(NumericFixture::Whole(-2.5) == "-2" && NumericFixture::Whole(-3.5) == "-4");
                assert(NumericFixture::Float(1.15f) == "1.1");
                assert(NumericFixture::Float(2.25f) == "2.2");
                assert(NumericFixture::Double(1e-310) == "0.0");
                assert(NumericFixture::Double(-1e-310) == "-0.0");
                assert(NumericFixture::Double(std::numeric_limits<double>::infinity()) == "Infinity");
                assert(NumericFixture::Double(-std::numeric_limits<double>::infinity()) == "-Infinity");
                assert(NumericFixture::Double(std::numeric_limits<double>::quiet_NaN()) == "NaN");
                assert(NumericFixture::Double(std::numeric_limits<double>::max()) == MAXIMUM_FIXED);
                assert(NumericFixture::IntegerProvider(73) == "73");
                NumericFixture::Calls = 0;
                assert(NumericFixture::Once() == "2.2" && NumericFixture::Calls == 1);
            }
            """;
        File.WriteAllText(Path.Combine(output, "native-harness.cpp"), harness.Replace("MAXIMUM_FIXED", JsonSerializer.Serialize(double.MaxValue.ToString("F1", CultureInfo.InvariantCulture))));
    }

    /// <summary>Refuses unsupported precision, dynamic formats and noncanonical providers before they become invalid primitive method calls.</summary>
    /// <param name="name">Stable fixture directory identifying the unsupported overload.</param>
    /// <param name="body">Method body that must produce an explicit conversion diagnostic.</param>
    [Theory]
    [InlineData("precision", "return value.ToString(\"F2\", CultureInfo.InvariantCulture);")]
    [InlineData("dynamic", "return value.ToString(format, CultureInfo.InvariantCulture);")]
    [InlineData("provider", "return value.ToString(provider);")]
    [InlineData("culture", "return value.ToString(CultureInfo.CurrentCulture);")]
    [InlineData("floating-provider", "return value.ToString(CultureInfo.InvariantCulture);")]
    public void WriteOutput_UnsupportedNumericOverload_ReportsDiagnostic(string name, string body) {
        string output = ConvertFixture(name, "using System; using System.Globalization; public class UnsupportedNumericFixture { public string Render(double value, string format, IFormatProvider provider) { " + body + " } }", true);
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "cpp-conversion-report.json")));
        Assert.True(report.RootElement.GetProperty("hasErrors").GetBoolean());
        Assert.Contains("supports only exact CultureInfo.InvariantCulture", report.RootElement.GetRawText());
    }

    /// <summary>Loads an actual managed project into the production converter while keeping all fixture artifacts in the task's fixed visible directory.</summary>
    /// <param name="name">Profile or rejection case used as the stable output directory.</param>
    /// <param name="source">Complete managed fixture source consumed by Roslyn.</param>
    /// <param name="exceptions">Selects native recoverable or fatal exception behavior.</param>
    /// <returns>The generated native output directory.</returns>
    static string ConvertFixture(string name, string source, bool exceptions) {
        string root = Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_ARTIFACT_ROOT")
            ?? "C:/dev/helworks/builds/helengine-ps1/menu-20261007/texturedraytracing/numeric-invariant-overload";
        string fixture = Path.Combine(root, name);
        Directory.CreateDirectory(fixture);
        File.WriteAllText(Path.Combine(fixture, "Fixture.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework><Nullable>disable</Nullable></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(fixture, "Fixture.cs"), source);
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.WriteConversionReport = true;
        options.PlatformOptionValues = new Dictionary<string, string> {
            [CPPCodegenOptionNames.UseExceptions] = exceptions ? "true" : "false"
        };
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(Path.Combine(fixture, "Fixture.csproj"));
        string output = Path.Combine(fixture, "generated");
        converter.WriteOutput(output);
        return output;
    }
}
