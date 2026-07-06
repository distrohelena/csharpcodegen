using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies that named conversion presets resolve to the expected profile combinations.
/// </summary>
public class CPPConversionPresetCatalogTests {
    /// <summary>
    /// Ensures the Windows no-shaders preset resolves to the expected compiler, platform, generic option, and restriction settings.
    /// </summary>
    [Fact]
    public void Resolve_WindowsNoShaders_UsesNamedPresetProfiles() {
        CPPConversionPreset preset = new CPPConversionPresetCatalog().Resolve("windows-no-shaders");

        Assert.Equal("windows-no-shaders", preset.Id);
        Assert.Equal("msvc", preset.CompilerProfile.Name);
        Assert.Equal("windows-headless", preset.PlatformProfile.Name);
        Assert.Equal("stl-lite", preset.RuntimeProfile.Name);
        Assert.Equal("shaders", preset.PlatformOptionValues[CPPCodegenOptionNames.ForcedDisabledFeatures]);
        Assert.Equal("desktop-no-shaders", preset.RestrictionProfile.Name);
        Assert.True(preset.RestrictionProfile.ForbidShaders);
    }

    /// <summary>
    /// Ensures the stripped native core-boot preset resolves to the expected compiler, platform, generic option, and restriction settings.
    /// </summary>
    [Fact]
    public void Resolve_NativeCoreBoot_UsesNamedPresetProfiles() {
        CPPConversionPreset preset = new CPPConversionPresetCatalog().Resolve("native-core-boot");

        Assert.Equal("native-core-boot", preset.Id);
        Assert.Equal("gcc", preset.CompilerProfile.Name);
        Assert.Equal("retroppc-headless", preset.PlatformProfile.Name);
        Assert.Equal("stl-lite", preset.RuntimeProfile.Name);
        Assert.Equal("shaders;debug_overlay", preset.PlatformOptionValues[CPPCodegenOptionNames.ForcedDisabledFeatures]);
        Assert.Equal("native-core-boot", preset.RestrictionProfile.Name);
        Assert.True(preset.RestrictionProfile.ForbidShaders);
        Assert.True(preset.RestrictionProfile.ForbidDebugOnlySystems);
    }

    /// <summary>
    /// Ensures the stripped native preset resolves the native column-vector generated math convention.
    /// </summary>
    [Fact]
    public void Resolve_NativeCoreBoot_UsesNativeColumnVectorMathConvention() {
        CPPConversionPreset preset = new CPPConversionPresetCatalog().Resolve("native-core-boot");

        object convention = typeof(CPPPlatformProfile).GetProperty("GeneratedMathConvention")?.GetValue(preset.PlatformProfile);
        Assert.Equal("NativeColumnVector", convention?.ToString());
    }

    /// <summary>
    /// Ensures the stripped native core-boot preset forbids reflection-like runtime systems.
    /// </summary>
    [Fact]
    public void Resolve_NativeCoreBoot_ForbidsReflectionLikeRuntime() {
        CPPConversionPreset preset = new CPPConversionPresetCatalog().Resolve("native-core-boot");

        Assert.True(preset.RestrictionProfile.ForbidReflectionLikeRuntime);
        Assert.True(preset.RestrictionProfile.ForbidRuntimeJson);
    }

    /// <summary>
    /// Ensures the stripped native core-boot preset carries only the reflection-disable preprocessor symbols required by the stripped runtime.
    /// </summary>
    [Fact]
    public void ApplyTo_NativeCoreBoot_AddsReflectionDisableSymbols() {
        CPPConversionOptions options = new CPPConversionOptions {
            PresetId = "native-core-boot",
            FeatureCatalog = new CPPExternalFeatureCatalog(
                [
                    new CPPExternalFeatureDefinition("debug_overlay", CPPFeatureMode.Auto, CPPFeatureConflictPolicy.Error),
                    new CPPExternalFeatureDefinition("shaders", CPPFeatureMode.Auto, CPPFeatureConflictPolicy.Error)
                ],
                [],
                [])
        };

        new CPPConversionPresetCatalog().ApplyTo(options);

        Assert.False(options.IncludeProjectDefinedPreprocessorSymbols);
        Assert.DoesNotContain("HELENGINE_RUNTIME_MATERIAL_RESOLUTION_COOKED_PLATFORM_OWNED", options.AdditionalPreprocessorSymbols);
        Assert.Contains("HELENGINE_CODEGEN_DISABLE_RUNTIME_SCRIPT_REFLECTION", options.AdditionalPreprocessorSymbols);
        Assert.Contains("HELENGINE_CODEGEN_DISABLE_MENU_REFLECTION", options.AdditionalPreprocessorSymbols);
    }

