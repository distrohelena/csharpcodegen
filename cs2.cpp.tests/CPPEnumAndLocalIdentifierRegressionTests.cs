using System.Text.Json;

namespace cs2.cpp.tests;

/// <summary>
/// Exercises semantic enum storage and local keyword lowering through complete C# project conversion.
/// </summary>
public class CPPEnumAndLocalIdentifierRegressionTests {
    /// <summary>
    /// Preserves each legal C# enum storage type, including unsigned values beyond the signed 32-bit range.
    /// </summary>
    [Fact]
    public void WriteOutput_EnumUnderlyingTypes_PreserveIntegralStorage() {
        string outputRoot = GenerateFixture("enum-underlying-types", """
            public enum ByteEnum : byte { Maximum = 255 }
            public enum SByteEnum : sbyte { Minimum = -128 }
            public enum ShortEnum : short { Minimum = -32768 }
            public enum UShortEnum : ushort { Maximum = 65535 }
            public enum IntEnum { Minimum = -2147483648 }
            public enum UIntEnum : uint { Tiled = 0x80000000, Arbitrary = 0xA0000000 }
            public enum LongEnum : long { Minimum = -9223372036854775808L }
            public enum ULongEnum : ulong { Maximum = 18446744073709551615UL }
            """);
        AssertEnumStorage(outputRoot, "ByteEnum", "uint8_t");
        AssertEnumStorage(outputRoot, "SByteEnum", "int8_t");
        AssertEnumStorage(outputRoot, "ShortEnum", "int16_t");
        AssertEnumStorage(outputRoot, "UShortEnum", "uint16_t");
        AssertEnumStorage(outputRoot, "IntEnum", "int32_t");
        AssertEnumStorage(outputRoot, "UIntEnum", "uint32_t");
        AssertEnumStorage(outputRoot, "LongEnum", "int64_t");
        AssertEnumStorage(outputRoot, "ULongEnum", "uint64_t");
        string uintHeader = File.ReadAllText(Path.Combine(outputRoot, "UIntEnum.hpp"));
        Assert.Contains("Tiled = 0x80000000", uintHeader);
        Assert.Contains("Arbitrary = 0xA0000000", uintHeader);
        File.WriteAllText(Path.Combine(outputRoot, "native-harness.cpp"), """
            #include "ByteEnum.hpp"
            #include "SByteEnum.hpp"
            #include "ShortEnum.hpp"
            #include "UShortEnum.hpp"
            #include "IntEnum.hpp"
            #include "UIntEnum.hpp"
            #include "LongEnum.hpp"
            #include "ULongEnum.hpp"
            #include <type_traits>
            static_assert(std::is_same<std::underlying_type_t<ByteEnum>, uint8_t>::value);
            static_assert(std::is_same<std::underlying_type_t<SByteEnum>, int8_t>::value);
            static_assert(std::is_same<std::underlying_type_t<ShortEnum>, int16_t>::value);
            static_assert(std::is_same<std::underlying_type_t<UShortEnum>, uint16_t>::value);
            static_assert(std::is_same<std::underlying_type_t<IntEnum>, int32_t>::value);
            static_assert(std::is_same<std::underlying_type_t<UIntEnum>, uint32_t>::value);
            static_assert(std::is_same<std::underlying_type_t<LongEnum>, int64_t>::value);
            static_assert(std::is_same<std::underlying_type_t<ULongEnum>, uint64_t>::value);
            static_assert(static_cast<uint32_t>(UIntEnum::Tiled) == 0x80000000u);
            static_assert(static_cast<uint32_t>(UIntEnum::Arbitrary) == 0xA0000000u);
            int main() { return 0; }
            """);
    }

    /// <summary>
    /// Uses the same escaped spelling for typed, inferred, constant and loop local declarations and their references.
    /// </summary>
    [Fact]
    public void WriteOutput_LocalCppKeywords_SanitizeDeclarationsAndReferences() {
        string outputRoot = GenerateFixture("local-cpp-keywords", """
            public class KeywordFixture {
                public static long Read(long value) {
                    long signed = value;
                    long unsigned = signed + 1;
                    var typename = unsigned;
                    const long alignas = 2;
                    for (int template = 0; template < 1; template++) {
                        typename += alignas;
                    }
                    return typename;
                }
            }
            """);
        string source = File.ReadAllText(Path.Combine(outputRoot, "KeywordFixture.cpp"));
        Assert.Contains("int64_t signed_ =", source);
        Assert.Contains("int64_t unsigned_ =", source);
        Assert.Contains("signed_ + 1", source);
        Assert.Contains("typename_", source);
        Assert.Contains("alignas_", source);
        Assert.Contains("template_", source);
        Assert.DoesNotContain("int64_t signed =", source);
        Assert.DoesNotContain("int64_t unsigned =", source);
        File.WriteAllText(Path.Combine(outputRoot, "native-harness.cpp"), """
            #include "KeywordFixture.hpp"
            #include <cassert>
            int main() { assert(KeywordFixture::Read(5) == 8); }
            """);
    }

    /// <summary>
    /// Converts a real source project into a stable task artifact directory without rewriting emitted files.
    /// </summary>
    /// <param name="fixtureName">Fixed directory name identifying the regression fixture.</param>
    /// <param name="source">Valid C# source to compile and lower through the production converter.</param>
    /// <returns>The generated output directory containing the converter's headers, sources and report.</returns>
    static string GenerateFixture(string fixtureName, string source) {
        string artifactRoot = Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_ARTIFACT_ROOT")
            ?? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "builds", "csharpcodegen", "enum-local-regressions"));
        string fixtureRoot = Path.Combine(artifactRoot, fixtureName);
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
        File.WriteAllText(Path.Combine(fixtureRoot, "Fixture.cs"), source);
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.WriteConversionReport = true;
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(Path.Combine(fixtureRoot, "Fixture.csproj"));
        converter.WriteOutput(outputRoot);
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputRoot, "cpp-conversion-report.json")));
        Assert.False(report.RootElement.GetProperty("hasErrors").GetBoolean());
        return outputRoot;
    }

    /// <summary>
    /// Checks the precise integral type used in an emitted enum declaration.
    /// </summary>
    /// <param name="outputRoot">Directory containing the generated enum header.</param>
    /// <param name="enumName">Source and emitted enum name.</param>
    /// <param name="storageType">Expected fixed-width C++ integral type.</param>
    static void AssertEnumStorage(string outputRoot, string enumName, string storageType) {
        string header = File.ReadAllText(Path.Combine(outputRoot, enumName + ".hpp"));
        Assert.Contains("enum class " + enumName + " : " + storageType, header);
    }
}
