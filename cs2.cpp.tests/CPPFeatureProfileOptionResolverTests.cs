namespace cs2.cpp.tests;

/// <summary>
/// Verifies generic codegen feature option parsing builds the expected feature profile.
/// </summary>
public sealed class CPPFeatureProfileOptionResolverTests {
    /// <summary>
    /// Ensures forced-disabled feature ids map to disabled feature-profile modes.
    /// </summary>
    [Fact]
    public void BuildProfile_with_forced_disabled_features_disables_each_feature() {
        CPPExternalFeatureCatalog featureCatalog = new CPPExternalFeatureCatalog(
            [
                new CPPExternalFeatureDefinition("debug_overlay", CPPFeatureMode.Auto, CPPFeatureConflictPolicy.Error),
                new CPPExternalFeatureDefinition("shaders", CPPFeatureMode.Auto, CPPFeatureConflictPolicy.Error),
                new CPPExternalFeatureDefinition("text2d", CPPFeatureMode.Auto, CPPFeatureConflictPolicy.Error)
            ],
            [],
            []);
        Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase) {
            [CPPCodegenOptionNames.ForcedDisabledFeatures] = "debug_overlay; shaders ;debug_overlay"
        };

        CPPBuildFeatureProfile profile = CPPFeatureProfileOptionResolver.BuildProfile(options, featureCatalog);

        Assert.Equal(CPPFeatureMode.Disabled, profile.GetMode("debug_overlay", CPPFeatureMode.Auto));
        Assert.Equal(CPPFeatureMode.Disabled, profile.GetMode("shaders", CPPFeatureMode.Auto));
        Assert.Equal(CPPFeatureMode.Auto, profile.GetMode("text2d", CPPFeatureMode.Auto));
    }

    /// <summary>
    /// Ensures explicitly enabled feature ids remain enabled without detected managed-code usage.
    /// </summary>
    [Fact]
    public void BuildProfile_with_enabled_features_enables_each_feature() {
        CPPExternalFeatureCatalog featureCatalog = new CPPExternalFeatureCatalog(
            [
                new CPPExternalFeatureDefinition("host_file_system", CPPFeatureMode.Auto, CPPFeatureConflictPolicy.Error),
                new CPPExternalFeatureDefinition("shaders", CPPFeatureMode.Auto, CPPFeatureConflictPolicy.Error)
            ],
            [],
            []);
        Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase) {
            [CPPCodegenOptionNames.EnabledFeatures] = "host_file_system"
        };

        CPPBuildFeatureProfile profile = CPPFeatureProfileOptionResolver.BuildProfile(options, featureCatalog);

        Assert.Equal(CPPFeatureMode.Enabled, profile.GetMode("host_file_system", CPPFeatureMode.Auto));
        Assert.Equal(CPPFeatureMode.Auto, profile.GetMode("shaders", CPPFeatureMode.Auto));
    }

    /// <summary>
    /// Ensures contradictory enabled and disabled feature selections fail rather than relying on option order.
    /// </summary>
    [Fact]
    public void BuildProfile_with_conflicting_feature_modes_throws() {
        CPPExternalFeatureCatalog featureCatalog = new CPPExternalFeatureCatalog(
            [
                new CPPExternalFeatureDefinition("host_file_system", CPPFeatureMode.Auto, CPPFeatureConflictPolicy.Error)
            ],
            [],
            []);
        Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase) {
            [CPPCodegenOptionNames.EnabledFeatures] = "host_file_system",
            [CPPCodegenOptionNames.ForcedDisabledFeatures] = "host_file_system"
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => CPPFeatureProfileOptionResolver.BuildProfile(options, featureCatalog));

        Assert.Contains("host_file_system", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures unknown forced-disabled feature ids fail fast instead of being silently ignored.
    /// </summary>
    [Fact]
    public void BuildProfile_with_unknown_forced_disabled_feature_throws() {
        CPPExternalFeatureCatalog featureCatalog = new CPPExternalFeatureCatalog(
            [
                new CPPExternalFeatureDefinition("debug_overlay", CPPFeatureMode.Auto, CPPFeatureConflictPolicy.Error)
            ],
            [],
            []);
        Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase) {
            [CPPCodegenOptionNames.ForcedDisabledFeatures] = "missing_feature"
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => CPPFeatureProfileOptionResolver.BuildProfile(options, featureCatalog));

        Assert.Contains("missing_feature", exception.Message, StringComparison.Ordinal);
    }
}
