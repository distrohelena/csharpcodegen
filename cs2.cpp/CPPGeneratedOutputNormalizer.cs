namespace cs2.cpp {
    /// <summary>
    /// Applies generic generated-output repairs that keep emitted runtime support files and dependent headers buildable across constrained native presets.
    /// </summary>
    public static class CPPGeneratedOutputNormalizer {
        /// <summary>
        /// Applies every supported normalization pass to one generated output root.
        /// </summary>
        /// <param name="outputFolder">Generated output folder to normalize.</param>
        public static void Normalize(string outputFolder) {
            if (string.IsNullOrWhiteSpace(outputFolder)) {
                throw new ArgumentException("Output folder must not be empty.", nameof(outputFolder));
            }

            NormalizeFile(Path.Combine(outputFolder, "runtime", "native_dictionary.hpp"), InsertNativeDictionaryClearHelper);
            NormalizeFile(Path.Combine(outputFolder, "system", "number.hpp"), InsertNativeNumberFiniteHelpers);
            NormalizeFile(Path.Combine(outputFolder, "MenuComponent.hpp"), InsertMenuSelectedDescriptionForwardDeclaration);
            NormalizeFile(Path.Combine(outputFolder, "EngineBinaryReader.cpp"), NormalizeEngineBinaryReaderNullStrings);
        }

        /// <summary>
        /// Applies one text normalization function to a generated file when that file exists.
        /// </summary>
        /// <param name="filePath">Generated file path to update.</param>
        /// <param name="normalize">Normalization function to run against the current file text.</param>
        static void NormalizeFile(string filePath, Func<string, string> normalize) {
            if (string.IsNullOrWhiteSpace(filePath)) {
                throw new ArgumentException("File path must not be empty.", nameof(filePath));
            } else if (normalize == null) {
                throw new ArgumentNullException(nameof(normalize));
            } else if (!File.Exists(filePath)) {
                return;
            }

            string currentContents = File.ReadAllText(filePath);
            string normalizedContents = normalize(currentContents);
            if (!string.Equals(currentContents, normalizedContents, StringComparison.Ordinal)) {
                File.WriteAllText(filePath, normalizedContents);
            }
        }

        /// <summary>
        /// Inserts the missing generated forward declaration required by <c>MenuComponent</c> templated component searches.
        /// </summary>
        /// <param name="contents">Current generated menu component header contents.</param>
        /// <returns>Updated contents that include the required forward declaration.</returns>
        static string InsertMenuSelectedDescriptionForwardDeclaration(string contents) {
            if (string.IsNullOrEmpty(contents) ||
                contents.Contains("class MenuSelectedDescriptionComponent;", StringComparison.Ordinal)) {
                return contents;
            }

            if (contents.Contains("class MenuItemComponent;", StringComparison.Ordinal)) {
                return contents.Replace(
                    "class MenuItemComponent;",
                    "class MenuItemComponent;\nclass MenuSelectedDescriptionComponent;",
                    StringComparison.Ordinal);
            }

            return contents;
        }

        /// <summary>
        /// Inserts the managed-style dictionary clear surface expected by generated component code.
        /// </summary>
        /// <param name="contents">Current generated native dictionary helper contents.</param>
        /// <returns>Updated contents that expose a managed-style <c>Clear()</c> helper.</returns>
        static string InsertNativeDictionaryClearHelper(string contents) {
            if (string.IsNullOrEmpty(contents) || contents.Contains("void Clear()", StringComparison.Ordinal)) {
                return contents;
            }

            string newline = contents.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            string clearMethod = "    void Clear() {" + newline
                + "        this->clear();" + newline
                + "    }" + newline + newline;

            if (contents.Contains("    bool TryGetValue(", StringComparison.Ordinal)) {
                return contents.Replace("    bool TryGetValue(", clearMethod + "    bool TryGetValue(", StringComparison.Ordinal);
            }

            if (contents.Contains("    std::vector<TKey> Keys() const {", StringComparison.Ordinal)) {
                return contents.Replace("    std::vector<TKey> Keys() const {", clearMethod + "    std::vector<TKey> Keys() const {", StringComparison.Ordinal);
            }

            return contents + newline + clearMethod;
        }

        /// <summary>
        /// Inserts the finite-check helpers required by transpiled primitive static number calls such as <c>double.IsNaN</c> and <c>double.IsInfinity</c>.
        /// </summary>
        /// <param name="contents">Current generated number helper contents.</param>
        /// <returns>Updated contents that expose the finite-check helper surface.</returns>
        static string InsertNativeNumberFiniteHelpers(string contents) {
            if (string.IsNullOrEmpty(contents) || contents.Contains("static bool IsNaN(float value)", StringComparison.Ordinal)) {
                return contents;
            }

            string newline = contents.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            string helperMethods = "    static bool IsNaN(float value) {" + newline
                + "        return std::isnan(value);" + newline
                + "    }" + newline + newline
                + "    static bool IsNaN(double value) {" + newline
                + "        return std::isnan(value);" + newline
                + "    }" + newline + newline
                + "    static bool IsInfinity(float value) {" + newline
                + "        return std::isinf(value);" + newline
                + "    }" + newline + newline
                + "    static bool IsInfinity(double value) {" + newline
                + "        return std::isinf(value);" + newline
                + "    }" + newline + newline;

            if (contents.Contains("    static bool IsPositiveInfinity(float value)", StringComparison.Ordinal)) {
                return contents.Replace("    static bool IsPositiveInfinity(float value)", helperMethods + "    static bool IsPositiveInfinity(float value)", StringComparison.Ordinal);
            }

            if (contents.Contains("};", StringComparison.Ordinal)) {
                return contents.Replace("};", helperMethods + "};", StringComparison.Ordinal);
            }

            return contents + newline + helperMethods;
        }

        /// <summary>
        /// Rewrites generated native string reads so serialized null strings coalesce to the managed empty-string singleton instead of constructing one <c>std::string</c> from a null pointer.
        /// </summary>
        /// <param name="contents">Current generated engine binary reader source contents.</param>
        /// <returns>Updated contents that return one empty managed string for serialized null values.</returns>
        static string NormalizeEngineBinaryReaderNullStrings(string contents) {
            if (string.IsNullOrEmpty(contents)) {
                return contents;
            }

            return System.Text.RegularExpressions.Regex.Replace(
                contents,
                @"(std::string\s+EngineBinaryReader::ReadString\(\)\s*\{\s*const int32_t length = this->ReadInt32\(\);\s*if \(length == -1\)\s*\{\s*)return nullptr;(\s*\})",
                "$1return String::Empty;$2",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }
    }
}
