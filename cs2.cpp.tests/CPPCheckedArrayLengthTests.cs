using System.Text.Json;

namespace cs2.cpp.tests;

/// <summary>
/// Regresses checked array lengths whose operand captures must survive allocation lowering.
/// </summary>
public class CPPCheckedArrayLengthTests {
    /// <summary>
    /// Generates single-dimensional, jagged and property-based lengths and leaves a native runtime harness for compilation.
    /// </summary>
    [Fact]
    public void WriteOutput_CheckedArrayLengths_PreserveOperandEvaluationStatements() {
        string artifactRoot = Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_ARTIFACT_ROOT")
            ?? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "builds", "csharpcodegen", "checked-array-lengths"));
        string fixtureRoot = Path.Combine(artifactRoot, "checked-array-lengths");
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
            public class CheckedArrayFixture {
                public static int Calls;
                public static int Order;
                static int Left() { Calls++; Order = Order * 10 + 1; return 2; }
                static int Right() { Calls++; Order = Order * 10 + 2; return 3; }
                static int Size { get { Calls++; return 3; } }
                public static byte[] Create() { return new byte[checked(Left() + Right())]; }
                public static byte[][] CreateJagged() { return new byte[checked(Left() + Right())][]; }
                public static byte[] CreateProperty() { return new byte[checked(2 + Size)]; }
                public static byte[] CreateOverflow(int value) { return new byte[checked(value + Right())]; }
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
        string output = File.ReadAllText(Path.Combine(outputRoot, "CheckedArrayFixture.cpp"));
        Assert.Contains("const auto __checked_left_", output);
        Assert.Contains("const auto __checked_right_", output);
        Assert.Contains("new Array<uint8_t>", output);
        Assert.Contains("new Array<Array<uint8_t>*>", output);
        Assert.Contains("Number::CheckedAdd(__checked_left_", output);
        File.WriteAllText(Path.Combine(fixtureRoot, "runtime-harness.cpp"), """
            #include "CheckedArrayFixture.hpp"
            #include "runtime/native_exceptions.hpp"
            #include <cassert>
            #include <limits>
            int main() {
                CheckedArrayFixture::Calls = 0; CheckedArrayFixture::Order = 0;
                auto* single = CheckedArrayFixture::Create();
                assert(single->Length == 5 && CheckedArrayFixture::Calls == 2 && CheckedArrayFixture::Order == 12);
                delete single;
                CheckedArrayFixture::Calls = 0; CheckedArrayFixture::Order = 0;
                auto* jagged = CheckedArrayFixture::CreateJagged();
                assert(jagged->Length == 5 && CheckedArrayFixture::Calls == 2 && CheckedArrayFixture::Order == 12);
                delete jagged;
                CheckedArrayFixture::Calls = 0;
                auto* property = CheckedArrayFixture::CreateProperty();
                assert(property->Length == 5 && CheckedArrayFixture::Calls == 1);
                delete property;
                CheckedArrayFixture::Calls = 0; CheckedArrayFixture::Order = 0;
                bool overflow = false;
                try { auto* invalid = CheckedArrayFixture::CreateOverflow(std::numeric_limits<int32_t>::max()); delete invalid; }
                catch (const OverflowException&) { overflow = true; }
                assert(overflow && CheckedArrayFixture::Calls == 1 && CheckedArrayFixture::Order == 2);
            }
            """);
    }
}
