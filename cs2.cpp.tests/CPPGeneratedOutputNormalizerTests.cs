namespace cs2.cpp.tests;

/// <summary>
/// Verifies generic post-generation normalization passes that keep emitted runtime support files buildable for constrained native presets.
/// </summary>
public class CPPGeneratedOutputNormalizerTests {
    /// <summary>
    /// Verifies the generated native dictionary helper gains the managed-style <c>Clear()</c> surface expected by emitted component code.
    /// </summary>
    [Fact]
    public void Normalize_WhenNativeDictionaryExists_InsertsClearHelper() {
        string outputPath = CreateOutputRoot();
        string dictionaryPath = Path.Combine(outputPath, "runtime", "native_dictionary.hpp");
        Directory.CreateDirectory(Path.GetDirectoryName(dictionaryPath) ?? throw new InvalidOperationException("Runtime helper directory must resolve."));
        File.WriteAllText(
            dictionaryPath,
            """
#pragma once

template<typename TKey, typename TValue>
class Dictionary {
public:
    bool TryGetValue(const TKey& key, TValue& value) const {
        return false;
    }
};
""");

        CPPGeneratedOutputNormalizer.Normalize(outputPath);

        string normalized = File.ReadAllText(dictionaryPath);
        Assert.Contains("void Clear()", normalized);
        Assert.Contains("this->clear();", normalized);
    }

    /// <summary>
    /// Verifies the generated number helper gains the finite-check APIs required by transpiled primitive static number calls.
    /// </summary>
    [Fact]
    public void Normalize_WhenGeneratedNumberHelperExists_InsertsFiniteCheckHelpers() {
        string outputPath = CreateOutputRoot();
        string numberPath = Path.Combine(outputPath, "system", "number.hpp");
        Directory.CreateDirectory(Path.GetDirectoryName(numberPath) ?? throw new InvalidOperationException("Number helper directory must resolve."));
        File.WriteAllText(
            numberPath,
            """
#pragma once

class Number {
public:
    static bool IsPositiveInfinity(float value) {
        return value > 0.0f;
    }
};
""");

        CPPGeneratedOutputNormalizer.Normalize(outputPath);

        string normalized = File.ReadAllText(numberPath);
        Assert.Contains("static bool IsNaN(float value)", normalized);
        Assert.Contains("static bool IsNaN(double value)", normalized);
        Assert.Contains("static bool IsInfinity(float value)", normalized);
        Assert.Contains("static bool IsInfinity(double value)", normalized);
    }

    /// <summary>
    /// Verifies the generated menu component header receives the missing forward declaration used by templated component searches.
    /// </summary>
    [Fact]
    public void Normalize_WhenMenuComponentHeaderExists_InsertsSelectedDescriptionForwardDeclaration() {
        string outputPath = CreateOutputRoot();
        string menuHeaderPath = Path.Combine(outputPath, "MenuComponent.hpp");
        File.WriteAllText(
            menuHeaderPath,
            """
#pragma once

class MenuItemComponent;

class MenuComponent {
};
""");

        CPPGeneratedOutputNormalizer.Normalize(outputPath);

        string normalized = File.ReadAllText(menuHeaderPath);
        Assert.Contains("class MenuSelectedDescriptionComponent;", normalized);
    }

    /// <summary>
    /// Verifies the generated native engine binary reader coalesces serialized null strings to one empty string instead of returning one null pointer through a <c>std::string</c> surface.
    /// </summary>
    [Fact]
    public void Normalize_WhenEngineBinaryReaderExists_RewritesNullStringBranchToEmptyString() {
        string outputPath = CreateOutputRoot();
        string readerPath = Path.Combine(outputPath, "EngineBinaryReader.cpp");
        File.WriteAllText(
            readerPath,
            """
std::string EngineBinaryReader::ReadString()
{
const int32_t length = this->ReadInt32();
    if (length == -1)
    {
return nullptr;    }
else     if (length < -1)
    {
throw new InvalidOperationException("String length cannot be negative.");
    }
else     if (length == 0)
    {
return String::Empty;    }
}
""");

        CPPGeneratedOutputNormalizer.Normalize(outputPath);

        string normalized = File.ReadAllText(readerPath);
        Assert.DoesNotContain("return nullptr;", normalized, StringComparison.Ordinal);
        Assert.Contains("return String::Empty;", normalized);
    }

    /// <summary>
    /// Creates one isolated output root for direct output-normalizer tests.
    /// </summary>
    /// <returns>Fresh temporary output root.</returns>
    static string CreateOutputRoot() {
        string outputPath = Path.Combine(Path.GetTempPath(), "cs2cpp-output-normalizer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputPath);
        return outputPath;
    }
}
