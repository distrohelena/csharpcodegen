using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies the feature profile defaults exposed by the C++ backend.
/// </summary>
public class CPPFeatureProfileTests {
    /// <summary>
    /// Ensures phase-one features default to automatic selection.
    /// </summary>
    [Fact]
    public void CreateDefault_UsesAutoForPhaseOneFeatures() {
        CPPBuildFeatureProfile profile = CPPBuildFeatureProfile.CreateDefault();

        Assert.Equal(CPPFeatureMode.Auto, profile.GetMode("shaders", CPPFeatureMode.Auto));
        Assert.Equal(CPPFeatureMode.Auto, profile.GetMode("sprites", CPPFeatureMode.Auto));
        Assert.Equal(CPPFeatureMode.Auto, profile.GetMode("text2d", CPPFeatureMode.Auto));
        Assert.Equal(CPPFeatureMode.Auto, profile.GetMode("render2d", CPPFeatureMode.Auto));
        Assert.Equal(CPPFeatureMode.Auto, profile.GetMode("debug_overlay", CPPFeatureMode.Auto));
    }

    /// <summary>
    /// Ensures conversion options surface a build feature profile by default.
    /// </summary>
    [Fact]
    public void CreateDefault_ExposesBuildFeatureProfile() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();

        Assert.NotNull(options.BuildFeatureProfile);
        Assert.Same(CPPExternalFeatureCatalog.Empty, options.FeatureCatalog);
    }

    /// <summary>
    /// Ensures preset-aware options remain unresolved until a named preset is explicitly selected.
    /// </summary>
    [Fact]
    public void CreateDefault_LeavesPresetIdEmptyUntilResolved() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();

        Assert.Equal(string.Empty, options.PresetId);
    }
}
