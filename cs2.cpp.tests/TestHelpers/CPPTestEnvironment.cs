namespace cs2.cpp.tests.TestHelpers;

/// <summary>
/// Routes test-owned generated projects, native sources, and logs outside the repository into one fixed scratch root.
/// </summary>
internal static class CPPTestEnvironment {
    /// <summary>
    /// Configures the process temporary-directory contract before xUnit creates any test fixtures.
    /// </summary>
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Initialize() {
        string scratchRoot = OperatingSystem.IsWindows()
            ? @"C:\temp\helengine\codegen\test-temp"
            : "/tmp/helengine/codegen/test-temp";
        Directory.CreateDirectory(scratchRoot);
        Environment.SetEnvironmentVariable("TEMP", scratchRoot);
        Environment.SetEnvironmentVariable("TMP", scratchRoot);
    }
}
