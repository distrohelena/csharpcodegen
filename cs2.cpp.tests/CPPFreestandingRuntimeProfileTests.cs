using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies the freestanding runtime profile, its resolver defaults, its generated config defines and its preset.
/// </summary>
public sealed class CPPFreestandingRuntimeProfileTests {
    /// <summary>The freestanding profile disables every hosted facility.</summary>
    [Fact]
    public void CreateFreestanding_DisablesEveryHostedFacility() {
        CPPRuntimeProfile profile = CPPRuntimeProfile.CreateFreestanding();

        Assert.Equal(CPPRuntimeKind.Freestanding, profile.Kind);
        Assert.Equal("freestanding", profile.Name);
        Assert.Equal("HE_CPP_RUNTIME_FREESTANDING", profile.DefineName);
        Assert.False(profile.UseStdString);
        Assert.False(profile.UseStdVector);
        Assert.False(profile.UseStdUnorderedMap);
        Assert.False(profile.UseStdUnorderedSet);
        Assert.False(profile.UseStdFunction);
        Assert.False(profile.UseStdChrono);
        Assert.False(profile.UseStdSharedPtr);
        Assert.False(profile.UseStdMath);
        Assert.False(profile.UseHostedFileSystem);
        Assert.False(profile.UseExceptions);
        Assert.False(profile.UseRtti);
    }

    /// <summary>The resolver supplies the codegen-owned provider and math headers when the caller gives none.</summary>
    [Fact]
    public void Resolve_FreestandingDefaultsProviderAndMathHeaders() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.RuntimeProfile = CPPRuntimeProfile.CreateFreestanding();
        options.PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        CPPRuntimeOptionResolver.Resolve(options);

        Assert.Equal("runtime/freestanding/freestanding_provider.hpp", CPPRuntimeOptionResolver.GetProviderHeader(options));
        Assert.Equal("runtime/freestanding/freestanding_math.hpp", options.PlatformOptionValues[CPPCodegenOptionNames.RuntimeMathHeader]);
    }

    /// <summary>A caller-supplied provider header wins over the freestanding default.</summary>
    [Fact]
    public void Resolve_FreestandingKeepsCallerProviderHeader() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.RuntimeProfile = CPPRuntimeProfile.CreateFreestanding();
        options.PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            [CPPCodegenOptionNames.RuntimeProviderHeader] = "platform/snes/SnesRuntimeProvider.hpp"
        };

        CPPRuntimeOptionResolver.Resolve(options);

        Assert.Equal("platform/snes/SnesRuntimeProvider.hpp", CPPRuntimeOptionResolver.GetProviderHeader(options));
    }

    /// <summary>The generated config announces the freestanding runtime and the absence of hosted services.</summary>
    [Fact]
    public void Write_FreestandingEmitsRuntimeAndHostedServiceDefines() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.RuntimeProfile = CPPRuntimeProfile.CreateFreestanding();
        CPPConversionReport report = new CPPConversionReport();
        CPPRuntimeRequirementRegistrar registrar = new CPPRuntimeRequirementRegistrar(new CPPRuntimeRequirementCatalog(), report);
        registrar.RegisterDefaults(options);
        string outputFolder = Path.Combine(Path.GetTempPath(), "cs2.cpp.tests", Guid.NewGuid().ToString("N"));

        string filePath = CPPGeneratedConfigWriter.Write(outputFolder, options, registrar);
        string output = File.ReadAllText(filePath);

        Assert.Contains("#define HE_CPP_RUNTIME_FREESTANDING 1", output);
        Assert.Contains("#define HE_CPP_RUNTIME_HAS_HOSTED_SERVICES 0", output);
        Assert.Contains("#define HE_CPP_USE_STD_MATH 0", output);
        Assert.Contains("#define HE_CPP_RUNTIME_PROVIDER_HEADER \"runtime/freestanding/freestanding_provider.hpp\"", output);
        Assert.Contains("#define HE_CPP_RUNTIME_MATH_HEADER \"runtime/freestanding/freestanding_math.hpp\"", output);
    }

    /// <summary>Hosted profiles keep announcing hosted services.</summary>
    [Fact]
    public void Write_StlLiteEmitsHostedServicesEnabled() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        CPPConversionReport report = new CPPConversionReport();
        CPPRuntimeRequirementRegistrar registrar = new CPPRuntimeRequirementRegistrar(new CPPRuntimeRequirementCatalog(), report);
        registrar.RegisterDefaults(options);
        string outputFolder = Path.Combine(Path.GetTempPath(), "cs2.cpp.tests", Guid.NewGuid().ToString("N"));

        string output = File.ReadAllText(CPPGeneratedConfigWriter.Write(outputFolder, options, registrar));

        Assert.Contains("#define HE_CPP_RUNTIME_HAS_HOSTED_SERVICES 1", output);
    }

    /// <summary>The freestanding core-boot preset combines the stripped core with the freestanding runtime and forbids hosted services.</summary>
    [Fact]
    public void Resolve_NativeCoreBootFreestandingPreset() {
        CPPConversionPreset preset = new CPPConversionPresetCatalog().Resolve("native-core-boot-freestanding");

        Assert.Equal(CPPRuntimeKind.Freestanding, preset.RuntimeProfile.Kind);
        Assert.True(preset.RestrictionProfile.ForbidHostedServices);
        Assert.True(preset.RestrictionProfile.ForbidShaders);
        Assert.True(preset.RestrictionProfile.ForbidRegex);
        Assert.Equal("retroppc", preset.PlatformProfile.Name.Replace("-headless", string.Empty));
    }
}
