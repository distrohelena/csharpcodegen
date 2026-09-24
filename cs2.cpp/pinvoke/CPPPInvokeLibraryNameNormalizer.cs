using System.Text;

namespace cs2.cpp;

/// <summary>
/// Converts a DllImport module name into the C++ namespace its forwarders are declared in, and into the link-library
/// name the generated project links against.
/// </summary>
public static class CPPPInvokeLibraryNameNormalizer {
    /// <summary>
    /// File extensions removed from module names because the platform adds them when resolving libraries.
    /// </summary>
    static readonly string[] KnownExtensions = { ".dll", ".so", ".dylib", ".lib" };

    /// <summary>
    /// Normalizes a module name such as <c>C:\libs\User32.dll</c> into the C++ namespace identifier <c>user32</c>.
    /// </summary>
    /// <param name="moduleName">Module name exactly as written in the DllImport attribute.</param>
    /// <returns>A lowercase C++ identifier naming the library.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="moduleName"/> is blank, or contains no characters usable in a C++ identifier.
    /// </exception>
    public static string Normalize(string moduleName) {
        string fileName = GetLinkLibraryName(moduleName);

        StringBuilder builder = new StringBuilder(fileName.Length + 1);
        foreach (char character in fileName.ToLowerInvariant()) {
            bool isIdentifierCharacter = (character >= 'a' && character <= 'z') || (character >= '0' && character <= '9') || character == '_';
            builder.Append(isIdentifierCharacter ? character : '_');
        }

        if (char.IsDigit(builder[0])) {
            builder.Insert(0, '_');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Gets the link-library name of a module: its base name with the directory and a known library extension
    /// (<c>.dll</c>, <c>.so</c>, <c>.dylib</c>, <c>.lib</c>) removed, keeping the original case and characters, so
    /// <c>C:\libs\gevo-native.dll</c> links as <c>gevo-native</c>.
    /// </summary>
    /// <param name="moduleName">Module name exactly as written in the DllImport attribute.</param>
    /// <returns>The library base name the generated project links against.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="moduleName"/> is blank, or names only a directory or an extension.
    /// </exception>
    public static string GetLinkLibraryName(string moduleName) {
        if (string.IsNullOrWhiteSpace(moduleName)) {
            throw new ArgumentException("A DllImport module name is required.", nameof(moduleName));
        }

        string fileName = moduleName.Trim().Replace('\\', '/');
        int slashIndex = fileName.LastIndexOf('/');
        if (slashIndex >= 0) {
            fileName = fileName.Substring(slashIndex + 1);
        }

        foreach (string extension in KnownExtensions) {
            if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) {
                fileName = fileName.Substring(0, fileName.Length - extension.Length);
                break;
            }
        }

        if (fileName.Length == 0) {
            throw new ArgumentException($"DllImport module name '{moduleName}' does not contain a library name.", nameof(moduleName));
        }

        return fileName;
    }
}
