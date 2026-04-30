using cs2.cpp;

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
        CPPBuildFeatureProfile profile = CPPBuildFeatureProfile.CreateDefault()
            .WithMode(CPPFeatureKind.Shaders, CPPFeatureMode.Disabled)
            .WithConflictPolicy(CPPFeatureKind.Shaders, CPPFeatureConflictPolicy.Error);

        CPPBuildUsageReport report = CPPFeatureResolver.Resolve(profile, new[] {
            new CPPFeatureUsageRoot {
                Feature = CPPFeatureKind.Shaders,
                RootId = "helengine.core.shaders.ShaderAsset",
                SourceKind = "Type",
            }
        });

        Assert.False(report.IsEnabled(CPPFeatureKind.Shaders));
        Assert.Equal(CPPFeatureDecisionOrigin.ForcedDisabled, report.GetDecision(CPPFeatureKind.Shaders).Origin);
        Assert.Single(report.Conflicts);
        Assert.Equal(CPPFeatureConflictPolicy.Error, report.Conflicts[0].Policy);
    }

    /// <summary>
    /// Ensures a forced-enabled feature stays enabled without detected usage.
    /// </summary>
    [Fact]
    public void Resolve_WhenFeatureIsForceEnabled_StaysEnabledWithoutDetectedUsage() {
        CPPBuildFeatureProfile profile = CPPBuildFeatureProfile.CreateDefault()
            .WithMode(CPPFeatureKind.Sprites, CPPFeatureMode.Enabled);

        CPPBuildUsageReport report = CPPFeatureResolver.Resolve(profile, Array.Empty<CPPFeatureUsageRoot>());

        Assert.True(report.IsEnabled(CPPFeatureKind.Sprites));
        Assert.Equal(CPPFeatureDecisionOrigin.ForcedEnabled, report.GetDecision(CPPFeatureKind.Sprites).Origin);
        Assert.Empty(report.Conflicts);
    }
}
