using cs2.cpp;
using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies feature resolution precedence and forced-disable conflict reporting.
/// </summary>
public class CPPFeatureResolverTests {
    /// <summary>
    /// Ensures a forced-disabled feature stays disabled even when usage is detected.
    /// </summary>
    [Fact]
    public void Resolve_WhenFeatureIsForceDisabled_WinsOverDetectedUsage() {
        CPPExternalFeatureCatalog catalog = CPPTestFeatureCatalogFactory.CreateHelengineCatalog();
        CPPBuildFeatureProfile profile = CPPBuildFeatureProfile.CreateDefault()
            .WithMode("shaders", CPPFeatureMode.Disabled)
            .WithConflictPolicy("shaders", CPPFeatureConflictPolicy.Error);

        CPPBuildUsageReport report = CPPFeatureResolver.Resolve(profile, catalog, new[] {
            new CPPFeatureUsageRoot {
                FeatureId = "shaders",
                RootId = "helengine.core.shaders.ShaderAsset",
                SourceKind = "Type",
            }
        });

        Assert.False(report.IsEnabled("shaders"));
        Assert.Equal(CPPFeatureDecisionOrigin.ForcedDisabled, report.GetDecision("shaders").Origin);
        Assert.Single(report.Conflicts);
        Assert.Equal(CPPFeatureConflictPolicy.Error, report.Conflicts[0].Policy);
    }

    /// <summary>
    /// Ensures a forced-enabled feature stays enabled without detected usage.
    /// </summary>
    [Fact]
    public void Resolve_WhenFeatureIsForceEnabled_StaysEnabledWithoutDetectedUsage() {
        CPPExternalFeatureCatalog catalog = CPPTestFeatureCatalogFactory.CreateHelengineCatalog();
        CPPBuildFeatureProfile profile = CPPBuildFeatureProfile.CreateDefault()
            .WithMode("sprites", CPPFeatureMode.Enabled);

        CPPBuildUsageReport report = CPPFeatureResolver.Resolve(profile, catalog, Array.Empty<CPPFeatureUsageRoot>());

        Assert.True(report.IsEnabled("sprites"));
        Assert.Equal(CPPFeatureDecisionOrigin.ForcedEnabled, report.GetDecision("sprites").Origin);
        Assert.Empty(report.Conflicts);
    }
}
