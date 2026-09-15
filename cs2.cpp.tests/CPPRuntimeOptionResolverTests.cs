using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies generic runtime capability options resolve after preset defaults and expose the selected provider contract.
/// </summary>
public sealed class CPPRuntimeOptionResolverTests {
    /// <summary>Requires an explicit mathematical ABI when hosted math is disabled.</summary>
    [Fact]
    public void Resolve_CustomMathRequiresHeader() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.PlatformOptionValues = new Dictionary<string, string> {
            [CPPCodegenOptionNames.UseStdMath] = "false"
        };
        ArgumentException error = Assert.Throws<ArgumentException>(() => CPPRuntimeOptionResolver.Resolve(options));
        Assert.Contains(CPPCodegenOptionNames.RuntimeMathHeader, error.Message);
        options.PlatformOptionValues = new Dictionary<string, string> {
            [CPPCodegenOptionNames.UseStdMath] = "false",
            [CPPCodegenOptionNames.RuntimeMathHeader] = "my_math.hpp",
            [CPPCodegenOptionNames.UseHostedFileSystem] = "false"
        };
        CPPRuntimeOptionResolver.Resolve(options);
        Assert.False(options.RuntimeProfile.UseStdMath);
        Assert.False(options.RuntimeProfile.UseHostedFileSystem);
    }
    /// <summary>
    /// Requires an explicit provider when delegates or monotonic timing cannot use the standard library.
    /// </summary>
    [Theory]
    [InlineData("codegen-use-std-function")]
    [InlineData("codegen-use-std-chrono")]
    [InlineData("codegen-use-std-shared-ptr")]
    [InlineData("codegen-use-std-unordered-set")]
    public void Resolve_CustomRuntimeServiceRequiresProvider(string optionName) {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.PlatformOptionValues = new Dictionary<string, string> { [optionName] = "false" };
        Assert.Throws<ArgumentException>(() => CPPRuntimeOptionResolver.Resolve(options));
    }

    /// <summary>
    /// Ensures caller-selected capability flags override the runtime profile supplied by a named preset.
    /// </summary>
    [Fact]
    public void Resolve_AppliesCallerCapabilityOverridesAfterPresetDefaults() {
        CPPConversionOptions options = new CPPConversionOptions {
            PresetId = "ps2-lite",
            FeatureCatalog = new CPPExternalFeatureCatalog(
                [
                    new CPPExternalFeatureDefinition("shaders", CPPFeatureMode.Auto, CPPFeatureConflictPolicy.Error),
                    new CPPExternalFeatureDefinition("debug_overlay", CPPFeatureMode.Auto, CPPFeatureConflictPolicy.Error)
                ],
                [],
                []),
            PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                [CPPCodegenOptionNames.RuntimeProviderHeader] = "platform/ps1/runtime.hpp",
                [CPPCodegenOptionNames.UseStdString] = "true",
                [CPPCodegenOptionNames.UseStdVector] = "false",
                [CPPCodegenOptionNames.UseStdUnorderedMap] = "true",
                [CPPCodegenOptionNames.UseExceptions] = "true",
                [CPPCodegenOptionNames.UseRtti] = "true"
            }
        };

        new CPPConversionPresetCatalog().ApplyTo(options);
        CPPRuntimeOptionResolver.Resolve(options);

        Assert.True(options.RuntimeProfile.UseStdString);
        Assert.False(options.RuntimeProfile.UseStdVector);
        Assert.True(options.RuntimeProfile.UseStdUnorderedMap);
        Assert.True(options.RuntimeProfile.UseExceptions);
        Assert.True(options.RuntimeProfile.UseRtti);
        Assert.Equal("platform/ps1/runtime.hpp", CPPRuntimeOptionResolver.GetProviderHeader(options));
    }

    /// <summary>
    /// Ensures malformed capability values fail instead of silently selecting a different runtime.
    /// </summary>
    [Fact]
    public void Resolve_WithInvalidCapabilityValue_ThrowsArgumentException() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            [CPPCodegenOptionNames.UseStdString] = "sometimes"
        };

        ArgumentException exception = Assert.Throws<ArgumentException>(() => CPPRuntimeOptionResolver.Resolve(options));

        Assert.Contains(CPPCodegenOptionNames.UseStdString, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures restricted storage profiles require an explicit caller-owned provider header.
    /// </summary>
    [Fact]
    public void Resolve_WithRestrictedStorageAndNoProvider_ThrowsArgumentException() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            [CPPCodegenOptionNames.UseStdString] = "false"
        };

        ArgumentException exception = Assert.Throws<ArgumentException>(() => CPPRuntimeOptionResolver.Resolve(options));

        Assert.Contains(CPPCodegenOptionNames.RuntimeProviderHeader, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the selected string spelling remains standard-library compatible by default and switches to the shared runtime alias when requested.
    /// </summary>
    [Fact]
    public void ResolveStringTypeName_UsesDefaultAndProviderSpellings() {
        CPPConversionOptions standardOptions = CPPConversionOptions.CreateDefault();
        CPPRuntimeOptionResolver.Resolve(standardOptions);

        CPPConversionOptions providerOptions = CPPConversionOptions.CreateDefault();
        providerOptions.PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            [CPPCodegenOptionNames.RuntimeProviderHeader] = "platform/runtime.hpp",
            [CPPCodegenOptionNames.UseStdString] = "false"
        };
        CPPRuntimeOptionResolver.Resolve(providerOptions);

        Assert.Equal("std::string", CPPRuntimeOptionResolver.GetStringTypeName(standardOptions));
        Assert.Equal("HeCppString", CPPRuntimeOptionResolver.GetStringTypeName(providerOptions));
    }

    /// <summary>
    /// Ensures caller-selected unordered-set storage can switch to the provider-backed container.
    /// </summary>
    [Fact]
    public void Resolve_AppliesUnorderedSetCapabilityOverride() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            [CPPCodegenOptionNames.RuntimeProviderHeader] = "platform/runtime.hpp",
            [CPPCodegenOptionNames.UseStdUnorderedSet] = "false"
        };

        CPPRuntimeOptionResolver.Resolve(options);

        Assert.False(options.RuntimeProfile.UseStdUnorderedSet);
    }
}


