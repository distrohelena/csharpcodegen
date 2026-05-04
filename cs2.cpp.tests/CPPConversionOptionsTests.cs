using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies the default profile and reporting models exposed by the C++ backend.
/// </summary>
public class CPPConversionOptionsTests {
    /// <summary>
    /// Ensures the default options target the approved first milestone profiles.
    /// </summary>
    [Fact]
    public void CreateDefault_UsesApprovedHeadlessCoreProfiles() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();

        Assert.Equal(string.Empty, options.PresetId);
        Assert.Equal(CPPCompilerKind.Msvc, options.CompilerProfile.Kind);
        Assert.Equal(CPPPlatformKind.WindowsHeadless, options.PlatformProfile.Kind);
        Assert.Equal(CPPRuntimeKind.StlLite, options.RuntimeProfile.Kind);
        Assert.True(options.CollectDiagnostics);
        Assert.False(options.FailOnError);
    }

    /// <summary>
    /// Ensures the default option surface exposes a permissive restriction profile before a named preset is resolved.
    /// </summary>
    [Fact]
    public void CreateDefault_ExposesEmptyPresetIdAndRestrictionProfile() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();

        Assert.Equal(string.Empty, options.PresetId);
        Assert.NotNull(options.RestrictionProfile);
        Assert.Equal("default", options.RestrictionProfile.Name);
        Assert.False(options.RestrictionProfile.ForbidShaders);
    }

    /// <summary>
    /// Ensures the dedicated GameCube helper selects the correct compiler and platform metadata.
    /// </summary>
    [Fact]
    public void CreateGameCubeDefault_UsesGccAndGameCubeHeadlessProfiles() {
        CPPConversionOptions options = CPPConversionOptions.CreateGameCubeDefault();

        Assert.Equal(CPPCompilerKind.Gcc, options.CompilerProfile.Kind);
        Assert.Equal(CPPPlatformKind.GameCubeHeadless, options.PlatformProfile.Kind);
        Assert.Equal(CPPRuntimeKind.StlLite, options.RuntimeProfile.Kind);
        Assert.False(options.PlatformProfile.IsLittleEndian);
        Assert.False(options.PlatformProfile.IsWindowsHost);
    }

    /// <summary>
    /// Ensures the conversion report can record blocking diagnostics.
    /// </summary>
    [Fact]
    public void AddDiagnostic_ErrorMarksReportAsFailed() {
        CPPConversionReport report = new CPPConversionReport();

        report.AddDiagnostic(CPPDiagnosticSeverity.Error, "CPP001", "Unsupported construct");

        Assert.True(report.HasErrors);
        Assert.Single(report.Diagnostics);
    }
}
