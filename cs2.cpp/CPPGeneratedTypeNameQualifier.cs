using System.Text;
using System.Text.RegularExpressions;

namespace cs2.cpp {
    /// <summary>
    /// Prefixes generated type identifiers inside rendered C++ type strings with the global scope operator, using a single
    /// left-to-right pass when every emitted name is a pure identifier and falling back to the original per-name regex
    /// replacement otherwise, so behavior stays byte-identical to the loop this class replaces.
    /// </summary>
    public static class CPPGeneratedTypeNameQualifier {
        /// <summary>
        /// Inserts <c>::</c> before every emitted type name found in a rendered C++ type string that is not already
        /// preceded by a colon, matching the reference regex loop exactly.
        /// </summary>
        /// <param name="renderedTypeName">Rendered C++ type string.</param>
        /// <param name="index">Emitted type name snapshot that requires global qualification.</param>
        /// <returns>The qualified type string, or the original instance when nothing changed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="index"/> is null.</exception>
        public static string Qualify(string renderedTypeName, CPPEmittedTypeNameIndex index) {
            if (index == null) {
                throw new ArgumentNullException(nameof(index));
            }

            if (string.IsNullOrWhiteSpace(renderedTypeName) || index.EmittedTypeNames.Count == 0) {
                return renderedTypeName;
            }

            if (!index.AllEmittedTypeNamesAreIdentifiers) {
                return QualifyWithRegexFallback(renderedTypeName, index.OrderedEmittedTypeNames);
            }

            return QualifyIdentifierRun(renderedTypeName, index.EmittedTypeNames);
        }

        /// <summary>
        /// Replays the original per-name regex replacement loop verbatim, for the rare case where an emitted type name
        /// contains characters outside the identifier character set and so cannot be matched by a maximal identifier scan.
        /// </summary>
        /// <param name="renderedTypeName">Rendered C++ type string.</param>
        /// <param name="orderedEmittedTypeNames">Emitted type names in first-occurrence program order.</param>
        /// <returns>The qualified type string produced by applying each name's replacement in order.</returns>
        static string QualifyWithRegexFallback(string renderedTypeName, IReadOnlyList<string> orderedEmittedTypeNames) {
            string qualifiedTypeName = renderedTypeName;
            for (int nameIndex = 0; nameIndex < orderedEmittedTypeNames.Count; nameIndex++) {
                string generatedTypeName = orderedEmittedTypeNames[nameIndex];
                qualifiedTypeName = Regex.Replace(
                    qualifiedTypeName,
                    $@"(?<!:)\b{Regex.Escape(generatedTypeName)}\b",
                    $"::{generatedTypeName}");
            }

            return qualifiedTypeName;
        }

        /// <summary>
        /// Inserts <c>::</c> before every maximal identifier run that names an emitted type and is not already preceded
        /// by a colon, in a single left-to-right pass. Valid only when every emitted name is a pure identifier.
        /// </summary>
        /// <param name="renderedTypeName">Rendered C++ type string.</param>
        /// <param name="emittedTypeNames">Emitted type names that require global qualification.</param>
        /// <returns>The qualified type string, or the original instance when nothing changed.</returns>
        static string QualifyIdentifierRun(string renderedTypeName, IReadOnlySet<string> emittedTypeNames) {
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
        /// Mirrors the regex word-character class for C++ identifiers: letters, digits and underscore. Shared with
        /// <see cref="CPPEmittedTypeNameIndex"/> so the index can classify emitted names using the same rule the
        /// single-pass scan relies on.
        /// </summary>
        /// <param name="character">Character to classify.</param>
        /// <returns>True when the character continues an identifier run.</returns>
        internal static bool IsIdentifierCharacter(char character) {
            return char.IsLetterOrDigit(character) || character == '_';
        }
    }
}
