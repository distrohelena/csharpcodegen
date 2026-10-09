using System.Text.Json;
using System.Text.RegularExpressions;

namespace cs2.cpp.tests;

/// <summary>
/// Converts binary precedence, partial enum switches and fixed-width literals through the production emitter.
/// </summary>
public sealed class CPPBinaryGroupingAndWidthRegressionTests {
    /// <summary>
    /// Produces native fixtures that retain lazy boolean evaluation, enum no-op branches and C# integer widths on ILP32.
    /// </summary>
    [Fact]
    public void WriteOutput_BinaryGroupingPartialEnumSwitchAndWideLiterals_PreserveSourceSemantics() {
        string artifactRoot = Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_ARTIFACT_ROOT")
            ?? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "builds", "csharpcodegen", "binary-grouping-width"));
        string fixtureRoot = Path.Combine(artifactRoot, "binary-grouping-width");
        string outputRoot = Path.Combine(fixtureRoot, "generated");
        Directory.CreateDirectory(fixtureRoot);
        File.WriteAllText(Path.Combine(fixtureRoot, "Fixture.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>disable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(fixtureRoot, "Fixture.cs"), """
            public enum PartialMode : uint { Hit = 1, Miss = 2, High = 0x80000000 }
            public class BinaryWidthFixture {
                public static int Trace;
                static bool Mark(int digit, bool value) { Trace = Trace * 10 + digit; return value; }
                public static bool Lazy(bool first, bool second, bool third) {
                    return Mark(1, first) && Mark(2, second) || Mark(3, third);
                }
                public static bool Grouped(bool a, bool b, bool c, bool d) { return a && b || c && d; }
                public static int Shift(int value) { return value + 128 >> 8; }
                public static int PartialSwitch(PartialMode mode) {
                    int result = 7;
                    switch (mode) { case PartialMode.Hit: result = 9; break; }
                    return result;
                }
                public static int ExplicitDefault(PartialMode mode) {
                    switch (mode) { case PartialMode.Hit: return 9; default: return 11; }
                }
                public static long WideProduct() { return 2L * int.MaxValue; }
                public static long SignedHex() { return 0x7FFFFFFFFFFFFFFFL; }
                public static long SignedBinary() { return 0b111111111111111111111111111111111111111111111111111111111111111L; }
                public static ulong UnsignedHex() { return 0x8000000000000001UL; }
                public static ulong UnsignedBinary() { return 0b1000000000000000000000000000000000000000000000000000000000000001UL; }
                public static uint Unsigned32() { return 0xFFFFFFFFU; }
            }
            """);
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.WriteConversionReport = true;
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(Path.Combine(fixtureRoot, "Fixture.csproj"));
        converter.WriteOutput(outputRoot);
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputRoot, "cpp-conversion-report.json")));
        Assert.False(report.RootElement.GetProperty("hasErrors").GetBoolean());
        string source = File.ReadAllText(Path.Combine(outputRoot, "BinaryWidthFixture.cpp"));
        Assert.Matches(@"\(\s*a\s*&&\s*b\s*\)\s*\|\|\s*\(\s*c\s*&&\s*d\s*\)", source);
        Assert.Matches(@"\(\s*value\s*\+\s*128\s*\)\s*>>\s*8", source);
        Assert.Matches(@"default:\s*(?:\{\s*)?break;", source);
        Assert.Matches(@"(?i)\b2LL\s*\*\s*2147483647\b", source);
        Assert.Contains("int64_t BinaryWidthFixture::WideProduct", source);
        Assert.Contains("uint64_t BinaryWidthFixture::UnsignedHex", source);
        Assert.Contains("uint32_t BinaryWidthFixture::Unsigned32", source);
        Assert.Equal(2, Regex.Matches(source, @"return\s+9223372036854775807LL;").Count);
        Assert.Equal(2, Regex.Matches(source, @"return\s+9223372036854775809ULL;").Count);
        Assert.Contains("4294967295U", source);
        File.WriteAllText(Path.Combine(outputRoot, "native-harness.cpp"), """
            #include "BinaryWidthFixture.hpp"
            #include <cassert>
            #include <cstdint>
            #include <limits>
            #include <type_traits>
            static_assert(sizeof(int64_t) == 8);
            static_assert(sizeof(uint64_t) == 8);
            static_assert(std::is_same<decltype(BinaryWidthFixture::WideProduct()), int64_t>::value);
            static_assert(std::is_same<decltype(BinaryWidthFixture::UnsignedHex()), uint64_t>::value);
            #if defined(__mips__)
            static_assert(sizeof(long) == 4, "The N64 check must exercise its actual ILP32 ABI.");
            #endif
            int main() {
                BinaryWidthFixture::Trace = 0;
                assert(BinaryWidthFixture::Lazy(true, true, false) && BinaryWidthFixture::Trace == 12);
                BinaryWidthFixture::Trace = 0;
                assert(BinaryWidthFixture::Lazy(false, true, true) && BinaryWidthFixture::Trace == 13);
                BinaryWidthFixture::Trace = 0;
                assert(!BinaryWidthFixture::Lazy(true, false, false) && BinaryWidthFixture::Trace == 123);
                for (int bits = 0; bits < 16; bits++) {
                    bool a = (bits & 1) != 0, b = (bits & 2) != 0, c = (bits & 4) != 0, d = (bits & 8) != 0;
                    assert(BinaryWidthFixture::Grouped(a, b, c, d) == ((a && b) || (c && d)));
                }
                assert(BinaryWidthFixture::Shift(128) == 1);
                assert(BinaryWidthFixture::Shift(127) == 0);
                assert(BinaryWidthFixture::PartialSwitch(PartialMode::Hit) == 9);
                assert(BinaryWidthFixture::PartialSwitch(PartialMode::Miss) == 7);
                assert(BinaryWidthFixture::PartialSwitch(static_cast<PartialMode>(255)) == 7);
                assert(BinaryWidthFixture::PartialSwitch(PartialMode::High) == 7);
                assert(BinaryWidthFixture::ExplicitDefault(PartialMode::Miss) == 11);
                assert(BinaryWidthFixture::WideProduct() == 4294967294LL);
                assert(BinaryWidthFixture::SignedHex() == std::numeric_limits<int64_t>::max());
                assert(BinaryWidthFixture::SignedBinary() == std::numeric_limits<int64_t>::max());
                assert(BinaryWidthFixture::UnsignedHex() == 0x8000000000000001ULL);
                assert(BinaryWidthFixture::UnsignedBinary() == 0x8000000000000001ULL);
                assert(BinaryWidthFixture::Unsigned32() == std::numeric_limits<uint32_t>::max());
            }
            """);
    }
}
