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
}
