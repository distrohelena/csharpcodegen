namespace cs2.cpp {
    /// <summary>
    /// Formats caller-owned feature ids into generated C++ identifier forms such as config defines and enum members.
    /// </summary>
    public static class CPPFeatureIdentifierFormatter {
        /// <summary>
        /// Converts one caller-owned feature id into the generated config define suffix used by `HE_CPP_FEATURE_*`.
        /// </summary>
        /// <param name="featureId">Caller-owned feature id to sanitize.</param>
        /// <returns>Uppercase underscore-separated config define suffix.</returns>
        public static string ToConfigDefineSuffix(string featureId) {
            return NormalizeIdentifierParts(featureId, false, true);
        }

        /// <summary>
        /// Converts one caller-owned feature id into the generated C++ enum member name.
        /// </summary>
        /// <param name="featureId">Caller-owned feature id to sanitize.</param>
        /// <returns>Pascal-cased enum member name.</returns>
        public static string ToEnumMemberName(string featureId) {
            return NormalizeIdentifierParts(featureId, true, false);
        }

        static string NormalizeIdentifierParts(string featureId, bool usePascalCase, bool useUpperUnderscore) {
            if (string.IsNullOrWhiteSpace(featureId)) {
                throw new ArgumentException("Feature id must not be empty.", nameof(featureId));
            }

            List<string> parts = new List<string>();
            char[] separators = new[] { '_', '-', '.', ' ' };
            foreach (string part in featureId.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                string sanitizedPart = SanitizePart(part);
                if (!string.IsNullOrWhiteSpace(sanitizedPart)) {
                    parts.Add(sanitizedPart);
                }
            }

            if (parts.Count == 0) {
                throw new InvalidOperationException($"Feature id '{featureId}' does not produce a valid generated identifier.");
            }

            if (useUpperUnderscore) {
                return string.Join("_", parts).ToUpperInvariant();
            }

            if (usePascalCase) {
                return string.Concat(parts.Select(ToPascalCasePart));
            }

            throw new InvalidOperationException("Unsupported feature identifier formatting mode.");
        }

        static string SanitizePart(string part) {
            char[] characters = part.Where(char.IsLetterOrDigit).ToArray();
            return new string(characters);
        }

        static string ToPascalCasePart(string part) {
            if (string.IsNullOrWhiteSpace(part)) {
                return string.Empty;
            }

            if (part.Length == 1) {
                return part.ToUpperInvariant();
            }

            return char.ToUpperInvariant(part[0]) + part[1..];
        }
    }
}
