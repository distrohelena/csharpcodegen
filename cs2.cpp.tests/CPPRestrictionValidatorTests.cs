using cs2.cpp;
using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies that strict restriction profiles reject forbidden feature and runtime helper combinations.
/// </summary>
public class CPPRestrictionValidatorTests {
    /// <summary>
    /// Ensures shader reachability is rejected when the active restriction profile forbids shader systems.
    /// </summary>
    [Fact]
    public void Validate_WhenShadersAreForbiddenAndReachable_ReturnsDiagnostic() {
        CPPExternalFeatureCatalog catalog = CPPTestFeatureCatalogFactory.CreateSampleFeatureCatalog();
        CPPBuildUsageReport usageReport = CPPFeatureResolver.Resolve(
            CPPBuildFeatureProfile.CreateDefault(),
            catalog,
            [
                new CPPFeatureUsageRoot {
                    FeatureId = "shaders",
                    RootId = "ExampleEngine.Core.Shaders.ShaderAsset",
                    SourceKind = "Type"
                }
            ]);

        CPPRestrictionProfile profile = new CPPRestrictionProfile {
            Name = "ps2-lite",
            ForbidShaders = true
        };

        CPPRestrictionValidationResult result = CPPRestrictionValidator.Validate(usageReport, [], profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("ps2-lite", StringComparison.Ordinal) && diagnostic.Contains("shaders", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ensures regex runtime helpers are rejected when the active restriction profile forbids regex support.
    /// </summary>
    [Fact]
    public void Validate_WhenRegexIsForbiddenAndRegistered_ReturnsDiagnostic() {
        CPPRuntimeRequirementCatalog catalog = new CPPRuntimeRequirementCatalog();
        Assert.True(catalog.TryGet("Regex", out CPPRuntimeRequirementDefinition regex));

        CPPRestrictionProfile profile = new CPPRestrictionProfile {
            Name = "ps2-lite",
            ForbidRegex = true
        };

        CPPRestrictionValidationResult result = CPPRestrictionValidator.Validate(new CPPBuildUsageReport(), [regex], profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Regex", StringComparison.Ordinal));
    }
}
