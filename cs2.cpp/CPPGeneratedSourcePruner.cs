namespace cs2.cpp {
    /// <summary>
    /// Removes generated source files that must never survive into runtime-native generated output.
    /// </summary>
    public static class CPPGeneratedSourcePruner {
        /// <summary>
        /// Editor-only attribute type names that are valid for authoring workflows but must not participate in runtime-native builds.
        /// </summary>
        static readonly string[] EditorOnlyGeneratedTypeNames = [
            "EditorPropertyDisplayNameAttribute",
            "EditorPropertyHiddenAttribute",
            "EditorPropertyOrderAttribute",
            "NativeFreeFunctionAttribute",
            "ScenePersistenceIgnoreAttribute"
        ];

        /// <summary>
        /// Generated source extensions emitted for runtime type conversions.
        /// </summary>
        static readonly string[] GeneratedSourceExtensions = [
            ".hpp",
            ".cpp",
            ".tpp"
        ];

        /// <summary>
        /// Removes generated editor-only attribute sources from one generated output root.
        /// </summary>
        /// <param name="outputFolder">Generated output folder that contains the converted native core.</param>
        public static void RemoveEditorOnlyGeneratedSourceFiles(string outputFolder) {
            if (string.IsNullOrWhiteSpace(outputFolder)) {
                throw new ArgumentException("Output folder must not be empty.", nameof(outputFolder));
            }

            for (int typeIndex = 0; typeIndex < EditorOnlyGeneratedTypeNames.Length; typeIndex++) {
                string typeName = EditorOnlyGeneratedTypeNames[typeIndex];
                for (int extensionIndex = 0; extensionIndex < GeneratedSourceExtensions.Length; extensionIndex++) {
                    string generatedPath = Path.Combine(outputFolder, typeName + GeneratedSourceExtensions[extensionIndex]);
                    if (!File.Exists(generatedPath)) {
                        continue;
                    }

                    File.Delete(generatedPath);
                }
            }
        }
    }
}