    /// <summary>
    /// Ensures preset aliases contribute generic forced-disabled feature defaults instead of mutating feature profiles directly.
    /// </summary>
    [Fact]
    public void ApplyTo_PresetAlias_uses_generic_forced_disabled_feature_option() {
        CPPConversionOptions options = new CPPConversionOptions {
            PresetId = "ps2-lite",
            FeatureCatalog = new CPPExternalFeatureCatalog(
                [
                    new CPPExternalFeatureDefinition("debug_overlay", CPPFeatureMode.Auto, CPPFeatureConflictPolicy.Error),
                    new CPPExternalFeatureDefinition("shaders", CPPFeatureMode.Auto, CPPFeatureConflictPolicy.Error)
                ],
                [],
                []),
            PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        new CPPConversionPresetCatalog().ApplyTo(options);

        Assert.Equal(CPPFeatureMode.Disabled, options.BuildFeatureProfile.GetMode("debug_overlay", CPPFeatureMode.Auto));
        Assert.Equal(CPPFeatureMode.Disabled, options.BuildFeatureProfile.GetMode("shaders", CPPFeatureMode.Auto));
        Assert.Equal("shaders;debug_overlay", options.PlatformOptionValues[CPPCodegenOptionNames.ForcedDisabledFeatures]);
    }

    /// <summary>
    /// Ensures the N64 compatibility preset routes all of its feature pruning through the generic forced-disabled option.
    /// </summary>
    [Fact]
    public void ApplyTo_N64Minimal_uses_generic_forced_disabled_feature_option() {
        CPPConversionOptions options = new CPPConversionOptions {
            PresetId = "n64-minimal",
            FeatureCatalog = new CPPExternalFeatureCatalog(
                [
                    new CPPExternalFeatureDefinition("debug_overlay", CPPFeatureMode.Auto, CPPFeatureConflictPolicy.Error),
                    new CPPExternalFeatureDefinition("shaders", CPPFeatureMode.Auto, CPPFeatureConflictPolicy.Error),
                    new CPPExternalFeatureDefinition("render2d", CPPFeatureMode.Auto, CPPFeatureConflictPolicy.Error),
                    new CPPExternalFeatureDefinition("text2d", CPPFeatureMode.Auto, CPPFeatureConflictPolicy.Error)
                ],
                [],
                []),
            PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        new CPPConversionPresetCatalog().ApplyTo(options);

        Assert.Equal(CPPFeatureMode.Disabled, options.BuildFeatureProfile.GetMode("debug_overlay", CPPFeatureMode.Auto));
        Assert.Equal(CPPFeatureMode.Disabled, options.BuildFeatureProfile.GetMode("shaders", CPPFeatureMode.Auto));
        Assert.Equal(CPPFeatureMode.Disabled, options.BuildFeatureProfile.GetMode("render2d", CPPFeatureMode.Auto));
        Assert.Equal(CPPFeatureMode.Disabled, options.BuildFeatureProfile.GetMode("text2d", CPPFeatureMode.Auto));
        Assert.Equal("shaders;debug_overlay;render2d;text2d", options.PlatformOptionValues[CPPCodegenOptionNames.ForcedDisabledFeatures]);
    }

    /// <summary>
    /// Ensures preset application preserves caller-provided preprocessor symbols required by platform-owned codegen seams.
    /// </summary>
    [Fact]
    public void ApplyTo_DsLite_PreservesCallerProvidedPreprocessorSymbols() {
        CPPConversionOptions options = new CPPConversionOptions {
            PresetId = "ds-lite",
            FeatureCatalog = new CPPExternalFeatureCatalog(
                [
                    new CPPExternalFeatureDefinition("debug_overlay", CPPFeatureMode.Auto, CPPFeatureConflictPolicy.Error)
                ],
                [],
                []),
            AdditionalPreprocessorSymbols = [
                "HELENGINE_RUNTIME_MATERIAL_RESOLUTION_COOKED_PLATFORM_OWNED",
                "HELENGINE_RUNTIME_TEXTURE_RESOLUTION_COOKED_PLATFORM_OWNED"
            ]
        };

        new CPPConversionPresetCatalog().ApplyTo(options);

        Assert.Contains("HELENGINE_RUNTIME_MATERIAL_RESOLUTION_COOKED_PLATFORM_OWNED", options.AdditionalPreprocessorSymbols);
        Assert.Contains("HELENGINE_RUNTIME_TEXTURE_RESOLUTION_COOKED_PLATFORM_OWNED", options.AdditionalPreprocessorSymbols);
        Assert.DoesNotContain("HELENGINE_CODEGEN_DISABLE_RUNTIME_SCRIPT_REFLECTION", options.AdditionalPreprocessorSymbols);
    }

}
