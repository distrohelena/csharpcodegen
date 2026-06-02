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
    /// Verifies the native dictionary runtime can use generated value-type equality and hash members for struct keys.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_native_dictionary_hashes_generated_value_type_keys() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "runtime",
            "native_dictionary.hpp");

        string source = File.ReadAllText(templatePath);

        Assert.Contains("class NativeDictionaryHash", source, StringComparison.Ordinal);
        Assert.Contains("class NativeDictionaryEqual", source, StringComparison.Ordinal);
        Assert.Contains("value.GetHashCode()", source, StringComparison.Ordinal);
        Assert.Contains("value.Equals(right)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the native list runtime can use generated value-type equality members for Contains and Remove.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_native_list_compares_generated_value_type_items() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "runtime",
            "native_list.hpp");

        string source = File.ReadAllText(templatePath);

        Assert.Contains("class NativeListEqual", source, StringComparison.Ordinal);
        Assert.Contains("value.Equals(right)", source, StringComparison.Ordinal);
        Assert.Contains("std::find_if", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the native event runtime stores and invokes free or static subscribers instead of discarding all event traffic.
    /// </summary>
    [Fact]
    public void RuntimeTemplates_native_event_invokes_static_subscribers() {
        string templatePath = Path.Combine(
            ResolveRepositoryRootPath(),
            "cs2.cpp",
            ".net.cpp",
            "runtime",
            "native_event.hpp");

        string source = File.ReadAllText(templatePath);

        Assert.Contains("std::vector<Subscriber> Subscribers", source, StringComparison.Ordinal);
        Assert.Contains("Event& operator+=(void (*handler)(TArgs...))", source, StringComparison.Ordinal);
        Assert.Contains("std::array<void*, sizeof...(TArgs)> argumentPointers", source, StringComparison.Ordinal);
        Assert.Contains("subscriber.Invoke(argumentPointers.data());", source, StringComparison.Ordinal);
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
