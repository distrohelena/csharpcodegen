using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies that named conversion presets resolve to the expected profile combinations.
/// </summary>
public class CPPConversionPresetCatalogTests {
    /// <summary>
    /// Ensures the Windows no-shaders preset resolves to the expected compiler, platform, feature, and restriction settings.
    /// </summary>
    [Fact]
    public void Resolve_WindowsNoShaders_UsesNamedPresetProfiles() {
        CPPConversionPreset preset = new CPPConversionPresetCatalog().Resolve("windows-no-shaders");

        Assert.Equal("windows-no-shaders", preset.Id);
        Assert.Equal("msvc", preset.CompilerProfile.Name);
        Assert.Equal("windows-headless", preset.PlatformProfile.Name);
        Assert.Equal("stl-lite", preset.RuntimeProfile.Name);
        Assert.Equal(CPPFeatureMode.Disabled, preset.BuildFeatureProfile.GetMode(CPPFeatureKind.Shaders));
        Assert.Equal("desktop-no-shaders", preset.RestrictionProfile.Name);
        Assert.True(preset.RestrictionProfile.ForbidShaders);
    }

    /// <summary>
    /// Ensures the GameCube core-boot preset resolves to the expected compiler, platform, feature, and restriction settings.
    /// </summary>
    [Fact]
    public void Resolve_GameCubeCoreBoot_UsesNamedPresetProfiles() {
        CPPConversionPreset preset = new CPPConversionPresetCatalog().Resolve("gamecube-core-boot");

        Assert.Equal("gamecube-core-boot", preset.Id);
        Assert.Equal("gcc", preset.CompilerProfile.Name);
        Assert.Equal("gamecube-headless", preset.PlatformProfile.Name);
        Assert.Equal("stl-lite", preset.RuntimeProfile.Name);
        Assert.Equal(CPPFeatureMode.Disabled, preset.BuildFeatureProfile.GetMode(CPPFeatureKind.Shaders));
        Assert.Equal(CPPFeatureMode.Disabled, preset.BuildFeatureProfile.GetMode(CPPFeatureKind.DebugOverlay));
        Assert.Equal("gamecube-core-boot", preset.RestrictionProfile.Name);
        Assert.True(preset.RestrictionProfile.ForbidShaders);
        Assert.True(preset.RestrictionProfile.ForbidDebugOnlySystems);
    }

    /// <summary>
    /// Ensures the GameCube preset resolves the native column-vector generated math convention.
    /// </summary>
    [Fact]
    public void Resolve_GameCubeCoreBoot_UsesNativeColumnVectorMathConvention() {
        CPPConversionPreset preset = new CPPConversionPresetCatalog().Resolve("gamecube-core-boot");

        object convention = typeof(CPPPlatformProfile).GetProperty("GeneratedMathConvention")?.GetValue(preset.PlatformProfile);
        Assert.Equal("NativeColumnVector", convention?.ToString());
    }

    /// <summary>
    /// Ensures the GameCube core-boot preset forbids reflection-like runtime systems.
    /// </summary>
    [Fact]
    public void Resolve_GameCubeCoreBoot_ForbidsReflectionLikeRuntime() {
        CPPConversionPreset preset = new CPPConversionPresetCatalog().Resolve("gamecube-core-boot");

        Assert.True(preset.RestrictionProfile.ForbidReflectionLikeRuntime);
        Assert.True(preset.RestrictionProfile.ForbidRuntimeJson);
    }

    /// <summary>
    /// Ensures the GameCube core-boot preset carries only the reflection-disable preprocessor symbols required by the stripped runtime.
    /// </summary>
    [Fact]
    public void ApplyTo_GameCubeCoreBoot_AddsReflectionDisableSymbols() {
        CPPConversionOptions options = new CPPConversionOptions {
            PresetId = "gamecube-core-boot"
        };

        new CPPConversionPresetCatalog().ApplyTo(options);

        Assert.False(options.IncludeProjectDefinedPreprocessorSymbols);
        Assert.DoesNotContain("HELENGINE_RUNTIME_MATERIAL_RESOLUTION_COOKED_PLATFORM_OWNED", options.AdditionalPreprocessorSymbols);
        Assert.Contains("HELENGINE_CODEGEN_DISABLE_RUNTIME_SCRIPT_REFLECTION", options.AdditionalPreprocessorSymbols);
        Assert.Contains("HELENGINE_CODEGEN_DISABLE_MENU_REFLECTION", options.AdditionalPreprocessorSymbols);
    }
}
