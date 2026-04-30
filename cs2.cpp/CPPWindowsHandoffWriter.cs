namespace cs2.cpp {
    /// <summary>
    /// Writes a small handoff contract that lets the Windows host consume a copied generated core folder deterministically.
    /// </summary>
    public static class CPPWindowsHandoffWriter {
        /// <summary>
        /// Gets the generated handoff contract file name.
        /// </summary>
        public const string FileName = "helengine_windows_handoff.cmake";

        /// <summary>
        /// Writes the Windows handoff contract into the generated output folder.
        /// </summary>
        /// <param name="outputFolder">Root output folder for the generated C++ project.</param>
        /// <returns>The emitted handoff contract path.</returns>
        public static string Write(string outputFolder) {
            if (string.IsNullOrWhiteSpace(outputFolder)) {
                throw new ArgumentException("Output folder must not be empty.", nameof(outputFolder));
            }

            Directory.CreateDirectory(outputFolder);

            string filePath = Path.Combine(outputFolder, FileName);
            File.WriteAllText(filePath, BuildFileText());
            return filePath;
        }

        /// <summary>
        /// Builds the CMake handoff contract that describes the generated core layout expected by the Windows host.
        /// </summary>
        /// <returns>The CMake contract source text.</returns>
        static string BuildFileText() {
            return """
set(HELENGINE_GENERATED_CORE_ROOT "${CMAKE_CURRENT_LIST_DIR}")
set(HELENGINE_GENERATED_CONFIG_HEADER "${HELENGINE_GENERATED_CORE_ROOT}/helcpp_config.hpp")
set(HELENGINE_GENERATED_UNITY_SOURCE "${HELENGINE_GENERATED_CORE_ROOT}/helengine_core_unity.cpp")
set(HELENGINE_GENERATED_FEATURE_MANIFEST_HEADER "${HELENGINE_GENERATED_CORE_ROOT}/runtime/feature_manifest.hpp")
""";
        }
    }
}
