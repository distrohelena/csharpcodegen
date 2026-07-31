namespace cs2.cpp.tests;

/// <summary>
/// Verifies that generator tests keep their temporary projects and generated artifacts inside the repository workspace.
/// </summary>
public sealed class CPPTestEnvironmentTests {
    /// <summary>
    /// Ensures the test assembly redirects the process temporary directory before converter fixtures allocate output paths.
    /// </summary>
    [Fact]
    public void ModuleInitializer_RoutesTemporaryOutputIntoRepositoryScratch() {
        string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string expectedRoot = Path.Combine(repositoryRoot, "scratch", "test-temp");

        Assert.Equal(expectedRoot, Path.TrimEndingDirectorySeparator(Path.GetTempPath()));
        Assert.True(Directory.Exists(expectedRoot));
    }
}
