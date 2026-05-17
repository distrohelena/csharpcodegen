namespace cs2.cpp.tests;

/// <summary>
/// Verifies runtime support contracts are authored directly in the C++ template sources instead of being repaired after generation.
/// </summary>
public sealed class CPPRuntimeTemplateContractTests {
    /// <summary>
    /// Verifies the native dictionary runtime template declares the managed-style <c>Clear()</c> surface directly.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_native_dictionary_declares_managed_clear_surface_directly() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "runtime",
            "native_dictionary.hpp");

        string source = File.ReadAllText(templatePath);

        Assert.Contains("void Clear()", source, StringComparison.Ordinal);
        Assert.Contains("this->clear();", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the number runtime template declares the finite-check helper surface directly.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_number_declares_finite_helpers_directly() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "system",
            "number.hpp");

        string source = File.ReadAllText(templatePath);

        Assert.Contains("static bool IsNaN(float value)", source, StringComparison.Ordinal);
        Assert.Contains("static bool IsNaN(double value)", source, StringComparison.Ordinal);
        Assert.Contains("static bool IsInfinity(float value)", source, StringComparison.Ordinal);
        Assert.Contains("static bool IsInfinity(double value)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the csharpcodegen repository root from the current test assembly location.
    /// </summary>
    /// <returns>Absolute repository root path.</returns>
    static string ResolveRepositoryRootPath() {
        string currentPath = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(currentPath)) {
            string rootMarkerPath = Path.Combine(currentPath, "cs2.cpp", "cs2.cpp.csproj");
            if (File.Exists(rootMarkerPath)) {
                return currentPath;
            }

            DirectoryInfo parentDirectory = Directory.GetParent(currentPath);
            if (parentDirectory == null) {
                break;
            }

            currentPath = parentDirectory.FullName;
        }

        throw new InvalidOperationException("Could not resolve the csharpcodegen repository root from the current test assembly location.");
    }
}
