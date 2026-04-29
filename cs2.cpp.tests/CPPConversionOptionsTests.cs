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

        Assert.Equal(CPPCompilerKind.Msvc, options.CompilerProfile.Kind);
        Assert.Equal(CPPPlatformKind.WindowsHeadless, options.PlatformProfile.Kind);
        Assert.Equal(CPPRuntimeKind.StlLite, options.RuntimeProfile.Kind);
        Assert.True(options.CollectDiagnostics);
        Assert.False(options.FailOnError);
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
