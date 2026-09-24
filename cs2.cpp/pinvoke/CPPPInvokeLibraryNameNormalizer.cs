using System.Text;

namespace cs2.cpp;

/// <summary>
/// Converts a DllImport module name into the C++ namespace and link-library identifier used for its forwarders.
/// </summary>
public static class CPPPInvokeLibraryNameNormalizer {
    /// <summary>
    /// File extensions removed from module names because the platform adds them when resolving libraries.
    /// </summary>
    static readonly string[] KnownExtensions = { ".dll", ".so", ".dylib", ".lib" };

    /// <summary>
    /// Normalizes a module name such as <c>C:\libs\User32.dll</c> into <c>user32</c>.
    /// </summary>
    /// <param name="moduleName">Module name exactly as written in the DllImport attribute.</param>
    /// <returns>A lowercase C++ identifier naming the library.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="moduleName"/> is blank, or contains no characters usable in a C++ identifier.
    /// </exception>
    public static string Normalize(string moduleName) {
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

        StringBuilder builder = new StringBuilder(fileName.Length + 1);
        foreach (char character in fileName.ToLowerInvariant()) {
            bool isIdentifierCharacter = (character >= 'a' && character <= 'z') || (character >= '0' && character <= '9') || character == '_';
            builder.Append(isIdentifierCharacter ? character : '_');
        }

        if (builder.Length == 0) {
            throw new ArgumentException($"DllImport module name '{moduleName}' does not contain a library name.", nameof(moduleName));
        }

        if (char.IsDigit(builder[0])) {
            builder.Insert(0, '_');
        }

        return builder.ToString();
    }
}
