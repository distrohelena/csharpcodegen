namespace cs2.cpp.tests.TestHelpers;

/// <summary>
/// Routes test-owned temporary files into an explicit directory or one fixed external scratch root.
/// </summary>
internal static class CPPTestEnvironment {
    /// <summary>
    /// Configures the process temporary-directory contract before xUnit creates any test fixtures.
    /// </summary>
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Initialize() {
        string scratchRoot = Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_TEMP_ROOT")
            ?? (OperatingSystem.IsWindows()
                ? @"C:\temp\helengine\windows\codegen-test-temp"
                : OperatingSystem.IsMacOS()
                    ? "/tmp/helengine/macos/codegen-test-temp"
                    : "/tmp/helengine/linux/codegen-test-temp");
        Directory.CreateDirectory(scratchRoot);
        Environment.SetEnvironmentVariable("TEMP", scratchRoot);
        Environment.SetEnvironmentVariable("TMP", scratchRoot);
    }
}
