using System.Text;

namespace cs2.cpp {
    /// <summary>
    /// Prefixes generated type identifiers inside rendered C++ type strings with the global scope operator in one left-to-right pass.
    /// </summary>
    public static class CPPGeneratedTypeNameQualifier {
        /// <summary>
        /// Inserts <c>::</c> before every maximal identifier run that names an emitted type and is not already preceded by a colon.
        /// </summary>
        /// <param name="renderedTypeName">Rendered C++ type string.</param>
        /// <param name="emittedTypeNames">Emitted type names that require global qualification.</param>
        /// <returns>The qualified type string, or the original instance when nothing changed.</returns>
        public static string Qualify(string renderedTypeName, IReadOnlySet<string> emittedTypeNames) {
            if (string.IsNullOrWhiteSpace(renderedTypeName) || emittedTypeNames == null || emittedTypeNames.Count == 0) {
                return renderedTypeName;
            }

            StringBuilder builder = null;
            int copiedUpTo = 0;
            int length = renderedTypeName.Length;
            int index = 0;
            while (index < length) {
                if (!IsIdentifierCharacter(renderedTypeName[index])) {
                    index++;
                    continue;
                }

                int start = index;
                while (index < length && IsIdentifierCharacter(renderedTypeName[index])) {
                    index++;
                }

                if (start > 0 && renderedTypeName[start - 1] == ':') {
                    continue;
                }

                string identifier = renderedTypeName.Substring(start, index - start);
                if (!emittedTypeNames.Contains(identifier)) {
                    continue;
                }

                builder ??= new StringBuilder(length + 16);
                builder.Append(renderedTypeName, copiedUpTo, start - copiedUpTo);
                builder.Append("::");
                copiedUpTo = start;
            }

            if (builder == null) {
                return renderedTypeName;
            }

            builder.Append(renderedTypeName, copiedUpTo, length - copiedUpTo);
            return builder.ToString();
        }

        /// <summary>
        /// Mirrors the regex word-character class for C++ identifiers: letters, digits and underscore.
        /// </summary>
        /// <param name="character">Character to classify.</param>
        /// <returns>True when the character continues an identifier run.</returns>
        static bool IsIdentifierCharacter(char character) {
            return char.IsLetterOrDigit(character) || character == '_';
        }
    }
}
