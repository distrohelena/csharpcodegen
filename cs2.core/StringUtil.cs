namespace cs2.core {
    /// <summary>
    /// Provides small string helpers used by the converter backends.
    /// </summary>
    public static class StringUtil {
        /// <summary>
        /// Converts the first character of a string to lower case.
        /// </summary>
        /// <param name="value">The string to convert.</param>
        /// <returns>The camel-cased string.</returns>
        public static string ToCamelCase(string value) {
            if (string.IsNullOrEmpty(value)) {
                return value;
            }

            if (value.Length == 1) {
                return value.ToLowerInvariant();
            }

            return char.ToLowerInvariant(value[0]) + value.Substring(1);
        }

        /// <summary>
        /// Converts the first character of a string to upper case.
        /// </summary>
        /// <param name="value">The string to convert.</param>
        /// <returns>The capitalized string.</returns>
        public static string CapitalizerFirstLetter(string value) {
            if (string.IsNullOrEmpty(value)) {
                return value;
            }

            if (value.Length == 1) {
                return value.ToUpperInvariant();
            }

            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        /// <summary>
        /// Replaces all occurrences of a substring using ordinal matching.
        /// </summary>
        /// <param name="value">The original string.</param>
        /// <param name="oldValue">Substring to replace.</param>
        /// <param name="newValue">Replacement substring.</param>
        /// <returns>The updated string.</returns>
        public static string Replace(string value, string oldValue, string newValue) {
            return value.Replace(oldValue, newValue, StringComparison.Ordinal);
        }

        /// <summary>
        /// Formats a DECODED string value as a double-quoted source literal whose escapes are valid in
        /// TypeScript, C#, and other C-family targets. Every backslash, double quote, and control
        /// character in the value is escaped, so a value containing quotes (JSON payloads, SQL error
        /// fragments) can never terminate the emitted literal early and corrupt the surrounding code.
        /// </summary>
        /// <param name="value">The decoded string value (the literal token's ValueText, not its source text).</param>
        /// <returns>The quoted, escaped source literal including the surrounding double quotes.</returns>
        public static string FormatDoubleQuotedLiteral(string value) {
            if (value == null) {
                return "\"\"";
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (char character in value) {
                if (character == '\\') {
                    builder.Append("\\\\");
                } else if (character == '"') {
                    builder.Append("\\\"");
                } else if (character == '\n') {
                    builder.Append("\\n");
                } else if (character == '\r') {
                    builder.Append("\\r");
                } else if (character == '\t') {
                    builder.Append("\\t");
                } else if (character == '\0') {
                    builder.Append("\\0");
                } else if (char.IsControl(character) || character == '\u2028' || character == '\u2029') {
                    // U+2028 / U+2029 are legal in strings for modern engines but historically break
                    // JavaScript parsers; escaping them costs nothing and keeps the output portable.
                    builder.Append("\\u").Append(((int)character).ToString("x4"));
                } else {
                    builder.Append(character);
                }
            }

            builder.Append('"');
            return builder.ToString();
        }
    }
}
