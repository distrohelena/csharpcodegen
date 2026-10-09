using System.Text.Json;

namespace cs2.cpp.tests;

/// <summary>Exercises checked operand preparation in lazy conditional expressions through real project conversion.</summary>
public sealed class CPPConditionalOperandPreparationTests {
    /// <summary>Generates selected checked branches, nullable branches, a prepared condition and nested ternaries for native execution.</summary>
    [Fact]
    public void WriteOutput_CheckedConditionalOperands_KeepPreparationInsideSelectedBranch() {
        string artifactRoot = Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_ARTIFACT_ROOT")
            ?? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "builds", "csharpcodegen", "conditional-operands"));
        string fixtureRoot = Path.Combine(artifactRoot, "conditional-operands");
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
            public class ConditionalFixture {
                public static int Trace;
                static bool Condition(bool selected) { Trace = Trace * 10 + 1; return selected; }
                static int Mark(int digit, int value) { Trace = Trace * 10 + digit; return value; }
                public static int Select(bool selected, int left) {
                    return Condition(selected) ? checked(Mark(2, left) + Mark(3, 1)) : checked(Mark(4, 10) + Mark(5, 20));
                }
                public static int Assign(bool selected, int left) {
                    int value = selected ? checked(Mark(2, left) + Mark(3, 1)) : checked(Mark(4, 10) + Mark(5, 20));
                    return value;
                }
                public static int PreparedCondition() {
                    return checked(Mark(1, 1) + Mark(2, 1)) == 2 ? checked(Mark(3, 3) + Mark(4, 4)) : Mark(5, 5);
                }
                public static int Nested(bool outer, bool inner) {
                    return Condition(outer) ? (Condition(inner) ? checked(Mark(2, 2) + Mark(3, 3)) : Mark(4, 4)) : Mark(5, 5);
                }
                public static int? MaybeValue(bool selected, int left) {
                    return Condition(selected) ? checked(Mark(2, left) + Mark(3, 1)) : null;
                }
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
        string source = File.ReadAllText(Path.Combine(outputRoot, "ConditionalFixture.cpp"));
        Assert.Contains(" ? ([&]() {", source);
        Assert.Contains(" : ([&]() {", source);
        Assert.Contains("const auto __checked_left_", source);
        Assert.Contains("Number::CheckedAdd(__checked_left_", source);
        File.WriteAllText(Path.Combine(outputRoot, "native-harness.cpp"), """
            #include "ConditionalFixture.hpp"
            #include "runtime/native_exceptions.hpp"
            #include <cassert>
            #include <limits>
            int main() {
                ConditionalFixture::Trace = 0;
                assert(ConditionalFixture::Select(true, 5) == 6 && ConditionalFixture::Trace == 123);
                ConditionalFixture::Trace = 0;
                assert(ConditionalFixture::Select(false, std::numeric_limits<int32_t>::max()) == 30 && ConditionalFixture::Trace == 145);
                ConditionalFixture::Trace = 0;
                bool overflow = false;
                try { ConditionalFixture::Select(true, std::numeric_limits<int32_t>::max()); }
                catch (const OverflowException&) { overflow = true; }
                assert(overflow && ConditionalFixture::Trace == 123);
                ConditionalFixture::Trace = 0;
                assert(ConditionalFixture::Assign(false, std::numeric_limits<int32_t>::max()) == 30 && ConditionalFixture::Trace == 45);
                ConditionalFixture::Trace = 0;
                assert(ConditionalFixture::PreparedCondition() == 7 && ConditionalFixture::Trace == 1234);
                ConditionalFixture::Trace = 0;
                assert(ConditionalFixture::Nested(true, true) == 5 && ConditionalFixture::Trace == 1123);
                ConditionalFixture::Trace = 0;
                assert(ConditionalFixture::Nested(true, false) == 4 && ConditionalFixture::Trace == 114);
                ConditionalFixture::Trace = 0;
                assert(ConditionalFixture::Nested(false, true) == 5 && ConditionalFixture::Trace == 15);
                ConditionalFixture::Trace = 0;
                auto empty = ConditionalFixture::MaybeValue(false, std::numeric_limits<int32_t>::max());
                assert(!empty.get_HasValue() && ConditionalFixture::Trace == 1);
                ConditionalFixture::Trace = 0;
                auto value = ConditionalFixture::MaybeValue(true, 5);
                assert(value.get_HasValue() && value.get_Value() == 6 && ConditionalFixture::Trace == 123);
            }
            """);
    }
}
