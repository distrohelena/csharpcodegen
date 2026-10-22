namespace cs2.cpp.tests;

/// <summary>
/// Verifies that generator tests keep temporary projects and artifacts in the selected external platform directory.
/// </summary>
public sealed class CPPTestEnvironmentTests {
    /// <summary>
    /// Ensures the test assembly redirects the process temporary directory before converter fixtures allocate output paths.
    /// </summary>
    [Fact]
    public void ModuleInitializer_RoutesTemporaryOutputIntoExternalPlatformScratch() {
        string expectedRoot = Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_TEMP_ROOT")
            ?? (OperatingSystem.IsWindows()
                ? @"C:\temp\helengine\windows\codegen-test-temp"
                : OperatingSystem.IsMacOS()
                    ? "/tmp/helengine/macos/codegen-test-temp"
                    : "/tmp/helengine/linux/codegen-test-temp");

        Assert.Equal(expectedRoot, Path.TrimEndingDirectorySeparator(Path.GetTempPath()));
        Assert.True(Directory.Exists(expectedRoot));
    }
}
