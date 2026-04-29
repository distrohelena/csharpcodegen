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
    }
}
