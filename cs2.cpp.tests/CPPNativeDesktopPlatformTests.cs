using codegen;

namespace cs2.cpp.tests;

/// <summary>Verifies native Linux and Apple consumers can use the generic platform contract without changing their numeric or matrix ABI.</summary>
public sealed class CPPNativeDesktopPlatformTests {
    /// <summary>Converts an actual source project for each native target and preserves its selected numeric identity in generated output.</summary>
    /// <param name="platformId">Public target identifier supplied by the platform builder.</param>
    /// <param name="numericBits">Selected storage and arithmetic width.</param>
    [Theory]
    [InlineData("linux", 16)]
    [InlineData("linux", 32)]
    [InlineData("linux", 64)]
    [InlineData("macos", 16)]
    [InlineData("macos", 32)]
    [InlineData("macos", 64)]
    [InlineData("ios", 16)]
    [InlineData("ios", 32)]
    [InlineData("ios", 64)]
    public void ConvertGenericNativeTargetPreservesAbi(string platformId, int numericBits) {
        string fixtureRoot = Path.Combine(Path.GetTempPath(), nameof(CPPNativeDesktopPlatformTests));
        string projectPath = Path.Combine(fixtureRoot, "Native Fixture.csproj");
        string outputPath = Path.Combine(fixtureRoot, "generated");
        Directory.CreateDirectory(fixtureRoot);
        try {
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net9.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>disable</Nullable>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(fixtureRoot, "NativeFixture.cs"), """
                namespace NativeFixture;
                public sealed class Counter {
                    public int Add(int left, int right) => left + right;
                }
                """);
            string[] arguments = [
                "--cpp", "--project", projectPath, "--output", outputPath,
                "--platform", platformId, "--compiler", "gcc", "--runtime", "stl-lite", "--endianness", "little",
                "--set", "generated-math-convention=engine-row-vector", "--set", "pointer-size-bytes=8",
                "--set", $"real-storage-bits={numericBits}", "--set", $"real-arithmetic-bits={numericBits}",
                "--set", "load-native-runtime-metadata=false"
            ];
            Assert.True(CodegenCliArgumentParser.TryParseArguments(arguments, out CodegenCliParsedArguments parsed, out string error), error);
            CPPConversionOptions options = CodegenCliOptionsBuilder.CreateConversionOptions(parsed);
            Assert.Equal(CPPPlatformKind.CustomHeadless, options.PlatformProfile.Kind);
            Assert.Equal(CPPGeneratedMathConventionKind.EngineRowVector, options.PlatformProfile.GeneratedMathConvention);
            Assert.Equal(8, options.PlatformProfile.PointerSizeInBytes);

            CPPCodeConverter converter = new(new CPPConversionRules(), options);
            converter.AddCsproj(projectPath);
            converter.WriteOutput(outputPath);

            Assert.False(converter.Report.HasErrors);
            Assert.True(File.Exists(Path.Combine(outputPath, "Counter.hpp")));
            string config = File.ReadAllText(Path.Combine(outputPath, CPPGeneratedConfigWriter.DefaultFileName));
            Assert.Contains($"#define HE_CPP_PLATFORM_{platformId.ToUpperInvariant()} 1", config);
            Assert.Contains("#define HE_CPP_PLATFORM_IS_LITTLE_ENDIAN 1", config);
            Assert.Contains("#define HE_CPP_PLATFORM_IS_WINDOWS_HOST 0", config);
            Assert.Contains($"#define HE_CPP_REAL_STORAGE_BITS {numericBits}", config);
            Assert.Contains($"#define HE_CPP_REAL_ARITHMETIC_BITS {numericBits}", config);
            Assert.Contains($"real-v1-s{numericBits}-a{numericBits}", config);
        } finally {
            Directory.Delete(fixtureRoot, true);
        }
    }

    /// <summary>Rejects incomplete custom platform metadata before conversion instead of inheriting the Windows pointer and matrix ABI.</summary>
    /// <param name="platformId">Public target identifier.</param>
    /// <param name="missingOption">Required platform shape setting omitted by the caller.</param>
    [Theory]
    [InlineData("linux", "generated-math-convention")]
    [InlineData("macos", "pointer-size-bytes")]
    [InlineData("ios", "generated-math-convention")]
    public void MissingPlatformShapeFailsBeforeConversion(string platformId, string missingOption) {
        CodegenCliParsedArguments parsed = CreateNativeArguments(platformId);
        parsed.SelectedOptions.Remove(missingOption);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => CodegenCliOptionsBuilder.CreateConversionOptions(parsed));

        Assert.Contains(missingOption, exception.Message);
    }

    /// <summary>Rejects unsupported numeric combinations on every native target instead of widening configurable arithmetic.</summary>
    /// <param name="platformId">Public target identifier.</param>
    [Theory]
    [InlineData("linux")]
    [InlineData("macos")]
    [InlineData("ios")]
    public void UnsupportedHalfPolicyFailsBeforeConversion(string platformId) {
        CodegenCliParsedArguments parsed = CreateNativeArguments(platformId);
        parsed.SelectedOptions["real-storage-bits"] = "16";
        parsed.SelectedOptions["real-arithmetic-bits"] = "64";

        Assert.Throws<NotSupportedException>(() => CodegenCliOptionsBuilder.CreateConversionOptions(parsed));
    }

    /// <summary>Creates only the explicit caller-owned metadata required to resolve one native platform shape.</summary>
    /// <param name="platformId">Public target identifier.</param>
    /// <returns>Arguments whose platform shape matches 64-bit little-endian native Linux and Apple targets.</returns>
    static CodegenCliParsedArguments CreateNativeArguments(string platformId) {
        CodegenCliParsedArguments parsed = new() {
            PlatformId = platformId,
            CompilerProfileName = "gcc",
            RuntimeProfileName = "stl-lite",
            Endianness = "little"
        };
        parsed.SelectedOptions["generated-math-convention"] = "engine-row-vector";
        parsed.SelectedOptions["pointer-size-bytes"] = "8";
        return parsed;
    }
}
