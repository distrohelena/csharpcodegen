namespace cs2.cpp;

/// <summary>Encodes managed literals as compiler-independent C++ source while preserving every UTF-16 code unit.</summary>
internal static class CPPUnicodeLiteralUtils {
    /// <summary>Formats one managed character as an explicitly sixteen-bit C++ literal, including surrogate code units.</summary>
    /// <param name="value">UTF-16 code unit stored by the managed character.</param>
    /// <returns>An ASCII-only C++ character literal with no dependency on the compiler source code page.</returns>
    public static string FormatCharacter(char value) {
        return "u'\\x" + ((int)value).ToString("X4", System.Globalization.CultureInfo.InvariantCulture) + "'";
    }

    /// <summary>Formats a string using UTF-16 construction when narrow literals cannot preserve its managed contents.</summary>
    /// <param name="value">Managed text whose embedded nulls and lone surrogates must survive generation.</param>
    /// <param name="stringTypeName">Configured runtime type used for ordinary ASCII string construction.</param>
    /// <returns>A native string expression containing only ASCII source characters.</returns>
    public static string FormatString(string value, string stringTypeName) {
        if (value.Any(character => character > 127 || character == 0 || (char.IsControl(character) && character != '\r' && character != '\n' && character != '\t'))) {
            string units = string.Concat(value.Select(character => "\\x" + ((int)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture)));
            return "he_cpp_string_from_utf16(u\"" + units + "\", " + value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        }
        string escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        return stringTypeName + "(\"" + escaped + "\")";
    }
}
