namespace cs2.cpp.tests.TestHelpers;

/// <summary>
/// Routes test-owned generated projects, native sources, and logs into the repository's ignored scratch directory.
/// </summary>
internal static class CPPTestEnvironment {
    /// <summary>
    /// Configures the process temporary-directory contract before xUnit creates any test fixtures.
    /// </summary>
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Initialize() {
        string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string scratchRoot = Path.Combine(repositoryRoot, "scratch", "test-temp");
        Directory.CreateDirectory(scratchRoot);
        Environment.SetEnvironmentVariable("TEMP", scratchRoot);
        Environment.SetEnvironmentVariable("TMP", scratchRoot);
    }
}
