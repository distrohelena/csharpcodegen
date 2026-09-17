using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies runtime profiles assume RTTI unless a target declares otherwise, matching what generated dispatch has always emitted.
/// </summary>
public sealed class CPPRuntimeProfileRttiDefaultTests {
    /// <summary>
    /// A bare profile must not silently opt a target out of RTTI-dependent lowering.
    /// </summary>
    [Fact]
    public void NewProfile_DefaultsToRtti() {
        CPPRuntimeProfile profile = new();

        Assert.True(profile.UseRtti);
    }

    /// <summary>
    /// The STL-lite preset is what every CLI invocation without a named profile receives, so it must keep RTTI on.
    /// </summary>
    [Fact]
    public void StlLite_DefaultsToRtti() {
        Assert.True(CPPRuntimeProfile.CreateStlLite().UseRtti);
    }

    /// <summary>
    /// The custom-retro preset targets compile with RTTI too; only an explicit option turns it off.
    /// </summary>
    [Fact]
    public void CustomRetro_DefaultsToRtti() {
        Assert.True(CPPRuntimeProfile.CreateCustomRetro().UseRtti);
    }

    /// <summary>
    /// A target that really lacks RTTI opts out through the generic option and the resolver honours it.
    /// </summary>
    [Fact]
    public void Resolve_ExplicitFalseDisablesRtti() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            [CPPCodegenOptionNames.UseRtti] = "false"
        };

        CPPRuntimeOptionResolver.Resolve(options);

        Assert.False(options.RuntimeProfile.UseRtti);
    }

    /// <summary>
    /// Without the option, resolution leaves the profile's RTTI default in place.
    /// </summary>
    [Fact]
    public void Resolve_WithoutOptionKeepsRtti() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        CPPRuntimeOptionResolver.Resolve(options);

        Assert.True(options.RuntimeProfile.UseRtti);
    }
}
