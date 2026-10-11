using System.Text.Json;

namespace cs2.cpp.tests;

/// <summary>
/// Converts checked texture-size arithmetic through the production emitter and emits a native execution fixture for overflow and promotion boundaries.
/// </summary>
public sealed class CPPCheckedBinaryArithmeticRegressionTests {
    /// <summary>
    /// Verifies multiplication, subtraction, division and mixed-width addition retain C# result types, exceptions and operand evaluation order.
    /// </summary>
    [Fact]
    public void WriteOutput_CheckedBinaryArithmetic_PreservesPromotionsAndFailures() {
        string artifactRoot = Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_ARTIFACT_ROOT")
            ?? throw new InvalidOperationException("The fixed regression artifact root must be configured.");
        string fixtureRoot = Path.Combine(artifactRoot, "checked-binary");
        string outputRoot = Path.Combine(fixtureRoot, "generated");
        Directory.CreateDirectory(fixtureRoot);
        File.WriteAllText(Path.Combine(fixtureRoot, "Fixture.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(fixtureRoot, "Fixture.cs"), """
            public class CheckedBinaryFixture {
                public static int Trace;
                public static int Multiply(int a, int b) { return checked(a * b); }
                public static long Multiply64(long a, long b) { return checked(a * b); }
                public static uint MultiplyU32(uint a, uint b) { return checked(a * b); }
                public static ulong MultiplyU64(ulong a, ulong b) { return checked(a * b); }
                public static int Subtract(int a, int b) { return checked(a - b); }
                public static long Subtract64(long a, long b) { return checked(a - b); }
                public static uint SubtractU32(uint a, uint b) { return checked(a - b); }
                public static ulong SubtractU64(ulong a, ulong b) { return checked(a - b); }
                public static int Divide(int a, int b) { return checked(a / b); }
                public static long Divide64(long a, long b) { return checked(a / b); }
                public static uint DivideU32(uint a, uint b) { return checked(a / b); }
                public static ulong DivideU64(ulong a, ulong b) { return checked(a / b); }
                public static long AddMixed(long a, int b) { return checked(a + b); }
                public static long MixedUnsigned(uint a, int b) { return checked(a * b); }
                public static int PixelLength(int width, int height, int bits) { return checked((int)(((long)width * height * bits + 7) / 8)); }
                public static int Blocks(int w, int h, int block) { return checked(((w + block - 1) / block) * ((h + block - 1) / block) * 32); }
                public static int Conditional(bool large, int a, int b) { return large ? checked(a * b) : checked(a - b); }
                static int Mark(int digit, int value) { Trace = Trace * 10 + digit; return value; }
                public static int Ordered(int a, int b) { return checked(Mark(1, a) * Mark(2, b)); }
                public static double Floating(double a, double b) { return checked(a * b); }
                public static int Unchecked(int a, int b) { return unchecked(a * b); }
            }
            """);
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.WriteConversionReport = true;
        options.RuntimeProfile.UseExceptions = true;
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(Path.Combine(fixtureRoot, "Fixture.csproj"));
        converter.WriteOutput(outputRoot);
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputRoot, "cpp-conversion-report.json")));
        Assert.False(report.RootElement.GetProperty("hasErrors").GetBoolean());
        Assert.Equal(0, report.RootElement.GetProperty("errorCount").GetInt32());
        string source = File.ReadAllText(Path.Combine(outputRoot, "CheckedBinaryFixture.cpp"));
        Assert.Contains("Number::CheckedMultiply<int32_t>", source);
        Assert.Contains("Number::CheckedMultiply<int64_t>", source);
        Assert.Contains("Number::CheckedSubtract<uint64_t>", source);
        Assert.Contains("Number::CheckedDivide<int64_t>", source);
        Assert.Contains("static_cast<int64_t>(b)", source);
        File.WriteAllText(Path.Combine(outputRoot, "native-harness.cpp"), """
            #include "CheckedBinaryFixture.hpp"
            #include <cassert>
            #include <cstdint>
            #include <limits>
            #include <cmath>
            template<typename TException, typename TCall> void Expect(TCall call) {
                bool caught = false;
                try { call(); } catch (const TException&) { caught = true; }
                assert(caught);
            }
            int main() {
                const int32_t min = std::numeric_limits<int32_t>::min(), max = std::numeric_limits<int32_t>::max();
                const int64_t min64 = std::numeric_limits<int64_t>::min(), max64 = std::numeric_limits<int64_t>::max();
                const uint32_t umax = std::numeric_limits<uint32_t>::max();
                const uint64_t umax64 = std::numeric_limits<uint64_t>::max();
                assert(CheckedBinaryFixture::Multiply(min, 1) == min);
                assert(CheckedBinaryFixture::Multiply(max, 1) == max);
                assert(CheckedBinaryFixture::Multiply(-3, -4) == 12);
                assert(CheckedBinaryFixture::Multiply(min, 0) == 0);
                Expect<OverflowException>([&] { CheckedBinaryFixture::Multiply(max, 2); });
                Expect<OverflowException>([&] { CheckedBinaryFixture::Multiply(min, -1); });
                Expect<OverflowException>([&] { CheckedBinaryFixture::Multiply(-1, min); });
                Expect<OverflowException>([&] { CheckedBinaryFixture::Multiply(min, 2); });
                Expect<OverflowException>([&] { CheckedBinaryFixture::Multiply(2, min); });
                assert(CheckedBinaryFixture::Multiply64(min64, 1) == min64);
                Expect<OverflowException>([&] { CheckedBinaryFixture::Multiply64(min64, -1); });
                Expect<OverflowException>([&] { CheckedBinaryFixture::Multiply64(max64, 2); });
                assert(CheckedBinaryFixture::MultiplyU32(umax, 1) == umax);
                Expect<OverflowException>([&] { CheckedBinaryFixture::MultiplyU32(umax, 2); });
                assert(CheckedBinaryFixture::MultiplyU64(umax64, 0) == 0);
                Expect<OverflowException>([&] { CheckedBinaryFixture::MultiplyU64(umax64, 2); });
                assert(CheckedBinaryFixture::Subtract(min, 0) == min);
                assert(CheckedBinaryFixture::Subtract(max, max) == 0);
                Expect<OverflowException>([&] { CheckedBinaryFixture::Subtract(min, 1); });
                Expect<OverflowException>([&] { CheckedBinaryFixture::Subtract(max, -1); });
                Expect<OverflowException>([&] { CheckedBinaryFixture::Subtract64(min64, 1); });
                Expect<OverflowException>([&] { CheckedBinaryFixture::Subtract64(max64, -1); });
                assert(CheckedBinaryFixture::SubtractU32(umax, 1) == umax - 1);
                Expect<OverflowException>([&] { CheckedBinaryFixture::SubtractU32(0, 1); });
                Expect<OverflowException>([&] { CheckedBinaryFixture::SubtractU64(0, umax64); });
                assert(CheckedBinaryFixture::Divide(-7, 2) == -3);
                assert(CheckedBinaryFixture::Divide64(min64, 1) == min64);
                assert(CheckedBinaryFixture::DivideU32(umax, 1) == umax);
                assert(CheckedBinaryFixture::DivideU64(umax64, 1) == umax64);
                Expect<DivideByZeroException>([&] { CheckedBinaryFixture::Divide(1, 0); });
                Expect<DivideByZeroException>([&] { CheckedBinaryFixture::DivideU64(1, 0); });
                Expect<OverflowException>([&] { CheckedBinaryFixture::Divide(min, -1); });
                Expect<OverflowException>([&] { CheckedBinaryFixture::Divide64(min64, -1); });
                assert(CheckedBinaryFixture::AddMixed(2147483647LL, 7) == 2147483654LL);
                Expect<OverflowException>([&] { CheckedBinaryFixture::AddMixed(max64, 1); });
                assert(CheckedBinaryFixture::MixedUnsigned(umax, -1) == -4294967295LL);
                assert(CheckedBinaryFixture::PixelLength(1024, 1024, 4) == 524288);
                Expect<OverflowException>([&] { CheckedBinaryFixture::PixelLength(max, max, max); });
                assert(CheckedBinaryFixture::Blocks(5, 7, 4) == 128);
                assert(CheckedBinaryFixture::Conditional(false, 7, 2) == 5);
                assert(CheckedBinaryFixture::Conditional(true, 7, 2) == 14);
                Expect<OverflowException>([&] { CheckedBinaryFixture::Conditional(false, max, -1); });
                CheckedBinaryFixture::Trace = 0;
                assert(CheckedBinaryFixture::Ordered(3, 4) == 12 && CheckedBinaryFixture::Trace == 12);
                CheckedBinaryFixture::Trace = 0;
                Expect<OverflowException>([&] { CheckedBinaryFixture::Ordered(max, 2); });
                assert(CheckedBinaryFixture::Trace == 12);
                assert(std::isinf(CheckedBinaryFixture::Floating(std::numeric_limits<double>::max(), 2)));
                assert(CheckedBinaryFixture::Unchecked(3, 4) == 12);
            }
            """);
        string fatalOutputRoot = Path.Combine(fixtureRoot, "generated-fatal");
        CPPConversionOptions fatalOptions = CPPConversionOptions.CreateDefault();
        fatalOptions.LoadNativeRuntimeMetadata = false;
        fatalOptions.WriteConversionReport = true;
        fatalOptions.RuntimeProfile.UseExceptions = false;
        CPPCodeConverter fatalConverter = new CPPCodeConverter(new CPPConversionRules(), fatalOptions);
        fatalConverter.AddCsproj(Path.Combine(fixtureRoot, "Fixture.csproj"));
        fatalConverter.WriteOutput(fatalOutputRoot);
        using JsonDocument fatalReport = JsonDocument.Parse(File.ReadAllText(Path.Combine(fatalOutputRoot, "cpp-conversion-report.json")));
        Assert.False(fatalReport.RootElement.GetProperty("hasErrors").GetBoolean());
        File.WriteAllText(Path.Combine(fatalOutputRoot, "native-harness.cpp"), """
            #include "CheckedBinaryFixture.hpp"
            #include <cassert>
            #include <limits>
            int main(int argc, char**) {
                if (argc > 2) return CheckedBinaryFixture::Divide(1, 0);
                if (argc > 1) return CheckedBinaryFixture::Multiply(std::numeric_limits<int32_t>::max(), 2);
                assert(CheckedBinaryFixture::PixelLength(1024, 1024, 4) == 524288);
                assert(CheckedBinaryFixture::Multiply(-3, -4) == 12);
                assert(CheckedBinaryFixture::MixedUnsigned(std::numeric_limits<uint32_t>::max(), -1) == -4294967295LL);
            }
            """);
    }
}
