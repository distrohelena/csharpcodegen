using cs2.cpp;
using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies runtime requirement registration and generated config header output for the C++ backend.
/// </summary>
public class CPPGeneratedConfigWriterTests {
    /// <summary>
    /// Ensures the registrar applies the default runtime requirements for the approved first milestone.
    /// </summary>
    [Fact]
    public void RegisterDefaults_RegistersHeadlessCoreRuntimeRequirements() {
        CPPConversionReport report = new CPPConversionReport();
        CPPRuntimeRequirementCatalog catalog = new CPPRuntimeRequirementCatalog();
        CPPRuntimeRequirementRegistrar registrar = new CPPRuntimeRequirementRegistrar(catalog, report);

        registrar.RegisterDefaults(CPPConversionOptions.CreateDefault());

        Assert.True(registrar.IsRegistered("NativeString"));
        Assert.True(registrar.IsRegistered("NativeList"));
        Assert.True(registrar.IsRegistered("NativeDictionary"));
        Assert.False(report.HasErrors);
    }

    /// <summary>
    /// Ensures feature-owned runtime helpers are skipped when every owning feature resolved to disabled.
    /// </summary>
    [Fact]
    public void Register_WhenFeatureOwnedRequirementIsDisabled_SkipsRegistration() {
        CPPExternalFeatureCatalog catalogMetadata = CPPTestFeatureCatalogFactory.CreateHelengineCatalog();
        CPPConversionReport report = new CPPConversionReport();
        CPPRuntimeRequirementCatalog catalog = new CPPRuntimeRequirementCatalog(catalogMetadata);
        CPPRuntimeRequirementRegistrar registrar = new CPPRuntimeRequirementRegistrar(catalog, report);
        CPPBuildUsageReport buildUsageReport = new CPPBuildUsageReport();

        buildUsageReport.FeatureDecisions.Add(new CPPFeatureDecision {
            FeatureId = "debug_overlay",
            Enabled = false,
            Origin = CPPFeatureDecisionOrigin.ForcedDisabled
        });
        buildUsageReport.FeatureDecisions.Add(new CPPFeatureDecision {
            FeatureId = "shaders",
            Enabled = false,
            Origin = CPPFeatureDecisionOrigin.ForcedDisabled
        });

        registrar.ApplyBuildUsageReport(buildUsageReport);

        Assert.False(registrar.Register("StringBuilder"));
        Assert.False(registrar.IsRegistered("StringBuilder"));
        Assert.False(report.HasErrors);
    }

    /// <summary>
    /// Ensures the generated config writer emits profile and runtime requirement defines.
    /// </summary>
    [Fact]
    public void Write_WritesExpectedConfigDefines() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        CPPConversionReport report = new CPPConversionReport();
        CPPRuntimeRequirementRegistrar registrar = new CPPRuntimeRequirementRegistrar(new CPPRuntimeRequirementCatalog(), report);
        registrar.RegisterDefaults(options);

        string outputFolder = Path.Combine(Path.GetTempPath(), "cs2.cpp.tests", Guid.NewGuid().ToString("N"));
        string filePath = CPPGeneratedConfigWriter.Write(outputFolder, options, registrar);
        string output = File.ReadAllText(filePath);

        Assert.Contains("#define HE_CPP_COMPILER_MSVC 1", output);
        Assert.Contains("#define HE_CPP_PLATFORM_WINDOWS 1", output);
        Assert.Contains("#define HE_CPP_RUNTIME_STL_LITE 1", output);
        Assert.Contains("#define HE_CPP_REQ_NATIVE_STRING 1", output);
        Assert.Contains("#define HE_CPP_REQ_NATIVE_LIST 1", output);
        Assert.Contains("#define HE_CPP_REQ_NATIVE_DICTIONARY 1", output);
    }

    /// <summary>
    /// Ensures the generated config writer emits caller-owned custom platform metadata from a generic custom profile.
    /// </summary>
    [Fact]
    public void Write_WithCustomPlatformProfile_WritesCustomPlatformDefines() {
        CPPConversionOptions options = new CPPConversionOptions {
            CompilerProfile = CPPCompilerProfile.CreateGcc(),
            PlatformProfile = CPPPlatformProfile.CreateCustomHeadless("retroppc", false, CPPGeneratedMathConventionKind.NativeColumnVector, 4),
            RuntimeProfile = CPPRuntimeProfile.CreateStlLite(),
            CollectDiagnostics = true,
            BuildFeatureProfile = CPPBuildFeatureProfile.CreateDefault(),
            LoadNativeRuntimeMetadata = true,
            PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["native-file-system-header"] = "\"platform/retro/RetroDiscFileSystem.hpp\"",
                ["native-file-system-type"] = "helengine::retro::RetroDiscFileSystem"
            }
        };
        CPPConversionReport report = new CPPConversionReport();
        CPPRuntimeRequirementRegistrar registrar = new CPPRuntimeRequirementRegistrar(new CPPRuntimeRequirementCatalog(), report);
        registrar.RegisterDefaults(options);

        string outputFolder = Path.Combine(Path.GetTempPath(), "cs2.cpp.tests", Guid.NewGuid().ToString("N"));
        string filePath = CPPGeneratedConfigWriter.Write(outputFolder, options, registrar);
        string output = File.ReadAllText(filePath);

        Assert.Contains("#define HE_CPP_COMPILER_GCC 1", output);
        Assert.Contains("#define HE_CPP_PLATFORM_RETROPPC 1", output);
        Assert.Contains("#define HE_CPP_RUNTIME_STL_LITE 1", output);
        Assert.Contains("#define HE_CPP_PLATFORM_IS_LITTLE_ENDIAN 0", output);
        Assert.Contains("#define HE_CPP_PLATFORM_IS_WINDOWS_HOST 0", output);
        Assert.Contains("#define HE_CPP_RUNTIME_HAS_CUSTOM_FILE_SYSTEM 1", output);
        Assert.Contains("#define HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_HEADER \"platform/retro/RetroDiscFileSystem.hpp\"", output);
        Assert.Contains("#define HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_TYPE helengine::retro::RetroDiscFileSystem", output);
    }

    /// <summary>
    /// Ensures the generated config writer emits resolved feature defines for compile-time pruning in native hosts.
    /// </summary>
    [Fact]
    public void Write_WithResolvedFeatureUsage_WritesFeatureDefines() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        CPPConversionReport report = new CPPConversionReport();
        CPPRuntimeRequirementRegistrar registrar = new CPPRuntimeRequirementRegistrar(new CPPRuntimeRequirementCatalog(), report);
        registrar.RegisterDefaults(options);

        CPPBuildUsageReport buildUsageReport = new CPPBuildUsageReport();
        buildUsageReport.FeatureDecisions.Add(new CPPFeatureDecision {
            FeatureId = "shaders",
            Enabled = false,
            Origin = CPPFeatureDecisionOrigin.ForcedDisabled
        });
        buildUsageReport.FeatureDecisions.Add(new CPPFeatureDecision {
            FeatureId = "sprites",
            Enabled = true,
            Origin = CPPFeatureDecisionOrigin.ForcedEnabled
        });

        string outputFolder = Path.Combine(Path.GetTempPath(), "cs2.cpp.tests", Guid.NewGuid().ToString("N"));
        string filePath = CPPGeneratedConfigWriter.Write(outputFolder, options, registrar, buildUsageReport);
        string output = File.ReadAllText(filePath);

        Assert.Contains("#define HE_CPP_FEATURE_SHADERS 0", output);
        Assert.Contains("#define HE_CPP_FEATURE_SPRITES 1", output);
    }
}
