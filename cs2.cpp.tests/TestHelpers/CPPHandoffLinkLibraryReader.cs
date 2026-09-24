namespace cs2.cpp.tests.TestHelpers;

/// <summary>
/// Reads the native link libraries a generated output publishes through the <c>CPP_GENERATED_NATIVE_LINK_LIBRARIES</c>
/// variable of its Windows handoff contract, so test programs link the same libraries the Windows host would.
/// </summary>
public static class CPPHandoffLinkLibraryReader {
    /// <summary>
    /// Prefix of the handoff line that assigns the native link-library list.
    /// </summary>
    const string VariablePrefix = "set(CPP_GENERATED_NATIVE_LINK_LIBRARIES \"";

    /// <summary>
    /// Suffix that closes the quoted link-library list and the CMake <c>set</c> call.
    /// </summary>
    const string VariableSuffix = "\")";

    /// <summary>
    /// Reads the semicolon-separated link-library names from a handoff contract file.
    /// </summary>
    /// <param name="handoffPath">Path of the generated <c>generated_windows_handoff.cmake</c> file.</param>
    /// <returns>The library base names (for example <c>kernel32</c>) in declaration order; empty when none are published.</returns>
    /// <exception cref="ArgumentException"><paramref name="handoffPath"/> is null or whitespace.</exception>
    /// <exception cref="FileNotFoundException">The handoff file does not exist.</exception>
    /// <exception cref="InvalidOperationException">The handoff file does not declare the link-library variable exactly once.</exception>
    public static IReadOnlyList<string> Read(string handoffPath) {
        if (string.IsNullOrWhiteSpace(handoffPath)) {
            throw new ArgumentException("A handoff contract path is required.", nameof(handoffPath));
        }
        if (!File.Exists(handoffPath)) {
            throw new FileNotFoundException("The generated Windows handoff contract was not found.", handoffPath);
        }

        List<string> variableLines = File.ReadAllLines(handoffPath)
            .Where(line => line.StartsWith(VariablePrefix, StringComparison.Ordinal))
            .ToList();
        if (variableLines.Count != 1) {
            throw new InvalidOperationException(
                "The handoff contract must declare CPP_GENERATED_NATIVE_LINK_LIBRARIES exactly once: " + handoffPath);
        }

        string line = variableLines[0];
        if (!line.EndsWith(VariableSuffix, StringComparison.Ordinal)) {
            throw new InvalidOperationException("The CPP_GENERATED_NATIVE_LINK_LIBRARIES line is malformed: " + line);
        }

        string value = line.Substring(VariablePrefix.Length, line.Length - VariablePrefix.Length - VariableSuffix.Length);
        return value.Split(';', StringSplitOptions.RemoveEmptyEntries);
    }
}
