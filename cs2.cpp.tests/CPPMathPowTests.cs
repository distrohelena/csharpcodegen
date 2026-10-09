using System.Text.Json;

namespace cs2.cpp.tests;

/// <summary>
/// Regresses generated Math.Pow calls through both hosted and custom double-precision providers.
/// </summary>
public class CPPMathPowTests {
    /// <summary>
    /// Emits a real math fixture and native harness for each provider, including argument ordering and checked operand preparation.
    /// </summary>
    /// <param name="useStandardMath">Whether the emitted runtime dispatches to std::pow or the caller's global pow hook.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WriteOutput_Pow_ProvidesDoubleRuntimeAndPreservesArgumentOrder(bool useStandardMath) {
        string artifactRoot = Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_ARTIFACT_ROOT")
            ?? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "builds", "csharpcodegen", "math-pow"));
        string fixtureRoot = Path.Combine(artifactRoot, "math-pow", useStandardMath ? "standard" : "custom");
        string outputRoot = Path.Combine(fixtureRoot, "generated");
        Directory.CreateDirectory(fixtureRoot);
        File.WriteAllText(Path.Combine(fixtureRoot, "Fixture.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework><Nullable>disable</Nullable></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(fixtureRoot, "Fixture.cs"), """
            using System;
            public class MathPowFixture {
                public static int Calls;
                public static int Order;
                static double Base() { Calls++; Order = Order * 10 + 1; return 2.0; }
                static double Exponent() { Calls++; Order = Order * 10 + 2; return 3.0; }
                public static double Ordered() { return Math.Pow(Base(), Exponent()); }
                public static double Named() { return Math.Pow(y: Exponent(), x: Base()); }
                public static double Nested() { return Math.Pow(Math.Pow(Base(), Exponent()), 2); }
                public static double Checked(int value) { return Math.Pow(checked(value + 1), 2); }
                public static double Power(double value, double exponent) { return Math.Pow(value, exponent); }
            }
            """);
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.WriteConversionReport = true;
        options.RuntimeProfile.UseExceptions = true;
        options.RuntimeProfile.UseStdMath = useStandardMath;
        if (!useStandardMath) {
            options.PlatformOptionValues = new Dictionary<string, string> {
                [CPPCodegenOptionNames.RuntimeMathHeader] = "X360Math.hpp"
            };
            options.PlatformProfile = CPPPlatformProfile.CreateCustomHeadless("x360", false,
                CPPGeneratedMathConventionKind.EngineRowVector, 4);
        }
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(Path.Combine(fixtureRoot, "Fixture.csproj"));
        converter.WriteOutput(outputRoot);
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputRoot, "cpp-conversion-report.json")));
        Assert.False(report.RootElement.GetProperty("hasErrors").GetBoolean());
        string output = File.ReadAllText(Path.Combine(outputRoot, "MathPowFixture.cpp"));
        Assert.Contains("Math::Pow(", output);
        Assert.Contains("const double __pow_arg_", output);
        string runtime = File.ReadAllText(Path.Combine(outputRoot, "system", "math.hpp"));
        Assert.Contains("return std::pow(value, exponent);", runtime);
        Assert.Contains("return pow(value, exponent);", runtime);
        if (!useStandardMath) {
            File.WriteAllText(Path.Combine(fixtureRoot, "no-pow-provider.hpp"), """
#include <math.h>
#define pow hel_test_provider_has_no_pow
""");
            File.WriteAllText(Path.Combine(fixtureRoot, "no-pow-harness.cpp"), """
#include "helcpp_config.hpp"
#undef HE_CPP_RUNTIME_MATH_HEADER
#define HE_CPP_RUNTIME_MATH_HEADER "no-pow-provider.hpp"
#include "system/math.hpp"
#include <cassert>
int main() { assert(Math::Sin(0.0) == 0.0); return 0; }
""");
        }
        File.WriteAllText(Path.Combine(fixtureRoot, "runtime-harness.cpp"), """
            #include "MathPowFixture.hpp"
            #include "runtime/native_exceptions.hpp"
            #include <cassert>
            #include <cmath>
            #include <limits>
            int main() {
                MathPowFixture::Calls = 0; MathPowFixture::Order = 0;
                assert(MathPowFixture::Ordered() == 8.0);
                assert(MathPowFixture::Calls == 2 && MathPowFixture::Order == 12);
                MathPowFixture::Calls = 0; MathPowFixture::Order = 0;
                assert(MathPowFixture::Named() == 8.0);
                assert(MathPowFixture::Calls == 2 && MathPowFixture::Order == 21);
                MathPowFixture::Calls = 0; MathPowFixture::Order = 0;
                assert(MathPowFixture::Nested() == 64.0);
                assert(MathPowFixture::Calls == 2 && MathPowFixture::Order == 12);
                assert(MathPowFixture::Checked(2) == 9.0);
                bool overflow = false;
                try { MathPowFixture::Checked(std::numeric_limits<int32_t>::max()); }
                catch (const OverflowException&) { overflow = true; }
                assert(overflow);
                assert(MathPowFixture::Power(2.0,-34.0) == 1.0/17179869184.0);
                assert(MathPowFixture::Power(2.0,-24.0) == 1.0/16777216.0);
                assert(MathPowFixture::Power(1.5,3.0) == 3.375);
                assert(MathPowFixture::Power(-2.0,3.0) == -8.0);
                assert(MathPowFixture::Power(0.0,0.0) == 1.0);
                assert(std::isinf(MathPowFixture::Power(0.0,-1.0)));
                assert(std::isnan(MathPowFixture::Power(-1.0,0.5)));
                assert(std::signbit(MathPowFixture::Power(-0.0,3.0)));
            }
            """);
    }
}
